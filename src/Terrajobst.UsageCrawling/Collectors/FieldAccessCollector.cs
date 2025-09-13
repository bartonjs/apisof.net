using System.Reflection.Metadata;

namespace Terrajobst.UsageCrawling.Collectors;

public sealed class FieldAccessCollector : IncrementalUsageCollector
{
    public override int VersionRequired => 4;

    protected override void CollectFeatures(LibraryReader libraryReader, AssemblyContext assemblyContext, Context context)
    {
        var metadataReader = libraryReader.MetadataReader;

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            foreach (var methodDefHandle in libraryReader.GetTypeDefinition(typeDefHandle).GetMethods())
            {
                foreach (var (opcode, arg) in OpCodeIterator.WalkMethod(libraryReader, methodDefHandle))
                {
                    if (arg.IsNil || arg.Kind != HandleKind.MemberReference)
                    {
                        continue;
                    }

                    var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)arg);
                    var parent = MetadataUtils.GetDocumentationParent(memberRef, metadataReader);

                    if (parent.IsNil || parent.Kind != HandleKind.TypeReference)
                    {
                        continue;
                    }

                    if (memberRef.GetKind() != MemberReferenceKind.Field)
                    {
                        continue;
                    }

                    switch (opcode)
                    {
                        case ILOpCode.Ldsfld:
                        case ILOpCode.Ldfld:
                            ReportReadOrWrite(context, memberRef, metadataReader, isRead: true);
                            break;
                        case ILOpCode.Stsfld:
                        case ILOpCode.Stfld:
                            ReportReadOrWrite(context, memberRef, metadataReader, isRead: false);
                            break;
                    }
                }
            }
        }
    }

    private static void ReportReadOrWrite(Context context, MemberReference memberRef, MetadataReader metadataReader, bool isRead)
    {
        var docId = MetadataUtils.GetDocumentationId(memberRef, metadataReader);

        if (docId is null)
            return;

        var featureUsage = isRead
            ? FeatureUsage.ForFieldRead(docId)
            : FeatureUsage.ForFieldWrite(docId);

        context.Report(featureUsage);
    }
}