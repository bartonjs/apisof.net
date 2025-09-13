using System.Collections.Concurrent;
using System.Text;
using GenUsageNuGet.Infra;
using Mono.Options;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Terrajobst.ApiCatalog.ActionsRunner;
using Terrajobst.UsageCrawling;
using Terrajobst.UsageCrawling.Collectors;

namespace GenUsageNuGet;

internal sealed class CrawlOneMain : ConsoleCommand
{
    private string? _packageId;
    private NuGetVersion? _packageVersion;
    private bool _verbose;

    public override string Name => "crawl-one";

    public override string Description => "Crawls one package";

    private static void Trace(string message)
    {
        IO.Trace(message);
    }

    public override void AddOptions(OptionSet options)
    {
        options.Add("n=", "{Name} of the package to crawl", v => _packageId = v);
        options.Add("v=", "{Version} of the package to crawl", v => _packageVersion = NuGetVersion.Parse(v));
        options.Add("a", "Show {all} scraped data.", v => _verbose = true);
    }

    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (_packageId is null || _packageVersion is null)
        {
            Trace("One or both of package name and version are missing.");
            return;
        }

        AppContext.SetData("GCHeapHardLimit", (ulong)4 * 1_024 * 1_024 * 1_024);
        GC.RefreshMemoryLimit();

        Trace("Starting...");
        CollectionSetResults results;

        try
        {
            results = await PackageCrawler.CrawlAsync(
                NuGetFeed.NuGetOrg,
                new PackageIdentity(_packageId, _packageVersion),
                cancellationToken);

        }
        catch (Exception ex)
        {
            Trace($"Error while crawling {_packageId} {_packageVersion}: {ex}");
            return;
        }

        if (results.FeatureSets.Count == 0)
        {
            Trace($"No results found for {_packageId} {_packageVersion}");
            return;
        }

        StringBuilder output = new();

        foreach (var featureSet in results.FeatureSets)
        {
            output.Append($"{featureSet.Features.Count} features from {featureSet.Version}");

            if (_verbose)
            {
                output.AppendLine(":");

                foreach (var feature in featureSet.Features.Order())
                {
                    output.AppendLine($"  {feature:N}");
                }
            }
            else
            {
                output.AppendLine();
            }
        }

        Trace(output.ToString());
    }
}