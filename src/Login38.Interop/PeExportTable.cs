using System.Buffers.Binary;
using System.Text;

namespace Login38.Interop;

/// <summary>
/// Resolves exports by reading a module's PE headers out of the target process.
/// </summary>
/// <remarks>
/// Resolving locally with <c>GetProcAddress</c> would give an address in <em>this</em>
/// process. That happens to match for system DLLs when both processes are the same
/// bitness and ASLR has not relocated them differently — an assumption that fails
/// silently and writes a jump to the wrong address. Reading the target's own headers
/// removes the assumption.
/// </remarks>
internal static class PeExportTable
{
    private const int DosHeaderSize = 64;
    private const int DosLfanewOffset = 60;

    /// <summary>PE signature + COFF header + the whole PE32 optional header.</summary>
    private const int PeHeaderSize = 264;

    /// <summary>
    /// Offset of the export directory RVA from the start of the PE signature:
    /// 24 bytes of signature and COFF header, then 96 bytes into the optional header.
    /// </summary>
    private const int ExportDirectoryRvaOffset = 120;

    private const int ExportDirectorySize = 40;
    private const int MaxExportNameLength = 64;

    internal static GameAddress? Find(RemoteProcess process, GameAddress moduleBase, string exportName)
    {
        var dos = process.ReadBytes(moduleBase, DosHeaderSize);
        if (dos[0] != (byte)'M' || dos[1] != (byte)'Z')
        {
            throw new GameProcessException($"No DOS header at {moduleBase}.");
        }

        var peOffset = BinaryPrimitives.ReadUInt32LittleEndian(dos.AsSpan(DosLfanewOffset));
        var peBase = moduleBase + peOffset;

        var pe = process.ReadBytes(peBase, PeHeaderSize);
        if (pe[0] != (byte)'P' || pe[1] != (byte)'E' || pe[2] != 0 || pe[3] != 0)
        {
            throw new GameProcessException($"No PE header at {peBase}.");
        }

        var exportRva = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(ExportDirectoryRvaOffset));
        if (exportRva == 0)
        {
            return null;
        }

        var directory = process.ReadBytes(moduleBase + exportRva, ExportDirectorySize);
        var functionCount = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(20));
        var nameCount = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(24));
        var addressTableRva = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(28));
        var nameTableRva = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(32));
        var ordinalTableRva = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(36));

        if (nameCount == 0 || functionCount == 0)
        {
            return null;
        }

        var namePointers = process.ReadBytes(moduleBase + nameTableRva, checked((int)nameCount * 4));
        var ordinals = process.ReadBytes(moduleBase + ordinalTableRva, checked((int)nameCount * 2));
        var addresses = process.ReadBytes(moduleBase + addressTableRva, checked((int)functionCount * 4));

        var target = Encoding.ASCII.GetBytes(exportName);

        for (var i = 0; i < nameCount; i++)
        {
            var nameRva = BinaryPrimitives.ReadUInt32LittleEndian(namePointers.AsSpan(i * 4));

            // Export names are NUL terminated and of unknown length; a fixed read is
            // enough for every name this launcher looks up.
            var nameBytes = process.ReadBytes(moduleBase + nameRva, MaxExportNameLength);
            var end = Array.IndexOf(nameBytes, (byte)0);
            var name = nameBytes.AsSpan(0, end < 0 ? nameBytes.Length : end);

            if (!name.SequenceEqual(target))
            {
                continue;
            }

            var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(ordinals.AsSpan(i * 2));
            if (ordinal >= functionCount)
            {
                return null;
            }

            var functionRva = BinaryPrimitives.ReadUInt32LittleEndian(addresses.AsSpan(ordinal * 4));
            return moduleBase + functionRva;
        }

        return null;
    }
}
