
namespace Terrajobst.UsageCrawling.Collectors;

public sealed class UsesNullableReferenceTypesCollector : IncrementalUsageCollector
{
    public override int VersionRequired => 3;

    private static string[] s_nullableAttributeNames =
    [
        "NullableAttribute",
        "NullableContextAttribute",
        "NullablePublicOnlyAttribute"
    ];

    protected override void CollectFeatures(LibraryReader libraryReader, AssemblyContext assemblyContext, Context context)
    {
        const string TargetNs = "System.Runtime.CompilerServices";
        var nullableAttributeNames = s_nullableAttributeNames;
        var metadataReader = libraryReader.MetadataReader;

        foreach (var typeRefHandle in metadataReader.TypeReferences)
        {
            if (MetadataUtils.IsNamedAny(typeRefHandle, metadataReader, TargetNs, nullableAttributeNames))
            {
                context.Report(FeatureUsage.UsesNullableReferenceTypes);
                return;
            }
        }

        // .NET 8.0+ has nullable attributes in the framework itself, so wouldn't be a typeDef.
        if (assemblyContext.Framework is not null)
        {
            if (assemblyContext.Framework.Framework is
                NuGet.Frameworks.FrameworkConstants.FrameworkIdentifiers.NetCore or
                NuGet.Frameworks.FrameworkConstants.FrameworkIdentifiers.NetCoreApp)
            {
                if (assemblyContext.Framework.Version >= new Version(8, 0))
                {
                    return;
                }
            }
        }

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            if (MetadataUtils.IsNamedAny(typeDefHandle, metadataReader, TargetNs, nullableAttributeNames))
            {
                context.Report(FeatureUsage.UsesNullableReferenceTypes);
                return;
            }
        }
    }
}