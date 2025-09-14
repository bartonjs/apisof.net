using ApisOfDotNet.Services;
using Microsoft.AspNetCore.Components;

using NuGet.Frameworks;

using Terrajobst.ApiCatalog.Features;

namespace ApisOfDotNet.Pages;

public partial class FeatureUsage
{
    [Inject]
    public required CatalogService CatalogService { get; set; }

    public TargetFrameworkHierarchy Hierarchy { get; set; } = TargetFrameworkHierarchy.Empty;

    public FeatureUsageSource? NuGetSource { get; set; }

    private readonly HashSet<TargetFrameworkNode> _expandedNodes = new();

    protected override void OnInitialized()
    {
        Hierarchy = TargetFrameworkHierarchy.Create(CatalogService.Catalog);
        NuGetSource = CatalogService.UsageData.UsageSources.FirstOrDefault(s => s.Name == "nuget.org");
    }

    private IReadOnlyList<(FeatureUsageSource Source, IReadOnlyList<(FeatureDefinition Feature, int HitCount)> Usages)> GetUsages()
    {
        var usages = new List<(FeatureUsageSource Source, FeatureDefinition Feature, int HitCount)>();

        var usageData = CatalogService.UsageData;

        foreach (var feature in FeatureDefinition.GlobalFeatures)
        {
            var featureId = feature.FeatureId;
            foreach (var (usageSource, hitCount) in usageData.GetUsage(featureId))
                usages.Add((usageSource, feature, hitCount));
        }

        return usages.GroupBy(u => u.Source)
                     .Select(g => (g.Key, (IReadOnlyList<(FeatureDefinition, int)>)g.Select(t => (t.Feature, t.HitCount)).ToArray()))
                     .ToArray();
    }

    private bool IsExpanded(TargetFrameworkNode node)
    {
        return _expandedNodes.Contains(node);
    }

    private void ExpandNode(TargetFrameworkNode node, bool expand = true)
    {
        if (expand)
            _expandedNodes.Add(node);
        else
            _expandedNodes.Remove(node);

        StateHasChanged();
    }
}