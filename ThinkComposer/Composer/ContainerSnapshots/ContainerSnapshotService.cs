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
using System.Web.Script.Serialization;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.ApplicationShell;
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
        private const int ManifestFormatVersion = 2;
        private const string JsonContentType = "application/json";
        private const string PngContentType = "image/png";
        private const int MaxPreviewViews = 20;
        private const int PreviewWidth = 1600;
        private const int PreviewHeight = 1200;
        private const int MaxCachedPreviewBytes = 64 * 1024 * 1024;
        private const int MaxCachedPreviewTotalBytes = 256 * 1024 * 1024;
        private const string PreviewRenderProfile = "ThinkComposer.WpfPng/1;1600x1200";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteCompositionSnapshot(Package Package, Composition Composition, Uri NativePartUri)
        {
            WriteCompositionSnapshot(Package, Composition, NativePartUri, null, null, null);
        }

        internal static void WriteCompositionSnapshot(Package Package, Composition Composition, Uri NativePartUri,
                                                      JsonPackagePersistence.PersistenceExportPayload CompositionPayload,
                                                      JsonPackagePersistence.PersistenceExportPayload DomainPayload,
                                                      PreviousPreviewCache PreviousPreviews)
        {
            if (Package == null || Composition == null)
                return;

            var Manifest = CreateManifest("composition", Composition, NativePartUri,
                                          CompositionPayload == null ? HashPart(Package, NativePartUri) : CompositionPayload.Sha256);

            if (CompositionPayload == null)
                WriteCompositionJsonPart(Package, Manifest, CompositionJsonPartUri, "composition", Composition);
            else
                WriteExportPayloadPart(Package, Manifest, CompositionJsonPartUri, "composition", CompositionPayload);

            if (Composition.CompositeContentDomain != null)
            {
                if (DomainPayload == null)
                    WriteDomainJsonPart(Package, Manifest, DomainJsonPartUri, "embeddedDomain", Composition.CompositeContentDomain);
                else
                    WriteExportPayloadPart(Package, Manifest, DomainJsonPartUri, "embeddedDomain", DomainPayload);
            }
            else
                AddWarning(Manifest, "Composition has no embedded domain to write as /Interchange/Domain.json.");

            WriteCompositionPreviews(Package, Manifest, Composition, CompositionPayload, DomainPayload, PreviousPreviews);
            WriteManifestPart(Package, Manifest);
            LogManifestSummary(Manifest);
        }

        public static void WriteDomainSnapshot(Package Package, Domain Domain, Uri NativePartUri, bool IncludeTemplateComposition)
        {
            WriteDomainSnapshot(Package, Domain, NativePartUri, IncludeTemplateComposition, null, null, null);
        }

        internal static void WriteDomainSnapshot(Package Package, Domain Domain, Uri NativePartUri, bool IncludeTemplateComposition,
                                                 JsonPackagePersistence.PersistenceExportPayload DomainPayload,
                                                 JsonPackagePersistence.PersistenceExportPayload TemplateCompositionPayload,
                                                 PreviousPreviewCache PreviousPreviews)
        {
            if (Package == null || Domain == null)
                return;

            var Manifest = CreateManifest("domain", Domain, NativePartUri,
                                          DomainPayload == null ? HashPart(Package, NativePartUri) : DomainPayload.Sha256);

            if (DomainPayload == null)
                WriteDomainJsonPart(Package, Manifest, DomainJsonPartUri, "domain", Domain);
            else
                WriteExportPayloadPart(Package, Manifest, DomainJsonPartUri, "domain", DomainPayload);

            if (IncludeTemplateComposition && Domain.OwnerComposition != null)
            {
                if (TemplateCompositionPayload == null)
                    WriteCompositionJsonPart(Package, Manifest, TemplateCompositionJsonPartUri, "templateComposition", Domain.OwnerComposition);
                else
                    WriteExportPayloadPart(Package, Manifest, TemplateCompositionJsonPartUri, "templateComposition", TemplateCompositionPayload);
                WriteCompositionPreviews(Package, Manifest, Domain.OwnerComposition,
                                         TemplateCompositionPayload, DomainPayload, PreviousPreviews);
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

        private static void WriteExportPayloadPart(Package Package, ContainerSnapshotManifest Manifest, Uri PartUri, string Kind,
                                                   JsonPackagePersistence.PersistenceExportPayload Payload)
        {
            try
            {
                if (Payload == null || Payload.Utf8Bytes == null)
                    throw new InvalidOperationException("The reusable JSON export payload is unavailable.");

                WriteBinaryPart(Package, PartUri, JsonContentType, Payload.Utf8Bytes, CompressionOption.Maximum);
                AddJsonPart(Manifest, Kind, PartUri, Payload.Utf8Bytes, Payload.Sha256);
                Console.WriteLine("AI-readable container snapshot reused authoritative JSON for {0}.", PartUri);
            }
            catch (Exception Problem)
            {
                AppExec.LogException(Problem, "AI-readable reusable JSON snapshot");
                AddWarning(Manifest, "JSON sidecar skipped for '" + Kind + "': " + Problem.Message);
            }
        }

        /// <summary>
        /// Reads and verifies the bounded preview cache before a new destination package is created.
        /// Invalid, v1, incomplete, or corrupt cache data is deliberately treated as a cache miss.
        /// </summary>
        internal static PreviousPreviewCache LoadPreviousPreviewCache(Uri SourceLocation)
        {
            var Result = new PreviousPreviewCache();
            if (SourceLocation == null)
                return Result;

            try
            {
                var SourcePath = SourceLocation.IsAbsoluteUri && SourceLocation.IsFile
                               ? SourceLocation.LocalPath
                               : null;
                if (String.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
                    return Result;

                using (var Package = System.IO.Packaging.Package.Open(SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (!Package.PartExists(ManifestPartUri))
                        return Result;

                    string ManifestJson;
                    using (var Stream = Package.GetPart(ManifestPartUri).GetStream(FileMode.Open, FileAccess.Read))
                    using (var Reader = new StreamReader(Stream, Encoding.UTF8, true))
                        ManifestJson = Reader.ReadToEnd();

                    var Serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
                    var Root = Serializer.DeserializeObject(ManifestJson) as IDictionary<string, object>;
                    if (Root == null ||
                        !String.Equals(GetManifestString(Root, "format"), ManifestFormat, StringComparison.Ordinal) ||
                        GetManifestInt(Root, "formatVersion") != ManifestFormatVersion)
                        return Result;

                    object PreviewGraph;
                    var PreviewItems = (Root.TryGetValue("previews", out PreviewGraph) ? PreviewGraph as IEnumerable : null);
                    if (PreviewItems == null)
                        return Result;

                    var TotalBytes = 0;
                    var Inspected = 0;
                    var Invalid = 0;
                    foreach (var Item in PreviewItems)
                    {
                        if (Inspected++ >= MaxPreviewViews)
                            break;

                        var Graph = Item as IDictionary<string, object>;
                        var Entry = TryLoadCachedPreview(Package, Graph, ref TotalBytes);
                        if (Entry == null)
                        {
                            Invalid++;
                            continue;
                        }

                        if (!Result.PreviewsByViewId.ContainsKey(Entry.ViewId))
                            Result.PreviewsByViewId.Add(Entry.ViewId, Entry);
                    }

                    if (Invalid > 0)
                        Result.LoadWarning = Invalid.ToString(CultureInfo.InvariantCulture) +
                                             " invalid preview cache entr" + (Invalid == 1 ? "y was" : "ies were") + " ignored.";
                }
            }
            catch (Exception Problem)
            {
                Result.PreviewsByViewId.Clear();
                Result.LoadWarning = "Previous preview cache could not be read and will be regenerated: " + Problem.Message;
            }

            return Result;
        }

        private static CachedPreview TryLoadCachedPreview(Package Package, IDictionary<string, object> Graph, ref int TotalBytes)
        {
            if (Graph == null)
                return null;

            var ViewId = GetManifestString(Graph, "viewId");
            var InputSha256 = GetManifestString(Graph, "inputSha256");
            var RenderProfile = GetManifestString(Graph, "renderProfile");
            var Disposition = GetManifestString(Graph, "disposition");
            var Width = GetManifestInt(Graph, "width");
            var Height = GetManifestInt(Graph, "height");
            var Skipped = GetManifestBool(Graph, "skipped");

            if (String.IsNullOrWhiteSpace(ViewId) || !IsSha256(InputSha256) ||
                String.IsNullOrWhiteSpace(RenderProfile) || Width == null || Height == null)
                return null;

            if (String.Equals(Disposition, "empty", StringComparison.Ordinal) && Skipped == true)
                return new CachedPreview
                {
                    ViewId = ViewId,
                    InputSha256 = InputSha256,
                    RenderProfile = RenderProfile,
                    Width = Width.Value,
                    Height = Height.Value,
                    IsEmpty = true
                };

            if (Skipped != false ||
                (!String.Equals(Disposition, "rendered", StringComparison.Ordinal) &&
                 !String.Equals(Disposition, "reused", StringComparison.Ordinal)))
                return null;

            var PartUriText = GetManifestString(Graph, "partUri");
            var ByteCount = GetManifestInt(Graph, "bytes");
            var PngSha256 = GetManifestString(Graph, "sha256");
            if (String.IsNullOrWhiteSpace(PartUriText) || ByteCount == null || ByteCount.Value < 0 ||
                ByteCount.Value > MaxCachedPreviewBytes || TotalBytes + ByteCount.Value > MaxCachedPreviewTotalBytes ||
                !IsSha256(PngSha256) || !IsSafePreviewPartUri(PartUriText))
                return null;

            var PartUri = new Uri(PartUriText, UriKind.Relative);
            if (!Package.PartExists(PartUri))
                return null;

            var Part = Package.GetPart(PartUri);
            if (!String.Equals(Part.ContentType, PngContentType, StringComparison.OrdinalIgnoreCase))
                return null;

            byte[] Bytes;
            using (var Stream = Part.GetStream(FileMode.Open, FileAccess.Read))
            using (var Buffer = new MemoryStream())
            {
                Stream.CopyTo(Buffer);
                if (Buffer.Length != ByteCount.Value || Buffer.Length > MaxCachedPreviewBytes)
                    return null;
                Bytes = Buffer.ToArray();
            }

            if (!String.Equals(HashBytes(Bytes), PngSha256, StringComparison.OrdinalIgnoreCase))
                return null;

            TotalBytes += Bytes.Length;
            return new CachedPreview
            {
                ViewId = ViewId,
                InputSha256 = InputSha256,
                RenderProfile = RenderProfile,
                Width = Width.Value,
                Height = Height.Value,
                PartUri = PartUriText,
                PngSha256 = PngSha256,
                Bytes = Bytes
            };
        }

        private static void WriteCompositionPreviews(Package Package, ContainerSnapshotManifest Manifest, Composition Composition,
                                                     JsonPackagePersistence.PersistenceExportPayload CompositionPayload,
                                                     JsonPackagePersistence.PersistenceExportPayload DomainPayload,
                                                     PreviousPreviewCache PreviousPreviews)
        {
            var AllViews = GetAllViews(Composition).ToList();
            var Views = AllViews.Take(MaxPreviewViews).ToList();
            Dictionary<string, string> InputHashes;
            using (PersistenceOperationContext.MeasureCurrentStage(PersistenceOperationStages.SavePreviewInputHash))
                InputHashes = CreatePreviewInputHashes(Composition, CompositionPayload, DomainPayload, Views);
            var Written = 0;
            var Reused = 0;

            if (PreviousPreviews != null && !String.IsNullOrWhiteSpace(PreviousPreviews.LoadWarning))
                AddWarning(Manifest, PreviousPreviews.LoadWarning);

            foreach (var View in Views)
            {
                var Preview = new ContainerSnapshotPreview();
                Preview.ViewId = IdOf(View);
                Preview.ViewName = View.Name;
                Preview.ViewTechName = View.TechName;
                Preview.Width = PreviewWidth;
                Preview.Height = PreviewHeight;
                Preview.Capped = true;
                Preview.RenderProfile = PreviewRenderProfile;
                Preview.InputSha256 = InputHashes[Preview.ViewId];

                try
                {
                    var PartUri = UniquePreviewUri(Manifest, View);
                    CachedPreview Cached;
                    var ReusedCachedPreview = false;
                    using (PersistenceOperationContext.MeasureCurrentStage(PersistenceOperationStages.SavePreviewReuse))
                    {
                        if (PreviousPreviews != null &&
                            PreviousPreviews.TryGetReusable(Preview.ViewId, Preview.InputSha256, Preview.RenderProfile,
                                                            Preview.Width, Preview.Height, PartUri.ToString(), out Cached))
                        {
                            if (Cached.IsEmpty)
                            {
                                Preview.Skipped = true;
                                Preview.Warning = "View has no renderable content.";
                                Preview.Disposition = "empty";
                            }
                            else
                            {
                                WriteBinaryPart(Package, PartUri, PngContentType, Cached.Bytes, CompressionOption.Normal);
                                Preview.PartUri = PartUri.ToString();
                                Preview.Sha256 = Cached.PngSha256;
                                Preview.Bytes = Cached.Bytes.Length;
                                Preview.Disposition = "reused";
                                Reused++;
                            }

                            ReusedCachedPreview = true;
                        }
                    }

                    if (ReusedCachedPreview)
                    {
                        Manifest.Previews.Add(Preview);
                        continue;
                    }

                    byte[] Bytes = null;
                    string PngSha256 = null;
                    using (PersistenceOperationContext.MeasureCurrentStage(PersistenceOperationStages.SavePreviewRender))
                    {
                        var Snapshot = View.ToSnapshot(false, PreviewWidth, PreviewHeight);
                        if (Snapshot != null)
                        {
                            Bytes = RenderPng(Snapshot.Item1.RenderToDrawingVisual(), PreviewWidth, PreviewHeight);
                            PngSha256 = HashBytes(Bytes);
                        }
                    }

                    if (Bytes == null)
                    {
                        Preview.Skipped = true;
                        Preview.Warning = "View has no renderable content.";
                        Preview.Disposition = "empty";
                        Manifest.Previews.Add(Preview);
                        continue;
                    }

                    WriteBinaryPart(Package, PartUri, PngContentType, Bytes, CompressionOption.Normal);

                    Preview.PartUri = PartUri.ToString();
                    Preview.Sha256 = PngSha256;
                    Preview.Bytes = Bytes.Length;
                    Preview.Disposition = "rendered";
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

            var SkippedByCap = AllViews.Count - Views.Count;
            if (SkippedByCap > 0)
                AddWarning(Manifest, SkippedByCap.ToString(CultureInfo.InvariantCulture) + " view preview(s) skipped by the " +
                                     MaxPreviewViews.ToString(CultureInfo.InvariantCulture) + "-view snapshot cap.");

            Console.WriteLine("AI-readable container snapshot preview summary: written={0}, reused={1}, empty={2}, skipped={3}.",
                              Written, Reused,
                              Manifest.Previews.Count(Preview => String.Equals(Preview.Disposition, "empty", StringComparison.Ordinal)),
                              Manifest.Previews.Count(Preview => Preview.Skipped &&
                                  !String.Equals(Preview.Disposition, "empty", StringComparison.Ordinal)) + SkippedByCap);
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
                                   .ToList();

            if (Composition.RootView != null && !Views.Contains(Composition.RootView))
                Views.Add(Composition.RootView);

            if (Composition.ActiveView != null && !Views.Contains(Composition.ActiveView))
                Views.Add(Composition.ActiveView);

            // Active/selected UI state must not reorder the preview cap or invalidate cache reuse.
            return Views.OrderBy(View => Object.ReferenceEquals(View, Composition.RootView) ? 0 : 1)
                        .ThenBy(View => View.Name ?? "")
                        .ThenBy(View => View.TechName ?? "")
                        .ThenBy(View => IdOf(View))
                        .ToList();
        }

        private static Dictionary<string, string> CreatePreviewInputHashes(Composition Composition,
                                                                           JsonPackagePersistence.PersistenceExportPayload CompositionPayload,
                                                                           JsonPackagePersistence.PersistenceExportPayload DomainPayload,
                                                                           IEnumerable<View> Views)
        {
            var Result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var Document = (CompositionPayload == null ? null : CompositionPayload.Document as CompositionJsonDocument);
            if (Document == null)
                Document = CompositionJsonExporter.Export(Composition);

            var DomainDocument = (DomainPayload == null ? null : DomainPayload.Document as DomainJsonDocument);
            if (DomainDocument == null && Composition != null && Composition.CompositeContentDomain != null)
                DomainDocument = DomainJsonExporter.Export(Composition.CompositeContentDomain);

            var DomainHash = CreateNormalizedDomainHash(DomainDocument, DomainPayload);
            var HashIndex = new PreviewHashIndex(Document);
            var ActualViewsById = new Dictionary<string, View>(StringComparer.OrdinalIgnoreCase);
            var ActualIdeas = Composition == null
                            ? Enumerable.Empty<Idea>()
                            : Composition.DeclaredIdeas.Where(Idea => Idea != null);
            var ActualIdeasById = FirstByKey(ActualIdeas, IdOf);
            var ActualIdeasByTechName = FirstByKey(ActualIdeas, Idea => Idea.TechName);
            var ViewRenderStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ActualView in GetAllViews(Composition))
            {
                var ActualViewId = IdOf(ActualView);
                if (!String.IsNullOrWhiteSpace(ActualViewId) && !ActualViewsById.ContainsKey(ActualViewId))
                    ActualViewsById.Add(ActualViewId, ActualView);
            }

            foreach (var View in Views ?? Enumerable.Empty<View>())
            {
                var ViewId = IdOf(View);
                CompositionJsonView SourceView;
                string Hash;
                if (HashIndex.ViewsById.TryGetValue(ViewId, out SourceView))
                    Hash = CreateViewInputHash(Document, SourceView, DomainHash, HashIndex,
                                               ActualViewsById, ActualIdeasById, ActualIdeasByTechName,
                                               ViewRenderStates);
                else
                {
                    var RenderState = GetViewRenderState(ViewId, ActualViewsById, ViewRenderStates);
                    Hash = HashText(PreviewRenderProfile + "\n" + DomainHash + "\n" +
                                    (CompositionPayload == null ? "" : CompositionPayload.Sha256) + "\n" + ViewId + "\n" +
                                    RenderState);
                }

                Result[ViewId] = Hash;
            }

            return Result;
        }

        private static string CreateNormalizedDomainHash(DomainJsonDocument Source,
                                                         JsonPackagePersistence.PersistenceExportPayload Payload)
        {
            if (Payload != null && !String.IsNullOrWhiteSpace(Payload.PreviewInputSha256))
                return Payload.PreviewInputSha256;

            if (Source == null)
                return Payload == null ? "" : Payload.Sha256.ToStringAlways("");

            // Compatibility callers without a native export bundle still serialize exactly once;
            // native saves reuse the cached hash computed from their authoritative canonical JSON.
            return JsonPackagePersistence.CreateNormalizedDomainPreviewHash(
                DomainJsonSerializer.Serialize(Source));
        }

        private static string CreateViewInputHash(CompositionJsonDocument Source, CompositionJsonView RootView,
                                                  string DomainHash, PreviewHashIndex Index,
                                                  IDictionary<string, View> ActualViewsById,
                                                  IDictionary<string, Idea> ActualIdeasById,
                                                  IDictionary<string, Idea> ActualIdeasByTechName,
                                                  IDictionary<string, string> ViewRenderStates)
        {
            if (Index == null)
                Index = new PreviewHashIndex(Source);

            var SelectedIdeas = new HashSet<CompositionJsonIdea>();
            var SelectedRelationships = new HashSet<CompositionJsonRelationship>();
            var SelectedViews = new HashSet<CompositionJsonView>();
            var PendingIdeas = new Queue<CompositionJsonIdea>();
            var PendingRelationships = new Queue<CompositionJsonRelationship>();
            var PendingViews = new Queue<CompositionJsonView>();
            var CompositeActiveViews = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Action<string> AddView = Id =>
            {
                CompositionJsonView Match;
                if (!String.IsNullOrWhiteSpace(Id) && Index.ViewsById.TryGetValue(Id, out Match) && SelectedViews.Add(Match))
                    PendingViews.Enqueue(Match);
            };

            Action<string, string> AddSemantic = (Id, TechName) =>
            {
                CompositionJsonIdea Idea;
                CompositionJsonRelationship Relationship;
                if ((!String.IsNullOrWhiteSpace(Id) && Index.IdeasById.TryGetValue(Id, out Idea)) ||
                    (!String.IsNullOrWhiteSpace(TechName) && Index.IdeasByTechName.TryGetValue(TechName, out Idea)))
                {
                    if (SelectedIdeas.Add(Idea))
                        PendingIdeas.Enqueue(Idea);
                    return;
                }

                if ((!String.IsNullOrWhiteSpace(Id) && Index.RelationshipsById.TryGetValue(Id, out Relationship)) ||
                    (!String.IsNullOrWhiteSpace(TechName) && Index.RelationshipsByTechName.TryGetValue(TechName, out Relationship)))
                    if (SelectedRelationships.Add(Relationship))
                        PendingRelationships.Enqueue(Relationship);
            };

            Action<string, string> AddRenderedCompositeView = (Id, TechName) =>
            {
                Idea ActualIdea;
                if ((!String.IsNullOrWhiteSpace(Id) && ActualIdeasById != null &&
                     ActualIdeasById.TryGetValue(Id, out ActualIdea)) ||
                    (!String.IsNullOrWhiteSpace(TechName) && ActualIdeasByTechName != null &&
                     ActualIdeasByTechName.TryGetValue(TechName, out ActualIdea)))
                {
                    var ActualId = IdOf(ActualIdea);
                    var SelectionKey = !String.IsNullOrWhiteSpace(ActualId)
                                     ? ActualId
                                     : "tech:" + TechName.ToStringAlways(ActualIdea.TechName.ToStringAlways("<unnamed>"));
                    var ActiveViewId = IdOf(ActualIdea.CompositeActiveView);
                    CompositeActiveViews[SelectionKey] = ActiveViewId.ToStringAlways("<none>");
                    AddView(ActiveViewId);
                    return;
                }

                // Native saves always have a matching actual Idea.  Retain a conservative DTO
                // fallback for compatibility callers that supply a detached/export-only graph.
                IEnumerable<string> CandidateViewIds = Enumerable.Empty<string>();
                CompositionJsonIdea Idea;
                CompositionJsonRelationship Relationship;
                if ((!String.IsNullOrWhiteSpace(Id) && Index.IdeasById.TryGetValue(Id, out Idea)) ||
                    (!String.IsNullOrWhiteSpace(TechName) && Index.IdeasByTechName.TryGetValue(TechName, out Idea)))
                    CandidateViewIds = Idea.CompositeViewIds ?? Enumerable.Empty<string>();
                else if ((!String.IsNullOrWhiteSpace(Id) && Index.RelationshipsById.TryGetValue(Id, out Relationship)) ||
                         (!String.IsNullOrWhiteSpace(TechName) && Index.RelationshipsByTechName.TryGetValue(TechName, out Relationship)))
                    CandidateViewIds = Relationship.CompositeViewIds ?? Enumerable.Empty<string>();

                var FallbackKey = !String.IsNullOrWhiteSpace(Id) ? Id : "tech:" + TechName.ToStringAlways("<unknown>");
                CompositeActiveViews[FallbackKey] = "<unresolved>";
                foreach (var ViewId in CandidateViewIds)
                    AddView(ViewId);
            };

            SelectedViews.Add(RootView);
            PendingViews.Enqueue(RootView);

            while (PendingViews.Count > 0 || PendingIdeas.Count > 0 || PendingRelationships.Count > 0)
            {
                while (PendingViews.Count > 0)
                {
                    var View = PendingViews.Dequeue();
                    AddSemantic(View.OwnerIdeaId, View.OwnerIdeaTechName);
                    foreach (var Visual in View.Visuals ?? Enumerable.Empty<CompositionJsonVisual>())
                    {
                        AddSemantic(Visual.IdeaId, Visual.IdeaTechName);
                        if (Visual.AreDetailsShown == true && Visual.ShowCompositeContentAsDetails == true)
                            AddRenderedCompositeView(Visual.IdeaId, Visual.IdeaTechName);
                        foreach (var Connector in Visual.Connectors ?? Enumerable.Empty<CompositionJsonConnector>())
                        {
                            AddSemantic(Connector.AssociatedIdeaId, Connector.AssociatedIdeaTechName);
                            AddSemantic(Connector.OriginIdeaId, Connector.OriginIdeaTechName);
                            AddSemantic(Connector.TargetIdeaId, Connector.TargetIdeaTechName);
                        }
                    }
                }

                while (PendingIdeas.Count > 0)
                {
                    var Idea = PendingIdeas.Dequeue();
                    foreach (var ChildId in Idea.ChildIdeaIds ?? Enumerable.Empty<string>())
                        AddSemantic(ChildId, null);
                }

                while (PendingRelationships.Count > 0)
                {
                    var Relationship = PendingRelationships.Dequeue();
                    foreach (var ChildId in Relationship.ChildIdeaIds ?? Enumerable.Empty<string>())
                        AddSemantic(ChildId, null);
                    foreach (var IdeaId in Relationship.OriginIdeaIds ?? Enumerable.Empty<string>())
                        AddSemantic(IdeaId, null);
                    foreach (var IdeaId in Relationship.TargetIdeaIds ?? Enumerable.Empty<string>())
                        AddSemantic(IdeaId, null);
                    foreach (var IdeaTechName in Relationship.OriginIdeaTechNames ?? Enumerable.Empty<string>())
                        AddSemantic(null, IdeaTechName);
                    foreach (var IdeaTechName in Relationship.TargetIdeaTechNames ?? Enumerable.Empty<string>())
                        AddSemantic(null, IdeaTechName);
                    foreach (var Link in Relationship.Links ?? Enumerable.Empty<CompositionJsonRelationshipLink>())
                        AddSemantic(Link.IdeaId, Link.IdeaTechName);
                }
            }

            var Signature = new CompositionJsonDocument();
            Signature.ExportedAtUtc = null;
            Signature.TargetContext = null;
            Signature.Requires = null;
            Signature.ImportOptions = null;
            Signature.VisualStrategy = null;
            Signature.Composition = CreatePreviewComposition(Source.Composition);
            Signature.Ideas = Index.Ideas.Where(SelectedIdeas.Contains).ToList();
            Signature.Relationships = Index.Relationships.Where(SelectedRelationships.Contains).ToList();
            Signature.Views = Index.Views.Where(SelectedViews.Contains).ToList();
            Signature.Operations = new List<CompositionJsonOperation>();
            Signature.Groups = new List<CompositionJsonGroup>();
            Signature.Warnings = new List<string>();

            // CompositionJsonView intentionally contains the portable semantic/visual graph, but
            // the WPF View also owns render switches and background overrides.  Include those
            // settings for the root and every recursively embedded composite view so a cached PNG
            // can never survive a render-affecting View change that does not alter the DTO graph.
            var RenderStateSignature = String.Join("\n",
                Index.Views.Where(SelectedViews.Contains)
                     .Select(View => View == null ? null : View.Id)
                     .Where(Id => !String.IsNullOrWhiteSpace(Id))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(Id => Id, StringComparer.Ordinal)
                     .Select(Id => Id + "=" + GetViewRenderState(Id, ActualViewsById, ViewRenderStates)));

            var CompositeActiveViewSignature = String.Join("\n",
                CompositeActiveViews.OrderBy(Pair => Pair.Key, StringComparer.Ordinal)
                                    .Select(Pair => Pair.Key + "=" + Pair.Value));

            return HashText(PreviewRenderProfile + "\n" + DomainHash + "\n" +
                            CompositeActiveViewSignature + "\n" +
                            RenderStateSignature + "\n" +
                            CompositionJsonSerializer.Serialize(Signature));
        }

        private static string GetViewRenderState(string ViewId, IDictionary<string, View> ActualViewsById,
                                                 IDictionary<string, string> Cache)
        {
            string State;
            if (Cache != null && Cache.TryGetValue(ViewId.ToStringAlways(""), out State))
                return State;

            View ActualView;
            State = ActualViewsById != null &&
                    ActualViewsById.TryGetValue(ViewId.ToStringAlways(""), out ActualView)
                  ? CreateViewRenderStateSignature(ActualView)
                  : "<missing-view-render-state>";

            if (Cache != null && !Cache.ContainsKey(ViewId.ToStringAlways("")))
                Cache.Add(ViewId.ToStringAlways(""), State);
            return State;
        }

        private static string CreateViewRenderStateSignature(View Source)
        {
            if (Source == null)
                return HashText("<null-view>");

            var Text = new StringBuilder(512);
            AppendRenderState(Text, "id", IdOf(Source));
            AppendRenderState(Text, "showSmoothEdges", Source.ShowSmoothEdges);
            AppendRenderState(Text, "showContextBackground", Source.ShowContextBackground);
            AppendRenderState(Text, "showContextGrid", Source.ShowContextGrid);
            AppendRenderState(Text, "showIndicators", Source.ShowIndicators);
            AppendRenderState(Text, "showConceptDefinitionLabels", Source.ShowConceptDefinitionLabels);
            AppendRenderState(Text, "showRelationshipDefinitionLabels", Source.ShowRelationshipDefinitionLabels);
            AppendRenderState(Text, "showLinkRoleDefNameLabels", Source.ShowLinkRoleDefNameLabels);
            AppendRenderState(Text, "showLinkRoleDescNameLabels", Source.ShowLinkRoleDescNameLabels);
            AppendRenderState(Text, "showLinkRoleVariantLabels", Source.ShowLinkRoleVariantLabels);
            AppendRenderState(Text, "showMarkers", Source.ShowMarkers);
            AppendRenderState(Text, "showMarkersTitles", Source.ShowMarkersTitles);
            AppendRenderState(Text, "gridUsesLines", Source.GridUsesLines);
            AppendRenderState(Text, "gridSize", Source.GridSize.ToString("R", CultureInfo.InvariantCulture));
            // DrawContent renders the effective working values, including Domain fallbacks that
            // are not all represented in the portable Domain DTO.
            AppendRenderState(Text, "backgroundBrush", CreateBrushRenderSignature(Source.BackgroundWorkingBrush));
            AppendRenderState(Text, "backgroundImage", CreateImageRenderSignature(Source.BackgroundWorkingImage));
            AppendRenderState(Text, "imageComplements", CreateImageComplementRenderSignature(Source));
            return HashText(Text.ToString());
        }

        private static string CreateImageComplementRenderSignature(View Source)
        {
            if (Source == null || Source.ViewChildren == null)
                return HashText("<no-image-complements>");

            var Text = new StringBuilder();
            var ImageCount = 0;
            var RenderIndex = 0;
            foreach (var Child in Source.ViewChildren)
            {
                var VisualObject = Child == null ? null : Child.Key as VisualObject;
                if (VisualObject == null || VisualObject is VisualInert)
                    continue;

                var Complement = VisualObject as VisualComplement;
                if (Complement == null || Complement.Kind == null || !Complement.IsComplementImage)
                {
                    RenderIndex++;
                    continue;
                }

                Text.Append("image[").Append(ImageCount++).Append("]\n");
                // Match View.GenerateRenderedContent ordering while excluding transient/non-model
                // ViewChildren such as selection UI state.
                AppendRenderState(Text, "renderIndex", RenderIndex);
                AppendRenderState(Text, "id", IdOf(Complement));
                AppendRenderState(Text, "kind", Complement.Kind.TechName);
                AppendRenderState(Text, "left", Complement.BaseLeft.ToString("R", CultureInfo.InvariantCulture));
                AppendRenderState(Text, "top", Complement.BaseTop.ToString("R", CultureInfo.InvariantCulture));
                AppendRenderState(Text, "width", Complement.BaseWidth.ToString("R", CultureInfo.InvariantCulture));
                AppendRenderState(Text, "height", Complement.BaseHeight.ToString("R", CultureInfo.InvariantCulture));

                var Target = Complement.Target;
                AppendRenderState(Text, "targetScope", Target == null ? "<none>" : (Target.IsGlobal ? "view" : "symbol"));
                AppendRenderState(Text, "targetId", Target == null
                                                    ? null
                                                    : Target.IsGlobal
                                                      ? IdOf(Target.OwnerGlobal)
                                                      : IdOf(Target.OwnerLocal));
                AppendRenderState(Text, "image", CreateImageRenderSignature(
                    Complement.GetPropertyField<ImageSource>(VisualComplement.PROP_FIELD_IMAGE)));
                RenderIndex++;
            }

            AppendRenderState(Text, "count", ImageCount);
            return HashText(Text.ToString());
        }

        private static void AppendRenderState(StringBuilder Target, string Name, object Value)
        {
            Target.Append(Name).Append('=').Append(Value == null ? "<null>" : Value.ToString()).Append('\n');
        }

        private static string CreateBrushRenderSignature(Brush Value)
        {
            if (Value == null)
                return "<null>";

            string Text = null;
            try
            {
                var Converter = new BrushConverter();
                if (Converter.CanConvertTo(typeof(string)))
                    Text = Converter.ConvertTo(null, CultureInfo.InvariantCulture, Value, typeof(string)) as string;
            }
            catch
            {
            }

            string Xaml = null;
            try
            {
                Xaml = XamlWriter.Save(Value);
            }
            catch
            {
            }

            return Value.GetType().FullName + "|opacity=" + Value.Opacity.ToString("R", CultureInfo.InvariantCulture) +
                   "|text=" + Text.ToStringAlways("<none>") +
                   "|xaml=" + (String.IsNullOrWhiteSpace(Xaml) ? "<none>" : HashText(Xaml));
        }

        private static string CreateImageRenderSignature(ImageSource Value)
        {
            if (Value == null)
                return "<null>";

            try
            {
                var Bytes = Value.ToBytes(true);
                if (Bytes != null && Bytes.Length > 0)
                    return Value.GetType().FullName + "|bytes=" + HashBytes(Bytes);
            }
            catch
            {
            }

            try
            {
                var Xaml = XamlWriter.Save(Value);
                if (!String.IsNullOrWhiteSpace(Xaml))
                    return Value.GetType().FullName + "|xaml=" + HashText(Xaml);
            }
            catch
            {
            }

            // All normal ThinkComposer images take the byte path above.  This deterministic
            // metadata fallback remains conservative for unusual custom ImageSource instances.
            return Value.GetType().FullName + "|value=" + Value.ToStringAlways("<unavailable>");
        }

        private static CompositionJsonComposition CreatePreviewComposition(CompositionJsonComposition Source)
        {
            if (Source == null)
                return null;

            return new CompositionJsonComposition
            {
                Id = Source.Id,
                Name = Source.Name,
                TechName = Source.TechName,
                Summary = Source.Summary,
                Description = Source.Description,
                TechSpec = Source.TechSpec,
                ViewsPrefix = Source.ViewsPrefix,
                // The normalized Domain hash already captures the complete render-relevant
                // Domain graph.  Its compatibility signature repeats that graph plus volatile
                // version timestamps, so retaining it here would invalidate every preview on a
                // timestamp-only edit.
                Domain = CreatePreviewDomain(Source.Domain)
            };
        }

        private static CompositionJsonDomain CreatePreviewDomain(CompositionJsonDomain Source)
        {
            if (Source == null)
                return null;

            return new CompositionJsonDomain
            {
                Id = Source.Id,
                Name = Source.Name,
                TechName = Source.TechName,
                Summary = Source.Summary,
                Description = Source.Description,
                TechSpec = Source.TechSpec,
                CompatibilitySignature = null,
                Definitions = Source.Definitions
            };
        }

        private static Dictionary<string, TItem> FirstByKey<TItem>(IEnumerable<TItem> Items, Func<TItem, string> KeySelector)
            where TItem : class
        {
            var Result = new Dictionary<string, TItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var Item in Items ?? Enumerable.Empty<TItem>())
            {
                var Key = Item == null ? null : KeySelector(Item);
                if (!String.IsNullOrWhiteSpace(Key) && !Result.ContainsKey(Key))
                    Result.Add(Key, Item);
            }
            return Result;
        }

        private static string HashText(string Text)
        {
            return HashBytes(Utf8NoBom.GetBytes(Text ?? ""));
        }

        private static string GetManifestString(IDictionary<string, object> Source, string Key)
        {
            object Value;
            return Source != null && Source.TryGetValue(Key, out Value) && Value != null
                 ? Value.ToString()
                 : null;
        }

        private static int? GetManifestInt(IDictionary<string, object> Source, string Key)
        {
            object Value;
            if (Source == null || !Source.TryGetValue(Key, out Value) || Value == null)
                return null;

            try
            {
                return Convert.ToInt32(Value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static bool? GetManifestBool(IDictionary<string, object> Source, string Key)
        {
            object Value;
            if (Source == null || !Source.TryGetValue(Key, out Value) || Value == null)
                return null;
            if (Value is bool)
                return (bool)Value;

            bool Parsed;
            return Boolean.TryParse(Value.ToString(), out Parsed) ? (bool?)Parsed : null;
        }

        private static bool IsSha256(string Value)
        {
            if (String.IsNullOrWhiteSpace(Value) || Value.Length != 64)
                return false;

            return Value.All(Character => (Character >= '0' && Character <= '9') ||
                                          (Character >= 'a' && Character <= 'f') ||
                                          (Character >= 'A' && Character <= 'F'));
        }

        private static bool IsSafePreviewPartUri(string Value)
        {
            return !String.IsNullOrWhiteSpace(Value) &&
                   Value.StartsWith("/Previews/views/", StringComparison.Ordinal) &&
                   Value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                   Value.IndexOf("..", StringComparison.Ordinal) < 0 &&
                   Value.IndexOf('\\') < 0 &&
                   Value.IndexOf('?') < 0 &&
                   Value.IndexOf('#') < 0;
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
            AddJsonPart(Manifest, Kind, PartUri, Bytes, HashBytes(Bytes));
        }

        private static void AddJsonPart(ContainerSnapshotManifest Manifest, string Kind, Uri PartUri, byte[] Bytes, string Sha256)
        {
            var Part = new ContainerSnapshotJsonPart();
            Part.Kind = Kind;
            Part.PartUri = PartUri.ToString();
            Part.Sha256 = Sha256;
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
            Add(Obj, "inputSha256", Preview.InputSha256);
            Add(Obj, "renderProfile", Preview.RenderProfile);
            AddIf(Obj, "disposition", Preview.Disposition);
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

        private sealed class PreviewHashIndex
        {
            internal readonly List<CompositionJsonIdea> Ideas;
            internal readonly List<CompositionJsonRelationship> Relationships;
            internal readonly List<CompositionJsonView> Views;
            internal readonly Dictionary<string, CompositionJsonIdea> IdeasById;
            internal readonly Dictionary<string, CompositionJsonIdea> IdeasByTechName;
            internal readonly Dictionary<string, CompositionJsonRelationship> RelationshipsById;
            internal readonly Dictionary<string, CompositionJsonRelationship> RelationshipsByTechName;
            internal readonly Dictionary<string, CompositionJsonView> ViewsById;

            internal PreviewHashIndex(CompositionJsonDocument Source)
            {
                this.Ideas = Source == null || Source.Ideas == null
                           ? new List<CompositionJsonIdea>() : Source.Ideas;
                this.Relationships = Source == null || Source.Relationships == null
                                   ? new List<CompositionJsonRelationship>() : Source.Relationships;
                this.Views = Source == null || Source.Views == null
                           ? new List<CompositionJsonView>() : Source.Views;
                this.IdeasById = FirstByKey(this.Ideas, Item => Item.Id);
                this.IdeasByTechName = FirstByKey(this.Ideas, Item => Item.TechName);
                this.RelationshipsById = FirstByKey(this.Relationships, Item => Item.Id);
                this.RelationshipsByTechName = FirstByKey(this.Relationships, Item => Item.TechName);
                this.ViewsById = FirstByKey(this.Views, Item => Item.Id);
            }
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
            public string InputSha256;
            public string RenderProfile;
            public string Disposition;
            public string Warning;
            public string Sha256;
            public int? Bytes;
        }

        internal sealed class PreviousPreviewCache
        {
            internal readonly Dictionary<string, CachedPreview> PreviewsByViewId =
                new Dictionary<string, CachedPreview>(StringComparer.OrdinalIgnoreCase);

            internal string LoadWarning;

            internal bool TryGetReusable(string ViewId, string InputSha256, string RenderProfile,
                                         int Width, int Height, string PartUri, out CachedPreview Preview)
            {
                Preview = null;
                CachedPreview Candidate;
                if (String.IsNullOrWhiteSpace(ViewId) ||
                    !this.PreviewsByViewId.TryGetValue(ViewId, out Candidate) ||
                    !String.Equals(Candidate.InputSha256, InputSha256, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(Candidate.RenderProfile, RenderProfile, StringComparison.Ordinal) ||
                    Candidate.Width != Width || Candidate.Height != Height)
                    return false;

                if (Candidate.IsEmpty)
                {
                    Preview = Candidate;
                    return true;
                }

                if (!String.Equals(Candidate.PartUri, PartUri, StringComparison.Ordinal) ||
                    Candidate.Bytes == null || Candidate.Bytes.Length == 0 ||
                    !String.Equals(Candidate.PngSha256, HashBytes(Candidate.Bytes), StringComparison.OrdinalIgnoreCase))
                    return false;

                Preview = Candidate;
                return true;
            }
        }

        internal sealed class CachedPreview
        {
            internal string ViewId;
            internal string InputSha256;
            internal string RenderProfile;
            internal int Width;
            internal int Height;
            internal bool IsEmpty;
            internal string PartUri;
            internal string PngSha256;
            internal byte[] Bytes;
        }
    }
}
