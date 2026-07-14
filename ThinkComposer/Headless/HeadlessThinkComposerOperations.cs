// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Reusable command-line operations for JSON interchange, reports and output generation.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.Composer.ContainerSnapshots;
using Instrumind.ThinkComposer.Composer.Generation;
using Instrumind.ThinkComposer.Composer.GitSync;
using Instrumind.ThinkComposer.Composer.JsonInterchange;
using Instrumind.ThinkComposer.Composer.Reporting;
using Instrumind.ThinkComposer.Definitor;
using Instrumind.ThinkComposer.Definitor.DomainJsonInterchange;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

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

    public sealed class HeadlessImageExportOptions
    {
        public string Input { get; set; }
        public string Output { get; set; }
        public string ViewTechName { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public double? Padding { get; set; }
        public bool Transparent { get; set; }
        public IList<string> FitTechNames { get; private set; }

        public HeadlessImageExportOptions()
        {
            this.FitTechNames = new List<string>();
        }
    }

    public static class HeadlessThinkComposerOperations
    {
        private const int DefaultImageExportWidth = 1600;
        private const int DefaultImageExportHeight = 1200;
        private const int MinImageExportDimension = 24;
        private const int MaxImageExportDimension = 10000;
        private const long MaxImageExportPixels = 100000000;
        private const double DefaultImageFitPadding = 20.0;

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

        public static OperationResult<string> ExportCompositionImage(HeadlessImageExportOptions Options)
        {
            return ExpectedOperation(delegate
            {
                if (Options == null)
                    return Fail("No image export options were supplied.");

                var Validation = ValidateInputOutput(Options.Input, Options.Output, Composition.FILE_EXTENSION_COMPOSITION, null, false);
                if (!Validation.WasSuccessful)
                    return Validation;

                if (!IsImageOutputPath(Options.Output))
                    return Fail("Image output must have .png, .jpg, .jpeg, .gif, .tif, .tiff, or .bmp extension.");

                var LoadResult = LoadComposition(Options.Input);
                if (!LoadResult.WasSuccessful)
                    return Fail(LoadResult.Message);

                var TargetComposition = LoadResult.Result.TargetComposition;
                var TargetView = ResolveCompositionView(TargetComposition, Options.ViewTechName);
                if (TargetView == null)
                    return Fail("Cannot resolve view '" + Options.ViewTechName.ToStringAlways() + "'. Known view TechNames: " +
                                KnownViewTechNames(TargetComposition));

                var FitTechNames = NormalizeReferences(Options.FitTechNames);
                var FitPadding = Options.Padding.HasValue ? Options.Padding.Value : DefaultImageFitPadding;
                if (FitPadding < 0)
                    return Fail("--padding must be zero or greater.");

                Rect SourceArea;
                string FitDescription;
                if (FitTechNames.Count > 0)
                {
                    var FitResult = DetermineImageFitSourceArea(TargetView, FitTechNames, FitPadding);
                    if (!FitResult.WasSuccessful)
                        return Fail(FitResult.Message);

                    SourceArea = FitResult.Result;
                    FitDescription = "TechNames: " + String.Join(", ", FitTechNames.ToArray());
                }
                else
                {
                    SourceArea = TargetView.DetermineContentArea();
                    FitDescription = "full view";
                }

                if (SourceArea == Rect.Empty || SourceArea.Width <= 0 || SourceArea.Height <= 0)
                    return Fail("View '" + DescribeView(TargetView) + "' has no renderable content.");

                int ExportWidth;
                int ExportHeight;
                string SizeError;
                if (!TryResolveImageExportSize(SourceArea, Options.Width, Options.Height, out ExportWidth, out ExportHeight, out SizeError))
                    return Fail(SizeError);

                EnsureParentDirectory(Options.Output);
                var Target = Path.GetFullPath(Options.Output);
                var Snapshot = TargetView.ToSnapshot(Options.Transparent, ExportWidth, ExportHeight, null, SourceArea);
                if (Snapshot == null)
                    return Fail("View '" + DescribeView(TargetView) + "' did not produce a renderable image snapshot.");

                var Error = Display.ExportImageTo(Target, Snapshot.Item1.RenderToDrawingVisual(), ExportWidth, ExportHeight);
                if (!Error.IsAbsent())
                    return Fail(Error);

                return Succeed("View image exported to: " + Target + Environment.NewLine +
                               "View: " + DescribeView(TargetView) + Environment.NewLine +
                               "Fit: " + FitDescription + Environment.NewLine +
                               "Size: " + ExportWidth.ToString(CultureInfo.InvariantCulture) + "x" +
                               ExportHeight.ToString(CultureInfo.InvariantCulture),
                               Target);
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
                SaveComposition(LoadResult.Result.TargetComposition, Output, null, null, Input);

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
                    SaveDomain(EditResult.Result.TargetComposition.CompositeContentDomain, Output, false, Input);
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
                    SaveComposition(LoadResult.Result.TargetComposition, Output, null, null, Input);
                    return Succeed("Embedded Domain JSON imported into: " + Path.GetFullPath(Output) + Environment.NewLine +
                                   ApplyReport.ApplySummary(), Path.GetFullPath(Output));
                }
            });
        }

        public static OperationResult<string> UpdateEmbeddedDomainFromNativeDomain(string Input, string DomainInput, string Output, bool InPlace, bool PreviewOnly)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateInputOutput(Input, Output, Composition.FILE_EXTENSION_COMPOSITION, Composition.FILE_EXTENSION_COMPOSITION, true);
                if (!Validation.WasSuccessful)
                    return Validation;

                if (String.IsNullOrWhiteSpace(DomainInput))
                    return Fail("Missing --domain.");

                if (!File.Exists(DomainInput))
                    return Fail("Domain source file not found: " + DomainInput);

                if (!HasExtension(DomainInput, Domain.FILE_EXTENSION_DOMAIN))
                    return Fail("Domain source must have .tdom extension.");

                if (SamePath(Input, Output) && !InPlace)
                    return Fail("Refusing to overwrite input. Use --in-place with --output set to the input path.");

                if (InPlace && !SamePath(Input, Output))
                    return Fail("--in-place requires --output to match --input.");

                var LoadResult = LoadComposition(Input);
                if (!LoadResult.WasSuccessful)
                    return Fail(LoadResult.Message);

                var SourceDomainResult = LoadNativeDomain(DomainInput);
                if (!SourceDomainResult.WasSuccessful)
                    return Fail(SourceDomainResult.Message);

                var TargetDomain = LoadResult.Result.TargetComposition.CompositeContentDomain;
                var Document = DomainJsonExporter.Export(SourceDomainResult.Result);
                var Preview = DomainJsonImporter.Preview(TargetDomain, Document);

                if (PreviewOnly)
                    return Succeed("Embedded Domain update preview completed." + Environment.NewLine + Preview.PreviewSummary(), null);

                var ApplyReport = ApplyDomainJson(LoadResult.Result, TargetDomain, Document);
                var GitSyncLink = SamePath(Input, Output) ? ReadPackageGitSyncLink(Input, GitPackageLink.KindComposition) : null;
                var EmbeddedDomainGitSyncLink = ReadDomainGitSyncLink(DomainInput) ?? ReadEmbeddedDomainGitSyncLink(Input);
                SaveComposition(LoadResult.Result.TargetComposition, Output, GitSyncLink, EmbeddedDomainGitSyncLink, Input);

                return Succeed("Embedded Domain updated from: " + Path.GetFullPath(DomainInput) + Environment.NewLine +
                               "Output: " + Path.GetFullPath(Output) + Environment.NewLine +
                               (EmbeddedDomainGitSyncLink == null
                                ? ""
                                : "Embedded Domain gitSync link copied from Domain source." + Environment.NewLine) +
                               ApplyReport.ApplySummary(), Path.GetFullPath(Output));
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

        public static OperationResult<string> InspectPackagePersistence(string Input)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateExistingFile(Input, null, "input");
                if (!Validation.WasSuccessful)
                    return Validation;

                var Inspection = JsonPackagePersistence.Inspect(Input);
                return Succeed(JsonPackagePersistence.DescribeInspection(Inspection), Path.GetFullPath(Input));
            });
        }

        public static OperationResult<string> LinkPackageToGit(string Input, string Remote, string Branch, string RepositoryPath,
                                                               string DomainPath, string Output, bool InPlace)
        {
            return ExpectedOperation(delegate
            {
                var Message = GitPackageSyncService.LinkPackage(Input, Output, InPlace, Remote, Branch, RepositoryPath, DomainPath);
                return Succeed(Message, String.IsNullOrWhiteSpace(Output) ? Path.GetFullPath(Input) : Path.GetFullPath(Output));
            });
        }

        public static OperationResult<string> UnlinkPackageFromGit(string Input, string Output, bool InPlace)
        {
            return ExpectedOperation(delegate
            {
                var Message = GitPackageSyncService.UnlinkPackage(Input, Output, InPlace);
                return Succeed(Message, String.IsNullOrWhiteSpace(Output) ? Path.GetFullPath(Input) : Path.GetFullPath(Output));
            });
        }

        public static OperationResult<string> GitPackageStatus(string Input)
        {
            return ExpectedOperation(delegate
            {
                return Succeed(GitPackageSyncService.StatusPackage(Input), Path.GetFullPath(Input));
            });
        }

        public static OperationResult<string> PullPackageFromGit(string Input, string Output, bool InPlace, string BackupDirectory)
        {
            return ExpectedOperation(delegate
            {
                var Result = GitPackageSyncService.PullPackage(Input, Output, InPlace, BackupDirectory);
                return Succeed(Result.Message, Result.OutputPath);
            });
        }

        public static OperationResult<string> PushCompositionToGit(string Input, string Message)
        {
            return ExpectedOperation(delegate
            {
                return Succeed(GitPackageSyncService.PushComposition(Input, Message), Path.GetFullPath(Input));
            });
        }

        public static OperationResult<string> ConvertCompositionToJsonPersistence(string Input, string Output)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateInputOutput(Input, Output, Composition.FILE_EXTENSION_COMPOSITION, Composition.FILE_EXTENSION_COMPOSITION, false);
                if (!Validation.WasSuccessful)
                    return Validation;

                var LoadResult = LoadComposition(Input);
                if (!LoadResult.WasSuccessful)
                    return Fail(LoadResult.Message);

                SaveComposition(LoadResult.Result.TargetComposition, Output, null, null, Input);
                var Inspection = JsonPackagePersistence.Inspect(Output);
                if (!Inspection.JsonAuthoritative)
                    return Fail("Converted composition package is not JSON-authoritative." + Environment.NewLine +
                                JsonPackagePersistence.DescribeInspection(Inspection));

                return Succeed("Composition converted to JSON-authoritative package: " + Path.GetFullPath(Output) + Environment.NewLine +
                               JsonPackagePersistence.DescribeInspection(Inspection), Path.GetFullPath(Output));
            });
        }

        public static OperationResult<string> ConvertDomainToJsonPersistence(string Input, string Output)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateInputOutput(Input, Output, Domain.FILE_EXTENSION_DOMAIN, Domain.FILE_EXTENSION_DOMAIN, false);
                if (!Validation.WasSuccessful)
                    return Validation;

                var LoadResult = LoadNativeDomain(Input);
                if (!LoadResult.WasSuccessful)
                    return Fail(LoadResult.Message);

                var IncludeTemplate = LoadResult.Result.OwnerComposition != null;
                SaveDomain(LoadResult.Result, Output, IncludeTemplate, Input);
                var Inspection = JsonPackagePersistence.Inspect(Output);
                if (!Inspection.JsonAuthoritative)
                    return Fail("Converted domain package is not JSON-authoritative." + Environment.NewLine +
                                JsonPackagePersistence.DescribeInspection(Inspection));

                return Succeed("Domain converted to JSON-authoritative package: " + Path.GetFullPath(Output) + Environment.NewLine +
                               JsonPackagePersistence.DescribeInspection(Inspection), Path.GetFullPath(Output));
            });
        }

        public static OperationResult<string> ValidateCompositionJsonPersistence(string Input, string OutputDirectory)
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

                var FirstPath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-1.tcom");
                var SecondPath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-2.tcom");
                var TimestampOnlyPath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-domain-timestamp-only.tcom");
                var RenderStatePath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-render-state-change.tcom");
                var CompositeBaselinePath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-composite-active-baseline.tcom");
                var CompositeChangePath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-composite-active-change.tcom");
                var ImageBaselinePath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-image-complement-baseline.tcom");
                var ImageChangePath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-image-complement-change.tcom");

                var FirstLoad = LoadComposition(Input);
                if (!FirstLoad.WasSuccessful)
                    return Fail(FirstLoad.Message);

                SaveComposition(FirstLoad.Result.TargetComposition, FirstPath, null, null, Input);

                var FirstInspection = JsonPackagePersistence.Inspect(FirstPath);
                if (!FirstInspection.JsonAuthoritative || FirstInspection.HasCompositionBinary ||
                    FirstInspection.TransitionalWithBinaryFallback || FirstInspection.LegacyBinaryOnly)
                    return Fail("First saved composition package is not JSON-authoritative and binary-free." + Environment.NewLine +
                                JsonPackagePersistence.DescribeInspection(FirstInspection));

                var SecondLoad = LoadComposition(FirstPath);
                if (!SecondLoad.WasSuccessful)
                    return Fail(SecondLoad.Message);

                if (!CompositionEngine.LastLoadUsedJsonPersistence || CompositionEngine.LastLoadUsedLegacyBinaryFallback)
                    return Fail("Modern composition package did not reopen from JSON persistence." + Environment.NewLine +
                                CompositionEngine.LastLoadPersistenceDiagnostic.ToStringAlways());

                SaveComposition(SecondLoad.Result.TargetComposition, SecondPath, null, null, FirstPath);

                var SecondInspection = JsonPackagePersistence.Inspect(SecondPath);
                if (!SecondInspection.JsonAuthoritative || SecondInspection.HasCompositionBinary ||
                    SecondInspection.TransitionalWithBinaryFallback || SecondInspection.LegacyBinaryOnly)
                    return Fail("Second saved composition package is not JSON-authoritative and binary-free." + Environment.NewLine +
                                JsonPackagePersistence.DescribeInspection(SecondInspection));

                var FirstPayload = JsonPackagePersistence.ReadCompositionPackage(FirstPath);
                var SecondPayload = JsonPackagePersistence.ReadCompositionPackage(SecondPath);

                CompositionJsonSerializer.Save(FirstPayload.CompositionDocument, Path.Combine(TargetDirectory, "composition-persistence-1.json"));
                CompositionJsonSerializer.Save(SecondPayload.CompositionDocument, Path.Combine(TargetDirectory, "composition-persistence-2.json"));
                DomainJsonSerializer.Save(FirstPayload.DomainDocument, Path.Combine(TargetDirectory, "domain-persistence-1.json"));
                DomainJsonSerializer.Save(SecondPayload.DomainDocument, Path.Combine(TargetDirectory, "domain-persistence-2.json"));

                string CompositionMismatch;
                string DomainMismatch;
                var CompositionMatches = CompareCompositionDocuments(FirstPayload.CompositionDocument, SecondPayload.CompositionDocument,
                                                                      Path.Combine(TargetDirectory, "composition-persistence-1.normalized.json"),
                                                                      Path.Combine(TargetDirectory, "composition-persistence-2.normalized.json"),
                                                                      out CompositionMismatch);
                var DomainMatches = CompareDomainDocuments(FirstPayload.DomainDocument, SecondPayload.DomainDocument,
                                                           Path.Combine(TargetDirectory, "domain-persistence-1.normalized.json"),
                                                           Path.Combine(TargetDirectory, "domain-persistence-2.normalized.json"),
                                                           out DomainMismatch);

                if (!CompositionMatches || !DomainMatches)
                    return Fail("Composition JSON persistence validation failed." + Environment.NewLine +
                                "Artifacts: " + TargetDirectory + Environment.NewLine +
                                CompositionMismatch + Environment.NewLine +
                                DomainMismatch);

                var HardeningNotes = ValidateCompositionPersistenceHardening(FirstPath, Path.GetFullPath(Input), TargetDirectory);

                var RenderStateView = SecondLoad.Result.TargetComposition.RootView;
                if (RenderStateView == null)
                    return Fail("Composition JSON persistence validation could not find a root View for preview-cache invalidation.");

                var RenderStateViewId = IdOf(RenderStateView);
                HardeningNotes.Add(ValidateDomainTimestampPreviewReuse(
                    SecondLoad.Result.TargetComposition, RenderStateView, SecondPath, TimestampOnlyPath));
                var BeforeRenderState = ReadPreviewCacheRecord(SecondPath, RenderStateViewId);
                var OriginalShowIndicators = RenderStateView.ShowIndicators;
                try
                {
                    RenderStateView.ShowIndicators = !OriginalShowIndicators;
                    SaveComposition(SecondLoad.Result.TargetComposition, RenderStatePath, null, null, SecondPath);
                }
                finally
                {
                    RenderStateView.ShowIndicators = OriginalShowIndicators;
                }

                var AfterRenderState = ReadPreviewCacheRecord(RenderStatePath, RenderStateViewId);
                if (String.Equals(BeforeRenderState.Item1, AfterRenderState.Item1, StringComparison.OrdinalIgnoreCase))
                    return Fail("Changing a render-affecting View setting did not change the v2 preview inputSha256.");
                if (String.Equals(AfterRenderState.Item2, "reused", StringComparison.OrdinalIgnoreCase))
                    return Fail("Changing a render-affecting View setting incorrectly reused the prior preview PNG.");
                HardeningNotes.Add("Hardening: a render-affecting View setting changed inputSha256 and did not reuse the prior preview.");

                HardeningNotes.Add(ValidateCompositeActiveViewPreviewInvalidation(
                    SecondLoad.Result.TargetComposition, RenderStateView, SecondPath,
                    CompositeBaselinePath, CompositeChangePath));
                HardeningNotes.Add(ValidateImageComplementPreviewInvalidation(
                    SecondLoad.Result.TargetComposition, RenderStateView, SecondPath,
                    ImageBaselinePath, ImageChangePath));

                return Succeed("Composition JSON persistence validation passed." + Environment.NewLine +
                               String.Join(Environment.NewLine, HardeningNotes.ToArray()) + Environment.NewLine +
                               "Artifacts: " + TargetDirectory + Environment.NewLine +
                               JsonPackagePersistence.DescribeInspection(FirstInspection), FirstPath);
            });
        }

        private static string ValidateDomainTimestampPreviewReuse(Composition Composition, View RootView,
                                                                   string PreviewSourcePath, string ChangedPath)
        {
            var Domain = Composition == null ? null : Composition.CompositeContentDomain;
            if (Domain == null || Domain.Version == null)
                throw new InvalidOperationException("Preview-cache hardening could not find a Domain version for timestamp normalization validation.");

            var ViewId = IdOf(RootView);
            var Before = ReadPreviewCacheRecord(PreviewSourcePath, ViewId);
            var OriginalLastModification = Domain.Version.LastModification;
            try
            {
                Domain.Version.LastModification = OriginalLastModification.Ticks < DateTime.MaxValue.Ticks
                                                ? OriginalLastModification.AddTicks(1)
                                                : OriginalLastModification.AddTicks(-1);
                SaveComposition(Composition, ChangedPath, null, null, PreviewSourcePath);
            }
            finally
            {
                Domain.Version.LastModification = OriginalLastModification;
            }

            var After = ReadPreviewCacheRecord(ChangedPath, ViewId);
            if (!String.Equals(Before.Item1, After.Item1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Changing only Domain.Version.LastModification incorrectly changed the v2 preview inputSha256.");
            if (!String.Equals(After.Item2, "reused", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(After.Item2, "empty", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Changing only Domain.Version.LastModification did not reuse the prior preview cache entry.");

            return "Hardening: a Domain timestamp-only edit preserved inputSha256 and reused the prior preview.";
        }

        private static string ValidateCompositeActiveViewPreviewInvalidation(Composition Composition, View RootView,
                                                                               string PreviewSourcePath,
                                                                               string BaselinePath, string ChangedPath)
        {
            var Representation = Composition.DeclaredIdeas
                                            .OfType<Concept>()
                                            .Where(Idea => Idea != null && !Object.ReferenceEquals(Idea, Composition))
                                            .SelectMany(Idea => Idea.VisualRepresentators)
                                            .FirstOrDefault(Item => Item != null && Item.MainSymbol != null &&
                                                                    Object.ReferenceEquals(Item.DisplayingView, RootView));
            if (Representation == null)
                throw new InvalidOperationException("Preview-cache hardening could not find a root-view Concept symbol for CompositeActiveView validation.");

            var CompositeIdea = Representation.RepresentedIdea;
            if (CompositeIdea == null || CompositeIdea.CompositeViews == null)
                throw new InvalidOperationException("Preview-cache hardening found a Concept without a CompositeViews collection.");

            var Symbol = Representation.MainSymbol;
            var OriginalActiveView = CompositeIdea.CompositeActiveView;
            var OriginalAreDetailsShown = Symbol.AreDetailsShown;
            var OriginalShowCompositeContent = Symbol.ShowCompositeContentAsDetails;
            var OriginalDetailsPosterHeight = Symbol.DetailsPosterHeight;
            var Suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var FirstNestedView = new View(CompositeIdea, "Preview Cache Active A " + Suffix,
                                           "Preview_Cache_Active_A_" + Suffix);
            var SecondNestedView = new View(CompositeIdea, "Preview Cache Active B " + Suffix,
                                            "Preview_Cache_Active_B_" + Suffix);
            Tuple<string, string> Before;
            Tuple<string, string> After;

            CompositeIdea.CompositeViews.Add(FirstNestedView);
            CompositeIdea.CompositeViews.Add(SecondNestedView);
            try
            {
                Symbol.AreDetailsShown = true;
                Symbol.ShowCompositeContentAsDetails = true;
                Symbol.DetailsPosterHeight = Math.Max(OriginalDetailsPosterHeight, 120.0);
                CompositeIdea.CompositeActiveView = FirstNestedView;
                SaveComposition(Composition, BaselinePath, null, null, PreviewSourcePath);
                Before = ReadPreviewCacheRecord(BaselinePath, IdOf(RootView));

                CompositeIdea.CompositeActiveView = SecondNestedView;
                SaveComposition(Composition, ChangedPath, null, null, BaselinePath);
                After = ReadPreviewCacheRecord(ChangedPath, IdOf(RootView));
            }
            finally
            {
                CompositeIdea.CompositeActiveView = OriginalActiveView;
                CompositeIdea.CompositeViews.Remove(SecondNestedView);
                CompositeIdea.CompositeViews.Remove(FirstNestedView);
                Symbol.AreDetailsShown = OriginalAreDetailsShown;
                Symbol.ShowCompositeContentAsDetails = OriginalShowCompositeContent;
                Symbol.DetailsPosterHeight = OriginalDetailsPosterHeight;
            }

            if (String.Equals(Before.Item1, After.Item1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Changing a rendered Idea's CompositeActiveView did not change the v2 preview inputSha256.");
            if (String.Equals(After.Item2, "reused", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Changing a rendered Idea's CompositeActiveView incorrectly reused the prior preview PNG.");

            return "Hardening: changing a rendered Idea's CompositeActiveView changed inputSha256 and rerendered its containing preview.";
        }

        private static string ValidateImageComplementPreviewInvalidation(Composition Composition, View RootView,
                                                                          string PreviewSourcePath,
                                                                          string BaselinePath, string ChangedPath)
        {
            var Owner = Ownership.Create<View, VisualSymbol>(RootView);
            var Complement = new VisualComplement(Domain.ComplementDefImage, Owner,
                                                  new Point(64.0, 64.0), 32.0);
            Complement.SetPropertyField(VisualComplement.PROP_FIELD_IMAGE,
                                        CreatePreviewCacheHardeningImage(0x20, 0x60, 0xD0));
            RootView.PutComplement(Complement);

            Tuple<string, string> Before;
            Tuple<string, string> After;
            try
            {
                SaveComposition(Composition, BaselinePath, null, null, PreviewSourcePath);
                Before = ReadPreviewCacheRecord(BaselinePath, IdOf(RootView));

                Complement.SetPropertyField(VisualComplement.PROP_FIELD_IMAGE,
                                            CreatePreviewCacheHardeningImage(0xD0, 0x40, 0x20));
                SaveComposition(Composition, ChangedPath, null, null, BaselinePath);
                After = ReadPreviewCacheRecord(ChangedPath, IdOf(RootView));
            }
            finally
            {
                RootView.Clear(Complement);
            }

            if (String.Equals(Before.Item1, After.Item1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Changing an image VisualComplement payload did not change the v2 preview inputSha256.");
            if (String.Equals(After.Item2, "reused", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Changing an image VisualComplement payload incorrectly reused the prior preview PNG.");

            return "Hardening: changing an image VisualComplement payload changed inputSha256 and rerendered its View preview.";
        }

        private static ImageSource CreatePreviewCacheHardeningImage(byte Red, byte Green, byte Blue)
        {
            const int Width = 2;
            const int Height = 2;
            var Pixels = new byte[Width * Height * 4];
            for (var Offset = 0; Offset < Pixels.Length; Offset += 4)
            {
                Pixels[Offset] = Blue;
                Pixels[Offset + 1] = Green;
                Pixels[Offset + 2] = Red;
                Pixels[Offset + 3] = 0xFF;
            }

            var Result = System.Windows.Media.Imaging.BitmapSource.Create(
                Width, Height, 96.0, 96.0, PixelFormats.Bgra32, null, Pixels, Width * 4);
            Result.Freeze();
            return Result;
        }

        public static OperationResult<string> ValidateDomainJsonPersistence(string Input, string OutputDirectory)
        {
            return ExpectedOperation(delegate
            {
                var Validation = ValidateExistingFile(Input, Domain.FILE_EXTENSION_DOMAIN, "input");
                if (!Validation.WasSuccessful)
                    return Validation;

                var DirectoryValidation = ValidateOutputDirectory(OutputDirectory);
                if (!DirectoryValidation.WasSuccessful)
                    return DirectoryValidation;

                var TargetDirectory = Path.GetFullPath(OutputDirectory);
                Directory.CreateDirectory(TargetDirectory);

                var FirstPath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-1.tdom");
                var SecondPath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(Input) + "-json-persistence-2.tdom");

                var FirstLoad = LoadNativeDomain(Input);
                if (!FirstLoad.WasSuccessful)
                    return Fail(FirstLoad.Message);

                SaveDomain(FirstLoad.Result, FirstPath, FirstLoad.Result.OwnerComposition != null, Input);

                var FirstInspection = JsonPackagePersistence.Inspect(FirstPath);
                if (!FirstInspection.JsonAuthoritative || FirstInspection.HasDomainBinary ||
                    FirstInspection.TransitionalWithBinaryFallback || FirstInspection.LegacyBinaryOnly)
                    return Fail("First saved domain package is not JSON-authoritative and binary-free." + Environment.NewLine +
                                JsonPackagePersistence.DescribeInspection(FirstInspection));

                var SecondLoad = LoadNativeDomain(FirstPath);
                if (!SecondLoad.WasSuccessful)
                    return Fail(SecondLoad.Message);

                if (!CompositionEngine.LastLoadUsedJsonPersistence || CompositionEngine.LastLoadUsedLegacyBinaryFallback)
                    return Fail("Modern domain package did not reopen from JSON persistence." + Environment.NewLine +
                                CompositionEngine.LastLoadPersistenceDiagnostic.ToStringAlways());

                SaveDomain(SecondLoad.Result, SecondPath, SecondLoad.Result.OwnerComposition != null, FirstPath);

                var SecondInspection = JsonPackagePersistence.Inspect(SecondPath);
                if (!SecondInspection.JsonAuthoritative || SecondInspection.HasDomainBinary ||
                    SecondInspection.TransitionalWithBinaryFallback || SecondInspection.LegacyBinaryOnly)
                    return Fail("Second saved domain package is not JSON-authoritative and binary-free." + Environment.NewLine +
                                JsonPackagePersistence.DescribeInspection(SecondInspection));

                var FirstPayload = JsonPackagePersistence.ReadDomainPackage(FirstPath);
                var SecondPayload = JsonPackagePersistence.ReadDomainPackage(SecondPath);

                DomainJsonSerializer.Save(FirstPayload.DomainDocument, Path.Combine(TargetDirectory, "domain-persistence-1.json"));
                DomainJsonSerializer.Save(SecondPayload.DomainDocument, Path.Combine(TargetDirectory, "domain-persistence-2.json"));

                string DomainMismatch;
                var DomainMatches = CompareDomainDocuments(FirstPayload.DomainDocument, SecondPayload.DomainDocument,
                                                           Path.Combine(TargetDirectory, "domain-persistence-1.normalized.json"),
                                                           Path.Combine(TargetDirectory, "domain-persistence-2.normalized.json"),
                                                           out DomainMismatch);

                var TemplateMatches = true;
                var TemplateMismatch = "Template composition JSON matched or was absent.";
                if (FirstPayload.TemplateCompositionDocument != null || SecondPayload.TemplateCompositionDocument != null)
                {
                    if (FirstPayload.TemplateCompositionDocument == null || SecondPayload.TemplateCompositionDocument == null)
                    {
                        TemplateMatches = false;
                        TemplateMismatch = "Template composition JSON presence changed across save/reload.";
                    }
                    else
                    {
                        CompositionJsonSerializer.Save(FirstPayload.TemplateCompositionDocument, Path.Combine(TargetDirectory, "template-composition-persistence-1.json"));
                        CompositionJsonSerializer.Save(SecondPayload.TemplateCompositionDocument, Path.Combine(TargetDirectory, "template-composition-persistence-2.json"));
                        TemplateMatches = CompareCompositionDocuments(FirstPayload.TemplateCompositionDocument, SecondPayload.TemplateCompositionDocument,
                                                                     Path.Combine(TargetDirectory, "template-composition-persistence-1.normalized.json"),
                                                                     Path.Combine(TargetDirectory, "template-composition-persistence-2.normalized.json"),
                                                                     out TemplateMismatch);
                    }
                }

                if (!DomainMatches || !TemplateMatches)
                    return Fail("Domain JSON persistence validation failed." + Environment.NewLine +
                                "Artifacts: " + TargetDirectory + Environment.NewLine +
                                DomainMismatch + Environment.NewLine +
                                TemplateMismatch);

                var HardeningNotes = ValidateDomainPersistenceHardening(FirstPath, Path.GetFullPath(Input), TargetDirectory);

                return Succeed("Domain JSON persistence validation passed." + Environment.NewLine +
                               String.Join(Environment.NewLine, HardeningNotes.ToArray()) + Environment.NewLine +
                               "Artifacts: " + TargetDirectory + Environment.NewLine +
                               JsonPackagePersistence.DescribeInspection(FirstInspection), FirstPath);
            });
        }

        private static List<string> ValidateCompositionPersistenceHardening(string SourcePackage, string LegacySourcePackage, string TargetDirectory)
        {
            var Notes = new List<string>();

            var NoBinaryPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-without-composition-bin", ".tcom");
            DeletePackagePart(NoBinaryPath, JsonPackagePersistence.LegacyCompositionBinaryPartUri);
            RequireCompositionJsonLoad(NoBinaryPath, "Composition package without /Composition.bin");
            Notes.Add("Hardening: composition package opened from root JSON with /Composition.bin removed.");

            var NoSidecarsPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-without-sidecars", ".tcom");
            DeletePackagePartsByPrefix(NoSidecarsPath, "/Interchange/");
            DeletePackagePartsByPrefix(NoSidecarsPath, "/Previews/");
            RequireCompositionJsonLoad(NoSidecarsPath, "Composition package without /Interchange sidecars");
            Notes.Add("Hardening: composition package opened from root JSON with /Interchange and /Previews removed.");

            var MissingManifestPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-without-manifest", ".tcom");
            DeletePackagePart(MissingManifestPath, JsonPackagePersistence.ManifestPartUri);
            RequireCompositionJsonLoad(MissingManifestPath, "Composition package without /manifest.json");
            var MissingManifestInspection = JsonPackagePersistence.Inspect(MissingManifestPath);
            if (!MissingManifestInspection.JsonAuthoritative || MissingManifestInspection.HasManifest)
                throw new InvalidOperationException("Composition package without /manifest.json was not inspected as JSON-authoritative with missing manifest.");
            Notes.Add("Hardening: composition package opened from root JSON with /manifest.json removed; inspect reports missing manifest.");

            var CorruptManifestPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-manifest", ".tcom");
            WritePackageTextPart(CorruptManifestPath, JsonPackagePersistence.ManifestPartUri, "{ invalid manifest json");
            var CorruptManifestInspection = JsonPackagePersistence.Inspect(CorruptManifestPath);
            if (String.IsNullOrWhiteSpace(CorruptManifestInspection.ManifestReadWarning))
                throw new InvalidOperationException("Corrupt composition /manifest.json did not produce an inspect warning.");
            RequireCompositionJsonLoad(CorruptManifestPath, "Composition package with corrupt /manifest.json");
            Notes.Add("Hardening: composition package opened from root JSON with corrupt /manifest.json; inspect reports manifestWarning.");

            var AuthorityPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-root-json-authority", ".tcom");
            var AuthorityHasLegacyBinary = TryCopyPackagePart(LegacySourcePackage,
                                                              JsonPackagePersistence.LegacyCompositionBinaryPartUri,
                                                              AuthorityPath,
                                                              JsonPackagePersistence.LegacyCompositionBinaryPartUri);
            var Suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var RootCompositionName = "Root JSON Authority Composition " + Suffix;
            var RootDomainName = "Root JSON Authority Domain " + Suffix;
            MutateCompositionAuthorityMarkers(AuthorityPath,
                                              RootCompositionName,
                                              RootDomainName,
                                              "Interchange Sidecar Composition " + Suffix,
                                              "Interchange Sidecar Domain " + Suffix);
            var AuthorityLoad = RequireCompositionJsonLoad(AuthorityPath, "Composition package with stale binary and stale /Interchange sidecars");
            if (!String.Equals(AuthorityLoad.TargetComposition.Name, RootCompositionName, StringComparison.Ordinal))
                throw new InvalidOperationException("Composition root /Composition.json did not win over stale /Composition.bin or /Interchange/Composition.json.");
            if (AuthorityLoad.TargetComposition.CompositeContentDomain == null ||
                !String.Equals(AuthorityLoad.TargetComposition.CompositeContentDomain.Name, RootDomainName, StringComparison.Ordinal))
                throw new InvalidOperationException("Composition root /Domain.json did not win over /Interchange/Domain.json.");
            Notes.Add("Hardening: root /Composition.json and /Domain.json won over stale /Interchange sidecars" +
                      (AuthorityHasLegacyBinary ? " and an exact legacy /Composition.bin fallback." : "."));

            var CorruptWithFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-json-with-fallback", ".tcom");
            if (TryCopyPackagePart(LegacySourcePackage,
                                   JsonPackagePersistence.LegacyCompositionBinaryPartUri,
                                   CorruptWithFallbackPath,
                                   JsonPackagePersistence.LegacyCompositionBinaryPartUri))
            {
                WritePackageTextPart(CorruptWithFallbackPath, JsonPackagePersistence.CompositionJsonPartUri, "{ invalid composition json");
                RequireCompositionFallbackLoad(CorruptWithFallbackPath, "Corrupt composition JSON with /Composition.bin fallback");
                Notes.Add("Hardening: corrupt /Composition.json with an exact legacy /Composition.bin fallback recovered with the fallback diagnostic.");
            }
            else
            {
                File.Delete(CorruptWithFallbackPath);
                Notes.Add("Hardening: corrupt-JSON legacy fallback scenario not applicable because the input had no /Composition.bin.");
            }

            var CorruptNoFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-json-no-fallback", ".tcom");
            WritePackageTextPart(CorruptNoFallbackPath, JsonPackagePersistence.CompositionJsonPartUri, "{ invalid composition json");
            DeletePackagePart(CorruptNoFallbackPath, JsonPackagePersistence.LegacyCompositionBinaryPartUri);
            RequireCompositionLoadFailure(CorruptNoFallbackPath, "Corrupt composition JSON without /Composition.bin fallback");
            Notes.Add("Hardening: corrupt /Composition.json without /Composition.bin fallback failed cleanly.");

            var MissingRootNoFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-missing-json-no-fallback", ".tcom");
            DeletePackagePart(MissingRootNoFallbackPath, JsonPackagePersistence.CompositionJsonPartUri);
            DeletePackagePart(MissingRootNoFallbackPath, JsonPackagePersistence.LegacyCompositionBinaryPartUri);
            RequireCompositionLoadFailure(MissingRootNoFallbackPath, "Missing composition JSON without /Composition.bin fallback");
            Notes.Add("Hardening: missing /Composition.json without /Composition.bin failed cleanly without deserializing an unrelated part.");

            ValidateTransactionalPackageShell(SourcePackage, TargetDirectory, ".tcom", Notes);

            return Notes;
        }

        private static List<string> ValidateDomainPersistenceHardening(string SourcePackage, string LegacySourcePackage, string TargetDirectory)
        {
            var Notes = new List<string>();

            var NoBinaryPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-without-domain-bin", ".tdom");
            DeletePackagePart(NoBinaryPath, JsonPackagePersistence.LegacyDomainBinaryPartUri);
            RequireDomainJsonLoad(NoBinaryPath, "Domain package without /Domain.bin");
            Notes.Add("Hardening: domain package opened from root JSON with /Domain.bin removed.");

            var NoSidecarsPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-without-sidecars", ".tdom");
            DeletePackagePartsByPrefix(NoSidecarsPath, "/Interchange/");
            DeletePackagePartsByPrefix(NoSidecarsPath, "/Previews/");
            RequireDomainJsonLoad(NoSidecarsPath, "Domain package without /Interchange sidecars");
            Notes.Add("Hardening: domain package opened from root JSON with /Interchange and /Previews removed.");

            var MissingManifestPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-without-manifest", ".tdom");
            DeletePackagePart(MissingManifestPath, JsonPackagePersistence.ManifestPartUri);
            RequireDomainJsonLoad(MissingManifestPath, "Domain package without /manifest.json");
            var MissingManifestInspection = JsonPackagePersistence.Inspect(MissingManifestPath);
            if (!MissingManifestInspection.JsonAuthoritative || MissingManifestInspection.HasManifest)
                throw new InvalidOperationException("Domain package without /manifest.json was not inspected as JSON-authoritative with missing manifest.");
            Notes.Add("Hardening: domain package opened from root JSON with /manifest.json removed; inspect reports missing manifest.");

            var CorruptManifestPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-manifest", ".tdom");
            WritePackageTextPart(CorruptManifestPath, JsonPackagePersistence.ManifestPartUri, "{ invalid manifest json");
            var CorruptManifestInspection = JsonPackagePersistence.Inspect(CorruptManifestPath);
            if (String.IsNullOrWhiteSpace(CorruptManifestInspection.ManifestReadWarning))
                throw new InvalidOperationException("Corrupt domain /manifest.json did not produce an inspect warning.");
            RequireDomainJsonLoad(CorruptManifestPath, "Domain package with corrupt /manifest.json");
            Notes.Add("Hardening: domain package opened from root JSON with corrupt /manifest.json; inspect reports manifestWarning.");

            var AuthorityPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-root-json-authority", ".tdom");
            var AuthorityHasLegacyBinary = TryCopyPackagePart(LegacySourcePackage,
                                                              JsonPackagePersistence.LegacyDomainBinaryPartUri,
                                                              AuthorityPath,
                                                              JsonPackagePersistence.LegacyDomainBinaryPartUri);
            var Suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var RootDomainName = "Root JSON Authority Domain " + Suffix;
            MutateDomainAuthorityMarkers(AuthorityPath,
                                         RootDomainName,
                                         "Interchange Sidecar Domain " + Suffix);
            var AuthorityLoad = RequireDomainJsonLoad(AuthorityPath, "Domain package with stale binary and stale /Interchange sidecars");
            if (!String.Equals(AuthorityLoad.Name, RootDomainName, StringComparison.Ordinal))
                throw new InvalidOperationException("Domain root /Domain.json did not win over stale /Domain.bin or /Interchange/Domain.json.");
            Notes.Add("Hardening: root /Domain.json won over stale /Interchange sidecars" +
                      (AuthorityHasLegacyBinary ? " and an exact legacy /Domain.bin fallback." : "."));

            var CorruptWithFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-json-with-fallback", ".tdom");
            if (TryCopyPackagePart(LegacySourcePackage,
                                   JsonPackagePersistence.LegacyDomainBinaryPartUri,
                                   CorruptWithFallbackPath,
                                   JsonPackagePersistence.LegacyDomainBinaryPartUri))
            {
                WritePackageTextPart(CorruptWithFallbackPath, JsonPackagePersistence.DomainJsonPartUri, "{ invalid domain json");
                RequireDomainFallbackLoad(CorruptWithFallbackPath, "Corrupt domain JSON with /Domain.bin fallback");
                Notes.Add("Hardening: corrupt /Domain.json with an exact legacy /Domain.bin fallback recovered with the fallback diagnostic.");
            }
            else
            {
                File.Delete(CorruptWithFallbackPath);
                Notes.Add("Hardening: corrupt-JSON legacy fallback scenario not applicable because the input had no /Domain.bin.");
            }

            var CorruptNoFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-json-no-fallback", ".tdom");
            WritePackageTextPart(CorruptNoFallbackPath, JsonPackagePersistence.DomainJsonPartUri, "{ invalid domain json");
            DeletePackagePart(CorruptNoFallbackPath, JsonPackagePersistence.LegacyDomainBinaryPartUri);
            RequireDomainLoadFailure(CorruptNoFallbackPath, "Corrupt domain JSON without /Domain.bin fallback");
            Notes.Add("Hardening: corrupt /Domain.json without /Domain.bin fallback failed cleanly.");

            var MissingRootNoFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-missing-json-no-fallback", ".tdom");
            DeletePackagePart(MissingRootNoFallbackPath, JsonPackagePersistence.DomainJsonPartUri);
            DeletePackagePart(MissingRootNoFallbackPath, JsonPackagePersistence.LegacyDomainBinaryPartUri);
            RequireDomainLoadFailure(MissingRootNoFallbackPath, "Missing domain JSON without /Domain.bin fallback");
            Notes.Add("Hardening: missing /Domain.json without /Domain.bin failed cleanly without deserializing an unrelated part.");

            ValidateTransactionalPackageShell(SourcePackage, TargetDirectory, ".tdom", Notes);

            return Notes;
        }

        private static void ValidateTransactionalPackageShell(string SourcePackage, string TargetDirectory,
                                                              string Extension, IList<string> Notes)
        {
            var Stem = Path.GetFileNameWithoutExtension(SourcePackage);
            var RequiredFailurePath = Path.Combine(TargetDirectory, Stem + "-required-writer-failure" + Extension);
            File.Copy(SourcePackage, RequiredFailurePath, true);
            var OriginalBytes = File.ReadAllBytes(RequiredFailurePath);

            var RequiredFailure = DocumentEngine.StorePackageToLocation<object>(
                new object(), "transactional validation", new Uri(RequiredFailurePath, UriKind.Absolute),
                Pack => { throw new InvalidOperationException("Injected required-part writer failure."); },
                false, true, null, null, true, null);
            if (String.IsNullOrWhiteSpace(RequiredFailure))
                throw new InvalidOperationException("Injected required-part writer failure unexpectedly succeeded.");
            if (!OriginalBytes.SequenceEqual(File.ReadAllBytes(RequiredFailurePath)))
                throw new InvalidOperationException("Required-part writer failure changed the original package bytes.");

            var OptionalFailurePath = Path.Combine(TargetDirectory, Stem + "-optional-writer-failure" + Extension);
            if (File.Exists(OptionalFailurePath))
                File.Delete(OptionalFailurePath);

            var RequiredPartUri = new Uri("/Required.json", UriKind.Relative);
            var ExpectedPayload = new UTF8Encoding(false).GetBytes("{\"required\":true}");
            var OptionalFailure = DocumentEngine.StorePackageToLocation<object>(
                new object(), "transactional validation", new Uri(OptionalFailurePath, UriKind.Absolute),
                Pack =>
                {
                    var Part = Pack.CreatePart(RequiredPartUri, "application/json", CompressionOption.Maximum);
                    using (var Stream = Part.GetStream(FileMode.Create, FileAccess.Write))
                        Stream.Write(ExpectedPayload, 0, ExpectedPayload.Length);
                },
                false, true, null, null, true,
                Pack => { throw new InvalidOperationException("Injected optional-part writer failure."); });
            if (!String.IsNullOrWhiteSpace(OptionalFailure))
                throw new InvalidOperationException("Optional-part writer failure aborted a package save: " + OptionalFailure);

            using (var Pack = Package.Open(OptionalFailurePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!Pack.PartExists(RequiredPartUri))
                    throw new InvalidOperationException("Optional-part writer failure left no required package part.");
                using (var Stream = Pack.GetPart(RequiredPartUri).GetStream(FileMode.Open, FileAccess.Read))
                using (var Buffer = new MemoryStream())
                {
                    Stream.CopyTo(Buffer);
                    if (!ExpectedPayload.SequenceEqual(Buffer.ToArray()))
                        throw new InvalidOperationException("Optional-part writer failure changed the required package payload.");
                }
            }

            Notes.Add("Hardening: required-writer failure preserved the original byte-for-byte; optional-writer failure retained a valid required package payload.");
        }

        private static CompositionEngine RequireCompositionJsonLoad(string PackagePath, string Scenario)
        {
            var Load = LoadComposition(PackagePath);
            if (!Load.WasSuccessful)
                throw new InvalidOperationException(Scenario + " did not load: " + Load.Message);

            if (!CompositionEngine.LastLoadUsedJsonPersistence || CompositionEngine.LastLoadUsedLegacyBinaryFallback)
                throw new InvalidOperationException(Scenario + " did not load from root JSON. " +
                                                    CompositionEngine.LastLoadPersistenceDiagnostic.ToStringAlways());

            return Load.Result;
        }

        private static Domain RequireDomainJsonLoad(string PackagePath, string Scenario)
        {
            var Load = LoadNativeDomain(PackagePath);
            if (!Load.WasSuccessful)
                throw new InvalidOperationException(Scenario + " did not load: " + Load.Message);

            if (!CompositionEngine.LastLoadUsedJsonPersistence || CompositionEngine.LastLoadUsedLegacyBinaryFallback)
                throw new InvalidOperationException(Scenario + " did not load from root JSON. " +
                                                    CompositionEngine.LastLoadPersistenceDiagnostic.ToStringAlways());

            return Load.Result;
        }

        private static void RequireCompositionFallbackLoad(string PackagePath, string Scenario)
        {
            var Load = LoadComposition(PackagePath);
            if (!Load.WasSuccessful)
                throw new InvalidOperationException(Scenario + " did not recover through binary fallback: " + Load.Message);

            if (!CompositionEngine.LastLoadUsedLegacyBinaryFallback)
                throw new InvalidOperationException(Scenario + " loaded without reporting legacy binary fallback.");
        }

        private static void RequireDomainFallbackLoad(string PackagePath, string Scenario)
        {
            var Load = LoadNativeDomain(PackagePath);
            if (!Load.WasSuccessful)
                throw new InvalidOperationException(Scenario + " did not recover through binary fallback: " + Load.Message);

            if (!CompositionEngine.LastLoadUsedLegacyBinaryFallback)
                throw new InvalidOperationException(Scenario + " loaded without reporting legacy binary fallback.");
        }

        private static void RequireCompositionLoadFailure(string PackagePath, string Scenario)
        {
            var Load = LoadComposition(PackagePath);
            if (Load.WasSuccessful)
                throw new InvalidOperationException(Scenario + " unexpectedly loaded.");

            if (Load.Message.IndexOf("Cannot load JSON-authoritative composition package", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(Scenario + " failed without the expected JSON diagnostic: " + Load.Message);
        }

        private static void RequireDomainLoadFailure(string PackagePath, string Scenario)
        {
            var Load = LoadNativeDomain(PackagePath);
            if (Load.WasSuccessful)
                throw new InvalidOperationException(Scenario + " unexpectedly loaded.");

            if (Load.Message.IndexOf("Cannot load JSON-authoritative domain package", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(Scenario + " failed without the expected JSON diagnostic: " + Load.Message);
        }

        private static string CopyPackageVariant(string SourcePackage, string TargetDirectory, string Suffix, string Extension)
        {
            var TargetPath = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(SourcePackage) + Suffix + Extension);
            File.Copy(SourcePackage, TargetPath, true);
            return TargetPath;
        }

        private static bool TryCopyPackagePart(string SourcePackage, Uri SourcePartUri,
                                               string TargetPackage, Uri TargetPartUri)
        {
            if (String.IsNullOrWhiteSpace(SourcePackage) || !File.Exists(SourcePackage))
                return false;

            byte[] Bytes;
            string ContentType;
            CompressionOption Compression;
            using (var Source = Package.Open(SourcePackage, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!Source.PartExists(SourcePartUri))
                    return false;

                var SourcePart = Source.GetPart(SourcePartUri);
                ContentType = SourcePart.ContentType;
                Compression = SourcePart.CompressionOption;
                using (var Stream = SourcePart.GetStream(FileMode.Open, FileAccess.Read))
                using (var Buffer = new MemoryStream())
                {
                    Stream.CopyTo(Buffer);
                    Bytes = Buffer.ToArray();
                }
            }

            using (var Target = Package.Open(TargetPackage, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (Target.PartExists(TargetPartUri))
                    Target.DeletePart(TargetPartUri);
                var Part = Target.CreatePart(TargetPartUri, ContentType, Compression);
                using (var Stream = Part.GetStream(FileMode.Create, FileAccess.Write))
                    Stream.Write(Bytes, 0, Bytes.Length);
            }

            return true;
        }

        private static void MutateCompositionAuthorityMarkers(string PackagePath,
                                                              string RootCompositionName,
                                                              string RootDomainName,
                                                              string SidecarCompositionName,
                                                              string SidecarDomainName)
        {
            var Payload = JsonPackagePersistence.ReadCompositionPackage(PackagePath);
            if (Payload.CompositionDocument == null || Payload.CompositionDocument.Composition == null)
                throw new InvalidOperationException("Cannot mutate composition authority marker without /Composition.json composition metadata.");
            if (Payload.DomainDocument == null || Payload.DomainDocument.Domain == null)
                throw new InvalidOperationException("Cannot mutate composition authority marker without /Domain.json domain metadata.");

            Payload.CompositionDocument.Composition.Name = RootCompositionName;
            Payload.DomainDocument.Domain.Name = RootDomainName;
            WritePackageTextPart(PackagePath,
                                 JsonPackagePersistence.CompositionJsonPartUri,
                                 CompositionJsonSerializer.Serialize(Payload.CompositionDocument));
            WritePackageTextPart(PackagePath,
                                 JsonPackagePersistence.DomainJsonPartUri,
                                 DomainJsonSerializer.Serialize(Payload.DomainDocument));

            if (PackagePartExists(PackagePath, ContainerSnapshotService.CompositionJsonPartUri))
            {
                var SidecarComposition = CompositionJsonSerializer.Deserialize(ReadPackageTextPart(PackagePath, ContainerSnapshotService.CompositionJsonPartUri));
                if (SidecarComposition != null && SidecarComposition.Composition != null)
                {
                    SidecarComposition.Composition.Name = SidecarCompositionName;
                    WritePackageTextPart(PackagePath,
                                         ContainerSnapshotService.CompositionJsonPartUri,
                                         CompositionJsonSerializer.Serialize(SidecarComposition));
                }
            }

            if (PackagePartExists(PackagePath, ContainerSnapshotService.DomainJsonPartUri))
            {
                var SidecarDomain = DomainJsonSerializer.Deserialize(ReadPackageTextPart(PackagePath, ContainerSnapshotService.DomainJsonPartUri));
                if (SidecarDomain != null && SidecarDomain.Domain != null)
                {
                    SidecarDomain.Domain.Name = SidecarDomainName;
                    WritePackageTextPart(PackagePath,
                                         ContainerSnapshotService.DomainJsonPartUri,
                                         DomainJsonSerializer.Serialize(SidecarDomain));
                }
            }
        }

        private static void MutateDomainAuthorityMarkers(string PackagePath,
                                                         string RootDomainName,
                                                         string SidecarDomainName)
        {
            var Payload = JsonPackagePersistence.ReadDomainPackage(PackagePath);
            if (Payload.DomainDocument == null || Payload.DomainDocument.Domain == null)
                throw new InvalidOperationException("Cannot mutate domain authority marker without /Domain.json domain metadata.");

            Payload.DomainDocument.Domain.Name = RootDomainName;
            WritePackageTextPart(PackagePath,
                                 JsonPackagePersistence.DomainJsonPartUri,
                                 DomainJsonSerializer.Serialize(Payload.DomainDocument));

            if (PackagePartExists(PackagePath, ContainerSnapshotService.DomainJsonPartUri))
            {
                var SidecarDomain = DomainJsonSerializer.Deserialize(ReadPackageTextPart(PackagePath, ContainerSnapshotService.DomainJsonPartUri));
                if (SidecarDomain != null && SidecarDomain.Domain != null)
                {
                    SidecarDomain.Domain.Name = SidecarDomainName;
                    WritePackageTextPart(PackagePath,
                                         ContainerSnapshotService.DomainJsonPartUri,
                                         DomainJsonSerializer.Serialize(SidecarDomain));
                }
            }
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

        private static OperationResult<Rect> DetermineImageFitSourceArea(View TargetView, IList<string> FitTechNames, double Padding)
        {
            if (TargetView == null)
                return OperationResult.Failure<Rect>("No target view was supplied.");

            var Representations = GetViewRepresentations(TargetView).ToList();
            var Missing = FitTechNames
                .Where(TechName => !Representations.Any(RepresentationHasTechName(TechName)))
                .ToList();

            if (Missing.Count > 0)
                return OperationResult.Failure<Rect>("Cannot resolve fit TechName(s) on view '" + DescribeView(TargetView) +
                                                     "': " + String.Join(", ", Missing.ToArray()) +
                                                     ". Known visible idea TechNames: " + KnownVisibleIdeaTechNames(TargetView));

            var Result = Rect.Empty;
            var MeasuredParts = 0;

            foreach (var Representator in Representations.Where(RepresentationMatchesAny(FitTechNames)))
                foreach (var Part in Representator.VisualParts.Where(Part => Part != null && Part.IsRelatedVisible))
                {
                    var PartArea = GetVisualObjectContentArea(Part);
                    if (PartArea == Rect.Empty || PartArea.Width <= 0 || PartArea.Height <= 0)
                        continue;

                    if (Result == Rect.Empty)
                        Result = PartArea;
                    else
                        Result.Union(PartArea);

                    MeasuredParts++;
                }

            if (MeasuredParts < 1 || Result == Rect.Empty)
                return OperationResult.Failure<Rect>("Fit TechName(s) resolved on view '" + DescribeView(TargetView) +
                                                     "', but no renderable visual area was found.");

            if (Padding > 0)
                Result.Inflate(Padding, Padding);

            return OperationResult.Success(Result);
        }

        private static Func<VisualRepresentation, bool> RepresentationHasTechName(string TechName)
        {
            return delegate(VisualRepresentation Representation)
            {
                return Representation != null &&
                       Representation.RepresentedIdea != null &&
                       String.Equals(Representation.RepresentedIdea.TechName, TechName, StringComparison.OrdinalIgnoreCase);
            };
        }

        private static Func<VisualRepresentation, bool> RepresentationMatchesAny(IList<string> TechNames)
        {
            return delegate(VisualRepresentation Representation)
            {
                return TechNames.Any(TechName => RepresentationHasTechName(TechName)(Representation));
            };
        }

        private static IEnumerable<VisualRepresentation> GetViewRepresentations(View TargetView)
        {
            if (TargetView == null || TargetView.ViewChildren == null)
                return Enumerable.Empty<VisualRepresentation>();

            return TargetView.ViewChildren
                             .Select(Child => Child == null ? null : Child.Key as VisualElement)
                             .Where(Element => Element != null && Element.OwnerRepresentation != null)
                             .Select(Element => Element.OwnerRepresentation)
                             .Distinct()
                             .Where(Representator => Representator != null && Representator.RepresentedIdea != null);
        }

        private static Rect GetVisualObjectContentArea(VisualObject Source)
        {
            if (Source == null)
                return Rect.Empty;

            var Result = Source.Graphic == null ? Rect.Empty : Source.Graphic.ContentBounds;
            if (Result == Rect.Empty || Result.Width <= 0 || Result.Height <= 0)
                Result = Source.TotalArea;

            return Result;
        }

        private static bool TryResolveImageExportSize(Rect SourceArea, int? RequestedWidth, int? RequestedHeight,
                                                      out int Width, out int Height, out string Message)
        {
            Width = RequestedWidth.HasValue ? RequestedWidth.Value : 0;
            Height = RequestedHeight.HasValue ? RequestedHeight.Value : 0;
            Message = null;

            if (!RequestedWidth.HasValue && !RequestedHeight.HasValue)
            {
                Width = DefaultImageExportWidth;
                Height = DefaultImageExportHeight;
            }
            else
                if (RequestedWidth.HasValue && !RequestedHeight.HasValue)
                    Height = Convert.ToInt32(Math.Round((double)Width * (SourceArea.Height / SourceArea.Width)));
                else
                    if (!RequestedWidth.HasValue && RequestedHeight.HasValue)
                        Width = Convert.ToInt32(Math.Round((double)Height * (SourceArea.Width / SourceArea.Height)));

            if (Width < MinImageExportDimension || Height < MinImageExportDimension)
            {
                Message = "--width and --height must resolve to at least " +
                          MinImageExportDimension.ToString(CultureInfo.InvariantCulture) + " pixels.";
                return false;
            }

            if (Width > MaxImageExportDimension || Height > MaxImageExportDimension)
            {
                Message = "--width and --height must be no greater than " +
                          MaxImageExportDimension.ToString(CultureInfo.InvariantCulture) + " pixels.";
                return false;
            }

            if ((long)Width * (long)Height > MaxImageExportPixels)
            {
                Message = "Image export is limited to " +
                          MaxImageExportPixels.ToString(CultureInfo.InvariantCulture) + " pixels to avoid exhausting memory.";
                return false;
            }

            return true;
        }

        private static IList<string> NormalizeReferences(IEnumerable<string> References)
        {
            return (References ?? Enumerable.Empty<string>())
                   .Where(Reference => !String.IsNullOrWhiteSpace(Reference))
                   .Select(Reference => Reference.Trim())
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList();
        }

        private static View ResolveCompositionView(Composition Composition, string ViewReference)
        {
            if (Composition == null)
                return null;

            var Views = GetCompositionViews(Composition).ToList();
            if (String.IsNullOrWhiteSpace(ViewReference))
                return Composition.RootView ?? Composition.ActiveView ?? Views.FirstOrDefault();

            if (String.Equals(ViewReference, "root", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(ViewReference, "main", StringComparison.OrdinalIgnoreCase))
                return Composition.RootView ?? Views.FirstOrDefault();

            if (String.Equals(ViewReference, "active", StringComparison.OrdinalIgnoreCase))
                return Composition.ActiveView ?? Composition.RootView ?? Views.FirstOrDefault();

            Guid ParsedId;
            if (Guid.TryParse(ViewReference, out ParsedId))
                return Views.FirstOrDefault(View => View.GlobalId == ParsedId);

            return Views.FirstOrDefault(View => String.Equals(View.TechName, ViewReference, StringComparison.OrdinalIgnoreCase)) ??
                   Views.FirstOrDefault(View => String.Equals(View.Name, ViewReference, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<View> GetCompositionViews(Composition Composition)
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

        private static string KnownViewTechNames(Composition Composition)
        {
            var Values = GetCompositionViews(Composition)
                         .Select(View => View.TechName)
                         .Where(TechName => !String.IsNullOrWhiteSpace(TechName))
                         .OrderBy(TechName => TechName)
                         .ToArray();

            return Values.Length == 0 ? "<none>" : String.Join(", ", Values);
        }

        private static string KnownVisibleIdeaTechNames(View TargetView)
        {
            var Values = GetViewRepresentations(TargetView)
                         .Select(Representator => Representator.RepresentedIdea.TechName)
                         .Where(TechName => !String.IsNullOrWhiteSpace(TechName))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(TechName => TechName)
                         .ToArray();

            return Values.Length == 0 ? "<none>" : String.Join(", ", Values);
        }

        private static string IdOf(UniqueElement Element)
        {
            return Element == null ? null : Element.GlobalId.ToString("D");
        }

        private static string DescribeView(View Source)
        {
            if (Source == null)
                return "<none>";

            return Source.Name.ToStringAlways() + " (" + Source.TechName.ToStringAlways() + ", id=" +
                   Source.GlobalId.ToString("D") + ")";
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
                DomainServices.UpdateDomainDependants(TargetDomain, null, false);

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
            var Context = HeadlessBootstrap.Initialize();
            CompositionEngine.CreateActiveCompositionEngine(Context.Compositions, Context.Visualizer, true);
            var Result = CompositionEngine.MaterializeDomain(new Uri(Path.GetFullPath(Input), UriKind.Absolute));

            if (Result == null || Result.Item1 == null)
                return OperationResult.Failure<Domain>("Cannot load domain: " + (Result == null ? "" : Result.Item2));

            return OperationResult.Success(Result.Item1);
        }

        private static OperationResult<CompositionEngine> LoadNativeDomainForEdit(string Input)
        {
            var Context = HeadlessBootstrap.Initialize();
            CompositionEngine.CreateActiveCompositionEngine(Context.Compositions, Context.Visualizer, true);
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

        private static void SaveComposition(Composition SourceComposition, string Output,
                                             GitPackageLink GitSyncLink = null,
                                             GitPackageLink EmbeddedDomainGitSyncLink = null,
                                             string PreviewSource = null)
        {
            EnsureParentDirectory(Output);
            var Location = new Uri(Path.GetFullPath(Output), UriKind.Absolute);

            var Error = JsonPackagePersistence.StoreComposition(SourceComposition, Location,
                                                                false, false,
                                                                 null, true,
                                                                 GitSyncLink,
                                                                 EmbeddedDomainGitSyncLink,
                                                                 String.IsNullOrWhiteSpace(PreviewSource)
                                                                 ? null
                                                                 : new Uri(Path.GetFullPath(PreviewSource), UriKind.Absolute));

            if (!String.IsNullOrEmpty(Error))
                throw new InvalidOperationException(Error);
        }

        private static GitPackageLink ReadDomainGitSyncLink(string DomainPackagePath)
        {
            return ReadPackageGitSyncLink(DomainPackagePath, GitPackageLink.KindDomain);
        }

        private static GitPackageLink ReadPackageGitSyncLink(string PackagePath, string PackageKind)
        {
            if (String.IsNullOrWhiteSpace(PackagePath) || !File.Exists(PackagePath))
                return null;

            try
            {
                var Link = JsonPackagePersistence.ReadGitSyncLink(PackagePath);
                return Link != null &&
                       Link.FindBaseline(PackageKind, GitPackageLink.RoleSelf) != null
                       ? Link
                       : null;
            }
            catch
            {
                return null;
            }
        }

        private static GitPackageLink ReadEmbeddedDomainGitSyncLink(string CompositionPackagePath)
        {
            if (String.IsNullOrWhiteSpace(CompositionPackagePath) || !File.Exists(CompositionPackagePath))
                return null;

            try
            {
                var Link = JsonPackagePersistence.ReadEmbeddedDomainGitSyncLink(CompositionPackagePath);
                return Link != null &&
                       Link.FindBaseline(GitPackageLink.KindDomain, GitPackageLink.RoleSelf) != null
                       ? Link
                       : null;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveDomain(Domain SourceDomain, string Output, bool IncludeTemplateComposition = false,
                                       string PreviewSource = null)
        {
            EnsureParentDirectory(Output);
            SourceDomain.SetTemplateSaving(IncludeTemplateComposition);

            var Error = JsonPackagePersistence.StoreDomain(SourceDomain,
                                                           new Uri(Path.GetFullPath(Output), UriKind.Absolute),
                                                            false, false,
                                                            null, true,
                                                            IncludeTemplateComposition,
                                                            null,
                                                            String.IsNullOrWhiteSpace(PreviewSource)
                                                            ? null
                                                            : new Uri(Path.GetFullPath(PreviewSource), UriKind.Absolute));

            if (!String.IsNullOrEmpty(Error))
                throw new InvalidOperationException(Error);
        }

        private static bool PackagePartExists(string PackagePath, Uri PartUri)
        {
            using (var Pack = Package.Open(PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                return Pack.PartExists(PartUri);
        }

        private static string ReadPackageTextPart(string PackagePath, Uri PartUri)
        {
            using (var Pack = Package.Open(PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var Stream = Pack.GetPart(PartUri).GetStream(FileMode.Open, FileAccess.Read))
            using (var Reader = new StreamReader(Stream, Encoding.UTF8, true))
                return Reader.ReadToEnd();
        }

        private static Tuple<string, string> ReadPreviewCacheRecord(string PackagePath, string ViewId)
        {
            var Serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            var Root = Serializer.DeserializeObject(ReadPackageTextPart(PackagePath, ContainerSnapshotService.ManifestPartUri))
                                 as IDictionary<string, object>;
            if (Root == null)
                throw new InvalidDataException("Preview manifest is not a JSON object: " + PackagePath);

            object VersionValue;
            int Version;
            if (!Root.TryGetValue("formatVersion", out VersionValue) ||
                !Int32.TryParse(Convert.ToString(VersionValue, CultureInfo.InvariantCulture),
                                NumberStyles.Integer, CultureInfo.InvariantCulture, out Version) || Version != 2)
                throw new InvalidDataException("Preview manifest is not format version 2: " + PackagePath);

            object PreviewsValue;
            var Previews = Root.TryGetValue("previews", out PreviewsValue)
                         ? PreviewsValue as System.Collections.IEnumerable
                         : null;
            if (Previews != null)
                foreach (var Item in Previews)
                {
                    var Preview = Item as IDictionary<string, object>;
                    object ItemViewId;
                    if (Preview == null || !Preview.TryGetValue("viewId", out ItemViewId) ||
                        !String.Equals(Convert.ToString(ItemViewId, CultureInfo.InvariantCulture), ViewId,
                                       StringComparison.OrdinalIgnoreCase))
                        continue;

                    object InputHash;
                    object Disposition;
                    Preview.TryGetValue("inputSha256", out InputHash);
                    Preview.TryGetValue("disposition", out Disposition);
                    var Hash = Convert.ToString(InputHash, CultureInfo.InvariantCulture);
                    if (String.IsNullOrWhiteSpace(Hash))
                        throw new InvalidDataException("Preview manifest entry has no inputSha256 for View '" + ViewId + "'.");

                    return Tuple.Create(Hash, Convert.ToString(Disposition, CultureInfo.InvariantCulture));
                }

            throw new InvalidDataException("Preview manifest has no entry for View '" + ViewId + "': " + PackagePath);
        }

        private static void WritePackageTextPart(string PackagePath, Uri PartUri, string Text)
        {
            using (var Pack = Package.Open(PackagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (Pack.PartExists(PartUri))
                    Pack.DeletePart(PartUri);

                var Part = Pack.CreatePart(PartUri, "application/json", CompressionOption.Maximum);
                var Bytes = new UTF8Encoding(false).GetBytes(Text ?? "");
                using (var Stream = Part.GetStream(FileMode.Create, FileAccess.Write))
                    Stream.Write(Bytes, 0, Bytes.Length);
            }
        }

        private static void DeletePackagePart(string PackagePath, Uri PartUri)
        {
            using (var Pack = Package.Open(PackagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                if (Pack.PartExists(PartUri))
                    Pack.DeletePart(PartUri);
        }

        private static void DeletePackagePartsByPrefix(string PackagePath, string Prefix)
        {
            using (var Pack = Package.Open(PackagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var Parts = Pack.GetParts()
                                .Where(Part => Part.Uri.ToString().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                                .Select(Part => Part.Uri)
                                .ToList();

                foreach (var PartUri in Parts)
                    Pack.DeletePart(PartUri);
            }
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

            if (!String.IsNullOrWhiteSpace(Extension) && !HasExtension(Input, Extension))
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

        private static bool IsImageOutputPath(string PathText)
        {
            return HasExtension(PathText, "png") ||
                   HasExtension(PathText, "jpg") ||
                   HasExtension(PathText, "jpeg") ||
                   HasExtension(PathText, "gif") ||
                   HasExtension(PathText, "tif") ||
                   HasExtension(PathText, "tiff") ||
                   HasExtension(PathText, "bmp");
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
