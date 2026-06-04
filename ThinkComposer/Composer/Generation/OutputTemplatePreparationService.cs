using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Instrumind.Common;

using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;

namespace Instrumind.ThinkComposer.Composer.Generation
{
    public enum OutputTemplatePreparationScope
    {
        ActiveComposition,
        Selection,
        Domain
    }

    public enum OutputTemplatePreparationIssueSeverity
    {
        Warning,
        Error
    }

    public class OutputTemplatePreparationOptions
    {
        public OutputTemplatePreparationOptions()
        {
            this.Scope = OutputTemplatePreparationScope.ActiveComposition;
            this.MaterializeMissingDefinitionTemplates = true;
            this.ValidateSubtemplates = true;
            this.CommandName = "Output template preparation";
        }

        public OutputTemplatePreparationScope Scope { get; set; }

        public string CommandName { get; set; }

        public ExternalLanguageDeclaration Language { get; set; }

        public IEnumerable<Idea> SelectedIdeas { get; set; }

        public bool MaterializeMissingDefinitionTemplates { get; set; }

        public bool ValidateSubtemplates { get; set; }
    }

    public class OutputTemplatePreparationIssue
    {
        public OutputTemplatePreparationIssue(OutputTemplatePreparationIssueSeverity Severity, string Message, string OwnerName = null)
        {
            this.Severity = Severity;
            this.Message = Message;
            this.OwnerName = OwnerName;
        }

        public OutputTemplatePreparationIssueSeverity Severity { get; private set; }

        public string Message { get; private set; }

        public string OwnerName { get; private set; }

        public override string ToString()
        {
            return this.Severity.ToString() + ": " +
                   (this.OwnerName.IsAbsent() ? "" : this.OwnerName + " - ") +
                   this.Message;
        }
    }

    public class OutputTemplatePreparationResult
    {
        public OutputTemplatePreparationResult()
        {
            this.Issues = new List<OutputTemplatePreparationIssue>();
            this.PreparedDefinitions = new List<IdeaDefinition>();
            this.PreparedDefinitionTemplateTexts = new Dictionary<IdeaDefinition, string>();
        }

        public string CommandName { get; set; }

        public OutputTemplatePreparationScope Scope { get; set; }

        public Composition Composition { get; set; }

        public Domain Domain { get; set; }

        public ExternalLanguageDeclaration Language { get; set; }

        public string CompositionTemplateText { get; set; }

        public int ConceptDefinitionsInspected { get; set; }

        public int RelationshipDefinitionsInspected { get; set; }

        public int OutputTemplatesInspected { get; set; }

        public int TemplatesMaterialized { get; set; }

        public int ExternalLanguagesResolved { get; set; }

        public int SubtemplatesDiscovered { get; set; }

        public int SubtemplatesRegistered { get; set; }

        public int MissingTemplates { get; set; }

        public int MissingExternalLanguages { get; set; }

        public int MissingRequiredSubtemplates { get; set; }

        public int LintInfos { get; set; }

        public int LintWarnings { get; set; }

        public int LintErrors { get; set; }

        public int LintBlocking { get; set; }

        public IList<OutputTemplatePreparationIssue> Issues { get; private set; }

        public IList<IdeaDefinition> PreparedDefinitions { get; private set; }

        public IDictionary<IdeaDefinition, string> PreparedDefinitionTemplateTexts { get; private set; }

        public int Warnings
        {
            get { return this.Issues.Count(Issue => Issue.Severity == OutputTemplatePreparationIssueSeverity.Warning); }
        }

        public int Errors
        {
            get { return this.Issues.Count(Issue => Issue.Severity == OutputTemplatePreparationIssueSeverity.Error); }
        }

        public bool HasBlockingErrors
        {
            get { return this.Errors > 0; }
        }

        public string BuildSummary()
        {
            return "Concept definitions inspected: " + this.ConceptDefinitionsInspected + Environment.NewLine +
                   "Relationship definitions inspected: " + this.RelationshipDefinitionsInspected + Environment.NewLine +
                   "Templates inspected: " + this.OutputTemplatesInspected + Environment.NewLine +
                   "Templates prepared: " + this.TemplatesMaterialized + Environment.NewLine +
                   "External languages resolved: " + this.ExternalLanguagesResolved + Environment.NewLine +
                   "Subtemplates discovered: " + this.SubtemplatesDiscovered + Environment.NewLine +
                   "Subtemplates registered: " + this.SubtemplatesRegistered + Environment.NewLine +
                   "Missing required subtemplates: " + this.MissingRequiredSubtemplates + Environment.NewLine +
                   "Missing optional templates: " + this.MissingTemplates + Environment.NewLine +
                   "Missing external languages: " + this.MissingExternalLanguages + Environment.NewLine +
                   "Lint infos: " + this.LintInfos + Environment.NewLine +
                   "Lint warnings: " + this.LintWarnings + Environment.NewLine +
                   "Lint errors: " + this.LintErrors + Environment.NewLine +
                   "Lint blocking: " + this.LintBlocking + Environment.NewLine +
                   "Warnings: " + this.Warnings + Environment.NewLine +
                   "Errors: " + this.Errors;
        }

        public string BuildBlockingMessage()
        {
            return "Cannot generate Composition output." + Environment.NewLine +
                   String.Join(Environment.NewLine, this.Issues
                       .Where(Issue => Issue.Severity == OutputTemplatePreparationIssueSeverity.Error)
                       .Select(Issue => Issue.ToString()).ToArray()) +
                   Environment.NewLine + "No output was generated.";
        }

        public string BuildWarningsMessage()
        {
            return "Output template preparation completed with warnings." + Environment.NewLine +
                   this.BuildSummary() + Environment.NewLine +
                   "See log for details.";
        }

        public void AddWarning(string Message, string OwnerName = null)
        {
            this.Issues.Add(new OutputTemplatePreparationIssue(OutputTemplatePreparationIssueSeverity.Warning, Message, OwnerName));
        }

        public void AddError(string Message, string OwnerName = null)
        {
            this.Issues.Add(new OutputTemplatePreparationIssue(OutputTemplatePreparationIssueSeverity.Error, Message, OwnerName));
        }

        public void LogToConsole()
        {
            Console.WriteLine("Output template preparation completed:");
            Console.WriteLine("  command=" + this.CommandName);
            Console.WriteLine("  composition=" + Describe(this.Composition));
            Console.WriteLine("  domain=" + Describe(this.Domain));
            Console.WriteLine("  scope=" + this.Scope);
            Console.WriteLine("  language=" + Describe(this.Language));
            Console.WriteLine("  conceptDefinitions=" + this.ConceptDefinitionsInspected);
            Console.WriteLine("  relationshipDefinitions=" + this.RelationshipDefinitionsInspected);
            Console.WriteLine("  templatesInspected=" + this.OutputTemplatesInspected);
            Console.WriteLine("  templatesMaterialized=" + this.TemplatesMaterialized);
            Console.WriteLine("  externalLanguagesResolved=" + this.ExternalLanguagesResolved);
            Console.WriteLine("  subtemplatesDiscovered=" + this.SubtemplatesDiscovered);
            Console.WriteLine("  subtemplatesRegistered=" + this.SubtemplatesRegistered);
            Console.WriteLine("  missingRequiredSubtemplates=" + this.MissingRequiredSubtemplates);
            Console.WriteLine("  missingTemplates=" + this.MissingTemplates);
            Console.WriteLine("  missingExternalLanguages=" + this.MissingExternalLanguages);
            Console.WriteLine("  lintInfos=" + this.LintInfos + ", lintWarnings=" + this.LintWarnings +
                              ", lintErrors=" + this.LintErrors + ", lintBlocking=" + this.LintBlocking);
            Console.WriteLine("  warnings=" + this.Warnings + ", errors=" + this.Errors);

            foreach (var Issue in this.Issues)
                Console.WriteLine("Output template " + Issue.ToString());
        }

        private static string Describe(FormalElement Source)
        {
            if (Source == null)
                return "<none>";

            return Source.Name + " [" + Source.TechName + "] id=" + Source.GlobalId;
        }
    }

    public class OutputTemplatePreparationService
    {
        private static readonly Regex InjectTagRegex =
            new Regex(@"\{%-?\s*inject\s+['""](?<name>[^'""]+)['""]", RegexOptions.IgnoreCase);

        private OutputTemplatePreparationOptions Options;

        private OutputTemplatePreparationResult Result;

        private HashSet<string> DeclaredSubtemplates;

        private List<Tuple<string, string>> ReferencedSubtemplates;

        private OutputTemplatePreparationService(OutputTemplatePreparationOptions Options)
        {
            this.Options = Options ?? new OutputTemplatePreparationOptions();
            this.DeclaredSubtemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.ReferencedSubtemplates = new List<Tuple<string, string>>();
        }

        public static OutputTemplatePreparationResult PrepareComposition(Composition Composition, ExternalLanguageDeclaration Language, string CommandName = null)
        {
            var Options = new OutputTemplatePreparationOptions();
            Options.Scope = OutputTemplatePreparationScope.ActiveComposition;
            Options.Language = Language;
            Options.CommandName = CommandName.NullDefault("Generate Files");
            return (new OutputTemplatePreparationService(Options)).Prepare(Composition);
        }

        public static OutputTemplatePreparationResult PrepareSelection(Composition Composition, ExternalLanguageDeclaration Language, IEnumerable<Idea> SelectedIdeas, string CommandName = null)
        {
            var Options = new OutputTemplatePreparationOptions();
            Options.Scope = OutputTemplatePreparationScope.Selection;
            Options.Language = Language;
            Options.SelectedIdeas = SelectedIdeas;
            Options.CommandName = CommandName.NullDefault("Generation Preview");
            return (new OutputTemplatePreparationService(Options)).Prepare(Composition);
        }

        public static OutputTemplatePreparationResult PrepareDomain(Domain Domain, ExternalLanguageDeclaration Language, string CommandName = null)
        {
            var Options = new OutputTemplatePreparationOptions();
            Options.Scope = OutputTemplatePreparationScope.Domain;
            Options.Language = Language;
            Options.CommandName = CommandName.NullDefault("Refresh Output Templates");
            return (new OutputTemplatePreparationService(Options)).Prepare(null, Domain);
        }

        public static TextTemplate EnsureTemplateForLanguage(IList<TextTemplate> Templates, ExternalLanguageDeclaration Language, bool ExtendsBaseTemplate)
        {
            if (Templates == null || Language == null)
                return null;

            var Existing = Templates.FirstOrDefault(Template => SameLanguage(Template.Language, Language));
            if (Existing != null)
                return Existing;

            Existing = new TextTemplate(Language, "", ExtendsBaseTemplate);
            Templates.Add(Existing);
            return Existing;
        }

        public OutputTemplatePreparationResult Prepare(Composition Composition, Domain ExplicitDomain = null)
        {
            this.Result = new OutputTemplatePreparationResult();
            this.Result.CommandName = this.Options.CommandName;
            this.Result.Scope = this.Options.Scope;
            this.Result.Composition = Composition;

            var Domain = ExplicitDomain.NullDefault(Composition == null ? null : Composition.CompositeContentDomain);
            Domain = Domain.NullDefault(Composition == null ? null : Composition.CompositionDefinitor);
            this.Result.Domain = Domain;

            Console.WriteLine("Output template preparation started:");
            Console.WriteLine("  command=" + this.Options.CommandName);
            Console.WriteLine("  composition=" + Describe(Composition));
            Console.WriteLine("  domain=" + Describe(Domain));
            Console.WriteLine("  scope=" + this.Options.Scope);

            if (Domain == null)
            {
                this.Result.AddError("Engine cannot resolve the active composition/domain.");
                return this.Result;
            }

            Domain.DeclareOutputTemplatesCollection();
            Domain.DeclareExtraCollections();

            var Language = this.ResolveRequestedLanguage(Domain);
            this.Result.Language = Language;
            Console.WriteLine("  language=" + Describe(Language));

            if (Language == null)
            {
                this.Result.MissingExternalLanguages++;
                this.Result.AddError("Selected external language does not exist.");
                return this.Result;
            }

            if (Domain.CurrentExternalLanguage != Language)
            {
                Domain.CurrentExternalLanguage = Language;
                this.Result.ExternalLanguagesResolved++;
            }

            this.PrepareTemplateList(Domain.OutputTemplates, Language, false, false, "composition", Domain.ToString());
            this.PrepareTemplateList(Domain.OutputTemplatesForConcepts, Language, false, false, "domain concept base", Domain.ToString());
            this.PrepareTemplateList(Domain.OutputTemplatesForRelationships, Language, false, false, "domain relationship base", Domain.ToString());

            if (Composition != null)
            {
                this.Result.CompositionTemplateText = Composition.IdeaDefinitor.GetGenerationFinalTemplate(Language);
                this.CollectTemplateDiagnostics("composition=" + Composition.TechName, this.Result.CompositionTemplateText);
            }

            foreach (var Definition in this.GetDefinitionsToPrepare(Composition, Domain))
                this.PrepareDefinition(Definition, Language);

            this.ValidateSubtemplateReferences();
            this.RunLint();

            return this.Result;
        }

        private IEnumerable<IdeaDefinition> GetDefinitionsToPrepare(Composition Composition, Domain Domain)
        {
            var Result = new List<IdeaDefinition>();

            if (this.Options.Scope == OutputTemplatePreparationScope.Domain)
            {
                foreach (var Definition in Domain.ConceptDefinitions)
                    AddDefinition(Result, Definition);

                foreach (var Definition in Domain.RelationshipDefinitions)
                    AddDefinition(Result, Definition);

                return Result;
            }

            IEnumerable<Idea> Ideas = null;

            if (this.Options.Scope == OutputTemplatePreparationScope.Selection)
                Ideas = (this.Options.SelectedIdeas == null ? new Idea[0] : this.Options.SelectedIdeas);
            else
                Ideas = (Composition == null ? new Idea[0] : Composition.DeclaredIdeas);

            foreach (var Idea in Ideas)
                if (Idea != null)
                    AddDefinition(Result, Idea.IdeaDefinitor);

            return Result;
        }

        private static void AddDefinition(IList<IdeaDefinition> Definitions, IdeaDefinition Definition)
        {
            if (Definition == null || Definition is Domain || Definitions.Any(Item => Item == Definition))
                return;

            Definitions.Add(Definition);
        }

        private void PrepareDefinition(IdeaDefinition Definition, ExternalLanguageDeclaration Language)
        {
            Definition.DeclareOutputTemplatesCollection();

            if (Definition is ConceptDefinition)
                this.Result.ConceptDefinitionsInspected++;
            else
                if (Definition is RelationshipDefinition)
                    this.Result.RelationshipDefinitionsInspected++;

            this.PrepareTemplateList(Definition.OutputTemplates, Language, this.Options.MaterializeMissingDefinitionTemplates, true,
                                     Definition is ConceptDefinition ? "concept definition" : "relationship definition",
                                     Definition.ToString());

            var TemplateText = Definition.GetGenerationFinalTemplate(Language);
            if (TemplateText.IsAbsent())
            {
                this.Result.MissingTemplates++;
                this.Result.AddWarning("No output template text is available for language '" + Language.TechName + "'.", Definition.ToString());
            }

            this.Result.PreparedDefinitions.Add(Definition);
            this.Result.PreparedDefinitionTemplateTexts[Definition] = TemplateText;
            this.CollectTemplateDiagnostics(Definition.ToString(), TemplateText);

            Console.WriteLine("Output template prepared:");
            Console.WriteLine("  definition=" + Definition);
            Console.WriteLine("  language=" + Language.TechName);
            Console.WriteLine("  source=domain/definition output-template collections");
        }

        private TextTemplate PrepareTemplateList(IList<TextTemplate> Templates, ExternalLanguageDeclaration Language,
                                                 bool MaterializeMissing, bool ExtendsBaseTemplate, string OwnerKind, string OwnerName)
        {
            if (Templates == null)
            {
                this.Result.AddError("Template collection is not initialized.", OwnerName);
                return null;
            }

            foreach (var Template in Templates.ToArray())
            {
                this.Result.OutputTemplatesInspected++;

                if (Template.Language == null)
                {
                    this.Result.MissingExternalLanguages++;
                    this.Result.AddWarning("Output template has no external language reference.", OwnerName);
                    continue;
                }

                var ResolvedLanguage = this.ResolveLanguage(Template.Language, this.Result.Domain);
                if (ResolvedLanguage == null)
                {
                    this.Result.MissingExternalLanguages++;
                    this.Result.AddWarning("Output template language '" + Template.Language.TechName + "' is not in the active domain.", OwnerName);
                    continue;
                }

                if (Template.Language != ResolvedLanguage)
                {
                    Template.Language = ResolvedLanguage;
                    this.Result.ExternalLanguagesResolved++;
                }
            }

            var Existing = Templates.FirstOrDefault(Template => SameLanguage(Template.Language, Language));
            if (Existing != null)
                return Existing;

            if (!MaterializeMissing)
                return null;

            // Root cause: TemplateEditor.CurrentTemplate used to create this per-language slot as a UI side effect.
            // Generation now performs the same idempotent model preparation without opening the editor.
            Existing = EnsureTemplateForLanguage(Templates, Language, ExtendsBaseTemplate);
            this.Result.TemplatesMaterialized++;

            Console.WriteLine("Output template materialized:");
            Console.WriteLine("  owner=" + OwnerName);
            Console.WriteLine("  ownerKind=" + OwnerKind);
            Console.WriteLine("  language=" + Language.TechName);

            return Existing;
        }

        private ExternalLanguageDeclaration ResolveRequestedLanguage(Domain Domain)
        {
            var Candidate = this.ResolveLanguage(this.Options.Language, Domain);
            if (Candidate != null)
                return Candidate;

            Candidate = this.ResolveLanguage(Domain.GenerationConfiguration.Language, Domain);
            if (Candidate != null)
                return Candidate;

            Candidate = this.ResolveLanguage(Domain.CurrentExternalLanguage, Domain);
            if (Candidate != null)
                return Candidate;

            return Domain.ExternalLanguages.FirstOrDefault();
        }

        private ExternalLanguageDeclaration ResolveLanguage(ExternalLanguageDeclaration Candidate, Domain Domain)
        {
            if (Candidate == null || Domain == null || Domain.ExternalLanguages == null)
                return null;

            var Match = Domain.ExternalLanguages.FirstOrDefault(Language => Language == Candidate);
            if (Match != null)
                return Match;

            if (!Candidate.TechName.IsAbsent())
            {
                Match = Domain.ExternalLanguages.FirstOrDefault(Language => String.Equals(Language.TechName, Candidate.TechName, StringComparison.OrdinalIgnoreCase));
                if (Match != null)
                    return Match;
            }

            return null;
        }

        private static bool SameLanguage(ExternalLanguageDeclaration First, ExternalLanguageDeclaration Second)
        {
            if (First == null || Second == null)
                return false;

            if (First == Second)
                return true;

            return !First.TechName.IsAbsent() &&
                   !Second.TechName.IsAbsent() &&
                   String.Equals(First.TechName, Second.TechName, StringComparison.OrdinalIgnoreCase);
        }

        private void CollectTemplateDiagnostics(string OwnerName, string TemplateText)
        {
            if (TemplateText.IsAbsent() || !this.Options.ValidateSubtemplates)
                return;

            Dictionary<string, string> ContainedTemplates = null;

            try
            {
                ContainedTemplates = FileGenerator.GetContainedTemplateTexts(TemplateText);
            }
            catch (Exception Problem)
            {
                this.Result.AddError("Cannot read template sections. Problem: " + Problem.Message, OwnerName);
                return;
            }

            foreach (var TemplateName in ContainedTemplates.Keys.Where(Key => !Key.IsAbsent()))
            {
                if (this.DeclaredSubtemplates.Add(TemplateName))
                    this.Result.SubtemplatesDiscovered++;
            }

            foreach (Match Match in InjectTagRegex.Matches(TemplateText))
            {
                var TemplateName = Match.Groups["name"].Value;
                if (!TemplateName.IsAbsent())
                    this.ReferencedSubtemplates.Add(Tuple.Create(OwnerName, TemplateName));
            }
        }

        private void ValidateSubtemplateReferences()
        {
            if (!this.Options.ValidateSubtemplates)
                return;

            foreach (var Reference in this.ReferencedSubtemplates
                         .GroupBy(Item => Item.Item1 + "\t" + Item.Item2)
                .Select(Group => Group.First()))
                if (!this.DeclaredSubtemplates.Contains(Reference.Item2))
                {
                    this.Result.MissingRequiredSubtemplates++;
                    this.Result.AddError("Missing required subtemplate: " + Reference.Item2, Reference.Item1);
                }
        }

        private void RunLint()
        {
            var Lint = OutputTemplateLintService.Lint(this.Result);
            this.Result.LintInfos = Lint.Infos;
            this.Result.LintWarnings = Lint.Warnings;
            this.Result.LintErrors = Lint.Errors;
            this.Result.LintBlocking = Lint.Blocking;

            Lint.LogToConsole();

            foreach (var Issue in Lint.Issues)
                if (Issue.Severity == OutputTemplateIssueSeverity.Info)
                    Console.WriteLine("Output template lint info: " + Issue);
                else
                    if (Issue.Severity == OutputTemplateIssueSeverity.Warning)
                        this.Result.AddWarning(Issue.Message, Issue.Owner);
                    else
                        this.Result.AddError(Issue.Message, Issue.Owner);
        }

        private static string Describe(FormalElement Source)
        {
            if (Source == null)
                return "<none>";

            return Source.Name + " [" + Source.TechName + "] id=" + Source.GlobalId;
        }
    }
}
