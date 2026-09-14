using OpenAiBench.Core.Domain;

namespace OpenAiBench.Core.Ports;

public interface IWorkspaceStore
{
    Task<IReadOnlyList<Experiment>> LoadAllExperimentsAsync(CancellationToken cancellationToken = default);
    Task SaveExperimentAsync(Experiment experiment, CancellationToken cancellationToken = default);
    Task DeleteExperimentAsync(Guid experimentId, CancellationToken cancellationToken = default);

    Task<Experiment> CloneExperimentAsync(Experiment source, string newName, CancellationToken cancellationToken = default);

    Task SaveRunAsync(Guid experimentId, ExecutionRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExecutionRun>> LoadRunsAsync(Guid experimentId, CancellationToken cancellationToken = default);
    Task ClearRunsAsync(Guid experimentId, CancellationToken cancellationToken = default);

    Task<AttachedFileRef> AddFileAsync(Guid experimentId, string sourceFilePath, CancellationToken cancellationToken = default);
    Task RemoveFileAsync(Guid experimentId, string fileId, CancellationToken cancellationToken = default);
    string GetFileAbsolutePath(Guid experimentId, AttachedFileRef file);

    Task<WorkspaceState> LoadWorkspaceStateAsync(CancellationToken cancellationToken = default);
    Task SaveWorkspaceStateAsync(WorkspaceState state, CancellationToken cancellationToken = default);
}
