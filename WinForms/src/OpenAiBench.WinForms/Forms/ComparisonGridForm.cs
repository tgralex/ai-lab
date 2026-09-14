using System.Data;
using OpenAiBench.Core.Domain;

namespace OpenAiBench.WinForms.Forms;

/// <summary>Sortable cross-experiment comparison grid — one row per experiment, its latest run's stats.</summary>
public sealed class ComparisonGridForm : Form
{
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };

    public ComparisonGridForm(IReadOnlyList<Experiment> experiments)
    {
        Text = "Comparison Grid";
        Width = 1200;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;

        var table = new DataTable();
        table.Columns.Add("Experiment", typeof(string));
        table.Columns.Add("Model", typeof(string));
        table.Columns.Add("Reasoning", typeof(string));
        table.Columns.Add("Total Time (ms)", typeof(double));
        table.Columns.Add("TTFT (ms)", typeof(double));
        table.Columns.Add("Generation Time (ms)", typeof(double));
        table.Columns.Add("Input Tokens", typeof(int));
        table.Columns.Add("Cached Tokens", typeof(int));
        table.Columns.Add("Cache %", typeof(double));
        table.Columns.Add("Output Tokens", typeof(int));
        table.Columns.Add("Reasoning Tokens", typeof(int));
        table.Columns.Add("Output Tokens/sec", typeof(double));
        table.Columns.Add("Estimated Cost", typeof(decimal));
        table.Columns.Add("Status", typeof(string));

        foreach (var experiment in experiments)
        {
            var run = experiment.Runs.LastOrDefault();
            table.Rows.Add(
                experiment.Name,
                experiment.Request.Model,
                experiment.Request.ReasoningEffort ?? string.Empty,
                run?.TotalDuration.TotalMilliseconds ?? 0,
                run?.TimeToFirstToken?.TotalMilliseconds ?? 0,
                run?.GenerationDuration?.TotalMilliseconds ?? 0,
                run?.Usage.InputTokens ?? 0,
                run?.Usage.CachedInputTokens ?? 0,
                run?.Usage.CacheHitPercentage ?? 0,
                run?.Usage.OutputTokens ?? 0,
                run?.Usage.ReasoningTokens ?? 0,
                run?.OutputTokensPerSecond ?? 0,
                run?.EstimatedCost ?? 0m,
                run?.Status.ToString() ?? "No runs");
        }

        _grid.DataSource = table;
        Controls.Add(_grid);
    }
}
