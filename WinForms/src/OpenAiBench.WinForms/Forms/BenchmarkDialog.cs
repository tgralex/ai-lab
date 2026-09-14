using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Execution;
using OpenAiBench.Core.Statistics;

namespace OpenAiBench.WinForms.Forms;

/// <summary>Runs an experiment N times sequentially and reports percentile/stddev statistics over that batch.</summary>
public sealed class BenchmarkDialog : Form
{
    private readonly Experiment _experiment;
    private readonly IExperimentRunner _runner;

    private readonly NumericUpDown _runsInput = new() { Minimum = 1, Maximum = 1000, Value = 5, Width = 80 };
    private readonly Button _runButton = new() { Text = "Run Benchmark", AutoSize = true };
    private readonly Label _progressLabel = new() { AutoSize = true };
    private readonly DataGridView _resultsGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };

    public BenchmarkDialog(Experiment experiment, IExperimentRunner runner)
    {
        _experiment = experiment;
        _runner = runner;

        Text = $"Benchmark: {experiment.Name}";
        Width = 700;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;

        var topPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        topPanel.Controls.Add(new Label { Text = "Runs:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        topPanel.Controls.Add(_runsInput);
        topPanel.Controls.Add(_runButton);
        topPanel.Controls.Add(_progressLabel);
        _progressLabel.Padding = new Padding(12, 6, 0, 0);

        _resultsGrid.Columns.Add("Metric", "Metric");
        _resultsGrid.Columns.Add("Count", "Count");
        _resultsGrid.Columns.Add("Min", "Min");
        _resultsGrid.Columns.Add("Max", "Max");
        _resultsGrid.Columns.Add("Mean", "Mean");
        _resultsGrid.Columns.Add("Median", "Median");
        _resultsGrid.Columns.Add("P90", "P90");
        _resultsGrid.Columns.Add("P95", "P95");
        _resultsGrid.Columns.Add("StdDev", "StdDev");

        _runButton.Click += OnRunClicked;

        Controls.Add(_resultsGrid);
        Controls.Add(topPanel);
    }

    private async void OnRunClicked(object? sender, EventArgs e)
    {
        _runButton.Enabled = false;
        var count = (int)_runsInput.Value;
        var batch = new List<ExecutionRun>(count);

        for (var i = 0; i < count; i++)
        {
            _progressLabel.Text = $"Running {i + 1} of {count}…";
            var run = await _runner.ExecuteAsync(_experiment);
            batch.Add(run);
        }

        _progressLabel.Text = $"Done: {count} run(s).";
        DisplayResults(batch);
        _runButton.Enabled = true;
    }

    private void DisplayResults(IReadOnlyList<ExecutionRun> batch)
    {
        _resultsGrid.Rows.Clear();
        var result = BenchmarkAggregator.ComputeBenchmark(batch);

        AddRow("Duration (ms)", result.DurationMs);
        AddRow("TTFT (ms)", result.TimeToFirstTokenMs);
        AddRow("Generation Duration (ms)", result.GenerationDurationMs);
        AddRow("Input Tokens", result.InputTokens);
        AddRow("Output Tokens", result.OutputTokens);
        AddRow("Total Tokens", result.TotalTokens);
        AddRow("Cost (USD)", result.CostUsd);

        _resultsGrid.Rows.Add("Success / Failure / Canceled", $"{result.SuccessCount} / {result.FailureCount} / {result.CanceledCount}");
    }

    private void AddRow(string metric, DescriptiveStatsSummary summary)
    {
        _resultsGrid.Rows.Add(
            metric,
            summary.Count,
            summary.Min.ToString("F2"),
            summary.Max.ToString("F2"),
            summary.Mean.ToString("F2"),
            summary.Median.ToString("F2"),
            summary.P90.ToString("F2"),
            summary.P95.ToString("F2"),
            summary.StdDev.ToString("F2"));
    }
}
