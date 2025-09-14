using ApisOfDotNet.Services;
using Microsoft.AspNetCore.Components;
using Terrajobst.ApiCatalog;
using Terrajobst.ApiCatalog.Features;

namespace ApisOfDotNet.Pages;

public partial class TopApis
{
    [Inject]
    public required CatalogService CatalogService { get; set; }

    public FeatureUsageSource? NuGetSource { get; set; }

    protected override void OnInitialized()
    {
        NuGetSource = CatalogService.UsageData.UsageSources.FirstOrDefault(s => s.Name == "nuget.org");
    }

    private IEnumerable<(int Rank, ApiModel Api, int HitCount)> GetData()
    {
        if (NuGetSource is null)
            return [];

        var usageData = CatalogService.UsageData.GetUsage(NuGetSource).
            Select(datum => (Api: GetApi(datum.FeatureId), datum.HitCount)).
            Where(datum => datum.Api is not null).
            Select(datum => (Api: datum.Api.GetValueOrDefault(), datum.HitCount)).
            OrderByDescending(datum => datum.HitCount).
            ThenByDescending(datum => datum.Api.Name).
            Select((datum, i) => (Rank: i + 1, datum.Api, datum.HitCount));

        return usageData.Take(100);
    }

    private ApiModel? GetApi(Guid featureId)
    {
        CatalogService.Catalog.TryGetApiByGuid(featureId, out var model);
        return model;
    }
}