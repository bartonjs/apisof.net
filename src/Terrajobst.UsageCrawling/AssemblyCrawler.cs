using System.Collections.Immutable;
using System.Reflection.Metadata;

using PrimitiveTypeCode = System.Reflection.Metadata.PrimitiveTypeCode;

namespace Terrajobst.UsageCrawling;

public sealed class AssemblyCrawler
{
    private readonly HashSet<ApiKey> _results = new();
    private MetadataReader _metadataReader = null!;
    private SignatureWalker _signatureWalker = null!;
    private int _crawlDepth;

    public CrawlerResults GetResults()
    {
        return new CrawlerResults(_results);
    }

    public void Crawl(LibraryReader libraryReader)
    {
        ThrowIfNull(libraryReader);

        _signatureWalker = new SignatureWalker(this);
        var metadataReader = libraryReader.MetadataReader;
        _metadataReader = metadataReader;
        var assembly = metadataReader.GetAssemblyDefinition();
        CrawlAttributes(assembly.GetCustomAttributes());

        foreach (var memberRef in metadataReader.MemberReferences)
        {
            Record(memberRef);
        }

        foreach (var typeRef in metadataReader.TypeReferences)
        {
            Record(typeRef);
        }

        foreach (var typeHandle in metadataReader.TypeDefinitions)
        {
            CrawlType(typeHandle);
        }
    }

    private void CrawlAttributes(CustomAttributeHandleCollection attributes)
    {
        var metadataReader = _metadataReader;

        foreach (var attrHandle in attributes)
        {
            var attr = metadataReader.GetCustomAttribute(attrHandle);

            // The ctor will be recorded from the MemberRef table

            if (attr.Constructor.Kind == HandleKind.MemberReference)
            {
                var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)attr.Constructor);

                if (memberRef.Parent.Kind != HandleKind.TypeReference)
                    continue;

                string? fieldPrefix = null;
                string? propPrefix = null;
                var parent = (TypeReferenceHandle)memberRef.Parent;

                foreach (var namedValue in AttributeValueIterator.EnumerateNamedArguments(metadataReader, attr))
                {
                    string? prefix;

                    switch (namedValue.Kind)
                    {
                        case CustomAttributeNamedArgumentKind.Field:
                            if (fieldPrefix is null)
                            {
                                var parentName = MetadataUtils.GetName(parent, metadataReader);

                                if (parentName is not null)
                                {
                                    fieldPrefix = "F:" + parentName + ".";
                                }
                            }

                            prefix = fieldPrefix;
                            break;
                        case CustomAttributeNamedArgumentKind.Property:
                            if (propPrefix is null)
                            {
                                var parentName = MetadataUtils.GetName(parent, metadataReader);

                                if (parentName is not null)
                                {
                                    propPrefix = "P:" + parentName + ".";
                                }
                            }

                            prefix = propPrefix;
                            break;
                        default:
                            throw new BadImageFormatException();
                    }
                    
                    if (prefix is not null)
                        Record(prefix + namedValue.Name);
                }
            }
        }
    }

    private void CrawlType(TypeDefinitionHandle typeHandle)
    {
        var reader = _metadataReader;
        var type = reader.GetTypeDefinition(typeHandle);

        if (IsIgnored(type))
            return;

        CrawlAttributes(type.GetCustomAttributes());

        // The base type, and interfaces, will have been reported from the typeref table,
        // if appropriate.

        foreach (var fieldHandle in type.GetFields())
        {
            CrawlField(reader.GetFieldDefinition(fieldHandle));
        }

        foreach (var methodDefHandle in type.GetMethods())
        {
            CrawlMethod(reader.GetMethodDefinition(methodDefHandle));
        }

        foreach (var propHandle in type.GetProperties())
        {
            CrawlProperty(reader.GetPropertyDefinition(propHandle));
        }

        foreach (var eventHandle in type.GetEvents())
        {
            CrawlEvent(reader.GetEventDefinition(eventHandle));
        }

        foreach (var nestedHandle in type.GetNestedTypes())
        {
            CrawlType(nestedHandle);
        }
    }

    private void CrawlMethod(MethodDefinitionHandle methodDefHandle, bool skipAttributes = false)
    {
        CrawlMethod(_metadataReader.GetMethodDefinition(methodDefHandle), skipAttributes);
    }

    private void CrawlMethod(MethodDefinition methodDef, bool skipAttributes = false)
    {
        if (!skipAttributes)
        {
            CrawlAttributes(methodDef.GetCustomAttributes());
        }
    }

    private void CrawlField(FieldDefinition field)
    {
        using var _ = DepthGuard.Enter(this);
        CrawlAttributes(field.GetCustomAttributes());
        field.DecodeSignature(_signatureWalker, genericContext: null);
    }

    private void CrawlProperty(PropertyDefinition p)
    {
        using var _ = DepthGuard.Enter(this);
        CrawlAttributes(p.GetCustomAttributes());
        p.DecodeSignature(_signatureWalker, genericContext: null);

        var accessors = p.GetAccessors();
        CrawlMethod(accessors.Getter);
        CrawlMethod(accessors.Setter);

        foreach (var otherHandle in accessors.Others)
        {
            CrawlMethod(otherHandle);
        }
    }

    private void CrawlEvent(EventDefinition e)
    {
        using var _ = DepthGuard.Enter(this);
        CrawlAttributes(e.GetCustomAttributes());
        Record(e.Type);

        var accessors = e.GetAccessors();
        CrawlMethod(accessors.Adder);
        CrawlMethod(accessors.Remover);

        foreach (var otherHandle in accessors.Others)
        {
            CrawlMethod(otherHandle);
        }
    }

    private bool IsIgnored(TypeDefinition type)
    {
        var metadataReader = _metadataReader;

        foreach (var attrHandle in type.GetCustomAttributes())
        {
            if (MetadataUtils.IsNamed(attrHandle, metadataReader, "Microsoft.CodeAnalysis", "EmbeddedAttribute"))
                return true;
        }

        return false;
    }

    private void Record(TypeReferenceHandle typeHandle)
    {
        var metadataReader = _metadataReader;
        var typeRef = metadataReader.GetTypeReference(typeHandle);
        Record(MetadataUtils.GetDocumentationId(typeRef, metadataReader));
    }

    private void Record(EntityHandle entityHandle)
    {
        using var _ = DepthGuard.Enter(this);
        switch (entityHandle.Kind)
        {
            case HandleKind.TypeReference:
                Record((TypeReferenceHandle)entityHandle);
                break;
            case HandleKind.MethodDefinition:
                CrawlMethod((MethodDefinitionHandle)entityHandle, skipAttributes: true);
                break;
            case HandleKind.MemberReference:
                Record((MemberReferenceHandle)entityHandle);
                break;
            case HandleKind.TypeSpecification:
                Record((TypeSpecificationHandle)entityHandle);
                break;
        }
    }

    private void Record(MemberReferenceHandle memberRefHandle)
    {
        var memberRef = _metadataReader.GetMemberReference(memberRefHandle);
        
        if (memberRef.Parent.Kind == HandleKind.TypeDefinition)
            return;

        // Parent will have been recorded from the typeref.
        Record(MetadataUtils.GetDocumentationId(memberRef, _metadataReader));
    }

    private void Record(TypeSpecificationHandle typeSpecHandle)
    {
        var typeSpec = _metadataReader.GetTypeSpecification(typeSpecHandle);
        CrawlAttributes(typeSpec.GetCustomAttributes());

        var typeSig = typeSpec.DecodeSignature(_signatureWalker, null);

        if (typeSig.Kind != HandleKind.TypeSpecification)
            Record(typeSig);
    }

    private void Record(string? documentationId)
    {
        if (string.IsNullOrEmpty(documentationId))
            return;

        var key = new ApiKey(documentationId);
        _results.Add(key);
    }

    private sealed class SignatureWalker :
        ISignatureTypeProvider<EntityHandle, object?>
    {
        private readonly AssemblyCrawler _crawler;

        internal SignatureWalker(AssemblyCrawler crawler)
        {
            _crawler = crawler;
        }

        public EntityHandle GetSZArrayType(EntityHandle elementType)
        {
            _crawler.Record(elementType);
            return elementType;
        }

        public EntityHandle GetArrayType(EntityHandle elementType, ArrayShape shape)
        {
            _crawler.Record(elementType);
            return elementType;
        }

        public EntityHandle GetByReferenceType(EntityHandle elementType)
        {
            _crawler.Record(elementType);
            return elementType;
        }

        public EntityHandle GetGenericInstantiation(EntityHandle genericType, ImmutableArray<EntityHandle> typeArguments)
        {
            _crawler.Record(genericType);

            foreach (var typeArg in typeArguments)
                _crawler.Record(typeArg);

            return genericType;
        }

        public EntityHandle GetPointerType(EntityHandle elementType)
        {
            _crawler.Record(elementType);
            return elementType;
        }

        public EntityHandle GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            if (typeCode >= PrimitiveTypeCode.Void &&
                typeCode <= PrimitiveTypeCode.Object)
            {
                _crawler.Record($"T:System.{typeCode}");
            }

            return default;
        }

        public EntityHandle GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            return handle;
        }

        public EntityHandle GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            _crawler.Record(handle);
            return handle;
        }

        public EntityHandle GetFunctionPointerType(MethodSignature<EntityHandle> signature)
        {
            foreach (var handle in signature.ParameterTypes)
            {
                _crawler.Record(handle);
            }

            return default;
        }

        public EntityHandle GetGenericMethodParameter(object? genericContext, int index)
        {
            return default;
        }

        public EntityHandle GetGenericTypeParameter(object? genericContext, int index)
        {
            return default;
        }

        public EntityHandle GetModifiedType(EntityHandle modifier, EntityHandle unmodifiedType, bool isRequired)
        {
            _crawler.Record(unmodifiedType);
            return unmodifiedType;
        }

        public EntityHandle GetPinnedType(EntityHandle elementType)
        {
            _crawler.Record(elementType);
            return elementType;
        }

        public EntityHandle GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            return handle;
        }
    }

    private struct DepthGuard : IDisposable
    {
        private AssemblyCrawler _crawler;

        public static DepthGuard Enter(AssemblyCrawler crawler)
        {
            if (crawler._crawlDepth > 100)
                throw new NotSupportedException("Maximum recursion depth exceeded.");

            crawler._crawlDepth++;

            return new DepthGuard
            {
                _crawler = crawler,
            };
        }

        public void Dispose()
        {
            _crawler._crawlDepth--;
        }
    }
}
