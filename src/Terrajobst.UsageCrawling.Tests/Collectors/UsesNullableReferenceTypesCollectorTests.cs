using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis.CSharp;
using Terrajobst.UsageCrawling.Collectors;
using Terrajobst.UsageCrawling.Tests.Infra;

namespace Terrajobst.UsageCrawling.Tests.Collectors;

public class UsesNullableReferenceTypesCollectorTests : CollectorTest<UsesNullableReferenceTypesCollector>
{
    [Theory]
    [MemberData(nameof(GetNullableModes))]
    public void UsesNullableReferenceTypesCollector_DoesNotReport_WhenDisabledAndNotUsed(NullableMode nullableMode)
    {
        var source =
            """
            #nullable disable
            public class C {
                public void M(string arg) { }
            }
            """;

        Check(nullableMode, source, []);
    }

    [Theory]
    [MemberData(nameof(GetNullableModes))]
    public void UsesNullableReferenceTypesCollector_DoesReport_WhenDisabledAndUsed(NullableMode nullableMode)
    {
        var source =
            """
            #nullable disable
            public class C {
                public void M(string? arg) { }
            }
            """;

        Check(nullableMode, source, [FeatureUsage.UsesNullableReferenceTypes]);
    }

    [Theory]
    [MemberData(nameof(GetNullableModes))]
    public void UsesNullableReferenceTypesCollector_Reports_WhenOn(NullableMode nullableMode)
    {
        var source =
            """
            #nullable enable
            public class C {
                public void M(string? arg) { }
            }
            """;

        Check(nullableMode, source, [FeatureUsage.UsesNullableReferenceTypes]);
    }

    private void Check(NullableMode nullableMode, string source, IEnumerable<FeatureUsage> expectedMetrics)
    {
        using var peReader = new AssemblyBuilder()
            .SetAssembly(source, transformer: c => ApplyNullableMode(c, nullableMode))
            .ToPEReader();

        Check(new LibraryReader(peReader), expectedMetrics);
    }

    public enum NullableMode
    {
        Embedded,
        ReferencedFrameworkTypes
    }

    public static IEnumerable<object[]> GetNullableModes()
    {
        return [
            [NullableMode.Embedded],
            [NullableMode.ReferencedFrameworkTypes]
        ];
    }

    private static CSharpCompilation ApplyNullableMode(CSharpCompilation compilation, NullableMode mode)
    {
        var references = mode == NullableMode.ReferencedFrameworkTypes
            ? Net80.References.All
            : NetStandard20.References.All;

        return compilation.WithReferences(references);
    }
}