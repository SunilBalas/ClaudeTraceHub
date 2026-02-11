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
    /// Generate a rich, insight-driven paragraph summarizing what was done on a given date.
    /// Uses smart heuristics to produce varied, natural-sounding summaries without any external API.
    /// </summary>
    public string GenerateDaySummary(DateTime date, List<SessionSummary> sessions, DailyStats stats)
    {
        if (sessions.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        var seed = date.DayOfYear + date.Year;

        // 1. Opening sentence - varied phrasing
        sb.Append(BuildOpening(date, sessions, stats, seed));

        // 2. Work classification insight
        var workInsight = BuildWorkInsight(sessions);
        if (!string.IsNullOrEmpty(workInsight))
            sb.Append(' ').Append(workInsight);

        // 3. Focus / depth insight
        sb.Append(' ').Append(BuildFocusInsight(sessions, stats, seed));

        // 4. Time pattern insight
        var timeInsight = BuildTimeInsight(sessions);
        if (!string.IsNullOrEmpty(timeInsight))
            sb.Append(' ').Append(timeInsight);

        // 5. Closing detail - topics
        var closingDetail = BuildClosingDetail(sessions);
        if (!string.IsNullOrEmpty(closingDetail))
            sb.Append(' ').Append(closingDetail);

        return sb.ToString();
    }

    #region Smart Summary Helpers

    private static string BuildOpening(DateTime date, List<SessionSummary> sessions, DailyStats stats, int seed)
    {
        var convText = stats.ConversationCount == 1 ? "1 conversation" : $"{stats.ConversationCount} conversations";
        var msgText = stats.MessageCount == 1 ? "1 message" : $"{stats.MessageCount} messages";
        var projectList = FormatProjectList(stats.ProjectNames);

        var templates = new[]
        {
            $"You had a {DescribeProductivity(stats)} day with {convText}{projectList}, exchanging {msgText}.",
            $"Across {convText}{projectList}, you exchanged {msgText}{DescribeDateSuffix(date)}.",
            $"Your {DescribeDateLabel(date)} included {convText}{projectList} with {msgText} exchanged.",
        };

        return templates[seed % templates.Length];
    }

    private static string FormatProjectList(List<string> projectNames)
    {
        if (projectNames.Count == 0) return "";
        if (projectNames.Count == 1) return $" in {projectNames[0]}";
        if (projectNames.Count == 2) return $" across {projectNames[0]} and {projectNames[1]}";
        var listed = string.Join(", ", projectNames.Take(3));
        var suffix = projectNames.Count > 3 ? $" and {projectNames.Count - 3} more" : "";
        return $" across {listed}{suffix}";
    }

    private static string DescribeProductivity(DailyStats stats)
    {
        var avgMsgs = stats.ConversationCount > 0 ? stats.MessageCount / stats.ConversationCount : 0;
        if (stats.ConversationCount >= 8 || stats.MessageCount >= 150) return "busy";
        if (avgMsgs >= 25) return "productive";
        if (stats.ConversationCount == 1) return "focused";
        return "steady";
    }

    private static string DescribeDateSuffix(DateTime date)
    {
        if (date.Date == DateTime.Today) return " today";
        if (date.Date == DateTime.Today.AddDays(-1)) return " yesterday";
        return $" on {date:MMMM dd}";
    }

    private static string DescribeDateLabel(DateTime date)
    {
        if (date.Date == DateTime.Today) return "day so far";
        if (date.Date == DateTime.Today.AddDays(-1)) return "yesterday";
        return date.ToString("dddd");
    }

    private static string? BuildWorkInsight(List<SessionSummary> sessions)
    {
        var categories = ClassifyWork(sessions);
        var parts = new List<string>();

        if (categories.Features > 0)
            parts.Add(categories.Features == 1 ? "feature development" : $"{categories.Features} feature tasks");
        if (categories.BugFixes > 0)
            parts.Add(categories.BugFixes == 1 ? "a bug fix" : $"{categories.BugFixes} bug fixes");
        if (categories.Refactoring > 0)
            parts.Add("refactoring");
        if (categories.Chores > 0)
            parts.Add(categories.Chores == 1 ? "a maintenance task" : "maintenance tasks");

        if (parts.Count == 0) return null;

        var joined = parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts.Last();

        return $"Your work included {joined}.";
    }

    private static WorkCategories ClassifyWork(List<SessionSummary> sessions)
    {
        var result = new WorkCategories();

        foreach (var session in sessions)
        {
            var branch = session.GitBranch?.ToLowerInvariant() ?? "";
            var prompt = session.FirstPrompt?.ToLowerInvariant() ?? "";

            if (branch.Contains("feature/") || branch.Contains("feat/")
                || prompt.Contains("add ") || prompt.Contains("implement") || prompt.Contains("create ")
                || prompt.Contains("new ") || prompt.Contains("build "))
            {
                result.Features++;
            }
            else if (branch.Contains("bugfix/") || branch.Contains("hotfix/") || branch.Contains("fix/")
                || prompt.Contains("fix ") || prompt.Contains("bug") || prompt.Contains("error")
                || prompt.Contains("issue") || prompt.Contains("broken"))
            {
                result.BugFixes++;
            }
            else if (branch.Contains("refactor/")
                || prompt.Contains("refactor") || prompt.Contains("clean up")
                || prompt.Contains("reorganize") || prompt.Contains("restructure"))
            {
                result.Refactoring++;
            }
            else if (branch.Contains("chore/") || branch.Contains("ci/") || branch.Contains("docs/")
                || prompt.Contains("update ") || prompt.Contains("bump") || prompt.Contains("config")
                || prompt.Contains("documentation") || prompt.Contains("readme"))
            {
                result.Chores++;
            }
            else
            {
                result.Other++;
            }
        }

        return result;
    }

    private static string BuildFocusInsight(List<SessionSummary> sessions, DailyStats stats, int seed)
    {
        var deepSessions = sessions.Count(s => s.MessageCount >= 15);
        var quickSessions = sessions.Count(s => s.MessageCount < 8);

        if (stats.ProjectCount == 1 && sessions.Count == 1)
        {
            var templates = new[]
            {
                "This was a single focused session dedicated entirely to one project.",
                "You spent the session fully concentrated on a single project."
            };
            return templates[seed % templates.Length];
        }

        if (stats.ProjectCount == 1 && deepSessions >= 2)
            return $"You maintained deep focus on {stats.ProjectNames[0]} across multiple sessions.";

        if (stats.ProjectCount >= 3)
            return $"You context-switched between {stats.ProjectCount} different projects throughout the day.";

        if (deepSessions > 0 && quickSessions > 0)
            return $"Your sessions varied in depth \u2014 {deepSessions} deep {(deepSessions == 1 ? "session" : "sessions")} alongside {quickSessions} quick {(quickSessions == 1 ? "interaction" : "interactions")}.";

        if (deepSessions >= 2)
            return $"You had {deepSessions} in-depth sessions with extensive back-and-forth.";

        if (quickSessions == sessions.Count)
            return "Your interactions were brief and targeted throughout the day.";

        return $"You worked across {stats.ProjectCount} {(stats.ProjectCount == 1 ? "project" : "projects")} in {sessions.Count} {(sessions.Count == 1 ? "session" : "sessions")}.";
    }

    private static string? BuildTimeInsight(List<SessionSummary> sessions)
    {
        var timestamps = sessions
            .Where(s => s.Created.HasValue)
            .Select(s => s.Created!.Value)
            .OrderBy(t => t)
            .ToList();

        if (timestamps.Count < 2) return null;

        var earliest = timestamps.First();
        var latest = sessions
            .Where(s => s.Modified.HasValue || s.Created.HasValue)
            .Max(s => s.Modified ?? s.Created!.Value);

        var span = latest - earliest;

        // Determine dominant time period
        var morningCount = timestamps.Count(t => t.Hour < 12);
        var afternoonCount = timestamps.Count(t => t.Hour >= 12 && t.Hour < 17);
        var eveningCount = timestamps.Count(t => t.Hour >= 17);

        string periodDesc;
        if (morningCount > 0 && afternoonCount == 0 && eveningCount == 0)
            periodDesc = $"concentrated in the morning ({earliest:HH:mm}\u2013{latest:HH:mm})";
        else if (afternoonCount > 0 && morningCount == 0 && eveningCount == 0)
            periodDesc = $"concentrated in the afternoon ({earliest:HH:mm}\u2013{latest:HH:mm})";
        else if (eveningCount > 0 && morningCount == 0 && afternoonCount == 0)
            periodDesc = $"concentrated in the evening ({earliest:HH:mm}\u2013{latest:HH:mm})";
        else if (span.TotalHours >= 8)
            periodDesc = $"spread across the day from {earliest:HH:mm} to {latest:HH:mm}";
        else if (span.TotalHours >= 4)
            periodDesc = $"spanning several hours ({earliest:HH:mm}\u2013{latest:HH:mm})";
        else
            periodDesc = $"within a {FormatSpan(span)} window ({earliest:HH:mm}\u2013{latest:HH:mm})";

        return $"Activity was {periodDesc}.";
    }

    private static string? BuildClosingDetail(List<SessionSummary> sessions)
    {
        var topics = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.FirstPrompt))
            .Select(s => TruncateForSummary(s.FirstPrompt, 50))
            .Distinct()
            .ToList();

        var branches = sessions
            .Where(s => !string.IsNullOrEmpty(s.GitBranch))
            .Select(s => s.GitBranch!)
            .Distinct()
            .Take(3)
            .ToList();

        var parts = new List<string>();

        if (topics.Count == 1)
        {
            parts.Add($"The session focused on \"{topics[0]}\"");
        }
        else if (topics.Count <= 3)
        {
            parts.Add($"Topics ranged from \"{topics[0]}\" to \"{topics.Last()}\"");
        }
        else
        {
            parts.Add($"You tackled {topics.Count} different topics including \"{topics[0]}\" and \"{topics[1]}\"");
        }

        if (branches.Count > 0)
        {
            var branchText = branches.Count == 1
                ? $"on branch {branches[0]}"
                : $"across branches {string.Join(", ", branches)}";
            parts.Add(branchText);
        }

        if (parts.Count == 0) return null;

        return string.Join(", ", parts) + ".";
    }

    private static string FormatSpan(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            var hours = (int)span.TotalHours;
            return hours == 1 ? "1-hour" : $"{hours}-hour";
        }
        var mins = (int)span.TotalMinutes;
        return $"{mins}-minute";
    }

    private record WorkCategories
    {
        public int Features { get; set; }
        public int BugFixes { get; set; }
        public int Refactoring { get; set; }
        public int Chores { get; set; }
        public int Other { get; set; }
    }

    #endregion

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
