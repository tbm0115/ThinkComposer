// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer CLI
//
// Reproducible, fresh-process performance harness for JSON-authoritative native persistence.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

using Instrumind.Common;
using Instrumind.ThinkComposer.ApplicationShell;
using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.Composer.ContainerSnapshots;
using Instrumind.ThinkComposer.Definitor;
using Instrumind.ThinkComposer.Headless;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.Model;

namespace Instrumind.ThinkComposer.Cli
{
    internal static partial class PersistencePerformance
    {
        private const string CorpusFormat = "ThinkComposer.JsonPersistenceCorpus";
        private const string ReportFormat = "ThinkComposer.JsonPersistenceBenchmark";
        private const string SampleFormat = "ThinkComposer.JsonPersistenceSample";
        private const int FormatVersion = 2;
        private const string CorpusModeDevelopment = "development";
        private const string CorpusModeCertification = "certification";
        private const string WorkerModeCorpus = "corpus";
        private const string WorkerModeBaseline = "baseline";
        private const string WorkerModeCandidate = "candidate";

        private static readonly JavaScriptSerializer Json = CreateJsonSerializer();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static OperationResult<string> PrepareCorpus(string SourceRoot, string OutputDirectory,
                                                            IEnumerable<string> RealPackages, string Mode)
        {
            try
            {
                SourceRoot = RequireDirectory(SourceRoot, "source root");
                OutputDirectory = Path.GetFullPath(RequireText(OutputDirectory, "output directory"));
                Mode = NormalizeCorpusMode(Mode);
                Directory.CreateDirectory(OutputDirectory);

                var CasesDirectory = Path.Combine(OutputDirectory, "cases");
                Directory.CreateDirectory(CasesDirectory);

                var RealPackagePaths = (RealPackages ?? Enumerable.Empty<string>())
                    .Where(PathHasText)
                    .Select(Path.GetFullPath)
                    .ToList();
                if (Mode == CorpusModeCertification && RealPackagePaths.Count == 0)
                    throw new ArgumentException("Certification corpus preparation requires at least one --real-package sanitized slow package.");

                var SanitizedSlowPackages = new HashSet<string>(RealPackagePaths, StringComparer.OrdinalIgnoreCase);
                var Sources = new List<string>();
                Sources.AddRange(Directory.GetFiles(Path.Combine(SourceRoot, "docs", "Examples"), "*.tcom")
                                          .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase));
                Sources.Add(Path.Combine(SourceRoot, "PredefinedContent", "All-Purpose.tdom"));
                Sources.Add(Path.Combine(SourceRoot, "PredefinedContent", "Genealogy_Tree.tdom"));
                Sources.AddRange(RealPackagePaths);

                var Manifest = new CorpusManifest();
                Manifest.Format = CorpusFormat;
                Manifest.FormatVersion = FormatVersion;
                Manifest.GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                Manifest.SourceRoot = SourceRoot;
                Manifest.Mode = Mode;

                var UsedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var Source in Sources)
                {
                    if (!File.Exists(Source))
                        throw new FileNotFoundException("Corpus source package not found.", Source);

                    var Kind = PackageKindFromPath(Source);
                    var Id = UniqueCaseId(SafeName(Path.GetFileNameWithoutExtension(Source)), UsedIds);
                    var Target = Path.Combine(CasesDirectory, Id + Path.GetExtension(Source).ToLowerInvariant());
                    ConvertToJsonPersistence(Source, Target, Kind);
                    ForceV1PreviewManifest(Target, Kind);
                    Manifest.Cases.Add(DescribeCase(OutputDirectory, Id, Kind, Target, false,
                                                    SanitizedSlowPackages.Contains(Path.GetFullPath(Source))));
                }

                var BaseComposition = Manifest.Cases.FirstOrDefault(Item => Item.Kind == "composition");
                var BaseDomain = Manifest.Cases.FirstOrDefault(Item => Item.Kind == "domain" &&
                                                                       String.Equals(Item.Id, "All-Purpose", StringComparison.OrdinalIgnoreCase));
                if (BaseDomain == null)
                    BaseDomain = Manifest.Cases.FirstOrDefault(Item => Item.Kind == "domain");
                if (BaseComposition == null || BaseDomain == null)
                    throw new InvalidOperationException("Corpus preparation requires at least one Composition and one Domain source.");

                var SyntheticCompositionId = UniqueCaseId("synthetic-large-composition", UsedIds);
                var SyntheticComposition = Path.Combine(CasesDirectory, SyntheticCompositionId + ".tcom");
                CreateSyntheticComposition(Path.Combine(OutputDirectory, BaseComposition.RelativePath),
                                           Path.Combine(OutputDirectory, BaseDomain.RelativePath),
                                           SyntheticComposition);
                ForceV1PreviewManifest(SyntheticComposition, "composition");
                Manifest.Cases.Add(DescribeCase(OutputDirectory, SyntheticCompositionId, "composition",
                                                SyntheticComposition, true, false));

                var SyntheticDomainId = UniqueCaseId("synthetic-large-domain", UsedIds);
                var SyntheticDomain = Path.Combine(CasesDirectory, SyntheticDomainId + ".tdom");
                CreateSyntheticDomain(Path.Combine(OutputDirectory, BaseDomain.RelativePath), SyntheticDomain);
                ForceV1PreviewManifest(SyntheticDomain, "domain");
                Manifest.Cases.Add(DescribeCase(OutputDirectory, SyntheticDomainId, "domain", SyntheticDomain, true, false));

                Manifest.Cases = Manifest.Cases.OrderBy(Item => Item.Id, StringComparer.Ordinal).ToList();
                foreach (var Case in Manifest.Cases)
                    ValidatePreparedCase(OutputDirectory, Case);

                Manifest.Fingerprint = ComputeCorpusFingerprint(Manifest.Cases, Manifest.Mode);
                var CorpusPath = Path.Combine(OutputDirectory, "corpus.json");
                WriteJson(CorpusPath, Manifest);

                return OperationResult.Success(CorpusPath,
                    "JSON persistence corpus prepared and validated: " + CorpusPath + Environment.NewLine +
                    "Cases: " + Manifest.Cases.Count.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Fingerprint: " + Manifest.Fingerprint);
            }
            catch (Exception Problem)
            {
                return OperationResult.Failure<string>("Cannot prepare JSON persistence corpus: " + Problem.Message);
            }
        }

        public static OperationResult<string> Benchmark(string CorpusPath, string OutputPath, int Warmup,
                                                        int Iterations, string BaselinePath, double MinimumSpeedup,
                                                        bool RequireResponsiveSplash,
                                                        bool AllowLegacyBaselineOutput)
        {
            try
            {
                CorpusPath = RequireFile(CorpusPath, "corpus");
                OutputPath = Path.GetFullPath(RequireText(OutputPath, "output"));
                if (Warmup < 0)
                    throw new ArgumentException("Warmup count cannot be negative.");
                if (Iterations < 1)
                    throw new ArgumentException("Iterations must be at least one.");
                if (MinimumSpeedup <= 0)
                    throw new ArgumentException("Minimum speedup must be greater than zero.");
                if (AllowLegacyBaselineOutput && !String.IsNullOrWhiteSpace(BaselinePath))
                    throw new ArgumentException("--allow-legacy-baseline-output is only for recording a baseline and cannot be combined with --baseline. Candidate comparison runs must validate JSON-only output.");

                var Corpus = ReadJson<CorpusManifest>(CorpusPath);
                ValidateCorpus(Corpus, CorpusPath);

                var Report = new BenchmarkReport();
                Report.Format = ReportFormat;
                Report.FormatVersion = FormatVersion;
                Report.GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                Report.ExecutablePath = EntryExecutable();
                Report.ExecutableSha256 = HashFile(Report.ExecutablePath);
                Report.Machine = MachineFingerprint.Create();
                Report.CorpusPath = CorpusPath;
                Report.CorpusFingerprint = Corpus.Fingerprint;
                Report.CorpusMode = Corpus.Mode;
                Report.Warmup = Warmup;
                Report.Iterations = Iterations;
                Report.MinimumSpeedup = MinimumSpeedup;
                Report.RequireResponsiveSplash = RequireResponsiveSplash;
                Report.AllowLegacyBaselineOutput = AllowLegacyBaselineOutput;

                var RunRoot = Path.Combine(Path.GetDirectoryName(OutputPath),
                                           Path.GetFileNameWithoutExtension(OutputPath) + "-work");
                Directory.CreateDirectory(RunRoot);
                var WorkerMode = AllowLegacyBaselineOutput ? WorkerModeBaseline : WorkerModeCandidate;

                foreach (var Case in Corpus.Cases)
                {
                    var CaseReport = new BenchmarkCaseResult();
                    CaseReport.Id = Case.Id;
                    CaseReport.Kind = Case.Kind;
                    CaseReport.AuthoritativeHash = Case.AuthoritativeHash;
                    CaseReport.PackageSha256 = Case.PackageSha256;
                    CaseReport.Bytes = Case.Bytes;
                    CaseReport.EntityCount = Case.EntityCount;
                    CaseReport.ViewCount = Case.ViewCount;
                    CaseReport.VisualCount = Case.VisualCount;
                    CaseReport.IsSanitizedSlowPackage = Case.IsSanitizedSlowPackage;
                    Report.Cases.Add(CaseReport);

                    var Input = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(CorpusPath), Case.RelativePath));
                    for (var Index = 0; Index < Warmup + Iterations; Index++)
                    {
                        var IsWarmup = Index < Warmup;
                        var Prefix = SafeName(Case.Id) + "-" + (IsWarmup ? "warmup" : "sample") + "-" +
                                     Index.ToString(CultureInfo.InvariantCulture);
                        var WorkingFile = Path.Combine(RunRoot, Prefix + Path.GetExtension(Input));
                        var ResultFile = Path.Combine(RunRoot, Prefix + ".json");

                        RunWorker(Input, WorkingFile, ResultFile, WorkerMode);
                        var Sample = ReadJson<BenchmarkSample>(ResultFile);
                        if (!String.Equals(Sample.Format, SampleFormat, StringComparison.Ordinal) ||
                            Sample.FormatVersion != FormatVersion || !Sample.Valid)
                            throw new InvalidDataException("Invalid benchmark sample for case '" + Case.Id + "': " + Sample.Error);
                        if (!String.Equals(Sample.AuthoritativeHash, Case.AuthoritativeHash, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Benchmark sample authoritative JSON changed for case '" +
                                Case.Id + "'. Expected " + Case.AuthoritativeHash + ", received " +
                                Sample.AuthoritativeHash + ".");

                        if (!IsWarmup)
                        {
                            CaseReport.LoadMilliseconds.Add(Sample.LoadMilliseconds);
                            CaseReport.SaveMilliseconds.Add(Sample.SaveMilliseconds);
                            CaseReport.SteadySaveMilliseconds.Add(Sample.SteadySaveMilliseconds);
                            CaseReport.Samples.Add(Sample);
                            CaseReport.LastWorkingPackage = WorkingFile;
                        }
                    }

                    Summarize(CaseReport);
                    ValidateMeasuredOutputs(CaseReport, Report.AllowLegacyBaselineOutput);
                    CaseReport.SplashResponsivenessPassed = CaseReport.Samples.Count == Iterations &&
                        CaseReport.Samples.All(SplashResponsivenessPassed);
                }

                Report.Aggregate = Aggregate(Report.Cases, Iterations);
                Report.PersistenceValidationPassed = Report.Cases.All(Item => Item.ValidationPassed);
                var SplashCertificationCases = Report.CorpusMode == CorpusModeCertification
                    ? Report.Cases.Where(Item => Item.IsSanitizedSlowPackage).ToList()
                    : Report.Cases;
                Report.SplashCertificationCaseCount = SplashCertificationCases.Count;
                Report.SplashResponsivenessPassed = SplashCertificationCases.Count > 0 &&
                                                    SplashCertificationCases.All(Item => Item.SplashResponsivenessPassed);
                Report.ValidationPassed = Report.PersistenceValidationPassed &&
                                          (!Report.RequireResponsiveSplash || Report.SplashResponsivenessPassed);

                if (!String.IsNullOrWhiteSpace(BaselinePath))
                {
                    var Baseline = ReadJson<BenchmarkReport>(RequireFile(BaselinePath, "baseline"));
                    CompareWithBaseline(Report, Baseline, Path.GetFullPath(BaselinePath));
                }

                EnsureParent(OutputPath);
                WriteJson(OutputPath, Report);

                if (!Report.PersistenceValidationPassed)
                    return OperationResult.Failure<string>("Benchmark completed but persistence validation failed. Report: " + OutputPath);

                if (Report.RequireResponsiveSplash && !Report.SplashResponsivenessPassed)
                    return OperationResult.Failure<string>("Benchmark completed but a loading splash failed the 250 ms first-paint, " +
                        "500 ms heartbeat, or clean dispatcher-stop requirement. Report: " + OutputPath);

                if (Report.BaselineComparison != null && !Report.BaselineComparison.Passed)
                    return OperationResult.Failure<string>("Benchmark did not meet the " +
                        MinimumSpeedup.ToString("0.###", CultureInfo.InvariantCulture) + "x median load/save gate. Report: " + OutputPath);

                return OperationResult.Success(OutputPath,
                    "JSON persistence benchmark completed: " + OutputPath + Environment.NewLine +
                    "Aggregate median load: " + Report.Aggregate.LoadMedianMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms" + Environment.NewLine +
                    "Aggregate median save: " + Report.Aggregate.SaveMedianMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms" + Environment.NewLine +
                    "Aggregate median steady save: " + Report.Aggregate.SteadySaveMedianMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms");
            }
            catch (Exception Problem)
            {
                return OperationResult.Failure<string>("Cannot benchmark JSON persistence: " + Problem.Message);
            }
        }

        public static OperationResult<string> RunSample(string Input, string WorkingFile, string ResultFile,
                                                        string SampleMode)
        {
            var Sample = new BenchmarkSample();
            Sample.Format = SampleFormat;
            Sample.FormatVersion = FormatVersion;
            Sample.Input = Input;
            Sample.WorkingFile = WorkingFile;
            Sample.SampleMode = SampleMode;

            TextWriter PreviousOut = null;
            TextWriter PreviousError = null;
            Exception ResultWriteProblem = null;
            try
            {
                SampleMode = NormalizeWorkerMode(SampleMode);
                Sample.SampleMode = SampleMode;
                Input = RequireFile(Input, "input");
                WorkingFile = Path.GetFullPath(RequireText(WorkingFile, "working file"));
                ResultFile = Path.GetFullPath(RequireText(ResultFile, "result"));
                Sample.Input = Input;
                Sample.WorkingFile = WorkingFile;
                EnsureParent(WorkingFile);
                EnsureParent(ResultFile);
                File.Copy(Input, WorkingFile, true);

                PreviousOut = Console.Out;
                PreviousError = Console.Error;
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);

                HeadlessBootstrap.Initialize();
                ForceFullCollection();

                var Kind = PackageKindFromPath(WorkingFile);
                Sample.Kind = Kind;
                if (Kind == "composition")
                    RunCompositionSample(WorkingFile, Sample, SampleMode == WorkerModeCandidate);
                else
                    RunDomainSample(WorkingFile, Sample, SampleMode == WorkerModeCandidate);

                Sample.AuthoritativeHash = ComputeBenchmarkAuthoritativeHash(WorkingFile, Kind);
                Sample.Valid = true;
            }
            catch (Exception Problem)
            {
                Sample.Valid = false;
                Sample.Error = Problem.ToString();
            }
            finally
            {
                if (PreviousOut != null)
                    Console.SetOut(PreviousOut);
                if (PreviousError != null)
                    Console.SetError(PreviousError);

                try
                {
                    if (!String.IsNullOrWhiteSpace(ResultFile))
                        WriteJson(ResultFile, Sample);
                }
                catch (Exception Problem)
                {
                    ResultWriteProblem = Problem;
                }
            }

            if (ResultWriteProblem != null)
                return OperationResult.Failure<string>("Cannot write benchmark sample: " + ResultWriteProblem.Message);

            return Sample.Valid
                 ? OperationResult.Success(ResultFile, "Benchmark sample written: " + ResultFile)
                 : OperationResult.Failure<string>("Benchmark sample failed: " + Sample.Error);
        }

        private static void RunCompositionSample(string WorkingFile, BenchmarkSample Sample,
                                                 bool RequireV2PreviewReuse)
        {
            var Context = HeadlessBootstrap.Initialize();
            Context.Visualizer.PostViewActivation = null;

            PersistenceLoadingSplash.PersistenceLoadingScope Loading = null;
            PersistenceOperationContext LoadContext = null;
            Tuple<CompositionEngine, string> Load;
            var Timer = Stopwatch.StartNew();
            Loading = PersistenceLoadingSplash.Begin(PersistenceOperationKind.OpenComposition,
                                                      "Opening Composition package and rebuilding its model...", null);
            using (Loading)
            {
                LoadContext = Loading.Context;
                CompositionEngine.CreateActiveCompositionEngine(Context.Compositions, Context.Visualizer, false);
                Load = CompositionEngine.Materialize(new Uri(WorkingFile, UriKind.Absolute));
                if (Load == null || Load.Item1 == null)
                    throw new InvalidOperationException("Cannot load benchmark Composition: " + (Load == null ? "" : Load.Item2));
                LoadContext.ReportStage(PersistenceOperationStages.ActivateWorkspace, 9,
                                        PersistenceOperationStages.LoadStageCount,
                                        "Registering and activating the workspace document", true);
                LoadWorkspaceDocument(Context.Workspace, Load.Item1.TargetComposition);
                Load.Item1.Show();
                Load.Item1.Start();
            }
            Timer.Stop();
            Sample.LoadMilliseconds = Timer.Elapsed.TotalMilliseconds;
            Sample.StageTimings["loadTotal"] = Sample.LoadMilliseconds;
            CopyLoadStageTimings(LoadContext, Sample);
            CopySplashResponsiveness(Loading.ResponsivenessResult, Sample);
            RequireJsonLoad();

            var WorkingLocation = new Uri(WorkingFile, UriKind.Absolute);
            var FirstSaveContext = new PersistenceOperationContext(Guid.NewGuid(),
                PersistenceOperationKind.SaveComposition, null);
            string Error = null;
            Timer.Restart();
            try
            {
                using (FirstSaveContext.MakeCurrent())
                    Error = Load.Item1.Store(WorkingLocation, true, true, false);
            }
            finally
            {
                Timer.Stop();
                FirstSaveContext.Complete();
            }
            if (!String.IsNullOrEmpty(Error))
                throw new InvalidOperationException(Error);
            Sample.SaveMilliseconds = Timer.Elapsed.TotalMilliseconds;
            Sample.StageTimings["firstSave"] = Sample.SaveMilliseconds;
            CopySaveStageTimings(FirstSaveContext, Sample, "firstSave.");

            var SteadySaveContext = new PersistenceOperationContext(Guid.NewGuid(),
                PersistenceOperationKind.SaveComposition, null);
            Timer.Restart();
            try
            {
                using (SteadySaveContext.MakeCurrent())
                    Error = Load.Item1.Store(WorkingLocation, true, true, false);
            }
            finally
            {
                Timer.Stop();
                SteadySaveContext.Complete();
            }
            if (!String.IsNullOrEmpty(Error))
                throw new InvalidOperationException(Error);
            Sample.SteadySaveMilliseconds = Timer.Elapsed.TotalMilliseconds;
            Sample.StageTimings["steadySave"] = Sample.SteadySaveMilliseconds;
            CopySaveStageTimings(SteadySaveContext, Sample, "steadySave.");
            if (RequireV2PreviewReuse)
                AssertV2PreviewReuse(WorkingFile, true);
        }

        private static void RunDomainSample(string WorkingFile, BenchmarkSample Sample,
                                            bool RequireV2PreviewReuse)
        {
            var Context = HeadlessBootstrap.Initialize();
            Context.Visualizer.PostViewActivation = null;

            PersistenceLoadingSplash.PersistenceLoadingScope Loading = null;
            PersistenceOperationContext LoadContext = null;
            Tuple<Domain, string> Load;
            CompositionEngine WorkspaceEngine;
            bool IncludeTemplate;
            Guid OriginalTemplateGlobalId = Guid.Empty;
            VersionCard OriginalTemplateVersion = null;
            var Timer = Stopwatch.StartNew();
            Loading = PersistenceLoadingSplash.Begin(PersistenceOperationKind.OpenDomain,
                                                      "Opening Domain package and rebuilding its model...", null);
            using (Loading)
            {
                LoadContext = Loading.Context;
                CompositionEngine.CreateActiveCompositionEngine(Context.Compositions, Context.Visualizer, true);
                Load = CompositionEngine.MaterializeDomain(new Uri(WorkingFile, UriKind.Absolute));
                if (Load == null || Load.Item1 == null)
                    throw new InvalidOperationException("Cannot load benchmark Domain: " + (Load == null ? "" : Load.Item2));
                RequireJsonLoad();
                IncludeTemplate = Load.Item1.OwnerComposition != null;

                // Exercise the same production materialization path as an interactive Domain open.
                // That path intentionally turns the stored template into a new Composition by
                // replacing its identity/version, so retain those authoritative values and restore
                // them after the timed open before persistence parity is measured.
                if (IncludeTemplate)
                {
                    OriginalTemplateGlobalId = Load.Item1.OwnerComposition.GlobalId;
                    OriginalTemplateVersion = Load.Item1.OwnerComposition.Version;
                }

                var WorkspaceLoad = CompositionEngine.Materialize(null, Load.Item1, IncludeTemplate);
                if (WorkspaceLoad == null || WorkspaceLoad.Item1 == null)
                    throw new InvalidOperationException("Cannot create the benchmark Domain workspace Composition: " +
                        (WorkspaceLoad == null ? "" : WorkspaceLoad.Item2));
                WorkspaceEngine = WorkspaceLoad.Item1;

                LoadContext.ReportStage(PersistenceOperationStages.ActivateWorkspace, 9,
                                        PersistenceOperationStages.LoadStageCount,
                                        "Registering and activating the Domain template document", true);
                WorkspaceEngine.DomainLocation = new Uri(WorkingFile, UriKind.Absolute);
                LoadWorkspaceDocument(Context.Workspace, WorkspaceEngine.TargetComposition);
                WorkspaceEngine.Show();
                WorkspaceEngine.Start();
            }
            Timer.Stop();
            Sample.LoadMilliseconds = Timer.Elapsed.TotalMilliseconds;
            Sample.StageTimings["loadTotal"] = Sample.LoadMilliseconds;
            CopyLoadStageTimings(LoadContext, Sample);
            CopySplashResponsiveness(Loading.ResponsivenessResult, Sample);

            if (IncludeTemplate)
            {
                Load.Item1.OwnerComposition.GlobalId = OriginalTemplateGlobalId;
                Load.Item1.OwnerComposition.Version = OriginalTemplateVersion;
                RestoreEngineGlobalId(WorkspaceEngine, OriginalTemplateGlobalId);
            }

            Load.Item1.SetTemplateSaving(IncludeTemplate);
            var WorkingLocation = new Uri(WorkingFile, UriKind.Absolute);
            var FirstSaveContext = new PersistenceOperationContext(Guid.NewGuid(),
                PersistenceOperationKind.SaveDomain, null);
            string Error = null;
            Timer.Restart();
            try
            {
                using (FirstSaveContext.MakeCurrent())
                {
                    var GitSyncLink = JsonPackagePersistence.ReadGitSyncLink(WorkingFile);
                    Error = JsonPackagePersistence.StoreDomain(Load.Item1, WorkingLocation,
                        false, false, null, true, IncludeTemplate, GitSyncLink, WorkingLocation);
                }
            }
            finally
            {
                Timer.Stop();
                FirstSaveContext.Complete();
            }
            if (!String.IsNullOrEmpty(Error))
                throw new InvalidOperationException(Error);
            Sample.SaveMilliseconds = Timer.Elapsed.TotalMilliseconds;
            Sample.StageTimings["firstSave"] = Sample.SaveMilliseconds;
            CopySaveStageTimings(FirstSaveContext, Sample, "firstSave.");

            var SteadySaveContext = new PersistenceOperationContext(Guid.NewGuid(),
                PersistenceOperationKind.SaveDomain, null);
            Timer.Restart();
            try
            {
                using (SteadySaveContext.MakeCurrent())
                {
                    var GitSyncLink = JsonPackagePersistence.ReadGitSyncLink(WorkingFile);
                    Error = JsonPackagePersistence.StoreDomain(Load.Item1, WorkingLocation,
                        false, false, null, true, IncludeTemplate, GitSyncLink, WorkingLocation);
                }
            }
            finally
            {
                Timer.Stop();
                SteadySaveContext.Complete();
            }
            if (!String.IsNullOrEmpty(Error))
                throw new InvalidOperationException(Error);
            Sample.SteadySaveMilliseconds = Timer.Elapsed.TotalMilliseconds;
            Sample.StageTimings["steadySave"] = Sample.SteadySaveMilliseconds;
            CopySaveStageTimings(SteadySaveContext, Sample, "steadySave.");
            if (RequireV2PreviewReuse)
                AssertV2PreviewReuse(WorkingFile, IncludeTemplate);
        }

        private static void RequireJsonLoad()
        {
            if (!CompositionEngine.LastLoadUsedJsonPersistence || CompositionEngine.LastLoadUsedLegacyBinaryFallback)
                throw new InvalidOperationException("Benchmark did not use authoritative JSON: " +
                                                    CompositionEngine.LastLoadPersistenceDiagnostic);
        }

        private static void CopyLoadStageTimings(PersistenceOperationContext Context, BenchmarkSample Sample)
        {
            if (Context == null || Sample == null)
                return;

            foreach (var Timing in Context.StageTimings)
            {
                double Existing;
                Sample.StageTimings.TryGetValue(Timing.StageId, out Existing);
                Sample.StageTimings[Timing.StageId] = Existing + Timing.Elapsed.TotalMilliseconds;
            }
        }

        private static void CopySaveStageTimings(PersistenceOperationContext Context, BenchmarkSample Sample,
                                                 string Prefix)
        {
            if (Context == null || Sample == null)
                return;

            Prefix = Prefix ?? String.Empty;
            var ExpectedStages = new[]
            {
                PersistenceOperationStages.SaveExportDto,
                PersistenceOperationStages.SaveJsonSerializationHash,
                PersistenceOperationStages.SavePreviewCacheRead,
                PersistenceOperationStages.SavePreviewInputHash,
                PersistenceOperationStages.SavePreviewRender,
                PersistenceOperationStages.SavePreviewReuse,
                PersistenceOperationStages.SaveRequiredPackageWrite,
                PersistenceOperationStages.SavePackageClose,
                PersistenceOperationStages.SaveSafeReplacement,
                PersistenceOperationStages.SaveOptionalSidecars
            };
            foreach (var ExpectedStage in ExpectedStages)
                Sample.StageTimings[Prefix + ExpectedStage] = 0.0;

            foreach (var Timing in Context.StageTimings)
            {
                var StageId = Prefix + Timing.StageId;
                double Existing;
                Sample.StageTimings.TryGetValue(StageId, out Existing);
                Sample.StageTimings[StageId] = Existing + Timing.Elapsed.TotalMilliseconds;
            }
        }

        private static void CopySplashResponsiveness(PersistenceSplashResponsivenessResult Result,
                                                     BenchmarkSample Sample)
        {
            if (Result == null || Sample == null)
                return;

            Sample.SplashResultPresent = true;
            Sample.SplashDispatcherStarted = Result.DispatcherStarted;
            Sample.SplashFirstPaintObserved = Result.FirstPaintObserved;
            Sample.SplashFirstPaintMilliseconds = Result.FirstPaintElapsed.TotalMilliseconds;
            Sample.SplashMaximumHeartbeatGapMilliseconds = Result.MaximumHeartbeatGap.TotalMilliseconds;
            Sample.SplashHeartbeatCount = Result.HeartbeatCount;
            Sample.SplashOperationMilliseconds = Result.OperationElapsed.TotalMilliseconds;
            Sample.SplashDispatcherStoppedCleanly = Result.DispatcherStoppedCleanly;
            Sample.SplashWithinRequiredThresholds = Result.IsWithinRequiredThresholds;
        }

        private static bool SplashResponsivenessPassed(BenchmarkSample Sample)
        {
            return Sample != null &&
                   Sample.SplashResultPresent &&
                   Sample.SplashDispatcherStarted &&
                   Sample.SplashFirstPaintObserved &&
                   Sample.SplashFirstPaintMilliseconds <=
                       PersistenceSplashResponsivenessResult.RequiredFirstPaintMilliseconds &&
                   Sample.SplashMaximumHeartbeatGapMilliseconds <=
                       PersistenceSplashResponsivenessResult.RequiredHeartbeatGapMilliseconds &&
                   Sample.SplashDispatcherStoppedCleanly &&
                   Sample.SplashWithinRequiredThresholds;
        }

        private static void LoadWorkspaceDocument(object Workspace, object Document)
        {
            if (Workspace == null || Document == null)
                throw new InvalidOperationException("Cannot activate a benchmark Composition without a workspace document.");

            var Method = Workspace.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                  .FirstOrDefault(Candidate => Candidate.Name == "LoadDocument" &&
                                                               Candidate.GetParameters().Length == 1);
            if (Method == null)
                throw new MissingMethodException(Workspace.GetType().FullName, "LoadDocument");
            Method.Invoke(Workspace, new[] { Document });
        }

        private static void RestoreEngineGlobalId(CompositionEngine Engine, Guid GlobalId)
        {
            if (Engine == null)
                throw new ArgumentNullException("Engine");

            var Property = Engine.GetType().GetProperty("GlobalId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var Setter = Property == null ? null : Property.GetSetMethod(true);
            if (Setter == null)
                throw new MissingMethodException(Engine.GetType().FullName, "set_GlobalId");

            Setter.Invoke(Engine, new object[] { GlobalId });
        }

        private static void RunWorker(string Input, string WorkingFile, string ResultFile, string WorkerMode)
        {
            var Arguments = "performance run-json-persistence-sample --input " + Quote(Input) +
                            " --working-file " + Quote(WorkingFile) + " --result " + Quote(ResultFile) +
                            " --sample-mode " + Quote(NormalizeWorkerMode(WorkerMode));
            RunChild(Arguments, "benchmark worker");
        }

        private static void ConvertToJsonPersistence(string Source, string Target, string Kind)
        {
            EnsureParent(Target);
            var Arguments = Kind == "composition"
                          ? "composition convert-json-persistence --input " + Quote(Source) + " --output " + Quote(Target)
                          : "domain convert-json-persistence --input " + Quote(Source) + " --output " + Quote(Target);
            RunChild(Arguments, "JSON persistence conversion");
        }

        private static void ValidatePreparedCase(string CorpusRoot, CorpusCase Item)
        {
            var Input = Path.GetFullPath(Path.Combine(CorpusRoot, Item.RelativePath));
            RunChild("package inspect --input " + Quote(Input), "package inspection");
            var ValidationDirectory = Path.Combine(CorpusRoot, "validation", SafeName(Item.Id));
            Directory.CreateDirectory(ValidationDirectory);

            if (!Item.Synthetic)
            {
                var Arguments = Item.Kind == "composition"
                              ? "composition validate-json-persistence --input " + Quote(Input) + " --output-dir " + Quote(ValidationDirectory)
                              : "domain validate-json-persistence --input " + Quote(Input) + " --output-dir " + Quote(ValidationDirectory);
                RunChild(Arguments, "JSON persistence validation");
            }

            // Freeze the corpus only after one complete load/two-save pass. Besides validating
            // the package shell and preview cache, this canonicalizes harmless first-save number
            // formatting (for example a last-bit coordinate normalization) so every measured
            // sample starts from an authoritative payload whose post-save hash is stable.
            // The generic validator is intentionally skipped for the synthetic 15,000-visual
            // Composition because it retains several complete normalized JSON strings and can
            // exhaust the application's required x86 address space; this fresh worker performs
            // the load/two-save/package checks without retaining those duplicate strings.
            var WorkingFile = Path.Combine(ValidationDirectory, "canonical" + Path.GetExtension(Input));
            var ResultFile = Path.Combine(ValidationDirectory, "sample.json");
            RunWorker(Input, WorkingFile, ResultFile, WorkerModeCorpus);
            var Sample = ReadJson<BenchmarkSample>(ResultFile);
            if (Sample == null || !Sample.Valid)
                throw new InvalidDataException("Persistence canonicalization failed: " +
                                               (Sample == null ? "missing sample" : Sample.Error));

            // Check the explicitly sized synthetic cases before the worker output replaces the
            // immutable source package. Repository/legacy cases can legitimately change raw
            // counts during their first canonicalization: Domain construction retains required
            // system defaults and the mandated repair pass removes invalid visuals. Their
            // semantic and failure behavior is covered by the hardening validators above.
            if (Item.Synthetic)
                AssertCanonicalSyntheticPackageCounts(WorkingFile, Item.Kind);

            File.Copy(WorkingFile, Input, true);
            ForceV1PreviewManifest(Input, Item.Kind);
            var Refreshed = DescribeCase(CorpusRoot, Item.Id, Item.Kind, Input, Item.Synthetic,
                                         Item.IsSanitizedSlowPackage);
            CopyCaseMetadata(Refreshed, Item);
            RunChild("package inspect --input " + Quote(Input), "canonical package inspection");
        }

        private static void CopyCaseMetadata(CorpusCase Source, CorpusCase Target)
        {
            Target.AuthoritativeHash = Source.AuthoritativeHash;
            Target.PackageSha256 = Source.PackageSha256;
            Target.Bytes = Source.Bytes;
            Target.EntityCount = Source.EntityCount;
            Target.ViewCount = Source.ViewCount;
            Target.VisualCount = Source.VisualCount;
        }

        private static void ForceV1PreviewManifest(string PackagePath, string PackageKind)
        {
            using (var Pack = Package.Open(Path.GetFullPath(PackagePath), FileMode.Open, FileAccess.ReadWrite))
            {
                IDictionary<string, object> Root = null;
                if (Pack.PartExists(ContainerSnapshotService.ManifestPartUri))
                {
                    string ExistingJson;
                    using (var Stream = Pack.GetPart(ContainerSnapshotService.ManifestPartUri).GetStream(FileMode.Open, FileAccess.Read))
                    using (var Reader = new StreamReader(Stream, Encoding.UTF8, true))
                        ExistingJson = Reader.ReadToEnd();
                    Root = Json.DeserializeObject(ExistingJson) as IDictionary<string, object>;
                }

                if (Root == null)
                    Root = new Dictionary<string, object>(StringComparer.Ordinal);
                Root["format"] = "ThinkComposer.ContainerSnapshot";
                Root["formatVersion"] = 1;
                if (!Root.ContainsKey("generatedAtUtc"))
                    Root["generatedAtUtc"] = SyntheticTimestamp;
                if (!Root.ContainsKey("application"))
                    Root["application"] = "ThinkComposer";
                if (!Root.ContainsKey("packageKind"))
                    Root["packageKind"] = PackageKind;
                if (!Root.ContainsKey("jsonParts"))
                    Root["jsonParts"] = new List<object>();
                if (!Root.ContainsKey("previews"))
                    Root["previews"] = new List<object>();
                if (!Root.ContainsKey("warnings"))
                    Root["warnings"] = new List<string>();

                WriteSyntheticPart(Pack, ContainerSnapshotService.ManifestPartUri,
                                   Utf8NoBom.GetBytes(Json.Serialize(Root)));
                Pack.Flush();
            }
        }

        private static void AssertV2PreviewReuse(string PackagePath, bool ExpectPreviews)
        {
            using (var Pack = Package.Open(Path.GetFullPath(PackagePath), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!Pack.PartExists(ContainerSnapshotService.ManifestPartUri))
                    throw new InvalidDataException("Steady-state save did not write /Interchange/manifest.json.");

                string ManifestJson;
                using (var Stream = Pack.GetPart(ContainerSnapshotService.ManifestPartUri).GetStream(FileMode.Open, FileAccess.Read))
                using (var Reader = new StreamReader(Stream, Encoding.UTF8, true))
                    ManifestJson = Reader.ReadToEnd();

                var Root = Json.DeserializeObject(ManifestJson) as IDictionary<string, object>;
                if (Root == null || GraphInt(Root, "formatVersion") != 2)
                    throw new InvalidDataException("Steady-state save did not produce a v2 preview manifest.");

                object PreviewGraph;
                var PreviewItems = Root.TryGetValue("previews", out PreviewGraph) ? PreviewGraph as IEnumerable : null;
                var ReusedOrEmpty = 0;
                if (PreviewItems != null)
                    foreach (var Item in PreviewItems)
                    {
                        var Preview = Item as IDictionary<string, object>;
                        if (Preview == null)
                            continue;

                        var Disposition = GraphString(Preview, "disposition");
                        if (!String.Equals(Disposition, "reused", StringComparison.Ordinal) &&
                            !String.Equals(Disposition, "empty", StringComparison.Ordinal))
                            throw new InvalidDataException("Steady-state preview was not reused or cached as empty; disposition='" +
                                                           (Disposition ?? "<missing>") + "'.");
                        ReusedOrEmpty++;
                    }

                if (ExpectPreviews && ReusedOrEmpty == 0)
                    throw new InvalidDataException("Steady-state save did not report a reusable or empty preview.");
            }
        }

        private static string GraphString(IDictionary<string, object> Graph, string Key)
        {
            object Value;
            return Graph != null && Graph.TryGetValue(Key, out Value) && Value != null
                 ? Convert.ToString(Value, CultureInfo.InvariantCulture) : null;
        }

        private static int GraphInt(IDictionary<string, object> Graph, string Key)
        {
            int Parsed;
            return Int32.TryParse(GraphString(Graph, Key), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out Parsed) ? Parsed : -1;
        }

        private static void ValidateMeasuredOutputs(BenchmarkCaseResult Item, bool AllowLegacyBaselineOutput)
        {
            var Failures = new List<string>();
            for (var Index = 0; Index < Item.Samples.Count; Index++)
            {
                var Sample = Item.Samples[Index];
                try
                {
                    Sample.OutputValidationMessage = ValidateMeasuredOutputPackage(
                        Sample.WorkingFile, Item.Kind, AllowLegacyBaselineOutput);
                    Sample.OutputPackageSha256 = HashFile(Sample.WorkingFile);
                    Sample.OutputBytes = new FileInfo(Sample.WorkingFile).Length;
                    Sample.OutputValidationPassed = true;
                }
                catch (Exception Problem)
                {
                    Sample.OutputValidationPassed = false;
                    Sample.OutputValidationMessage = Problem.Message;
                    Failures.Add("sample " + (Index + 1).ToString(CultureInfo.InvariantCulture) +
                                 ": " + Problem.Message);
                }
            }

            Item.ValidationPassed = Item.Samples.Count > 0 && Failures.Count == 0;
            Item.ValidationMessage = Item.ValidationPassed
                ? "All " + Item.Samples.Count.ToString(CultureInfo.InvariantCulture) +
                  " measured outputs passed " + (AllowLegacyBaselineOutput
                    ? "JSON-authoritative baseline" : "strict JSON-only") + " inspection."
                : (Failures.Count == 0 ? "No measured outputs were available for inspection."
                                       : String.Join(" | ", Failures.ToArray()));
        }

        private static string ValidateMeasuredOutputPackage(string PackagePath, string Kind,
                                                            bool AllowLegacyBaselineOutput)
        {
            var Inspection = JsonPackagePersistence.Inspect(RequireFile(PackagePath, "measured output"));
            if (!Inspection.JsonAuthoritative || Inspection.LegacyBinaryOnly)
                throw new InvalidDataException("Measured output is not JSON-authoritative persistence.");

            var IsComposition = String.Equals(Kind, "composition", StringComparison.OrdinalIgnoreCase);
            var IsDomain = String.Equals(Kind, "domain", StringComparison.OrdinalIgnoreCase);
            if (!IsComposition && !IsDomain)
                throw new InvalidDataException("Measured output has an unsupported package kind: " + Kind + ".");

            var HasAuthoritativeRoot = IsComposition ? Inspection.HasCompositionJson : Inspection.HasDomainJson;
            var HasMatchingLegacyFallback = IsComposition ? Inspection.HasCompositionBinary : Inspection.HasDomainBinary;
            var HasUnrelatedLegacyFallback = IsComposition ? Inspection.HasDomainBinary : Inspection.HasCompositionBinary;
            if (!HasAuthoritativeRoot)
                throw new InvalidDataException("Measured output is missing its authoritative root JSON payload.");
            if (HasUnrelatedLegacyFallback)
                throw new InvalidDataException("Measured output contains an unrelated legacy binary part.");
            if (HasMatchingLegacyFallback && !AllowLegacyBaselineOutput)
                throw new InvalidDataException("Measured output is not JSON-only authoritative persistence.");

            return HasMatchingLegacyFallback
                ? "JSON-authoritative package inspection passed with the matching legacy binary fallback permitted for a pre-optimization baseline."
                : "JSON-authoritative, binary-free package inspection passed.";
        }

        private static void RunChild(string Arguments, string Description)
        {
            var Start = new ProcessStartInfo();
            Start.FileName = EntryExecutable();
            Start.Arguments = Arguments;
            Start.UseShellExecute = false;
            Start.CreateNoWindow = true;
            Start.RedirectStandardOutput = true;
            Start.RedirectStandardError = true;

            using (var Child = System.Diagnostics.Process.Start(Start))
            {
                var OutputTask = Child.StandardOutput.ReadToEndAsync();
                var ErrorTask = Child.StandardError.ReadToEndAsync();
                Child.WaitForExit();
                Task.WaitAll(OutputTask, ErrorTask);
                if (Child.ExitCode != 0)
                    throw new InvalidOperationException(Description + " failed with exit code " +
                        Child.ExitCode.ToString(CultureInfo.InvariantCulture) + ". " +
                        ErrorTask.Result + OutputTask.Result);
            }
        }

        private static CorpusCase DescribeCase(string CorpusRoot, string Id, string Kind, string FilePath,
                                               bool Synthetic, bool IsSanitizedSlowPackage)
        {
            var Item = new CorpusCase();
            Item.Id = Id;
            Item.Kind = Kind;
            Item.RelativePath = MakeRelativePath(CorpusRoot, FilePath);
            Item.AuthoritativeHash = ComputeBenchmarkAuthoritativeHash(FilePath, Kind);
            Item.PackageSha256 = HashFile(FilePath);
            Item.Bytes = new FileInfo(FilePath).Length;
            Item.Synthetic = Synthetic;
            Item.IsSanitizedSlowPackage = IsSanitizedSlowPackage;

            if (Kind == "composition")
            {
                var Payload = JsonPackagePersistence.ReadCompositionPackage(FilePath);
                Item.EntityCount = (Payload.CompositionDocument.Ideas == null ? 0 : Payload.CompositionDocument.Ideas.Count) +
                                   (Payload.CompositionDocument.Relationships == null ? 0 : Payload.CompositionDocument.Relationships.Count);
                Item.ViewCount = Payload.CompositionDocument.Views == null ? 0 : Payload.CompositionDocument.Views.Count;
                Item.VisualCount = Payload.CompositionDocument.Views == null ? 0 :
                    Payload.CompositionDocument.Views.Sum(View => View.Visuals == null ? 0 : View.Visuals.Count);
            }
            else
            {
                var Payload = JsonPackagePersistence.ReadDomainPackage(FilePath);
                var Document = Payload.DomainDocument;
                Item.EntityCount = Count(Document.ConceptDefinitions) + Count(Document.RelationshipDefinitions) +
                                   Count(Document.TableDefinitions) + Count(Document.MarkerDefinitions) +
                                   Count(Document.ExternalLanguages) + Count(Document.ConceptDefinitionOutputTemplates) +
                                   Count(Document.RelationshipDefinitionOutputTemplates);
                Item.ViewCount = Payload.TemplateCompositionDocument == null || Payload.TemplateCompositionDocument.Views == null
                               ? 0 : Payload.TemplateCompositionDocument.Views.Count;
                Item.VisualCount = Payload.TemplateCompositionDocument == null || Payload.TemplateCompositionDocument.Views == null
                                 ? 0 : Payload.TemplateCompositionDocument.Views.Sum(View => View.Visuals == null ? 0 : View.Visuals.Count);
            }

            return Item;
        }

        private static int Count<T>(ICollection<T> Items)
        {
            return Items == null ? 0 : Items.Count;
        }

        private static void Summarize(BenchmarkCaseResult Item)
        {
            Item.LoadMedianMilliseconds = Median(Item.LoadMilliseconds);
            Item.LoadP95Milliseconds = NearestRank(Item.LoadMilliseconds, 0.95);
            Item.SaveMedianMilliseconds = Median(Item.SaveMilliseconds);
            Item.SaveP95Milliseconds = NearestRank(Item.SaveMilliseconds, 0.95);
            Item.SteadySaveMedianMilliseconds = Median(Item.SteadySaveMilliseconds);
            Item.SteadySaveP95Milliseconds = NearestRank(Item.SteadySaveMilliseconds, 0.95);
        }

        private static AggregateResult Aggregate(IList<BenchmarkCaseResult> Cases, int Iterations)
        {
            var Result = new AggregateResult();
            for (var Index = 0; Index < Iterations; Index++)
            {
                Result.LoadMilliseconds.Add(Cases.Sum(Item => Item.LoadMilliseconds[Index]));
                Result.SaveMilliseconds.Add(Cases.Sum(Item => Item.SaveMilliseconds[Index]));
                Result.SteadySaveMilliseconds.Add(Cases.Sum(Item => Item.SteadySaveMilliseconds[Index]));
            }

            Result.LoadMedianMilliseconds = Median(Result.LoadMilliseconds);
            Result.LoadP95Milliseconds = NearestRank(Result.LoadMilliseconds, 0.95);
            Result.SaveMedianMilliseconds = Median(Result.SaveMilliseconds);
            Result.SaveP95Milliseconds = NearestRank(Result.SaveMilliseconds, 0.95);
            Result.SteadySaveMedianMilliseconds = Median(Result.SteadySaveMilliseconds);
            Result.SteadySaveP95Milliseconds = NearestRank(Result.SteadySaveMilliseconds, 0.95);
            return Result;
        }

        private static void CompareWithBaseline(BenchmarkReport Candidate, BenchmarkReport Baseline, string BaselinePath)
        {
            if (Baseline == null || Baseline.Aggregate == null)
                throw new InvalidDataException("Baseline report has no aggregate results.");
            if (!String.Equals(Baseline.Format, ReportFormat, StringComparison.Ordinal) ||
                Baseline.FormatVersion != FormatVersion)
                throw new InvalidDataException("Unsupported baseline report format.");
            if (!Baseline.ValidationPassed)
                throw new InvalidDataException("Baseline report did not pass persistence validation.");
            if (Candidate.AllowLegacyBaselineOutput)
                throw new InvalidDataException("Candidate benchmark reports must use strict JSON-only output validation.");
            if (!String.Equals(Candidate.CorpusMode, Baseline.CorpusMode, StringComparison.Ordinal))
                throw new InvalidDataException("Baseline corpus mode does not match the candidate corpus mode.");
            if (!String.Equals(Candidate.CorpusFingerprint, Baseline.CorpusFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Baseline corpus fingerprint does not match the candidate corpus.");
            if (Candidate.Iterations != Baseline.Iterations)
                throw new InvalidDataException("Baseline iteration count does not match the candidate run.");
            if (Candidate.Warmup != Baseline.Warmup)
                throw new InvalidDataException("Baseline warmup count does not match the candidate run.");
            if (Candidate.RequireResponsiveSplash != Baseline.RequireResponsiveSplash)
                throw new InvalidDataException("Baseline splash-certification mode does not match the candidate run.");
            if (Baseline.Cases == null || Baseline.Cases.Count == 0 ||
                Baseline.Cases.Any(Item => Item == null || Item.Samples == null ||
                                                    Item.Samples.Count != Baseline.Iterations))
                throw new InvalidDataException("Baseline report does not contain the expected measured sample count.");
            if (Candidate.Cases == null || Candidate.Cases.Count == 0 ||
                Candidate.Cases.Any(Item => Item == null || Item.Samples == null ||
                                                     Item.Samples.Count != Candidate.Iterations))
                throw new InvalidDataException("Candidate report does not contain the expected measured sample count.");
            ValidateMatchingBenchmarkCases(Candidate.Cases, Baseline.Cases);
            if (Candidate.RequireResponsiveSplash &&
                (Baseline.Cases.SelectMany(Item => Item.Samples).Any(Item => !Item.SplashResultPresent) ||
                 Candidate.Cases.SelectMany(Item => Item.Samples).Any(Item => !Item.SplashResultPresent)))
                throw new InvalidDataException("Baseline and candidate reports must both contain measured splash telemetry.");
            if (!Candidate.Machine.EquivalentTo(Baseline.Machine))
                throw new InvalidDataException("Baseline machine fingerprint does not match the candidate machine.");

            var Comparison = new BaselineComparison();
            Comparison.BaselinePath = BaselinePath;
            Comparison.LoadSpeedup = Divide(Baseline.Aggregate.LoadMedianMilliseconds,
                                             Candidate.Aggregate.LoadMedianMilliseconds);
            Comparison.SaveSpeedup = Divide(Baseline.Aggregate.SaveMedianMilliseconds,
                                             Candidate.Aggregate.SaveMedianMilliseconds);
            Comparison.Passed = Comparison.LoadSpeedup >= Candidate.MinimumSpeedup &&
                                Comparison.SaveSpeedup >= Candidate.MinimumSpeedup;
            Candidate.BaselineComparison = Comparison;
        }

        private static void ValidateMatchingBenchmarkCases(IList<BenchmarkCaseResult> CandidateCases,
                                                           IList<BenchmarkCaseResult> BaselineCases)
        {
            if (CandidateCases.Count != BaselineCases.Count)
                throw new InvalidDataException("Baseline case count does not match the candidate corpus.");

            var BaselineById = new Dictionary<string, BenchmarkCaseResult>(StringComparer.Ordinal);
            foreach (var Item in BaselineCases)
            {
                if (String.IsNullOrWhiteSpace(Item.Id) || BaselineById.ContainsKey(Item.Id))
                    throw new InvalidDataException("Baseline report contains a missing or duplicate case id.");
                BaselineById.Add(Item.Id, Item);
            }

            foreach (var CandidateCase in CandidateCases)
            {
                BenchmarkCaseResult BaselineCase;
                if (String.IsNullOrWhiteSpace(CandidateCase.Id) ||
                    !BaselineById.TryGetValue(CandidateCase.Id, out BaselineCase))
                    throw new InvalidDataException("Baseline report is missing candidate case: " + CandidateCase.Id);
                if (!String.Equals(CandidateCase.Kind, BaselineCase.Kind, StringComparison.Ordinal) ||
                    !String.Equals(CandidateCase.AuthoritativeHash, BaselineCase.AuthoritativeHash,
                                   StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(CandidateCase.PackageSha256, BaselineCase.PackageSha256,
                                   StringComparison.OrdinalIgnoreCase) ||
                    CandidateCase.Bytes != BaselineCase.Bytes ||
                    CandidateCase.IsSanitizedSlowPackage != BaselineCase.IsSanitizedSlowPackage)
                    throw new InvalidDataException("Baseline hashes or metadata do not match candidate case: " +
                                                   CandidateCase.Id);
            }
        }

        private static double Divide(double Numerator, double Denominator)
        {
            return Denominator <= 0 ? 0 : Numerator / Denominator;
        }

        private static double Median(IEnumerable<double> Values)
        {
            var Sorted = Values.OrderBy(Value => Value).ToList();
            if (Sorted.Count == 0)
                return 0;
            var Middle = Sorted.Count / 2;
            return Sorted.Count % 2 == 0 ? (Sorted[Middle - 1] + Sorted[Middle]) / 2.0 : Sorted[Middle];
        }

        private static double NearestRank(IEnumerable<double> Values, double Percentile)
        {
            var Sorted = Values.OrderBy(Value => Value).ToList();
            if (Sorted.Count == 0)
                return 0;
            var Rank = Math.Max(1, (int)Math.Ceiling(Percentile * Sorted.Count));
            return Sorted[Math.Min(Sorted.Count, Rank) - 1];
        }

        private static void ValidateCorpus(CorpusManifest Corpus, string CorpusPath)
        {
            if (Corpus == null || Corpus.Format != CorpusFormat || Corpus.FormatVersion != FormatVersion)
                throw new InvalidDataException("Unsupported corpus format: " + CorpusPath);
            if (Corpus.Cases == null || Corpus.Cases.Count == 0)
                throw new InvalidDataException("Corpus contains no cases.");
            Corpus.Mode = NormalizeCorpusMode(Corpus.Mode);
            if (Corpus.Mode == CorpusModeCertification &&
                !Corpus.Cases.Any(Item => Item != null && Item.IsSanitizedSlowPackage))
                throw new InvalidDataException("Certification corpus contains no tagged sanitized slow package.");
            if (!String.Equals(Corpus.Fingerprint, ComputeCorpusFingerprint(Corpus.Cases, Corpus.Mode),
                               StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Corpus fingerprint does not match its case metadata.");

            foreach (var Item in Corpus.Cases)
            {
                if (Item == null)
                    throw new InvalidDataException("Corpus contains an empty case entry.");
                var Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(CorpusPath), Item.RelativePath));
                if (!File.Exists(Path))
                    throw new FileNotFoundException("Corpus case file not found.", Path);
                var ActualBytes = new FileInfo(Path).Length;
                if (ActualBytes != Item.Bytes)
                    throw new InvalidDataException("Corpus case byte length changed: " + Item.Id +
                        ". Expected " + Item.Bytes.ToString(CultureInfo.InvariantCulture) + ", received " +
                        ActualBytes.ToString(CultureInfo.InvariantCulture) + ".");
                var ActualPackageSha256 = HashFile(Path);
                if (!String.Equals(ActualPackageSha256, Item.PackageSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Corpus case package SHA-256 changed: " + Item.Id);
                var Actual = ComputeBenchmarkAuthoritativeHash(Path, Item.Kind);
                if (!String.Equals(Actual, Item.AuthoritativeHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Corpus case authoritative hash changed: " + Item.Id);
            }
        }

        private static string ComputeBenchmarkAuthoritativeHash(string FilePath, string Kind)
        {
            var Parts = new List<Tuple<Uri, bool>>();
            if (String.Equals(Kind, "composition", StringComparison.OrdinalIgnoreCase))
            {
                Parts.Add(Tuple.Create(JsonPackagePersistence.CompositionJsonPartUri, true));
                Parts.Add(Tuple.Create(JsonPackagePersistence.DomainJsonPartUri, true));
            }
            else if (String.Equals(Kind, "domain", StringComparison.OrdinalIgnoreCase))
            {
                Parts.Add(Tuple.Create(JsonPackagePersistence.DomainJsonPartUri, true));
                Parts.Add(Tuple.Create(JsonPackagePersistence.TemplateCompositionJsonPartUri, false));
            }
            else
                throw new ArgumentException("Unknown package kind: " + Kind, "Kind");

            var Builder = new StringBuilder();
            using (var Pack = Package.Open(Path.GetFullPath(FilePath), FileMode.Open, FileAccess.Read, FileShare.Read))
                foreach (var Part in Parts)
                {
                    if (!Pack.PartExists(Part.Item1))
                    {
                        if (Part.Item2)
                            throw new InvalidDataException("Package is missing authoritative JSON part: " + Part.Item1);
                        Builder.Append(Part.Item1).Append("=<missing>\n");
                        continue;
                    }

                    Builder.Append(Part.Item1).Append("=")
                           .Append(HashNormalizedJsonPart(Pack, Part.Item1)).Append("\n");
                }

            return HashBytes(Utf8NoBom.GetBytes(Builder.ToString()));
        }

        private static string HashNormalizedJsonPart(Package Package, Uri PartUri)
        {
            using (var Hash = SHA256.Create())
            using (var Stream = Package.GetPart(PartUri).GetStream(FileMode.Open, FileAccess.Read))
            using (var Reader = new StreamReader(Stream, Encoding.UTF8, true))
            {
                string Line;
                while ((Line = Reader.ReadLine()) != null)
                {
                    if (Line.TrimStart().StartsWith("\"exportedAtUtc\":", StringComparison.Ordinal))
                        continue;

                    var Bytes = Utf8NoBom.GetBytes(Line + "\n");
                    Hash.TransformBlock(Bytes, 0, Bytes.Length, Bytes, 0);
                }
                Hash.TransformFinalBlock(new byte[0], 0, 0);
                return ToHex(Hash.Hash);
            }
        }

        private static string ComputeCorpusFingerprint(IEnumerable<CorpusCase> Cases, string Mode)
        {
            var Text = "mode|" + NormalizeCorpusMode(Mode) + "\n" + String.Join("\n", Cases.OrderBy(Item => Item.Id, StringComparer.Ordinal)
                .Select(Item => Item.Id + "|" + Item.Kind + "|" + Item.AuthoritativeHash + "|" +
                                Item.PackageSha256 + "|" + Item.Bytes.ToString(CultureInfo.InvariantCulture) + "|" +
                                (Item.IsSanitizedSlowPackage ? "sanitizedSlow" : "standard")).ToArray());
            return HashBytes(Utf8NoBom.GetBytes(Text));
        }

        private static string UniqueCaseId(string Proposed, ISet<string> Used)
        {
            var Result = Proposed;
            var Suffix = 2;
            while (!Used.Add(Result))
            {
                Result = Proposed + "-" + Suffix.ToString(CultureInfo.InvariantCulture);
                Suffix++;
            }
            return Result;
        }

        private static string PackageKindFromPath(string Path)
        {
            var Extension = System.IO.Path.GetExtension(Path);
            if (String.Equals(Extension, ".tcom", StringComparison.OrdinalIgnoreCase))
                return "composition";
            if (String.Equals(Extension, ".tdom", StringComparison.OrdinalIgnoreCase))
                return "domain";
            throw new ArgumentException("Expected a .tcom or .tdom package: " + Path);
        }

        private static string NormalizeCorpusMode(string Mode)
        {
            if (String.IsNullOrWhiteSpace(Mode) ||
                String.Equals(Mode, "dev", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(Mode, CorpusModeDevelopment, StringComparison.OrdinalIgnoreCase))
                return CorpusModeDevelopment;
            if (String.Equals(Mode, "cert", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(Mode, CorpusModeCertification, StringComparison.OrdinalIgnoreCase))
                return CorpusModeCertification;

            throw new ArgumentException("Corpus mode must be 'development' or 'certification'.");
        }

        private static string NormalizeWorkerMode(string Mode)
        {
            if (String.Equals(Mode, WorkerModeCorpus, StringComparison.OrdinalIgnoreCase))
                return WorkerModeCorpus;
            if (String.Equals(Mode, WorkerModeBaseline, StringComparison.OrdinalIgnoreCase))
                return WorkerModeBaseline;
            if (String.Equals(Mode, WorkerModeCandidate, StringComparison.OrdinalIgnoreCase))
                return WorkerModeCandidate;

            throw new ArgumentException("Benchmark sample mode must be 'corpus', 'baseline', or 'candidate'.");
        }

        private static string EntryExecutable()
        {
            return Assembly.GetEntryAssembly().Location;
        }

        private static string Quote(string Value)
        {
            return "\"" + (Value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string RequireText(string Value, string Name)
        {
            if (String.IsNullOrWhiteSpace(Value))
                throw new ArgumentException("Missing " + Name + ".");
            return Value;
        }

        private static string RequireFile(string Value, string Name)
        {
            var Result = Path.GetFullPath(RequireText(Value, Name));
            if (!File.Exists(Result))
                throw new FileNotFoundException("Cannot find " + Name + ".", Result);
            return Result;
        }

        private static string RequireDirectory(string Value, string Name)
        {
            var Result = Path.GetFullPath(RequireText(Value, Name));
            if (!Directory.Exists(Result))
                throw new DirectoryNotFoundException("Cannot find " + Name + ": " + Result);
            return Result;
        }

        private static bool PathHasText(string Value)
        {
            return !String.IsNullOrWhiteSpace(Value);
        }

        private static void EnsureParent(string FilePath)
        {
            var Parent = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (!String.IsNullOrEmpty(Parent))
                Directory.CreateDirectory(Parent);
        }

        private static string SafeName(string Text)
        {
            var Invalid = Path.GetInvalidFileNameChars();
            var Result = new string((Text ?? "case").Select(Character => Invalid.Contains(Character) ? '_' : Character).ToArray());
            return String.IsNullOrWhiteSpace(Result) ? "case" : Result;
        }

        private static string MakeRelativePath(string Root, string FilePath)
        {
            var RootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(Root)));
            var FileUri = new Uri(Path.GetFullPath(FilePath));
            return Uri.UnescapeDataString(RootUri.MakeRelativeUri(FileUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string PathText)
        {
            return PathText.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                 ? PathText : PathText + Path.DirectorySeparatorChar;
        }

        private static void ForceFullCollection()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
        }

        private static JavaScriptSerializer CreateJsonSerializer()
        {
            var Serializer = new JavaScriptSerializer();
            Serializer.MaxJsonLength = Int32.MaxValue;
            Serializer.RecursionLimit = 512;
            return Serializer;
        }

        private static T ReadJson<T>(string FilePath)
        {
            return Json.Deserialize<T>(File.ReadAllText(FilePath, Encoding.UTF8));
        }

        private static void WriteJson(string FilePath, object Value)
        {
            EnsureParent(FilePath);
            File.WriteAllText(FilePath, Json.Serialize(Value), Utf8NoBom);
        }

        private static string HashFile(string FilePath)
        {
            using (var Stream = File.OpenRead(FilePath))
            using (var Hash = SHA256.Create())
                return ToHex(Hash.ComputeHash(Stream));
        }

        internal static string HashBytes(byte[] Bytes)
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

        internal sealed class CorpusManifest
        {
            public string Format { get; set; }
            public int FormatVersion { get; set; }
            public string GeneratedAtUtc { get; set; }
            public string SourceRoot { get; set; }
            public string Mode { get; set; }
            public string Fingerprint { get; set; }
            public List<CorpusCase> Cases { get; set; }

            public CorpusManifest() { this.Cases = new List<CorpusCase>(); }
        }

        internal sealed class CorpusCase
        {
            public string Id { get; set; }
            public string Kind { get; set; }
            public string RelativePath { get; set; }
            public string AuthoritativeHash { get; set; }
            public string PackageSha256 { get; set; }
            public long Bytes { get; set; }
            public int EntityCount { get; set; }
            public int ViewCount { get; set; }
            public int VisualCount { get; set; }
            public bool Synthetic { get; set; }
            public bool IsSanitizedSlowPackage { get; set; }
        }

        internal sealed class BenchmarkSample
        {
            public string Format { get; set; }
            public int FormatVersion { get; set; }
            public string Input { get; set; }
            public string WorkingFile { get; set; }
            public string SampleMode { get; set; }
            public string Kind { get; set; }
            public double LoadMilliseconds { get; set; }
            public double SaveMilliseconds { get; set; }
            public double SteadySaveMilliseconds { get; set; }
            public string AuthoritativeHash { get; set; }
            public Dictionary<string, double> StageTimings { get; set; }
            public bool SplashResultPresent { get; set; }
            public bool SplashDispatcherStarted { get; set; }
            public bool SplashFirstPaintObserved { get; set; }
            public double SplashFirstPaintMilliseconds { get; set; }
            public double SplashMaximumHeartbeatGapMilliseconds { get; set; }
            public int SplashHeartbeatCount { get; set; }
            public double SplashOperationMilliseconds { get; set; }
            public bool SplashDispatcherStoppedCleanly { get; set; }
            public bool SplashWithinRequiredThresholds { get; set; }
            public bool OutputValidationPassed { get; set; }
            public string OutputValidationMessage { get; set; }
            public string OutputPackageSha256 { get; set; }
            public long OutputBytes { get; set; }
            public bool Valid { get; set; }
            public string Error { get; set; }

            public BenchmarkSample()
            {
                this.StageTimings = new Dictionary<string, double>(StringComparer.Ordinal);
            }
        }

        internal sealed class BenchmarkReport
        {
            public string Format { get; set; }
            public int FormatVersion { get; set; }
            public string GeneratedAtUtc { get; set; }
            public string ExecutablePath { get; set; }
            public string ExecutableSha256 { get; set; }
            public MachineFingerprint Machine { get; set; }
            public string CorpusPath { get; set; }
            public string CorpusFingerprint { get; set; }
            public string CorpusMode { get; set; }
            public int Warmup { get; set; }
            public int Iterations { get; set; }
            public double MinimumSpeedup { get; set; }
            public bool RequireResponsiveSplash { get; set; }
            public bool AllowLegacyBaselineOutput { get; set; }
            public List<BenchmarkCaseResult> Cases { get; set; }
            public AggregateResult Aggregate { get; set; }
            public bool PersistenceValidationPassed { get; set; }
            public int SplashCertificationCaseCount { get; set; }
            public bool SplashResponsivenessPassed { get; set; }
            public bool ValidationPassed { get; set; }
            public BaselineComparison BaselineComparison { get; set; }

            public BenchmarkReport() { this.Cases = new List<BenchmarkCaseResult>(); }
        }

        internal sealed class BenchmarkCaseResult
        {
            public string Id { get; set; }
            public string Kind { get; set; }
            public string AuthoritativeHash { get; set; }
            public string PackageSha256 { get; set; }
            public long Bytes { get; set; }
            public int EntityCount { get; set; }
            public int ViewCount { get; set; }
            public int VisualCount { get; set; }
            public bool IsSanitizedSlowPackage { get; set; }
            public List<double> LoadMilliseconds { get; set; }
            public List<double> SaveMilliseconds { get; set; }
            public List<double> SteadySaveMilliseconds { get; set; }
            public List<BenchmarkSample> Samples { get; set; }
            public double LoadMedianMilliseconds { get; set; }
            public double LoadP95Milliseconds { get; set; }
            public double SaveMedianMilliseconds { get; set; }
            public double SaveP95Milliseconds { get; set; }
            public double SteadySaveMedianMilliseconds { get; set; }
            public double SteadySaveP95Milliseconds { get; set; }
            public bool SplashResponsivenessPassed { get; set; }
            public bool ValidationPassed { get; set; }
            public string ValidationMessage { get; set; }
            public string LastWorkingPackage { get; set; }

            public BenchmarkCaseResult()
            {
                this.LoadMilliseconds = new List<double>();
                this.SaveMilliseconds = new List<double>();
                this.SteadySaveMilliseconds = new List<double>();
                this.Samples = new List<BenchmarkSample>();
            }
        }

        internal sealed class AggregateResult
        {
            public List<double> LoadMilliseconds { get; set; }
            public List<double> SaveMilliseconds { get; set; }
            public List<double> SteadySaveMilliseconds { get; set; }
            public double LoadMedianMilliseconds { get; set; }
            public double LoadP95Milliseconds { get; set; }
            public double SaveMedianMilliseconds { get; set; }
            public double SaveP95Milliseconds { get; set; }
            public double SteadySaveMedianMilliseconds { get; set; }
            public double SteadySaveP95Milliseconds { get; set; }

            public AggregateResult()
            {
                this.LoadMilliseconds = new List<double>();
                this.SaveMilliseconds = new List<double>();
                this.SteadySaveMilliseconds = new List<double>();
            }
        }

        internal sealed class BaselineComparison
        {
            public string BaselinePath { get; set; }
            public double LoadSpeedup { get; set; }
            public double SaveSpeedup { get; set; }
            public bool Passed { get; set; }
        }

        internal sealed class MachineFingerprint
        {
            public string MachineName { get; set; }
            public string OperatingSystem { get; set; }
            public string Cpu { get; set; }
            public int ProcessorCount { get; set; }
            public string Clr { get; set; }
            public string ProcessArchitecture { get; set; }

            public static MachineFingerprint Create()
            {
                var Result = new MachineFingerprint();
                Result.MachineName = Environment.MachineName;
                Result.OperatingSystem = Environment.OSVersion.VersionString;
                Result.Cpu = ReadCpuName();
                Result.ProcessorCount = Environment.ProcessorCount;
                Result.Clr = Environment.Version.ToString();
                Result.ProcessArchitecture = Environment.Is64BitProcess ? "x64" : "x86";
                return Result;
            }

            public bool EquivalentTo(MachineFingerprint Other)
            {
                return Other != null &&
                       String.Equals(this.MachineName, Other.MachineName, StringComparison.OrdinalIgnoreCase) &&
                       String.Equals(this.OperatingSystem, Other.OperatingSystem, StringComparison.Ordinal) &&
                       String.Equals(this.Cpu, Other.Cpu, StringComparison.Ordinal) &&
                       this.ProcessorCount == Other.ProcessorCount &&
                       String.Equals(this.Clr, Other.Clr, StringComparison.Ordinal) &&
                       String.Equals(this.ProcessArchitecture, Other.ProcessArchitecture, StringComparison.Ordinal);
            }

            private static string ReadCpuName()
            {
                try
                {
                    using (var Searcher = new ManagementObjectSearcher("select Name from Win32_Processor"))
                        foreach (var Item in Searcher.Get())
                            return Convert.ToString(Item["Name"], CultureInfo.InvariantCulture);
                }
                catch
                {
                }
                return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown";
            }
        }
    }
}
