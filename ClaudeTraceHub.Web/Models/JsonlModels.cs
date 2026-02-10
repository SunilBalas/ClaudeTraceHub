using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTraceHub.Web.Models;

public class JsonlEntry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("parentUuid")]
    public string? ParentUuid { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("message")]
    public JsonlMessage? Message { get; set; }

    [JsonPropertyName("gitBranch")]
    public string? GitBranch { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("isSidechain")]
    public bool IsSidechain { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public class JsonlMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }

    [JsonPropertyName("usage")]
    public JsonlUsage? Usage { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }
}

public class JsonlUsage
{
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; set; }

    [JsonPropertyName("cache_creation_input_tokens")]
    public int? CacheCreationInputTokens { get; set; }

    [JsonPropertyName("cache_read_input_tokens")]
    public int? CacheReadInputTokens { get; set; }
}

public class JsonlContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }
}

public class SessionsIndex
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("entries")]
    public List<SessionIndexEntry> Entries { get; set; } = new();

    [JsonPropertyName("originalPath")]
    public string? OriginalPath { get; set; }
}

public class SessionIndexEntry
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("fullPath")]
    public string? FullPath { get; set; }

    [JsonPropertyName("firstPrompt")]
    public string? FirstPrompt { get; set; }

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }

    [JsonPropertyName("created")]
    public string? Created { get; set; }

    [JsonPropertyName("modified")]
    public string? Modified { get; set; }

    [JsonPropertyName("gitBranch")]
    public string? GitBranch { get; set; }

    [JsonPropertyName("projectPath")]
    public string? ProjectPath { get; set; }

    [JsonPropertyName("isSidechain")]
    public bool IsSidechain { get; set; }
}
