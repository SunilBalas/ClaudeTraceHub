using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeTraceHub.Web.Models;

namespace ClaudeTraceHub.Web.Services;

public class ClaudeDataDiscoveryService
{
    private readonly string _projectsRoot;
    private readonly JsonlParserService _parser;

    public ClaudeDataDiscoveryService(IConfiguration config, JsonlParserService parser)
    {
        _parser = parser;
        var configPath = config["ClaudeDataPath"];
        _projectsRoot = !string.IsNullOrEmpty(configPath)
            ? configPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    }

    public List<ClaudeProject> GetAllProjects()
    {
        if (!Directory.Exists(_projectsRoot))
            return new List<ClaudeProject>();

        var projects = new List<ClaudeProject>();

        foreach (var dir in Directory.GetDirectories(_projectsRoot).OrderBy(d => d))
        {
            var dirName = Path.GetFileName(dir);
            var (projectPath, projectName) = DecodeProjectDirName(dirName);

            var indexPath = Path.Combine(dir, "sessions-index.json");
            int sessionCount = 0;
            int totalMessages = 0;
            DateTime? lastActivity = null;

            if (File.Exists(indexPath))
            {
                try
                {
                    var json = File.ReadAllText(indexPath);
                    var index = JsonSerializer.Deserialize<SessionsIndex>(json);
                    if (index != null)
                    {
                        sessionCount = index.Entries.Count;
                        totalMessages = index.Entries.Sum(e => e.MessageCount);

                        if (!string.IsNullOrEmpty(index.OriginalPath))
                        {
                            projectPath = index.OriginalPath;
                            projectName = Path.GetFileName(projectPath);
                        }
                        else if (index.Entries.Count > 0 && !string.IsNullOrEmpty(index.Entries[0].ProjectPath))
                        {
                            projectPath = index.Entries[0].ProjectPath;
                            projectName = Path.GetFileName(projectPath);
                        }

                        var dates = index.Entries
                            .Select(e => ParseTimestamp(e.Modified))
                            .Where(d => d.HasValue)
                            .Select(d => d!.Value)
                            .ToList();
                        if (dates.Count > 0)
                            lastActivity = dates.Max();
                    }
                }
                catch (Exception)
                {
                    ScanFallbackMetadata(dir, projectName ?? dirName, dirName,
                        out sessionCount, out totalMessages, out lastActivity);
                }
            }
            else
            {
                ScanFallbackMetadata(dir, projectName ?? dirName, dirName,
                    out sessionCount, out totalMessages, out lastActivity);
            }

            if (sessionCount == 0) continue;

            projects.Add(new ClaudeProject
            {
                DirName = dirName,
                ProjectPath = projectPath ?? "",
                ProjectName = projectName ?? dirName,
                FullDirPath = dir,
                SessionCount = sessionCount,
                LastActivity = lastActivity,
                TotalMessages = totalMessages
            });
        }

        return projects.OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<SessionSummary> GetSessionsForProject(string projectDirName)
    {
        var dir = Path.Combine(_projectsRoot, projectDirName);
        if (!Directory.Exists(dir))
            return new List<SessionSummary>();

        var (projectPath, projectName) = DecodeProjectDirName(projectDirName);
        var indexPath = Path.Combine(dir, "sessions-index.json");

        if (File.Exists(indexPath))
        {
            try
            {
                var json = File.ReadAllText(indexPath);
                var index = JsonSerializer.Deserialize<SessionsIndex>(json);
                if (index != null)
                {
                    if (!string.IsNullOrEmpty(index.OriginalPath))
                    {
                        projectPath = index.OriginalPath;
                        projectName = Path.GetFileName(projectPath);
                    }

                    return index.Entries
                        .Where(e => !e.IsSidechain)
                        .Select(e => new SessionSummary
                        {
                            SessionId = e.SessionId,
                            ProjectName = projectName,
                            ProjectDirName = projectDirName,
                            FilePath = e.FullPath ?? Path.Combine(dir, $"{e.SessionId}.jsonl"),
                            Created = ParseTimestamp(e.Created),
                            Modified = ParseTimestamp(e.Modified),
                            MessageCount = e.MessageCount,
                            FirstPrompt = StripIdeTags(e.FirstPrompt ?? ""),
                            GitBranch = e.GitBranch
                        })
                        .OrderByDescending(s => s.Created)
                        .ToList();
                }
            }
            catch (Exception) { }
        }

        // Fallback: scan JSONL files with lightweight metadata extraction
        return Directory.GetFiles(dir, "*.jsonl")
            .Select(f => _parser.ScanMetadata(f, projectName ?? projectDirName, projectDirName))
            .OrderByDescending(s => s.Created ?? s.Modified)
            .ToList();
    }

    public List<SessionSummary> GetAllSessions()
    {
        var projects = GetAllProjects();
        return projects
            .SelectMany(p => GetSessionsForProject(p.DirName))
            .OrderByDescending(s => s.Created ?? s.Modified)
            .ToList();
    }

    public string? GetJsonlFilePath(string projectDirName, string sessionId)
    {
        var path = Path.Combine(_projectsRoot, projectDirName, $"{sessionId}.jsonl");
        return File.Exists(path) ? path : null;
    }

    private void ScanFallbackMetadata(string dir, string projectName, string dirName,
        out int sessionCount, out int totalMessages, out DateTime? lastActivity)
    {
        var files = Directory.GetFiles(dir, "*.jsonl");
        sessionCount = files.Length;
        totalMessages = 0;
        lastActivity = null;

        foreach (var f in files)
        {
            try
            {
                var summary = _parser.ScanMetadata(f, projectName, dirName);
                totalMessages += summary.MessageCount;
                var dt = summary.Created ?? summary.Modified;
                if (dt.HasValue && (lastActivity == null || dt.Value > lastActivity.Value))
                    lastActivity = dt.Value;
            }
            catch { }
        }
    }

    private static (string projectPath, string projectName) DecodeProjectDirName(string dirName)
    {
        var parts = dirName.Split('-');
        if (parts.Length >= 3 && parts[0].Length == 1 && parts[1] == "")
        {
            var drive = parts[0];
            var pathParts = parts.Skip(2).ToArray();
            var projectPath = $"{drive}:\\{string.Join("\\", pathParts)}";
            var projectName = pathParts.Length > 0 ? pathParts[^1] : dirName;
            return (projectPath, projectName);
        }

        return (dirName, dirName.Contains('-') ? dirName.Split('-')[^1] : dirName);
    }

    private static DateTime? ParseTimestamp(string? ts)
    {
        if (string.IsNullOrEmpty(ts)) return null;
        if (DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime();
        return null;
    }

    private static readonly Regex IdeTagRegex = new(@"<ide_\w+>.*?</ide_\w+>\s*", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex IdeOpenTagRegex = new(@"<ide_\w+>[^<]*$", RegexOptions.Singleline | RegexOptions.Compiled);

    public static string StripIdeTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = IdeTagRegex.Replace(text, "");
        text = IdeOpenTagRegex.Replace(text, "");
        return text.Trim();
    }
}
