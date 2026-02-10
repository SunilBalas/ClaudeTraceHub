using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeTraceHub.Web.Models;

namespace ClaudeTraceHub.Web.Services;

public class JsonlParserService
{
    private static readonly HashSet<string> SkipTypes = new() { "queue-operation", "file-history-snapshot", "progress", "system" };

    private static readonly Regex IdeTagRegex = new(@"<ide_\w+>.*?</ide_\w+>\s*", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex IdeOpenTagRegex = new(@"<ide_\w+>[^<]*$", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex SystemReminderRegex = new(@"<system-reminder>.*?</system-reminder>\s*", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Lightweight scan: extracts only metadata (timestamps, message count, first prompt, git branch)
    /// without parsing full message content or tool usages.
    /// </summary>
    public SessionSummary ScanMetadata(string filePath, string projectName, string projectDirName)
    {
        var summary = new SessionSummary
        {
            SessionId = Path.GetFileNameWithoutExtension(filePath),
            ProjectName = projectName,
            ProjectDirName = projectDirName,
            FilePath = filePath,
            Modified = File.GetLastWriteTimeUtc(filePath)
        };

        if (!File.Exists(filePath)) return summary;

        DateTime? firstTimestamp = null;
        DateTime? lastTimestamp = null;
        int messageCount = 0;
        string? firstPrompt = null;
        string? gitBranch = null;

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                if (type == null || SkipTypes.Contains(type)) continue;

                // Timestamp
                var tsStr = root.TryGetProperty("timestamp", out var tsProp) ? tsProp.GetString() : null;
                if (!string.IsNullOrEmpty(tsStr) &&
                    DateTime.TryParse(tsStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                {
                    var local = ts.ToLocalTime();
                    firstTimestamp ??= local;
                    lastTimestamp = local;
                }

                // Git branch
                if (gitBranch == null && root.TryGetProperty("gitBranch", out var brProp) &&
                    brProp.ValueKind == JsonValueKind.String)
                {
                    var br = brProp.GetString();
                    if (!string.IsNullOrEmpty(br)) gitBranch = br;
                }

                // Count user/assistant messages and extract first prompt
                if (type == "user")
                {
                    if (root.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("content", out var content))
                    {
                        var (text, isToolResult) = ExtractUserTextQuick(content);
                        if (!isToolResult)
                        {
                            messageCount++;
                            if (firstPrompt == null && !string.IsNullOrWhiteSpace(text))
                                firstPrompt = text.Length > 200 ? text[..200] : text;
                        }
                    }
                }
                else if (type == "assistant")
                {
                    // Only count unique message IDs
                    if (root.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("stop_reason", out var sr) &&
                        sr.ValueKind == JsonValueKind.String)
                    {
                        // This is the final entry for this assistant message
                        messageCount++;
                    }
                }
            }
            catch (JsonException) { }
        }

        summary.Created = firstTimestamp;
        summary.Modified = lastTimestamp ?? summary.Modified;
        summary.MessageCount = messageCount;
        summary.FirstPrompt = ClaudeDataDiscoveryService.StripIdeTags(firstPrompt ?? "");
        summary.GitBranch = gitBranch;

        return summary;
    }

    /// <summary>
    /// Quick text extraction for metadata scanning — avoids full content parsing.
    /// </summary>
    private (string text, bool isToolResult) ExtractUserTextQuick(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return (StripTags(content.GetString() ?? ""), false);

        if (content.ValueKind != JsonValueKind.Array)
            return ("", false);

        bool hasText = false;
        bool hasToolResult = false;
        string? firstText = null;

        foreach (var elem in content.EnumerateArray())
        {
            var type = elem.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            if (type == "text")
            {
                hasText = true;
                if (firstText == null)
                {
                    var t = elem.TryGetProperty("text", out var tp) ? tp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(t))
                        firstText = StripTags(t);
                }
            }
            else if (type == "tool_result")
            {
                hasToolResult = true;
            }
        }

        if (hasText) return (firstText ?? "", false);
        if (hasToolResult && !hasText) return ("", true);
        return ("", false);
    }

    public Conversation ParseFile(string filePath, string projectName, string projectDirName)
    {
        var conversation = new Conversation
        {
            SessionId = Path.GetFileNameWithoutExtension(filePath),
            ProjectName = projectName,
            ProjectDirName = projectDirName
        };

        if (!File.Exists(filePath))
            return conversation;

        var rawEntries = new List<JsonlEntry>();
        var lineNum = 0;

        foreach (var line in File.ReadLines(filePath))
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entry = JsonSerializer.Deserialize<JsonlEntry>(line);
                if (entry != null)
                    rawEntries.Add(entry);
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        // Separate by type, track ordering
        var orderedMessages = new List<(int order, ConversationMessage msg)>();
        var assistantGroups = new Dictionary<string, List<JsonlEntry>>();
        var assistantGroupOrder = new Dictionary<string, int>();
        var orderCounter = 0;

        foreach (var entry in rawEntries)
        {
            if (SkipTypes.Contains(entry.Type))
                continue;

            if (entry.Type == "user")
            {
                var msg = ParseUserEntry(entry);
                if (msg != null && !msg.IsToolResult)
                {
                    orderedMessages.Add((orderCounter, msg));
                }
                orderCounter++;
            }
            else if (entry.Type == "assistant")
            {
                var msgId = entry.Message?.Id ?? $"_standalone_{orderCounter}";
                if (!assistantGroups.ContainsKey(msgId))
                {
                    assistantGroups[msgId] = new List<JsonlEntry>();
                    assistantGroupOrder[msgId] = orderCounter;
                    orderCounter++;
                }
                assistantGroups[msgId].Add(entry);
            }
        }

        // Merge assistant groups
        foreach (var (msgId, entries) in assistantGroups)
        {
            var msg = MergeAssistantEntries(entries);
            if (msg != null && !string.IsNullOrWhiteSpace(msg.Text))
            {
                orderedMessages.Add((assistantGroupOrder[msgId], msg));
            }
        }

        // Sort by order
        orderedMessages.Sort((a, b) => a.order.CompareTo(b.order));
        conversation.Messages = orderedMessages.Select(m => m.msg).ToList();

        // Set message index and timestamp on tool usages for timeline ordering
        for (int i = 0; i < conversation.Messages.Count; i++)
        {
            foreach (var tool in conversation.Messages[i].ToolUsages)
            {
                tool.MessageIndex = i;
                tool.Timestamp = conversation.Messages[i].Timestamp;
            }
        }

        // Set metadata
        if (conversation.Messages.Count > 0)
        {
            conversation.Created = conversation.Messages[0].Timestamp;
            conversation.Modified = conversation.Messages[^1].Timestamp;

            var firstUser = conversation.Messages.FirstOrDefault(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Text));
            if (firstUser != null)
                conversation.FirstPrompt = firstUser.Text.Length > 200 ? firstUser.Text[..200] : firstUser.Text;
        }

        // Git branch from first entry that has it
        var branchEntry = rawEntries.FirstOrDefault(e => !string.IsNullOrEmpty(e.GitBranch));
        if (branchEntry != null)
            conversation.GitBranch = branchEntry.GitBranch;

        return conversation;
    }

    private ConversationMessage? ParseUserEntry(JsonlEntry entry)
    {
        if (entry.Message?.Content == null) return null;

        var timestamp = ParseTimestamp(entry.Timestamp);
        var (text, isToolResult) = ExtractUserText(entry.Message.Content.Value);

        if (string.IsNullOrWhiteSpace(text) && !isToolResult)
            return null;

        return new ConversationMessage
        {
            Timestamp = timestamp,
            Role = "user",
            Text = text,
            IsToolResult = isToolResult
        };
    }

    private ConversationMessage? MergeAssistantEntries(List<JsonlEntry> entries)
    {
        var textParts = new List<string>();
        var toolUsages = new List<ToolUsageInfo>();
        string? model = null;
        int? maxOutputTokens = null;
        int? maxInputTokens = null;
        DateTime earliest = DateTime.MaxValue;

        foreach (var entry in entries)
        {
            var ts = ParseTimestamp(entry.Timestamp);
            if (ts < earliest) earliest = ts;

            if (entry.Message != null)
            {
                model ??= entry.Message.Model;

                var usage = entry.Message.Usage;
                if (usage?.OutputTokens != null)
                {
                    maxOutputTokens = maxOutputTokens == null
                        ? usage.OutputTokens
                        : Math.Max(maxOutputTokens.Value, usage.OutputTokens.Value);
                }
                if (usage?.InputTokens != null)
                {
                    maxInputTokens = maxInputTokens == null
                        ? usage.InputTokens
                        : Math.Max(maxInputTokens.Value, usage.InputTokens.Value);
                }

                if (entry.Message.Content is JsonElement content)
                {
                    var blocks = ParseContentBlocks(content);
                    foreach (var block in blocks)
                    {
                        if (block.Type == "text" && !string.IsNullOrWhiteSpace(block.Text))
                        {
                            textParts.Add(block.Text.Trim());
                        }
                        else if (block.Type == "tool_use" && !string.IsNullOrEmpty(block.Name))
                        {
                            var change = ExtractFileChange(block.Name, block.Input);
                            toolUsages.Add(new ToolUsageInfo
                            {
                                ToolName = block.Name,
                                Summary = SummarizeToolUse(block.Name, block.Input),
                                FilePath = change.FilePath,
                                FileAction = change.Action,
                                OldContent = change.OldContent,
                                NewContent = change.NewContent,
                                ReplaceAll = change.ReplaceAll
                            });
                        }
                        // Skip "thinking" blocks
                    }
                }
            }
        }

        if (earliest == DateTime.MaxValue)
            earliest = DateTime.UtcNow;

        return new ConversationMessage
        {
            Timestamp = earliest,
            Role = "assistant",
            Text = string.Join("\n\n", textParts),
            Model = model,
            OutputTokens = maxOutputTokens,
            InputTokens = maxInputTokens,
            MessageId = entries[0].Message?.Id,
            ToolUsages = toolUsages
        };
    }

    private (string text, bool isToolResult) ExtractUserText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return (StripTags(content.GetString() ?? ""), false);
        }

        if (content.ValueKind != JsonValueKind.Array)
            return ("", false);

        var textParts = new List<string>();
        bool hasText = false;
        bool hasToolResult = false;

        foreach (var elem in content.EnumerateArray())
        {
            var type = elem.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

            if (type == "text")
            {
                hasText = true;
                var text = elem.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
                if (!string.IsNullOrEmpty(text))
                {
                    var cleaned = StripTags(text);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                        textParts.Add(cleaned);
                }
            }
            else if (type == "tool_result")
            {
                hasToolResult = true;
            }
        }

        if (hasText)
            return (string.Join("\n\n", textParts), false);

        if (hasToolResult && !hasText)
            return ("", true);

        return ("", false);
    }

    private List<JsonlContentBlock> ParseContentBlocks(JsonElement content)
    {
        var blocks = new List<JsonlContentBlock>();

        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in content.EnumerateArray())
            {
                try
                {
                    var block = JsonSerializer.Deserialize<JsonlContentBlock>(elem.GetRawText());
                    if (block != null)
                        blocks.Add(block);
                }
                catch { }
            }
        }

        return blocks;
    }

    private string StripTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = IdeTagRegex.Replace(text, "");
        text = IdeOpenTagRegex.Replace(text, "");
        text = SystemReminderRegex.Replace(text, "");
        return text.Trim();
    }

    private static string SummarizeToolUse(string toolName, JsonElement? input)
    {
        if (input == null || input.Value.ValueKind != JsonValueKind.Object)
            return "";

        var obj = input.Value;

        return toolName switch
        {
            "Read" or "Write" or "Edit" => GetStringProp(obj, "file_path"),
            "Grep" => $"\"{GetStringProp(obj, "pattern")}\" in {GetStringProp(obj, "path")}",
            "Glob" => GetStringProp(obj, "pattern"),
            "Bash" => Truncate(GetStringProp(obj, "command"), 80),
            "Task" => GetStringProp(obj, "description"),
            "WebSearch" => GetStringProp(obj, "query"),
            _ => Truncate(obj.GetRawText(), 80)
        };
    }

    private const int MaxContentLength = 200_000;

    private static FileChangeData ExtractFileChange(string toolName, JsonElement? input)
    {
        var result = new FileChangeData();
        if (input == null || input.Value.ValueKind != JsonValueKind.Object)
            return result;

        var obj = input.Value;
        switch (toolName)
        {
            case "Write":
                result.FilePath = GetStringProp(obj, "file_path");
                result.Action = FileActionType.Created;
                var writeContent = GetStringProp(obj, "content");
                result.NewContent = writeContent.Length > MaxContentLength
                    ? writeContent[..MaxContentLength] + "\n\n--- Content truncated at 200KB ---"
                    : writeContent;
                break;
            case "Edit":
                result.FilePath = GetStringProp(obj, "file_path");
                result.Action = FileActionType.Modified;
                result.OldContent = GetStringProp(obj, "old_string");
                result.NewContent = GetStringProp(obj, "new_string");
                result.ReplaceAll = GetBoolProp(obj, "replace_all");
                break;
            case "Read":
                result.FilePath = GetStringProp(obj, "file_path");
                result.Action = FileActionType.Read;
                break;
        }
        return result;
    }

    private class FileChangeData
    {
        public string? FilePath { get; set; }
        public FileActionType Action { get; set; } = FileActionType.None;
        public string? OldContent { get; set; }
        public string? NewContent { get; set; }
        public bool ReplaceAll { get; set; }
    }

    private static string GetStringProp(JsonElement obj, string prop)
    {
        return obj.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? ""
            : "";
    }

    private static bool GetBoolProp(JsonElement obj, string prop)
    {
        return obj.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.True;
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text[..max] + "...";
    }

    private static DateTime ParseTimestamp(string? ts)
    {
        if (string.IsNullOrEmpty(ts)) return DateTime.Now;
        if (DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime();
        return DateTime.Now;
    }
}
