// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Writes AI-readable JSON and preview sidecar parts into native .tcom/.tdom packages.
// Root JSON persistence parts are authoritative for modern packages; these sidecars are context snapshots.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.Composer.JsonInterchange;
using Instrumind.ThinkComposer.Definitor;
using Instrumind.ThinkComposer.Definitor.DomainJsonInterchange;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.ContainerSnapshots
{
    /// <summary>
    /// Adds non-authoritative, AI-readable JSON and PNG preview sidecars to native document packages.
    /// </summary>
    public static class ContainerSnapshotService
    {
        public static readonly Uri ManifestPartUri = new Uri("/Interchange/manifest.json", UriKind.Relative);
        public static readonly Uri CompositionJsonPartUri = new Uri("/Interchange/Composition.json", UriKind.Relative);
        public static readonly Uri DomainJsonPartUri = new Uri("/Interchange/Domain.json", UriKind.Relative);
        public static readonly Uri TemplateCompositionJsonPartUri = new Uri("/Interchange/TemplateComposition.json", UriKind.Relative);

        private const string ManifestFormat = "ThinkComposer.ContainerSnapshot";
        private const int ManifestFormatVersion = 1;
        private const string JsonContentType = "application/json";
        private const string PngContentType = "image/png";
        private const int MaxPreviewViews = 20;
        private const int PreviewWidth = 1600;
        private const int PreviewHeight = 1200;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteCompositionSnapshot(Package Package, Composition Composition, Uri NativePartUri)
        {
            if (Package == null || Composition == null)
                return;

            var Manifest = CreateManifest("composition", Composition, NativePartUri, HashPart(Package, NativePartUri));

            WriteCompositionJsonPart(Package, Manifest, CompositionJsonPartUri, "composition", Composition);

            if (Composition.CompositeContentDomain != null)
                WriteDomainJsonPart(Package, Manifest, DomainJsonPartUri, "embeddedDomain", Composition.CompositeContentDomain);
            else
                AddWarning(Manifest, "Composition has no embedded domain to write as /Interchange/Domain.json.");

            WriteCompositionPreviews(Package, Manifest, Composition);
            WriteManifestPart(Package, Manifest);
            LogManifestSummary(Manifest);
        }

        public static void WriteDomainSnapshot(Package Package, Domain Domain, Uri NativePartUri, bool IncludeTemplateComposition)
        {
            if (Package == null || Domain == null)
                return;

            var Manifest = CreateManifest("domain", Domain, NativePartUri, HashPart(Package, NativePartUri));

            WriteDomainJsonPart(Package, Manifest, DomainJsonPartUri, "domain", Domain);

            if (IncludeTemplateComposition && Domain.OwnerComposition != null)
            {
                WriteCompositionJsonPart(Package, Manifest, TemplateCompositionJsonPartUri, "templateComposition", Domain.OwnerComposition);
                WriteCompositionPreviews(Package, Manifest, Domain.OwnerComposition);
            }
            else
                AddWarning(Manifest, "Domain template composition JSON/previews were skipped because no template composition was included in this .tdom save.");

            WriteManifestPart(Package, Manifest);
            LogManifestSummary(Manifest);
        }

        private static ContainerSnapshotManifest CreateManifest(string PackageKind, IFormalizedRecognizableElement Source, Uri NativePartUri, string NativeHash)
        {
            var Manifest = new ContainerSnapshotManifest();
            Manifest.GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Manifest.ApplicationVersion = AppExec.ApplicationVersion;
            Manifest.PackageKind = PackageKind;
            Manifest.NativePartUri = NativePartUri == null ? null : NativePartUri.ToString();
            Manifest.NativePartSha256 = NativeHash;
            Manifest.Source = CreateSource(Source);
            return Manifest;
        }

        private static ContainerSnapshotSource CreateSource(IFormalizedRecognizableElement Source)
        {
            if (Source == null)
                return null;

            var Result = new ContainerSnapshotSource();
            Result.Id = Source.GlobalId.ToString("D");
            Result.Name = Source.Name;
            Result.TechName = Source.TechName;
            Result.Summary = Source.Summary;
            if (Source.Version != null)
            {
                Result.VersionNumber = Source.Version.VersionNumber == null ? null : Source.Version.VersionNumber.ToString();
                Result.VersionSequence = Source.Version.VersionSequence;
                Result.LastModification = Source.Version.LastModification.ToString("o", CultureInfo.InvariantCulture);
            }
            return Result;
        }

        private static void WriteCompositionJsonPart(Package Package, ContainerSnapshotManifest Manifest, Uri PartUri, string Kind, Composition Composition)
        {
            try
            {
                var Json = CompositionJsonSerializer.Serialize(CompositionJsonExporter.Export(Composition));
                WriteTextPart(Package, PartUri, Json);
                AddJsonPart(Manifest, Kind, PartUri, Json);
                Console.WriteLine("AI-readable container snapshot wrote {0}.", PartUri);
            }
            catch (Exception Problem)
            {
                AppExec.LogException(Problem, "AI-readable Composition JSON snapshot");
                AddWarning(Manifest, "Composition JSON sidecar skipped for '" + Kind + "': " + Problem.Message);
            }
        }

        private static void WriteDomainJsonPart(Package Package, ContainerSnapshotManifest Manifest, Uri PartUri, string Kind, Domain Domain)
        {
            try
            {
                var Json = DomainJsonSerializer.Serialize(DomainJsonExporter.Export(Domain));
                WriteTextPart(Package, PartUri, Json);
                AddJsonPart(Manifest, Kind, PartUri, Json);
                Console.WriteLine("AI-readable container snapshot wrote {0}.", PartUri);
            }
            catch (Exception Problem)
            {
                AppExec.LogException(Problem, "AI-readable Domain JSON snapshot");
                AddWarning(Manifest, "Domain JSON sidecar skipped for '" + Kind + "': " + Problem.Message);
            }
        }

        private static void WriteCompositionPreviews(Package Package, ContainerSnapshotManifest Manifest, Composition Composition)
        {
            var Views = GetAllViews(Composition).ToList();
            var Written = 0;

            foreach (var View in Views.Take(MaxPreviewViews))
            {
                var Preview = new ContainerSnapshotPreview();
                Preview.ViewId = IdOf(View);
                Preview.ViewName = View.Name;
                Preview.ViewTechName = View.TechName;
                Preview.Width = PreviewWidth;
                Preview.Height = PreviewHeight;
                Preview.Capped = true;

                try
                {
                    var Snapshot = View.ToSnapshot(false, PreviewWidth, PreviewHeight);
                    if (Snapshot == null)
                    {
                        Preview.Skipped = true;
                        Preview.Warning = "View has no renderable content.";
                        Manifest.Previews.Add(Preview);
                        continue;
                    }

                    var Bytes = RenderPng(Snapshot.Item1.RenderToDrawingVisual(), PreviewWidth, PreviewHeight);
                    var PartUri = UniquePreviewUri(Manifest, View);
                    WriteBinaryPart(Package, PartUri, PngContentType, Bytes, CompressionOption.Normal);

                    Preview.PartUri = PartUri.ToString();
                    Preview.Sha256 = HashBytes(Bytes);
                    Preview.Bytes = Bytes.Length;
                    Manifest.Previews.Add(Preview);
                    Written++;

                    Console.WriteLine("AI-readable container snapshot wrote preview {0} for view '{1}'.", PartUri, View.TechName);
                }
                catch (Exception Problem)
                {
                    AppExec.LogException(Problem, "AI-readable view preview snapshot");
                    Preview.Skipped = true;
                    Preview.Warning = Problem.Message;
                    Manifest.Previews.Add(Preview);
                    AddWarning(Manifest, "Preview skipped for view '" + View.TechName.ToStringAlways() + "': " + Problem.Message);
                }
            }

            var SkippedByCap = Views.Count - Math.Min(Views.Count, MaxPreviewViews);
            if (SkippedByCap > 0)
                AddWarning(Manifest, SkippedByCap.ToString(CultureInfo.InvariantCulture) + " view preview(s) skipped by the " +
                                     MaxPreviewViews.ToString(CultureInfo.InvariantCulture) + "-view snapshot cap.");

            Console.WriteLine("AI-readable container snapshot preview summary: written={0}, skipped={1}.",
                              Written, Manifest.Previews.Count(Preview => Preview.Skipped) + SkippedByCap);
        }

        private static byte[] RenderPng(System.Windows.Media.Visual Visual, int Width, int Height)
        {
            using (var Stream = new MemoryStream())
            {
                var Error = Display.ExportImageTo(new PngBitmapEncoder(), Stream, Visual, Width, Height);
                if (!Error.IsAbsent())
                    throw new InvalidOperationException(Error);

                return Stream.ToArray();
            }
        }

        private static IEnumerable<View> GetAllViews(Composition Composition)
        {
            if (Composition == null)
                return Enumerable.Empty<View>();

            var Views = Composition.GetSubgraphChildren()
                                   .Where(Idea => Idea != null && Idea.CompositeViews != null)
                                   .SelectMany(Idea => Idea.CompositeViews)
                                   .Where(View => View != null)
                                   .Distinct()
                                   .OrderBy(View => View.Name ?? "")
                                   .ThenBy(View => View.TechName ?? "")
                                   .ThenBy(View => IdOf(View))
                                   .ToList();

            if (Composition.RootView != null && !Views.Contains(Composition.RootView))
                Views.Insert(0, Composition.RootView);

            if (Composition.ActiveView != null && !Views.Contains(Composition.ActiveView))
                Views.Insert(0, Composition.ActiveView);

            return Views;
        }

        private static Uri UniquePreviewUri(ContainerSnapshotManifest Manifest, View View)
        {
            var BaseName = SafePathSegment((View.TechName ?? View.Name).AbsentDefault("View"));
            var IdText = IdOf(View);
            if (!IdText.IsAbsent() && IdText.Length >= 8)
                BaseName = BaseName + "-" + IdText.Substring(0, 8);

            var Candidate = "/Previews/views/" + BaseName + ".png";
            var Index = 2;
            while (Manifest.Previews.Any(Preview => Preview.PartUri == Candidate))
            {
                Candidate = "/Previews/views/" + BaseName + "-" + Index.ToString(CultureInfo.InvariantCulture) + ".png";
                Index++;
            }

            return new Uri(Candidate, UriKind.Relative);
        }

        private static string SafePathSegment(string Text)
        {
            if (Text.IsAbsent())
                return "item";

            var Builder = new StringBuilder();
            foreach (var Character in Text)
                if (Char.IsLetterOrDigit(Character) || Character == '_' || Character == '-')
                    Builder.Append(Character);
                else
                    Builder.Append('_');

            var Result = Builder.ToString().Trim('_');
            return Result.IsAbsent() ? "item" : Result;
        }

        private static void WriteManifestPart(Package Package, ContainerSnapshotManifest Manifest)
        {
            try
            {
                WriteTextPart(Package, ManifestPartUri, SerializeManifest(Manifest));
                Console.WriteLine("AI-readable container snapshot manifest written to {0}.", ManifestPartUri);
            }
            catch (Exception Problem)
            {
                AppExec.LogException(Problem, "AI-readable container snapshot manifest");
                Console.WriteLine("AI-readable container snapshot warning: manifest was not written. " + Problem.Message);
            }
        }

        private static void LogManifestSummary(ContainerSnapshotManifest Manifest)
        {
            Console.WriteLine("AI-readable container snapshot summary: packageKind={0}, jsonParts={1}, previews={2}, warnings={3}.",
                              Manifest.PackageKind,
                              Manifest.JsonParts.Count,
                              Manifest.Previews.Count(Preview => !Preview.Skipped),
                              Manifest.Warnings.Count);

            foreach (var Warning in Manifest.Warnings)
                Console.WriteLine("AI-readable container snapshot warning: " + Warning);
        }

        private static void AddJsonPart(ContainerSnapshotManifest Manifest, string Kind, Uri PartUri, string Json)
        {
            var Bytes = Utf8NoBom.GetBytes(Json);
            var Part = new ContainerSnapshotJsonPart();
            Part.Kind = Kind;
            Part.PartUri = PartUri.ToString();
            Part.Sha256 = HashBytes(Bytes);
            Part.Bytes = Bytes.Length;
            Manifest.JsonParts.Add(Part);
        }

        private static void AddWarning(ContainerSnapshotManifest Manifest, string Warning)
        {
            if (Manifest != null && !Warning.IsAbsent() && !Manifest.Warnings.Contains(Warning))
                Manifest.Warnings.Add(Warning);
        }

        private static void WriteTextPart(Package Package, Uri PartUri, string Text)
        {
            WriteBinaryPart(Package, PartUri, JsonContentType, Utf8NoBom.GetBytes(Text ?? ""), CompressionOption.Maximum);
        }

        private static void WriteBinaryPart(Package Package, Uri PartUri, string ContentType, byte[] Bytes, CompressionOption Compression)
        {
            if (Package.PartExists(PartUri))
                Package.DeletePart(PartUri);

            var Part = Package.CreatePart(PartUri, ContentType, Compression);
            using (var Stream = Part.GetStream(FileMode.Create, FileAccess.Write))
                Stream.Write(Bytes, 0, Bytes.Length);
        }

        private static string HashPart(Package Package, Uri PartUri)
        {
            if (Package == null || PartUri == null || !Package.PartExists(PartUri))
                return null;

            using (var Stream = Package.GetPart(PartUri).GetStream(FileMode.Open, FileAccess.Read))
                return HashStream(Stream);
        }

        private static string HashStream(Stream Stream)
        {
            using (var Hash = SHA256.Create())
                return ToHex(Hash.ComputeHash(Stream));
        }

        private static string HashBytes(byte[] Bytes)
        {
            using (var Hash = SHA256.Create())
                return ToHex(Hash.ComputeHash(Bytes ?? new byte[0]));
        }

        private static string ToHex(byte[] Bytes)
        {
            var Builder = new StringBuilder(Bytes.Length * 2);
            foreach (var Byte in Bytes)
                Builder.Append(Byte.ToString("x2", CultureInfo.InvariantCulture));
            return Builder.ToString();
        }

        private static string IdOf(UniqueElement Element)
        {
            return Element == null ? null : Element.GlobalId.ToString("D");
        }

        private static string SerializeManifest(ContainerSnapshotManifest Manifest)
        {
            var Builder = new StringBuilder();
            WriteJsonValue(Builder, ToGraph(Manifest), 0);
            Builder.AppendLine();
            return Builder.ToString();
        }

        private static OrderedDictionary ToGraph(ContainerSnapshotManifest Manifest)
        {
            var Obj = NewObject();
            Add(Obj, "format", ManifestFormat);
            Add(Obj, "formatVersion", ManifestFormatVersion);
            Add(Obj, "generatedAtUtc", Manifest.GeneratedAtUtc);
            Add(Obj, "application", "ThinkComposer");
            AddIf(Obj, "applicationVersion", Manifest.ApplicationVersion);
            Add(Obj, "packageKind", Manifest.PackageKind);
            AddIf(Obj, "nativePartUri", Manifest.NativePartUri);
            AddIf(Obj, "nativePartSha256", Manifest.NativePartSha256);
            AddIf(Obj, "source", ToGraph(Manifest.Source));
            Add(Obj, "jsonParts", Manifest.JsonParts.Select(ToGraph).ToList());
            Add(Obj, "previews", Manifest.Previews.Select(ToGraph).ToList());
            Add(Obj, "warnings", Manifest.Warnings);
            return Obj;
        }

        private static OrderedDictionary ToGraph(ContainerSnapshotSource Source)
        {
            if (Source == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "id", Source.Id);
            AddIf(Obj, "name", Source.Name);
            AddIf(Obj, "techName", Source.TechName);
            AddIf(Obj, "summary", Source.Summary);
            AddIf(Obj, "versionNumber", Source.VersionNumber);
            AddIf(Obj, "versionSequence", Source.VersionSequence);
            AddIf(Obj, "lastModification", Source.LastModification);
            return Obj;
        }

        private static OrderedDictionary ToGraph(ContainerSnapshotJsonPart Part)
        {
            var Obj = NewObject();
            Add(Obj, "kind", Part.Kind);
            Add(Obj, "partUri", Part.PartUri);
            Add(Obj, "sha256", Part.Sha256);
            Add(Obj, "bytes", Part.Bytes);
            return Obj;
        }

        private static OrderedDictionary ToGraph(ContainerSnapshotPreview Preview)
        {
            var Obj = NewObject();
            AddIf(Obj, "viewId", Preview.ViewId);
            AddIf(Obj, "viewName", Preview.ViewName);
            AddIf(Obj, "viewTechName", Preview.ViewTechName);
            AddIf(Obj, "partUri", Preview.PartUri);
            Add(Obj, "width", Preview.Width);
            Add(Obj, "height", Preview.Height);
            Add(Obj, "capped", Preview.Capped);
            Add(Obj, "skipped", Preview.Skipped);
            AddIf(Obj, "warning", Preview.Warning);
            AddIf(Obj, "sha256", Preview.Sha256);
            AddIf(Obj, "bytes", Preview.Bytes);
            return Obj;
        }

        private static OrderedDictionary NewObject()
        {
            return new OrderedDictionary();
        }

        private static void Add(OrderedDictionary Obj, string Key, object Value)
        {
            Obj.Add(Key, Value);
        }

        private static void AddIf(OrderedDictionary Obj, string Key, object Value)
        {
            if (Value != null)
                Obj.Add(Key, Value);
        }

        private static void WriteJsonValue(StringBuilder Builder, object Value, int Indent)
        {
            if (Value == null)
            {
                Builder.Append("null");
                return;
            }

            if (Value is string)
            {
                WriteJsonString(Builder, (string)Value);
                return;
            }

            if (Value is bool)
            {
                Builder.Append(((bool)Value) ? "true" : "false");
                return;
            }

            if (Value is int || Value is long || Value is double || Value is decimal)
            {
                Builder.Append(Convert.ToString(Value, CultureInfo.InvariantCulture));
                return;
            }

            var Dictionary = Value as IDictionary;
            if (Dictionary != null)
            {
                Builder.Append("{");
                var First = true;
                foreach (DictionaryEntry Entry in Dictionary)
                {
                    if (!First)
                        Builder.Append(",");
                    Builder.AppendLine();
                    Builder.Append(' ', (Indent + 1) * 2);
                    WriteJsonString(Builder, Entry.Key.ToString());
                    Builder.Append(": ");
                    WriteJsonValue(Builder, Entry.Value, Indent + 1);
                    First = false;
                }
                if (!First)
                {
                    Builder.AppendLine();
                    Builder.Append(' ', Indent * 2);
                }
                Builder.Append("}");
                return;
            }

            var Items = Value as IEnumerable;
            if (Items != null)
            {
                Builder.Append("[");
                var First = true;
                foreach (var Item in Items)
                {
                    if (!First)
                        Builder.Append(",");
                    Builder.AppendLine();
                    Builder.Append(' ', (Indent + 1) * 2);
                    WriteJsonValue(Builder, Item, Indent + 1);
                    First = false;
                }
                if (!First)
                {
                    Builder.AppendLine();
                    Builder.Append(' ', Indent * 2);
                }
                Builder.Append("]");
                return;
            }

            WriteJsonString(Builder, Value.ToString());
        }

        private static void WriteJsonString(StringBuilder Builder, string Text)
        {
            Builder.Append('"');
            foreach (var Character in Text ?? "")
                switch (Character)
                {
                    case '"':
                        Builder.Append("\\\"");
                        break;
                    case '\\':
                        Builder.Append("\\\\");
                        break;
                    case '\b':
                        Builder.Append("\\b");
                        break;
                    case '\f':
                        Builder.Append("\\f");
                        break;
                    case '\n':
                        Builder.Append("\\n");
                        break;
                    case '\r':
                        Builder.Append("\\r");
                        break;
                    case '\t':
                        Builder.Append("\\t");
                        break;
                    default:
                        if (Character < 32)
                            Builder.Append("\\u" + ((int)Character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            Builder.Append(Character);
                        break;
                }
            Builder.Append('"');
        }

        private class ContainerSnapshotManifest
        {
            public string GeneratedAtUtc;
            public string ApplicationVersion;
            public string PackageKind;
            public string NativePartUri;
            public string NativePartSha256;
            public ContainerSnapshotSource Source;
            public List<ContainerSnapshotJsonPart> JsonParts = new List<ContainerSnapshotJsonPart>();
            public List<ContainerSnapshotPreview> Previews = new List<ContainerSnapshotPreview>();
            public List<string> Warnings = new List<string>();
        }

        private class ContainerSnapshotSource
        {
            public string Id;
            public string Name;
            public string TechName;
            public string Summary;
            public string VersionNumber;
            public int? VersionSequence;
            public string LastModification;
        }

        private class ContainerSnapshotJsonPart
        {
            public string Kind;
            public string PartUri;
            public string Sha256;
            public int Bytes;
        }

        private class ContainerSnapshotPreview
        {
            public string ViewId;
            public string ViewName;
            public string ViewTechName;
            public string PartUri;
            public int Width;
            public int Height;
            public bool Capped;
            public bool Skipped;
            public string Warning;
            public string Sha256;
            public int Bytes;
        }
    }
}
