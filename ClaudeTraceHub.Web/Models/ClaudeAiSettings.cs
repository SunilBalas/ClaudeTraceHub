namespace ClaudeTraceHub.Web.Models;

public class ClaudeAiSettings
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-haiku-4-5-20251001";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
