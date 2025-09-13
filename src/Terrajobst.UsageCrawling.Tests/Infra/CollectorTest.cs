using Terrajobst.UsageCrawling.Collectors;

namespace Terrajobst.UsageCrawling.Tests.Infra;

public abstract class CollectorTest<TCollector>
    where TCollector: UsageCollector, new()
{
    protected void Check(string source, string lines, Func<string, FeatureUsage> lineToMetricConverter)
    {
        var metrics = new List<FeatureUsage>();

        foreach (var lineSpan in lines.AsSpan().EnumerateLines())
        {
            var line = lineSpan.Trim().ToString();
            var metric = lineToMetricConverter(line);
            metrics.Add(metric);
        }

        Check(source, metrics);
    }

    protected void Check(string source, IEnumerable<FeatureUsage> expectedUsages)
    {
        ThrowIfNull(source);
        ThrowIfNull(expectedUsages);

        var assembly = new AssemblyBuilder()
            .SetAssembly(source);
            
        using (var peReader = assembly.ToPEReader())
        {
            Check(new LibraryReader(peReader), expectedUsages);
        }
    }

    protected void Check(string dependencySource, string source, IEnumerable<FeatureUsage> expectedUsages)
    {
        ThrowIfNull(dependencySource);
        ThrowIfNull(source);
        ThrowIfNull(expectedUsages);

        var assembly = new AssemblyBuilder()
            .SetAssembly(source)
            .AddDependency(dependencySource);

        using (var reader = assembly.ToPEReader())
        {
            Check(new LibraryReader(reader), expectedUsages);
        }
    }

    protected void Check(LibraryReader libraryReader, IEnumerable<FeatureUsage> expectedUsages)
    {
        Check(libraryReader, AssemblyContext.Empty, expectedUsages);
    }

    protected void Check(LibraryReader libraryReader, AssemblyContext assemblyContext, IEnumerable<FeatureUsage> expectedUsages)
    {
        var collector = new TCollector();
        collector.Collect(libraryReader, assemblyContext);

        var expectedFeaturesOrdered = expectedUsages.OrderBy(u => u.FeatureId);
        var actualResultsOrdered = collector.GetResults().Where(Include).OrderBy(u => u.FeatureId);
        Assert.Equal(expectedFeaturesOrdered, actualResultsOrdered);
    }

    protected virtual bool Include(FeatureUsage metric)
    {
        return true;
    }
}
