using System.Text.Json;
using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Execution;
using OpenAiBench.Core.Export;
using OpenAiBench.Core.Ports;
using OpenAiBench.WinForms.Controls;
using OpenAiBench.WinForms.Forms;
using OpenAiBench.WinForms.Icons;

namespace OpenAiBench.WinForms;

public sealed partial class MainForm : Form
{
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IExperimentRunner _runner;
    private readonly ExecuteAllCoordinator _executeAllCoordinator;
    private readonly IModelCapabilityProvider _modelProvider;

    private readonly TabControl _experimentTabs = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<Guid, ExperimentTabControl> _controlsByExperimentId = new();

    private readonly NumericUpDown _maxConcurrencyInput = new() { Minimum = 1, Maximum = 64, Value = 4, Width = 60 };
    private readonly ComboBox _executionModeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Button _cancelAllButton = new() { Text = "Cancel All", AutoSize = true, Enabled = false };

    private CancellationTokenSource? _executeAllCts;
    private bool _canClose;

    public MainForm(IWorkspaceStore workspaceStore, IExperimentRunner runner, ExecuteAllCoordinator executeAllCoordinator, IModelCapabilityProvider modelProvider)
    {
        _workspaceStore = workspaceStore;
        _runner = runner;
        _executeAllCoordinator = executeAllCoordinator;
        _modelProvider = modelProvider;

        Text = "OpenAiBench — OpenAI Experiment & Benchmark Harness";
        Icon = Icon.FromHandle(((Bitmap)IconFactory.Get(IconKind.Benchmark)).GetHicon());
        Width = 1100;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        BuildToolbar(out var toolbar);
        Controls.Add(_experimentTabs);
        Controls.Add(toolbar);

        Load += async (_, _) => await LoadWorkspaceAsync();
        FormClosing += OnFormClosing;
    }

    private void BuildToolbar(out ToolStrip toolbar)
    {
        toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(16, 16) };

        toolbar.Items.Add(NewButton("New", IconKind.New, async () => await OnNewExperimentAsync()));
        toolbar.Items.Add(NewButton("Clone", IconKind.Clone, async () => await OnCloneExperimentAsync()));
        toolbar.Items.Add(NewButton("Delete", IconKind.Delete, async () => await OnDeleteExperimentAsync()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(NewButton("Execute", IconKind.Execute, async () => await OnExecuteActiveAsync()));
        toolbar.Items.Add(NewButton("Benchmark…", IconKind.Benchmark, OnOpenBenchmarkDialog));
        toolbar.Items.Add(new ToolStripSeparator());

        _executionModeCombo.Items.AddRange(new object[] { nameof(ExecutionMode.Sequential), nameof(ExecutionMode.Parallel) });
        _executionModeCombo.SelectedIndex = 0;
        toolbar.Items.Add(new ToolStripLabel("Mode:"));
        toolbar.Items.Add(new ToolStripControlHost(_executionModeCombo));
        toolbar.Items.Add(new ToolStripLabel("Max concurrency:"));
        toolbar.Items.Add(new ToolStripControlHost(_maxConcurrencyInput));
        toolbar.Items.Add(NewButton("Execute All", IconKind.ExecuteAll, async () => await OnExecuteAllAsync()));
        _cancelAllButton.Click += (_, _) => _executeAllCts?.Cancel();
        _cancelAllButton.Image = IconFactory.Get(IconKind.CancelAll);
        _cancelAllButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        toolbar.Items.Add(new ToolStripControlHost(_cancelAllButton));
        toolbar.Items.Add(new ToolStripSeparator());

        toolbar.Items.Add(NewButton("Comparison Grid", IconKind.ComparisonGrid, OnOpenComparisonGrid));
        toolbar.Items.Add(NewButton("Export CSV…", IconKind.Export, () => OnExport(isCsv: true)));
        toolbar.Items.Add(NewButton("Export JSON…", IconKind.Export, () => OnExport(isCsv: false)));
    }

    private static ToolStripButton NewButton(string text, IconKind icon, Action onClick)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, Image = IconFactory.Get(icon) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static ToolStripButton NewButton(string text, IconKind icon, Func<Task> onClickAsync)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, Image = IconFactory.Get(icon) };
        button.Click += async (_, _) => await onClickAsync();
        return button;
    }

    private ExperimentTabControl? ActiveControl_ =>
        _experimentTabs.SelectedTab?.Controls.OfType<ExperimentTabControl>().FirstOrDefault();
}
