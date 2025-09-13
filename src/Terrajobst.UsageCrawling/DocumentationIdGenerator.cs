using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Terrajobst.UsageCrawling
{
    internal sealed class DocumentationIdGenerator :
        ISignatureTypeProvider<string, object?>
    {
        internal static DocumentationIdGenerator Instance { get; } = new();

        public string GetSZArrayType(string elementType)
        {
            return elementType + "[]";
        }

        public string GetArrayType(string elementType, ArrayShape shape)
        {
            return elementType + "[" + new string(',', shape.Rank - 1) + "]";
        }

        public string GetByReferenceType(string elementType)
        {
            return elementType + "@";
        }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            return genericType + "{" + string.Join(",", typeArguments) + "}";
        }

        public string GetPointerType(string elementType)
        {
            return elementType + "*";
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return "System." + typeCode.ToString();
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var typeDef = reader.GetTypeDefinition(handle);

            if (typeDef.IsNested)
            {
                var declaringType = GetTypeFromDefinition(reader, typeDef.GetDeclaringType(), rawTypeKind);
                return declaringType + "." + reader.GetString(typeDef.Name);
            }

            var name = reader.GetString(typeDef.Name);

            if (typeDef.Namespace.IsNil)
                return name;

            return reader.GetString(typeDef.Namespace) + "." + name;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var typeRef = reader.GetTypeReference(handle);
            if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                var declaringType = GetTypeFromReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope, rawTypeKind);
                return declaringType + "." + reader.GetString(typeRef.Name);
            }

            var name = reader.GetString(typeRef.Name);

            var backtick = name.IndexOf('`');
            
            if (backtick > 0)
            {
                name = name.Substring(0, backtick);
            }

            name = name.Replace('+', '.');

            if (typeRef.Namespace.IsNil)
                return name;

            return reader.GetString(typeRef.Namespace) + "." + name;
        }

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            return "function " + signature.ReturnType + " (" + string.Join(",", signature.ParameterTypes) + ")";
        }

        public string GetGenericMethodParameter(object? genericContext, int index)
        {
            return "``" + index;
        }

        public string GetGenericTypeParameter(object? genericContext, int index)
        {
            return "`" + index;
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            return unmodifiedType;
        }

        public string GetPinnedType(string elementType)
        {
            return elementType + "&";
        }

        public string GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            var typeSpec = reader.GetTypeSpecification(handle);
            return typeSpec.DecodeSignature(this, genericContext);
        }
    }
}
