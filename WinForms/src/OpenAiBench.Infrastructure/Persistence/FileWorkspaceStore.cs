using System.Text.Json;
using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Ports;

namespace OpenAiBench.Infrastructure.Persistence;

/// <summary>
/// Workspace/
///   workspace.json
///   Experiments/&lt;id&gt;/experiment.json
///   Experiments/&lt;id&gt;/files/&lt;fileId&gt;_&lt;originalFileName&gt;
///   Experiments/&lt;id&gt;/runs/&lt;runId&gt;.json
///
/// Run history is discovered by scanning the runs/ folder, not by trusting experiment.json's RunIds
/// list (which is informational only) — this avoids a read-modify-write race between saving a run and
/// saving the experiment. This store never mutates the <see cref="Experiment"/> instances passed to it;
/// callers own in-memory state.
/// </summary>
public sealed class FileWorkspaceStore : IWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _root;

    public FileWorkspaceStore(string workspaceRootPath)
    {
        _root = Path.GetFullPath(workspaceRootPath);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(ExperimentsRoot);
    }

    private string ExperimentsRoot => Path.Combine(_root, "Experiments");
    private string WorkspaceStateFilePath => Path.Combine(_root, "workspace.json");

    private string GetExperimentDirectory(Guid experimentId) => Path.Combine(ExperimentsRoot, experimentId.ToString());
    private string GetExperimentFilePath(Guid experimentId) => Path.Combine(GetExperimentDirectory(experimentId), "experiment.json");
    private string GetFilesDirectory(Guid experimentId) => Path.Combine(GetExperimentDirectory(experimentId), "files");
    private string GetRunsDirectory(Guid experimentId) => Path.Combine(GetExperimentDirectory(experimentId), "runs");
    private string GetRunFilePath(Guid experimentId, Guid runId) => Path.Combine(GetRunsDirectory(experimentId), $"{runId}.json");

    public async Task<IReadOnlyList<Experiment>> LoadAllExperimentsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Experiment>();
        if (!Directory.Exists(ExperimentsRoot))
        {
            return results;
        }

        foreach (var directory in Directory.GetDirectories(ExperimentsRoot))
        {
            var experimentJsonPath = Path.Combine(directory, "experiment.json");
            if (!File.Exists(experimentJsonPath))
            {
                continue;
            }

            var json = await File.ReadAllTextAsync(experimentJsonPath, cancellationToken).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<ExperimentFileDto>(json, JsonOptions);
            if (dto is null)
            {
                continue;
            }

            var experiment = PersistenceMapper.FromDto(dto);
            var runs = await LoadRunsAsync(experiment.Id, cancellationToken).ConfigureAwait(false);
            experiment.Runs = runs.ToList();
            results.Add(experiment);
        }

        return results.OrderBy(e => e.Order).ToList();
    }

    public async Task SaveExperimentAsync(Experiment experiment, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(GetExperimentDirectory(experiment.Id));
        var dto = PersistenceMapper.ToDto(experiment);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await AtomicFileWriter.WriteAllTextAsync(GetExperimentFilePath(experiment.Id), json, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteExperimentAsync(Guid experimentId, CancellationToken cancellationToken = default)
    {
        var directory = GetExperimentDirectory(experimentId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public async Task<Experiment> CloneExperimentAsync(Experiment source, string newName, CancellationToken cancellationToken = default)
    {
        var cloned = source.Clone(newName);
        var idMap = new Dictionary<string, string>();
        var newFiles = new List<AttachedFileRef>();

        foreach (var file in source.Files)
        {
            var sourcePath = GetFileAbsolutePath(source.Id, file);
            var newFileId = Guid.NewGuid().ToString("N");
            var relativePath = Path.Combine("files", $"{newFileId}_{file.OriginalFileName}");
            var destinationPath = Path.Combine(GetExperimentDirectory(cloned.Id), relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);

            idMap[file.Id] = newFileId;
            newFiles.Add(new AttachedFileRef
            {
                Id = newFileId,
                OriginalFileName = file.OriginalFileName,
                StoredRelativePath = relativePath,
                SizeBytes = file.SizeBytes,
                ContentType = file.ContentType,
                Sha256 = file.Sha256,
                AddedAt = file.AddedAt
            });
        }

        cloned.Files = newFiles;
        RemapFileIds(cloned.Request.CachedContext, idMap);
        RemapFileIds(cloned.Request.UserContext, idMap);
        cloned.Request.Variables = cloned.Request.Variables
            .Select(v => v.Kind == VariableBindingKind.File && v.FileId is not null && idMap.TryGetValue(v.FileId, out var mapped)
                ? new VariableBinding { Name = v.Name, Kind = v.Kind, TextValue = v.TextValue, FileId = mapped }
                : v)
            .ToList();

        await SaveExperimentAsync(cloned, cancellationToken).ConfigureAwait(false);
        return cloned;
    }

    private static void RemapFileIds(ContentSet set, Dictionary<string, string> idMap) =>
        set.FileIds = set.FileIds.Select(id => idMap.GetValueOrDefault(id, id)).ToList();

    public async Task SaveRunAsync(Guid experimentId, ExecutionRun run, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(GetRunsDirectory(experimentId));
        var dto = PersistenceMapper.ToDto(run);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await AtomicFileWriter.WriteAllTextAsync(GetRunFilePath(experimentId, run.Id), json, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutionRun>> LoadRunsAsync(Guid experimentId, CancellationToken cancellationToken = default)
    {
        var runsDirectory = GetRunsDirectory(experimentId);
        if (!Directory.Exists(runsDirectory))
        {
            return Array.Empty<ExecutionRun>();
        }

        var runs = new List<ExecutionRun>();
        foreach (var file in Directory.GetFiles(runsDirectory, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<RunFileDto>(json, JsonOptions);
            if (dto is not null)
            {
                runs.Add(PersistenceMapper.FromDto(dto));
            }
        }

        return runs.OrderBy(r => r.StartedAt).ToList();
    }

    public Task ClearRunsAsync(Guid experimentId, CancellationToken cancellationToken = default)
    {
        var runsDirectory = GetRunsDirectory(experimentId);
        if (Directory.Exists(runsDirectory))
        {
            foreach (var file in Directory.GetFiles(runsDirectory, "*.json"))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    public async Task<AttachedFileRef> AddFileAsync(Guid experimentId, string sourceFilePath, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid().ToString("N");
        var originalFileName = Path.GetFileName(sourceFilePath);
        var relativePath = Path.Combine("files", $"{fileId}_{originalFileName}");
        var destinationPath = Path.Combine(GetExperimentDirectory(experimentId), relativePath);

        Directory.CreateDirectory(GetFilesDirectory(experimentId));
        File.Copy(sourceFilePath, destinationPath, overwrite: false);

        var hash = await FileHasher.ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false);
        var sizeBytes = new FileInfo(destinationPath).Length;

        return new AttachedFileRef
        {
            Id = fileId,
            OriginalFileName = originalFileName,
            StoredRelativePath = relativePath,
            SizeBytes = sizeBytes,
            ContentType = ContentTypeGuesser.Guess(originalFileName),
            Sha256 = hash,
            AddedAt = DateTimeOffset.UtcNow
        };
    }

    public Task RemoveFileAsync(Guid experimentId, string fileId, CancellationToken cancellationToken = default)
    {
        var filesDirectory = GetFilesDirectory(experimentId);
        if (Directory.Exists(filesDirectory))
        {
            foreach (var file in Directory.GetFiles(filesDirectory, $"{fileId}_*"))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    public string GetFileAbsolutePath(Guid experimentId, AttachedFileRef file) =>
        Path.Combine(GetExperimentDirectory(experimentId), file.StoredRelativePath);

    public async Task<WorkspaceState> LoadWorkspaceStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(WorkspaceStateFilePath))
        {
            return new WorkspaceState();
        }

        var json = await File.ReadAllTextAsync(WorkspaceStateFilePath, cancellationToken).ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<WorkspaceStateDto>(json, JsonOptions);
        return dto is null
            ? new WorkspaceState()
            : new WorkspaceState { ExperimentOrder = dto.ExperimentOrder, WindowLayoutJson = dto.WindowLayoutJson };
    }

    public async Task SaveWorkspaceStateAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        var dto = new WorkspaceStateDto { ExperimentOrder = state.ExperimentOrder, WindowLayoutJson = state.WindowLayoutJson };
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await AtomicFileWriter.WriteAllTextAsync(WorkspaceStateFilePath, json, cancellationToken).ConfigureAwait(false);
    }
}
