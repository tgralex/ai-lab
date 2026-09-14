using OpenAiBench.Core.Domain;
using OpenAiBench.WinForms.Icons;

namespace OpenAiBench.WinForms.Controls;

/// <summary>
/// Editable grid of {{name}} variable bindings, each pointing at either literal text or an
/// attached file, so the same prompt template can be re-run against different data.
/// </summary>
public sealed class VariablesEditor : UserControl
{
    private readonly DataGridView _grid;
    private readonly DataGridViewComboBoxColumn _fileColumn;

    private Experiment _experiment = null!;
    private RequestDefinition _request = null!;
    private Action? _onChanged;
    private bool _suppressEvents;

    public VariablesEditor()
    {
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Variable ({{name}})" });

        var kindColumn = new DataGridViewComboBoxColumn { Name = "Kind", HeaderText = "Kind" };
        kindColumn.Items.AddRange(new object[] { nameof(VariableBindingKind.Text), nameof(VariableBindingKind.File) });
        _grid.Columns.Add(kindColumn);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TextValue", HeaderText = "Text Value" });

        _fileColumn = new DataGridViewComboBoxColumn { Name = "FileId", HeaderText = "File", ValueType = typeof(string) };
        _grid.Columns.Add(_fileColumn);

        _grid.CellValueChanged += OnCellValueChanged;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        var addButton = new Button { Text = "Add Variable", AutoSize = true, Image = IconFactory.Get(IconKind.AddVariable), TextImageRelation = TextImageRelation.ImageBeforeText };
        addButton.Click += (_, _) => AddRow();
        var removeButton = new Button { Text = "Remove Variable", AutoSize = true, Image = IconFactory.Get(IconKind.RemoveVariable), TextImageRelation = TextImageRelation.ImageBeforeText };
        removeButton.Click += (_, _) => RemoveSelectedRow();

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        buttonPanel.Controls.Add(addButton);
        buttonPanel.Controls.Add(removeButton);

        Controls.Add(_grid);
        Controls.Add(buttonPanel);
        Dock = DockStyle.Fill;
    }

    public void Initialize(Experiment experiment, RequestDefinition request, Action onChanged)
    {
        _experiment = experiment;
        _request = request;
        _onChanged = onChanged;
        RefreshFileOptions();
        RefreshGrid();
    }

    public void RefreshFileOptions()
    {
        if (_experiment is null)
        {
            // Called via a sibling editor's onChanged callback during initial load, before our own
            // Initialize has run yet — nothing to refresh against until then.
            return;
        }

        _fileColumn.Items.Clear();
        _fileColumn.Items.AddRange(_experiment.Files.Select(f => (object)f.OriginalFileName).ToArray());
    }

    private void RefreshGrid()
    {
        _suppressEvents = true;
        _grid.Rows.Clear();
        foreach (var variable in _request.Variables)
        {
            var fileDisplayName = variable.FileId is null
                ? null
                : _experiment.Files.FirstOrDefault(f => f.Id == variable.FileId)?.OriginalFileName;

            var rowIndex = _grid.Rows.Add(variable.Name, variable.Kind.ToString(), variable.TextValue, fileDisplayName);
            _grid.Rows[rowIndex].Tag = variable;
        }

        _suppressEvents = false;
    }

    private void AddRow()
    {
        var variable = new VariableBinding { Name = $"var{_request.Variables.Count + 1}", Kind = VariableBindingKind.Text, TextValue = string.Empty };
        _request.Variables.Add(variable);
        RefreshGrid();
        _onChanged?.Invoke();
    }

    private void RemoveSelectedRow()
    {
        if (_grid.CurrentRow?.Tag is not VariableBinding variable)
        {
            return;
        }

        _request.Variables.Remove(variable);
        RefreshGrid();
        _onChanged?.Invoke();
    }

    private void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressEvents || e.RowIndex < 0)
        {
            return;
        }

        if (_grid.Rows[e.RowIndex].Tag is not VariableBinding oldVariable)
        {
            return;
        }

        var row = _grid.Rows[e.RowIndex];
        var name = row.Cells["Name"].Value?.ToString() ?? oldVariable.Name;
        var kind = Enum.TryParse<VariableBindingKind>(row.Cells["Kind"].Value?.ToString(), out var parsedKind) ? parsedKind : VariableBindingKind.Text;
        var textValue = row.Cells["TextValue"].Value?.ToString();
        var fileDisplayName = row.Cells["FileId"].Value?.ToString();
        var fileId = _experiment.Files.FirstOrDefault(f => f.OriginalFileName == fileDisplayName)?.Id;

        var updated = new VariableBinding { Name = name, Kind = kind, TextValue = textValue, FileId = fileId };
        var index = _request.Variables.IndexOf(oldVariable);
        if (index >= 0)
        {
            _request.Variables[index] = updated;
            row.Tag = updated;
        }

        _onChanged?.Invoke();
    }
}
