using System.Reflection;
using System.Reflection.Metadata;

namespace Terrajobst.UsageCrawling.Collectors;

public sealed class DefaultInterfaceImplementationCollector : IncrementalUsageCollector
{
    public override int VersionRequired => 4;

    protected override void CollectFeatures(LibraryReader libraryReader, AssemblyContext assemblyContext, Context context)
    {
        var metadataReader = libraryReader.MetadataReader;

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);

            if ((typeDef.Attributes & TypeAttributes.Interface) == 0)
            {
                continue;
            }

            foreach (var methodImplHandle in typeDef.GetMethodImplementations())
            {
                var methodImpl = metadataReader.GetMethodImplementation(methodImplHandle);

                var declHandle = methodImpl.MethodDeclaration;

                if (declHandle.Kind != HandleKind.MemberReference)
                {
                    continue;
                }

                var docId = MetadataUtils.GetDocumentationId(
                    metadataReader.GetMemberReference((MemberReferenceHandle)declHandle),
                    metadataReader);

                if (docId is null)
                {
                    continue;
                }

                var key = new ApiKey(docId);
                var metric = FeatureUsage.ForDim(key);
                context.Report(metric);
            }
        }
    }
}
