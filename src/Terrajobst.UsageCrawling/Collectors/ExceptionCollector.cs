using System.Reflection.Metadata;

namespace Terrajobst.UsageCrawling.Collectors;

public sealed class ExceptionCollector : IncrementalUsageCollector
{
    public override int VersionRequired => 4;

    protected override void CollectFeatures(LibraryReader libraryReader, AssemblyContext assemblyContext, Context context)
    {
        var metadataReader = libraryReader.MetadataReader;

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            foreach (var methodDefHandle in libraryReader.GetTypeDefinition(typeDefHandle).GetMethods())
            {
                if (!libraryReader.TryGetMethodBody(methodDefHandle, out var body))
                {
                    continue;
                }

                ILOpCode prevCode = default;
                EntityHandle prevArg = default;

                foreach (var (opcode, arg) in OpCodeIterator.WalkMethod(body))
                {
                    if (opcode == ILOpCode.Throw && prevCode == ILOpCode.Newobj && !prevArg.IsNil)
                    {
                        ReportThrow(context, metadataReader, prevArg);
                    }

                    if (opcode != ILOpCode.Nop)
                    {
                        prevCode = opcode;
                        prevArg = arg;
                    }
                }

                foreach (var exception in body.ExceptionRegions)
                {
                    var catchType = exception.CatchType;

                    if (catchType.IsNil)
                    {
                        continue;
                    }

                    if (catchType.Kind == HandleKind.TypeSpecification)
                    {
                        catchType = MetadataUtils.GetTypeSpecTarget((TypeSpecificationHandle)catchType, metadataReader);
                    }
                    if (catchType.Kind != HandleKind.TypeReference)
                    {
                        continue;
                    }
                    var docId = MetadataUtils.GetDocumentationId(
                        metadataReader.GetTypeReference((TypeReferenceHandle)catchType),
                        metadataReader);

                    if (docId is not null)
                    {
                        context.Report(FeatureUsage.ForExceptionCatch(docId));
                    }
                }
            }
        }

        static void ReportThrow(Context context, MetadataReader metadataReader, EntityHandle prevArg)
        {
            if (prevArg.Kind != HandleKind.MemberReference)
            {
                return;
            }

            var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)prevArg);
            var parent = MetadataUtils.GetDocumentationParent(memberRef, metadataReader);

            if (parent.Kind != HandleKind.TypeReference)
            {
                return;
            }

            if (memberRef.GetKind() != MemberReferenceKind.Method)
            {
                return;
            }

            var docId = MetadataUtils.GetDocumentationId(memberRef, metadataReader);

            if (docId is null)
            {
                return;
            }

            context.Report(FeatureUsage.ForExceptionThrow(docId));
        }
    }
}