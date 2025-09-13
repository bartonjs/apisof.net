using System.Reflection.Metadata;

namespace Terrajobst.UsageCrawling;

public static class MetadataUtils
{
    internal static bool IsNamed(
        CustomAttributeHandle attributeHandle,
        MetadataReader metadataReader,
        string? targetNamespace,
        string targetName,
        bool allowTypeDef = true)
    {
        var attr = metadataReader.GetCustomAttribute(attributeHandle);
        return IsNamed(attr, metadataReader, targetNamespace, targetName, allowTypeDef);
    }

    internal static bool IsNamed(
        CustomAttribute attribute,
        MetadataReader metadataReader,
        string? targetNamespace,
        string targetName,
        bool allowTypeDef = true)
    {
        var ctor = attribute.Constructor;

        if (ctor.Kind != HandleKind.MemberReference)
            return false;

        var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)ctor);

        switch (memberRef.Parent.Kind)
        {
            case HandleKind.TypeReference:
                return IsNamed(
                    metadataReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent),
                    metadataReader,
                    targetNamespace,
                    targetName);
            case HandleKind.TypeDefinition:
                return allowTypeDef && IsNamed(
                    metadataReader.GetTypeDefinition((TypeDefinitionHandle)memberRef.Parent),
                    metadataReader,
                    targetNamespace,
                    targetName);
        }

        return false;
    }

    internal static bool IsNamed(
        TypeReference typeRef,
        MetadataReader metadataReader,
        string? targetNamespace,
        string targetName)
    {
        // Nested types don't match.
        if (typeRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return false;

        return string.Equals(targetNamespace, metadataReader.GetString(typeRef.Namespace)) &&
               string.Equals(targetName, metadataReader.GetString(typeRef.Name));
    }

    internal static bool IsNamed(
        TypeDefinition typeDef,
        MetadataReader metadataReader,
        string? targetNamespace,
        string targetName)
    {
        // Nested types don't match.
        if (typeDef.IsNested)
            return false;

        return
            string.Equals(targetNamespace, metadataReader.GetString(typeDef.Namespace)) &&
            string.Equals(targetName, metadataReader.GetString(typeDef.Name));
    }

    internal static bool IsNamedAny(
        TypeDefinitionHandle typeDefHandle,
        MetadataReader metadataReader,
        string? targetNamespace,
        ReadOnlySpan<string> targetNames)
    {
        if (targetNames.IsEmpty)
            return false;

        return IsNamedAny(
            metadataReader.GetTypeDefinition(typeDefHandle),
            metadataReader,
            targetNamespace,
            targetNames);
    }

    internal static bool IsNamedAny(
        TypeDefinition typeDef,
        MetadataReader metadataReader,
        string? targetNamespace,
        ReadOnlySpan<string> targetNames)
    {
        if (targetNames.IsEmpty)
            return false;

        // Nested types don't match.
        if (typeDef.IsNested)
            return false;

        return string.Equals(targetNamespace, metadataReader.GetString(typeDef.Namespace)) &&
               targetNames.Contains(metadataReader.GetString(typeDef.Name));
    }

    internal static bool IsNamedAny(
        TypeReferenceHandle typeRefHandle,
        MetadataReader metadataReader,
        string? targetNamespace,
        ReadOnlySpan<string> targetNames)
    {
        if (targetNames.IsEmpty)
            return false;

        return IsNamedAny(
            metadataReader.GetTypeReference(typeRefHandle),
            metadataReader,
            targetNamespace,
            targetNames);
    }

    internal static bool IsNamedAny(
        TypeReference typeRef,
        MetadataReader metadataReader,
        string? targetNamespace,
        ReadOnlySpan<string> targetNames)
    {
        if (targetNames.IsEmpty)
            return false;

        // Nested types don't match.
        if (typeRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return false;

        return string.Equals(targetNamespace, metadataReader.GetString(typeRef.Namespace)) &&
               targetNames.Contains(metadataReader.GetString(typeRef.Name));
    }

    internal static string? GetName(EntityHandle entityHandle, MetadataReader metadataReader)
    {
        switch (entityHandle.Kind)
        {
            case HandleKind.TypeReference:
                return GetName((TypeReferenceHandle)entityHandle, metadataReader);
            case HandleKind.TypeSpecification:
                var typeSpec = metadataReader.GetTypeSpecification((TypeSpecificationHandle)entityHandle);
                var typeDefOrRefHandle = GetTypeSpecTarget(typeSpec, metadataReader);

                if (typeDefOrRefHandle.IsNil)
                    return null;

                return GetName(typeDefOrRefHandle, metadataReader);
            default:
                return null;
        }
    }

    internal static string? GetName(TypeReferenceHandle typeRefHandle, MetadataReader metadataReader)
    {
        return GetName(metadataReader.GetTypeReference(typeRefHandle), metadataReader);
    }

    internal static string? GetName(TypeReference typeRef, MetadataReader metadataReader)
    {
        if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var parentRef = metadataReader.GetTypeReference((TypeReferenceHandle)typeRef.ResolutionScope);

            if (parentRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
                return null;

            return GetName(parentRef, metadataReader) + "." + metadataReader.GetString(typeRef.Name);
        }

        if (typeRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return null;

        var name = metadataReader.GetString(typeRef.Name).Replace('+', '.');

        if (typeRef.Namespace.IsNil)
        {
            return name;
        }

        var ns = metadataReader.GetString(typeRef.Namespace);
        return $"{ns}.{name}";
    }

    internal static string? GetDocumentationId(TypeReferenceHandle typeRefHandle, MetadataReader metadataReader)
    {
        return GetDocumentationId(
            metadataReader.GetTypeReference(typeRefHandle),
            metadataReader);
    }

    internal static string? GetDocumentationId(TypeReference typeRef, MetadataReader metadataReader)
    {
        var name = GetName(typeRef, metadataReader);

        if (name is null)
            return null;

        return "T:" + name;
    }

    internal static string? GetDocumentationId(MemberReference memberRef, MetadataReader metadataReader)
    {
        var parentHandle = memberRef.Parent;

        switch (parentHandle.Kind)
        {
            case HandleKind.TypeReference:
            case HandleKind.TypeSpecification:
                break;
            default:
                return null;
        }

        var parentName = GetName(parentHandle, metadataReader);

        if (parentName is null)
            return null;

        char prefix;
        string suffix;
        var kind = memberRef.GetKind();

        if (kind == MemberReferenceKind.Method)
        {
            prefix = 'M';
            var sig = memberRef.DecodeMethodSignature(DocumentationIdGenerator.Instance, null);

            if (sig.ParameterTypes.IsEmpty)
            {
                suffix = "";
            }
            else
            {
                suffix = "(" + string.Join(",", sig.ParameterTypes) + ")";
            }
        }
        else if (kind == MemberReferenceKind.Field)
        {
            prefix = 'F';
            suffix = "";
        }
        else
        {
            throw new BadImageFormatException();
        }

        string? typeName = null;

        if (memberRef.Parent.Kind == HandleKind.TypeReference)
        {
            var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
            typeName = GetName(typeRef, metadataReader);
        }
        else
        {
            var typeSpec = metadataReader.GetTypeSpecification((TypeSpecificationHandle)memberRef.Parent);
            var typeDefOrRef = GetTypeSpecTarget(typeSpec, metadataReader);

            if (!typeDefOrRef.IsNil)
            {
                typeName = GetName(typeDefOrRef, metadataReader);
            }
        }

        if (typeName is null)
            return null;

        var genericSuffix = "";
        var sigReader = metadataReader.GetBlobReader(memberRef.Signature);
        var header = sigReader.ReadSignatureHeader();

        if (header.IsGeneric)
        {
            var arity = sigReader.ReadCompressedInteger();
            genericSuffix = "``" + arity;
        }

        return $"{prefix}:{typeName}.{metadataReader.GetString(memberRef.Name).Replace('.', '#')}{genericSuffix}{suffix}";
    }

    internal static EntityHandle GetDocumentationParent(
        MemberReference memberRef,
        MetadataReader metadataReader)
    {
        var parentHandle = memberRef.Parent;
        
        if (parentHandle.Kind == HandleKind.TypeSpecification)
        {
            return GetTypeSpecTarget((TypeSpecificationHandle)parentHandle, metadataReader);
        }

        return parentHandle;
    }

    internal static EntityHandle GetTypeSpecTarget(
        TypeSpecificationHandle typeSpecHandle,
        MetadataReader metadataReader)
    {
        return GetTypeSpecTarget(
            metadataReader.GetTypeSpecification(typeSpecHandle),
            metadataReader);
    }

    internal static EntityHandle GetTypeSpecTarget(
        TypeSpecification typeSpec,
        MetadataReader metadataReader)
    {
        var reader = metadataReader.GetBlobReader(typeSpec.Signature);
        var header = (SignatureTypeCode)reader.ReadCompressedInteger();

        if (header != SignatureTypeCode.GenericTypeInstance)
            return default;

        var elementKind = (SignatureTypeKind)reader.ReadCompressedInteger();

        if (elementKind != SignatureTypeKind.Class &&
            elementKind != SignatureTypeKind.ValueType)
        {
            throw new BadImageFormatException();
        }

        var typeDefOrRef = reader.ReadTypeHandle();

        switch (typeDefOrRef.Kind)
        {
            case HandleKind.TypeDefinition:
            case HandleKind.TypeReference:
                return typeDefOrRef;
            default:
                return default;
        }
    }

    internal static ILOpCode ReadOpCode(ref this BlobReader reader)
    {
        ushort val = reader.ReadByte();

        if (val < 0xF0)
            return (ILOpCode)val;

        val <<= 8;
        var second = reader.ReadByte();
        return (ILOpCode)(val | second);
    }

    internal static bool TryGetOpcodeSize(ILOpCode opcode, out int size)
    {
        var local = opcode switch
        {
            // No operand
            ILOpCode.Readonly or
                ILOpCode.Tail or
                ILOpCode.Volatile or
                ILOpCode.Add or
                ILOpCode.Add_ovf or
                ILOpCode.Add_ovf_un or
                ILOpCode.And or
                ILOpCode.Arglist or
                ILOpCode.Break or
                ILOpCode.Ceq or
                ILOpCode.Cgt or
                ILOpCode.Cgt_un or
                ILOpCode.Ckfinite or
                ILOpCode.Clt or
                ILOpCode.Clt_un or
                ILOpCode.Conv_i or
                ILOpCode.Conv_i1 or
                ILOpCode.Conv_i2 or
                ILOpCode.Conv_i4 or
                ILOpCode.Conv_i8 or
                ILOpCode.Conv_u or
                ILOpCode.Conv_u1 or
                ILOpCode.Conv_u2 or
                ILOpCode.Conv_u4 or
                ILOpCode.Conv_u8 or
                ILOpCode.Conv_r4 or
                ILOpCode.Conv_r8 or
                ILOpCode.Conv_r_un or
                ILOpCode.Conv_ovf_i or
                ILOpCode.Conv_ovf_i1 or
                ILOpCode.Conv_ovf_i2 or
                ILOpCode.Conv_ovf_i4 or
                ILOpCode.Conv_ovf_i8 or
                ILOpCode.Conv_ovf_u or
                ILOpCode.Conv_ovf_u1 or
                ILOpCode.Conv_ovf_u2 or
                ILOpCode.Conv_ovf_u4 or
                ILOpCode.Conv_ovf_u8 or
                ILOpCode.Conv_ovf_i_un or
                ILOpCode.Conv_ovf_i1_un or
                ILOpCode.Conv_ovf_i2_un or
                ILOpCode.Conv_ovf_i4_un or
                ILOpCode.Conv_ovf_i8_un or
                ILOpCode.Conv_ovf_u_un or
                ILOpCode.Conv_ovf_u1_un or
                ILOpCode.Conv_ovf_u2_un or
                ILOpCode.Conv_ovf_u4_un or
                ILOpCode.Conv_ovf_u8_un or
                ILOpCode.Cpblk or
                ILOpCode.Div or
                ILOpCode.Div_un or
                ILOpCode.Dup or
                ILOpCode.Endfilter or
                ILOpCode.Endfinally or
                ILOpCode.Initblk or
                ILOpCode.Ldarg_0 or
                ILOpCode.Ldarg_1 or
                ILOpCode.Ldarg_2 or
                ILOpCode.Ldarg_3 or
                ILOpCode.Ldc_i4_0 or
                ILOpCode.Ldc_i4_1 or
                ILOpCode.Ldc_i4_2 or
                ILOpCode.Ldc_i4_3 or
                ILOpCode.Ldc_i4_4 or
                ILOpCode.Ldc_i4_5 or
                ILOpCode.Ldc_i4_6 or
                ILOpCode.Ldc_i4_7 or
                ILOpCode.Ldc_i4_8 or
                ILOpCode.Ldc_i4_m1 or
                ILOpCode.Ldind_i1 or
                ILOpCode.Ldind_i2 or
                ILOpCode.Ldind_i4 or
                ILOpCode.Ldind_i8 or
                ILOpCode.Ldind_u1 or
                ILOpCode.Ldind_u2 or
                ILOpCode.Ldind_u4 or
                ILOpCode.Ldind_r4 or
                ILOpCode.Ldind_r8 or
                ILOpCode.Ldind_i or
                ILOpCode.Ldind_ref or
                ILOpCode.Ldloc_0 or
                ILOpCode.Ldloc_1 or
                ILOpCode.Ldloc_2 or
                ILOpCode.Ldloc_3 or
                ILOpCode.Ldnull or
                ILOpCode.Localloc or
                ILOpCode.Mul or
                ILOpCode.Mul_ovf or
                ILOpCode.Mul_ovf_un or
                ILOpCode.Neg or
                ILOpCode.Nop or
                ILOpCode.Not or
                ILOpCode.Or or
                ILOpCode.Pop or
                ILOpCode.Rem or
                ILOpCode.Rem_un or
                ILOpCode.Ret or
                ILOpCode.Shl or
                ILOpCode.Shr or
                ILOpCode.Shr_un or
                ILOpCode.Stind_i1 or
                ILOpCode.Stind_i2 or
                ILOpCode.Stind_i4 or
                ILOpCode.Stind_i8 or
                ILOpCode.Stind_r4 or
                ILOpCode.Stind_r8 or
                ILOpCode.Stind_i or
                ILOpCode.Stind_ref or
                ILOpCode.Stloc_0 or
                ILOpCode.Stloc_1 or
                ILOpCode.Stloc_2 or
                ILOpCode.Stloc_3 or
                ILOpCode.Sub or
                ILOpCode.Sub_ovf or
                ILOpCode.Sub_ovf_un or
                ILOpCode.Xor or
                ILOpCode.Ldelem_i1 or
                ILOpCode.Ldelem_i2 or
                ILOpCode.Ldelem_i4 or
                ILOpCode.Ldelem_i8 or
                ILOpCode.Ldelem_u1 or
                ILOpCode.Ldelem_u2 or
                ILOpCode.Ldelem_u4 or
                ILOpCode.Ldelem_r4 or
                ILOpCode.Ldelem_r8 or
                ILOpCode.Ldelem_i or
                ILOpCode.Ldelem_ref or
                ILOpCode.Ldlen or
                ILOpCode.Refanytype or
                ILOpCode.Rethrow or
                ILOpCode.Stelem_i1 or
                ILOpCode.Stelem_i2 or
                ILOpCode.Stelem_i4 or
                ILOpCode.Stelem_i8 or
                ILOpCode.Stelem_r4 or
                ILOpCode.Stelem_r8 or
                ILOpCode.Stelem_i or
                ILOpCode.Stelem_ref or
                ILOpCode.Throw or
                ILOpCode.Add => 0,

            // Int8 (1 byte)
            //ILOpCode.No or
            ILOpCode.Unaligned or
                ILOpCode.Beq_s or
                ILOpCode.Bge_s or
                ILOpCode.Bge_un_s or
                ILOpCode.Bgt_s or
                ILOpCode.Bgt_un_s or
                ILOpCode.Ble_s or
                ILOpCode.Ble_un_s or
                ILOpCode.Blt_s or
                ILOpCode.Blt_un_s or
                ILOpCode.Bne_un_s or
                ILOpCode.Br_s or
                ILOpCode.Brfalse_s or
                ILOpCode.Brtrue_s or
                ILOpCode.Ldarg_s or
                ILOpCode.Ldarga_s or
                ILOpCode.Ldc_i4_s or
                ILOpCode.Ldloc_s or
                ILOpCode.Ldloca_s or
                ILOpCode.Leave_s or
                ILOpCode.Starg_s or
                ILOpCode.Stloc_s or
                ILOpCode.Beq_s => 1,

            // Int16 (2 bytes)
            ILOpCode.Ldarg or
                ILOpCode.Ldarga or
                ILOpCode.Ldloc or
                ILOpCode.Ldloca or
                ILOpCode.Starg or
                ILOpCode.Stloc or
                ILOpCode.Ldarg => 2,

            // Int32 (4 bytes)
            ILOpCode.Beq or
                ILOpCode.Bge or
                ILOpCode.Bge_un or
                ILOpCode.Bgt or
                ILOpCode.Bgt_un or
                ILOpCode.Ble or
                ILOpCode.Ble_un or
                ILOpCode.Blt or
                ILOpCode.Blt_un or
                ILOpCode.Bne_un or
                ILOpCode.Br or
                ILOpCode.Brfalse or
                ILOpCode.Brtrue or
                ILOpCode.Ldc_i4 or
                ILOpCode.Ldc_r4 or
                ILOpCode.Leave or
                ILOpCode.Bge => 4,

            // Metadata Token (4 bytes)
            ILOpCode.Call or
                ILOpCode.Calli or
                ILOpCode.Jmp or
                ILOpCode.Ldftn or
                ILOpCode.Box or
                ILOpCode.Callvirt or
                ILOpCode.Castclass or
                ILOpCode.Cpobj or
                ILOpCode.Initobj or
                ILOpCode.Isinst or
                ILOpCode.Ldelem or
                ILOpCode.Ldelema or
                ILOpCode.Ldfld or
                ILOpCode.Ldflda or
                ILOpCode.Ldobj or
                ILOpCode.Ldsfld or
                ILOpCode.Ldsflda or
                ILOpCode.Ldstr or
                ILOpCode.Ldtoken or
                ILOpCode.Ldvirtftn or
                ILOpCode.Mkrefany or
                ILOpCode.Newarr or
                ILOpCode.Refanyval or
                ILOpCode.Sizeof or
                ILOpCode.Stelem or
                ILOpCode.Stfld or
                ILOpCode.Stobj or
                ILOpCode.Stsfld or
                ILOpCode.Unbox or
                ILOpCode.Unbox_any or
                ILOpCode.Constrained or
                ILOpCode.Newobj => 4,

            // Int64 (8 bytes)
            ILOpCode.Ldc_i8 or
                ILOpCode.Ldc_r8 => 8,

            // Variable
            ILOpCode.Switch => throw new NotSupportedException(),

            _ => -1,
        };

        if (local < 0)
        {
            size = 0;
            return false;
        }

        size = local;
        return true;
    }

    public static string? GetTargetFrameworkMoniker(MetadataReader metadataReader)
    {
        var assembly = metadataReader.GetAssemblyDefinition();
        
        foreach (var attrHandle in assembly.GetCustomAttributes())
        {
            var attr = metadataReader.GetCustomAttribute(attrHandle);

            if (IsNamed(attr, metadataReader, "System.Runtime.Versioning", "TargetFrameworkAttribute"))
            {
                var reader = metadataReader.GetBlobReader(attr.Value);

                // Really this is a BadImageFormatException, but let's just say "not found"
                if (reader.ReadInt16() != 1)
                    return null;

                if (reader.TryReadCompressedInteger(out int strLength))
                {
                    return reader.ReadUTF8(strLength);
                }

                // Either it is 0xFF (null), or it should be BadImageFormatException,
                // either way, return null
                return null;
            }
        }

        return null;
    }
}