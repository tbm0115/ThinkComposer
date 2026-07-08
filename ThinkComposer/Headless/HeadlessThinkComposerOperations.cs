// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Reusable command-line operations for JSON interchange, reports and output generation.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Windows.Threading;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.Composer.ContainerSnapshots;
using Instrumind.ThinkComposer.Composer.Generation;
using Instrumind.ThinkComposer.Composer.JsonInterchange;
using Instrumind.ThinkComposer.Composer.Reporting;
using Instrumind.ThinkComposer.Definitor;
using Instrumind.ThinkComposer.Definitor.DomainJsonInterchange;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;

namespace Instrumind.ThinkComposer.Headless
{
    public sealed class HeadlessOutputOptions
    {
        public string Input { get; set; }
        public string OutputDirectory { get; set; }
        public string LanguageTechName { get; set; }
        public bool GenerateRelationships { get; set; }
        public bool CreateCompositionRootDirectory { get; set; }
        public bool UseTechNamesAsProgramIdentifiers { get; set; }
        public IList<string> ExcludedIdeas { get; private set; }

        public HeadlessOutputOptions()
        {
            this.ExcludedIdeas = new List<string>();
        }
    }

    public static class HeadlessThinkComposerOperations
    {
        public static OperationResult<string> ExportCompositionJson(string Input, string Output)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateInputOutput(Input, Output, Composition.FILE_EXTENSION_COMPOSITION, "json", false);
                if (!Validation.WasSuccessful)
                    return Validation;

                var LoadResult = LoadComposition(Input);
                if (!LoadResult.WasSuccessful)
                    return Fail(LoadResult.Message);

                EnsureParentDirectory(Output);
                var Document = CompositionJsonExporter.Export(LoadResult.Result.TargetComposition);
                CompositionJsonSerializer.Save(Document, Path.GetFullPath(Output));
                return Succeed("Composition JSON exported to: " + Path.GetFullPath(Output), Path.GetFullPath(Output));
            });
        }

        public static OperationResult<string> ImportCompositionJson(string Input, string Json, string Output, bool InPlace, bool PreviewOnly)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateImportPaths(Input, Json, Output, Composition.FILE_EXTENSION_COMPOSITION, Composition.FILE_EXTENSION_COMPOSITION, InPlace);
                if (!Validation.WasSuccessful)
                    return Validation;

                var LoadResult = LoadComposition(Input);
                if (!LoadResult.WasSuccessful)
                    return Fail(LoadResult.Message);

                var Document = CompositionJsonSerializer.Load(Json);
                CompositionJsonSerializer.Validate(Document);
                var Preview = CompositionJsonImporter.Preview(LoadResult.Result.TargetComposition, Document);

                if (PreviewOnly)
                    return Succeed("Composition JSON import preview completed." + Environment.NewLine + Preview.ToSummaryString(true), null);

                var ApplyReport = CompositionJsonImporter.Import(LoadResult.Result, Document, Preview);
                SaveComposition(LoadResult.Result.TargetComposition, Output);

                return Succeed("Composition JSON imported into: " + Path.GetFullPath(Output) + Environment.NewLine +
                               ApplyReport.ToSummaryString(true), Path.GetFullPath(Output));
            });
        }

        public static OperationResult<string> ExportDomainJson(string Input, string Output)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateDomainInput(Input);
                if (!Validation.WasSuccessful)
                    return Validation;

                if (!HasExtension(Output, "json"))
                    return Fail("Output must have .json extension.");

                Domain Domain = null;
                if (HasExtension(Input, Domain.FILE_EXTENSION_DOMAIN))
                {
                    var DomainResult = LoadNativeDomain(Input);
                    if (!DomainResult.WasSuccessful)
                        return Fail(DomainResult.Message);

                    Domain = DomainResult.Result;
                }
                else
                {
                    var LoadResult = LoadComposition(Input);
                    if (!LoadResult.WasSuccessful)
                        return Fail(LoadResult.Message);

                    Domain = LoadResult.Result.TargetComposition.CompositeContentDomain;
                }

                EnsureParentDirectory(Output);
                DomainJsonSerializer.Save(DomainJsonExporter.Export(Domain), Path.GetFullPath(Output));
                return Succeed("Domain JSON exported to: " + Path.GetFullPath(Output), Path.GetFullPath(Output));
            });
        }

        public static OperationResult<string> ImportDomainJson(string Input, string Json, string Output, bool InPlace, bool PreviewOnly)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateDomainInput(Input);
                if (!Validation.WasSuccessful)
                    return Validation;

                if (!File.Exists(Json))
                    return Fail("JSON file not found: " + Json);

                if (!HasExtension(Json, "json"))
                    return Fail("JSON input must have .json extension.");

                if (String.IsNullOrWhiteSpace(Output))
                    return Fail("Import requires --output.");

                if (HasExtension(Input, Domain.FILE_EXTENSION_DOMAIN) && !HasExtension(Output, Domain.FILE_EXTENSION_DOMAIN))
                    return Fail("Native domain import output must have .tdom extension.");

                if (HasExtension(Input, Composition.FILE_EXTENSION_COMPOSITION) && !HasExtension(Output, Composition.FILE_EXTENSION_COMPOSITION))
                    return Fail("Embedded domain import output must have .tcom extension.");

                if (SamePath(Input, Output) && !InPlace)
                    return Fail("Refusing to overwrite input. Use --in-place with --output set to the input path.");

                if (InPlace && !SamePath(Input, Output))
                    return Fail("--in-place requires --output to match --input.");

                var Document = DomainJsonSerializer.Load(Json);
                DomainJsonSerializer.Validate(Document);

                if (HasExtension(Input, Domain.FILE_EXTENSION_DOMAIN))
                {
                    var EditResult = LoadNativeDomainForEdit(Input);
                    if (!EditResult.WasSuccessful)
                        return Fail(EditResult.Message);

                    var Preview = DomainJsonImporter.Preview(EditResult.Result.TargetComposition.CompositeContentDomain, Document);
                    if (PreviewOnly)
                        return Succeed("Domain JSON import preview completed." + Environment.NewLine + Preview.PreviewSummary(), null);

                    var ApplyReport = ApplyDomainJson(EditResult.Result, EditResult.Result.TargetComposition.CompositeContentDomain, Document);
                    SaveDomain(EditResult.Result.TargetComposition.CompositeContentDomain, Output);
                    return Succeed("Domain JSON imported into: " + Path.GetFullPath(Output) + Environment.NewLine +
                                   ApplyReport.ApplySummary(), Path.GetFullPath(Output));
                }
                else
                {
                    var LoadResult = LoadComposition(Input);
                    if (!LoadResult.WasSuccessful)
                        return Fail(LoadResult.Message);

                    var TargetDomain = LoadResult.Result.TargetComposition.CompositeContentDomain;
                    var Preview = DomainJsonImporter.Preview(TargetDomain, Document);
                    if (PreviewOnly)
                        return Succeed("Embedded Domain JSON import preview completed." + Environment.NewLine + Preview.PreviewSummary(), null);

                    var ApplyReport = ApplyDomainJson(LoadResult.Result, TargetDomain, Document);
                    SaveComposition(LoadResult.Result.TargetComposition, Output);
                    return Succeed("Embedded Domain JSON imported into: " + Path.GetFullPath(Output) + Environment.NewLine +
                                   ApplyReport.ApplySummary(), Path.GetFullPath(Output));
                }
            });
        }

        public static OperationResult<string> ValidateCompositionJsonRoundTrip(string Input, string OutputDirectory)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateExistingFile(Input, Composition.FILE_EXTENSION_COMPOSITION, "input");
                if (!Validation.WasSuccessful)
                    return Validation;

                var DirectoryValidation = ValidateOutputDirectory(OutputDirectory);
                if (!DirectoryValidation.WasSuccessful)
                    return DirectoryValidation;

                var TargetDirectory = Path.GetFullPath(OutputDirectory);
                Directory.CreateDirectory(TargetDirectory);

                var LoadResult = LoadComposition(Input);
                if (!LoadResult.WasSuccessful)
                    return Fail(LoadResult.Message);

                var SourceComposition = LoadResult.Result.TargetComposition;
                var SourceDomain = SourceComposition.CompositeContentDomain;
                var SourceCompositionDocument = CompositionJsonExporter.Export(SourceComposition);
                var SourceDomainDocument = DomainJsonExporter.Export(SourceDomain);

                var CompositionOriginalPath = Path.Combine(TargetDirectory, "composition-original.json");
                var DomainOriginalPath = Path.Combine(TargetDirectory, "domain-original.json");
                CompositionJsonSerializer.Save(SourceCompositionDocument, CompositionOriginalPath);
                DomainJsonSerializer.Save(SourceDomainDocument, DomainOriginalPath);

                var RehydratedEngineResult = RehydrateCompositionFromJson(SourceCompositionDocument, SourceDomainDocument);
                if (!RehydratedEngineResult.WasSuccessful)
                    return Fail(RehydratedEngineResult.Message);

                var RehydratedCompositionPath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-roundtrip.tcom");
                SaveComposition(RehydratedEngineResult.Result.TargetComposition, RehydratedCompositionPath);

                var ReexportedCompositionDocument = CompositionJsonExporter.Export(RehydratedEngineResult.Result.TargetComposition);
                var ReexportedDomainDocument = DomainJsonExporter.Export(RehydratedEngineResult.Result.TargetComposition.CompositeContentDomain);
                var CompositionReexportPath = Path.Combine(TargetDirectory, "composition-reexport.json");
                var DomainReexportPath = Path.Combine(TargetDirectory, "domain-reexport.json");
                CompositionJsonSerializer.Save(ReexportedCompositionDocument, CompositionReexportPath);
                DomainJsonSerializer.Save(ReexportedDomainDocument, DomainReexportPath);

                string CompositionMismatch;
                string DomainMismatch;
                var CompositionMatches = CompareCompositionDocuments(SourceCompositionDocument, ReexportedCompositionDocument,
                                                                      Path.Combine(TargetDirectory, "composition-original.normalized.json"),
                                                                      Path.Combine(TargetDirectory, "composition-reexport.normalized.json"),
                                                                      out CompositionMismatch);
                var DomainMatches = CompareDomainDocuments(SourceDomainDocument, ReexportedDomainDocument,
                                                           Path.Combine(TargetDirectory, "domain-original.normalized.json"),
                                                           Path.Combine(TargetDirectory, "domain-reexport.normalized.json"),
                                                           out DomainMismatch);

                if (!CompositionMatches || !DomainMatches)
                    return Fail("Composition JSON round-trip parity failed." + Environment.NewLine +
                                "Artifacts: " + TargetDirectory + Environment.NewLine +
                                CompositionMismatch + Environment.NewLine +
                                DomainMismatch);

                return Succeed("Composition JSON round-trip parity passed." + Environment.NewLine +
                               "Artifacts: " + TargetDirectory, RehydratedCompositionPath);
            });
        }

        public static OperationResult<string> ValidateDomainJsonRoundTrip(string Input, string OutputDirectory)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateDomainInput(Input);
                if (!Validation.WasSuccessful)
                    return Validation;

                var DirectoryValidation = ValidateOutputDirectory(OutputDirectory);
                if (!DirectoryValidation.WasSuccessful)
                    return DirectoryValidation;

                var TargetDirectory = Path.GetFullPath(OutputDirectory);
                Directory.CreateDirectory(TargetDirectory);

                Domain SourceDomain = null;
                if (HasExtension(Input, Domain.FILE_EXTENSION_DOMAIN))
                {
                    var DomainResult = LoadNativeDomain(Input);
                    if (!DomainResult.WasSuccessful)
                        return Fail(DomainResult.Message);

                    SourceDomain = DomainResult.Result;
                }
                else
                {
                    var LoadResult = LoadComposition(Input);
                    if (!LoadResult.WasSuccessful)
                        return Fail(LoadResult.Message);

                    SourceDomain = LoadResult.Result.TargetComposition.CompositeContentDomain;
                }

                var SourceDomainDocument = DomainJsonExporter.Export(SourceDomain);
                var DomainOriginalPath = Path.Combine(TargetDirectory, "domain-original.json");
                DomainJsonSerializer.Save(SourceDomainDocument, DomainOriginalPath);

                var RehydratedEngineResult = RehydrateDomainFromJson(SourceDomainDocument);
                if (!RehydratedEngineResult.WasSuccessful)
                    return Fail(RehydratedEngineResult.Message);

                var RehydratedDomainPath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-roundtrip.tdom");
                SaveDomain(RehydratedEngineResult.Result.TargetComposition.CompositeContentDomain, RehydratedDomainPath);

                var ReexportedDomainDocument = DomainJsonExporter.Export(RehydratedEngineResult.Result.TargetComposition.CompositeContentDomain);
                var DomainReexportPath = Path.Combine(TargetDirectory, "domain-reexport.json");
                DomainJsonSerializer.Save(ReexportedDomainDocument, DomainReexportPath);

                string DomainMismatch;
                var DomainMatches = CompareDomainDocuments(SourceDomainDocument, ReexportedDomainDocument,
                                                           Path.Combine(TargetDirectory, "domain-original.normalized.json"),
                                                           Path.Combine(TargetDirectory, "domain-reexport.normalized.json"),
                                                           out DomainMismatch);

                if (!DomainMatches)
                    return Fail("Domain JSON round-trip parity failed." + Environment.NewLine +
                                "Artifacts: " + TargetDirectory + Environment.NewLine +
                                DomainMismatch);

                return Succeed("Domain JSON round-trip parity passed." + Environment.NewLine +
                               "Artifacts: " + TargetDirectory, RehydratedDomainPath);
            });
        }

        public static OperationResult<string> GenerateReport(string Input, string Output)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateInputOutput(Input, Output, Composition.FILE_EXTENSION_COMPOSITION, null, false);
                if (!Validation.WasSuccessful)
                    return Validation;

                if (!HasExtension(Output, "pdf") && !HasExtension(Output, "xps"))
                    return Fail("Report output must have .pdf or .xps extension.");

                var LoadResult = LoadComposition(Input);
                if (!LoadResult.WasSuccessful)
                    return Fail(LoadResult.Message);

                EnsureParentDirectory(Output);

                var TargetComposition = LoadResult.Result.TargetComposition;
                var Configuration = TargetComposition.CompositeContentDomain.ReportingConfiguration;
                if (Configuration == null)
                    Configuration = new ReportConfiguration();

                var Generator = new ReportStandardGenerator(TargetComposition, Configuration);
                var Worker = new ThreadWorker<int>(Dispatcher.CurrentDispatcher);
                var Result = Generator.Generate(Worker);
                if (!Result.WasSuccessful)
                    return Fail(Result.Message);

                if (String.IsNullOrEmpty(Generator.GeneratedDocumentTempFilePath) || !File.Exists(Generator.GeneratedDocumentTempFilePath))
                    return Fail("Report generator did not produce a temporary XPS document.");

                var Target = Path.GetFullPath(Output);
                if (HasExtension(Output, "xps"))
                    File.Copy(Generator.GeneratedDocumentTempFilePath, Target, true);
                else
                {
                    var Error = Display.ConvertXPStoPDF(Generator.GeneratedDocumentTempFilePath, Target);
                    if (!String.IsNullOrEmpty(Error))
                        return Fail(Error);
                }

                TryDelete(Generator.GeneratedDocumentTempFilePath);
                return Succeed("Report generated at: " + Target, Target);
            });
        }

        public static OperationResult<string> GenerateOutput(HeadlessOutputOptions Options)
        {
            return ExpectedOperation(delegate
            {
                if (Options == null)
                    return Fail("No output options were supplied.");

                if (String.IsNullOrWhiteSpace(Options.OutputDirectory))
                    return Fail("Output generation requires --output-dir.");

                if (String.IsNullOrWhiteSpace(Options.LanguageTechName))
                    return Fail("Output generation requires --language.");

                var Validation = ValidateExistingFile(Options.Input, Composition.FILE_EXTENSION_COMPOSITION, "input");
                if (!Validation.WasSuccessful)
                    return Validation;

                var LoadResult = LoadComposition(Options.Input);
                if (!LoadResult.WasSuccessful)
                    return Fail(LoadResult.Message);

                var TargetComposition = LoadResult.Result.TargetComposition;
                var TargetDomain = TargetComposition.CompositeContentDomain;
                var Language = ResolveLanguage(TargetDomain, Options.LanguageTechName);
                if (Language == null)
                    return Fail("Unknown language tech name '" + Options.LanguageTechName + "'. Known languages: " + KnownLanguages(TargetDomain));

                var TargetDirectory = Path.GetFullPath(Options.OutputDirectory);
                Directory.CreateDirectory(TargetDirectory);

                var Configuration = new FileGenerationConfiguration();
                Configuration.TargetDirectory = TargetDirectory;
                Configuration.Language = Language;
                Configuration.CreateCompositionRootDirectory = Options.CreateCompositionRootDirectory;
                Configuration.UseIdeaTechNameForFileNaming = true;
                Configuration.UseTechNamesAsProgramIdentifiers = Options.UseTechNamesAsProgramIdentifiers;
                Configuration.GenerateFilesForRelationships = Options.GenerateRelationships;
                Configuration.CompositeContentSubdirSuffix = TargetDomain.GenerationConfiguration.CompositeContentSubdirSuffix;

                foreach (var Exclusion in Options.ExcludedIdeas)
                {
                    var Idea = ResolveIdea(TargetComposition, Exclusion);
                    if (Idea == null)
                        return Fail("Cannot resolve excluded idea '" + Exclusion + "'. Use an idea GlobalId or TechName.");

                    Configuration.ExcludedIdeasGlobalIds.Add(Idea.GlobalId.ToString());
                }

                var Preparation = OutputTemplatePreparationService.PrepareComposition(TargetComposition, Language, "Generate Files");
                if (Preparation.HasBlockingErrors)
                    return Fail(Preparation.BuildBlockingMessage());

                Configuration.Language = Preparation.Language;
                TargetDomain.CurrentExternalLanguage = Preparation.Language;

                var Generator = new FileGenerator(TargetComposition, Preparation.Language, Configuration, Preparation);
                var PreviousReadTechNames = LoadResult.Result.ReadTechNamesAsProgramIdentifiers;
                LoadResult.Result.ReadTechNamesAsProgramIdentifiers = Options.UseTechNamesAsProgramIdentifiers;
                GenerationManager.SetCurrentGenerationConsumer(TargetComposition);

                try
                {
                    var Result = Generator.Generate(new ThreadWorker<int>(Dispatcher.CurrentDispatcher));
                    if (!Result.WasSuccessful)
                        return Fail(Result.Message);

                    return Succeed(Result.Message, TargetDirectory);
                }
                finally
                {
                    LoadResult.Result.ReadTechNamesAsProgramIdentifiers = PreviousReadTechNames;
                    GenerationManager.SetCurrentGenerationConsumer(null);
                }
            });
        }

        private static OperationResult<CompositionEngine> RehydrateCompositionFromJson(CompositionJsonDocument CompositionDocument,
                                                                                       DomainJsonDocument DomainDocument)
        {
            var RehydratedDomain = RehydrateDomainFromJson(DomainDocument);
            if (!RehydratedDomain.WasSuccessful)
                return RehydratedDomain;

            var ImportDocument = CloneCompositionDocument(CompositionDocument);
            ImportDocument.ImportOptions = BuildFullStateImportOptions();

            var Preview = CompositionJsonImporter.Preview(RehydratedDomain.Result.TargetComposition, ImportDocument);
            if (Preview.CompatibilityBlocked || Preview.HasErrors)
                return OperationResult.Failure<CompositionEngine>("Composition JSON rehydration preview failed." + Environment.NewLine +
                                                                  Preview.ToSummaryString(true));

            var ApplyReport = CompositionJsonImporter.Import(RehydratedDomain.Result, ImportDocument, Preview);
            if (ApplyReport.CompatibilityBlocked || ApplyReport.HasErrors)
                return OperationResult.Failure<CompositionEngine>("Composition JSON rehydration failed." + Environment.NewLine +
                                                                  ApplyReport.ToSummaryString(true));

            return OperationResult.Success(RehydratedDomain.Result);
        }

        private static OperationResult<CompositionEngine> RehydrateDomainFromJson(DomainJsonDocument Document)
        {
            var EngineResult = CreateBlankComposition(null, true);
            if (!EngineResult.WasSuccessful)
                return EngineResult;

            var TargetDomain = EngineResult.Result.TargetComposition.CompositeContentDomain;
            var Preview = DomainJsonImporter.Preview(TargetDomain, Document);
            if (Preview.Errors.Count > 0)
                return OperationResult.Failure<CompositionEngine>("Domain JSON rehydration preview failed." + Environment.NewLine +
                                                                  Preview.PreviewSummary());

            var ApplyReport = DomainJsonImporter.ApplyPreservingIds(TargetDomain, Document, new DomainJsonImportReport());
            if (ApplyReport.Errors.Count > 0)
                return OperationResult.Failure<CompositionEngine>("Domain JSON rehydration failed." + Environment.NewLine +
                                                                  ApplyReport.ApplySummary());

            return OperationResult.Success(EngineResult.Result);
        }

        private static OperationResult<CompositionEngine> CreateBlankComposition(Domain RootDomain, bool IsForEditDomain)
        {
            var Context = HeadlessBootstrap.Initialize();
            CompositionEngine.CreateActiveCompositionEngine(Context.Compositions, Context.Visualizer, IsForEditDomain);
            var Materialized = CompositionEngine.Materialize(null, RootDomain, false);

            if (Materialized == null || Materialized.Item1 == null)
                return OperationResult.Failure<CompositionEngine>("Cannot create blank composition: " +
                                                                  (Materialized == null ? "" : Materialized.Item2));

            Context.Workspace.LoadDocument(Materialized.Item1.TargetComposition);
            return OperationResult.Success(Materialized.Item1);
        }

        private static CompositionJsonImportOptions BuildFullStateImportOptions()
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

        private static bool CompareCompositionDocuments(CompositionJsonDocument Original, CompositionJsonDocument Reexported,
                                                        string OriginalNormalizedPath, string ReexportedNormalizedPath,
                                                        out string Mismatch)
        {
            var NormalizedOriginal = CloneCompositionDocument(Original);
            var NormalizedReexported = CloneCompositionDocument(Reexported);
            NormalizeCompositionDocument(NormalizedOriginal);
            NormalizeCompositionDocument(NormalizedReexported);

            CompositionJsonSerializer.Save(NormalizedOriginal, OriginalNormalizedPath);
            CompositionJsonSerializer.Save(NormalizedReexported, ReexportedNormalizedPath);

            if (CompositionJsonSerializer.Serialize(NormalizedOriginal) == CompositionJsonSerializer.Serialize(NormalizedReexported))
            {
                Mismatch = "Composition JSON matched.";
                return true;
            }

            Mismatch = "Composition JSON mismatch. Normalized files: " + OriginalNormalizedPath + " and " + ReexportedNormalizedPath + ".";
            return false;
        }

        private static bool CompareDomainDocuments(DomainJsonDocument Original, DomainJsonDocument Reexported,
                                                   string OriginalNormalizedPath, string ReexportedNormalizedPath,
                                                   out string Mismatch)
        {
            var NormalizedOriginal = CloneDomainDocument(Original);
            var NormalizedReexported = CloneDomainDocument(Reexported);
            NormalizeDomainDocument(NormalizedOriginal);
            NormalizeDomainDocument(NormalizedReexported);

            DomainJsonSerializer.Save(NormalizedOriginal, OriginalNormalizedPath);
            DomainJsonSerializer.Save(NormalizedReexported, ReexportedNormalizedPath);

            if (DomainJsonSerializer.Serialize(NormalizedOriginal) == DomainJsonSerializer.Serialize(NormalizedReexported))
            {
                Mismatch = "Domain JSON matched.";
                return true;
            }

            Mismatch = "Domain JSON mismatch. Normalized files: " + OriginalNormalizedPath + " and " + ReexportedNormalizedPath + ".";
            return false;
        }

        private static CompositionJsonDocument CloneCompositionDocument(CompositionJsonDocument Source)
        {
            return CompositionJsonSerializer.Deserialize(CompositionJsonSerializer.Serialize(Source));
        }

        private static DomainJsonDocument CloneDomainDocument(DomainJsonDocument Source)
        {
            return DomainJsonSerializer.Deserialize(DomainJsonSerializer.Serialize(Source));
        }

        private static void NormalizeCompositionDocument(CompositionJsonDocument Document)
        {
            if (Document != null)
                Document.ExportedAtUtc = null;
        }

        private static void NormalizeDomainDocument(DomainJsonDocument Document)
        {
            if (Document != null)
                Document.ExportedAtUtc = null;
        }

        private static DomainJsonImportReport ApplyDomainJson(CompositionEngine Engine, Domain TargetDomain, DomainJsonDocument Document)
        {
            DomainJsonImportReport Report = null;
            Engine.StartCommandVariation("Import Domain JSON");

            try
            {
                Report = DomainJsonImporter.Apply(TargetDomain, Document, new DomainJsonImportReport());
                TargetDomain.UpdateVersion();
                DomainServices.UpdateDomainDependants(TargetDomain);

                if (Engine.IsVariating)
                    Engine.CompleteCommandVariation();

                Engine.ExistenceStatus = EExistenceStatus.Modified;
            }
            catch
            {
                if (Engine.IsVariating)
                    Engine.DiscardCommandVariation();

                throw;
            }

            return Report;
        }

        private static OperationResult<CompositionEngine> LoadComposition(string Input)
        {
            var Context = HeadlessBootstrap.Initialize();
            var Engine = CompositionEngine.CreateActiveCompositionEngine(Context.Compositions, Context.Visualizer, false);
            var Result = CompositionEngine.Materialize(new Uri(Path.GetFullPath(Input), UriKind.Absolute));

            if (Result == null || Result.Item1 == null)
                return OperationResult.Failure<CompositionEngine>("Cannot load composition: " + (Result == null ? "" : Result.Item2));

            Context.Workspace.LoadDocument(Result.Item1.TargetComposition);
            return OperationResult.Success(Result.Item1);
        }

        private static OperationResult<Domain> LoadNativeDomain(string Input)
        {
            HeadlessBootstrap.Initialize();
            var Result = CompositionEngine.MaterializeDomain(new Uri(Path.GetFullPath(Input), UriKind.Absolute));

            if (Result == null || Result.Item1 == null)
                return OperationResult.Failure<Domain>("Cannot load domain: " + (Result == null ? "" : Result.Item2));

            return OperationResult.Success(Result.Item1);
        }

        private static OperationResult<CompositionEngine> LoadNativeDomainForEdit(string Input)
        {
            var Context = HeadlessBootstrap.Initialize();
            var DomainResult = CompositionEngine.MaterializeDomain(new Uri(Path.GetFullPath(Input), UriKind.Absolute));

            if (DomainResult == null || DomainResult.Item1 == null)
                return OperationResult.Failure<CompositionEngine>("Cannot load domain: " + (DomainResult == null ? "" : DomainResult.Item2));

            var Engine = CompositionEngine.CreateActiveCompositionEngine(Context.Compositions, Context.Visualizer, true);
            var Materialized = CompositionEngine.Materialize(null, DomainResult.Item1, false);

            if (Materialized == null || Materialized.Item1 == null)
                return OperationResult.Failure<CompositionEngine>("Cannot create editable domain context: " + (Materialized == null ? "" : Materialized.Item2));

            Context.Workspace.LoadDocument(Materialized.Item1.TargetComposition);
            return OperationResult.Success(Materialized.Item1);
        }

        private static void SaveComposition(Composition SourceComposition, string Output)
        {
            EnsureParentDirectory(Output);
            var Location = new Uri(Path.GetFullPath(Output), UriKind.Absolute);

            var Error = DocumentEngine.StoreToLocation<ISphereModel>(
                SourceComposition,
                Composition.__ClassDefinitor.Name,
                SourceComposition.Classification.ContentTypeCode,
                Location,
                CompositionEngine.CompositionDocumentUri,
                false,
                false,
                SourceComposition,
                null,
                true,
                delegate(Package Package)
                {
                    ContainerSnapshotService.WriteCompositionSnapshot(Package, SourceComposition, CompositionEngine.CompositionDocumentUri);
                });

            if (!String.IsNullOrEmpty(Error))
                throw new InvalidOperationException(Error);
        }

        private static void SaveDomain(Domain SourceDomain, string Output)
        {
            EnsureParentDirectory(Output);
            SourceDomain.SetTemplateSaving(false);

            var Error = DocumentEngine.StoreToLocation<Domain>(
                SourceDomain,
                Domain.__ClassDefinitor.Name,
                SourceDomain.Classification.ContentTypeCode,
                new Uri(Path.GetFullPath(Output), UriKind.Absolute),
                DomainsManager.DomainDocumentUri,
                false,
                false,
                SourceDomain,
                null,
                true,
                delegate(Package Package)
                {
                    ContainerSnapshotService.WriteDomainSnapshot(Package, SourceDomain, DomainsManager.DomainDocumentUri, false);
                });

            if (!String.IsNullOrEmpty(Error))
                throw new InvalidOperationException(Error);
        }

        private static OperationResult<string> ValidateInputOutput(string Input, string Output, string InputExtension, string OutputExtension, bool AllowSamePath)
        {
            var Validation = ValidateExistingFile(Input, InputExtension, "input");
            if (!Validation.WasSuccessful)
                return Validation;

            if (String.IsNullOrWhiteSpace(Output))
                return Fail("Missing --output.");

            if (!String.IsNullOrWhiteSpace(OutputExtension) && !HasExtension(Output, OutputExtension))
                return Fail("Output must have ." + OutputExtension + " extension.");

            if (!AllowSamePath && SamePath(Input, Output))
                return Fail("Input and output paths must be different.");

            return Succeed(null, null);
        }

        private static OperationResult<string> ValidateImportPaths(string Input, string Json, string Output, string InputExtension, string OutputExtension, bool InPlace)
        {
            var Validation = ValidateInputOutput(Input, Output, InputExtension, OutputExtension, true);
            if (!Validation.WasSuccessful)
                return Validation;

            if (!File.Exists(Json))
                return Fail("JSON file not found: " + Json);

            if (!HasExtension(Json, "json"))
                return Fail("JSON input must have .json extension.");

            if (SamePath(Input, Output) && !InPlace)
                return Fail("Refusing to overwrite input. Use --in-place with --output set to the input path.");

            if (InPlace && !SamePath(Input, Output))
                return Fail("--in-place requires --output to match --input.");

            return Succeed(null, null);
        }

        private static OperationResult<string> ValidateDomainInput(string Input)
        {
            if (String.IsNullOrWhiteSpace(Input))
                return Fail("Missing --input.");

            if (!File.Exists(Input))
                return Fail("Input file not found: " + Input);

            if (!HasExtension(Input, Domain.FILE_EXTENSION_DOMAIN) && !HasExtension(Input, Composition.FILE_EXTENSION_COMPOSITION))
                return Fail("Input must have .tdom or .tcom extension.");

            return Succeed(null, null);
        }

        private static OperationResult<string> ValidateOutputDirectory(string OutputDirectory)
        {
            if (String.IsNullOrWhiteSpace(OutputDirectory))
                return Fail("Missing --output-dir.");

            if (File.Exists(OutputDirectory))
                return Fail("--output-dir points to a file: " + OutputDirectory);

            return Succeed(null, null);
        }

        private static OperationResult<string> ValidateExistingFile(string Input, string Extension, string Name)
        {
            if (String.IsNullOrWhiteSpace(Input))
                return Fail("Missing --" + Name + ".");

            if (!File.Exists(Input))
                return Fail(Name.Substring(0, 1).ToUpperInvariant() + Name.Substring(1) + " file not found: " + Input);

            if (!HasExtension(Input, Extension))
                return Fail(nameWithArticle(Name) + " must have ." + Extension + " extension.");

            return Succeed(null, null);
        }

        private static string nameWithArticle(string Name)
        {
            return Name.Substring(0, 1).ToUpperInvariant() + Name.Substring(1);
        }

        private static ExternalLanguageDeclaration ResolveLanguage(Domain Domain, string LanguageTechName)
        {
            if (Domain == null || Domain.ExternalLanguages == null || String.IsNullOrWhiteSpace(LanguageTechName))
                return null;

            return Domain.ExternalLanguages.FirstOrDefault(Language => String.Equals(Language.TechName, LanguageTechName, StringComparison.OrdinalIgnoreCase));
        }

        private static string KnownLanguages(Domain Domain)
        {
            if (Domain == null || Domain.ExternalLanguages == null)
                return "<none>";

            return String.Join(", ", Domain.ExternalLanguages.Select(Language => Language.TechName).OrderBy(Text => Text).ToArray());
        }

        private static Idea ResolveIdea(Composition Composition, string Reference)
        {
            if (Composition == null || String.IsNullOrWhiteSpace(Reference))
                return null;

            Guid Parsed;
            var Ideas = new List<Idea>();
            Ideas.Add(Composition);
            Ideas.AddRange(Composition.DeclaredIdeas);

            if (Guid.TryParse(Reference, out Parsed))
                return Ideas.FirstOrDefault(Idea => Idea != null && Idea.GlobalId == Parsed);

            return Ideas.FirstOrDefault(Idea => Idea != null &&
                                                String.Equals(Idea.TechName, Reference, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasExtension(string PathText, string Extension)
        {
            if (String.IsNullOrWhiteSpace(PathText) || String.IsNullOrWhiteSpace(Extension))
                return false;

            return String.Equals(Path.GetExtension(PathText).TrimStart('.'), Extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SamePath(string First, string Second)
        {
            if (String.IsNullOrWhiteSpace(First) || String.IsNullOrWhiteSpace(Second))
                return false;

            return String.Equals(Path.GetFullPath(First).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 Path.GetFullPath(Second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureParentDirectory(string FilePath)
        {
            var Parent = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (!String.IsNullOrEmpty(Parent) && !Directory.Exists(Parent))
                Directory.CreateDirectory(Parent);
        }

        private static void TryDelete(string FilePath)
        {
            try
            {
                if (!String.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
            }
        }

        private static OperationResult<string> ExpectedOperation(Func<OperationResult<string>> Operation)
        {
            try
            {
                return Operation();
            }
            catch (InvalidDataException Problem)
            {
                return Fail(Problem.Message);
            }
            catch (IOException Problem)
            {
                return Fail(Problem.Message);
            }
            catch (UnauthorizedAccessException Problem)
            {
                return Fail(Problem.Message);
            }
            catch (ExternalAnomaly Problem)
            {
                return Fail(Problem.Message);
            }
            catch (UsageAnomaly Problem)
            {
                return Fail(Problem.Message);
            }
            catch (BusinessAnomaly Problem)
            {
                return Fail(Problem.Message);
            }
            catch (ArgumentException Problem)
            {
                return Fail(Problem.Message);
            }
            catch (InvalidOperationException Problem)
            {
                return Fail(Problem.Message);
            }
        }

        private static OperationResult<string> Succeed(string Message, string Result)
        {
            return OperationResult.Success(Result, Message);
        }

        private static OperationResult<string> Fail(string Message)
        {
            return OperationResult.Failure<string>(Message);
        }
    }
}
