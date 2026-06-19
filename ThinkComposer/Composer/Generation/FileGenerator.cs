// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------
//
// Project: Instrumind ThinkComposer v1.0
// File   : FileGenerator.cs
// Object : Instrumind.ThinkComposer.Composer.Generation.FileGenerator (Class)
//
// Date       Author             Changes
// ---------- ------------------ -------------------------------------------------------------
// 2013.01.09 Néstor Sánchez A.  Creation
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text;

using DotLiquid;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.EntityDefinition;
using Instrumind.Common.Visualization;
using Instrumind.Common.Visualization.Widgets;

using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;

/// Provides features for file generation.
namespace Instrumind.ThinkComposer.Composer.Generation
{
    /// <summary>
    /// Generates files from Compositions based on Idea content and their Definition template.
    /// </summary>
    public partial class FileGenerator
    {
        // -----------------------------------------------------------------------------------------
        public const string DEFAULT_INITIAL_TEMPLATE_NAME = ""; // This must be empty or at least enclosed in special chars

        static FileGenerator()
        {
            DotLiquid.Template.NamingConvention = new DotLiquid.NamingConventions.CSharpNamingConvention();
        }

        public static GenerationResult GenerateFilePreview(Idea Source, ExternalLanguageDeclaration Language,
                                                           bool FailWhenInvalid = false)
        {
            var TemplateText = Source.IdeaDefinitor.GetGenerationFinalTemplate(Language);
            var Result = GenerateFilePreview(Source, TemplateText, FailWhenInvalid);
            return Result;
        }

        public static GenerationResult GenerateFilePreview(Idea Source, string SourceTemplateText, bool FailWhenInvalid = false)
        {
            GenerationResult Result = null;
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            try
            {
                var CompiledTemplate = CreateCompiledTemplate(Source.IdeaDefinitor.ToString(), SourceTemplateText, FailWhenInvalid).Result;

                var Parameters = new RenderParameters();
                Parameters.LocalVariables = DotLiquid.Hash.FromAnonymousObject(Source);
                Parameters.RethrowErrors = true;

                var GeneratedOutput = CompiledTemplate.Render(Parameters);
                Result = new GenerationResult((Source.OwnerComposition.CompositeContentDomain.GenerationConfiguration.UseIdeaTechNameForFileNaming
                                               ? Source.TechName : Source.Name) + GenerationManager.DEFAULT_GEN_EXT, GeneratedOutput);
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }

            return Result;
        }

        /// <summary>
        /// Creates a compiled Template, plus possible sub-templates, from the supplied source-template-text.
        /// </summary>
        public static OperationResult<Template> CreateCompiledTemplate(string SourceDefName, string SourceTemplateText, bool FailWhenInvalid = false)
        {
            Template Result = null;

            try
            {
                var ContainedTemplateTexts = GetContainedTemplateTexts(SourceTemplateText);

                foreach(var Subtemplate in ContainedTemplateTexts)
                {
                    var CompiledTemplate = DotLiquid.Template.Parse(Subtemplate.Value);

                    if (Result == null) // If at the first one
                        Result = CompiledTemplate;

                    if (Subtemplate.Key != "")  // Register subtemplates with name (including the first-one/main if is not anonymous)
                        DotLiquid.Tags.Inject.RegisterSubTemplate(Subtemplate.Key, CompiledTemplate);
                }
            }
            catch (Exception Problem)
            {
                var Message = "Template from '" + SourceDefName + "' is invalid. Problem: " + Problem.Message;
                Console.WriteLine(Message);

                if (FailWhenInvalid)
                    throw new BusinessAnomaly(Message);

                return OperationResult.Failure<Template>(Message);
            }

            return OperationResult.Success(Result);
        }

        public static Dictionary<string,string> GetContainedTemplateTexts(string SourceTemplateText)
        {
            var Result = new Dictionary<string,string>();
            var Reader = new StringReader(SourceTemplateText);
            var Builder = new StringBuilder();
            var CurrentSubtemplateName = DEFAULT_INITIAL_TEMPLATE_NAME;
            var NewSubtemplateName = "";
            bool AddSubtemplate = false;
            string Line = null;

            do
            {
                Line = Reader.ReadLine();

                if (Line == null)
                    AddSubtemplate = true;
                else
                    if (Line.TrimStart().StartsWith(GenerationManager.GENPAR_PREFIX, StringComparison.Ordinal) &&
                        Line.TrimStart().Substring(GenerationManager.GENPAR_PREFIX.Length)
                            .TrimStart()
                            .StartsWith(GenerationManager.GENKEY_SEC_SUBTEMPLATE + GenerationManager.GENPAR_ASSIGN, StringComparison.OrdinalIgnoreCase))
                    {
                        var Trimmed = Line.TrimStart();
                        var AssignIndex = Trimmed.IndexOf(GenerationManager.GENPAR_ASSIGN, StringComparison.Ordinal);
                        NewSubtemplateName = (AssignIndex < 0 ? "" : Trimmed.Substring(AssignIndex + GenerationManager.GENPAR_ASSIGN.Length).Trim());
                        if (NewSubtemplateName.IsAbsent())
                            throw new UsageAnomaly("Subtemplate has no name declared.");

                        AddSubtemplate = true;
                    }
                    else
                        Builder.AppendLine(Line);

                if (AddSubtemplate)
                {
                    AddSubtemplate = false;

                    if (Builder.Length > 0)
                    {
                        if (Result.ContainsKey(CurrentSubtemplateName))
                            throw new UsageAnomaly("Subtemplate '" + CurrentSubtemplateName + "' is declared more than once in the same template.");

                        Result.Add(CurrentSubtemplateName, Builder.ToString());
                        Builder.Clear();
                    }

                    CurrentSubtemplateName = NewSubtemplateName;
                }

            } while (Line != null);

            return Result;
        }

        // -----------------------------------------------------------------------------------------
        public Composition SourceComposition { get; protected set; }
        public ExternalLanguageDeclaration Language { get; protected set; }
        public FileGenerationConfiguration Configuration { get; protected set; }

        private Template CompositionTemplate = null;
        private Dictionary<IdeaDefinition, Template> CompiledTemplates = new Dictionary<IdeaDefinition, Template>();
        private OutputTemplatePreparationResult PreparationResult = null;
        private int DocumentRootTemplatesRendered = 0;
        private int SuppressedFragmentTemplates = 0;
        private int ValidationWarnings = 0;
        private int ValidationErrors = 0;
        private int XmlValid = 0;
        private int XmlInvalid = 0;
        private int JsonValid = 0;
        private int JsonInvalid = 0;

        private ThreadWorker<int> CurrentWorker { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public FileGenerator(Composition SourceComposition, ExternalLanguageDeclaration Language, FileGenerationConfiguration NewConfiguration,
                             OutputTemplatePreparationResult PreparationResult = null)
        {
            this.SourceComposition = SourceComposition;
            this.Language = Language;
            this.PreparationResult = PreparationResult;

            if (NewConfiguration == null)
                NewConfiguration = new FileGenerationConfiguration();

            this.Configuration = NewConfiguration;
        }

        public GenerationResult GeneratePreview(Idea SourceIdea)
        {
            General.ContractRequiresNotNull(SourceIdea);

            var Preparation = this.PreparationResult;
            var OwnerComposition = SourceIdea as Composition;
            if (OwnerComposition == null)
                OwnerComposition = SourceIdea.OwnerComposition;
            if (Preparation == null)
                Preparation = (SourceIdea is Composition
                               ? OutputTemplatePreparationService.PrepareComposition(OwnerComposition, this.Language, "Generation Preview")
                               : OutputTemplatePreparationService.PrepareSelection(OwnerComposition, this.Language, new Idea[] { SourceIdea }, "Generation Preview"));

            if (Preparation.HasBlockingErrors)
                throw new BusinessAnomaly(Preparation.BuildBlockingMessage());

            this.Language = Preparation.Language;
            this.CompilePreparedTemplates(Preparation);

            var TemplateText = this.ResolveTemplateText(SourceIdea, Preparation);
            var CompiledTemplate = this.ResolveCompiledTemplate(SourceIdea, TemplateText);
            if (CompiledTemplate == null)
                return new GenerationResult(SourceIdea.TechName + GenerationManager.DEFAULT_GEN_EXT, "");

            return this.RenderIdea(SourceIdea, CompiledTemplate, TemplateText, null, false);
        }

        /// <summary>
        /// Generates Files based on the current Configuration.
        /// Returns operation-result.
        /// </summary>
        public OperationResult<int> Generate(ThreadWorker<int> Worker)
        {
            General.ContractRequiresNotNull(Worker);
            var GeneratedFiles = 0;

            try
            {
                this.CurrentWorker = Worker;
                this.CurrentWorker.ReportProgress(0, "Starting.");

                this.CurrentWorker.ReportProgress(1, "Preparing output templates.");
                var Preparation = this.PreparationResult;
                if (Preparation == null)
                    Preparation = OutputTemplatePreparationService.PrepareComposition(this.SourceComposition, this.Language, "Generate Files");

                if (Preparation.HasBlockingErrors)
                {
                    Preparation.LogToConsole();
                    return OperationResult.Failure<int>(Preparation.BuildBlockingMessage(), Result: GeneratedFiles);
                }

                this.Language = Preparation.Language;

                this.CurrentWorker.ReportProgress(2, "Compiling output templates.");
                this.CompilePreparedTemplates(Preparation);
                Preparation.LogToConsole();

                // Determine excluded ideas
                var ExcludedIdeas = this.SourceComposition.DeclaredIdeas.Where(idea => idea.GlobalId.ToString().IsIn(this.Configuration.ExcludedIdeasGlobalIds)).ToArray();

                // Travel subgraph
                var WorkingDirExists = false;
                GeneratedFiles = this.GenerateIdeaFiles(this.SourceComposition, ExcludedIdeas, 1.0, 99.0,
                                                        this.Configuration.TargetDirectory, ref WorkingDirExists,
                                                        this.Configuration.CreateCompositionRootDirectory);

                // Finish
                this.CurrentWorker.ReportProgress(100, "Generation complete.");
                this.CurrentWorker = null;
            }
            catch (Exception Problem)
            {
                this.CurrentWorker = null;
                return OperationResult.Failure<int>("Cannot complete generation.\nProblem: " + Problem.Message, Result: GeneratedFiles);
            }

            return OperationResult.Success<int>(GeneratedFiles, BuildGenerationSummary(GeneratedFiles));
        }

        private void CompilePreparedTemplates(OutputTemplatePreparationResult Preparation)
        {
            this.CompositionTemplate = null;
            this.CompiledTemplates.Clear();
            DotLiquid.Tags.Inject.ClearRegisteredSubTemplates(DotLiquid.Tags.Inject.CurrentConsumerContextId);
            Console.WriteLine("Output template subtemplate registry cleared for consumer context=" +
                              DotLiquid.Tags.Inject.CurrentConsumerContextId.ToStringAlways());

            var CompoTextTemplate = (Preparation == null
                                     ? this.SourceComposition.IdeaDefinitor.GetGenerationFinalTemplate(this.Language)
                                     : Preparation.CompositionTemplateText);
            if (!CompoTextTemplate.IsAbsent())
            {
                this.CompositionTemplate = CreateCompiledTemplate(this.SourceComposition.CompositeContentDomain.ToString(), CompoTextTemplate, true).Result;
                if (Preparation != null)
                {
                    Preparation.SubtemplatesRegistered += CountDeclaredSubtemplates(CompoTextTemplate);
                    LogSubtemplateRegistry(this.SourceComposition.TechName, CompoTextTemplate);
                }
            }

            if (Preparation == null)
                return;

            foreach (var PreparedTemplate in Preparation.PreparedDefinitionTemplateTexts
                         .OrderBy(Item => Item.Key is ConceptDefinition ? "conceptDefinition" : "relationshipDefinition", StringComparer.OrdinalIgnoreCase)
                         .ThenBy(Item => Item.Key.TechName.NullDefault(Item.Key.Name), StringComparer.OrdinalIgnoreCase))
            {
                if (PreparedTemplate.Value.IsAbsent())
                    continue;

                var CompiledTemplate = CreateCompiledTemplate(PreparedTemplate.Key.ToString(), PreparedTemplate.Value, true).Result;
                this.CompiledTemplates.AddOrReplace(PreparedTemplate.Key, CompiledTemplate);
                Preparation.SubtemplatesRegistered += CountDeclaredSubtemplates(PreparedTemplate.Value);
                LogSubtemplateRegistry(PreparedTemplate.Key.ToString(), PreparedTemplate.Value);
            }
        }

        private static int CountDeclaredSubtemplates(string TemplateText)
        {
            if (TemplateText.IsAbsent())
                return 0;

            return GetContainedTemplateTexts(TemplateText).Keys.Count(Key => !Key.IsAbsent());
        }

        // -----------------------------------------------------------------------------------------
        public int GenerateIdeaFiles(Idea SourceIdea, IEnumerable<Idea> ExcludedIdeas, double ProgressPercentageStart, double ProgressPercentageEnd,
                                     string WorkingDirectory, ref bool WorkingDirExists, bool CreateContentDir = true)
        {
            // Generate file, if selected
            var FilesGenerated = 0;
            var FileName = SourceIdea.TechName;

            if (!(SourceIdea.IsIn(ExcludedIdeas) || ((SourceIdea is Relationship) && !this.Configuration.GenerateFilesForRelationships)))
            {
                // Determine compiled-template to use
                var TemplateText = this.ResolveTemplateText(SourceIdea, this.PreparationResult);
                var Directives = OutputTemplateDirectiveInfo.Parse(TemplateText);

                // Apply template to source Idea
                if (!Directives.ShouldEmitStandalone)
                {
                    this.SuppressedFragmentTemplates++;
                    Console.WriteLine("Output template standalone generation suppressed:");
                    Console.WriteLine("  sourceItem=" + SourceIdea.TechName);
                    Console.WriteLine("  role=" + Directives.Role);
                    Console.WriteLine("  reason=Fragment/SubTemplate/Disabled/NotApplicable templates are not emitted as deliverables by default.");
                }
                else
                {
                    var CompiledTemplate = this.ResolveCompiledTemplate(SourceIdea, TemplateText);
                    if (CompiledTemplate != null)
                    {
                        var GeneratedResult = this.RenderIdea(SourceIdea, CompiledTemplate, TemplateText, WorkingDirectory, true);

                        // Create directory and save file, if needed
                        if (!WorkingDirExists)
                        {
                            if (!Directory.Exists(WorkingDirectory))
                                Directory.CreateDirectory(WorkingDirectory);

                            WorkingDirExists = true;
                        }

                        FileName = GeneratedResult.FileName;
                        var GenerationPath = Path.Combine(WorkingDirectory, FileName);
                        this.WriteGeneratedFile(GenerationPath, GeneratedResult.GeneratedText,
                                                OutputTemplateDirectiveInfo.Merge(Directives, GeneratedResult.Parameters));

                        FilesGenerated = 1;
                    }
                }
            }

            // Determine Ideas to generate
            if (SourceIdea.CompositeIdeas.Count > 0)
            {
                var SelectedIdeas = SourceIdea.CompositeIdeas.Where(ideasel => !ideasel.IsIn(ExcludedIdeas) || ideasel.CompositeIdeas.Count > 0
                                                                               || (this.Configuration.GenerateFilesForRelationships && ideasel is Relationship)).ToList();

                // Create content directory
                var ContentDirectory = (CreateContentDir
                                        ? Path.Combine(WorkingDirectory, Path.GetFileNameWithoutExtension(FileName) + this.Configuration.CompositeContentSubdirSuffix.Trim())
                                        : WorkingDirectory);
                var ContentDirExists = !CreateContentDir;

                // Determine progress
                var ProgressStep = ((ProgressPercentageEnd - ProgressPercentageStart) / (double)SelectedIdeas.Count);
                var ProgressPercentage = ProgressPercentageStart; 
                
                // Travel composites
                foreach (var SelectedIdea in SelectedIdeas)
                {
                    ProgressPercentage += ProgressStep;
                    FilesGenerated += GenerateIdeaFiles(SelectedIdea, ExcludedIdeas, ProgressPercentage, (ProgressPercentage + ProgressStep),
                                                        ContentDirectory, ref ContentDirExists);
                }
            }

            return FilesGenerated;
        }

        private Template ResolveCompiledTemplate(Idea SourceIdea, string TemplateText)
        {
            if (TemplateText.IsAbsent())
                return null;

            if (SourceIdea is Composition)
                return this.CompositionTemplate ?? CreateCompiledTemplate(SourceIdea.ToString(), TemplateText, true).Result;

            Template CompiledTemplate = null;
            if (this.CompiledTemplates.TryGetValue(SourceIdea.IdeaDefinitor, out CompiledTemplate))
                return CompiledTemplate;

            CompiledTemplate = CreateCompiledTemplate(SourceIdea.IdeaDefinitor.ToString(), TemplateText, true).Result;
            this.CompiledTemplates.Add(SourceIdea.IdeaDefinitor, CompiledTemplate);
            return CompiledTemplate;
        }

        private string ResolveTemplateText(Idea SourceIdea, OutputTemplatePreparationResult Preparation)
        {
            if (SourceIdea is Composition)
                return (Preparation == null ? SourceIdea.IdeaDefinitor.GetGenerationFinalTemplate(this.Language) : Preparation.CompositionTemplateText);

            if (Preparation != null && Preparation.PreparedDefinitionTemplateTexts.ContainsKey(SourceIdea.IdeaDefinitor))
                return Preparation.PreparedDefinitionTemplateTexts[SourceIdea.IdeaDefinitor];

            return SourceIdea.IdeaDefinitor.GetGenerationFinalTemplate(this.Language);
        }

        private GenerationResult RenderIdea(Idea SourceIdea, Template CompiledTemplate, string TemplateText, string WorkingDirectory, bool IsFileGeneration)
        {
            var TemplateDirectives = OutputTemplateDirectiveInfo.Parse(TemplateText);
            var GeneratedOutput = CompiledTemplate.Render(DotLiquid.Hash.FromAnonymousObject(SourceIdea));
            var DefaultFileName = (this.Configuration.UseIdeaTechNameForFileNaming
                                   ? SourceIdea.TechName : SourceIdea.Name) + GenerationManager.DEFAULT_GEN_EXT;
            var GeneratedResult = new GenerationResult(DefaultFileName, GeneratedOutput);
            var Directives = OutputTemplateDirectiveInfo.Merge(TemplateDirectives, GeneratedResult.Parameters);
            GeneratedResult.FileName = OutputTemplateDiagnostics.ResolveTargetFileName(GeneratedResult.FileName, Directives);

            var Notes = new List<string>();
            var PostProcessed = OutputTemplateDiagnostics.ApplyPostProcessing(GeneratedResult.GeneratedText, this.Language,
                                                                              GeneratedResult.FileName, Directives, Notes);
            GeneratedResult.ReplaceGeneratedText(PostProcessed);

            var Validation = OutputTemplateDiagnostics.ValidateRenderedText(GeneratedResult.GeneratedText, this.Language,
                                                                            GeneratedResult.FileName, Directives);
            if (Validation.ValidationRan)
            {
                GeneratedResult.ValidationSummary = Validation.Message;
                if (Validation.IsValid)
                {
                    if (Validation.ValidationKind == "XML")
                        this.XmlValid++;
                    else if (Validation.ValidationKind == "JSON")
                        this.JsonValid++;
                }
                else
                {
                    this.ValidationWarnings++;
                    this.ValidationErrors++;
                    if (Validation.ValidationKind == "XML")
                        this.XmlInvalid++;
                    else if (Validation.ValidationKind == "JSON")
                        this.JsonInvalid++;
                }
            }

            if (Notes.Count > 0)
                GeneratedResult.DiagnosticsText = String.Join(Environment.NewLine, Notes.ToArray());

            var GenerationPath = WorkingDirectory.IsAbsent() ? GeneratedResult.FileName : Path.Combine(WorkingDirectory, GeneratedResult.FileName);
            Console.WriteLine(OutputTemplateDiagnostics.BuildResolutionLog(SourceIdea, this.Language, GenerationPath, TemplateText,
                                                                           Directives, Directives.Role == OutputTemplateRole.SubTemplate,
                                                                           Directives.Role == OutputTemplateRole.DocumentRoot ||
                                                                           Directives.Role == OutputTemplateRole.Unknown,
                                                                           Directives.Role.ToString(), GeneratedResult));

            if (IsFileGeneration)
                this.DocumentRootTemplatesRendered++;

            return GeneratedResult;
        }

        private void WriteGeneratedFile(string FilePath, string Text, OutputTemplateDirectiveInfo Directives)
        {
            if (OutputTemplateDiagnostics.ShouldWriteUtf8NoBom(Directives, this.Language, FilePath))
                File.WriteAllText(FilePath, Text.NullDefault(""), new UTF8Encoding(false));
            else
                General.StringToFile(FilePath, Text.NullDefault(""));
        }

        private void LogSubtemplateRegistry(string OwnerName, string TemplateText)
        {
            Dictionary<string, string> Sections = null;
            try
            {
                Sections = GetContainedTemplateTexts(TemplateText);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Output template subtemplate registry skipped for " + OwnerName + ": " + Problem.Message);
                return;
            }

            foreach (var Section in Sections.Where(Section => !Section.Key.IsAbsent()).OrderBy(Section => Section.Key, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine("Output template subtemplates registered: " + Section.Key + " -> " +
                                  OwnerName + " hash=" + OutputTemplateDiagnostics.HashText(Section.Value).Substring(0, 16));
        }

        private string BuildGenerationSummary(int GeneratedFiles)
        {
            var Builder = new StringBuilder();
            Builder.AppendLine("Files generated: " + GeneratedFiles.ToString());
            Builder.AppendLine("Root templates rendered: " + this.DocumentRootTemplatesRendered.ToString());
            Builder.AppendLine("Fragment/subtemplates suppressed: " + this.SuppressedFragmentTemplates.ToString());
            Builder.AppendLine("XML valid: " + this.XmlValid.ToString());
            Builder.AppendLine("XML invalid: " + this.XmlInvalid.ToString());
            Builder.AppendLine("JSON valid: " + this.JsonValid.ToString());
            Builder.AppendLine("JSON invalid: " + this.JsonInvalid.ToString());
            Builder.AppendLine("Validation warnings: " + this.ValidationWarnings.ToString());
            Builder.AppendLine("Validation errors: " + this.ValidationErrors.ToString());
            Builder.AppendLine("Generated at:");
            Builder.AppendLine(this.Configuration.TargetDirectory);
            return Builder.ToString();
        }

        // -----------------------------------------------------------------------------------------
    }
}
