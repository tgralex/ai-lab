namespace OpenAiBench.Core.Diffing;

/// <summary>Line-based diff using the classic LCS (longest common subsequence) backtrack.</summary>
public static class TextDiffer
{
    public static TextDiffResult Diff(string left, string right)
    {
        var leftLines = SplitLines(left);
        var rightLines = SplitLines(right);

        var lcs = BuildLcsTable(leftLines, rightLines);
        var lines = new List<DiffLine>();
        Backtrack(lcs, leftLines, rightLines, leftLines.Length, rightLines.Length, lines);
        lines.Reverse();

        return new TextDiffResult { Lines = lines };
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n');

    /// <summary>Prefix-based LCS table: table[i,j] = LCS length of a[..i] and b[..j].</summary>
    private static int[,] BuildLcsTable(string[] a, string[] b)
    {
        var table = new int[a.Length + 1, b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                table[i, j] = a[i - 1] == b[j - 1]
                    ? table[i - 1, j - 1] + 1
                    : Math.Max(table[i - 1, j], table[i, j - 1]);
            }
        }

        return table;
    }

    private static void Backtrack(int[,] lcs, string[] a, string[] b, int i, int j, List<DiffLine> output)
    {
        while (true)
        {
            if (i > 0 && j > 0 && a[i - 1] == b[j - 1])
            {
                output.Add(new DiffLine { Kind = DiffLineKind.Unchanged, Text = a[i - 1] });
                i--; j--;
            }
            else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
            {
                output.Add(new DiffLine { Kind = DiffLineKind.Added, Text = b[j - 1] });
                j--;
            }
            else if (i > 0 && (j == 0 || lcs[i, j - 1] < lcs[i - 1, j]))
            {
                output.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = a[i - 1] });
                i--;
            }
            else
            {
                break;
            }
        }
    }
}
