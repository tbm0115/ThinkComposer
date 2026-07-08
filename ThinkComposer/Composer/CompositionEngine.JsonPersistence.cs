// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// JSON-first native package load helpers for CompositionEngine.
// -------------------------------------------------------------------------------------------

using System;
using System.IO;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.ThinkComposer.Composer.JsonInterchange;
using Instrumind.ThinkComposer.Definitor;
using Instrumind.ThinkComposer.Definitor.DomainJsonInterchange;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.Model;

namespace Instrumind.ThinkComposer.Composer
{
    public partial class CompositionEngine
    {
        public static bool LastLoadUsedJsonPersistence { get; private set; }
        public static bool LastLoadUsedLegacyBinaryFallback { get; private set; }
        public static string LastLoadPersistenceDiagnostic { get; private set; }

        private static void ResetJsonPersistenceLoadDiagnostics()
        {
            LastLoadUsedJsonPersistence = false;
            LastLoadUsedLegacyBinaryFallback = false;
            LastLoadPersistenceDiagnostic = null;
        }

        private static void MarkJsonPersistenceLoad(bool UsedJson, bool UsedLegacyBinaryFallback, string Diagnostic)
        {
            LastLoadUsedJsonPersistence = UsedJson;
            LastLoadUsedLegacyBinaryFallback = UsedLegacyBinaryFallback;
            LastLoadPersistenceDiagnostic = Diagnostic;
        }

        private static JsonPersistenceLoadResult<Composition> TryLoadCompositionFromJsonPackage(CompositionEngine Engine, Uri SourceLocation)
        {
            var Result = new JsonPersistenceLoadResult<Composition>();

            try
            {
                var Payload = JsonPackagePersistence.ReadCompositionPackage(SourceLocation.LocalPath);
                Result.HasAuthoritativeJson = Payload.HasAuthoritativeJson;
                Result.HasLegacyBinaryFallback = Payload.HasLegacyBinaryFallback;

                if (!Payload.HasAuthoritativeJson)
                    return Result;

                Result.Content = RehydrateCompositionFromJsonDocument(Engine, Payload.CompositionDocument, Payload.DomainDocument);
                Result.Loaded = true;
                Console.WriteLine("JSON persistence loaded composition package: {0}", SourceLocation.LocalPath);
            }
            catch (Exception Problem)
            {
                Result.Error = "Cannot load JSON-authoritative composition package: " + Problem.Message;
                Result.Exception = Problem;
                Console.WriteLine(Result.Error);
                Console.WriteLine(Problem.ToString());
                try
                {
                    var Inspection = JsonPackagePersistence.Inspect(SourceLocation.LocalPath);
                    Result.HasAuthoritativeJson = Inspection.JsonAuthoritative || Inspection.HasCompositionJson;
                    Result.HasLegacyBinaryFallback = Inspection.HasCompositionBinary;
                }
                catch
                {
                }
            }

            return Result;
        }

        private static JsonPersistenceLoadResult<Domain> TryLoadDomainFromJsonPackage(Uri SourceLocation)
        {
            var Result = new JsonPersistenceLoadResult<Domain>();

            try
            {
                var Payload = JsonPackagePersistence.ReadDomainPackage(SourceLocation.LocalPath);
                Result.HasAuthoritativeJson = Payload.HasAuthoritativeJson;
                Result.HasLegacyBinaryFallback = Payload.HasLegacyBinaryFallback;

                if (!Payload.HasAuthoritativeJson)
                    return Result;

                var Engine = CompositionEngine.ActiveCompositionEngine;
                if (Engine == null)
                    throw new InvalidOperationException("JSON-authoritative Domain package load requires an active CompositionEngine.");

                var Domain = RehydrateDomainFromJsonDocument(Engine, Payload.DomainDocument);

                if (Payload.TemplateCompositionDocument != null)
                {
                    var Template = RehydrateCompositionFromJsonDocument(Engine, Payload.TemplateCompositionDocument, Domain);
                    Domain.SetOwnerComposition(Template);
                }

                Result.Content = Domain;
                Result.Loaded = true;
                Console.WriteLine("JSON persistence loaded domain package: {0}", SourceLocation.LocalPath);
            }
            catch (Exception Problem)
            {
                Result.Error = "Cannot load JSON-authoritative domain package: " + Problem.Message;
                Result.Exception = Problem;
                Console.WriteLine(Result.Error);
                Console.WriteLine(Problem.ToString());
                try
                {
                    var Inspection = JsonPackagePersistence.Inspect(SourceLocation.LocalPath);
                    Result.HasAuthoritativeJson = Inspection.JsonAuthoritative || Inspection.HasDomainJson;
                    Result.HasLegacyBinaryFallback = Inspection.HasDomainBinary;
                }
                catch
                {
                }
            }

            return Result;
        }

        private static Composition RehydrateCompositionFromJsonDocument(CompositionEngine Engine,
                                                                        CompositionJsonDocument CompositionDocument,
                                                                        DomainJsonDocument DomainDocument)
        {
            var Domain = RehydrateDomainFromJsonDocument(Engine, DomainDocument);
            return RehydrateCompositionFromJsonDocument(Engine, CompositionDocument, Domain);
        }

        private static Composition RehydrateCompositionFromJsonDocument(CompositionEngine Engine,
                                                                        CompositionJsonDocument CompositionDocument,
                                                                        Domain Domain)
        {
            if (Engine == null)
                throw new UsageAnomaly("Cannot rehydrate a Composition without an active engine.");

            if (CompositionDocument == null)
                throw new UsageAnomaly("Cannot rehydrate a Composition without /Composition.json.");

            if (Domain == null)
                throw new UsageAnomaly("Cannot rehydrate a Composition without /Domain.json.");

            var CompositionInfo = CompositionDocument.Composition;
            var Name = CompositionInfo == null || CompositionInfo.Name.IsAbsent()
                       ? Engine.Manager.DocumentsPrefix + Engine.Manager.GetNewDocumentNumber().ToString()
                       : CompositionInfo.Name;
            var TechName = CompositionInfo == null || CompositionInfo.TechName.IsAbsent()
                           ? Name.TextToIdentifier()
                           : CompositionInfo.TechName;

            var TargetComposition = new Composition(Engine, Domain, Name, TechName);
            Engine.TargetComposition = TargetComposition;

            TargetComposition.CompositionDefinitor.SetOwnerComposition(TargetComposition);
            TargetComposition.Initialize();

            var ImportDocument = CloneCompositionDocument(CompositionDocument);
            ImportDocument.ImportOptions = BuildPersistenceImportOptions();

            var Report = CompositionJsonImporter.RehydrateFullState(Engine, ImportDocument);
            if (Report.CompatibilityBlocked || Report.HasErrors)
                throw new InvalidOperationException("Composition JSON persistence rehydration failed." + Environment.NewLine +
                                                    Report.ToSummaryString(true));

            TargetComposition.CompositionDefinitor.SetOwnerComposition(TargetComposition);
            ModelFixes.ApplyModelFixes(TargetComposition.CompositionDefinitor);
            TargetComposition.Initialize();
            Engine.GlobalId = TargetComposition.GlobalId;

            return TargetComposition;
        }

        private static Domain RehydrateDomainFromJsonDocument(CompositionEngine Engine, DomainJsonDocument DomainDocument)
        {
            if (DomainDocument == null)
                throw new UsageAnomaly("Cannot rehydrate a Domain without /Domain.json.");

            var TargetDomain = Domain.Create(Engine);
            var Preview = DomainJsonImporter.Preview(TargetDomain, DomainDocument);
            if (Preview.Errors.Count > 0)
                throw new InvalidOperationException("Domain JSON persistence preview failed." + Environment.NewLine +
                                                    Preview.PreviewSummary());

            var Report = DomainJsonImporter.ApplyPreservingIds(TargetDomain, DomainDocument, new DomainJsonImportReport());
            if (Report.Errors.Count > 0)
                throw new InvalidOperationException("Domain JSON persistence rehydration failed." + Environment.NewLine +
                                                    Report.ApplySummary());

            TargetDomain.DeclareExtraCollections();

            return TargetDomain;
        }

        private static CompositionJsonImportOptions BuildPersistenceImportOptions()
        {
            return new CompositionJsonImportOptions
            {
                AutoPlaceNewItems = true,
                AutoFitPlacedConcepts = false,
                AutoRoutePlacedLinks = false,
                UseActiveCompositionAsContainer = false,
                TreatMissingFullStateItemsAsCreates = true,
                DetailFallbackMode = "skip",
                DomainCompatibilityPolicy = "ignore",
                CompositionVersionPolicy = "ignore",
                StrictRelationshipCompatibility = false,
                AbortOnRelationshipCompatibilityFailure = false,
                StrictDetailsCompatibility = false,
                AbortOnDetailCompatibilityFailure = false,
                RelationshipVisualPlacementMode = "explicit",
                RecomputeSuspiciousRelationshipVisuals = false,
                HideGenericRelationshipCenters = false,
                LayoutMode = "none",
                PreventSelfRecursiveCompositeViews = true,
                RepairRecursiveVisuals = true
            };
        }

        private static CompositionJsonDocument CloneCompositionDocument(CompositionJsonDocument Source)
        {
            return CompositionJsonSerializer.Deserialize(CompositionJsonSerializer.Serialize(Source));
        }

        private class JsonPersistenceLoadResult<TContent>
        {
            public bool HasAuthoritativeJson;
            public bool HasLegacyBinaryFallback;
            public bool Loaded;
            public TContent Content;
            public string Error;
            public Exception Exception;
        }
    }
}
