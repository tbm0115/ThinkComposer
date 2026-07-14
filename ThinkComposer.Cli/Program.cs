// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer CLI
//
// Headless command-line entry point.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

using Instrumind.Common;
using Instrumind.ThinkComposer.Headless;

namespace Instrumind.ThinkComposer.Cli
{
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitFailure = 1;
        private const int ExitUnexpected = 2;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args == null || args.Length == 0 || IsHelp(args[0]))
                {
                    PrintGlobalHelp();
                    return ExitSuccess;
                }

                return Execute(args);
            }
            catch (UsageException Problem)
            {
                Console.Error.WriteLine(Problem.Message);
                Console.Error.WriteLine();
                Console.Error.WriteLine("Run 'thinkcomposer --help' for usage.");
                return ExitFailure;
            }
            catch (Exception Problem)
            {
                Console.Error.WriteLine("Unexpected exception:");
                Console.Error.WriteLine(Problem.ToString());
                return ExitUnexpected;
            }
            finally
            {
                if (Application.Current != null)
                    Application.Current.Shutdown();
            }
        }

        private static int Execute(string[] Args)
        {
            var Area = Args[0].ToLowerInvariant();

            if (Area == "composition")
                return ExecuteComposition(Args.Skip(1).ToArray());

            if (Area == "domain")
                return ExecuteDomain(Args.Skip(1).ToArray());

            if (Area == "package")
                return ExecutePackage(Args.Skip(1).ToArray());

            if (Area == "git")
                return ExecuteGit(Args.Skip(1).ToArray());

            if (Area == "report")
                return ExecuteReport(Args.Skip(1).ToArray());

            if (Area == "output")
                return ExecuteOutput(Args.Skip(1).ToArray());

            if (Area == "performance")
                return ExecutePerformance(Args.Skip(1).ToArray());

            throw new UsageException("Unknown command area: " + Args[0]);
        }

        private static int ExecuteComposition(string[] Args)
        {
            if (Args.Length == 0 || IsHelp(Args[0]))
            {
                PrintCompositionHelp();
                return ExitSuccess;
            }

            var Command = Args[0].ToLowerInvariant();
            var Options = OptionSet.Parse(Args.Skip(1).ToArray());

            if (Options.HelpRequested)
            {
                PrintCompositionHelp();
                return ExitSuccess;
            }

            if (Command == "export-json")
                return Finish(HeadlessThinkComposerOperations.ExportCompositionJson(
                    Options.Required("input"),
                    Options.Required("output")));

            if (Command == "export-image")
            {
                var ImageOptions = new HeadlessImageExportOptions();
                ImageOptions.Input = Options.Required("input");
                ImageOptions.Output = Options.Required("output");
                ImageOptions.ViewTechName = Options.Optional("view");
                ImageOptions.Width = OptionalPositiveInt(Options, "width");
                ImageOptions.Height = OptionalPositiveInt(Options, "height");
                ImageOptions.Padding = OptionalNonNegativeDouble(Options, "padding");
                ImageOptions.Transparent = Options.Has("transparent");

                foreach (var FitTechName in Options.Values("fit"))
                    ImageOptions.FitTechNames.Add(FitTechName);

                foreach (var FitTechName in Options.Values("fit-tech-name"))
                    ImageOptions.FitTechNames.Add(FitTechName);

                return Finish(HeadlessThinkComposerOperations.ExportCompositionImage(ImageOptions));
            }

            if (Command == "import-json")
                return Finish(HeadlessThinkComposerOperations.ImportCompositionJson(
                    Options.Required("input"),
                    Options.Required("json"),
                    Options.Required("output"),
                    Options.Has("in-place"),
                    Options.Has("preview-only")));

            if (Command == "validate-json-roundtrip")
                return Finish(HeadlessThinkComposerOperations.ValidateCompositionJsonRoundTrip(
                    Options.Required("input"),
                    Options.Required("output-dir")));

            if (Command == "convert-json-persistence")
                return Finish(HeadlessThinkComposerOperations.ConvertCompositionToJsonPersistence(
                    Options.Required("input"),
                    Options.Required("output")));

            if (Command == "validate-json-persistence")
                return Finish(HeadlessThinkComposerOperations.ValidateCompositionJsonPersistence(
                    Options.Required("input"),
                    Options.Required("output-dir")));

            throw new UsageException("Unknown composition command: " + Args[0]);
        }

        private static int ExecuteDomain(string[] Args)
        {
            if (Args.Length == 0 || IsHelp(Args[0]))
            {
                PrintDomainHelp();
                return ExitSuccess;
            }

            var Command = Args[0].ToLowerInvariant();
            var Options = OptionSet.Parse(Args.Skip(1).ToArray());

            if (Options.HelpRequested)
            {
                PrintDomainHelp();
                return ExitSuccess;
            }

            if (Command == "export-json")
                return Finish(HeadlessThinkComposerOperations.ExportDomainJson(
                    Options.Required("input"),
                    Options.Required("output")));

            if (Command == "import-json")
                return Finish(HeadlessThinkComposerOperations.ImportDomainJson(
                    Options.Required("input"),
                    Options.Required("json"),
                    Options.Required("output"),
                    Options.Has("in-place"),
                    Options.Has("preview-only")));

            if (Command == "update-embedded")
                return Finish(HeadlessThinkComposerOperations.UpdateEmbeddedDomainFromNativeDomain(
                    Options.Required("input"),
                    Options.Required("domain"),
                    Options.Required("output"),
                    Options.Has("in-place"),
                    Options.Has("preview-only")));

            if (Command == "validate-json-roundtrip")
                return Finish(HeadlessThinkComposerOperations.ValidateDomainJsonRoundTrip(
                    Options.Required("input"),
                    Options.Required("output-dir")));

            if (Command == "convert-json-persistence")
                return Finish(HeadlessThinkComposerOperations.ConvertDomainToJsonPersistence(
                    Options.Required("input"),
                    Options.Required("output")));

            if (Command == "validate-json-persistence")
                return Finish(HeadlessThinkComposerOperations.ValidateDomainJsonPersistence(
                    Options.Required("input"),
                    Options.Required("output-dir")));

            throw new UsageException("Unknown domain command: " + Args[0]);
        }

        private static int ExecutePackage(string[] Args)
        {
            if (Args.Length == 0 || IsHelp(Args[0]))
            {
                PrintPackageHelp();
                return ExitSuccess;
            }

            var Command = Args[0].ToLowerInvariant();
            var Options = OptionSet.Parse(Args.Skip(1).ToArray());

            if (Options.HelpRequested)
            {
                PrintPackageHelp();
                return ExitSuccess;
            }

            if (Command == "inspect")
                return Finish(HeadlessThinkComposerOperations.InspectPackagePersistence(
                    Options.Required("input")));

            throw new UsageException("Unknown package command: " + Args[0]);
        }

        private static int ExecuteGit(string[] Args)
        {
            if (Args.Length == 0 || IsHelp(Args[0]))
            {
                PrintGitHelp();
                return ExitSuccess;
            }

            var Command = Args[0].ToLowerInvariant();
            var Options = OptionSet.Parse(Args.Skip(1).ToArray());

            if (Options.HelpRequested)
            {
                PrintGitHelp();
                return ExitSuccess;
            }

            if (Command == "link")
                return Finish(HeadlessThinkComposerOperations.LinkPackageToGit(
                    Options.Required("input"),
                    Options.Required("remote"),
                    Options.Required("branch"),
                    Options.Required("path"),
                    Options.Optional("domain-path"),
                    Options.Optional("output"),
                    Options.Has("in-place")));

            if (Command == "unlink")
                return Finish(HeadlessThinkComposerOperations.UnlinkPackageFromGit(
                    Options.Required("input"),
                    Options.Optional("output"),
                    Options.Has("in-place")));

            if (Command == "status")
                return Finish(HeadlessThinkComposerOperations.GitPackageStatus(
                    Options.Required("input")));

            if (Command == "pull")
                return Finish(HeadlessThinkComposerOperations.PullPackageFromGit(
                    Options.Required("input"),
                    Options.Optional("output"),
                    Options.Has("in-place"),
                    Options.Optional("backup-dir")));

            if (Command == "push")
                return Finish(HeadlessThinkComposerOperations.PushCompositionToGit(
                    Options.Required("input"),
                    Options.Optional("message")));

            throw new UsageException("Unknown git command: " + Args[0]);
        }

        private static int ExecuteReport(string[] Args)
        {
            if (Args.Length == 0 || IsHelp(Args[0]))
            {
                PrintReportHelp();
                return ExitSuccess;
            }

            var Command = Args[0].ToLowerInvariant();
            var Options = OptionSet.Parse(Args.Skip(1).ToArray());

            if (Options.HelpRequested)
            {
                PrintReportHelp();
                return ExitSuccess;
            }

            if (Command == "pdf")
                return Finish(HeadlessThinkComposerOperations.GenerateReport(
                    Options.Required("input"),
                    Options.Required("output")));

            throw new UsageException("Unknown report command: " + Args[0]);
        }

        private static int ExecuteOutput(string[] Args)
        {
            if (Args.Length == 0 || IsHelp(Args[0]))
            {
                PrintOutputHelp();
                return ExitSuccess;
            }

            var Command = Args[0].ToLowerInvariant();
            var Options = OptionSet.Parse(Args.Skip(1).ToArray());

            if (Options.HelpRequested)
            {
                PrintOutputHelp();
                return ExitSuccess;
            }

            if (Command == "generate")
            {
                var OutputOptions = new HeadlessOutputOptions();
                OutputOptions.Input = Options.Required("input");
                OutputOptions.OutputDirectory = Options.Required("output-dir");
                OutputOptions.LanguageTechName = Options.Required("language");
                OutputOptions.GenerateRelationships = Options.Has("relationships");
                OutputOptions.CreateCompositionRootDirectory = Options.Has("composition-root-dir");
                OutputOptions.UseTechNamesAsProgramIdentifiers = Options.Has("use-tech-names");

                foreach (var Exclusion in Options.Values("exclude"))
                    OutputOptions.ExcludedIdeas.Add(Exclusion);

                return Finish(HeadlessThinkComposerOperations.GenerateOutput(OutputOptions));
            }

            throw new UsageException("Unknown output command: " + Args[0]);
        }

        private static int ExecutePerformance(string[] Args)
        {
            if (Args.Length == 0 || IsHelp(Args[0]))
            {
                PrintPerformanceHelp();
                return ExitSuccess;
            }

            var Command = Args[0].ToLowerInvariant();
            var Options = OptionSet.Parse(Args.Skip(1).ToArray());
            if (Options.HelpRequested)
            {
                PrintPerformanceHelp();
                return ExitSuccess;
            }

            if (Command == "prepare-json-persistence-corpus")
                return Finish(PersistencePerformance.PrepareCorpus(
                    Options.Required("source-root"),
                    Options.Required("output-dir"),
                    Options.Values("real-package"),
                    Options.Optional("mode")));

            if (Command == "benchmark-json-persistence")
            {
                var Warmup = OptionalNonNegativeInt(Options, "warmup") ?? 1;
                var Iterations = OptionalPositiveInt(Options, "iterations") ?? 5;
                var MinimumSpeedup = OptionalNonNegativeDouble(Options, "minimum-speedup") ?? 2.0;
                if (MinimumSpeedup <= 0)
                    throw new UsageException("--minimum-speedup must be greater than zero.");

                return Finish(PersistencePerformance.Benchmark(
                    Options.Required("corpus"),
                    Options.Required("output"),
                    Warmup,
                    Iterations,
                    Options.Optional("baseline"),
                    MinimumSpeedup,
                    !Options.Has("skip-splash-responsiveness-gate"),
                    Options.Has("allow-legacy-baseline-output")));
            }

            // Coordinator-only implementation detail. Each timed sample runs in a clean process.
            if (Command == "run-json-persistence-sample")
                return Finish(PersistencePerformance.RunSample(
                    Options.Required("input"),
                    Options.Required("working-file"),
                    Options.Required("result"),
                    Options.Required("sample-mode")));

            throw new UsageException("Unknown performance command: " + Args[0]);
        }

        private static int Finish(OperationResult<string> Result)
        {
            if (Result == null)
            {
                Console.Error.WriteLine("Operation did not return a result.");
                return ExitFailure;
            }

            if (!Result.WasSuccessful)
            {
                Console.Error.WriteLine(Result.Message);
                return ExitFailure;
            }

            if (!String.IsNullOrWhiteSpace(Result.Message))
                Console.WriteLine(Result.Message);

            return ExitSuccess;
        }

        private static int? OptionalPositiveInt(OptionSet Options, string Key)
        {
            var Text = Options.Optional(Key);
            if (String.IsNullOrWhiteSpace(Text))
                return null;

            int Result;
            if (!Int32.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out Result) || Result <= 0)
                throw new UsageException("--" + Key + " must be a positive integer.");

            return Result;
        }

        private static int? OptionalNonNegativeInt(OptionSet Options, string Key)
        {
            var Text = Options.Optional(Key);
            if (String.IsNullOrWhiteSpace(Text))
                return null;

            int Result;
            if (!Int32.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out Result) || Result < 0)
                throw new UsageException("--" + Key + " must be zero or greater.");

            return Result;
        }

        private static double? OptionalNonNegativeDouble(OptionSet Options, string Key)
        {
            var Text = Options.Optional(Key);
            if (String.IsNullOrWhiteSpace(Text))
                return null;

            double Result;
            if (!Double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out Result) || Result < 0)
                throw new UsageException("--" + Key + " must be zero or greater.");

            return Result;
        }

        private static bool IsHelp(string Arg)
        {
            return String.Equals(Arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(Arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(Arg, "help", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintGlobalHelp()
        {
            Console.WriteLine("ThinkComposer headless CLI");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  thinkcomposer composition export-json --input <file.tcom> --output <file.json>");
            Console.WriteLine("  thinkcomposer composition export-image --input <file.tcom> --output <file.png|file.jpg|file.gif|file.tif|file.bmp> [--view <view-tech-name>] [--fit <idea-tech-name>] [--width <px>] [--height <px>] [--padding <px>] [--transparent]");
            Console.WriteLine("  thinkcomposer composition import-json --input <file.tcom> --json <file.json> --output <file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine("  thinkcomposer composition validate-json-roundtrip --input <file.tcom> --output-dir <dir>");
            Console.WriteLine("  thinkcomposer composition convert-json-persistence --input <file.tcom> --output <file.tcom>");
            Console.WriteLine("  thinkcomposer composition validate-json-persistence --input <file.tcom> --output-dir <dir>");
            Console.WriteLine("  thinkcomposer domain export-json --input <file.tdom|file.tcom> --output <file.json>");
            Console.WriteLine("  thinkcomposer domain import-json --input <file.tdom|file.tcom> --json <file.json> --output <file.tdom|file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine("  thinkcomposer domain update-embedded --input <file.tcom> --domain <file.tdom> --output <file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine("  thinkcomposer domain validate-json-roundtrip --input <file.tdom|file.tcom> --output-dir <dir>");
            Console.WriteLine("  thinkcomposer domain convert-json-persistence --input <file.tdom> --output <file.tdom>");
            Console.WriteLine("  thinkcomposer domain validate-json-persistence --input <file.tdom> --output-dir <dir>");
            Console.WriteLine("  thinkcomposer package inspect --input <file.tcom|file.tdom>");
            Console.WriteLine("  thinkcomposer git link --input <file.tcom|file.tdom> --remote <url> --branch <branch> --path <repo-path> [--domain-path <repo-tdom-path>] --output <file> [--in-place]");
            Console.WriteLine("  thinkcomposer git unlink --input <file.tcom|file.tdom> --output <file> [--in-place]");
            Console.WriteLine("  thinkcomposer git status --input <file.tcom|file.tdom>");
            Console.WriteLine("  thinkcomposer git pull --input <file.tcom|file.tdom> --output <file> [--in-place] [--backup-dir <dir>]");
            Console.WriteLine("  thinkcomposer git push --input <file.tcom> --message <message>");
            Console.WriteLine("  thinkcomposer report pdf --input <file.tcom> --output <file.pdf|file.xps>");
            Console.WriteLine("  thinkcomposer output generate --input <file.tcom> --output-dir <dir> --language <language-tech-name> [--relationships] [--composition-root-dir] [--use-tech-names] [--exclude <idea-id>]");
            Console.WriteLine("  thinkcomposer performance prepare-json-persistence-corpus --source-root <repo> --output-dir <dir> [--mode <development|certification>] [--real-package <sanitized-slow-file>]...");
            Console.WriteLine("  thinkcomposer performance benchmark-json-persistence --corpus <dir>\\corpus.json --output <report.json> [--warmup 1] [--iterations 5] [--baseline <report.json>] [--minimum-speedup 2.0] [--allow-legacy-baseline-output] [--skip-splash-responsiveness-gate]");
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 success, 1 usage/validation/operation failure, 2 unexpected exception.");
        }

        private static void PrintCompositionHelp()
        {
            Console.WriteLine("Composition commands:");
            Console.WriteLine("  thinkcomposer composition export-json --input <file.tcom> --output <file.json>");
            Console.WriteLine("  thinkcomposer composition export-image --input <file.tcom> --output <file.png|file.jpg|file.gif|file.tif|file.bmp> [--view <view-tech-name>] [--fit <idea-tech-name>] [--width <px>] [--height <px>] [--padding <px>] [--transparent]");
            Console.WriteLine("  thinkcomposer composition import-json --input <file.tcom> --json <file.json> --output <file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine("  thinkcomposer composition validate-json-roundtrip --input <file.tcom> --output-dir <dir>");
            Console.WriteLine("  thinkcomposer composition convert-json-persistence --input <file.tcom> --output <file.tcom>");
            Console.WriteLine("  thinkcomposer composition validate-json-persistence --input <file.tcom> --output-dir <dir>");
            Console.WriteLine();
            Console.WriteLine("Imports require --output. To overwrite --input, set --output to the input path and pass --in-place.");
            Console.WriteLine("Image export defaults to the root/main view fitted into 1600x1200 pixels. Repeat --fit to fit specific visible idea TechNames.");
            Console.WriteLine("Round-trip validation rebuilds Domain and Composition from JSON and compares normalized re-exported JSON.");
            Console.WriteLine("Persistence validation saves a JSON-authoritative package, reopens it through normal load, saves again, and compares canonical root JSON payloads.");
        }

        private static void PrintDomainHelp()
        {
            Console.WriteLine("Domain commands:");
            Console.WriteLine("  thinkcomposer domain export-json --input <file.tdom|file.tcom> --output <file.json>");
            Console.WriteLine("  thinkcomposer domain import-json --input <file.tdom|file.tcom> --json <file.json> --output <file.tdom|file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine("  thinkcomposer domain update-embedded --input <file.tcom> --domain <file.tdom> --output <file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine("  thinkcomposer domain validate-json-roundtrip --input <file.tdom|file.tcom> --output-dir <dir>");
            Console.WriteLine("  thinkcomposer domain convert-json-persistence --input <file.tdom> --output <file.tdom>");
            Console.WriteLine("  thinkcomposer domain validate-json-persistence --input <file.tdom> --output-dir <dir>");
            Console.WriteLine();
            Console.WriteLine("For .tcom input, domain import updates the embedded domain and writes a .tcom output.");
            Console.WriteLine("Use update-embedded to update a .tcom embedded domain directly from a native .tdom source.");
            Console.WriteLine("Round-trip validation rebuilds a Domain from JSON and compares normalized re-exported JSON.");
            Console.WriteLine("Persistence validation saves a JSON-authoritative .tdom, reopens it through normal load, saves again, and compares canonical root JSON payloads.");
        }

        private static void PrintPackageHelp()
        {
            Console.WriteLine("Package commands:");
            Console.WriteLine("  thinkcomposer package inspect --input <file.tcom|file.tdom>");
            Console.WriteLine();
            Console.WriteLine("Reports whether a native package is JSON-authoritative, transitional with binary fallback, or legacy binary-only.");
        }

        private static void PrintGitHelp()
        {
            Console.WriteLine("Git sync commands:");
            Console.WriteLine("  thinkcomposer git link --input <file.tcom|file.tdom> --remote <url> --branch <branch> --path <repo-path> [--domain-path <repo-tdom-path>] --output <file> [--in-place]");
            Console.WriteLine("  thinkcomposer git unlink --input <file.tcom|file.tdom> --output <file> [--in-place]");
            Console.WriteLine("  thinkcomposer git status --input <file.tcom|file.tdom>");
            Console.WriteLine("  thinkcomposer git pull --input <file.tcom|file.tdom> --output <file> [--in-place] [--backup-dir <dir>]");
            Console.WriteLine("  thinkcomposer git push --input <file.tcom> --message <message>");
            Console.WriteLine();
            Console.WriteLine("Git sync stores remote/branch/path linkage in /manifest.json and uses installed git.exe for remote operations.");
            Console.WriteLine("Domains are pull-only in this version. Composition push commits and pushes the linked .tcom path.");
        }

        private static void PrintReportHelp()
        {
            Console.WriteLine("Report commands:");
            Console.WriteLine("  thinkcomposer report pdf --input <file.tcom> --output <file.pdf|file.xps>");
            Console.WriteLine();
            Console.WriteLine("Uses the existing ThinkComposer standard PDF/XPS report workflow and saved/default report settings.");
        }

        private static void PrintOutputHelp()
        {
            Console.WriteLine("Output commands:");
            Console.WriteLine("  thinkcomposer output generate --input <file.tcom> --output-dir <dir> --language <language-tech-name> [--relationships] [--composition-root-dir] [--use-tech-names] [--exclude <idea-id>]");
            Console.WriteLine();
            Console.WriteLine("--exclude can be repeated and accepts an idea GlobalId or TechName.");
        }

        private static void PrintPerformanceHelp()
        {
            Console.WriteLine("JSON persistence performance commands:");
            Console.WriteLine("  thinkcomposer performance prepare-json-persistence-corpus --source-root <repo> --output-dir <dir> [--mode <development|certification>] [--real-package <sanitized-slow-file>]...");
            Console.WriteLine("  thinkcomposer performance benchmark-json-persistence --corpus <dir>\\corpus.json --output <report.json> [--warmup 1] [--iterations 5] [--baseline <report.json>] [--minimum-speedup 2.0] [--allow-legacy-baseline-output] [--skip-splash-responsiveness-gate]");
            Console.WriteLine();
            Console.WriteLine("Development corpus mode is the default and permits repository-only preparation. Certification mode requires at least one --real-package; those inputs are tagged as sanitized slow packages for splash certification.");
            Console.WriteLine("Corpus preparation converts the three repository examples, All-Purpose and Genealogy Tree Domains, tagged sanitized packages, and deterministic large synthetic packages to JSON-authoritative persistence.");
            Console.WriteLine("Benchmark samples copy their input before timing and run load, first save, and steady-state save in a fresh child process. Defaults are one warmup and five measured samples.");
            Console.WriteLine("When --baseline is supplied, full corpus package hashes, machine fingerprint, and run counts must match; both aggregate load and first-save medians must meet --minimum-speedup.");
            Console.WriteLine("Use --allow-legacy-baseline-output only to record a pre-optimization baseline whose JSON-authoritative saves retain the exact matching legacy binary fallback. It cannot be combined with --baseline; candidate runs remain strictly JSON-only.");
            Console.WriteLine("Certification gates tagged sanitized slow opens; development gates all cases. Each selected splash must paint within 250 ms, heartbeat within 500 ms, and stop its dispatcher cleanly. --skip-splash-responsiveness-gate makes the checks diagnostic only.");
        }

        private sealed class OptionSet
        {
            private readonly Dictionary<string, List<string>> Options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            public bool HelpRequested { get; private set; }

            public static OptionSet Parse(string[] Args)
            {
                var Result = new OptionSet();

                for (int Index = 0; Index < Args.Length; Index++)
                {
                    var Arg = Args[Index];
                    if (IsHelp(Arg))
                    {
                        Result.HelpRequested = true;
                        continue;
                    }

                    if (!Arg.StartsWith("--", StringComparison.Ordinal))
                        throw new UsageException("Unexpected argument: " + Arg);

                    var Key = Arg.Substring(2);
                    if (String.IsNullOrWhiteSpace(Key))
                        throw new UsageException("Invalid option: " + Arg);

                    string Value = null;
                    if (Index + 1 < Args.Length && !Args[Index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        Value = Args[Index + 1];
                        Index++;
                    }

                    if (!Result.Options.ContainsKey(Key))
                        Result.Options[Key] = new List<string>();

                    if (Value != null)
                        Result.Options[Key].Add(Value);
                    else
                        Result.Options[Key].Add(null);
                }

                return Result;
            }

            public bool Has(string Key)
            {
                return this.Options.ContainsKey(Key);
            }

            public string Required(string Key)
            {
                var Value = this.Optional(Key);
                if (String.IsNullOrWhiteSpace(Value))
                    throw new UsageException("Missing required option --" + Key + ".");

                return Value;
            }

            public string Optional(string Key)
            {
                List<string> Values = null;
                if (!this.Options.TryGetValue(Key, out Values) || Values.Count < 1)
                    return null;

                return Values.LastOrDefault(Value => Value != null);
            }

            public IEnumerable<string> Values(string Key)
            {
                List<string> Values = null;
                if (!this.Options.TryGetValue(Key, out Values))
                    return new string[0];

                return Values.Where(Value => !String.IsNullOrWhiteSpace(Value)).ToArray();
            }
        }

        private sealed class UsageException : Exception
        {
            public UsageException(string Message)
                : base(Message)
            {
            }
        }
    }
}
