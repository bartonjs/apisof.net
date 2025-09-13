using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Terrajobst.UsageCrawling;

internal struct OpCodeIterator
{
    public static IEnumerable<(ILOpCode OpCode, EntityHandle EntityArgument)> WalkMethod(
        LibraryReader libraryReader,
        MethodDefinitionHandle methodDefHandle)
    {
        return WalkMethod(libraryReader, libraryReader.MetadataReader.GetMethodDefinition(methodDefHandle));
    }

    internal static IEnumerable<(ILOpCode OpCode, EntityHandle EntityArgument)> WalkMethod(
        LibraryReader libraryReader,
        MethodDefinition methodDefinition)
    {
        if (libraryReader.TryGetMethodBody(methodDefinition, out var body))
        {
            return WalkMethod(body);
        }

        return [];
    }

    internal static IEnumerable<(ILOpCode OpCode, EntityHandle EntityArgument)> WalkMethod(MethodBodyBlock body)
    {
        var reader = body.GetILReader();

        while (reader.RemainingBytes > 0)
        {
            var opCode = reader.ReadOpCode();

            switch (opCode)
            {
                case ILOpCode.Call:
                case ILOpCode.Calli:
                case ILOpCode.Jmp:
                case ILOpCode.Ldftn:
                case ILOpCode.Box:
                case ILOpCode.Callvirt:
                case ILOpCode.Castclass:
                case ILOpCode.Cpobj:
                case ILOpCode.Initobj:
                case ILOpCode.Isinst:
                case ILOpCode.Ldelem:
                case ILOpCode.Ldelema:
                case ILOpCode.Ldfld:
                case ILOpCode.Ldflda:
                case ILOpCode.Ldobj:
                case ILOpCode.Ldsfld:
                case ILOpCode.Ldsflda:
                case ILOpCode.Ldtoken:
                case ILOpCode.Ldvirtftn:
                case ILOpCode.Mkrefany:
                case ILOpCode.Newarr:
                case ILOpCode.Refanyval:
                case ILOpCode.Sizeof:
                case ILOpCode.Stelem:
                case ILOpCode.Stfld:
                case ILOpCode.Stobj:
                case ILOpCode.Stsfld:
                case ILOpCode.Unbox:
                case ILOpCode.Unbox_any:
                case ILOpCode.Constrained:
                case ILOpCode.Newobj:
                    var token = MetadataTokens.EntityHandle(reader.ReadInt32());
                    yield return (opCode, token);
                    break;
                case ILOpCode.Switch:
                    var count = reader.ReadInt32();
                    reader.Offset += count * sizeof(int);
                    break;
                default:
                    if (!MetadataUtils.TryGetOpcodeSize(opCode, out var size))
                    {
                        yield break;
                    }

                    reader.Offset += size;
                    yield return (opCode, default);
                    break;
            }
        }
    }
}