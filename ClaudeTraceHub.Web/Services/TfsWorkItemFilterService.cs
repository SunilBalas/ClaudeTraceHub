using ClaudeTraceHub.Web.Models;
using Microsoft.Extensions.Caching.Memory;

namespace ClaudeTraceHub.Web.Services;

public class TfsWorkItemFilterService
{
    private readonly ClaudeDataDiscoveryService _discoveryService;
    private readonly AzureDevOpsService _azureDevOpsService;
    private readonly IMemoryCache _cache;
    private const string CachePrefix = "tfs_filter:";

    public TfsWorkItemFilterService(
        ClaudeDataDiscoveryService discoveryService,
        AzureDevOpsService azureDevOpsService,
        IMemoryCache cache)
    {
        _discoveryService = discoveryService;
        _azureDevOpsService = azureDevOpsService;
        _cache = cache;
    }

    public bool IsConfigured => _azureDevOpsService.IsConfigured;

    public List<BranchSessionGroup> GetAllBranchGroups()
    {
        return _discoveryService.GetAllSessions()
            .Where(s => !string.IsNullOrEmpty(s.GitBranch))
            .GroupBy(s => s.GitBranch!)
            .Select(g => new BranchSessionGroup
            {
                BranchName = g.Key,
                Sessions = g.OrderByDescending(s => s.Created ?? s.Modified).ToList()
            })
            .OrderByDescending(g => g.Sessions.Max(s => s.Created ?? s.Modified ?? DateTime.MinValue))
            .ToList();
    }

    public List<SessionSummary> GetSessionsWithoutBranch()
    {
        return _discoveryService.GetAllSessions()
            .Where(s => string.IsNullOrEmpty(s.GitBranch))
            .OrderByDescending(s => s.Created ?? s.Modified)
            .ToList();
    }

    public async Task<TfsQueryResult> GetWorkItemsForBranchCachedAsync(string branchName)
    {
        var key = CachePrefix + branchName;
        if (_cache.TryGetValue<TfsQueryResult>(key, out var cached) && cached != null)
            return cached;

        var result = await _azureDevOpsService.GetWorkItemsForBranchAsync(branchName);
        _cache.Set(key, result, TimeSpan.FromMinutes(15));
        return result;
    }

    public async Task<WorkItemScanResult> ScanAllBranchesAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var branchGroups = GetAllBranchGroups();
        var noBranchSessions = GetSessionsWithoutBranch();
        var linkMap = new Dictionary<int, WorkItemConversationLink>();
        var unlinkedSessions = new List<SessionSummary>();
        var scanned = 0;

        foreach (var group in branchGroups)
        {
            ct.ThrowIfCancellationRequested();

            var result = await GetWorkItemsForBranchCachedAsync(group.BranchName);
            scanned++;

            if (result.Success && result.AllWorkItems.Count > 0)
            {
                foreach (var wi in result.AllWorkItems)
                    MergeLink(linkMap, wi, group, result.Path);
            }
            else
            {
                unlinkedSessions.AddRange(group.Sessions);
            }

            progress?.Report(new ScanProgress
            {
                ScannedBranches = scanned,
                TotalBranches = branchGroups.Count,
                WorkItemsFound = linkMap.Count,
                CurrentBranch = group.BranchName
            });
        }

        unlinkedSessions.AddRange(noBranchSessions);

        return new WorkItemScanResult
        {
            WorkItemLinks = linkMap.Values
                .OrderBy(l => l.WorkItem.WorkItemType)
                .ThenBy(l => l.WorkItem.Id)
                .ToList(),
            UnlinkedSessions = unlinkedSessions
                .GroupBy(s => s.SessionId)
                .Select(g => g.First())
                .OrderByDescending(s => s.Created ?? s.Modified)
                .ToList(),
            TotalBranchesScanned = scanned
        };
    }

    private static void MergeLink(Dictionary<int, WorkItemConversationLink> map,
        TfsWorkItem wi, BranchSessionGroup group, DiscoveryPath path)
    {
        if (!map.TryGetValue(wi.Id, out var link))
        {
            link = new WorkItemConversationLink
            {
                WorkItem = wi,
                DiscoveryPath = path
            };
            map[wi.Id] = link;
        }

        foreach (var s in group.Sessions)
        {
            if (!link.LinkedSessions.Any(x => x.SessionId == s.SessionId))
                link.LinkedSessions.Add(s);
        }

        if (!link.BranchNames.Contains(group.BranchName))
            link.BranchNames.Add(group.BranchName);
    }
}
