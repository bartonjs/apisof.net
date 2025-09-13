using System.Reflection.Metadata;

namespace Terrajobst.UsageCrawling.Collectors;

public sealed class DefinesAnyRefStructsCollector : IncrementalUsageCollector
{
    public override int VersionRequired => 2;

    protected override void CollectFeatures(LibraryReader libraryReader, AssemblyContext assemblyContext, Context context)
    {
        var metadataReader = libraryReader.MetadataReader;

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);

            foreach (var attrHandle in typeDef.GetCustomAttributes())
            {
                var attr = metadataReader.GetCustomAttribute(attrHandle);

                if (attr.Constructor.Kind != HandleKind.MemberReference)
                    continue;

                var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                var container = memberRef.Parent;
                string attrTypeName;
                string attrTypeNamespace;
                if (container.Kind == HandleKind.TypeReference)
                {
                    var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)container);
                    attrTypeName = metadataReader.GetString(typeRef.Name);
                    attrTypeNamespace = metadataReader.GetString(typeRef.Namespace);
                }
                else if (container.Kind == HandleKind.TypeDefinition)
                {
                    var attrTypeDef = metadataReader.GetTypeDefinition((TypeDefinitionHandle)container);
                    attrTypeName = metadataReader.GetString(attrTypeDef.Name);
                    attrTypeNamespace = metadataReader.GetString(attrTypeDef.Namespace);
                }
                else
                {
                    continue;
                }

                if (attrTypeNamespace == "System.Runtime.CompilerServices" &&
                    attrTypeName == "IsByRefLikeAttribute")
                {
                    context.Report(FeatureUsage.DefinesAnyRefStructs);
                }
            }
        }
    }
}