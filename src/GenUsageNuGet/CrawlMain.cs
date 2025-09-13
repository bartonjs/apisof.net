using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using GenUsageNuGet.Infra;
using GenUsagePlanner;
using Microsoft.Extensions.Hosting;
using Mono.Options;
using NuGet.Packaging.Core;
using Terrajobst.ApiCatalog;
using Terrajobst.ApiCatalog.ActionsRunner;
using Terrajobst.UsageCrawling.Collectors;
using Terrajobst.UsageCrawling.Storage;

namespace GenUsageNuGet;

internal sealed class CrawlMain : ConsoleCommand
{
    private static int s_workerStartedCount;

    private readonly IHostEnvironment _hostEnvironment;
    private readonly ScratchFileProvider _scratchFileProvider;
    private readonly ApisOfDotNetStore _store;
    private readonly GitHubActionsSummaryTable _summaryTable;

    private int _workerCount = 10;
    private int _memLimitGb = 40;
    private bool _forceUpload;

    public CrawlMain(
        IHostEnvironment hostEnvironment,
        ScratchFileProvider scratchFileProvider,
        ApisOfDotNetStore store,
        GitHubActionsSummaryTable summaryTable)
    {
        ThrowIfNull(hostEnvironment);
        ThrowIfNull(scratchFileProvider);
        ThrowIfNull(store);
        ThrowIfNull(summaryTable);

        _hostEnvironment = hostEnvironment;
        _scratchFileProvider = scratchFileProvider;
        _store = store;
        _summaryTable = summaryTable;
    }
    
    public override string Name => "crawl";

    public override string Description => "Starts the crawling";

    private static void Trace(string message)
    {
        IO.Trace(message);
    }

    public override void AddOptions(OptionSet options)
    {
        options.Add("w=", $"Number of {{worker}} threads (default {_workerCount})", v => _workerCount = int.Parse(v));
        options.Add("m=", $"{{Memory}} limit, in GB (default {_memLimitGb}", (int v) => _memLimitGb = v);
        options.Add("u", "{Upload}, even in development mode.", v => _forceUpload = true);
    }

    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        AppContext.SetData("GCHeapHardLimit", (ulong)_memLimitGb * 1_024 * 1_024 * 1_024);
        GC.RefreshMemoryLimit();

        var apiCatalogPath = _scratchFileProvider.GetScratchFilePath("apicatalog.dat");
        var databasePath = _scratchFileProvider.GetScratchFilePath("usages-nuget.db");
        var usagesPath = _scratchFileProvider.GetScratchFilePath("usages-nuget.tsv");

        if (_forceUpload)
            Trace("Force upload enabled, will upload even in development mode.");

        Trace("Downloading API catalog...");
        await _store.DownloadApiCatalogAsync(apiCatalogPath);

        Trace("Loading API catalog...");
        var apiCatalog = await ApiCatalogModel.LoadAsync(apiCatalogPath);

        Trace("Downloading previously indexed usages...");
        await _store.DownloadNuGetUsageDatabaseAsync(databasePath);

        Trace("Loading existing database");
        using var usageDatabase = await NuGetUsageDatabase.OpenOrCreateAsync(databasePath);

        Trace("Discovering existing packages...");

        var packagesWithVersions = (await usageDatabase.GetReferenceUnitsAsync()).ToArray();

        Trace("Discovering latest packages...");

        var stopwatch = Stopwatch.StartNew();
        (IReadOnlyList<PackageIdentity> packages, var incremental) = await IO.LoadPackageQueueAsync(cancellationToken);

        Trace($"Finished package discovery. Took {stopwatch.Elapsed}");
        _summaryTable.AppendNumber("#Packages (Latest Stable & Preview)", packages.Count);

        var indexedPackages = new HashSet<PackageIdentity>(packagesWithVersions.Select(p => p.ReferenceUnit));
        var currentPackages = new HashSet<PackageIdentity>(packages);

        var packagesToBeDeleted = incremental ? [] : indexedPackages.Where(p => !currentPackages.Contains(p)).ToArray();
        var packagesToBeIndexed = currentPackages.Where(p => !indexedPackages.Contains(p)).ToArray();
        var packagesToBeReIndexed = packagesWithVersions.Where(pv => pv.CollectorVersion < UsageCollectorSet.CurrentVersion)
            .Where(pv => !packagesToBeDeleted.Contains(pv.ReferenceUnit))
            .Select(pv => pv.ReferenceUnit)
            .ToArray();
        var packagesToBeCrawled = packagesToBeIndexed.Concat(packagesToBeReIndexed).ToArray();

        Trace($"Found {indexedPackages.Count:N0} package(s) in the index.");
        
        if (!incremental)
            Trace($"Found {packagesToBeDeleted.Length:N0} package(s) to remove from the index.");
        
        Trace($"Found {packagesToBeIndexed.Length:N0} package(s) to add to the index.");
        Trace($"Found {packagesToBeReIndexed.Length:N0} package(s) to be re-indexed.");
        Trace($"Found {packagesToBeCrawled.Length:N0} package(s) to be crawled.");

        _summaryTable.AppendNumber("#Packages in index", indexedPackages.Count);

        if (!incremental)
            _summaryTable.AppendNumber("#Packages to be removed", packagesToBeDeleted.Length);

        _summaryTable.AppendNumber("#Packages to be added", packagesToBeIndexed.Length);
        _summaryTable.AppendNumber("#Packages to be re-indexed", packagesToBeReIndexed.Length);
        _summaryTable.AppendNumber("#Packages to be crawled", packagesToBeCrawled.Length);

        Trace("Deleting packages...");
        stopwatch.Restart();
        await usageDatabase.DeleteReferenceUnitsAsync(packagesToBeDeleted);
        Trace($"Finished deleting packages. Took {stopwatch.Elapsed}");

        stopwatch.Restart();
        List<PackageIdentity> skippedPackages = new();
        await CrawlPackagesAsync(usageDatabase, packagesToBeCrawled, skippedPackages, cancellationToken);
        Trace($"Finished crawling. Took {stopwatch.Elapsed}");

        if (skippedPackages.Count > 0)
        {
            await IO.WritePackageQueueAsync(skippedPackages, default);
        }

        Trace("Deleting features without usages...");
        stopwatch.Restart();
        var featuresWithoutUsages = await usageDatabase.DeleteFeaturesWithoutUsagesAsync();

        Trace($"Finished deleting features without usages. Deleted {featuresWithoutUsages:N0} features. Took {stopwatch.Elapsed}");
        _summaryTable.AppendNumber("#Features without usages", featuresWithoutUsages);

        Trace("Vacuuming database...");
        stopwatch.Restart();
        await usageDatabase.VacuumAsync();

        Trace($"Finished vacuuming database. Took {stopwatch.Elapsed}");

        await usageDatabase.CloseAsync();

        Trace("Uploading database...");
        stopwatch.Restart();
        await _store.UploadNuGetUsageDatabaseAsync(databasePath, _forceUpload);
        Trace($"Finished uploading database. Took {stopwatch.Elapsed}");

        if (_hostEnvironment.IsDevelopment())
        {
            Trace("Development Environment: Saving database backup...");
            File.Copy(databasePath, databasePath + ".bak", overwrite: true);

            if (!_forceUpload)
            {
                Trace("Development Environment: Exiting before destructive operations");
                return;
            }
        }

        var databaseSize = new FileInfo(databasePath).Length;
        await usageDatabase.OpenAsync();

        Trace("Getting statistics...");
        stopwatch.Restart();
        var statistics = await usageDatabase.GetStatisticsAsync();
        Trace($"Finished getting statistics. Took {stopwatch.Elapsed}");
        _summaryTable.AppendNumber("#Indexed Features", statistics.FeatureCount);
        _summaryTable.AppendNumber("#Indexed Reference Units", statistics.ReferenceUnitCount);
        _summaryTable.AppendNumber("#Indexed Usages", statistics.UsageCount);
        _summaryTable.AppendBytes("#Index Size", databaseSize);

        Trace("Deleting reference units without usages...");
        stopwatch.Restart();
        var referenceUnitsWithoutUsages = await usageDatabase.DeleteReferenceUnitsWithoutUsages();

        Trace($"Finished deleting reference units without usages. Deleted {referenceUnitsWithoutUsages:N0} reference units. Took {stopwatch.Elapsed}");
        _summaryTable.AppendNumber("#Reference units without usages", referenceUnitsWithoutUsages);

        Trace("Deleting irrelevant features...");
        stopwatch.Restart();
        var irrelevantFeatures = await usageDatabase.DeleteIrrelevantFeaturesAsync(apiCatalog);

        Trace($"Finished deleting irrelevant features. Deleted {irrelevantFeatures:N0} features. Took {stopwatch.Elapsed}");
        _summaryTable.AppendNumber("#Irrelevant features", irrelevantFeatures);

        Trace("Inserting parent features...");
        stopwatch.Restart();
        await usageDatabase.InsertParentsFeaturesAsync(apiCatalog);

        Trace($"Finished inserting parent features. Took {stopwatch.Elapsed}");

        Trace("Exporting usages...");
        stopwatch.Restart();
        await usageDatabase.ExportUsagesAsync(usagesPath);
        Trace($"Finished exporting usages. Took {stopwatch.Elapsed}");

        Trace("Uploading usages...");
        await _store.UploadNuGetUsageResultsAsync(usagesPath, _forceUpload);

        Trace("Deleting local database copy due to destructive feature computation.");
        await usageDatabase.CloseAsync();

        try
        {
            File.Delete(databasePath);
        }
        catch (Exception e)
        {
            Trace($"Delete failed, manually delete {databasePath}: {e}");
        }
    }

    private async Task CrawlPackagesAsync(
        NuGetUsageDatabase usageDatabase,
        IEnumerable<PackageIdentity> packages,
        List<PackageIdentity>? skippedPackages,
        CancellationToken cancellationToken)
    {
        var preProcessed = new HashSet<PackageIdentity>();

        const int BatchSize = 2000;
        var countdown = BatchSize;
        var packageQueue = new ConcurrentQueue<PackageIdentity>(packages);
        var processed = 0;
        var skipCount = 0;
        var remaining = packageQueue.Count;

        var outputChannel = Channel.CreateBounded<PackageResult>(
            new BoundedChannelOptions(_workerCount * 2)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

        Trace($"Spawning {_workerCount} worker(s).");
        var tasks = new Task[_workerCount];

        for (var i = 0; i < _workerCount; i++)
        {
            tasks[i] = CrawlPackageWorkerAsync(packageQueue, preProcessed, outputChannel, cancellationToken);
        }

        var closeTask = Task.WhenAll(tasks).ContinueWith(
            static (_, channel) => ((Channel<PackageResult>)channel!).Writer.Complete(),
            outputChannel,
            cancellationToken);

        var reader = outputChannel.Reader;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested || remaining == 0)
                break;

            PackageResult result;

            try
            {
                if (!await reader.WaitToReadAsync(cancellationToken))
                {
                    Trace($"All writers have exited.");
                    break;
                }

                result = await reader.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Trace("Exiting gracefully with work unfinished.");
                break;
            }

            countdown--;
            processed++;
            remaining--;

            if (result.Results is not null)
            {
                await usageDatabase.DeleteReferenceUnitsAsync([result.Identity]);
                await usageDatabase.AddReferenceUnitAsync(result.Identity, UsageCollectorSet.CurrentVersion);

                foreach (var featureSet in result.Results.FeatureSets)
                foreach (var feature in featureSet.Features)
                {
                    await usageDatabase.TryAddFeatureAsync(feature, featureSet.Version);
                    await usageDatabase.AddUsageAsync(result.Identity, feature);
                }
            }
            else if (skippedPackages is not null)
            {
                skippedPackages.Add(result.Identity);
                skipCount++;
            }

            if (--countdown == 0)
            {
                countdown = BatchSize;

                Trace($"Processed {processed} packages. {skipCount} skipped, approx {packageQueue.Count} remain.");
            }
        }

        Trace($"Done. Processed {processed} packages. {skipCount} skipped.");
    }

    private static async Task CrawlPackageWorkerAsync(
        ConcurrentQueue<PackageIdentity> packageQueue,
        HashSet<PackageIdentity> preProcessed,
        Channel<PackageResult> outputChannel,
        CancellationToken cancellationToken)
    {
        var workerId = Interlocked.Increment(ref s_workerStartedCount);
        var writer = outputChannel.Writer;

        Trace($"Starting worker {workerId}");

        while (!cancellationToken.IsCancellationRequested && packageQueue.TryDequeue(out var packageId))
        {
            if (preProcessed.Contains(packageId))
            {
                continue;
            }

            try
            {
                var results = await PackageCrawler.CrawlAsync(NuGetFeed.NuGetOrg, packageId, cancellationToken);
                await writer.WriteAsync(new PackageResult(packageId, results), cancellationToken);
            }
            catch (Exception e)
            {
                Trace($"Error while processing {packageId}: {e.Message}");
                await writer.WriteAsync(new PackageResult(packageId, null), cancellationToken);
            }
        }

        Trace($"Worker {workerId} finished");
    }
}