using GenUsageNuGet.Infra;
using GenUsagePlanner;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace GenUsageNuGet;

internal static class IO
{
    private const string PackageListFile = "package_queue.txt";

    private static string? s_packageQueueFile;

    internal static void Trace(string message)
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {message}");
    }

    internal static Task<(List<PackageIdentity> Packages, bool Incremental)> LoadPackageQueueAsync(
        CancellationToken cancellationToken = default)
    {
        var path = (s_packageQueueFile ??= ScratchFileProvider.Instance.GetScratchFilePath(PackageListFile));
        return LoadPackageQueueAsync(path, cancellationToken);
    }

    internal static async Task<(List<PackageIdentity> Packages, bool Incremental)> LoadPackageQueueAsync(
        string packageListPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packageListPath))
        {
            Trace("No package queue, fetching full package list...");
            var packages = await GetAllPackagesAsync(cancellationToken);

            Trace($"Trimming {packages.Count} packages to latest versions...");
            packages = CollapseToLatestStableOrLatestPreview(packages);
            Trace($"Trimmed to {packages.Count} package-versions...");

            await WritePackageQueueAsync(packageListPath, packages, cancellationToken);

            return (new List<PackageIdentity>(packages), false);
        }

        Trace($"Loading package queue from {packageListPath}...");
        var lines = File.ReadLinesAsync(packageListPath, cancellationToken);
        var queue = new List<PackageIdentity>();

        await foreach (var line in lines)
        {
            var parts = line.Split('|');

            if (parts.Length != 2)
                continue;

            var id = parts[0];

            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (!NuGetVersion.TryParse(parts[1], out var version))
                continue;

            var packageId = new PackageIdentity(id, version);
            queue.Add(packageId);
        }

        Trace($"Loaded {queue.Count} packages in the queue.");
        return (queue, true);
    }

    internal static Task WritePackageQueueAsync(
        IEnumerable<PackageIdentity> packages,
        CancellationToken cancellationToken = default)
    {
        var path = (s_packageQueueFile ??= ScratchFileProvider.Instance.GetScratchFilePath(PackageListFile));
        return WritePackageQueueAsync(path, packages, cancellationToken);
    }

    private static async Task WritePackageQueueAsync(
        string packageListPath,
        IEnumerable<PackageIdentity> packages,
        CancellationToken cancellationToken)
    {
        var tmpPath = packageListPath + ".tmp";
        Trace($"Writing package queue to {packageListPath}");

        try
        {
            await File.WriteAllLinesAsync(
                tmpPath,
                packages.Select(p => $"{p.Id}|{p.Version}"),
                cancellationToken);
        }
        catch
        {
            File.Delete(tmpPath);
        }

        File.Move(tmpPath, packageListPath, overwrite: true);
    }

    private static async Task<IReadOnlyList<PackageIdentity>> GetAllPackagesAsync(CancellationToken cancellationToken)
    {
        var result = await NuGetFeed.NuGetOrg.GetAllPackages(cancellationToken: cancellationToken);

        // The NuGet package list crawler allocates a ton of memory because it fans out pretty hard.
        // Let's make sure we're releasing as much memory as can so that the processes we're about
        // to spin up got more memory to play with.

        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        return result;
    }

    private static IReadOnlyList<PackageIdentity> CollapseToLatestStableOrLatestPreview(IEnumerable<PackageIdentity> packages)
    {
        var result = new List<PackageIdentity>();

        foreach (var pg in packages.GroupBy(p => p.Id))
        {
            var latestStable = pg.Where(p => !p.Version.IsPrerelease).MaxBy(p => p.Version);
            var latestPreview = pg.Where(p => p.Version.IsPrerelease).MaxBy(p => p.Version);

            if (latestStable is not null)
            {
                result.Add(latestStable);

                // For now, ignore preview if there's a stable.
                latestPreview = null;
            }

            if (latestPreview is not null)
            {
                if (latestStable is null || latestStable.Version < latestPreview.Version)
                {
                    result.Add(latestPreview);
                }
            }
        }

        return result.ToArray();
    }
}