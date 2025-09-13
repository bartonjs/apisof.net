using NuGet.Packaging.Core;
using Terrajobst.UsageCrawling.Collectors;

namespace GenUsageNuGet;

internal class PackageResult
{
    internal PackageIdentity Identity { get; }
    internal CollectionSetResults? Results { get; }

    internal PackageResult(PackageIdentity identity, CollectionSetResults? results)
    {
        Identity = identity;
        Results = results;
    }
}