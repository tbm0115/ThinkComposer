using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml.Linq;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    internal static class ModelJson
    {
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static object Parse(string text) { return new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 512 }.DeserializeObject(text); }
        internal static object Read(string path) { return Parse(File.ReadAllText(path, Utf8)); }
        internal static string Text(object value) { return Format(value, 0) + "\n"; }
        internal static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, Text(value), Utf8);
        }
        internal static Dictionary<string, object> Obj(object value) { return value as Dictionary<string, object>; }
        internal static IEnumerable<object> Array(object value) { return value is IEnumerable && !(value is string) && !(value is IDictionary) ? ((IEnumerable)value).Cast<object>() : Enumerable.Empty<object>(); }
        internal static object Get(object value, string name) { var d = Obj(value); object v; return d != null && d.TryGetValue(name, out v) ? v : null; }
        internal static string Str(object value, string name) { var v = Get(value, name); return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }
        internal static Dictionary<string, object> Map(params object[] pairs)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }
        internal static bool Equal(object a, object b) { return Text(a) == Text(b); }
        private static string Format(object value, int level)
        {
            var map = value as IDictionary<string, object>;
            var indent = new string(' ', level * 2); var next = indent + "  ";
            if (map != null)
            {
                if (map.Count == 0) return "{}";
                return "{\n" + String.Join(",\n", map.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => next + Format(p.Key, 0) + ": " + Format(p.Value, level + 1))) + "\n" + indent + "}";
            }
            if (value is IEnumerable && !(value is string))
            {
                var list = ((IEnumerable)value).Cast<object>().ToList(); if (list.Count == 0) return "[]";
                return "[\n" + String.Join(",\n", list.Select(v => next + Format(v, level + 1))) + "\n" + indent + "]";
            }
            return new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(value);
        }
        internal static string Hash(byte[] bytes) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static string FileHash(string path) { using (var s = File.OpenRead(path)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
    }

    // Raw ZIP entries preserve extension fields, assets and OPC relationships during merging.
    internal sealed class ModelPackage
    {
        internal Dictionary<string, byte[]> Parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        internal string SourcePath, SourceHash;
        internal string Kind { get { return ModelJson.Str(Manifest, "packageKind"); } }
        internal Dictionary<string, object> Manifest;
        internal static readonly string[] JsonParts = { "Composition.json", "Domain.json", "TemplateComposition.json" };

        internal static ModelPackage Read(string path)
        {
            var result = new ModelPackage { SourcePath = Path.GetFullPath(path), SourceHash = ModelJson.FileHash(path) };
            using (var stream = File.OpenRead(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    SafeName(entry.FullName);
                    using (var input = entry.Open()) using (var bytes = new MemoryStream())
                    { input.CopyTo(bytes); result.Parts.Add(entry.FullName, bytes.ToArray()); }
                }
            if (!result.Parts.ContainsKey("manifest.json")) throw new GitWorkflowException("LEGACY_PACKAGE", "Convert this container to JSON persistence before enabling managed tracking.");
            result.Manifest = ModelJson.Obj(ModelJson.Parse(ModelJson.Utf8.GetString(result.Parts["manifest.json"])));
            if (result.Manifest == null || ModelJson.Str(result.Manifest, "persistenceFormat") != "json" || !new[] { "composition", "domain" }.Contains(result.Kind))
                throw new GitWorkflowException("UNSUPPORTED_PACKAGE", "A JSON-authoritative ThinkComposer container is required.");
            return result;
        }

        internal static void SafeName(string name)
        {
            if (String.IsNullOrEmpty(name) || name.Contains("\\") || name.StartsWith("/") || name.Contains(":") || name.Split('/').Any(s => s == ".." || s == "." || s.Length == 0))
                throw new GitWorkflowException("INVALID_PART", "Unsafe package entry: " + name);
        }
        internal static bool Derived(string name)
        {
            return name == "manifest.json" || name.StartsWith("Previews/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Interchange/", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("docProps/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("package/services/metadata/", StringComparison.OrdinalIgnoreCase)
                || name == "Composition.bin" || name == "Domain.bin";
        }
        internal SortedDictionary<string, byte[]> Content()
        {
            var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var p in Parts.Where(p => !Derived(p.Key)))
            {
                var bytes = p.Value;
                if (JsonParts.Contains(p.Key))
                {
                    var graph = ModelJson.Obj(ModelJson.Parse(ModelJson.Utf8.GetString(bytes)));
                    if (graph == null) throw new GitWorkflowException("INVALID_JSON", p.Key + " must be an object.");
                    graph.Remove("exportedAtUtc");
                    bytes = ModelJson.Utf8.GetBytes(ModelJson.Text(graph));
                }
                else if (p.Key == "[Content_Types].xml" || p.Key.EndsWith(".rels", StringComparison.Ordinal))
                    bytes = CanonicalPackaging(p.Key, bytes);
                result.Add(p.Key, bytes);
            }
            return result;
        }
        private byte[] CanonicalPackaging(string name, byte[] bytes)
        {
            XDocument xml;
            using (var stream = new MemoryStream(bytes)) xml = XDocument.Load(stream);
            foreach (var element in xml.Root.Elements().ToArray())
            {
                var target = (string)element.Attribute("Target");
                var type = (string)element.Attribute("Type");
                var part = (string)element.Attribute("PartName");
                var extension = (string)element.Attribute("Extension");
                if ((target != null && Derived(target.TrimStart('/'))) || (type != null && type.EndsWith("/metadata/core-properties", StringComparison.Ordinal)) ||
                    (part != null && Derived(part.TrimStart('/'))) ||
                    (name == "[Content_Types].xml" && extension != null && !Parts.Keys.Any(k => !Derived(k) && k.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)))) element.Remove();
            }
            return ModelJson.Utf8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        }
        internal string Fingerprint()
        {
            var catalog = Content().Select(p => ModelJson.Map("path", p.Key, "sha256", ModelJson.Hash(p.Value))).ToArray();
            return ModelJson.Hash(ModelJson.Utf8.GetBytes(ModelJson.Text(ModelJson.Map("version", 1, "kind", Kind, "parts", catalog, "metadata", AuthoredMetadata(false)))));
        }
        internal object AuthoredMetadata(bool includeLegacyLinks)
        {
            var ignored = new HashSet<string>(new[] { "savedAtUtc", "source", "applicationVersion", "authoritativeParts", "sidecars", "legacyBinaryFallback", "modelTracking", "contentFingerprint", "warnings" });
            if (!includeLegacyLinks) { ignored.Add("gitSync"); ignored.Add("embeddedDomainGitSync"); }
            return Manifest.Where(p => !ignored.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        }
        internal void Stamp(string trackingId)
        {
            if (trackingId == null) { Manifest.Remove("modelTracking"); Manifest.Remove("contentFingerprint"); }
            else
            {
                Manifest["modelTracking"] = ModelJson.Map("version", 1, "id", trackingId);
                Manifest["contentFingerprint"] = ModelJson.Map("algorithm", "sha256", "formatVersion", 1, "value", Fingerprint());
            }
        }
        internal string TrackingId { get { return ModelJson.Str(ModelJson.Get(Manifest, "modelTracking"), "id"); } }

        internal void PreserveExtensions(ModelPackage previous)
        {
            foreach (var part in JsonParts.Where(n => Parts.ContainsKey(n) && previous.Parts.ContainsKey(n)))
            {
                var source = ModelJson.Utf8.GetString(previous.Parts[part]);
                var known = part == "Domain.json" ? Definitor.DomainJsonInterchange.DomainJsonSerializer.Serialize(Definitor.DomainJsonInterchange.DomainJsonSerializer.Deserialize(source)) : JsonInterchange.CompositionJsonSerializer.Serialize(JsonInterchange.CompositionJsonSerializer.Deserialize(source));
                var fresh = ModelJson.Parse(ModelJson.Utf8.GetString(Parts[part]));
                PreserveFields(ModelJson.Parse(source), ModelJson.Parse(known), fresh);
                Parts[part] = ModelJson.Utf8.GetBytes(ModelJson.Text(fresh));
            }
            foreach (var part in previous.Parts.Where(p => !Derived(p.Key) && !JsonParts.Contains(p.Key) && !Parts.ContainsKey(p.Key))) Parts.Add(part.Key, part.Value);
            foreach (var pair in ModelJson.Obj(previous.AuthoredMetadata(true))) if (!Manifest.ContainsKey(pair.Key)) Manifest[pair.Key] = pair.Value;
            foreach (var name in previous.Parts.Keys.Where(n => (n == "[Content_Types].xml" || n.EndsWith(".rels", StringComparison.Ordinal)) && Parts.ContainsKey(n)))
            {
                XDocument old, fresh;
                using (var stream = new MemoryStream(previous.CanonicalPackaging(name, previous.Parts[name]))) old = XDocument.Load(stream);
                using (var stream = new MemoryStream(Parts[name])) fresh = XDocument.Load(stream);
                foreach (var element in old.Root.Elements())
                {
                    var key = element.Attribute("PartName") ?? element.Attribute("Extension") ?? element.Attribute("Id");
                    if (key != null && !fresh.Root.Elements().Any(e => (string)e.Attribute(key.Name) == key.Value)) fresh.Root.Add(new XElement(element));
                }
                Parts[name] = ModelJson.Utf8.GetBytes(fresh.ToString(SaveOptions.DisableFormatting));
            }
        }
        private static void PreserveFields(object original, object known, object fresh)
        {
            var before = ModelJson.Obj(original); var understood = ModelJson.Obj(known); var after = ModelJson.Obj(fresh);
            if (before != null && understood != null && after != null)
            {
                foreach (var pair in before)
                    if (!understood.ContainsKey(pair.Key)) { if (!after.ContainsKey(pair.Key)) after[pair.Key] = pair.Value; }
                    else if (after.ContainsKey(pair.Key)) PreserveFields(pair.Value, understood[pair.Key], after[pair.Key]);
                return;
            }
            var oldList = ModelJson.Array(original).ToList(); var knownList = ModelJson.Array(known).ToList(); var newList = ModelJson.Array(fresh).ToList();
            for (int i = 0; i < oldList.Count; i++)
            {
                var id = ModelJson.Str(oldList[i], "id");
                var oldKnown = id == null ? knownList.ElementAtOrDefault(i) : knownList.FirstOrDefault(v => ModelJson.Str(v, "id") == id);
                var newItem = id == null ? newList.ElementAtOrDefault(i) : newList.FirstOrDefault(v => ModelJson.Str(v, "id") == id);
                if (oldKnown != null && newItem != null) PreserveFields(oldList[i], oldKnown, newItem);
            }
        }

        internal void Write(string path)
        {
            var authoritative = ModelJson.Array(ModelJson.Get(Manifest, "authoritativeParts")).ToList();
            foreach (var part in authoritative)
            {
                var uri = ModelJson.Str(part, "partUri"); byte[] bytes;
                if (uri != null && Parts.TryGetValue(uri.TrimStart('/'), out bytes))
                { ModelJson.Obj(part)["sha256"] = ModelJson.Hash(bytes); ModelJson.Obj(part)["bytes"] = bytes.Length; }
            }
            Manifest["authoritativeParts"] = authoritative.Where(p => Parts.ContainsKey((ModelJson.Str(p, "partUri") ?? "").TrimStart('/'))).ToArray();
            Parts["manifest.json"] = ModelJson.Utf8.GetBytes(ModelJson.Text(Manifest));
            using (var output = File.Create(path))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
                foreach (var part in Parts.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var entry = zip.CreateEntry(part.Key, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using (var target = entry.Open()) target.Write(part.Value, 0, part.Value.Length);
                }
        }

        internal void Validate()
        {
            if (!Parts.ContainsKey("Domain.json") || (Kind == "composition" && !Parts.ContainsKey("Composition.json")))
                throw new GitWorkflowException("MISSING_PAYLOAD", "Required model payload is missing.");
            foreach (var name in JsonParts.Where(Parts.ContainsKey))
            {
                var graph = ModelJson.Obj(ModelJson.Parse(ModelJson.Utf8.GetString(Parts[name])));
                if (graph == null || !new[] { "1", "2" }.Contains(ModelJson.Str(graph, "formatVersion"))) throw new GitWorkflowException("UNSUPPORTED_FORMAT", name);
                if (name == "Domain.json")
                    Definitor.DomainJsonInterchange.DomainJsonSerializer.Validate(Definitor.DomainJsonInterchange.DomainJsonSerializer.Deserialize(ModelJson.Utf8.GetString(Parts[name])));
                else
                    JsonInterchange.CompositionJsonSerializer.Validate(JsonInterchange.CompositionJsonSerializer.Deserialize(ModelJson.Utf8.GetString(Parts[name])));
                if (ModelJson.Array(ModelJson.Get(graph, "operations")).Any()) throw new GitWorkflowException("OPERATIONS_IN_SNAPSHOT", "Projection payloads are complete snapshots, not operation queues.");
                ModelMerge.ValidateGraph(name, graph, Parts);
            }
        }
    }
}
