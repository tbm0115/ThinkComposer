using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Instrumind.ThinkComposer.Services;

public sealed class ThinkComposerFileSummary
{
    public string FullPath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public long Length { get; init; }

    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> WorkspaceItems { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ReportItems { get; init; } = Array.Empty<string>();

    public byte[]? SnapshotImageBytes { get; init; }

    public string? SnapshotEntryName { get; init; }

    public byte[]? PictogramImageBytes { get; init; }

    public string? PictogramEntryName { get; init; }

    public LegacyBinaryModelSummary? LegacyModel { get; init; }
}

public static class ThinkComposerFileProbe
{
    private const int PreviewLineLimit = 40;
    private const int ItemLimit = 250;

    public static ThinkComposerFileSummary Load(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            throw new ArgumentException("A file path is required.", nameof(route));

        var path = Environment.ExpandEnvironmentVariables(route.Trim().Trim('"'));
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("The selected ThinkComposer file does not exist.", path);

        using var stream = file.OpenRead();
        var header = ReadHeader(stream);
        stream.Position = 0;

        if (IsZipPackage(header))
            return LoadPackage(file, stream);

        if (LooksLikeXml(stream))
        {
            stream.Position = 0;
            return LoadXml(file, stream);
        }

        stream.Position = 0;
        return LoadTextOrBinary(file, stream);
    }

    private static ThinkComposerFileSummary LoadPackage(FileInfo file, Stream stream)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var totalSize = entries.Sum(entry => entry.Length);
            var snapshotEntry = FindSnapshotEntry(entries);
            var snapshotImageBytes = snapshotEntry == null ? null : ReadEntry(snapshotEntry);
            var pictogramEntry = FindPictogramEntry(entries);
            var pictogramImageBytes = pictogramEntry == null ? null : ReadEntry(pictogramEntry);
            var modelEntry = FindModelEntry(file, entries);
            var legacyModel = modelEntry == null ? null : LegacyBinaryModelProbe.Analyze(modelEntry.FullName, ReadEntry(modelEntry));
            var items = entries
                .Take(ItemLimit)
                .Select(entry => FormatPackageEntry(entry))
                .ToList();

            if (entries.Length > ItemLimit)
                items.Add($"... {entries.Length - ItemLimit} more package entries");

            var details = new List<string>
            {
                $"Route: {file.FullName}",
                $"Size: {FormatBytes(file.Length)}",
                $"Package parts: {entries.Length}",
                $"Uncompressed content: {FormatBytes(totalSize)}"
            };

            AddKnownPackageHints(details, entries);
            if (snapshotEntry != null)
                details.Add($"Snapshot preview: {snapshotEntry.FullName} ({FormatBytes(snapshotEntry.Length)})");
            if (legacyModel != null)
            {
                details.Add($"Legacy model part: {legacyModel.EntryName}");
                if (!string.IsNullOrWhiteSpace(legacyModel.RootTypeName))
                    details.Add($"Model root: {legacyModel.RootTypeName}");
                details.Add($"Model names detected: {legacyModel.CandidateNames.Count}");
            }

            return new ThinkComposerFileSummary
            {
                FullPath = file.FullName,
                FileName = file.Name,
                Kind = Classify(file.Extension, isPackage: true),
                Length = file.Length,
                Details = details,
                Items = items,
                WorkspaceItems = BuildWorkspaceItems(file, entries),
                ReportItems = BuildReportItems(file, entries.Length),
                SnapshotImageBytes = snapshotImageBytes,
                SnapshotEntryName = snapshotEntry?.FullName,
                PictogramImageBytes = pictogramImageBytes,
                PictogramEntryName = pictogramEntry?.FullName,
                LegacyModel = legacyModel
            };
        }
        catch (InvalidDataException)
        {
            stream.Position = 0;
            return LoadTextOrBinary(file, stream, "ThinkComposer binary document");
        }
    }

    private static ThinkComposerFileSummary LoadXml(FileInfo file, Stream stream)
    {
        var document = XDocument.Load(stream, LoadOptions.None);
        var root = document.Root;
        var elements = document.Descendants().ToArray();
        var attributes = elements.SelectMany(element => element.Attributes()).Count();

        var items = elements
            .Take(ItemLimit)
            .Select(element => FormatXmlElement(element))
            .ToList();

        if (elements.Length > ItemLimit)
            items.Add($"... {elements.Length - ItemLimit} more XML elements");

        return new ThinkComposerFileSummary
        {
            FullPath = file.FullName,
            FileName = file.Name,
            Kind = Classify(file.Extension, isPackage: false),
            Length = file.Length,
            Details = new[]
            {
                $"Route: {file.FullName}",
                $"Size: {FormatBytes(file.Length)}",
                $"Root element: {root?.Name.LocalName ?? "(none)"}",
                $"Elements: {elements.Length}",
                $"Attributes: {attributes}"
            },
            Items = items,
            WorkspaceItems = new[]
            {
                "Loaded XML",
                $"Root: {root?.Name.LocalName ?? "(none)"}",
                $"Elements: {elements.Length}"
            },
            ReportItems = new[]
            {
                "Summary",
                "XML element inventory",
                "Migration diagnostics"
            }
        };
    }

    private static ThinkComposerFileSummary LoadTextOrBinary(
        FileInfo file,
        Stream stream,
        string? forcedKind = null)
    {
        var preview = ReadTextPreview(stream);
        var hasText = preview.Count > 0;

        return new ThinkComposerFileSummary
        {
            FullPath = file.FullName,
            FileName = file.Name,
            Kind = forcedKind ?? Classify(file.Extension, isPackage: false),
            Length = file.Length,
            Details = new[]
            {
                $"Route: {file.FullName}",
                $"Size: {FormatBytes(file.Length)}",
                hasText ? $"Preview lines: {preview.Count}" : "Preview: binary or empty file"
            },
            Items = hasText ? preview : new[] { "No text preview is available for this binary file." },
            WorkspaceItems = new[]
            {
                "Loaded file",
                Path.GetExtension(file.Name).TrimStart('.').ToUpperInvariant(),
                "Raw preview"
            },
            ReportItems = new[]
            {
                "Summary",
                "Raw file diagnostics"
            }
        };
    }

    private static byte[] ReadHeader(Stream stream)
    {
        var header = new byte[8];
        var count = stream.Read(header, 0, header.Length);
        if (count == header.Length)
            return header;

        Array.Resize(ref header, count);
        return header;
    }

    private static bool IsZipPackage(byte[] header)
    {
        return header.Length >= 4
            && header[0] == 0x50
            && header[1] == 0x4B
            && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07)
            && (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);
    }

    private static bool LooksLikeXml(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var buffer = new char[512];
        var count = reader.Read(buffer, 0, buffer.Length);
        var prefix = new string(buffer, 0, count).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return prefix.StartsWith("<", StringComparison.Ordinal);
    }

    private static List<string> ReadTextPreview(Stream stream)
    {
        var lines = new List<string>();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

        while (!reader.EndOfStream && lines.Count < PreviewLineLimit)
        {
            var line = reader.ReadLine();
            if (line == null)
                break;

            if (line.IndexOf('\0', StringComparison.Ordinal) >= 0)
                return new List<string>();

            lines.Add(line.Length > 240 ? line.Substring(0, 240) + "..." : line);
        }

        return lines;
    }

    private static string Classify(string extension, bool isPackage)
    {
        var normalized = extension.ToLowerInvariant();

        return normalized switch
        {
            ".tdom" => isPackage ? "ThinkComposer domain package" : "ThinkComposer domain",
            ".tcom" => isPackage ? "ThinkComposer composition package" : "ThinkComposer composition",
            ".tct" => "ThinkComposer template",
            ".json" => "JSON interchange document",
            ".xml" => "XML document",
            _ => isPackage ? "Packaged document" : "File"
        };
    }

    private static List<string> BuildWorkspaceItems(FileInfo file, IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        var items = new List<string>
        {
            Path.GetExtension(file.Name).Equals(".tdom", StringComparison.OrdinalIgnoreCase)
                ? "Domain"
                : "Composition",
            "Package parts"
        };

        if (entries.Any(entry => entry.FullName.EndsWith(".tct", StringComparison.OrdinalIgnoreCase)))
            items.Add("Templates");

        if (entries.Any(entry => entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
            items.Add("Serialized model");

        items.Add("Report preview");
        return items;
    }

    private static IReadOnlyList<string> BuildReportItems(FileInfo file, int partCount)
    {
        return new[]
        {
            "Summary",
            $"{Classify(file.Extension, isPackage: true)}",
            $"{partCount} package parts",
            "Direct PDF export"
        };
    }

    private static void AddKnownPackageHints(List<string> details, IEnumerable<ZipArchiveEntry> entries)
    {
        var names = entries.Select(entry => entry.FullName).ToArray();

        if (names.Any(name => name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
            details.Add("Contains legacy serialized binary model parts.");

        if (names.Any(name => name.EndsWith(".tct", StringComparison.OrdinalIgnoreCase)))
            details.Add("Contains ThinkComposer template files.");

        if (names.Any(name => name.EndsWith("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)))
            details.Add("Uses an Open Packaging Convention layout.");
    }

    private static string FormatPackageEntry(ZipArchiveEntry entry)
    {
        return $"{entry.FullName}  ({FormatBytes(entry.Length)})";
    }

    private static ZipArchiveEntry? FindSnapshotEntry(IEnumerable<ZipArchiveEntry> entries)
    {
        var supportedImageExtensions = new[] { ".jpg", ".jpeg", ".png" };
        var entryList = entries.ToArray();

        return entryList.FirstOrDefault(entry =>
                   Path.GetFileName(entry.FullName).Equals("Snapshot.jpg", StringComparison.OrdinalIgnoreCase))
               ?? entryList.FirstOrDefault(entry =>
                   Path.GetFileNameWithoutExtension(entry.FullName).Equals("Snapshot", StringComparison.OrdinalIgnoreCase)
                   && supportedImageExtensions.Contains(Path.GetExtension(entry.FullName), StringComparer.OrdinalIgnoreCase));
    }

    private static ZipArchiveEntry? FindPictogramEntry(IEnumerable<ZipArchiveEntry> entries)
    {
        var supportedImageExtensions = new[] { ".jpg", ".jpeg", ".png" };

        return entries.FirstOrDefault(entry =>
            Path.GetFileNameWithoutExtension(entry.FullName).Equals("Pictogram", StringComparison.OrdinalIgnoreCase)
            && supportedImageExtensions.Contains(Path.GetExtension(entry.FullName), StringComparer.OrdinalIgnoreCase));
    }

    private static ZipArchiveEntry? FindModelEntry(FileInfo file, IEnumerable<ZipArchiveEntry> entries)
    {
        var preferred = Path.GetExtension(file.Name).Equals(".tdom", StringComparison.OrdinalIgnoreCase)
            ? "Domain.bin"
            : "Composition.bin";

        var entryList = entries.ToArray();
        return entryList.FirstOrDefault(entry =>
                   string.Equals(entry.FullName, preferred, StringComparison.OrdinalIgnoreCase))
               ?? entryList.FirstOrDefault(entry =>
                   entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string FormatXmlElement(XElement element)
    {
        var depth = element.Ancestors().Count();
        var indent = new string(' ', depth * 2);
        var attributes = element.Attributes().Take(3).Select(attribute => $"{attribute.Name.LocalName}=\"{attribute.Value}\"");
        var attributeText = string.Join(" ", attributes);

        return string.IsNullOrEmpty(attributeText)
            ? indent + element.Name.LocalName
            : indent + element.Name.LocalName + " " + attributeText;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "bytes", "KB", "MB", "GB" };
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
