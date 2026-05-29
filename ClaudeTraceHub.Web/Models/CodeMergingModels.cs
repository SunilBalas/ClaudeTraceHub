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

    /// <summary>Azure DevOps work item URL, opened in a new tab from the ID column.</summary>
    public string WorkItemUrl { get; set; } = "";

    public List<PullRequestInfo> PullRequests { get; set; } = new();

    public int TotalPRs => PullRequests.Count;
    public int TotalCommits => PullRequests.Sum(pr => pr.Commits.Count);

    /// <summary>
    /// Earliest commit date across all linked PRs. Null when no commits are linked.
    /// Used to chronologically order work items by when development first started on them.
    /// </summary>
    public DateTime? FirstCommitDate
    {
        get
        {
            DateTime? min = null;
            foreach (var pr in PullRequests)
            {
                foreach (var c in pr.Commits)
                {
                    if (min == null || c.AuthorDate < min) min = c.AuthorDate;
                }
            }
            return min;
        }
    }

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

    /// <summary>
    /// Earliest commit date on this PR, falling back to the PR creation date when no commits are loaded.
    /// Used to order PRs chronologically inside a requirement.
    /// </summary>
    public DateTime FirstActivityDate =>
        Commits.Count > 0 ? Commits.Min(c => c.AuthorDate) : CreationDate;
}

/// <summary>
/// A single commit associated with a Pull Request.
/// </summary>
public class CommitInfo
{
    /// <summary>
    /// Prefix of auto-generated merge commits produced when a branch is synced with its
    /// remote (e.g. "Merge remote-tracking branch 'origin/main' into ..."). These cannot be
    /// cherry-picked/merged independently to a release branch, so they are treated as optional
    /// when deciding whether a PR has been fully merged to a target branch.
    /// </summary>
    public const string MergeTrackingPrefix = "Merge remote-tracking branch";

    public string CommitId { get; set; } = "";
    public string ShortId => CommitId.Length >= 8 ? CommitId[..8] : CommitId;
    public string Message { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime AuthorDate { get; set; }

    /// <summary>Azure DevOps commit URL, opened in a new tab from the commit SHA cell.</summary>
    public string CommitUrl { get; set; } = "";

    /// <summary>
    /// True when this is an auto-generated "Merge remote-tracking branch …" commit. Such commits
    /// are optional for merge verification — a PR is still considered merged when every other
    /// (non-merge-tracking) commit is present on the target branch.
    /// </summary>
    public bool IsMergeTrackingCommit =>
        Message.TrimStart().StartsWith(MergeTrackingPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Per-commit merge status against the verification target branch.
    /// null = not yet verified (or no target branch selected).
    /// </summary>
    public bool? IsMergedToTargetBranch { get; set; }
}
