using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

using Instrumind.Common;

using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.InformationModel;

namespace Instrumind.ThinkComposer.Composer.Generation
{
    public enum OutputTemplateRole
    {
        Unknown,
        DocumentRoot,
        Fragment,
        SubTemplate,
        Diagnostic,
        Disabled,
        NotApplicable
    }

    public enum OutputTemplateIssueSeverity
    {
        Info,
        Warning,
        Error,
        Blocking
    }

    public class OutputTemplateDirectiveInfo
    {
        public OutputTemplateDirectiveInfo()
        {
            this.Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.Role = OutputTemplateRole.Unknown;
        }

        public IDictionary<string, string> Parameters { get; private set; }

        public OutputTemplateRole Role { get; set; }

        public bool HasExplicitRole { get; set; }

        public string SubTemplateName { get; set; }

        public string TargetFileName { get; set; }

        public string TargetFileExtension { get; set; }

        public bool TrimLeadingWhitespace { get; set; }

        public string NormalizeLineEndings { get; set; }

        public bool WriteUtf8NoBom { get; set; }

        public bool EnsureTrailingNewline { get; set; }

        public bool ValidateAfterRender { get; set; }

        public string OutputValidation { get; set; }

        public static OutputTemplateDirectiveInfo Parse(string TemplateText)
        {
            var Result = new OutputTemplateDirectiveInfo();
            if (TemplateText == null)
                TemplateText = "";

            using (var Reader = new StringReader(TemplateText))
            {
                string Line = null;
                while ((Line = Reader.ReadLine()) != null)
                {
                    var Trimmed = Line.TrimStart();
                    if (!Trimmed.StartsWith(GenerationManager.GENPAR_PREFIX, StringComparison.Ordinal))
                        continue;

                    var Declaration = Trimmed.Substring(GenerationManager.GENPAR_PREFIX.Length);
                    var AssignIndex = Declaration.IndexOf(GenerationManager.GENPAR_ASSIGN, StringComparison.Ordinal);
                    if (AssignIndex < 0)
                        continue;

                    var Key = Declaration.Substring(0, AssignIndex).Trim();
                    var Value = Declaration.Substring(AssignIndex + GenerationManager.GENPAR_ASSIGN.Length).Trim();
                    if (Key.IsAbsent())
                        continue;

                    Result.Parameters[Key] = Value;
                }
            }

            Result.ApplyParameterHints();

            if (!Result.HasExplicitRole)
                Result.Role = InferRole(TemplateText);

            if (IsXmlLike(null, Result.TargetFileName.NullDefault(Result.TargetFileExtension), Result))
                Result.TrimLeadingWhitespace = true;

            return Result;
        }

        public static OutputTemplateDirectiveInfo Merge(OutputTemplateDirectiveInfo TemplateDirectives, IDictionary<string, string> RenderedParameters)
        {
            var Result = new OutputTemplateDirectiveInfo();
            if (TemplateDirectives != null)
                foreach (var Pair in TemplateDirectives.Parameters)
                    Result.Parameters[Pair.Key] = Pair.Value;

            if (RenderedParameters != null)
                foreach (var Pair in RenderedParameters)
                    Result.Parameters[Pair.Key] = Pair.Value;

            Result.ApplyParameterHints();

            if (!Result.HasExplicitRole && TemplateDirectives != null)
                Result.Role = TemplateDirectives.Role;

            return Result;
        }

        public bool ShouldEmitStandalone
        {
            get
            {
                return this.Role != OutputTemplateRole.SubTemplate &&
                       this.Role != OutputTemplateRole.Fragment &&
                       this.Role != OutputTemplateRole.Disabled &&
                       this.Role != OutputTemplateRole.NotApplicable;
            }
        }

        private void ApplyParameterHints()
        {
            string Value = null;

            if (TryGet(this.Parameters, "TemplateRole", out Value) || TryGet(this.Parameters, "Role", out Value))
            {
                this.Role = ParseRole(Value);
                this.HasExplicitRole = true;
            }

            if (TryGet(this.Parameters, GenerationManager.GENKEY_SEC_SUBTEMPLATE, out Value) && !Value.IsAbsent())
            {
                this.SubTemplateName = Value;
                if (!this.HasExplicitRole)
                    this.Role = OutputTemplateRole.SubTemplate;
            }

            if (TryGet(this.Parameters, GenerationManager.GENKEY_VAR_FILENAME, out Value) ||
                TryGet(this.Parameters, "TargetFileName", out Value))
                this.TargetFileName = Value;

            if (TryGet(this.Parameters, "TargetFileExtension", out Value) ||
                TryGet(this.Parameters, "TargetExtension", out Value) ||
                TryGet(this.Parameters, "FileExtension", out Value))
                this.TargetFileExtension = NormalizeExtension(Value);

            this.TrimLeadingWhitespace = GetBool(this.Parameters, "outputPostProcess.trimLeadingWhitespace") ||
                                          GetBool(this.Parameters, "trimLeadingWhitespace");
            this.NormalizeLineEndings = GetString(this.Parameters, "outputPostProcess.normalizeLineEndings")
                                        .NullDefault(GetString(this.Parameters, "normalizeLineEndings"));
            this.WriteUtf8NoBom = GetBool(this.Parameters, "outputPostProcess.writeUtf8NoBom") ||
                                   GetBool(this.Parameters, "writeUtf8NoBom");
            this.EnsureTrailingNewline = GetBool(this.Parameters, "outputPostProcess.ensureTrailingNewline") ||
                                         GetBool(this.Parameters, "ensureTrailingNewline");
            this.ValidateAfterRender = GetBool(this.Parameters, "validateAfterRender");
            this.OutputValidation = GetString(this.Parameters, "outputValidation");
            if (!String.IsNullOrWhiteSpace(this.OutputValidation))
                this.ValidateAfterRender = true;
        }

        private static bool TryGet(IDictionary<string, string> Values, string Key, out string Value)
        {
            Value = null;
            return Values != null && Values.TryGetValue(Key, out Value);
        }

        private static string GetString(IDictionary<string, string> Values, string Key)
        {
            string Value;
            return TryGet(Values, Key, out Value) ? Value : null;
        }

        private static bool GetBool(IDictionary<string, string> Values, string Key)
        {
            string Value;
            if (!TryGet(Values, Key, out Value) || Value.IsAbsent())
                return false;

            bool Parsed;
            return Boolean.TryParse(Value, out Parsed) ? Parsed :
                   String.Equals(Value, "1", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(Value, "yes", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(Value, "on", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(Value, "enabled", StringComparison.OrdinalIgnoreCase);
        }

        private static OutputTemplateRole InferRole(string TemplateText)
        {
            if (TemplateText.IsAbsent())
                return OutputTemplateRole.Unknown;

            try
            {
                var Sections = FileGenerator.GetContainedTemplateTexts(TemplateText);
                if (Sections.Count > 0 &&
                    !Sections.ContainsKey(FileGenerator.DEFAULT_INITIAL_TEMPLATE_NAME) &&
                    Sections.Keys.Any(Key => !Key.IsAbsent()))
                    return OutputTemplateRole.SubTemplate;
            }
            catch
            {
                return OutputTemplateRole.Unknown;
            }

            return OutputTemplateRole.DocumentRoot;
        }

        public static OutputTemplateRole ParseRole(string RoleText)
        {
            if (RoleText.IsAbsent())
                return OutputTemplateRole.Unknown;

            RoleText = RoleText.Trim().Replace("-", "").Replace("_", "");

            if (String.Equals(RoleText, "DocumentRoot", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(RoleText, "Root", StringComparison.OrdinalIgnoreCase))
                return OutputTemplateRole.DocumentRoot;

            if (String.Equals(RoleText, "Fragment", StringComparison.OrdinalIgnoreCase))
                return OutputTemplateRole.Fragment;

            if (String.Equals(RoleText, "SubTemplate", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(RoleText, "Subtemplate", StringComparison.OrdinalIgnoreCase))
                return OutputTemplateRole.SubTemplate;

            if (String.Equals(RoleText, "Diagnostic", StringComparison.OrdinalIgnoreCase))
                return OutputTemplateRole.Diagnostic;

            if (String.Equals(RoleText, "Disabled", StringComparison.OrdinalIgnoreCase))
                return OutputTemplateRole.Disabled;

            if (String.Equals(RoleText, "NotApplicable", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(RoleText, "NotApp", StringComparison.OrdinalIgnoreCase))
                return OutputTemplateRole.NotApplicable;

            return OutputTemplateRole.Unknown;
        }

        public static bool IsXmlLike(ExternalLanguageDeclaration Language, string FileName, OutputTemplateDirectiveInfo Directives)
        {
            if (Directives != null && String.Equals(Directives.OutputValidation, "XmlWellFormed", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!FileName.IsAbsent() && Path.GetExtension(FileName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                return true;

            if (Language == null)
                return false;

            return Language.TechName.ToStringAlways().IndexOf("XML", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   Language.Name.ToStringAlways().IndexOf("XML", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsJsonLike(ExternalLanguageDeclaration Language, string FileName, OutputTemplateDirectiveInfo Directives)
        {
            if (Directives != null && String.Equals(Directives.OutputValidation, "Json", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!FileName.IsAbsent() && Path.GetExtension(FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
                return true;

            if (Language == null)
                return false;

            return Language.TechName.ToStringAlways().IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   Language.Name.ToStringAlways().IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeExtension(string Extension)
        {
            if (Extension.IsAbsent())
                return Extension;

            Extension = Extension.Trim();
            return Extension.StartsWith(".", StringComparison.Ordinal) ? Extension : "." + Extension;
        }
    }

    public class OutputTemplateLintIssue
    {
        public OutputTemplateLintIssue(OutputTemplateIssueSeverity Severity, string Message, string Owner = null)
        {
            this.Severity = Severity;
            this.Message = Message;
            this.Owner = Owner;
        }

        public OutputTemplateIssueSeverity Severity { get; private set; }

        public string Message { get; private set; }

        public string Owner { get; private set; }

        public override string ToString()
        {
            return this.Severity + ": " + (this.Owner.IsAbsent() ? "" : this.Owner + " - ") + this.Message;
        }
    }

    public class OutputTemplateLintResult
    {
        public OutputTemplateLintResult()
        {
            this.Issues = new List<OutputTemplateLintIssue>();
        }

        public int TemplatesInspected { get; set; }
        public int DocumentRoots { get; set; }
        public int SubTemplates { get; set; }
        public int DiagnosticsOrNotApplicable { get; set; }
        public int MissingSubTemplates { get; set; }
        public int DuplicateSubTemplates { get; set; }
        public IList<OutputTemplateLintIssue> Issues { get; private set; }

        public int Infos { get { return this.Issues.Count(Issue => Issue.Severity == OutputTemplateIssueSeverity.Info); } }
        public int Warnings { get { return this.Issues.Count(Issue => Issue.Severity == OutputTemplateIssueSeverity.Warning); } }
        public int Errors { get { return this.Issues.Count(Issue => Issue.Severity == OutputTemplateIssueSeverity.Error); } }
        public int Blocking { get { return this.Issues.Count(Issue => Issue.Severity == OutputTemplateIssueSeverity.Blocking); } }

        public void Add(OutputTemplateIssueSeverity Severity, string Message, string Owner = null)
        {
            this.Issues.Add(new OutputTemplateLintIssue(Severity, Message, Owner));
        }

        public string BuildSummary()
        {
            return "Templates inspected: " + this.TemplatesInspected + Environment.NewLine +
                   "Document roots: " + this.DocumentRoots + Environment.NewLine +
                   "Subtemplates: " + this.SubTemplates + Environment.NewLine +
                   "Diagnostics/NotApplicable: " + this.DiagnosticsOrNotApplicable + Environment.NewLine +
                   "Missing subtemplates: " + this.MissingSubTemplates + Environment.NewLine +
                   "Duplicate subtemplates: " + this.DuplicateSubTemplates + Environment.NewLine +
                   "Infos: " + this.Infos + Environment.NewLine +
                   "Warnings: " + this.Warnings + Environment.NewLine +
                   "Errors: " + this.Errors + Environment.NewLine +
                   "Blocking: " + this.Blocking;
        }

        public void LogToConsole()
        {
            Console.WriteLine("Output template lint summary:");
            Console.WriteLine(this.BuildSummary().Replace(Environment.NewLine, "; "));
            foreach (var Issue in this.Issues)
                Console.WriteLine("Output template lint " + Issue);
        }
    }

    public static class OutputTemplateLintService
    {
        private static readonly Regex InjectTagRegex =
            new Regex(@"\{%-?\s*inject\s+['""](?<name>[^'""]+)['""]", RegexOptions.IgnoreCase);

        private static readonly Regex BareAttributeRegex =
            new Regex(@"\s+[A-Za-z_:][A-Za-z0-9_\-:.]*\s*=\s*""\s*\{\{[^}]+\}\}\s*""", RegexOptions.Compiled);

        public static OutputTemplateLintResult Lint(OutputTemplatePreparationResult Preparation)
        {
            var Result = new OutputTemplateLintResult();
            if (Preparation == null)
                return Result;

            var Templates = new List<Tuple<string, string>>();
            if (!Preparation.CompositionTemplateText.IsAbsent())
                Templates.Add(Tuple.Create("composition=" + SafeTechName(Preparation.Composition), Preparation.CompositionTemplateText));

            foreach (var Pair in Preparation.PreparedDefinitionTemplateTexts.OrderBy(Item => SafeTechName(Item.Key), StringComparer.OrdinalIgnoreCase))
                Templates.Add(Tuple.Create(Pair.Key.ToString(), Pair.Value));

            var Declared = new Dictionary<string, List<Tuple<string, string>>>(StringComparer.OrdinalIgnoreCase);
            var References = new List<Tuple<string, string>>();

            foreach (var Item in Templates)
            {
                var Owner = Item.Item1;
                var Text = Item.Item2.NullDefault("");
                Result.TemplatesInspected++;

                var Directives = OutputTemplateDirectiveInfo.Parse(Text);
                if (Directives.Role == OutputTemplateRole.DocumentRoot || Directives.Role == OutputTemplateRole.Unknown)
                    Result.DocumentRoots++;
                else if (Directives.Role == OutputTemplateRole.SubTemplate || Directives.Role == OutputTemplateRole.Fragment)
                    Result.SubTemplates++;
                else if (Directives.Role == OutputTemplateRole.Diagnostic || Directives.Role == OutputTemplateRole.NotApplicable || Directives.Role == OutputTemplateRole.Disabled)
                    Result.DiagnosticsOrNotApplicable++;

                if (Text.IsAbsent())
                    Result.Add(OutputTemplateIssueSeverity.Warning, "Template body is empty.", Owner);

                if (Regex.IsMatch(Text, @"^\s+<\?xml", RegexOptions.CultureInvariant))
                    Result.Add(OutputTemplateIssueSeverity.Warning, "XML declaration is preceded by whitespace; XML post-processing will trim it for XML-like output.", Owner);

                if ((Directives.Role == OutputTemplateRole.SubTemplate || Directives.Role == OutputTemplateRole.Fragment) &&
                    Text.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase) >= 0)
                    Result.Add(OutputTemplateIssueSeverity.Warning, "Fragment/SubTemplate appears to contain a full XML document.", Owner);

                if (BareAttributeRegex.IsMatch(Text))
                    Result.Add(OutputTemplateIssueSeverity.Warning, "Template has XML attributes filled directly from expressions; use XmlAttr/EscapeXmlAttribute/DefaultIfEmpty helpers for null-sensitive attributes.", Owner);

                Dictionary<string, string> Sections = null;
                try
                {
                    Sections = FileGenerator.GetContainedTemplateTexts(Text);
                }
                catch (Exception Problem)
                {
                    Result.Add(OutputTemplateIssueSeverity.Blocking, "Cannot parse template sections. Problem: " + Problem.Message, Owner);
                    continue;
                }

                foreach (var Section in Sections.Where(Section => !Section.Key.IsAbsent()))
                {
                    List<Tuple<string, string>> Owners;
                    if (!Declared.TryGetValue(Section.Key, out Owners))
                    {
                        Owners = new List<Tuple<string, string>>();
                        Declared[Section.Key] = Owners;
                    }

                    Owners.Add(Tuple.Create(Owner, HashText(Section.Value)));
                }

                foreach (Match Match in InjectTagRegex.Matches(Text))
                {
                    var Name = Match.Groups["name"].Value;
                    if (!Name.IsAbsent())
                        References.Add(Tuple.Create(Owner, Name));
                }
            }

            foreach (var Pair in Declared.OrderBy(Item => Item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (Pair.Value.Count <= 1)
                    continue;

                Result.DuplicateSubTemplates++;
                var Hashes = Pair.Value.Select(Item => Item.Item2).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var Candidates = String.Join("; ", Pair.Value.Select(Item => Item.Item1 + " hash=" + Item.Item2.Substring(0, Math.Min(12, Item.Item2.Length))).ToArray());
                if (Hashes.Count == 1)
                    Result.Add(OutputTemplateIssueSeverity.Warning, "Duplicate subtemplate '" + Pair.Key + "' has identical bodies; deterministic first registration will be used. Candidates: " + Candidates);
                else
                    Result.Add(OutputTemplateIssueSeverity.Blocking, "Duplicate subtemplate '" + Pair.Key + "' has conflicting bodies. Candidates: " + Candidates);
            }

            foreach (var Reference in References)
                if (!Declared.ContainsKey(Reference.Item2))
                {
                    Result.MissingSubTemplates++;
                    Result.Add(OutputTemplateIssueSeverity.Blocking, "Missing required subtemplate: " + Reference.Item2, Reference.Item1);
                }

            foreach (var Reference in References)
                if (String.Equals(Reference.Item1, Reference.Item2, StringComparison.OrdinalIgnoreCase))
                    Result.Add(OutputTemplateIssueSeverity.Blocking, "Template appears to inject itself recursively: " + Reference.Item2, Reference.Item1);

            return Result;
        }

        public static string HashText(string Text)
        {
            using (var Hash = SHA256.Create())
            {
                var Bytes = Encoding.UTF8.GetBytes(Text.NullDefault(""));
                return BytesToHex(Hash.ComputeHash(Bytes));
            }
        }

        private static string BytesToHex(byte[] Bytes)
        {
            var Builder = new StringBuilder(Bytes.Length * 2);
            foreach (var Byte in Bytes)
                Builder.Append(Byte.ToString("x2", CultureInfo.InvariantCulture));
            return Builder.ToString();
        }

        private static string SafeTechName(Idea Source)
        {
            return Source == null ? "<none>" : Source.TechName.NullDefault(Source.Name).NullDefault("<unnamed>");
        }

        private static string SafeTechName(IdeaDefinition Source)
        {
            return Source == null ? "<none>" : Source.TechName.NullDefault(Source.Name).NullDefault("<unnamed>");
        }
    }

    public class OutputTemplateValidationResult
    {
        public bool ValidationRan { get; set; }
        public bool IsValid { get; set; }
        public string ValidationKind { get; set; }
        public string Message { get; set; }
    }

    public static class OutputTemplateDiagnostics
    {
        public static string HashText(string Text)
        {
            return OutputTemplateLintService.HashText(Text);
        }

        public static string ApplyPostProcessing(string Text, ExternalLanguageDeclaration Language, string FileName,
                                                 OutputTemplateDirectiveInfo Directives, IList<string> Notes)
        {
            Text = Text.NullDefault("");
            Directives = Directives ?? new OutputTemplateDirectiveInfo();
            var Original = Text;

            if (Directives.TrimLeadingWhitespace ||
                OutputTemplateDirectiveInfo.IsXmlLike(Language, FileName, Directives))
            {
                var Trimmed = Text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
                if (Trimmed != Text)
                {
                    Text = Trimmed;
                    AddNote(Notes, "trimLeadingWhitespace changed output");
                }
            }

            if (!Directives.NormalizeLineEndings.IsAbsent())
            {
                var Normalized = NormalizeLineEndings(Text, Directives.NormalizeLineEndings);
                if (Normalized != Text)
                {
                    Text = Normalized;
                    AddNote(Notes, "normalizeLineEndings=" + Directives.NormalizeLineEndings + " changed output");
                }
            }

            if (Directives.EnsureTrailingNewline && !Text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                Text += Environment.NewLine;
                AddNote(Notes, "ensureTrailingNewline changed output");
            }

            if (Original == Text)
                AddNote(Notes, "post-processing made no text changes");

            return Text;
        }

        public static OutputTemplateValidationResult ValidateRenderedText(string Text, ExternalLanguageDeclaration Language,
                                                                          string FileName, OutputTemplateDirectiveInfo Directives)
        {
            var Result = new OutputTemplateValidationResult();
            Text = Text.NullDefault("");
            Directives = Directives ?? new OutputTemplateDirectiveInfo();

            if (OutputTemplateDirectiveInfo.IsXmlLike(Language, FileName, Directives))
            {
                Result.ValidationRan = true;
                Result.ValidationKind = "XML";
                try
                {
                    var Settings = new XmlReaderSettings();
                    Settings.DtdProcessing = DtdProcessing.Ignore;
                    using (var Reader = XmlReader.Create(new StringReader(Text), Settings))
                        while (Reader.Read())
                        {
                        }

                    Result.IsValid = true;
                    Result.Message = "XML well-formed validation passed.";
                }
                catch (XmlException Problem)
                {
                    Result.IsValid = false;
                    Result.Message = "XML validation failed at line " + Problem.LineNumber + ", position " + Problem.LinePosition + ": " + Problem.Message;
                }
                catch (Exception Problem)
                {
                    Result.IsValid = false;
                    Result.Message = "XML validation failed: " + Problem.Message;
                }

                return Result;
            }

            if (OutputTemplateDirectiveInfo.IsJsonLike(Language, FileName, Directives))
            {
                Result.ValidationRan = true;
                Result.ValidationKind = "JSON";
                try
                {
                    var Serializer = new JavaScriptSerializer();
                    Serializer.MaxJsonLength = Int32.MaxValue;
                    Serializer.DeserializeObject(Text);
                    Result.IsValid = true;
                    Result.Message = "JSON parse validation passed.";
                }
                catch (Exception Problem)
                {
                    Result.IsValid = false;
                    Result.Message = "JSON parse validation failed: " + Problem.Message;
                }
            }

            return Result;
        }

        public static string BuildPreviewMetadata(Idea Source, ExternalLanguageDeclaration Language, string EffectiveTemplateText,
                                                  OutputTemplateDirectiveInfo Directives, GenerationResult RenderedResult,
                                                  OutputTemplatePreparationResult Preparation)
        {
            var Builder = new StringBuilder();
            Builder.AppendLine("target item name: " + Source.Name.ToStringAlways());
            Builder.AppendLine("target item techName: " + Source.TechName.ToStringAlways());
            Builder.AppendLine("target item id: " + Source.GlobalId);
            Builder.AppendLine("target item kind: " + TargetKind(Source));
            Builder.AppendLine("external language: " + Describe(Language));
            Builder.AppendLine("resolved template owner scope: " + OwnerScope(Source));
            Builder.AppendLine("resolved template owner techName: " + OwnerTechName(Source));
            Builder.AppendLine("template source collection: " + SourceCollection(Source));
            Builder.AppendLine("template role: " + (Directives == null ? OutputTemplateRole.Unknown : Directives.Role));
            Builder.AppendLine("template text length: " + EffectiveTemplateText.NullDefault("").Length);
            Builder.AppendLine("template hash: " + HashText(EffectiveTemplateText).Substring(0, 16));
            Builder.AppendLine("extends base template: " + ExtendsBaseTemplate(Source, Language));

            if (RenderedResult != null)
            {
                Builder.AppendLine("generated filename: " + RenderedResult.FileName);
                if (!RenderedResult.ValidationSummary.IsAbsent())
                    Builder.AppendLine("validation: " + RenderedResult.ValidationSummary);
                if (!RenderedResult.DiagnosticsText.IsAbsent())
                {
                    Builder.AppendLine("render diagnostics:");
                    Builder.AppendLine(RenderedResult.DiagnosticsText);
                }
            }

            if (Preparation != null)
            {
                Builder.AppendLine("preparation scope: " + Preparation.Scope);
                Builder.AppendLine("required subtemplates discovered: " + Preparation.SubtemplatesDiscovered);
                Builder.AppendLine("resolved subtemplates registered: " + Preparation.SubtemplatesRegistered);
                Builder.AppendLine("missing subtemplates: " + Preparation.MissingRequiredSubtemplates);
                Builder.AppendLine("lint warnings: " + Preparation.LintWarnings);
                Builder.AppendLine("lint errors: " + Preparation.LintErrors);
                Builder.AppendLine("lint blocking: " + Preparation.LintBlocking);
            }

            return Builder.ToString();
        }

        public static string BuildResolutionLog(Idea Source, ExternalLanguageDeclaration Language, string FilePath,
                                                string TemplateText, OutputTemplateDirectiveInfo Directives,
                                                bool IsSubtemplate, bool IsDocumentRoot, string OutputRole,
                                                GenerationResult Result)
        {
            Directives = Directives ?? OutputTemplateDirectiveInfo.Parse(TemplateText);
            var Builder = new StringBuilder();
            Builder.AppendLine("Output template resolution:");
            Builder.AppendLine("  file=" + FilePath.ToStringAlways());
            Builder.AppendLine("  generationScope=" + TargetKind(Source));
            Builder.AppendLine("  sourceItem=" + Source.Name.ToStringAlways());
            Builder.AppendLine("  sourceItemTechName=" + Source.TechName.ToStringAlways());
            Builder.AppendLine("  sourceItemId=" + Source.GlobalId);
            Builder.AppendLine("  language=" + Describe(Language));
            Builder.AppendLine("  templateName=" + OwnerTechName(Source) + " " + LanguageTechName(Language) + " Output Template");
            Builder.AppendLine("  templateTechName=" + (OwnerTechName(Source) + "_" + LanguageTechName(Language)).TextToIdentifier());
            Builder.AppendLine("  templateOwnerScope=" + OwnerScope(Source));
            Builder.AppendLine("  templateOwnerTechName=" + OwnerTechName(Source));
            Builder.AppendLine("  templateSourceCollection=" + SourceCollection(Source));
            Builder.AppendLine("  templateTextLength=" + TemplateText.NullDefault("").Length);
            Builder.AppendLine("  templateHash=" + HashText(TemplateText));
            Builder.AppendLine("  isSubtemplate=" + IsSubtemplate);
            Builder.AppendLine("  isDocumentRoot=" + IsDocumentRoot);
            Builder.AppendLine("  extendsBaseTemplate=" + ExtendsBaseTemplate(Source, Language));
            Builder.AppendLine("  outputRole=" + OutputRole.NullDefault(Directives.Role.ToString()));
            if (Result != null && !Result.ValidationSummary.IsAbsent())
                Builder.AppendLine("  validation=" + Result.ValidationSummary);
            return Builder.ToString();
        }

        public static string ResolveTargetFileName(string DefaultFileName, OutputTemplateDirectiveInfo Directives)
        {
            if (Directives == null)
                return DefaultFileName;

            if (!Directives.TargetFileName.IsAbsent())
                return Directives.TargetFileName;

            if (!Directives.TargetFileExtension.IsAbsent())
            {
                var BaseName = Path.GetFileNameWithoutExtension(DefaultFileName.NullDefault("output"));
                return BaseName + Directives.TargetFileExtension;
            }

            return DefaultFileName;
        }

        public static bool ShouldWriteUtf8NoBom(OutputTemplateDirectiveInfo Directives, ExternalLanguageDeclaration Language, string FileName)
        {
            return (Directives != null && Directives.WriteUtf8NoBom) ||
                   OutputTemplateDirectiveInfo.IsXmlLike(Language, FileName, Directives) ||
                   OutputTemplateDirectiveInfo.IsJsonLike(Language, FileName, Directives);
        }

        private static void AddNote(IList<string> Notes, string Text)
        {
            if (Notes != null)
                Notes.Add(Text);
        }

        private static string NormalizeLineEndings(string Text, string Mode)
        {
            var Normalized = Text.Replace("\r\n", "\n").Replace("\r", "\n");
            if (String.Equals(Mode, "CRLF", StringComparison.OrdinalIgnoreCase))
                return Normalized.Replace("\n", "\r\n");
            return Normalized;
        }

        private static string TargetKind(Idea Source)
        {
            if (Source is Composition)
                return "Composition";
            if (Source is Relationship)
                return "Relationship";
            if (Source is Concept)
                return "Concept";
            return "Idea";
        }

        private static string OwnerScope(Idea Source)
        {
            if (Source is Composition)
                return "composition-level";
            if (Source is Relationship)
                return "relationship-definition-level";
            if (Source is Concept)
                return "concept-definition-level";
            return "idea-definition-level";
        }

        private static string OwnerTechName(Idea Source)
        {
            if (Source == null)
                return "<none>";
            if (Source is Composition)
                return Source.TechName.NullDefault(Source.Name);
            return Source.IdeaDefinitor == null ? "<none>" : Source.IdeaDefinitor.TechName.NullDefault(Source.IdeaDefinitor.Name);
        }

        private static string SourceCollection(Idea Source)
        {
            if (Source is Composition)
                return "Domain.OutputTemplates / Composition.IdeaDefinitor";
            if (Source is Relationship)
                return "Domain.OutputTemplatesForRelationships + RelationshipDefinition.OutputTemplates";
            if (Source is Concept)
                return "Domain.OutputTemplatesForConcepts + ConceptDefinition.OutputTemplates";
            return "IdeaDefinition.OutputTemplates";
        }

        private static string Describe(ExternalLanguageDeclaration Language)
        {
            if (Language == null)
                return "<none>";
            return Language.Name.ToStringAlways() + " [" + Language.TechName.ToStringAlways() + "]";
        }

        private static string LanguageTechName(ExternalLanguageDeclaration Language)
        {
            return Language == null ? "Language" : Language.TechName.NullDefault(Language.Name).NullDefault("Language");
        }

        private static string ExtendsBaseTemplate(Idea Source, ExternalLanguageDeclaration Language)
        {
            if (Source == null || Source.IdeaDefinitor == null || Source.IdeaDefinitor.OutputTemplates == null || Language == null)
                return "unknown";

            var Template = Source.IdeaDefinitor.OutputTemplates.FirstOrDefault(Item => Item.Language == Language);
            return Template == null ? "inferred/default" : Template.ExtendsBaseTemplate.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static class OutputTemplateSafeText
    {
        public static string XmlAttribute(object Source)
        {
            return EscapeXml(Source);
        }

        public static string XmlText(object Source)
        {
            return EscapeXml(Source);
        }

        public static string EscapeXml(object Source)
        {
            return System.Security.SecurityElement.Escape(Source.ToStringAlways()).NullDefault("");
        }

        public static string JsonString(object Source)
        {
            var Text = Source.ToStringAlways();
            var Builder = new StringBuilder();
            Builder.Append('"');
            foreach (var Character in Text)
            {
                switch (Character)
                {
                    case '\\': Builder.Append("\\\\"); break;
                    case '"': Builder.Append("\\\""); break;
                    case '\r': Builder.Append("\\r"); break;
                    case '\n': Builder.Append("\\n"); break;
                    case '\t': Builder.Append("\\t"); break;
                    default:
                        if (Char.IsControl(Character))
                            Builder.Append("\\u" + ((int)Character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            Builder.Append(Character);
                        break;
                }
            }
            Builder.Append('"');
            return Builder.ToString();
        }

        public static string NormalizeTechName(object Source)
        {
            return Source.ToStringAlways().TextToIdentifier();
        }

        public static string DetailValue(object Source, string FieldName)
        {
            var Idea = Source as Idea;
            if (Idea == null || FieldName.IsAbsent())
                return "";

            foreach (var Table in Idea.Details.OfType<Table>())
            {
                if (Table.Records == null || Table.Records.Count < 1 || Table.Definition == null)
                    continue;

                var Field = Table.Definition.FieldDefinitions
                    .FirstOrDefault(Definition => String.Equals(Definition.TechName, FieldName, StringComparison.OrdinalIgnoreCase) ||
                                                  String.Equals(Definition.Name, FieldName, StringComparison.OrdinalIgnoreCase));
                if (Field == null)
                    continue;

                return Table.Records[0].GetFieldValueForExport(Field, false, true, true).ToStringAlways();
            }

            return "";
        }
    }
}
