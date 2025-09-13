using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Terrajobst.UsageCrawling;

public sealed class LibraryReader : IDisposable
{
    private readonly bool _leaveOpen;

    public PEReader PEReader { get; private set; }
    public MetadataReader MetadataReader { get; private set; }

    public LibraryReader(PEReader peReader, bool leaveOpen = false)
    {
        _leaveOpen = leaveOpen;
        PEReader = peReader;
        MetadataReader = peReader.GetMetadataReader();
    }

    private LibraryReader(PEReader peReader, MetadataReader metadataReader, bool leaveOpen = false)
    {
        _leaveOpen = leaveOpen;
        PEReader = peReader;
        MetadataReader = metadataReader;
    }

    public static bool TryOpen(
        string filePath,
        [NotNullWhen(true)] out LibraryReader? libraryReader)
    {
        Stream? fileStream = null;

        try
        {
            fileStream = File.OpenRead(filePath);

            if (TryOpen(fileStream, out libraryReader))
            {
                fileStream = null;
                return true;
            }
        }
        finally
        {
            fileStream?.Dispose();
        }

        libraryReader = null;
        return false;
    }

    public static bool TryOpen(
        Stream stream,
        [NotNullWhen(true)] out LibraryReader? libraryReader,
        bool leaveOpen = false)
    {
        using (var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            try
            {
                if (peReader.HasMetadata)
                {
                    var metadataReader = peReader.GetMetadataReader();

                    if (metadataReader.IsAssembly)
                    {
                        libraryReader = new LibraryReader(peReader, metadataReader, leaveOpen);
                        return true;
                    }
                }
            }
            catch (BadImageFormatException)
            {
                // Not a PE file, or corrupted metadata
            }

            libraryReader = null;
            return false;
        }
    }

    public void Dispose()
    {
        if (!_leaveOpen)
        {
            PEReader?.Dispose();
        }

        PEReader = null!;
        MetadataReader = null!;
    }

    internal TypeDefinition GetTypeDefinition(EntityHandle typeDefHandle)
    {
        return MetadataReader.GetTypeDefinition((TypeDefinitionHandle)typeDefHandle);
    }

    internal bool TryGetMethodBody(MethodDefinitionHandle methodDefHandle, [NotNullWhen(true)] out MethodBodyBlock? body)
    {
        return TryGetMethodBody(MetadataReader.GetMethodDefinition(methodDefHandle), out body);
    }

    internal bool TryGetMethodBody(MethodDefinition methodDef, [NotNullWhen(true)] out MethodBodyBlock? body)
    {
        var rva = methodDef.RelativeVirtualAddress;

        const MethodImplAttributes RejectMask =
            MethodImplAttributes.Runtime |
            MethodImplAttributes.InternalCall |
            MethodImplAttributes.Unmanaged |
            MethodImplAttributes.Native |
            MethodImplAttributes.OPTIL;

        if (rva == 0 || (methodDef.ImplAttributes & RejectMask) != 0)
        {
            body = null;
            return false;
        }

        try
        {
            body = PEReader.GetMethodBody(rva);
            return true;
        }
        catch (BadImageFormatException)
        {
            body = null;
            return false;
        }
    }
}
