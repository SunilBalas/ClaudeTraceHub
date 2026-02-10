using ClaudeTraceHub.Web.Models;

namespace ClaudeTraceHub.Web.Services;

public static class LineDiffHelper
{
    private const int MaxLcsProduct = 100_000;

    /// <summary>
    /// Compute a line-level diff between old and new text.
    /// For Edit operations: shows removed/added/unchanged lines.
    /// </summary>
    public static List<DiffLine> ComputeDiff(string? oldText, string? newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        if (oldLines.Length == 0 && newLines.Length == 0)
            return new List<DiffLine>();

        if (oldLines.Length == 0)
            return ForAllAdded(newLines);

        if (newLines.Length == 0)
            return ForAllRemoved(oldLines);

        // If too large for LCS, fall back to simple remove-all/add-all
        if ((long)oldLines.Length * newLines.Length > MaxLcsProduct)
            return FallbackDiff(oldLines, newLines);

        return LcsDiff(oldLines, newLines);
    }

    /// <summary>
    /// Generate diff lines for a newly created file (all added).
    /// </summary>
    public static List<DiffLine> ForNewFile(string? content)
    {
        return ForAllAdded(SplitLines(content));
    }

    /// <summary>
    /// Group flat diff lines into hunks with N context lines around changes (like git diff / GitHub).
    /// </summary>
    public static List<DiffHunk> GroupIntoHunks(List<DiffLine> allLines, int contextLines = 3)
    {
        if (allLines.Count == 0) return new();

        // Find indices of changed lines
        var changeIndices = new List<int>();
        for (int i = 0; i < allLines.Count; i++)
        {
            if (allLines[i].Type != DiffLineType.Unchanged)
                changeIndices.Add(i);
        }

        // If no changes found (all unchanged) or all changes, return single hunk
        if (changeIndices.Count == 0 || !allLines.Any(l => l.Type == DiffLineType.Unchanged))
            return new List<DiffHunk> { BuildHunk(allLines, 0, allLines.Count - 1) };

        // Build ranges: each change gets contextLines before and after
        var hunks = new List<DiffHunk>();
        int start = Math.Max(0, changeIndices[0] - contextLines);
        int end = Math.Min(allLines.Count - 1, changeIndices[0] + contextLines);

        for (int i = 1; i < changeIndices.Count; i++)
        {
            int nextStart = Math.Max(0, changeIndices[i] - contextLines);
            int nextEnd = Math.Min(allLines.Count - 1, changeIndices[i] + contextLines);

            if (nextStart <= end + 1)
            {
                // Overlapping or adjacent — merge
                end = nextEnd;
            }
            else
            {
                hunks.Add(BuildHunk(allLines, start, end));
                start = nextStart;
                end = nextEnd;
            }
        }

        hunks.Add(BuildHunk(allLines, start, end));
        return hunks;
    }

    private static DiffHunk BuildHunk(List<DiffLine> allLines, int start, int end)
    {
        var hunkLines = allLines.GetRange(start, end - start + 1);

        int oldStart = 1, newStart = 1;
        foreach (var l in hunkLines)
        {
            if (l.OldLineNumber.HasValue) { oldStart = l.OldLineNumber.Value; break; }
        }
        foreach (var l in hunkLines)
        {
            if (l.NewLineNumber.HasValue) { newStart = l.NewLineNumber.Value; break; }
        }

        int oldCount = hunkLines.Count(l => l.Type != DiffLineType.Added);
        int newCount = hunkLines.Count(l => l.Type != DiffLineType.Removed);

        return new DiffHunk
        {
            OldStart = oldStart,
            OldCount = oldCount,
            NewStart = newStart,
            NewCount = newCount,
            Lines = hunkLines
        };
    }

    private static List<DiffLine> LcsDiff(string[] oldLines, string[] newLines)
    {
        int m = oldLines.Length, n = newLines.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                dp[i, j] = oldLines[i - 1] == newLines[j - 1]
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        // Backtrack
        var result = new List<DiffLine>();
        int oi = m, ni = n;

        while (oi > 0 || ni > 0)
        {
            if (oi > 0 && ni > 0 && oldLines[oi - 1] == newLines[ni - 1])
            {
                result.Add(new DiffLine
                {
                    OldLineNumber = oi,
                    NewLineNumber = ni,
                    Content = oldLines[oi - 1],
                    Type = DiffLineType.Unchanged
                });
                oi--;
                ni--;
            }
            else if (ni > 0 && (oi == 0 || dp[oi, ni - 1] >= dp[oi - 1, ni]))
            {
                result.Add(new DiffLine
                {
                    NewLineNumber = ni,
                    Content = newLines[ni - 1],
                    Type = DiffLineType.Added
                });
                ni--;
            }
            else
            {
                result.Add(new DiffLine
                {
                    OldLineNumber = oi,
                    Content = oldLines[oi - 1],
                    Type = DiffLineType.Removed
                });
                oi--;
            }
        }

        result.Reverse();
        return result;
    }

    private static List<DiffLine> FallbackDiff(string[] oldLines, string[] newLines)
    {
        var result = new List<DiffLine>(oldLines.Length + newLines.Length);

        for (int i = 0; i < oldLines.Length; i++)
            result.Add(new DiffLine
            {
                OldLineNumber = i + 1,
                Content = oldLines[i],
                Type = DiffLineType.Removed
            });

        for (int i = 0; i < newLines.Length; i++)
            result.Add(new DiffLine
            {
                NewLineNumber = i + 1,
                Content = newLines[i],
                Type = DiffLineType.Added
            });

        return result;
    }

    private static List<DiffLine> ForAllAdded(string[] lines)
    {
        var result = new List<DiffLine>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
            result.Add(new DiffLine
            {
                NewLineNumber = i + 1,
                Content = lines[i],
                Type = DiffLineType.Added
            });
        return result;
    }

    private static List<DiffLine> ForAllRemoved(string[] lines)
    {
        var result = new List<DiffLine>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
            result.Add(new DiffLine
            {
                OldLineNumber = i + 1,
                Content = lines[i],
                Type = DiffLineType.Removed
            });
        return result;
    }

    private static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        // Normalize line endings then split
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    }
}
