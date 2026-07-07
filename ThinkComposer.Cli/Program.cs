// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer CLI
//
// Headless command-line entry point.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

            if (Area == "report")
                return ExecuteReport(Args.Skip(1).ToArray());

            if (Area == "output")
                return ExecuteOutput(Args.Skip(1).ToArray());

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

            if (Command == "import-json")
                return Finish(HeadlessThinkComposerOperations.ImportCompositionJson(
                    Options.Required("input"),
                    Options.Required("json"),
                    Options.Required("output"),
                    Options.Has("in-place"),
                    Options.Has("preview-only")));

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

            throw new UsageException("Unknown domain command: " + Args[0]);
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
            Console.WriteLine("  thinkcomposer composition import-json --input <file.tcom> --json <file.json> --output <file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine("  thinkcomposer domain export-json --input <file.tdom|file.tcom> --output <file.json>");
            Console.WriteLine("  thinkcomposer domain import-json --input <file.tdom|file.tcom> --json <file.json> --output <file.tdom|file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine("  thinkcomposer report pdf --input <file.tcom> --output <file.pdf|file.xps>");
            Console.WriteLine("  thinkcomposer output generate --input <file.tcom> --output-dir <dir> --language <language-tech-name> [--relationships] [--composition-root-dir] [--use-tech-names] [--exclude <idea-id>]");
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 success, 1 usage/validation/operation failure, 2 unexpected exception.");
        }

        private static void PrintCompositionHelp()
        {
            Console.WriteLine("Composition commands:");
            Console.WriteLine("  thinkcomposer composition export-json --input <file.tcom> --output <file.json>");
            Console.WriteLine("  thinkcomposer composition import-json --input <file.tcom> --json <file.json> --output <file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine();
            Console.WriteLine("Imports require --output. To overwrite --input, set --output to the input path and pass --in-place.");
        }

        private static void PrintDomainHelp()
        {
            Console.WriteLine("Domain commands:");
            Console.WriteLine("  thinkcomposer domain export-json --input <file.tdom|file.tcom> --output <file.json>");
            Console.WriteLine("  thinkcomposer domain import-json --input <file.tdom|file.tcom> --json <file.json> --output <file.tdom|file.tcom> [--in-place] [--preview-only]");
            Console.WriteLine();
            Console.WriteLine("For .tcom input, domain import updates the embedded domain and writes a .tcom output.");
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
