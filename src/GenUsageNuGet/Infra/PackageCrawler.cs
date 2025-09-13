using System.Reflection.PortableExecutable;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using Terrajobst.UsageCrawling;
using Terrajobst.UsageCrawling.Collectors;

namespace GenUsageNuGet.Infra;

internal static class PackageCrawler
{
    public static async Task<CollectionSetResults> CrawlAsync(NuGetFeed feed, PackageIdentity packageId, CancellationToken cancellationToken)
    {
        var collectorSet = new UsageCollectorSet();
        var reader = await feed.GetPackageAsync(packageId, cancellationToken);

        foreach (var packagePath in reader.GetFiles())
        {
            if (!packagePath.StartsWith("lib/") && !packagePath.StartsWith("runtimes/"))
                continue;

            if (!packagePath.EndsWith(".dll") && !packagePath.EndsWith(".exe"))
                continue;

            var framework = GetFrameworkFromPackagePath(packagePath);

            var assemblyContext = new AssemblyContext
            {
                Package = reader,
                Framework = framework
            };

            await using var assemblyStream = reader.GetStream(packagePath);
            await using var memoryStream = new MemoryStream();
            await assemblyStream.CopyToAsync(memoryStream, cancellationToken);

            if (memoryStream.Length == 0)
            {
                continue;
            }

            try
            {
                memoryStream.Position = 0;

                using (var peReader = new PEReader(memoryStream, PEStreamOptions.LeaveOpen))
                {
                    PEHeaders peHeaders;

                    try
                    {
                        peHeaders = peReader.PEHeaders;
                    }
                    catch (BadImageFormatException)
                    {
                        // Not a PE file
                        continue;
                    }

                    if (peHeaders.MetadataSize > 0)
                    {
                        var libraryReader = new LibraryReader(peReader);

                        if (libraryReader.MetadataReader.IsAssembly)
                            collectorSet.Collect(libraryReader, assemblyContext);
                    }
                }
            }
            catch (BadImageFormatException ex)
            {
                Console.WriteLine($"Warning: Could not read metadata for {packageId} at {packagePath}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Warning: Exception of type {ex.GetType()} while processing {packageId} file {packagePath}, aborting package: {ex.Message}");
                throw;
            }
        }

        return collectorSet.GetResults();
    }

    private static NuGetFramework? GetFrameworkFromPackagePath(string packagePath)
    {
        var segments = packagePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length >= 3)
        {
            try
            {
                var frameworkFolder = segments[1];
                var result = NuGetFramework.ParseFolder(frameworkFolder);
                if (result.IsSpecificFramework)
                    return result;
            }
            catch
            {
                // Ignore
            }
        }

        return null;
    }
}