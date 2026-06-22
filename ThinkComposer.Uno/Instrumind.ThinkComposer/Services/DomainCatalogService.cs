using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Instrumind.ThinkComposer.Services;

public sealed class DomainCatalogEntry
{
    public string FullPath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public byte[]? SnapshotImageBytes { get; init; }

    public byte[]? PictogramImageBytes { get; init; }
}

public static class DomainCatalogService
{
    public static IReadOnlyList<DomainCatalogEntry> Load(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return Array.Empty<DomainCatalogEntry>();

        return Directory.EnumerateFiles(folder, "*.tdom", SearchOption.TopDirectoryOnly)
            .Select(TryReadDomain)
            .Where(entry => entry != null)
            .OrderBy(entry => entry!.Name, StringComparer.OrdinalIgnoreCase)
            .Cast<DomainCatalogEntry>()
            .ToArray();
    }

    private static DomainCatalogEntry? TryReadDomain(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var metadata = ReadCoreProperties(archive);
            var fileName = Path.GetFileName(path);
            var fallbackName = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ');

            return new DomainCatalogEntry
            {
                FullPath = Path.GetFullPath(path),
                FileName = fileName,
                Name = metadata.GetValueOrDefault("title", fallbackName),
                Summary = metadata.GetValueOrDefault("description", string.Empty),
                Version = metadata.GetValueOrDefault("version", string.Empty),
                SnapshotImageBytes = ReadEntry(archive, "Snapshot"),
                PictogramImageBytes = ReadEntry(archive, "Pictogram")
            };
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ReadCoreProperties(ZipArchive archive)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entry = archive.Entries.FirstOrDefault(item =>
            item.FullName.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase));

        if (entry == null)
            return result;

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace dc = "http://purl.org/dc/elements/1.1/";

        Add(result, "title", document.Descendants(dc + "title").FirstOrDefault()?.Value);
        Add(result, "description", document.Descendants(dc + "description").FirstOrDefault()?.Value);
        Add(result, "version", document.Descendants().FirstOrDefault(element => element.Name.LocalName == "version")?.Value);

        return result;
    }

    private static byte[]? ReadEntry(ZipArchive archive, string logicalName)
    {
        var supportedExtensions = new[] { ".jpg", ".jpeg", ".png" };
        var entry = archive.Entries.FirstOrDefault(item =>
            string.Equals(Path.GetFileNameWithoutExtension(item.FullName), logicalName, StringComparison.OrdinalIgnoreCase)
            && supportedExtensions.Contains(Path.GetExtension(item.FullName), StringComparer.OrdinalIgnoreCase));

        if (entry == null)
            return null;

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void Add(Dictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values[key] = value.Trim();
    }
}
