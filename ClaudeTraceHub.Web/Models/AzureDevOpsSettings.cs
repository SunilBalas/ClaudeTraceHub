namespace ClaudeTraceHub.Web.Models;

public class AzureDevOpsSettings
{
    public string OrganizationUrl { get; set; } = "";
    public string PersonalAccessToken { get; set; } = "";
    public List<string> Projects { get; set; } = new();
    public string ApiVersion { get; set; } = "5.0";

    public List<string> BranchWorkItemPatterns { get; set; } = new()
    {
        @"^(?:feature|bug|hotfix|task|requirement|cr|mgr)/?\s*(\d+)",
        @"(\d{4,})"
    };

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(OrganizationUrl)
        && !string.IsNullOrWhiteSpace(PersonalAccessToken)
        && Projects.Count > 0;
}
