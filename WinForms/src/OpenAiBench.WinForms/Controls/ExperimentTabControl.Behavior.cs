using System.Text.Json;
using Markdig;
using OpenAiBench.Core.Diffing;
using OpenAiBench.Core.Domain;
using OpenAiBench.WinForms.Forms;

namespace OpenAiBench.WinForms.Controls;

public sealed partial class ExperimentTabControl
{
    private static readonly string[] ReasoningEfforts = { "minimal", "low", "medium", "high" };

    private void WireEvents()
    {
        _executeButton.Click += async (_, _) => await ExecuteOnceAsync();
        _cancelButton.Click += (_, _) => _currentCts?.Cancel();

        _nameBox.TextChanged += (_, _) => { if (!_loading) { Experiment.Name = _nameBox.Text; NotifyMutated(); } };
        _tagsBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            Experiment.Tags = _tagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            NotifyMutated();
        };
        _notesBox.TextChanged += (_, _) => { if (!_loading) { Experiment.Notes = _notesBox.Text; NotifyMutated(); } };
        _systemPromptBox.TextChanged += (_, _) => { if (!_loading) { Experiment.Request.SystemPrompt = _systemPromptBox.Text; NotifyMutated(); } };
        _promptCacheKeyBox.TextChanged += (_, _) => { if (!_loading) { Experiment.Request.PromptCacheKey = string.IsNullOrWhiteSpace(_promptCacheKeyBox.Text) ? null : _promptCacheKeyBox.Text; NotifyMutated(); } };
        _responseSchemaBox.TextChanged += (_, _) => { if (!_loading) { Experiment.Request.ResponseSchema = string.IsNullOrWhiteSpace(_responseSchemaBox.Text) ? null : _responseSchemaBox.Text; NotifyMutated(); } };
        _streamCheckBox.CheckedChanged += (_, _) => { if (!_loading) { Experiment.Request.Stream = _streamCheckBox.Checked; NotifyMutated(); } };
        _maxOutputTokensBox.ValueChanged += (_, _) => { if (!_loading) { Experiment.Request.MaxOutputTokens = _maxOutputTokensBox.Value == 0 ? null : (int)_maxOutputTokensBox.Value; NotifyMutated(); } };
        _reasoningCombo.SelectedIndexChanged += (_, _) => { if (!_loading) { Experiment.Request.ReasoningEffort = _reasoningCombo.SelectedItem as string; NotifyMutated(); } };

        _modelCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _modelCombo.SelectedItem is not ModelComboItem item)
            {
                return;
            }

            Experiment.Request.Model = item.ModelInfo.Id;
            ApplyModelCapabilities(item.ModelInfo);
            NotifyMutated();
        };

        _streamThrottleTimer.Tick += (_, _) => FlushStreamingBuffer();
        _elapsedTimer.Tick += (_, _) => _elapsedLabel.Text = _executionStopwatch.Elapsed.ToString(@"mm\:ss\.f");
    }

    private void NotifyMutated() => Mutated?.Invoke(this, EventArgs.Empty);

    private void LoadFromExperiment()
    {
        _loading = true;

        _nameBox.Text = Experiment.Name;
        _tagsBox.Text = string.Join(", ", Experiment.Tags);
        _notesBox.Text = Experiment.Notes;
        _systemPromptBox.Text = Experiment.Request.SystemPrompt;
        _promptCacheKeyBox.Text = Experiment.Request.PromptCacheKey ?? string.Empty;
        _responseSchemaBox.Text = Experiment.Request.ResponseSchema ?? string.Empty;
        _streamCheckBox.Checked = Experiment.Request.Stream;
        _maxOutputTokensBox.Value = Experiment.Request.MaxOutputTokens ?? 0;

        _modelCombo.Items.Clear();
        foreach (var model in _modelProvider.GetAll())
        {
            _modelCombo.Items.Add(new ModelComboItem(model));
        }

        var currentModel = _modelProvider.Get(string.IsNullOrEmpty(Experiment.Request.Model) ? _modelProvider.GetAll().FirstOrDefault()?.Id ?? string.Empty : Experiment.Request.Model);
        Experiment.Request.Model = currentModel.Id;
        var selected = _modelCombo.Items.Cast<ModelComboItem>().FirstOrDefault(i => i.ModelInfo.Id == currentModel.Id);
        _modelCombo.SelectedItem = selected;

        _reasoningCombo.Items.Clear();
        _reasoningCombo.Items.AddRange(ReasoningEfforts);
        _reasoningCombo.SelectedItem = Experiment.Request.ReasoningEffort ?? "medium";

        ApplyModelCapabilities(currentModel);

        _cachedContextEditor.Initialize(Experiment, Experiment.Request.CachedContext, _workspaceStore, () => { _variablesEditor.RefreshFileOptions(); NotifyMutated(); });
        _userContextEditor.Initialize(Experiment, Experiment.Request.UserContext, _workspaceStore, () => { _variablesEditor.RefreshFileOptions(); NotifyMutated(); });
        _variablesEditor.Initialize(Experiment, Experiment.Request, NotifyMutated);

        _loading = false;
    }

    private void ApplyModelCapabilities(ModelInfo model)
    {
        _reasoningLabel.Visible = model.SupportsReasoningEffort;
        _reasoningCombo.Visible = model.SupportsReasoningEffort;
        _schemaLabel.Visible = model.SupportsStructuredOutput;
        _responseSchemaBox.Visible = model.SupportsStructuredOutput;
        _cacheKeyLabel.Visible = model.SupportsPromptCacheKey;
        _promptCacheKeyBox.Visible = model.SupportsPromptCacheKey;
        _streamCheckBox.Enabled = model.SupportsStreaming;

        if (model.MaxOutputTokensLimit is { } limit)
        {
            _maxOutputTokensBox.Maximum = limit;
        }
    }

    private sealed record ModelComboItem(ModelInfo ModelInfo)
    {
        public override string ToString() => ModelInfo.Recommended ? $"★ {ModelInfo.DisplayName}" : ModelInfo.DisplayName;
    }

    public Task ExecuteAsync() => ExecuteOnceAsync();

    public void SetExternalStatus(ExecutionStatus status) => UpdateStatus(status);

    public void RefreshAfterExternalRun(ExecutionRun run)
    {
        DisplayRun(run);
        RefreshHistoryGrid();
    }

    public void RefreshFromHistory() => RefreshHistoryGrid();

    private async Task ExecuteOnceAsync()
    {
        if (_currentCts is not null)
        {
            return;
        }

        _executeButton.Enabled = false;
        _cancelButton.Enabled = true;
        _streamingBuffer.Clear();
        _plainTextView.Text = string.Empty;
        UpdateStatus(ExecutionStatus.Running);

        _currentCts = new CancellationTokenSource();
        var progress = new Progress<StreamingUpdate>(OnStreamingUpdate);
        _streamThrottleTimer.Start();

        try
        {
            var run = await _runner.ExecuteAsync(Experiment, progress, _currentCts.Token);
            DisplayRun(run);
            RefreshHistoryGrid();
            RunCompleted?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _streamThrottleTimer.Stop();
            FlushStreamingBuffer();
            _currentCts.Dispose();
            _currentCts = null;
            _executeButton.Enabled = true;
            _cancelButton.Enabled = false;
        }
    }

    private void OnStreamingUpdate(StreamingUpdate update)
    {
        UpdateStatus(update.Status);
        if (!string.IsNullOrEmpty(update.DeltaText))
        {
            _streamingBuffer.Append(update.DeltaText);
        }
    }

    private void FlushStreamingBuffer()
    {
        if (_streamingBuffer.Length == 0)
        {
            return;
        }

        _plainTextView.Text = _streamingBuffer.ToString();
        _plainTextView.SelectionStart = _plainTextView.Text.Length;
        _plainTextView.ScrollToCaret();
    }

    private void UpdateStatus(ExecutionStatus status)
    {
        _statusLabel.Text = status.ToString();
        _statusLabel.ForeColor = status switch
        {
            ExecutionStatus.Completed => Color.DarkGreen,
            ExecutionStatus.Failed => Color.DarkRed,
            ExecutionStatus.Canceled => Color.DarkOrange,
            ExecutionStatus.Streaming or ExecutionStatus.Running => Color.DarkBlue,
            _ => SystemColors.ControlText
        };

        if (status is ExecutionStatus.Running or ExecutionStatus.Streaming)
        {
            if (!_executionStopwatch.IsRunning)
            {
                _executionStopwatch.Restart();
                _elapsedTimer.Start();
            }
        }
        else
        {
            _elapsedTimer.Stop();
            _executionStopwatch.Stop();
            if (_executionStopwatch.Elapsed > TimeSpan.Zero)
            {
                _elapsedLabel.Text = _executionStopwatch.Elapsed.ToString(@"mm\:ss\.f");
            }
        }
    }

    private void DisplayRun(ExecutionRun run)
    {
        UpdateStatus(run.Status);
        PopulateStatsGrid(run);
        PopulateOutcome(run);
    }

    private void PopulateStatsGrid(ExecutionRun run)
    {
        _statsGrid.Rows.Clear();
        void Add(string metric, string? value) => _statsGrid.Rows.Add(metric, value ?? string.Empty);

        Add("Status", run.Status.ToString());
        Add("Requested Model", run.RequestedModel);
        Add("Actual Model", run.ActualModel);
        Add("Response Id", run.ResponseId);
        Add("Finish Reason", run.FinishReason);
        Add("HTTP Status", run.HttpStatus?.ToString());
        Add("Retry Count", run.RetryCount.ToString());
        Add("Started At", run.StartedAt.ToLocalTime().ToString("G"));
        Add("Finished At", run.FinishedAt.ToLocalTime().ToString("G"));
        Add("Total Duration (ms)", run.TotalDuration.TotalMilliseconds.ToString("F0"));
        Add("Time To First Response Event (ms)", run.TimeToFirstResponseEvent?.TotalMilliseconds.ToString("F0"));
        Add("Time To First Token / TTFT (ms)", run.TimeToFirstToken?.TotalMilliseconds.ToString("F0"));
        Add("Generation Duration (ms)", run.GenerationDuration?.TotalMilliseconds.ToString("F0"));
        Add("Request Preparation Duration (ms)", run.RequestPreparationDuration?.TotalMilliseconds.ToString("F0"));
        Add("Input Tokens", run.Usage.InputTokens.ToString());
        Add("Cached Input Tokens", run.Usage.CachedInputTokens.ToString());
        Add("Uncached Input Tokens", run.Usage.UncachedInputTokens.ToString());
        Add("Output Tokens", run.Usage.OutputTokens.ToString());
        Add("Reasoning Tokens", run.Usage.ReasoningTokens.ToString());
        Add("Total Tokens", run.Usage.TotalTokens.ToString());
        Add("Cache Hit %", run.Usage.CacheHitPercentage.ToString("F1"));
        Add("Output Tokens / sec", run.OutputTokensPerSecond?.ToString("F1"));
        Add("Total Tokens / sec", run.TotalTokensPerSecond?.ToString("F1"));
        Add("Estimated Input Cost", run.EstimatedInputCost?.ToString("C4"));
        Add("Estimated Cached Input Cost", run.EstimatedCachedInputCost?.ToString("C4"));
        Add("Estimated Output Cost", run.EstimatedOutputCost?.ToString("C4"));
        Add("Estimated Total Cost", run.EstimatedCost?.ToString("C4"));
        Add("Request Byte Size", run.RequestByteSize.ToString());
        Add("Response Byte Size", run.ResponseByteSize.ToString());
        Add("Failed After Streaming Began", run.FailedAfterStreamingBegan.ToString());
        Add("Error", run.Error);
        Add("Exception Type", run.ExceptionType);
        Add("API Error Body", run.ApiErrorBody);
    }

    private async void PopulateOutcome(ExecutionRun run)
    {
        _plainTextView.Text = run.Output;
        _rawJsonView.Text = run.RawResponse;

        await RenderMarkdownAsync(run.Output);
        PopulateJsonTree(run.Output);
    }

    private async Task RenderMarkdownAsync(string markdown)
    {
        try
        {
            if (!_webViewReady)
            {
                await _markdownView.EnsureCoreWebView2Async();
                _webViewReady = true;
            }

            var html = Markdown.ToHtml(markdown ?? string.Empty);
            var document = $"<html><head><meta charset='utf-8'><style>body{{font-family:Segoe UI,sans-serif;font-size:13px;padding:8px;}} pre{{background:#f4f4f4;padding:8px;overflow:auto;}} code{{font-family:Consolas,monospace;}}</style></head><body>{html}</body></html>";
            _markdownView.NavigateToString(document);
        }
        catch
        {
            // WebView2 runtime unavailable — plain text / raw JSON tabs still work.
        }
    }

    private void PopulateJsonTree(string output)
    {
        _jsonTreeView.Nodes.Clear();
        _schemaErrorLabel.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = new TreeNode("root");
            BuildJsonTreeNode(root, document.RootElement);
            _jsonTreeView.Nodes.Add(root);
            root.Expand();
        }
        catch (JsonException ex)
        {
            if (!string.IsNullOrEmpty(Experiment.Request.ResponseSchema))
            {
                _schemaErrorLabel.Text = $"Output is not valid JSON despite a structured-output schema being set: {ex.Message}";
            }
        }
    }

    private static void BuildJsonTreeNode(TreeNode parent, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var child = new TreeNode(property.Name);
                    BuildJsonTreeNode(child, property.Value);
                    parent.Nodes.Add(child);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var child = new TreeNode($"[{index}]");
                    BuildJsonTreeNode(child, item);
                    parent.Nodes.Add(child);
                    index++;
                }
                break;
            default:
                parent.Text += $": {element}";
                break;
        }
    }

    private void FindInActiveOutcomeView(string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return;
        }

        var textBox = _outcomeTabs.SelectedTab?.Controls.OfType<TextBox>().FirstOrDefault()
            ?? (_outcomeTabs.SelectedIndex == 1 ? _plainTextView : _outcomeTabs.SelectedIndex == 2 ? _rawJsonView : null);
        if (textBox is null)
        {
            return;
        }

        var startIndex = textBox.SelectionStart + textBox.SelectionLength;
        var index = textBox.Text.IndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            index = textBox.Text.IndexOf(searchText, 0, StringComparison.OrdinalIgnoreCase);
        }

        if (index >= 0)
        {
            textBox.Focus();
            textBox.Select(index, searchText.Length);
            textBox.ScrollToCaret();
        }
    }

    private void OnExportOutputClicked(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog { Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*", FileName = $"{Experiment.Name}-output.txt" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, _plainTextView.Text);
        }
    }

    private void RefreshHistoryGrid()
    {
        _historyGrid.Rows.Clear();
        foreach (var run in Experiment.Runs.OrderByDescending(r => r.StartedAt))
        {
            var rowIndex = _historyGrid.Rows.Add(
                run.StartedAt.ToLocalTime().ToString("G"),
                run.Status.ToString(),
                run.TotalDuration.TotalMilliseconds.ToString("F0"),
                run.TimeToFirstToken?.TotalMilliseconds.ToString("F0") ?? string.Empty,
                run.Usage.InputTokens.ToString(),
                run.Usage.OutputTokens.ToString(),
                run.EstimatedCost?.ToString("C4") ?? string.Empty);
            _historyGrid.Rows[rowIndex].Tag = run;
        }
    }

    private void OnViewSelectedRunClicked(object? sender, EventArgs e)
    {
        if (_historyGrid.SelectedRows.Count == 0 || _historyGrid.SelectedRows[0].Tag is not ExecutionRun run)
        {
            return;
        }

        DisplayRun(run);
    }

    private void OnCompareSelectedRunsClicked(object? sender, EventArgs e)
    {
        var runs = _historyGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as ExecutionRun).Where(r => r is not null).Cast<ExecutionRun>().ToList();
        if (runs.Count != 2)
        {
            MessageBox.Show(this, "Select exactly two runs to compare.", "Compare Runs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DiffForm.ShowDiff(this, $"Run {runs[0].StartedAt.ToLocalTime():G}", runs[0].Output, $"Run {runs[1].StartedAt.ToLocalTime():G}", runs[1].Output);
    }

    private async void OnClearHistoryClicked(object? sender, EventArgs e)
    {
        if (MessageBox.Show(this, "Clear all run history for this experiment?", "Clear History", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        await _workspaceStore.ClearRunsAsync(Experiment.Id);
        Experiment.Runs.Clear();
        RefreshHistoryGrid();
        _statsGrid.Rows.Clear();
        _plainTextView.Text = string.Empty;
        _rawJsonView.Text = string.Empty;
        _jsonTreeView.Nodes.Clear();
    }
}
