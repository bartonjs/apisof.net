using System.Reflection.Metadata;

namespace Terrajobst.UsageCrawling.Collectors;

public sealed class DefinesAnyRefFieldsCollector : IncrementalUsageCollector
{
    public override int VersionRequired => 3;

    protected override void CollectFeatures(LibraryReader libraryReader, AssemblyContext assemblyContext, Context context)
    {
        var metadataReader = libraryReader.MetadataReader;

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            foreach (var fieldDefHandle in metadataReader.GetTypeDefinition(typeDefHandle).GetFields())
            {
                var fieldDef = metadataReader.GetFieldDefinition(fieldDefHandle);
                var reader = metadataReader.GetBlobReader(fieldDef.Signature);
                
                if (reader.ReadSignatureHeader().Kind != SignatureKind.Field)
                    continue;
                
                var fieldTypeCode = reader.ReadSignatureTypeCode();

                while (fieldTypeCode == SignatureTypeCode.OptionalModifier ||
                       fieldTypeCode == SignatureTypeCode.RequiredModifier)
                {
                    _ = reader.ReadCompressedInteger();
                    fieldTypeCode = reader.ReadSignatureTypeCode();
                }

                if (fieldTypeCode == SignatureTypeCode.ByReference)
                {
                    context.Report(FeatureUsage.DefinesAnyRefFields);
                }
            }
        }
    }
}