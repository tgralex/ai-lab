using System.Diagnostics;
using System.Text;
using Microsoft.Web.WebView2.WinForms;
using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Execution;
using OpenAiBench.Core.Ports;
using OpenAiBench.WinForms.Icons;

namespace OpenAiBench.WinForms.Controls;

/// <summary>One experiment's full UI: Request / Stats / Outcome / History nested tabs.</summary>
public sealed partial class ExperimentTabControl : UserControl
{
    public Experiment Experiment { get; }

    private readonly IExperimentRunner _runner;
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IModelCapabilityProvider _modelProvider;

    public event EventHandler? Mutated;
    public event EventHandler? RunCompleted;

    // Top status bar
    private readonly Label _statusLabel = new() { AutoSize = true, Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold) };
    private readonly Label _elapsedLabel = new() { AutoSize = true, Font = new Font(FontFamily.GenericMonospace, 9) };
    private readonly Button _executeButton = new() { Text = "Execute", AutoSize = true, Image = IconFactory.Get(IconKind.Execute), TextImageRelation = TextImageRelation.ImageBeforeText };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, Enabled = false, Image = IconFactory.Get(IconKind.Cancel), TextImageRelation = TextImageRelation.ImageBeforeText };
    private readonly Stopwatch _executionStopwatch = new();
    private readonly System.Windows.Forms.Timer _elapsedTimer = new() { Interval = 200 };

    // Request tab
    private readonly TextBox _nameBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _tagsBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _notesBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox _modelCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _reasoningCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox _streamCheckBox = new() { Text = "Stream", AutoSize = true, Checked = true };
    private readonly NumericUpDown _maxOutputTokensBox = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1_000_000, Increment = 128 };
    private readonly TextBox _promptCacheKeyBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _responseSchemaBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 9) };
    private readonly TextBox _systemPromptBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly ContentSetEditor _cachedContextEditor = new();
    private readonly ContentSetEditor _userContextEditor = new();
    private readonly VariablesEditor _variablesEditor = new();
    private readonly Label _reasoningLabel = new() { Text = "Reasoning effort:", AutoSize = true };
    private readonly Label _schemaLabel = new() { Text = "Structured output JSON schema (optional):", AutoSize = true };
    private readonly Label _cacheKeyLabel = new() { Text = "Prompt cache key (optional):", AutoSize = true };

    // Stats tab
    private readonly DataGridView _statsGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersVisible = true
    };

    // Outcome tab
    private readonly TabControl _outcomeTabs = new() { Dock = DockStyle.Fill };
    private readonly WebView2 _markdownView = new() { Dock = DockStyle.Fill };
    private readonly TextBox _plainTextView = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true, WordWrap = true };
    private readonly TextBox _rawJsonView = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true, Font = new Font(FontFamily.GenericMonospace, 9) };
    private readonly TreeView _jsonTreeView = new() { Dock = DockStyle.Fill };
    private readonly TextBox _searchBox = new() { Width = 200 };
    private readonly Label _schemaErrorLabel = new() { AutoSize = true, ForeColor = Color.DarkRed, Dock = DockStyle.Top };

    // History tab
    private readonly DataGridView _historyGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        MultiSelect = true
    };

    private CancellationTokenSource? _currentCts;
    private readonly StringBuilder _streamingBuffer = new();
    private readonly System.Windows.Forms.Timer _streamThrottleTimer = new() { Interval = 100 };
    private bool _webViewReady;
    private bool _loading;

    public ExperimentTabControl(Experiment experiment, IExperimentRunner runner, IWorkspaceStore workspaceStore, IModelCapabilityProvider modelProvider)
    {
        Experiment = experiment;
        _runner = runner;
        _workspaceStore = workspaceStore;
        _modelProvider = modelProvider;
        Dock = DockStyle.Fill;

        BuildLayout();
        WireEvents();
        LoadFromExperiment();

        var lastRun = experiment.Runs.LastOrDefault();
        if (lastRun is not null)
        {
            DisplayRun(lastRun);
        }

        RefreshHistoryGrid();
        UpdateStatus(lastRun?.Status ?? ExecutionStatus.Idle);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        topBar.Controls.Add(_executeButton);
        topBar.Controls.Add(_cancelButton);
        topBar.Controls.Add(new Label { Text = "  Status:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        _statusLabel.Padding = new Padding(4, 6, 0, 0);
        topBar.Controls.Add(_statusLabel);
        _elapsedLabel.Padding = new Padding(12, 6, 0, 0);
        topBar.Controls.Add(_elapsedLabel);
        root.Controls.Add(topBar, 0, 0);

        var innerTabs = new TabControl { Dock = DockStyle.Fill };
        innerTabs.TabPages.Add(BuildRequestTab());
        innerTabs.TabPages.Add(BuildStatsTab());
        innerTabs.TabPages.Add(BuildOutcomeTab());
        innerTabs.TabPages.Add(BuildHistoryTab());
        root.Controls.Add(innerTabs, 0, 1);

        Controls.Add(root);
    }

    private TabPage BuildRequestTab()
    {
        var page = new TabPage("Request");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control, int height = 28)
        {
            var row = layout.RowCount;
            layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(4, 6, 4, 0) }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        AddRow("Name:", _nameBox);
        AddRow("Tags (comma-separated):", _tagsBox);
        AddRow("Notes:", _notesBox, 50);
        AddRow("Model:", _modelCombo);

        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(_reasoningLabel, 0, layout.RowCount - 1);
        layout.Controls.Add(_reasoningCombo, 1, layout.RowCount - 1);

        AddRow("Max output tokens (0 = unset):", _maxOutputTokensBox);
        AddRow("", _streamCheckBox);

        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(_cacheKeyLabel, 0, layout.RowCount - 1);
        layout.Controls.Add(_promptCacheKeyBox, 1, layout.RowCount - 1);

        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.Controls.Add(_schemaLabel, 0, layout.RowCount - 1);
        layout.Controls.Add(_responseSchemaBox, 1, layout.RowCount - 1);

        AddRow("System prompt:", _systemPromptBox, 90);

        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
        layout.Controls.Add(new Label { Text = "Cached Context:", AutoSize = true }, 0, layout.RowCount - 1);
        layout.Controls.Add(_cachedContextEditor, 1, layout.RowCount - 1);

        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
        layout.Controls.Add(new Label { Text = "User Context:", AutoSize = true }, 0, layout.RowCount - 1);
        layout.Controls.Add(_userContextEditor, 1, layout.RowCount - 1);

        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        layout.Controls.Add(new Label { Text = "Variables:", AutoSize = true }, 0, layout.RowCount - 1);
        layout.Controls.Add(_variablesEditor, 1, layout.RowCount - 1);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildStatsTab()
    {
        var page = new TabPage("Statistics");
        _statsGrid.Columns.Add("Metric", "Metric");
        _statsGrid.Columns.Add("Value", "Value");
        page.Controls.Add(_statsGrid);
        return page;
    }

    private TabPage BuildOutcomeTab()
    {
        var page = new TabPage("Outcome");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var copyButton = new Button { Text = "Copy Output", AutoSize = true, Image = IconFactory.Get(IconKind.Copy), TextImageRelation = TextImageRelation.ImageBeforeText };
        copyButton.Click += (_, _) => { if (!string.IsNullOrEmpty(_plainTextView.Text)) Clipboard.SetText(_plainTextView.Text); };
        var exportButton = new Button { Text = "Export Output…", AutoSize = true, Image = IconFactory.Get(IconKind.Export), TextImageRelation = TextImageRelation.ImageBeforeText };
        exportButton.Click += OnExportOutputClicked;
        var findButton = new Button { Text = "Find", AutoSize = true, Image = IconFactory.Get(IconKind.Search), TextImageRelation = TextImageRelation.ImageBeforeText };
        findButton.Click += (_, _) => FindInActiveOutcomeView(_searchBox.Text);

        toolbar.Controls.Add(copyButton);
        toolbar.Controls.Add(exportButton);
        toolbar.Controls.Add(new Label { Text = "Search:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        toolbar.Controls.Add(_searchBox);
        toolbar.Controls.Add(findButton);
        root.Controls.Add(toolbar, 0, 0);

        var markdownPage = new TabPage("Markdown") { };
        markdownPage.Controls.Add(_markdownView);

        var plainPage = new TabPage("Plain Text");
        plainPage.Controls.Add(_plainTextView);

        var rawJsonPage = new TabPage("Raw JSON");
        rawJsonPage.Controls.Add(_rawJsonView);

        var treePage = new TabPage("Structured / JSON Tree");
        var treeContainer = new Panel { Dock = DockStyle.Fill };
        treeContainer.Controls.Add(_jsonTreeView);
        treeContainer.Controls.Add(_schemaErrorLabel);
        treePage.Controls.Add(treeContainer);

        _outcomeTabs.TabPages.Add(markdownPage);
        _outcomeTabs.TabPages.Add(plainPage);
        _outcomeTabs.TabPages.Add(rawJsonPage);
        _outcomeTabs.TabPages.Add(treePage);

        root.Controls.Add(_outcomeTabs, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildHistoryTab()
    {
        var page = new TabPage("History");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _historyGrid.Columns.Add("StartedAt", "Started At");
        _historyGrid.Columns.Add("Status", "Status");
        _historyGrid.Columns.Add("TotalMs", "Total (ms)");
        _historyGrid.Columns.Add("TtftMs", "TTFT (ms)");
        _historyGrid.Columns.Add("InputTokens", "Input Tok");
        _historyGrid.Columns.Add("OutputTokens", "Output Tok");
        _historyGrid.Columns.Add("Cost", "Est. Cost");

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var viewButton = new Button { Text = "View Selected", AutoSize = true, Image = IconFactory.Get(IconKind.View), TextImageRelation = TextImageRelation.ImageBeforeText };
        viewButton.Click += OnViewSelectedRunClicked;
        var compareButton = new Button { Text = "Compare Selected (2)", AutoSize = true, Image = IconFactory.Get(IconKind.Compare), TextImageRelation = TextImageRelation.ImageBeforeText };
        compareButton.Click += OnCompareSelectedRunsClicked;
        var clearButton = new Button { Text = "Clear History", AutoSize = true, Image = IconFactory.Get(IconKind.ClearHistory), TextImageRelation = TextImageRelation.ImageBeforeText };
        clearButton.Click += OnClearHistoryClicked;
        buttonPanel.Controls.Add(viewButton);
        buttonPanel.Controls.Add(compareButton);
        buttonPanel.Controls.Add(clearButton);

        root.Controls.Add(_historyGrid, 0, 0);
        root.Controls.Add(buttonPanel, 0, 1);
        page.Controls.Add(root);
        return page;
    }
}
