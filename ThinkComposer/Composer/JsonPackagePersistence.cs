// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// JSON-authoritative native package persistence for .tcom and .tdom containers.
// Legacy binary parts may remain in transitional packages, but readers prefer the root JSON
// payloads and use binary only as a recovery path.
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
using System.Windows.Media;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.Composer.ContainerSnapshots;
using Instrumind.ThinkComposer.Composer.GitSync;
using Instrumind.ThinkComposer.Composer.JsonInterchange;
using Instrumind.ThinkComposer.Definitor;
using Instrumind.ThinkComposer.Definitor.DomainJsonInterchange;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.Model;

namespace Instrumind.ThinkComposer.Composer
{
    public static class JsonPackagePersistence
    {
        public static readonly Uri ManifestPartUri = new Uri("/manifest.json", UriKind.Relative);
        public static readonly Uri CompositionJsonPartUri = new Uri("/Composition.json", UriKind.Relative);
        public static readonly Uri DomainJsonPartUri = new Uri("/Domain.json", UriKind.Relative);
        public static readonly Uri TemplateCompositionJsonPartUri = new Uri("/TemplateComposition.json", UriKind.Relative);
        public static readonly Uri LegacyCompositionBinaryPartUri = new Uri("/Composition.bin", UriKind.Relative);
        public static readonly Uri LegacyDomainBinaryPartUri = new Uri("/Domain.bin", UriKind.Relative);

        private const string ManifestFormat = "ThinkComposer.Package";
        private const int ManifestFormatVersion = 1;
        private const string PersistenceFormat = "json";
        private const int PersistenceFormatVersion = 1;
        private const string JsonContentType = "application/json";
        private const string CompositionKind = "composition";
        private const string DomainKind = "domain";
        private const string GitSyncManifestKey = "gitSync";
        private const string EmbeddedDomainGitSyncManifestKey = "embeddedDomainGitSync";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string StoreComposition(Composition SourceComposition, Uri Location,
                                              bool RegisterAsRecentDoc, bool SilentSave,
                                              Visual Snapshot, bool SafeSaving,
                                              GitPackageLink GitSyncLink = null,
                                              GitPackageLink EmbeddedDomainGitSyncLink = null)
        {
            return DocumentEngine.StoreToLocation<ISphereModel>(
                SourceComposition,
                Composition.__ClassDefinitor.Name,
                SourceComposition.Classification.ContentTypeCode,
                Location,
                CompositionEngine.CompositionDocumentUri,
                RegisterAsRecentDoc,
                SilentSave,
                SourceComposition,
                Snapshot,
                SafeSaving,
                delegate(Package Package)
                {
                    WriteCompositionPersistenceParts(Package, SourceComposition, GitSyncLink, EmbeddedDomainGitSyncLink);
                    ContainerSnapshotService.WriteCompositionSnapshot(Package, SourceComposition, CompositionEngine.CompositionDocumentUri);
                });
        }

        public static string StoreDomain(Domain SourceDomain, Uri Location,
                                         bool RegisterAsRecentDoc, bool SilentSave,
                                         Visual Snapshot, bool SafeSaving,
                                         bool IncludeTemplateComposition,
                                         GitPackageLink GitSyncLink = null)
        {
            SourceDomain.SetTemplateSaving(IncludeTemplateComposition);

            return DocumentEngine.StoreToLocation<Domain>(
                SourceDomain,
                Domain.__ClassDefinitor.Name,
                SourceDomain.Classification.ContentTypeCode,
                Location,
                DomainsManager.DomainDocumentUri,
                RegisterAsRecentDoc,
                SilentSave,
                SourceDomain,
                Snapshot,
                SafeSaving,
                delegate(Package Package)
                {
                    WriteDomainPersistenceParts(Package, SourceDomain, IncludeTemplateComposition, GitSyncLink);
                    ContainerSnapshotService.WriteDomainSnapshot(Package, SourceDomain, DomainsManager.DomainDocumentUri, IncludeTemplateComposition);
                });
        }

        public static CompositionPackagePayload ReadCompositionPackage(string FilePath)
        {
            var Result = new CompositionPackagePayload();
            Result.FilePath = Path.GetFullPath(FilePath);

            using (var Pack = Package.Open(Result.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Result.HasManifest = Pack.PartExists(ManifestPartUri);
                Result.HasLegacyBinaryFallback = Pack.PartExists(LegacyCompositionBinaryPartUri);
                Result.HasAuthoritativeJson = Pack.PartExists(CompositionJsonPartUri);
                Result.HasEmbeddedDomainJson = Pack.PartExists(DomainJsonPartUri);
                Result.HasInterchangeSnapshot = Pack.PartExists(ContainerSnapshotService.ManifestPartUri);

                if (Result.HasManifest)
                    Result.Manifest = ReadTextPart(Pack, ManifestPartUri);

                if (!Result.HasAuthoritativeJson)
                    return Result;

                Result.CompositionJson = ReadTextPart(Pack, CompositionJsonPartUri);
                Result.CompositionDocument = CompositionJsonSerializer.Deserialize(Result.CompositionJson);
                CompositionJsonSerializer.Validate(Result.CompositionDocument);

                if (!Result.HasEmbeddedDomainJson)
                    throw new InvalidOperationException("JSON-authoritative composition package is missing /Domain.json for the embedded Domain.");

                Result.DomainJson = ReadTextPart(Pack, DomainJsonPartUri);
                Result.DomainDocument = DomainJsonSerializer.Deserialize(Result.DomainJson);
                DomainJsonSerializer.Validate(Result.DomainDocument);
            }

            return Result;
        }

        public static DomainPackagePayload ReadDomainPackage(string FilePath)
        {
            var Result = new DomainPackagePayload();
            Result.FilePath = Path.GetFullPath(FilePath);

            using (var Pack = Package.Open(Result.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Result.HasManifest = Pack.PartExists(ManifestPartUri);
                Result.HasLegacyBinaryFallback = Pack.PartExists(LegacyDomainBinaryPartUri);
                Result.HasAuthoritativeJson = Pack.PartExists(DomainJsonPartUri);
                Result.HasTemplateCompositionJson = Pack.PartExists(TemplateCompositionJsonPartUri);
                Result.HasInterchangeSnapshot = Pack.PartExists(ContainerSnapshotService.ManifestPartUri);

                if (Result.HasManifest)
                    Result.Manifest = ReadTextPart(Pack, ManifestPartUri);

                if (!Result.HasAuthoritativeJson)
                    return Result;

                Result.DomainJson = ReadTextPart(Pack, DomainJsonPartUri);
                Result.DomainDocument = DomainJsonSerializer.Deserialize(Result.DomainJson);
                DomainJsonSerializer.Validate(Result.DomainDocument);

                if (Result.HasTemplateCompositionJson)
                {
                    Result.TemplateCompositionJson = ReadTextPart(Pack, TemplateCompositionJsonPartUri);
                    Result.TemplateCompositionDocument = CompositionJsonSerializer.Deserialize(Result.TemplateCompositionJson);
                    CompositionJsonSerializer.Validate(Result.TemplateCompositionDocument);
                }
            }

            return Result;
        }

        public static PackagePersistenceInspection Inspect(string FilePath)
        {
            var Result = new PackagePersistenceInspection();
            Result.FilePath = Path.GetFullPath(FilePath);

            using (var Pack = Package.Open(Result.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Result.HasManifest = Pack.PartExists(ManifestPartUri);
                Result.HasCompositionJson = Pack.PartExists(CompositionJsonPartUri);
                Result.HasDomainJson = Pack.PartExists(DomainJsonPartUri);
                Result.HasTemplateCompositionJson = Pack.PartExists(TemplateCompositionJsonPartUri);
                Result.HasCompositionBinary = Pack.PartExists(LegacyCompositionBinaryPartUri);
                Result.HasDomainBinary = Pack.PartExists(LegacyDomainBinaryPartUri);
                Result.HasInterchangeManifest = Pack.PartExists(ContainerSnapshotService.ManifestPartUri);
                Result.HasInterchangeCompositionJson = Pack.PartExists(ContainerSnapshotService.CompositionJsonPartUri);
                Result.HasInterchangeDomainJson = Pack.PartExists(ContainerSnapshotService.DomainJsonPartUri);

                if (Result.HasManifest)
                {
                    Result.ManifestJson = ReadTextPart(Pack, ManifestPartUri);
                    PopulateInspectionFromManifest(Result, Result.ManifestJson);
                }

                if (String.IsNullOrWhiteSpace(Result.PackageKind))
                    Result.PackageKind = Result.HasCompositionJson ? CompositionKind
                                       : Result.HasDomainJson ? DomainKind
                                       : Result.HasCompositionBinary ? CompositionKind
                                       : Result.HasDomainBinary ? DomainKind
                                       : "unknown";

                Result.JsonAuthoritative = String.Equals(Result.PersistenceFormat, PersistenceFormat, StringComparison.OrdinalIgnoreCase) ||
                                           Result.HasCompositionJson ||
                                           Result.HasDomainJson;
                Result.TransitionalWithBinaryFallback = Result.JsonAuthoritative && (Result.HasCompositionBinary || Result.HasDomainBinary);
                Result.LegacyBinaryOnly = !Result.JsonAuthoritative && (Result.HasCompositionBinary || Result.HasDomainBinary);
            }

            return Result;
        }

        public static GitPackageLink ReadGitSyncLink(string FilePath)
        {
            return ReadGitSyncLink(FilePath, GitSyncManifestKey);
        }

        public static GitPackageLink ReadEmbeddedDomainGitSyncLink(string FilePath)
        {
            return ReadGitSyncLink(FilePath, EmbeddedDomainGitSyncManifestKey);
        }

        private static GitPackageLink ReadGitSyncLink(string FilePath, string ManifestKey)
        {
            if (String.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
                return null;

            using (var Pack = Package.Open(Path.GetFullPath(FilePath), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!Pack.PartExists(ManifestPartUri))
                    return null;

                var ManifestJson = ReadTextPart(Pack, ManifestPartUri);
                var Serializer = new JavaScriptSerializer();
                Serializer.MaxJsonLength = Int32.MaxValue;
                var Root = Serializer.DeserializeObject(ManifestJson) as IDictionary<string, object>;
                object GitSyncGraph;
                if (Root == null || !Root.TryGetValue(ManifestKey, out GitSyncGraph) || GitSyncGraph == null)
                    return null;

                return GitPackageLink.FromGraph(GitSyncGraph);
            }
        }

        public static string ComputeAuthoritativeJsonHash(string FilePath, string PackageKind = null)
        {
            if (String.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
                throw new FileNotFoundException("Package file not found.", FilePath);

            using (var Pack = Package.Open(Path.GetFullPath(FilePath), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var Kind = PackageKind;
                if (String.IsNullOrWhiteSpace(Kind))
                    Kind = Pack.PartExists(CompositionJsonPartUri) ? CompositionKind
                         : Pack.PartExists(DomainJsonPartUri) ? DomainKind
                         : null;

                var Parts = new List<Tuple<Uri, bool>>();
                if (String.Equals(Kind, CompositionKind, StringComparison.OrdinalIgnoreCase))
                {
                    Parts.Add(Tuple.Create(CompositionJsonPartUri, true));
                    Parts.Add(Tuple.Create(DomainJsonPartUri, true));
                }
                else
                    if (String.Equals(Kind, DomainKind, StringComparison.OrdinalIgnoreCase))
                    {
                        Parts.Add(Tuple.Create(DomainJsonPartUri, true));
                        Parts.Add(Tuple.Create(TemplateCompositionJsonPartUri, false));
                    }
                    else
                        throw new InvalidDataException("Cannot determine package kind for authoritative JSON hash.");

                var Builder = new StringBuilder();
                foreach (var Part in Parts)
                {
                    if (!Pack.PartExists(Part.Item1))
                    {
                        if (Part.Item2)
                            throw new InvalidDataException("Package is missing authoritative JSON part: " + Part.Item1);

                        Builder.Append(Part.Item1).Append("=<missing>\n");
                        continue;
                    }

                    Builder.Append(Part.Item1).Append("=").Append(HashPart(Pack, Part.Item1)).Append("\n");
                }

                return HashBytes(Utf8NoBom.GetBytes(Builder.ToString()));
            }
        }

        public static string ComputeDomainJsonHash(string FilePath)
        {
            if (String.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
                throw new FileNotFoundException("Package file not found.", FilePath);

            using (var Pack = Package.Open(Path.GetFullPath(FilePath), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!Pack.PartExists(DomainJsonPartUri))
                    throw new InvalidDataException("Package is missing authoritative JSON part: " + DomainJsonPartUri);

                return HashPart(Pack, DomainJsonPartUri);
            }
        }

        public static void WriteGitSyncLink(string FilePath, GitPackageLink GitSyncLink)
        {
            if (String.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
                throw new FileNotFoundException("Package file not found.", FilePath);

            if (GitSyncLink != null)
                GitSyncLink.Validate();

            using (var Pack = Package.Open(Path.GetFullPath(FilePath), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (!Pack.PartExists(ManifestPartUri))
                    throw new InvalidDataException("Package is missing /manifest.json.");

                var ManifestJson = ReadTextPart(Pack, ManifestPartUri);
                var Serializer = new JavaScriptSerializer();
                Serializer.MaxJsonLength = Int32.MaxValue;
                var Root = Serializer.DeserializeObject(ManifestJson) as IDictionary<string, object>;
                if (Root == null)
                    throw new InvalidDataException("Cannot parse package manifest.");

                var ExistingEmbeddedDomainGitSyncLink = ReadGitSyncLinkFromManifestRoot(Root, EmbeddedDomainGitSyncManifestKey);
                var Graph = RebuildManifestGraph(Root, GitSyncLink, ExistingEmbeddedDomainGitSyncLink);
                WriteTextPart(Pack, ManifestPartUri, SerializeGraph(Graph));
            }
        }

        public static string DescribeInspection(PackagePersistenceInspection Inspection)
        {
            if (Inspection == null)
                return "Package inspection failed: no result.";

            var Builder = new StringBuilder();
            Builder.AppendLine("Package: " + Inspection.FilePath);
            Builder.AppendLine("  kind: " + Inspection.PackageKind.ToStringAlways("unknown"));
            Builder.AppendLine("  persistenceFormat: " + Inspection.PersistenceFormat.ToStringAlways(Inspection.JsonAuthoritative ? "json" : "legacy-binary"));
            Builder.AppendLine("  manifest: " + (Inspection.HasManifest ? ManifestPartUri.ToString() : "<missing>"));
            if (!String.IsNullOrWhiteSpace(Inspection.ManifestReadWarning))
                Builder.AppendLine("  manifestWarning: " + Inspection.ManifestReadWarning);
            Builder.AppendLine("  jsonAuthoritative: " + Inspection.JsonAuthoritative.ToString().ToLowerInvariant());
            Builder.AppendLine("  transitionalWithBinaryFallback: " + Inspection.TransitionalWithBinaryFallback.ToString().ToLowerInvariant());
            Builder.AppendLine("  legacyBinaryOnly: " + Inspection.LegacyBinaryOnly.ToString().ToLowerInvariant());
            Builder.AppendLine("  root parts:");
            Builder.AppendLine("    /Composition.json: " + Inspection.HasCompositionJson.ToString().ToLowerInvariant());
            Builder.AppendLine("    /Domain.json: " + Inspection.HasDomainJson.ToString().ToLowerInvariant());
            Builder.AppendLine("    /TemplateComposition.json: " + Inspection.HasTemplateCompositionJson.ToString().ToLowerInvariant());
            Builder.AppendLine("    /Composition.bin: " + Inspection.HasCompositionBinary.ToString().ToLowerInvariant());
            Builder.AppendLine("    /Domain.bin: " + Inspection.HasDomainBinary.ToString().ToLowerInvariant());
            Builder.AppendLine("  interchange sidecars:");
            Builder.AppendLine("    /Interchange/manifest.json: " + Inspection.HasInterchangeManifest.ToString().ToLowerInvariant());
            Builder.AppendLine("    /Interchange/Composition.json: " + Inspection.HasInterchangeCompositionJson.ToString().ToLowerInvariant());
            Builder.AppendLine("    /Interchange/Domain.json: " + Inspection.HasInterchangeDomainJson.ToString().ToLowerInvariant());
            Builder.AppendLine("  gitSync: " + Inspection.GitSyncPresent.ToString().ToLowerInvariant());
            if (Inspection.GitSyncPresent)
            {
                Builder.AppendLine("    remote: " + Inspection.GitSyncRemoteDisplayUrl.ToStringAlways("<missing>"));
                Builder.AppendLine("    branch: " + Inspection.GitSyncBranch.ToStringAlways("<missing>"));
                Builder.AppendLine("    baselines:");
                foreach (var Baseline in Inspection.GitSyncBaselines)
                    Builder.AppendLine("      " + Baseline);
            }
            Builder.AppendLine("  embeddedDomainGitSync: " + Inspection.EmbeddedDomainGitSyncPresent.ToString().ToLowerInvariant());
            if (Inspection.EmbeddedDomainGitSyncPresent)
            {
                Builder.AppendLine("    remote: " + Inspection.EmbeddedDomainGitSyncRemoteDisplayUrl.ToStringAlways("<missing>"));
                Builder.AppendLine("    branch: " + Inspection.EmbeddedDomainGitSyncBranch.ToStringAlways("<missing>"));
                Builder.AppendLine("    baselines:");
                foreach (var Baseline in Inspection.EmbeddedDomainGitSyncBaselines)
                    Builder.AppendLine("      " + Baseline);
            }
            return Builder.ToString().TrimEnd();
        }

        private static void WriteCompositionPersistenceParts(Package Package, Composition Composition,
                                                             GitPackageLink GitSyncLink,
                                                             GitPackageLink EmbeddedDomainGitSyncLink)
        {
            var Parts = new List<PersistenceJsonPart>();
            var Warnings = new List<string>();

            var CompositionDocument = CompositionJsonExporter.Export(Composition);
            AddJsonWarnings(Warnings, "composition", CompositionDocument.Warnings);
            var CompositionJson = CompositionJsonSerializer.Serialize(CompositionDocument);
            WriteTextPart(Package, CompositionJsonPartUri, CompositionJson);
            Parts.Add(CreateJsonPart("composition", CompositionJsonPartUri, CompositionJson, CompositionJsonDocument.CurrentFormat));

            if (Composition.CompositeContentDomain != null)
            {
                var DomainDocument = DomainJsonExporter.Export(Composition.CompositeContentDomain);
                AddJsonWarnings(Warnings, "embeddedDomain", DomainDocument.Warnings);
                var DomainJson = DomainJsonSerializer.Serialize(DomainDocument);
                WriteTextPart(Package, DomainJsonPartUri, DomainJson);
                Parts.Add(CreateJsonPart("embeddedDomain", DomainJsonPartUri, DomainJson, DomainJsonDocument.CurrentFormat));
            }
            else
                Warnings.Add("Composition has no embedded Domain; /Domain.json was not written.");

            var Manifest = CreateManifest(CompositionKind, Composition, LegacyCompositionBinaryPartUri, Package, Parts, Warnings, GitSyncLink, EmbeddedDomainGitSyncLink);
            WriteTextPart(Package, ManifestPartUri, SerializeManifest(Manifest));
            Console.WriteLine("JSON persistence package wrote /Composition.json as authoritative composition payload.");
        }

        private static void WriteDomainPersistenceParts(Package Package, Domain Domain, bool IncludeTemplateComposition, GitPackageLink GitSyncLink)
        {
            var Parts = new List<PersistenceJsonPart>();
            var Warnings = new List<string>();

            var DomainDocument = DomainJsonExporter.Export(Domain);
            AddJsonWarnings(Warnings, "domain", DomainDocument.Warnings);
            var DomainJson = DomainJsonSerializer.Serialize(DomainDocument);
            WriteTextPart(Package, DomainJsonPartUri, DomainJson);
            Parts.Add(CreateJsonPart("domain", DomainJsonPartUri, DomainJson, DomainJsonDocument.CurrentFormat));

            if (IncludeTemplateComposition && Domain.OwnerComposition != null)
            {
                var CompositionDocument = CompositionJsonExporter.Export(Domain.OwnerComposition);
                AddJsonWarnings(Warnings, "templateComposition", CompositionDocument.Warnings);
                var CompositionJson = CompositionJsonSerializer.Serialize(CompositionDocument);
                WriteTextPart(Package, TemplateCompositionJsonPartUri, CompositionJson);
                Parts.Add(CreateJsonPart("templateComposition", TemplateCompositionJsonPartUri, CompositionJson, CompositionJsonDocument.CurrentFormat));
            }
            else
            {
                DeletePartIfExists(Package, TemplateCompositionJsonPartUri);
                if (IncludeTemplateComposition)
                    Warnings.Add("Domain template composition was requested but no owner composition was available.");
            }

            var Manifest = CreateManifest(DomainKind, Domain, LegacyDomainBinaryPartUri, Package, Parts, Warnings, GitSyncLink, null);
            WriteTextPart(Package, ManifestPartUri, SerializeManifest(Manifest));
            Console.WriteLine("JSON persistence package wrote /Domain.json as authoritative domain payload.");
        }

        private static void AddJsonWarnings(List<string> Warnings, string PartKind, IEnumerable<string> SourceWarnings)
        {
            if (Warnings == null || SourceWarnings == null)
                return;

            foreach (var Warning in SourceWarnings.Where(Item => !String.IsNullOrWhiteSpace(Item)).OrderBy(Item => Item))
                Warnings.Add(PartKind + ": " + Warning);
        }

        private static PersistenceManifest CreateManifest(string PackageKind,
                                                          IFormalizedRecognizableElement Source,
                                                          Uri LegacyBinaryPartUri,
                                                          Package Package,
                                                          List<PersistenceJsonPart> Parts,
                                                          List<string> Warnings,
                                                          GitPackageLink GitSyncLink,
                                                          GitPackageLink EmbeddedDomainGitSyncLink)
        {
            var Manifest = new PersistenceManifest();
            Manifest.Format = ManifestFormat;
            Manifest.FormatVersion = ManifestFormatVersion;
            Manifest.Application = "ThinkComposer";
            Manifest.ApplicationVersion = AppExec.ApplicationVersion;
            Manifest.SavedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Manifest.PackageKind = PackageKind;
            Manifest.PersistenceFormat = PersistenceFormat;
            Manifest.PersistenceFormatVersion = PersistenceFormatVersion;
            Manifest.AuthoritativeParts = Parts ?? new List<PersistenceJsonPart>();
            Manifest.LegacyBinaryFallback = CreateLegacyFallback(Package, LegacyBinaryPartUri);
            Manifest.Source = CreateSource(Source);
            Manifest.GitSync = GitSyncLink;
            Manifest.EmbeddedDomainGitSync = EmbeddedDomainGitSyncLink;
            Manifest.Sidecars = new PersistenceSidecars();
            Manifest.Sidecars.InterchangeManifestUri = ContainerSnapshotService.ManifestPartUri.ToString();
            Manifest.Warnings = Warnings ?? new List<string>();
            return Manifest;
        }

        private static PersistenceLegacyBinaryFallback CreateLegacyFallback(Package Package, Uri PartUri)
        {
            var Result = new PersistenceLegacyBinaryFallback();
            Result.PartUri = PartUri == null ? null : PartUri.ToString();
            Result.Present = PartUri != null && Package.PartExists(PartUri);
            if (Result.Present)
                Result.Sha256 = HashPart(Package, PartUri);
            return Result;
        }

        private static PersistenceSource CreateSource(IFormalizedRecognizableElement Source)
        {
            if (Source == null)
                return null;

            var Result = new PersistenceSource();
            Result.Id = Source.GlobalId.ToString("D");
            Result.Name = Source.Name;
            Result.TechName = Source.TechName;
            Result.Summary = Source.Summary;
            if (Source.Version != null)
            {
                Result.VersionNumber = Source.Version.VersionNumber;
                Result.VersionSequence = Source.Version.VersionSequence;
                Result.LastModification = Source.Version.LastModification.ToString("o", CultureInfo.InvariantCulture);
            }
            return Result;
        }

        private static PersistenceJsonPart CreateJsonPart(string Kind, Uri PartUri, string Json, string Format)
        {
            var Bytes = Utf8NoBom.GetBytes(Json ?? "");
            var Part = new PersistenceJsonPart();
            Part.Kind = Kind;
            Part.PartUri = PartUri.ToString();
            Part.Format = Format;
            Part.Sha256 = HashBytes(Bytes);
            Part.Bytes = Bytes.Length;
            return Part;
        }

        private static void PopulateInspectionFromManifest(PackagePersistenceInspection Inspection, string ManifestJson)
        {
            try
            {
                var Serializer = new JavaScriptSerializer();
                Serializer.MaxJsonLength = Int32.MaxValue;
                var Root = Serializer.DeserializeObject(ManifestJson) as IDictionary<string, object>;
                if (Root == null)
                    return;

                Inspection.ManifestFormat = GetString(Root, "format");
                Inspection.PackageKind = GetString(Root, "packageKind");
                Inspection.PersistenceFormat = GetString(Root, "persistenceFormat");
                Inspection.PersistenceFormatVersion = GetInt(Root, "persistenceFormatVersion");
                Inspection.ApplicationVersion = GetString(Root, "applicationVersion");
                Inspection.SavedAtUtc = GetString(Root, "savedAtUtc");

                object GitSyncGraph;
                if (Root.TryGetValue(GitSyncManifestKey, out GitSyncGraph) && GitSyncGraph != null)
                    PopulateGitSyncInspection(Link: GitPackageLink.FromGraph(GitSyncGraph),
                                              PresentSetter: Value => Inspection.GitSyncPresent = Value,
                                              RemoteUrlSetter: Value => Inspection.GitSyncRemoteUrl = Value,
                                              RemoteDisplayUrlSetter: Value => Inspection.GitSyncRemoteDisplayUrl = Value,
                                              BranchSetter: Value => Inspection.GitSyncBranch = Value,
                                              BaselinesSetter: Value => Inspection.GitSyncBaselines = Value);

                object EmbeddedDomainGitSyncGraph;
                if (Root.TryGetValue(EmbeddedDomainGitSyncManifestKey, out EmbeddedDomainGitSyncGraph) && EmbeddedDomainGitSyncGraph != null)
                    PopulateGitSyncInspection(Link: GitPackageLink.FromGraph(EmbeddedDomainGitSyncGraph),
                                              PresentSetter: Value => Inspection.EmbeddedDomainGitSyncPresent = Value,
                                              RemoteUrlSetter: Value => Inspection.EmbeddedDomainGitSyncRemoteUrl = Value,
                                              RemoteDisplayUrlSetter: Value => Inspection.EmbeddedDomainGitSyncRemoteDisplayUrl = Value,
                                              BranchSetter: Value => Inspection.EmbeddedDomainGitSyncBranch = Value,
                                              BaselinesSetter: Value => Inspection.EmbeddedDomainGitSyncBaselines = Value);
            }
            catch (Exception Problem)
            {
                Inspection.ManifestReadWarning = Problem.Message;
            }
        }

        private static void PopulateGitSyncInspection(GitPackageLink Link,
                                                      Action<bool> PresentSetter,
                                                      Action<string> RemoteUrlSetter,
                                                      Action<string> RemoteDisplayUrlSetter,
                                                      Action<string> BranchSetter,
                                                      Action<List<string>> BaselinesSetter)
        {
            PresentSetter(true);
            var RemoteUrl = Link.Remote == null ? null : Link.Remote.Url;
            RemoteUrlSetter(RemoteUrl);
            RemoteDisplayUrlSetter(GitPackageLink.RedactRemoteUrl(RemoteUrl));
            BranchSetter(Link.Remote == null ? null : Link.Remote.Branch);
            BaselinesSetter(Link.Baselines
                                .Select(Baseline => Baseline.Kind + " " + Baseline.Role + ": " + Baseline.Path)
                                .ToList());
        }

        private static string GetString(IDictionary<string, object> Source, string Key)
        {
            object Value;
            return Source != null && Source.TryGetValue(Key, out Value) && Value != null ? Value.ToString() : null;
        }

        private static int? GetInt(IDictionary<string, object> Source, string Key)
        {
            object Value;
            if (Source == null || !Source.TryGetValue(Key, out Value) || Value == null)
                return null;

            if (Value is int)
                return (int)Value;

            int Parsed;
            if (Int32.TryParse(Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out Parsed))
                return Parsed;

            return null;
        }

        private static GitPackageLink ReadGitSyncLinkFromManifestRoot(IDictionary<string, object> Root, string ManifestKey)
        {
            object Graph;
            if (Root == null || !Root.TryGetValue(ManifestKey, out Graph) || Graph == null)
                return null;

            return GitPackageLink.FromGraph(Graph);
        }

        private static string ReadTextPart(Package Package, Uri PartUri)
        {
            using (var Stream = Package.GetPart(PartUri).GetStream(FileMode.Open, FileAccess.Read))
            using (var Reader = new StreamReader(Stream, Encoding.UTF8, true))
                return Reader.ReadToEnd();
        }

        private static void WriteTextPart(Package Package, Uri PartUri, string Text)
        {
            WriteBinaryPart(Package, PartUri, JsonContentType, Utf8NoBom.GetBytes(Text ?? ""), CompressionOption.Maximum);
        }

        private static void WriteBinaryPart(Package Package, Uri PartUri, string ContentType, byte[] Bytes, CompressionOption Compression)
        {
            DeletePartIfExists(Package, PartUri);

            var Part = Package.CreatePart(PartUri, ContentType, Compression);
            using (var Stream = Part.GetStream(FileMode.Create, FileAccess.Write))
                Stream.Write(Bytes, 0, Bytes.Length);
        }

        private static void DeletePartIfExists(Package Package, Uri PartUri)
        {
            if (Package.PartExists(PartUri))
                Package.DeletePart(PartUri);
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

        private static string SerializeManifest(PersistenceManifest Manifest)
        {
            return SerializeGraph(ToGraph(Manifest));
        }

        private static string SerializeGraph(OrderedDictionary Graph)
        {
            var Builder = new StringBuilder();
            WriteJsonValue(Builder, Graph, 0);
            Builder.AppendLine();
            return Builder.ToString();
        }

        private static OrderedDictionary ToGraph(PersistenceManifest Manifest)
        {
            var Obj = NewObject();
            Add(Obj, "format", Manifest.Format);
            Add(Obj, "formatVersion", Manifest.FormatVersion);
            Add(Obj, "application", Manifest.Application);
            AddIf(Obj, "applicationVersion", Manifest.ApplicationVersion);
            Add(Obj, "savedAtUtc", Manifest.SavedAtUtc);
            Add(Obj, "packageKind", Manifest.PackageKind);
            Add(Obj, "persistenceFormat", Manifest.PersistenceFormat);
            Add(Obj, "persistenceFormatVersion", Manifest.PersistenceFormatVersion);
            Add(Obj, "authoritativeParts", Manifest.AuthoritativeParts.Select(ToGraph).ToList());
            Add(Obj, "legacyBinaryFallback", ToGraph(Manifest.LegacyBinaryFallback));
            AddIf(Obj, "source", ToGraph(Manifest.Source));
            AddIf(Obj, GitSyncManifestKey, Manifest.GitSync == null ? null : Manifest.GitSync.ToGraph());
            AddIf(Obj, EmbeddedDomainGitSyncManifestKey, Manifest.EmbeddedDomainGitSync == null ? null : Manifest.EmbeddedDomainGitSync.ToGraph());
            Add(Obj, "sidecars", ToGraph(Manifest.Sidecars));
            Add(Obj, "warnings", Manifest.Warnings);
            return Obj;
        }

        private static OrderedDictionary RebuildManifestGraph(IDictionary<string, object> Root,
                                                              GitPackageLink GitSyncLink,
                                                              GitPackageLink EmbeddedDomainGitSyncLink)
        {
            var Obj = NewObject();
            CopyIfPresent(Root, Obj, "format");
            CopyIfPresent(Root, Obj, "formatVersion");
            CopyIfPresent(Root, Obj, "application");
            CopyIfPresent(Root, Obj, "applicationVersion");
            CopyIfPresent(Root, Obj, "savedAtUtc");
            CopyIfPresent(Root, Obj, "packageKind");
            CopyIfPresent(Root, Obj, "persistenceFormat");
            CopyIfPresent(Root, Obj, "persistenceFormatVersion");
            CopyIfPresent(Root, Obj, "authoritativeParts");
            CopyIfPresent(Root, Obj, "legacyBinaryFallback");
            CopyIfPresent(Root, Obj, "source");
            AddIf(Obj, GitSyncManifestKey, GitSyncLink == null ? null : GitSyncLink.ToGraph());
            AddIf(Obj, EmbeddedDomainGitSyncManifestKey, EmbeddedDomainGitSyncLink == null ? null : EmbeddedDomainGitSyncLink.ToGraph());
            CopyIfPresent(Root, Obj, "sidecars");
            CopyIfPresent(Root, Obj, "warnings");

            foreach (var Entry in Root)
                if (!Obj.Contains(Entry.Key) &&
                    !String.Equals(Entry.Key, GitSyncManifestKey, StringComparison.Ordinal) &&
                    !String.Equals(Entry.Key, EmbeddedDomainGitSyncManifestKey, StringComparison.Ordinal))
                    Obj.Add(Entry.Key, Entry.Value);

            return Obj;
        }

        private static void CopyIfPresent(IDictionary<string, object> Source, OrderedDictionary Target, string Key)
        {
            object Value;
            if (Source != null && Source.TryGetValue(Key, out Value))
                Target.Add(Key, Value);
        }

        private static OrderedDictionary ToGraph(PersistenceJsonPart Part)
        {
            var Obj = NewObject();
            Add(Obj, "kind", Part.Kind);
            Add(Obj, "partUri", Part.PartUri);
            Add(Obj, "format", Part.Format);
            Add(Obj, "sha256", Part.Sha256);
            Add(Obj, "bytes", Part.Bytes);
            return Obj;
        }

        private static OrderedDictionary ToGraph(PersistenceLegacyBinaryFallback Fallback)
        {
            var Obj = NewObject();
            Add(Obj, "present", Fallback != null && Fallback.Present);
            AddIf(Obj, "partUri", Fallback == null ? null : Fallback.PartUri);
            AddIf(Obj, "sha256", Fallback == null ? null : Fallback.Sha256);
            return Obj;
        }

        private static OrderedDictionary ToGraph(PersistenceSource Source)
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

        private static OrderedDictionary ToGraph(PersistenceSidecars Sidecars)
        {
            var Obj = NewObject();
            AddIf(Obj, "interchangeManifestUri", Sidecars == null ? null : Sidecars.InterchangeManifestUri);
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

        public sealed class CompositionPackagePayload
        {
            public string FilePath;
            public bool HasManifest;
            public bool HasAuthoritativeJson;
            public bool HasEmbeddedDomainJson;
            public bool HasLegacyBinaryFallback;
            public bool HasInterchangeSnapshot;
            public string Manifest;
            public string CompositionJson;
            public string DomainJson;
            public CompositionJsonDocument CompositionDocument;
            public DomainJsonDocument DomainDocument;
        }

        public sealed class DomainPackagePayload
        {
            public string FilePath;
            public bool HasManifest;
            public bool HasAuthoritativeJson;
            public bool HasTemplateCompositionJson;
            public bool HasLegacyBinaryFallback;
            public bool HasInterchangeSnapshot;
            public string Manifest;
            public string DomainJson;
            public string TemplateCompositionJson;
            public DomainJsonDocument DomainDocument;
            public CompositionJsonDocument TemplateCompositionDocument;
        }

        public sealed class PackagePersistenceInspection
        {
            public string FilePath;
            public bool HasManifest;
            public bool HasCompositionJson;
            public bool HasDomainJson;
            public bool HasTemplateCompositionJson;
            public bool HasCompositionBinary;
            public bool HasDomainBinary;
            public bool HasInterchangeManifest;
            public bool HasInterchangeCompositionJson;
            public bool HasInterchangeDomainJson;
            public string ManifestJson;
            public string ManifestFormat;
            public string PackageKind;
            public string PersistenceFormat;
            public int? PersistenceFormatVersion;
            public string ApplicationVersion;
            public string SavedAtUtc;
            public string ManifestReadWarning;
            public bool JsonAuthoritative;
            public bool TransitionalWithBinaryFallback;
            public bool LegacyBinaryOnly;
            public bool GitSyncPresent;
            public string GitSyncRemoteUrl;
            public string GitSyncRemoteDisplayUrl;
            public string GitSyncBranch;
            public List<string> GitSyncBaselines = new List<string>();
            public bool EmbeddedDomainGitSyncPresent;
            public string EmbeddedDomainGitSyncRemoteUrl;
            public string EmbeddedDomainGitSyncRemoteDisplayUrl;
            public string EmbeddedDomainGitSyncBranch;
            public List<string> EmbeddedDomainGitSyncBaselines = new List<string>();
        }

        private sealed class PersistenceManifest
        {
            public string Format;
            public int FormatVersion;
            public string Application;
            public string ApplicationVersion;
            public string SavedAtUtc;
            public string PackageKind;
            public string PersistenceFormat;
            public int PersistenceFormatVersion;
            public List<PersistenceJsonPart> AuthoritativeParts = new List<PersistenceJsonPart>();
            public PersistenceLegacyBinaryFallback LegacyBinaryFallback;
            public PersistenceSource Source;
            public GitPackageLink GitSync;
            public GitPackageLink EmbeddedDomainGitSync;
            public PersistenceSidecars Sidecars;
            public List<string> Warnings = new List<string>();
        }

        private sealed class PersistenceJsonPart
        {
            public string Kind;
            public string PartUri;
            public string Format;
            public string Sha256;
            public int Bytes;
        }

        private sealed class PersistenceLegacyBinaryFallback
        {
            public bool Present;
            public string PartUri;
            public string Sha256;
        }

        private sealed class PersistenceSource
        {
            public string Id;
            public string Name;
            public string TechName;
            public string Summary;
            public string VersionNumber;
            public int? VersionSequence;
            public string LastModification;
        }

        private sealed class PersistenceSidecars
        {
            public string InterchangeManifestUri;
        }
    }
}
