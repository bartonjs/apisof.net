using System.Collections.Concurrent;
using System.Net;
using Newtonsoft.Json;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace GenUsageNuGet.Infra;

internal sealed class NuGetFeed
{
    public static NuGetFeed NuGetOrg { get; } = new("https://api.nuget.org/v3/index.json");

    public NuGetFeed(string feedUrl)
    {
        ThrowIfNull(feedUrl);

        FeedUrl = feedUrl;
    }

    public string FeedUrl { get; }

    public async Task<IReadOnlyList<PackageIdentity>> GetAllPackages(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        var sourceRepository = Repository.Factory.GetCoreV3(FeedUrl);
        var serviceIndex = await sourceRepository.GetResourceAsync<ServiceIndexResourceV3>(cancellationToken);
        var catalogIndexUrl = serviceIndex.GetServiceEntryUri("Catalog/3.0.0")?.ToString();

        if (catalogIndexUrl is null)
            throw new InvalidOperationException("This feed doesn't support enumeration");

        const int MaxDegreeOfParallelism = 64;

        ThreadPool.SetMinThreads(MaxDegreeOfParallelism, completionPortThreads: 4);
        ServicePointManager.DefaultConnectionLimit = MaxDegreeOfParallelism;
        ServicePointManager.MaxServicePointIdleTime = 10000;

        using var handler = new HttpClientHandler
        {
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12
        };
        using var httpClient = new HttpClient(handler);

        var indexString = await httpClient.GetStringAsync(catalogIndexUrl, cancellationToken);
        var index = JsonConvert.DeserializeObject<CatalogIndex>(indexString)!;

        // Find all pages in the catalog index.
        var pageItems = new ConcurrentBag<CatalogPage>(index.Items);
        var catalogLeaves = new ConcurrentBag<CatalogLeaf>();
        var deletions = new ConcurrentBag<CatalogLeaf>();

        var fetchLeafsTasks = RunInParallel(async () =>
        {
            while (pageItems.TryTake(out var pageItem))
            {
                if (since is not null && pageItem.CommitTimeStamp < since.Value)
                    continue;

                var retryCount = 3;
            Retry:
                try
                {
                    // Download the catalog page and deserialize it.
                    var pageString = await httpClient.GetStringAsync(pageItem.Url, cancellationToken);
                    var page = JsonConvert.DeserializeObject<CatalogPage>(pageString)!;

                    foreach (var pageLeafItem in page.Items)
                    {
                        //if (!pageLeafItem.Id.StartsWith("Z."))
                        //    continue;

                        if (pageLeafItem.Type == "nuget:PackageDetails")
                            catalogLeaves.Add(pageLeafItem);
                        else if (pageLeafItem.Type == "nuget:PackageDelete")
                            deletions.Add(pageLeafItem);
                    }
                }
                catch (Exception ex) when (retryCount > 0)
                {
                    retryCount--;
                    Console.Error.WriteLine($"error: {ex.Message}, retries left = {retryCount}");
                    goto Retry;
                }
            }
        });

        await Task.WhenAll(fetchLeafsTasks);

        var netResult = new HashSet<PackageIdentity>(
            catalogLeaves.Select(l => new PackageIdentity(l.Id, NuGetVersion.Parse(l.Version))));

        foreach (var deletion in deletions)
        {
            netResult.Remove(new PackageIdentity(deletion.Id, NuGetVersion.Parse(deletion.Version)));
        }

        return netResult
            .OrderBy(p => p.Id)
            .ThenBy(p => p.Version)
            .ToArray();

        static List<Task> RunInParallel(Func<Task> work)
        {
            return Enumerable.Range(0, MaxDegreeOfParallelism)
                .Select(_ => work())
                .ToList();
        }
    }

    public async Task<PackageArchiveReader> GetPackageAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ThrowIfNull(identity);

        var url = await GetPackageUrlAsync(identity);

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        using (CancellationTokenSource cts = new(TimeSpan.FromSeconds(30)))
        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken))
        {
            var nupkgStream = await httpClient.GetStreamAsync(url, linkedCts.Token);
            return new PackageArchiveReader(nupkgStream);
        }
    }

    private async Task<string> GetPackageUrlAsync(PackageIdentity identity)
    {
        ThrowIfNull(identity);

        var sourceRepository = Repository.Factory.GetCoreV3(FeedUrl);
        var serviceIndex = await sourceRepository.GetResourceAsync<ServiceIndexResourceV3>();
        var packageBaseAddress = serviceIndex.GetServiceEntryUri("PackageBaseAddress/3.0.0")?.ToString();

        var id = identity.Id.ToLowerInvariant();
        var version = identity.Version.ToNormalizedString().ToLowerInvariant();
        return $"{packageBaseAddress}{id}/{version}/{id}.{version}.nupkg";
    }

    private abstract class CatalogEntity
    {
        [JsonProperty("@id")]
        public required string Url { get; set; }

        [JsonProperty("commitTimeStamp")]
        public required DateTime CommitTimeStamp { get; set; }
    }

    private sealed class CatalogIndex : CatalogEntity
    {
        public required List<CatalogPage> Items { get; set; }
    }

    private sealed class CatalogPage : CatalogEntity
    {
        public required List<CatalogLeaf> Items { get; set; }
    }

    private sealed class CatalogLeaf : CatalogEntity
    {
        [JsonProperty("nuget:id")]
        public required string Id { get; set; }

        [JsonProperty("nuget:version")]
        public required string Version { get; set; }

        [JsonProperty("@type")]
        public required string Type { get; set; }
    }
}