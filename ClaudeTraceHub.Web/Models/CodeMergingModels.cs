namespace ClaudeTraceHub.Web.Models;

/// <summary>
/// Top-level bundle returned by CodeMergingService.
/// </summary>
public class CodeMergingBundle
{
    public List<RequirementMergingRow> Requirements { get; set; } = new();
    public string TeamName { get; set; } = "";
    public string IterationPath { get; set; } = "";
    public int TotalRequirements { get; set; }
    public int TotalPRs { get; set; }
    public int TotalCommits { get; set; }
    public int MergedToTargetCount { get; set; }
    public int NotMergedToTargetCount { get; set; }
    public string? TargetBranchName { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// One row per deliverable requirement (Requirement/User Story/Product Backlog Item).
/// </summary>
public class RequirementMergingRow
{
    public int WorkItemId { get; set; }
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public string AssignedTo { get; set; } = "Unassigned";
    public string WorkItemType { get; set; } = "";
    public List<PullRequestInfo> PullRequests { get; set; } = new();

    public int TotalPRs => PullRequests.Count;
    public int TotalCommits => PullRequests.Sum(pr => pr.Commits.Count);

    public bool? HasAllPRsMergedToTarget =>
        PullRequests.Count == 0 || PullRequests.All(pr => pr.IsMergedToTargetBranch == null)
            ? null
            : PullRequests.Where(pr => pr.IsMergedToTargetBranch != null)
                          .All(pr => pr.IsMergedToTargetBranch == true);
}

/// <summary>
/// Details of a single Pull Request linked to a requirement.
/// </summary>
public class PullRequestInfo
{
    public int PullRequestId { get; set; }
    public string Title { get; set; } = "";
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public string Status { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreationDate { get; set; }
    public DateTime? ClosedDate { get; set; }
    public string RepositoryName { get; set; } = "";
    public string RepositoryId { get; set; } = "";
    public string? LastMergeCommitId { get; set; }
    public string? LastMergeSourceCommitId { get; set; }
    public List<CommitInfo> Commits { get; set; } = new();
    public bool? IsMergedToTargetBranch { get; set; }
    public string PullRequestUrl { get; set; } = "";
}

/// <summary>
/// A single commit associated with a Pull Request.
/// </summary>
public class CommitInfo
{
    public string CommitId { get; set; } = "";
    public string ShortId => CommitId.Length >= 8 ? CommitId[..8] : CommitId;
    public string Message { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime AuthorDate { get; set; }
}
