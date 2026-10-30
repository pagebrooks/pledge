using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;

namespace PledgeContracts.Build.Tests;

// Ways to compare two builds of the same assembly: as bytes, as bytes with the build identity blanked out, and as
// ILSpy's IL disassembly, which is also what a failed comparison prints so the differing methods are visible.
internal static class Assemblies
{
    // A deterministic build derives the MVID, the COFF timestamp and the debug directory (PDB id, checksum) from
    // a hash of the PDB, and the PDB records source lines, so two builds of different source differ there even
    // when the code is the same. Everything else must match byte for byte.
    public static byte[] WithoutBuildIdentity(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var regions = new List<(int Offset, int Length)>();

        // Collect first, blank afterwards: the reader parses lazily from these same bytes.
        using (var reader = new PEReader(new MemoryStream(File.ReadAllBytes(path))))
        {
            var headers = reader.PEHeaders;
            regions.Add((headers.CoffHeaderStartOffset + 4, 4));
            regions.Add((headers.PEHeaderStartOffset + 64, 4));

            var debugTable = headers.PEHeader!.DebugTableDirectory;
            if (debugTable.Size > 0 && headers.TryGetDirectoryOffset(debugTable, out var tableOffset))
            {
                regions.Add((tableOffset, debugTable.Size));
            }

            foreach (var entry in reader.ReadDebugDirectory())
            {
                regions.Add((entry.DataPointer, entry.DataSize));
            }

            var metadata = reader.GetMetadataReader();
            var mvidIndex = MetadataTokens.GetHeapOffset(metadata.GetModuleDefinition().Mvid);
            regions.Add((headers.MetadataStartOffset + metadata.GetHeapMetadataOffset(HeapIndex.Guid) + ((mvidIndex - 1) * 16), 16));
        }

        foreach (var (offset, length) in regions)
        {
            Array.Clear(bytes, offset, length);
        }

        return bytes;
    }

    public static string Disassemble(string path, bool includeAssemblyHeader = true)
    {
        using var stream = File.OpenRead(path);
        using var file = new PEFile(path, stream, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default, null);
        using var writer = new StringWriter();
        var disassembler = new ReflectionDisassembler(new PlainTextOutput(writer), CancellationToken.None)
        {
            DetectControlStructure = false,
            ShowMetadataTokens = false,
        };

        if (includeAssemblyHeader)
        {
            disassembler.WriteAssemblyReferences(file.Metadata);
            disassembler.WriteAssemblyHeader(file);
        }

        disassembler.WriteModuleContents(file);
        return writer.ToString();
    }

    // The first lines where two disassemblies part ways, with a little context, for an assertion message.
    public static string FirstDifference(string expected, string actual, int context = 6)
    {
        var left = expected.Split('\n');
        var right = actual.Split('\n');
        var index = 0;
        while (index < left.Length && index < right.Length && left[index] == right[index])
        {
            index++;
        }

        if (index == left.Length && index == right.Length)
        {
            return "The disassemblies are identical; the difference is outside the IL and metadata ILSpy prints.";
        }

        var from = Math.Max(0, index - context);
        return $"""
            Disassembly differs at line {index + 1}.
            --- without Pledge
            {string.Join('\n', left.Skip(from).Take(context * 2))}
            --- with Pledge
            {string.Join('\n', right.Skip(from).Take(context * 2))}
            """;
    }

    // Where two images differ, as file offsets grouped into runs and placed in their PE section or header.
    public static string DescribeByteDifferences(byte[] expected, byte[] actual, string layoutFrom)
    {
        if (expected.Length != actual.Length)
        {
            return $"The files differ in length: {expected.Length} bytes without Pledge, {actual.Length} with.";
        }

        using var reader = new PEReader(File.OpenRead(layoutFrom));
        var runs = new List<string>();
        for (var offset = 0; offset < expected.Length && runs.Count < 10; offset++)
        {
            if (expected[offset] == actual[offset])
            {
                continue;
            }

            var start = offset;
            while (offset < expected.Length && expected[offset] != actual[offset])
            {
                offset++;
            }

            var section = reader.PEHeaders.SectionHeaders.FirstOrDefault(header => start >= header.PointerToRawData && start < header.PointerToRawData + header.SizeOfRawData);
            var where = section.Name is null ? "headers" : $"{section.Name} at RVA 0x{start - section.PointerToRawData + section.VirtualAddress:x}";
            runs.Add($"0x{start:x}..0x{offset:x} ({offset - start} bytes, {where})");
        }

        return "Differing byte runs: " + string.Join(", ", runs);
    }

}
