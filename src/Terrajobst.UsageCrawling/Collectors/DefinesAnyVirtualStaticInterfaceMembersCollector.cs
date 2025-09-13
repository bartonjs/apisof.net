using System.Reflection;

namespace Terrajobst.UsageCrawling.Collectors;

public sealed class DefinesAnyVirtualStaticInterfaceMembersCollector : IncrementalUsageCollector
{
    public override int VersionRequired => 2;

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

            foreach (var methodDefHandle in typeDef.GetMethods())
            {
                var methodDef = metadataReader.GetMethodDefinition(methodDefHandle);

                const MethodAttributes Mask = MethodAttributes.Static | MethodAttributes.Virtual;

                if ((methodDef.Attributes & Mask) != Mask)
                {
                    continue;
                }

                context.Report(FeatureUsage.DefinesAnyVirtualStaticInterfaceMembers);
                return;
            }
        }
    }
}