using System.Text.Json;
using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Execution;
using OpenAiBench.Core.Export;
using OpenAiBench.Core.Statistics;
using OpenAiBench.WinForms.Controls;
using OpenAiBench.WinForms.Forms;

namespace OpenAiBench.WinForms;

public sealed partial class MainForm
{
    private sealed record WindowLayout(int X, int Y, int Width, int Height, bool Maximized);

    private async Task LoadWorkspaceAsync()
    {
        var state = await _workspaceStore.LoadWorkspaceStateAsync();
        ApplyWindowLayout(state.WindowLayoutJson);

        var experiments = await _workspaceStore.LoadAllExperimentsAsync();
        var ordered = state.ExperimentOrder.Count > 0
            ? experiments.OrderBy(e => state.ExperimentOrder.IndexOf(e.Id) is var i && i >= 0 ? i : int.MaxValue)
            : experiments.OrderBy(e => e.Order);

        foreach (var experiment in ordered)
        {
            AddExperimentTab(experiment);
        }
    }

    private void ApplyWindowLayout(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            var layout = JsonSerializer.Deserialize<WindowLayout>(json);
            if (layout is null)
            {
                return;
            }

            StartPosition = FormStartPosition.Manual;
            Location = new Point(layout.X, layout.Y);
            Size = new Size(layout.Width, layout.Height);
            if (layout.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }
        }
        catch (JsonException)
        {
            // Ignore a corrupted layout blob — defaults already applied.
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_canClose)
        {
            return;
        }

        e.Cancel = true;
        await SaveAllAsync();
        _canClose = true;
        Close();
    }

    private async Task SaveAllAsync()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var layout = new WindowLayout(bounds.X, bounds.Y, bounds.Width, bounds.Height, WindowState == FormWindowState.Maximized);

        var order = _experimentTabs.TabPages.Cast<TabPage>()
            .Select(p => p.Controls.OfType<ExperimentTabControl>().First().Experiment.Id)
            .ToList();

        await _workspaceStore.SaveWorkspaceStateAsync(new WorkspaceState
        {
            ExperimentOrder = order,
            WindowLayoutJson = JsonSerializer.Serialize(layout)
        });

        foreach (var control in _controlsByExperimentId.Values)
        {
            await _workspaceStore.SaveExperimentAsync(control.Experiment);
        }
    }

    private void AddExperimentTab(Experiment experiment)
    {
        var control = new ExperimentTabControl(experiment, _runner, _workspaceStore, _modelProvider);
        control.Mutated += async (_, _) => await _workspaceStore.SaveExperimentAsync(experiment);

        var page = new TabPage(experiment.Name) { Tag = experiment.Id };
        control.Mutated += (_, _) => page.Text = experiment.Name;
        page.Controls.Add(control);

        _experimentTabs.TabPages.Add(page);
        _controlsByExperimentId[experiment.Id] = control;
        _experimentTabs.SelectedTab = page;
    }

    private async Task OnNewExperimentAsync()
    {
        var experiment = new Experiment { Order = _experimentTabs.TabPages.Count };
        await _workspaceStore.SaveExperimentAsync(experiment);
        AddExperimentTab(experiment);
    }

    private async Task OnCloneExperimentAsync()
    {
        var active = ActiveControl_;
        if (active is null)
        {
            return;
        }

        var cloned = await _workspaceStore.CloneExperimentAsync(active.Experiment, active.Experiment.Name + " (copy)");
        AddExperimentTab(cloned);
    }

    private async Task OnDeleteExperimentAsync()
    {
        var active = ActiveControl_;
        var page = _experimentTabs.SelectedTab;
        if (active is null || page is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Delete experiment '{active.Experiment.Name}'? This removes all its files and run history.", "Delete Experiment", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        await _workspaceStore.DeleteExperimentAsync(active.Experiment.Id);
        _controlsByExperimentId.Remove(active.Experiment.Id);
        _experimentTabs.TabPages.Remove(page);
    }

    private async Task OnExecuteActiveAsync()
    {
        var active = ActiveControl_;
        if (active is not null)
        {
            await active.ExecuteAsync();
        }
    }

    private void OnOpenBenchmarkDialog()
    {
        var active = ActiveControl_;
        if (active is null)
        {
            return;
        }

        using var dialog = new BenchmarkDialog(active.Experiment, _runner);
        dialog.ShowDialog(this);
        active.RefreshFromHistory();
    }

    private async Task OnExecuteAllAsync()
    {
        var experiments = _controlsByExperimentId.Values.Select(c => c.Experiment).ToList();
        if (experiments.Count == 0)
        {
            return;
        }

        var mode = _executionModeCombo.SelectedItem?.ToString() == nameof(ExecutionMode.Parallel) ? ExecutionMode.Parallel : ExecutionMode.Sequential;
        var maxConcurrency = (int)_maxConcurrencyInput.Value;

        _executeAllCts = new CancellationTokenSource();
        _cancelAllButton.Enabled = true;

        var progress = new Progress<ExecuteAllProgress>(p =>
        {
            if (_controlsByExperimentId.TryGetValue(p.ExperimentId, out var control))
            {
                control.SetExternalStatus(p.Status);
            }
        });

        try
        {
            var result = await _executeAllCoordinator.ExecuteAllAsync(experiments, mode, maxConcurrency, progress, _executeAllCts.Token);

            foreach (var (experiment, run) in experiments.Zip(result.Runs))
            {
                if (_controlsByExperimentId.TryGetValue(experiment.Id, out var control))
                {
                    control.RefreshAfterExternalRun(run);
                }
            }

            ShowGlobalSummary(result.Summary);
        }
        finally
        {
            _executeAllCts.Dispose();
            _executeAllCts = null;
            _cancelAllButton.Enabled = false;
        }
    }

    private void ShowGlobalSummary(GlobalExecutionSummary summary)
    {
        using var form = new Form { Text = "Execute All — Global Summary", Width = 520, Height = 480, StartPosition = FormStartPosition.CenterParent };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        grid.Columns.Add("Metric", "Metric");
        grid.Columns.Add("Value", "Value");

        grid.Rows.Add("Wall-Clock Duration (ms)", summary.WallClockDuration.TotalMilliseconds.ToString("F0"));
        grid.Rows.Add("Sum Of Individual Durations (ms)", summary.SumOfIndividualDurations.TotalMilliseconds.ToString("F0"));
        grid.Rows.Add("Request Count", summary.RequestCount);
        grid.Rows.Add("Success / Failure / Canceled", $"{summary.SuccessCount} / {summary.FailureCount} / {summary.CanceledCount}");
        grid.Rows.Add("Input Tokens", summary.InputTokens);
        grid.Rows.Add("Cached Input Tokens", summary.CachedInputTokens);
        grid.Rows.Add("Uncached Input Tokens", summary.UncachedInputTokens);
        grid.Rows.Add("Output Tokens", summary.OutputTokens);
        grid.Rows.Add("Reasoning Tokens", summary.ReasoningTokens);
        grid.Rows.Add("Total Tokens", summary.TotalTokens);
        grid.Rows.Add("Total Estimated Cost", summary.TotalEstimatedCost.ToString("C4"));
        grid.Rows.Add("Latency Mean / Median (ms)", $"{summary.LatencyMs.Mean:F0} / {summary.LatencyMs.Median:F0}");
        grid.Rows.Add("Latency P90 / P95 (ms)", $"{summary.LatencyMs.P90:F0} / {summary.LatencyMs.P95:F0}");
        grid.Rows.Add("Average TTFT (ms)", summary.AverageTimeToFirstTokenMs?.ToString("F0") ?? "n/a");
        grid.Rows.Add("Average Generation Duration (ms)", summary.AverageGenerationDurationMs?.ToString("F0") ?? "n/a");
        grid.Rows.Add("Average Output Tokens/sec", summary.AverageOutputTokensPerSecond?.ToString("F1") ?? "n/a");

        form.Controls.Add(grid);
        form.ShowDialog(this);
    }

    private void OnOpenComparisonGrid()
    {
        var experiments = _controlsByExperimentId.Values.Select(c => c.Experiment).ToList();
        using var form = new ComparisonGridForm(experiments);
        form.ShowDialog(this);
    }

    private void OnExport(bool isCsv)
    {
        var rows = new List<BenchmarkExportRow>();
        foreach (var control in _controlsByExperimentId.Values)
        {
            var experiment = control.Experiment;
            foreach (var run in experiment.Runs)
            {
                rows.Add(new BenchmarkExportRow
                {
                    Experiment = experiment.Name,
                    Run = run.Id.ToString(),
                    StartedAt = run.StartedAt,
                    Model = run.ActualModel ?? run.RequestedModel,
                    Reasoning = experiment.Request.ReasoningEffort,
                    TotalMs = run.TotalDuration.TotalMilliseconds,
                    TtftMs = run.TimeToFirstToken?.TotalMilliseconds,
                    GenerationMs = run.GenerationDuration?.TotalMilliseconds,
                    InputTokens = run.Usage.InputTokens,
                    CachedTokens = run.Usage.CachedInputTokens,
                    OutputTokens = run.Usage.OutputTokens,
                    ReasoningTokens = run.Usage.ReasoningTokens,
                    TokensPerSecond = run.OutputTokensPerSecond,
                    EstimatedCost = run.EstimatedCost,
                    Status = run.Status.ToString()
                });
            }
        }

        using var dialog = new SaveFileDialog
        {
            Filter = isCsv ? "CSV file (*.csv)|*.csv" : "JSON file (*.json)|*.json",
            FileName = isCsv ? "benchmark-export.csv" : "benchmark-export.json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var content = isCsv ? CsvExporter.Export(rows) : JsonExporter.Export(rows);
        File.WriteAllText(dialog.FileName, content);
    }
}
