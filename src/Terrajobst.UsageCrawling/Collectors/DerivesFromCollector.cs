using System.Reflection.Metadata;

namespace Terrajobst.UsageCrawling.Collectors;

public sealed class DerivesFromCollector : IncrementalUsageCollector
{
    public override int VersionRequired => 4;

    protected override void CollectFeatures(LibraryReader libraryReader, AssemblyContext assemblyContext, Context context)
    {
        var metadataReader = libraryReader.MetadataReader;

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            ReportContext(context, typeDef.BaseType, metadataReader);

            foreach (var interfaceImplHandle in typeDef.GetInterfaceImplementations())
            {
                var interfaceImpl = metadataReader.GetInterfaceImplementation(interfaceImplHandle);
                ReportContext(context, interfaceImpl.Interface, metadataReader);
            }
        }
    }

    private static void ReportContext(Context context, EntityHandle baseTypeHandle, MetadataReader metadataReader)
    {
        if (baseTypeHandle.IsNil)
        {
            return;
        }

        if (baseTypeHandle.Kind == HandleKind.TypeSpecification)
        {
            baseTypeHandle =
                MetadataUtils.GetTypeSpecTarget((TypeSpecificationHandle)baseTypeHandle, metadataReader);
        }

        if (baseTypeHandle.Kind != HandleKind.TypeReference)
        {
            return;
        }

        var docId = MetadataUtils.GetDocumentationId((TypeReferenceHandle)baseTypeHandle, metadataReader);

        if (docId is not null)
        {
            context.Report(FeatureUsage.ForDerivesFrom(docId));
        }
    }
}