using ClaudeTraceHub.Web.Models;

namespace ClaudeTraceHub.Web.Services;

public class DailySummaryService
{
    private readonly ClaudeDataDiscoveryService _discovery;
    private readonly ConversationCacheService _cache;

    public DailySummaryService(ClaudeDataDiscoveryService discovery, ConversationCacheService cache)
    {
        _discovery = discovery;
        _cache = cache;
    }

    /// <summary>
    /// Get sessions for a specific date. Uses SessionSummary only (lightweight).
    /// </summary>
    public List<SessionSummary> GetSessionsForDate(DateTime date, string? projectDirName = null)
    {
        var sessions = _discovery.GetAllSessions();
        var query = sessions.Where(s => s.Created.HasValue && s.Created.Value.Date == date.Date);

        if (!string.IsNullOrEmpty(projectDirName))
            query = query.Where(s => s.ProjectDirName == projectDirName);

        return query.OrderByDescending(s => s.Created).ToList();
    }

    /// <summary>
    /// Compute aggregate stats for a date from SessionSummary data only.
    /// </summary>
    public DailyStats GetDailyStats(DateTime date, string? projectDirName = null)
    {
        var sessions = GetSessionsForDate(date, projectDirName);
        var projectNames = sessions.Select(s => s.ProjectName).Distinct().ToList();

        return new DailyStats
        {
            Date = date.Date,
            ConversationCount = sessions.Count,
            MessageCount = sessions.Sum(s => s.MessageCount),
            ProjectCount = projectNames.Count,
            ProjectNames = projectNames
        };
    }

    /// <summary>
    /// Get daily activity points for the last N days (for the mini bar chart).
    /// </summary>
    public List<DailyActivityPoint> GetActivityOverDays(int days = 30)
    {
        var sessions = _discovery.GetAllSessions();
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);

        var grouped = sessions
            .Where(s => s.Created.HasValue && s.Created.Value.Date >= cutoff)
            .GroupBy(s => s.Created!.Value.Date)
            .ToDictionary(g => g.Key, g => new
            {
                ConvCount = g.Count(),
                MsgCount = g.Sum(s => s.MessageCount)
            });

        var result = new List<DailyActivityPoint>();
        for (var d = cutoff; d <= DateTime.UtcNow.Date; d = d.AddDays(1))
        {
            var exists = grouped.GetValueOrDefault(d);
            result.Add(new DailyActivityPoint
            {
                Date = d,
                ConversationCount = exists?.ConvCount ?? 0,
                MessageCount = exists?.MsgCount ?? 0
            });
        }

        return result;
    }

    /// <summary>
    /// Get distinct projects that have sessions on a given date.
    /// </summary>
    public List<(string DirName, string Name)> GetActiveProjectsForDate(DateTime date)
    {
        var sessions = _discovery.GetAllSessions()
            .Where(s => s.Created.HasValue && s.Created.Value.Date == date.Date);

        return sessions
            .Select(s => (s.ProjectDirName, s.ProjectName))
            .Distinct()
            .OrderBy(p => p.ProjectName)
            .ToList();
    }

    /// <summary>
    /// Full-parse a single conversation and return rich detail.
    /// Called when user expands a conversation card.
    /// </summary>
    public DailyConversationDetail GetConversationDetail(SessionSummary session)
    {
        if (!File.Exists(session.FilePath))
        {
            return new DailyConversationDetail
            {
                SessionId = session.SessionId,
                ProjectName = session.ProjectName,
                ProjectDirName = session.ProjectDirName,
                FirstPrompt = session.FirstPrompt,
                GitBranch = session.GitBranch,
                MessageCount = session.MessageCount
            };
        }

        var conv = _cache.GetOrParse(session.FilePath, session.ProjectName, session.ProjectDirName);

        var toolSummary = conv.Messages
            .SelectMany(m => m.ToolUsages)
            .GroupBy(t => t.ToolName)
            .ToDictionary(g => g.Key, g => g.Count());

        var firstModel = conv.Messages
            .FirstOrDefault(m => m.Role == "assistant" && !string.IsNullOrEmpty(m.Model))?.Model;

        return new DailyConversationDetail
        {
            SessionId = conv.SessionId,
            ProjectName = conv.ProjectName,
            ProjectDirName = conv.ProjectDirName,
            FirstPrompt = conv.FirstPrompt,
            GitBranch = conv.GitBranch,
            Started = conv.Created,
            Ended = conv.Modified,
            Duration = conv.Created.HasValue && conv.Modified.HasValue
                ? conv.Modified.Value - conv.Created.Value
                : null,
            MessageCount = conv.Messages.Count,
            InputTokens = conv.TotalInputTokens,
            OutputTokens = conv.TotalOutputTokens,
            Model = firstModel != null ? SimplifyModelName(firstModel) : null,
            FilesTouched = conv.FilesTouched,
            ToolUsageSummary = toolSummary
        };
    }

    /// <summary>
    /// Aggregate all files changed across all conversations for a date.
    /// </summary>
    public List<DailyFileActivity> GetDailyFileActivity(DateTime date, string? projectDirName = null)
    {
        var sessions = GetSessionsForDate(date, projectDirName);
        var fileMap = new Dictionary<string, DailyFileActivity>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            if (!File.Exists(session.FilePath)) continue;

            var conv = _cache.GetOrParse(session.FilePath, session.ProjectName, session.ProjectDirName);
            foreach (var file in conv.FilesTouched)
            {
                if (!fileMap.TryGetValue(file.FilePath, out var activity))
                {
                    activity = new DailyFileActivity
                    {
                        FilePath = file.FilePath,
                        Action = file.Action,
                        TotalOperations = 0,
                        SessionIds = new List<string>()
                    };
                    fileMap[file.FilePath] = activity;
                }

                activity.TotalOperations += file.Count;
                if (!activity.SessionIds.Contains(session.SessionId))
                    activity.SessionIds.Add(session.SessionId);

                // Keep the most significant action (Created < Modified < Read)
                if (file.Action < activity.Action)
                    activity.Action = file.Action;
            }
        }

        return fileMap.Values
            .OrderBy(f => f.Action)
            .ThenByDescending(f => f.TotalOperations)
            .ToList();
    }

    /// <summary>
    /// Compute hourly token usage breakdown for a date.
    /// </summary>
    public List<HourlyTokenUsage> GetHourlyTokenUsage(DateTime date, string? projectDirName = null)
    {
        var sessions = GetSessionsForDate(date, projectDirName);
        var hourly = new long[24, 2]; // [hour, 0=input 1=output]

        foreach (var session in sessions)
        {
            if (!File.Exists(session.FilePath)) continue;

            var conv = _cache.GetOrParse(session.FilePath, session.ProjectName, session.ProjectDirName);
            foreach (var msg in conv.Messages.Where(m => m.Role == "assistant"))
            {
                var hour = msg.Timestamp.Hour;
                hourly[hour, 0] += msg.InputTokens ?? 0;
                hourly[hour, 1] += msg.OutputTokens ?? 0;
            }
        }

        var result = new List<HourlyTokenUsage>();
        for (int h = 0; h < 24; h++)
        {
            result.Add(new HourlyTokenUsage
            {
                Hour = h,
                InputTokens = hourly[h, 0],
                OutputTokens = hourly[h, 1]
            });
        }

        return result;
    }

    /// <summary>
    /// Generate a brief paragraph summarizing what was done on a given date.
    /// Uses lightweight SessionSummary data only.
    /// </summary>
    public string GenerateDaySummary(DateTime date, List<SessionSummary> sessions, DailyStats stats)
    {
        if (sessions.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();

        // Opening: date + conversation count + project context
        var dateStr = date.Date == DateTime.Today ? "Today" : date.ToString("MMMM dd, yyyy");
        sb.Append($"On {dateStr}, you had ");
        sb.Append(stats.ConversationCount == 1 ? "1 conversation" : $"{stats.ConversationCount} conversations");

        if (stats.ProjectCount > 0)
        {
            sb.Append(" across ");
            sb.Append(stats.ProjectCount == 1 ? "1 project" : $"{stats.ProjectCount} projects");
            var projectList = stats.ProjectNames.Take(3).ToList();
            sb.Append($" ({string.Join(", ", projectList)}");
            if (stats.ProjectNames.Count > 3)
                sb.Append($" and {stats.ProjectNames.Count - 3} more");
            sb.Append(')');
        }

        sb.Append($", exchanging {FormatCount(stats.MessageCount, "message")}. ");

        // Time range
        var earliest = sessions.Where(s => s.Created.HasValue).Min(s => s.Created!.Value);
        var latest = sessions.Where(s => s.Modified.HasValue || s.Created.HasValue)
            .Max(s => s.Modified ?? s.Created!.Value);
        sb.Append($"Activity spanned from {earliest:HH:mm} to {latest:HH:mm}. ");

        // Topics worked on (from first prompts, deduplicated, top 3)
        var topics = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.FirstPrompt))
            .Select(s => TruncateForSummary(s.FirstPrompt, 60))
            .Distinct()
            .Take(3)
            .ToList();

        if (topics.Count > 0)
        {
            sb.Append("Topics included: ");
            sb.Append(string.Join("; ", topics.Select(t => $"\"{t}\"")));
            if (sessions.Count > topics.Count)
                sb.Append($" and {sessions.Count - topics.Count} more");
            sb.Append('.');
        }

        // Git branches
        var branches = sessions
            .Where(s => !string.IsNullOrEmpty(s.GitBranch))
            .Select(s => s.GitBranch!)
            .Distinct()
            .Take(3)
            .ToList();

        if (branches.Count > 0)
        {
            sb.Append($" Branches: {string.Join(", ", branches)}.");
        }

        return sb.ToString();
    }

    private static string FormatCount(int count, string singular)
    {
        return count == 1 ? $"1 {singular}" : $"{count} {singular}s";
    }

    private static string TruncateForSummary(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        // Take first line only
        var firstLine = text.Split('\n')[0].Trim();
        return firstLine.Length <= max ? firstLine : firstLine[..max] + "...";
    }

    private static string SimplifyModelName(string model)
    {
        if (model.Contains("opus")) return "Claude Opus";
        if (model.Contains("sonnet")) return "Claude Sonnet";
        if (model.Contains("haiku")) return "Claude Haiku";
        return model;
    }
}
