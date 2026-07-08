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
using System.Text;
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
                SaveComposition(LoadResult.Result.TargetComposition, Output);

                return Succeed("Embedded Domain updated from: " + Path.GetFullPath(DomainInput) + Environment.NewLine +
                               "Output: " + Path.GetFullPath(Output) + Environment.NewLine +
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

                SaveComposition(LoadResult.Result.TargetComposition, Output);
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
                SaveDomain(LoadResult.Result, Output, IncludeTemplate);
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

                var FirstLoad = LoadComposition(Input);
                if (!FirstLoad.WasSuccessful)
                    return Fail(FirstLoad.Message);

                SaveComposition(FirstLoad.Result.TargetComposition, FirstPath);

                var FirstInspection = JsonPackagePersistence.Inspect(FirstPath);
                if (!FirstInspection.JsonAuthoritative)
                    return Fail("First saved composition package is not JSON-authoritative." + Environment.NewLine +
                                JsonPackagePersistence.DescribeInspection(FirstInspection));

                var SecondLoad = LoadComposition(FirstPath);
                if (!SecondLoad.WasSuccessful)
                    return Fail(SecondLoad.Message);

                if (!CompositionEngine.LastLoadUsedJsonPersistence || CompositionEngine.LastLoadUsedLegacyBinaryFallback)
                    return Fail("Modern composition package did not reopen from JSON persistence." + Environment.NewLine +
                                CompositionEngine.LastLoadPersistenceDiagnostic.ToStringAlways());

                SaveComposition(SecondLoad.Result.TargetComposition, SecondPath);

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

                var HardeningNotes = ValidateCompositionPersistenceHardening(FirstPath, TargetDirectory);

                return Succeed("Composition JSON persistence validation passed." + Environment.NewLine +
                               String.Join(Environment.NewLine, HardeningNotes.ToArray()) + Environment.NewLine +
                               "Artifacts: " + TargetDirectory + Environment.NewLine +
                               JsonPackagePersistence.DescribeInspection(FirstInspection), FirstPath);
            });
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

                SaveDomain(FirstLoad.Result, FirstPath, FirstLoad.Result.OwnerComposition != null);

                var FirstInspection = JsonPackagePersistence.Inspect(FirstPath);
                if (!FirstInspection.JsonAuthoritative)
                    return Fail("First saved domain package is not JSON-authoritative." + Environment.NewLine +
                                JsonPackagePersistence.DescribeInspection(FirstInspection));

                var SecondLoad = LoadNativeDomain(FirstPath);
                if (!SecondLoad.WasSuccessful)
                    return Fail(SecondLoad.Message);

                if (!CompositionEngine.LastLoadUsedJsonPersistence || CompositionEngine.LastLoadUsedLegacyBinaryFallback)
                    return Fail("Modern domain package did not reopen from JSON persistence." + Environment.NewLine +
                                CompositionEngine.LastLoadPersistenceDiagnostic.ToStringAlways());

                SaveDomain(SecondLoad.Result, SecondPath, SecondLoad.Result.OwnerComposition != null);

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

                var HardeningNotes = ValidateDomainPersistenceHardening(FirstPath, TargetDirectory);

                return Succeed("Domain JSON persistence validation passed." + Environment.NewLine +
                               String.Join(Environment.NewLine, HardeningNotes.ToArray()) + Environment.NewLine +
                               "Artifacts: " + TargetDirectory + Environment.NewLine +
                               JsonPackagePersistence.DescribeInspection(FirstInspection), FirstPath);
            });
        }

        private static List<string> ValidateCompositionPersistenceHardening(string SourcePackage, string TargetDirectory)
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
            Notes.Add("Hardening: root /Composition.json and /Domain.json won over stale binary fallback and stale /Interchange sidecars.");

            var CorruptWithFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-json-with-fallback", ".tcom");
            WritePackageTextPart(CorruptWithFallbackPath, JsonPackagePersistence.CompositionJsonPartUri, "{ invalid composition json");
            RequireCompositionFallbackLoad(CorruptWithFallbackPath, "Corrupt composition JSON with /Composition.bin fallback");
            Notes.Add("Hardening: corrupt /Composition.json with /Composition.bin fallback recovered with legacy fallback diagnostic.");

            var CorruptNoFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-json-no-fallback", ".tcom");
            WritePackageTextPart(CorruptNoFallbackPath, JsonPackagePersistence.CompositionJsonPartUri, "{ invalid composition json");
            DeletePackagePart(CorruptNoFallbackPath, JsonPackagePersistence.LegacyCompositionBinaryPartUri);
            RequireCompositionLoadFailure(CorruptNoFallbackPath, "Corrupt composition JSON without /Composition.bin fallback");
            Notes.Add("Hardening: corrupt /Composition.json without /Composition.bin fallback failed cleanly.");

            return Notes;
        }

        private static List<string> ValidateDomainPersistenceHardening(string SourcePackage, string TargetDirectory)
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
            var Suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var RootDomainName = "Root JSON Authority Domain " + Suffix;
            MutateDomainAuthorityMarkers(AuthorityPath,
                                         RootDomainName,
                                         "Interchange Sidecar Domain " + Suffix);
            var AuthorityLoad = RequireDomainJsonLoad(AuthorityPath, "Domain package with stale binary and stale /Interchange sidecars");
            if (!String.Equals(AuthorityLoad.Name, RootDomainName, StringComparison.Ordinal))
                throw new InvalidOperationException("Domain root /Domain.json did not win over stale /Domain.bin or /Interchange/Domain.json.");
            Notes.Add("Hardening: root /Domain.json won over stale binary fallback and stale /Interchange sidecars.");

            var CorruptWithFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-json-with-fallback", ".tdom");
            WritePackageTextPart(CorruptWithFallbackPath, JsonPackagePersistence.DomainJsonPartUri, "{ invalid domain json");
            RequireDomainFallbackLoad(CorruptWithFallbackPath, "Corrupt domain JSON with /Domain.bin fallback");
            Notes.Add("Hardening: corrupt /Domain.json with /Domain.bin fallback recovered with legacy fallback diagnostic.");

            var CorruptNoFallbackPath = CopyPackageVariant(SourcePackage, TargetDirectory, "-corrupt-json-no-fallback", ".tdom");
            WritePackageTextPart(CorruptNoFallbackPath, JsonPackagePersistence.DomainJsonPartUri, "{ invalid domain json");
            DeletePackagePart(CorruptNoFallbackPath, JsonPackagePersistence.LegacyDomainBinaryPartUri);
            RequireDomainLoadFailure(CorruptNoFallbackPath, "Corrupt domain JSON without /Domain.bin fallback");
            Notes.Add("Hardening: corrupt /Domain.json without /Domain.bin fallback failed cleanly.");

            return Notes;
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

        private static void SaveComposition(Composition SourceComposition, string Output)
        {
            EnsureParentDirectory(Output);
            var Location = new Uri(Path.GetFullPath(Output), UriKind.Absolute);

            var Error = JsonPackagePersistence.StoreComposition(SourceComposition, Location,
                                                                false, false,
                                                                null, true);

            if (!String.IsNullOrEmpty(Error))
                throw new InvalidOperationException(Error);
        }

        private static void SaveDomain(Domain SourceDomain, string Output, bool IncludeTemplateComposition = false)
        {
            EnsureParentDirectory(Output);
            SourceDomain.SetTemplateSaving(IncludeTemplateComposition);

            var Error = JsonPackagePersistence.StoreDomain(SourceDomain,
                                                           new Uri(Path.GetFullPath(Output), UriKind.Absolute),
                                                           false, false,
                                                           null, true,
                                                           IncludeTemplateComposition);

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
