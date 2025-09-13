using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;

namespace Terrajobst.UsageCrawling;

internal struct AttributeNamedArgument
{
    public CustomAttributeNamedArgumentKind Kind;
    public string? Name;
}

internal struct AttributeValueIterator
{
    private BlobReader _valReader;

    // Mutable value type, but always copied by value to reset from one read to the next.
    private readonly BlobReader _genericContextReader;
    private bool _abort;

    private AttributeValueIterator(MetadataReader metadataReader, CustomAttribute attr)
    {
        var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
        var signature = memberRef.Signature;

        if (memberRef.Parent.Kind == HandleKind.TypeSpecification)
        {
            GetGenericContextReader(metadataReader, memberRef, ref _genericContextReader);
        }

        _valReader = metadataReader.GetBlobReader(attr.Value);
        ReadHeader(metadataReader.GetBlobReader(signature), metadataReader);
    }

    internal static IEnumerable<AttributeNamedArgument> EnumerateNamedArguments(MetadataReader metadataReader, CustomAttribute attr)
    {
        AttributeValueIterator iter = new(metadataReader, attr);

        if (iter._abort)
            yield break;

        var namedArgCount = iter._valReader.ReadUInt16();

        for (var i = 0; i < namedArgCount; i++)
        {
            var kind = (CustomAttributeNamedArgumentKind)iter._valReader.ReadByte();

            if (kind != CustomAttributeNamedArgumentKind.Field && kind != CustomAttributeNamedArgumentKind.Property)
            {
                throw new BadImageFormatException();
            }

            var typeCode = ReadTaggedType(ref iter._valReader);

            if (typeCode.TypeCode == SerializationTypeCode.Invalid)
            {
                yield break;
            }

            var name = iter._valReader.ReadSerializedString();

            iter.ReadValue(ref iter._valReader, typeCode);

            if (iter._abort)
            {
                yield break;
            }

            yield return new AttributeNamedArgument
            {
                Kind = kind,
                Name = name,
            };
        }
    }

    private void GetGenericContextReader(
        MetadataReader metadataReader,
        MemberReference memberRef,
        ref BlobReader persistedReader)
    {
        var genericContext = metadataReader.GetTypeSpecification((TypeSpecificationHandle)memberRef.Parent).Signature;

        if (!genericContext.IsNil)
        {
            var genericContextReader = metadataReader.GetBlobReader(genericContext);

            if (genericContextReader.ReadSignatureTypeCode() == SignatureTypeCode.GenericTypeInstance)
            {
                var elementType = (SignatureTypeKind)genericContextReader.ReadCompressedInteger();

                if (elementType != SignatureTypeKind.Class && elementType != SignatureTypeKind.ValueType)
                {
                    throw new BadImageFormatException();
                }

                genericContextReader.ReadTypeHandle();

                // The reader is now positioned at the number of generic arguments.
                persistedReader = genericContextReader;
            }
        }
    }

    private void ReadHeader(BlobReader sigReader, MetadataReader metadataReader)
    {
        var reader = sigReader;

        // ECMA-335 II.23.3 Custom attributes
        if (_valReader.ReadInt16() != 1)
            throw new BadImageFormatException();

        var sigHeader = reader.ReadSignatureHeader();

        if (sigHeader.Kind != SignatureKind.Method || sigHeader.IsGeneric)
            throw new BadImageFormatException();

        var paramCount = reader.ReadCompressedInteger();

        if ((uint)paramCount > 0x1FFFFFFF)
            throw new BadImageFormatException();

        var retType = reader.ReadSignatureTypeCode();

        if (retType != SignatureTypeCode.Void)
            throw new BadImageFormatException();

        for (var i = 0; i < paramCount; i++)
        {
            var signatureTypeCode = ReadSignatureType(ref reader, metadataReader, _genericContextReader);

            if (signatureTypeCode.TypeCode == SerializationTypeCode.Invalid)
            {
                _abort = true;
                return;
            }

            ReadValue(ref _valReader, signatureTypeCode);

            if (_abort)
                return;
        }
    }

    private static ComposedTypeCode ReadSignatureType(
        ref BlobReader sigReader,
        MetadataReader metadataReader,
        BlobReader genericContextReader,
        bool isSzElement = false)
    {
        var signatureTypeCode = sigReader.ReadSignatureTypeCode();

        switch (signatureTypeCode)
        {
            case SignatureTypeCode.Boolean:
            case SignatureTypeCode.Char:
            case SignatureTypeCode.SByte:
            case SignatureTypeCode.Byte:
            case SignatureTypeCode.Int16:
            case SignatureTypeCode.UInt16:
            case SignatureTypeCode.Int32:
            case SignatureTypeCode.UInt32:
            case SignatureTypeCode.Int64:
            case SignatureTypeCode.UInt64:
            case SignatureTypeCode.Single:
            case SignatureTypeCode.Double:
            case SignatureTypeCode.String:
                return ComposedTypeCode.FromTypeCode(signatureTypeCode);
            case SignatureTypeCode.Object:
                return ComposedTypeCode.FromTypeCode(SerializationTypeCode.TaggedObject);
            case SignatureTypeCode.TypeHandle:
                var typeHandle = sigReader.ReadTypeHandle();

                if (typeHandle.Kind == HandleKind.TypeReference)
                {
                    var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)typeHandle);

                    if (metadataReader.GetString(typeRef.Namespace) == "System" &&
                        metadataReader.GetString(typeRef.Name) == "Type")
                    {
                        return ComposedTypeCode.FromTypeCode(SerializationTypeCode.Type);
                    }

                    // Otherwise it's an enum, but we need to know the underlying type.
                    var underlyingType = TypeResolver.GetEnumUnderlyingType(metadataReader, typeRef);

                    if (underlyingType != 0)
                    {
                        return ComposedTypeCode.FromTypeCode(underlyingType);
                    }
                }

                return ComposedTypeCode.FromTypeCode(SerializationTypeCode.Invalid);
            case SignatureTypeCode.SZArray:
                if (isSzElement)
                    throw new BadImageFormatException();

                var elementType = ReadSignatureType(ref sigReader, metadataReader, genericContextReader, isSzElement: true);
                return ComposedTypeCode.ForArray(elementType.TypeCode);
            case SignatureTypeCode.GenericTypeParameter:
                var genArgCount = genericContextReader.ReadCompressedInteger();
                var genArgIndex = sigReader.ReadCompressedInteger();

                if (genArgIndex >= genArgCount)
                    throw new BadImageFormatException();

                for (var i = 0; i < genArgIndex; i++)
                {
                    SkipType(ref genericContextReader);
                }

                return ReadSignatureType(ref genericContextReader, metadataReader, default, isSzElement);
            default:
                throw new NotSupportedException($"Unhandled type code: {signatureTypeCode}");
        }
    }

    private void ReadValue(ref BlobReader reader, ComposedTypeCode typeCode)
    {
        if (typeCode.TypeCode == SerializationTypeCode.TaggedObject)
        {
            typeCode = ReadTaggedType(ref reader);
        }

        switch (typeCode.TypeCode)
        {
            case SerializationTypeCode.Invalid:
                _abort = true;
                return;
            case SerializationTypeCode.Boolean:
                reader.ReadByte();
                break;
            case SerializationTypeCode.Char:
                reader.ReadUInt16();
                break;
            case SerializationTypeCode.SByte:
                reader.ReadSByte();
                break;
            case SerializationTypeCode.Byte:
                reader.ReadByte();
                break;
            case SerializationTypeCode.Int16:
                reader.ReadInt16();
                break;
            case SerializationTypeCode.UInt16:
                reader.ReadUInt16();
                break;
            case SerializationTypeCode.Int32:
                reader.ReadInt32();
                break;
            case SerializationTypeCode.UInt32:
                reader.ReadUInt32();
                break;
            case SerializationTypeCode.Int64:
                reader.ReadInt64();
                break;
            case SerializationTypeCode.UInt64:
                reader.ReadUInt64();
                break;
            case SerializationTypeCode.Single:
                reader.ReadSingle();
                break;
            case SerializationTypeCode.Double:
                reader.ReadDouble();
                break;
            case SerializationTypeCode.Type:
            case SerializationTypeCode.String:
                if (reader.TryReadCompressedInteger(out var utf8Length))
                {
                    reader.Offset += utf8Length;
                }
                else
                {
                    // Null string
                    var marker = reader.ReadByte();

                    if (marker != 0xFF)
                        throw new BadImageFormatException();
                }

                break;
            case SerializationTypeCode.SZArray:
                ReadArray(ref reader, typeCode.ElementTypeCode);
                break;
            default:
                throw new NotSupportedException($"Unhandled type code: {typeCode.TypeCode}");
        }
    }

    private void ReadArray(ref BlobReader reader, SerializationTypeCode elementType)
    {
        var count = reader.ReadInt32();

        if (count < -1)
            throw new BadImageFormatException();

        // -1 is null, no data.
        // 0 is empty, no data.
        if (count < 1)
            return;

        for (var i = 0; i < count; i++)
        {
            ReadValue(ref reader, ComposedTypeCode.FromTypeCode(elementType));

            if (_abort)
                return;
        }
    }

    private static ComposedTypeCode ReadTaggedType(ref BlobReader reader, bool isElementType = false)
    {
        var typeCode = reader.ReadSerializationTypeCode();

        switch (typeCode)
        {
            case SerializationTypeCode.Boolean:
            case SerializationTypeCode.SByte:
            case SerializationTypeCode.Byte:
            case SerializationTypeCode.Char:
            case SerializationTypeCode.Int16:
            case SerializationTypeCode.UInt16:
            case SerializationTypeCode.Int32:
            case SerializationTypeCode.UInt32:
            case SerializationTypeCode.Int64:
            case SerializationTypeCode.UInt64:
            case SerializationTypeCode.Single:
            case SerializationTypeCode.Double:
            case SerializationTypeCode.String:
            case SerializationTypeCode.Type:
            case SerializationTypeCode.TaggedObject:
                return ComposedTypeCode.FromTypeCode(typeCode);

            case SerializationTypeCode.SZArray:
                if (isElementType)
                    throw new BadImageFormatException();

                var elementCode = ReadTaggedType(ref reader, isElementType: true);
                return ComposedTypeCode.ForArray(elementCode.TypeCode);

            case SerializationTypeCode.Enum:
                var typeName = reader.ReadSerializedString();
                var elemType = TypeResolver.GetEnumUnderlyingType(typeName);

                if (elemType == 0)
                    return ComposedTypeCode.FromTypeCode(SerializationTypeCode.Invalid);

                return ComposedTypeCode.FromTypeCode(elemType);
            default:
                throw new BadImageFormatException();
        }
    }

    private static void SkipType(ref BlobReader blobReader)
    {
        var typeCode = blobReader.ReadSignatureTypeCode();

        switch (typeCode)
        {
            case SignatureTypeCode.Boolean:
            case SignatureTypeCode.Char:
            case SignatureTypeCode.SByte:
            case SignatureTypeCode.Byte:
            case SignatureTypeCode.Int16:
            case SignatureTypeCode.UInt16:
            case SignatureTypeCode.Int32:
            case SignatureTypeCode.UInt32:
            case SignatureTypeCode.Int64:
            case SignatureTypeCode.UInt64:
            case SignatureTypeCode.Single:
            case SignatureTypeCode.Double:
            case SignatureTypeCode.IntPtr:
            case SignatureTypeCode.UIntPtr:
            case SignatureTypeCode.Object:
            case SignatureTypeCode.String:
            case SignatureTypeCode.Void:
            case SignatureTypeCode.TypedReference:
                return;

            case SignatureTypeCode.Pointer:
            case SignatureTypeCode.ByReference:
            case SignatureTypeCode.Pinned:
            case SignatureTypeCode.SZArray:
                SkipType(ref blobReader);
                return;

            case SignatureTypeCode.FunctionPointer:
                var header = blobReader.ReadSignatureHeader();

                if (header.IsGeneric)
                {
                    // arity
                    blobReader.ReadCompressedInteger();
                }

                var paramCount = blobReader.ReadCompressedInteger();
                SkipType(ref blobReader);

                for (var i = 0; i < paramCount; i++)
                {
                    SkipType(ref blobReader);
                }

                return;

            case SignatureTypeCode.Array:
                SkipType(ref blobReader);

                // rank
                blobReader.ReadCompressedInteger();

                var boundsCount = blobReader.ReadCompressedInteger();

                for (var i = 0; i < boundsCount; i++)
                {
                    blobReader.ReadCompressedInteger();
                }

                var lowerBoundsCount = blobReader.ReadCompressedInteger();

                for (var i = 0; i < lowerBoundsCount; i++)
                {
                    blobReader.ReadCompressedSignedInteger();
                }
                return;

            case SignatureTypeCode.RequiredModifier:
            case SignatureTypeCode.OptionalModifier:
                blobReader.ReadTypeHandle();
                SkipType(ref blobReader);
                return;

            case SignatureTypeCode.GenericTypeInstance:
                SkipType(ref blobReader);
                var count = blobReader.ReadCompressedInteger();

                for (var i = 0; i < count; i++)
                {
                    SkipType(ref blobReader);
                }
                return;

            case SignatureTypeCode.GenericTypeParameter:
                blobReader.ReadCompressedInteger();
                return;

            case SignatureTypeCode.TypeHandle:
                SkipType(ref blobReader);
                return;

            default:
                throw new BadImageFormatException();
        }
    }

    private struct ComposedTypeCode
    {
        public SerializationTypeCode TypeCode;
        public SerializationTypeCode ElementTypeCode;

        public static ComposedTypeCode FromTypeCode(SignatureTypeCode typeCode)
        {
            if (typeCode < SignatureTypeCode.Boolean || typeCode > SignatureTypeCode.String)
                throw new InvalidOperationException($"Can't process SignatureTypeCode {typeCode}");

            return new ComposedTypeCode
            {
                TypeCode = (SerializationTypeCode)typeCode,
                ElementTypeCode = SerializationTypeCode.Invalid,
            };
        }

        public static ComposedTypeCode FromTypeCode(SerializationTypeCode typeCode)
        {
            return new ComposedTypeCode
            {
                TypeCode = typeCode,
                ElementTypeCode = SerializationTypeCode.Invalid,
            };
        }

        public static ComposedTypeCode FromTypeCode(PrimitiveTypeCode typeCode)
        {
            if (typeCode < PrimitiveTypeCode.Boolean || typeCode > PrimitiveTypeCode.String)
                throw new InvalidOperationException($"Can't process PrimitiveTypeCode {typeCode}");

            return new ComposedTypeCode
            {
                TypeCode = (SerializationTypeCode)typeCode,
                ElementTypeCode = SerializationTypeCode.Invalid,
            };
        }

        public static ComposedTypeCode ForArray(SerializationTypeCode elementTypeCode)
        {
            return new ComposedTypeCode
            {
                TypeCode = SerializationTypeCode.SZArray,
                ElementTypeCode = elementTypeCode,
            };
        }
    }

    private static class TypeResolver
    {
        private static readonly AssemblyLoadContext s_alc = new("EnumTokenResolver");

        internal static PrimitiveTypeCode GetEnumUnderlyingType(
            MetadataReader metadataReader,
            TypeReference typeRef)
        {
            var parentHandle = typeRef.ResolutionScope;
            TypeReference parentRef = default;
            bool useParent = false;

            if (parentHandle.Kind == HandleKind.TypeReference)
            {
                parentRef = metadataReader.GetTypeReference((TypeReferenceHandle)parentHandle);
                parentHandle = parentRef.ResolutionScope;
                useParent = true;
            }

            if (parentHandle.Kind != HandleKind.AssemblyReference)
            {
                return default;
            }

            var asmRef = metadataReader.GetAssemblyReference((AssemblyReferenceHandle)parentHandle);
            var asmName = new AssemblyName
            {
                Name = metadataReader.GetString(asmRef.Name),
                //Version = asmRef.Version,
                CultureName = asmRef.Culture.IsNil ? null : metadataReader.GetString(asmRef.Culture),
            };

            if (!asmRef.PublicKeyOrToken.IsNil)
            {
                var blobReader = metadataReader.GetBlobReader(asmRef.PublicKeyOrToken);
                asmName.SetPublicKeyToken(blobReader.ReadBytes(blobReader.RemainingBytes));
            }

            Assembly? assembly = null;

            try
            {
                assembly = s_alc.LoadFromAssemblyName(asmName);
            }
            catch (FileNotFoundException)
            {
            }
            catch (FileLoadException)
            {
            }
            catch (BadImageFormatException)
            {
            }

            if (assembly is null)
            {
                return default;
            }

            var lookup1 = useParent ? parentRef : typeRef;

            var ns = lookup1.Namespace.IsNil ? "" : metadataReader.GetString(lookup1.Namespace) + ".";
            var typeName = ns + metadataReader.GetString(lookup1.Name);

            var t = assembly.GetType(typeName);

            if (t is not null && useParent)
            {
                var nestedTypeName = metadataReader.GetString(typeRef.Name);
                t = t.GetNestedType(nestedTypeName, BindingFlags.Public | BindingFlags.NonPublic);
            }

            return GetEnumUnderlyingType(t);
        }

        internal static PrimitiveTypeCode GetEnumUnderlyingType(string? assemblyQualifiedTypeName)
        {
            if (assemblyQualifiedTypeName is null || !TypeName.TryParse(assemblyQualifiedTypeName, out var typeName))
                return default;

            TypeName? parentName = null;

            if (typeName.AssemblyName is null || !typeName.IsSimple)
                return default;

            AssemblyName asmName;
            Assembly? assembly = null;

            if (typeName.IsNested)
            {
                parentName = typeName.DeclaringType;

                if (parentName.IsNested || parentName.AssemblyName is null)
                {
                    return default;
                }

                asmName = parentName.AssemblyName.ToAssemblyName();
            }
            else
            {
                asmName = typeName.AssemblyName.ToAssemblyName();
            }

            asmName.Version = null;

            try
            {
                assembly = s_alc.LoadFromAssemblyName(asmName);
            }
            catch (FileNotFoundException)
            {
            }
            catch (FileLoadException)
            {
            }
            catch (BadImageFormatException)
            {
            }

            if (assembly is null)
            {
                return default;
            }

            Type? t;

            if (parentName is not null)
            {
                t = assembly.GetType(parentName.FullName);

                if (t is not null)
                {
                    t = t.GetNestedType(typeName.Name, BindingFlags.Public | BindingFlags.NonPublic);
                }
            }
            else
            {
                t = assembly.GetType(typeName.FullName);
            }

            return GetEnumUnderlyingType(t);
        }

        private static PrimitiveTypeCode GetEnumUnderlyingType(Type? t)
        {
            if (t is null || !t.IsEnum)
            {
                return default;
            }

            var typeCode = Type.GetTypeCode(t.GetEnumUnderlyingType());
            return typeCode switch
            {
                TypeCode.Byte => PrimitiveTypeCode.Byte,
                TypeCode.SByte => PrimitiveTypeCode.SByte,
                TypeCode.Int16 => PrimitiveTypeCode.Int16,
                TypeCode.UInt16 => PrimitiveTypeCode.UInt16,
                TypeCode.Int32 => PrimitiveTypeCode.Int32,
                TypeCode.UInt32 => PrimitiveTypeCode.UInt32,
                TypeCode.Int64 => PrimitiveTypeCode.Int64,
                TypeCode.UInt64 => PrimitiveTypeCode.UInt64,
                _ => default,
            };
        }
    }
}