namespace ClaudeTraceHub.Web.Models;

public class ClaudeProject
{
    public string DirName { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string FullDirPath { get; set; } = "";
    public int SessionCount { get; set; }
    public DateTime? LastActivity { get; set; }
    public int TotalMessages { get; set; }
}

public class SessionSummary
{
    public string SessionId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ProjectDirName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime? Created { get; set; }
    public DateTime? Modified { get; set; }
    public int MessageCount { get; set; }
    public string FirstPrompt { get; set; } = "";
    public string? GitBranch { get; set; }
    public bool IsSidechain { get; set; }
}

public class Conversation
{
    public string SessionId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ProjectDirName { get; set; } = "";
    public DateTime? Created { get; set; }
    public DateTime? Modified { get; set; }
    public string? GitBranch { get; set; }
    public string FirstPrompt { get; set; } = "";
    public List<ConversationMessage> Messages { get; set; } = new();

    public long TotalOutputTokens => Messages
        .Where(m => m.OutputTokens.HasValue)
        .Sum(m => (long)m.OutputTokens!.Value);

    public long TotalInputTokens => Messages
        .Where(m => m.InputTokens.HasValue)
        .Sum(m => (long)m.InputTokens!.Value);

    public List<FileTouchedInfo> FilesTouched => Messages
        .SelectMany(m => m.ToolUsages)
        .Where(t => !string.IsNullOrEmpty(t.FilePath) && t.FileAction != FileActionType.None)
        .GroupBy(t => NormalizePath(t.FilePath!))
        .Select(g => new FileTouchedInfo
        {
            FilePath = g.Key,
            Action = g.Min(t => t.FileAction),
            Count = g.Count()
        })
        .OrderBy(f => f.Action)
        .ThenBy(f => f.FilePath)
        .ToList();

    public FileChangeTimeline GetFileTimeline(string filePath)
    {
        var normalized = NormalizePath(filePath);
        var changes = Messages
            .SelectMany(m => m.ToolUsages)
            .Where(t => !string.IsNullOrEmpty(t.FilePath)
                        && t.FileAction != FileActionType.None
                        && string.Equals(NormalizePath(t.FilePath!), normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.MessageIndex)
            .Select(t =>
            {
                var isLarge = (t.NewContent?.Length ?? 0) > 5000;
                var addedCount = (t.NewContent ?? "").Split('\n').Length;
                var removedCount = (t.OldContent ?? "").Split('\n').Length;
                if (string.IsNullOrEmpty(t.NewContent)) addedCount = 0;
                if (string.IsNullOrEmpty(t.OldContent)) removedCount = 0;
                return new FileChangeEntry
                {
                    MessageIndex = t.MessageIndex,
                    Timestamp = t.Timestamp,
                    Action = t.FileAction,
                    ToolName = t.ToolName,
                    OldContent = t.OldContent,
                    NewContent = t.NewContent,
                    ReplaceAll = t.ReplaceAll,
                    IsLargeContent = isLarge,
                    TruncatedNewContent = isLarge
                        ? string.Join("\n", (t.NewContent ?? "").Split('\n').Take(50)) + "\n... (truncated)"
                        : t.NewContent,
                    AddedLineCount = addedCount,
                    RemovedLineCount = removedCount
                };
            })
            .ToList();

        for (int i = 0; i < changes.Count; i++)
            changes[i].StepNumber = i + 1;

        return new FileChangeTimeline
        {
            FilePath = filePath,
            OverallAction = changes.Count > 0 ? changes.Min(c => c.Action) : FileActionType.None,
            Changes = changes
        };
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public class ConversationMessage
{
    public DateTime Timestamp { get; set; }
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Model { get; set; }
    public int? OutputTokens { get; set; }
    public int? InputTokens { get; set; }
    public string? MessageId { get; set; }
    public bool IsToolResult { get; set; }
    public List<ToolUsageInfo> ToolUsages { get; set; } = new();
}

public class ToolUsageInfo
{
    public string ToolName { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? FilePath { get; set; }
    public FileActionType FileAction { get; set; } = FileActionType.None;
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }
    public bool ReplaceAll { get; set; }
    public int MessageIndex { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum FileActionType
{
    None,
    Created,
    Modified,
    Read
}

public class FileTouchedInfo
{
    public string FilePath { get; set; } = "";
    public FileActionType Action { get; set; }
    public int Count { get; set; }
}

public class FileChangeTimeline
{
    public string FilePath { get; set; } = "";
    public FileActionType OverallAction { get; set; }
    public List<FileChangeEntry> Changes { get; set; } = new();
}

public class FileChangeEntry
{
    public int StepNumber { get; set; }
    public int MessageIndex { get; set; }
    public DateTime Timestamp { get; set; }
    public FileActionType Action { get; set; }
    public string ToolName { get; set; } = "";
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }
    public bool ReplaceAll { get; set; }
    public bool IsLargeContent { get; set; }
    public string? TruncatedNewContent { get; set; }
    public int AddedLineCount { get; set; }
    public int RemovedLineCount { get; set; }
}

public enum DiffLineType
{
    Unchanged,
    Added,
    Removed
}

public class DiffLine
{
    public int? OldLineNumber { get; set; }
    public int? NewLineNumber { get; set; }
    public string Content { get; set; } = "";
    public DiffLineType Type { get; set; }
}

public class DiffHunk
{
    public int OldStart { get; set; }
    public int OldCount { get; set; }
    public int NewStart { get; set; }
    public int NewCount { get; set; }
    public List<DiffLine> Lines { get; set; } = new();
    public string Header => $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@";
}
