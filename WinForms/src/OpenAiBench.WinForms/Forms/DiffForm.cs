using System.Text.Json;
using OpenAiBench.Core.Diffing;

namespace OpenAiBench.WinForms.Forms;

/// <summary>Shows a text diff, or a JSON-aware diff when both sides parse as JSON.</summary>
public sealed class DiffForm : Form
{
    private readonly RichTextBox _output = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font(FontFamily.GenericMonospace, 9) };

    private DiffForm(string leftLabel, string leftText, string rightLabel, string rightText)
    {
        Text = $"Compare: {leftLabel}  vs  {rightLabel}";
        Width = 900;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;

        Controls.Add(_output);
        RenderDiff(leftText ?? string.Empty, rightText ?? string.Empty);
    }

    public static void ShowDiff(IWin32Window owner, string leftLabel, string leftText, string rightLabel, string rightText)
    {
        using var form = new DiffForm(leftLabel, leftText, rightLabel, rightText);
        form.ShowDialog(owner);
    }

    private void RenderDiff(string leftText, string rightText)
    {
        if (TryJsonDiff(leftText, rightText, out var jsonResult))
        {
            RenderJsonDiff(jsonResult!);
            return;
        }

        RenderTextDiff(TextDiffer.Diff(leftText, rightText));
    }

    private static bool TryJsonDiff(string left, string right, out JsonDiffResult? result)
    {
        try
        {
            _ = JsonDocument.Parse(left);
            _ = JsonDocument.Parse(right);
            result = JsonDiffer.Diff(left, right);
            return true;
        }
        catch (JsonException)
        {
            result = null;
            return false;
        }
    }

    private void RenderJsonDiff(JsonDiffResult result)
    {
        _output.AppendText("JSON-aware diff" + Environment.NewLine + Environment.NewLine);

        if (result.IsEqual)
        {
            AppendLine("(no differences)", Color.Gray);
            return;
        }

        foreach (var entry in result.Differences)
        {
            var color = entry.Kind switch
            {
                JsonDiffKind.Added => Color.DarkGreen,
                JsonDiffKind.Removed => Color.DarkRed,
                _ => Color.DarkOrange
            };

            var text = entry.Kind switch
            {
                JsonDiffKind.Added => $"+ {entry.Path} = {entry.RightValue}",
                JsonDiffKind.Removed => $"- {entry.Path} = {entry.LeftValue}",
                _ => $"~ {entry.Path}: {entry.LeftValue} -> {entry.RightValue}"
            };

            AppendLine(text, color);
        }
    }

    private void RenderTextDiff(TextDiffResult result)
    {
        _output.AppendText("Text diff" + Environment.NewLine + Environment.NewLine);

        if (result.IsEqual)
        {
            AppendLine("(no differences)", Color.Gray);
            return;
        }

        foreach (var line in result.Lines)
        {
            var (prefix, color) = line.Kind switch
            {
                DiffLineKind.Added => ("+ ", Color.DarkGreen),
                DiffLineKind.Removed => ("- ", Color.DarkRed),
                _ => ("  ", Color.Black)
            };

            AppendLine(prefix + line.Text, color);
        }
    }

    private void AppendLine(string text, Color color)
    {
        _output.SelectionStart = _output.TextLength;
        _output.SelectionLength = 0;
        _output.SelectionColor = color;
        _output.AppendText(text + Environment.NewLine);
    }
}
