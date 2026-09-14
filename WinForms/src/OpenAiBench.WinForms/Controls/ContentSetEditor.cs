using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Ports;
using OpenAiBench.Infrastructure.Persistence;
using OpenAiBench.WinForms.Icons;

namespace OpenAiBench.WinForms.Controls;

/// <summary>Editor for a <see cref="ContentSet"/>: free text plus a grid of attached files (name/size/type/hash).</summary>
public sealed class ContentSetEditor : UserControl
{
    private readonly TextBox _textBox;
    private readonly DataGridView _filesGrid;
    private readonly Button _addButton;
    private readonly Button _removeButton;
    private readonly Button _openButton;

    private Experiment _experiment = null!;
    private ContentSet _contentSet = null!;
    private IWorkspaceStore _workspaceStore = null!;
    private Action? _onChanged;
    private bool _initializing;

    public ContentSetEditor()
    {
        _textBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 80 };
        _textBox.TextChanged += (_, _) =>
        {
            if (_initializing)
            {
                return;
            }

            _contentSet.Text = _textBox.Text;
            _onChanged?.Invoke();
        };

        _filesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            Height = 90
        };
        _filesGrid.Columns.Add("Name", "File");
        _filesGrid.Columns.Add("Size", "Size (bytes)");
        _filesGrid.Columns.Add("Type", "Type");
        _filesGrid.Columns.Add("Hash", "SHA-256");
        _filesGrid.Columns["Hash"]!.FillWeight = 200;

        _addButton = new Button { Text = "Add File…", AutoSize = true, Image = IconFactory.Get(IconKind.AddFile), TextImageRelation = TextImageRelation.ImageBeforeText };
        _addButton.Click += OnAddFileClicked;
        _removeButton = new Button { Text = "Remove", AutoSize = true, Image = IconFactory.Get(IconKind.RemoveFile), TextImageRelation = TextImageRelation.ImageBeforeText };
        _removeButton.Click += OnRemoveFileClicked;
        _openButton = new Button { Text = "Open", AutoSize = true, Image = IconFactory.Get(IconKind.OpenFile), TextImageRelation = TextImageRelation.ImageBeforeText };
        _openButton.Click += OnOpenFileClicked;

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttonPanel.Controls.Add(_addButton);
        buttonPanel.Controls.Add(_removeButton);
        buttonPanel.Controls.Add(_openButton);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        split.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        split.Controls.Add(_textBox, 0, 0);
        split.Controls.Add(_filesGrid, 0, 1);
        split.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(split);
        Dock = DockStyle.Fill;
    }

    public void Initialize(Experiment experiment, ContentSet contentSet, IWorkspaceStore workspaceStore, Action onChanged)
    {
        _initializing = true;
        _experiment = experiment;
        _contentSet = contentSet;
        _workspaceStore = workspaceStore;
        _onChanged = onChanged;

        _textBox.Text = contentSet.Text;
        RefreshFilesGrid();
        _initializing = false;
    }

    private void RefreshFilesGrid()
    {
        _filesGrid.Rows.Clear();
        foreach (var fileId in _contentSet.FileIds)
        {
            var file = _experiment.Files.FirstOrDefault(f => f.Id == fileId);
            if (file is null)
            {
                continue;
            }

            var rowIndex = _filesGrid.Rows.Add(file.OriginalFileName, file.SizeBytes, file.ContentType, file.Sha256);
            _filesGrid.Rows[rowIndex].Tag = file.Id;
        }
    }

    private async void OnAddFileClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "Attach file(s)" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            try
            {
                // Dedupe by content hash before copying — attaching the same file (here or in another
                // content set) reuses the existing record instead of storing a second identical copy.
                var hash = await FileHasher.ComputeSha256Async(path);
                var existing = _experiment.Files.FirstOrDefault(f => f.Sha256 == hash);

                var fileRef = existing ?? await _workspaceStore.AddFileAsync(_experiment.Id, path);
                if (existing is null)
                {
                    _experiment.Files.Add(fileRef);
                }

                if (!_contentSet.FileIds.Contains(fileRef.Id))
                {
                    _contentSet.FileIds.Add(fileRef.Id);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not attach '{path}': {ex.Message}", "Attach file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        RefreshFilesGrid();
        _onChanged?.Invoke();
    }

    private void OnRemoveFileClicked(object? sender, EventArgs e)
    {
        if (_filesGrid.CurrentRow?.Tag is not string fileId)
        {
            return;
        }

        _contentSet.FileIds.Remove(fileId);
        RefreshFilesGrid();
        _onChanged?.Invoke();
    }

    private void OnOpenFileClicked(object? sender, EventArgs e)
    {
        if (_filesGrid.CurrentRow?.Tag is not string fileId)
        {
            return;
        }

        var file = _experiment.Files.FirstOrDefault(f => f.Id == fileId);
        if (file is null)
        {
            return;
        }

        var path = _workspaceStore.GetFileAbsolutePath(_experiment.Id, file);
        if (File.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
