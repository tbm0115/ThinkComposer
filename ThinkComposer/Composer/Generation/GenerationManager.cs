using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using DotLiquid;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.EntityDefinition;
using Instrumind.Common.Visualization;
using Instrumind.Common.Visualization.Widgets;

using Instrumind.ThinkComposer.Composer.Generation.Widgets;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;

namespace Instrumind.ThinkComposer.Composer.Generation
{
    static class GenerationManager
    {
        public const string GENPAR_PREFIX = "%%:";
        public const string GENPAR_ASSIGN = "=";

        public const string GENKEY_VAR_FILENAME = "FILENAME";
        public const string GENKEY_SEC_SUBTEMPLATE = "SUBTEMPLATE";
        public const string GENKEY_POS_EXTENSION = "[EXTENSIONPLACE]";

        public const string DEFAULT_GEN_EXT = ".txt";

        public static void SetCurrentGenerationConsumer(Composition CurrentComposition)
        {
            DotLiquid.Tags.Inject.CurrentConsumerContextId = (CurrentComposition == null ? null : CurrentComposition.GlobalId.ToString());
        }

        public static void GenerateGlobalFiles(Composition Source)
        {
            if (Source == null || Source.EditEngine == null)
                return;

            if (!ProductDirector.ValidateEditionPermission(AppExec.LIC_EDITION_PROFESSIONAL, "Generate Files", false, new DateTime(2013, 6, 22)))
                return;

            if (!GenerationManager.ConfigureAndStartGeneration(Source.OwnerComposition))
                return;

            var Title = "File Generation from " + Source.Name;
            var Configuration = Source.OwnerComposition.CompositeContentDomain.GenerationConfiguration;
            var Preparation = OutputTemplatePreparationService.PrepareComposition(Source.OwnerComposition, Configuration.Language, "Generate Files");

            if (GenerationManager.ShowPreparationIssues(Preparation, true))
                return;

            Preparation.LogToConsole();
            Configuration.Language = Preparation.Language;
            Source.OwnerComposition.CompositeContentDomain.CurrentExternalLanguage = Preparation.Language;

            var Generator = new FileGenerator(Source.OwnerComposition, Preparation.Language, Configuration, Preparation);

            EntityEditEngine.ActiveEntityEditor.ReadTechNamesAsProgramIdentifiers =
                    Source.CompositeContentDomain.GenerationConfiguration.UseTechNamesAsProgramIdentifiers; ;

            var Started = ProgressiveThreadedExecutor<int>.Execute(Title, Generator.Generate,
                (opresult) =>
                {
                    EntityEditEngine.ActiveEntityEditor.ReadTechNamesAsProgramIdentifiers = false;

                    if (!opresult.WasSuccessful)
                    {
                        Display.DialogMessage("Generation not completed!", opresult.Message, EMessageType.Warning);
                        return;
                    }

                    if (opresult.Message != null)
                        Console.WriteLine(opresult.Message);

                    Display.DialogMessage("Generation completed", opresult.Message.AbsentDefault("File Generation successfully executed."));
                });

            if (!Started)
                EntityEditEngine.ActiveEntityEditor.ReadTechNamesAsProgramIdentifiers = false;
        }
        
        public static bool ConfigureAndStartGeneration(Composition SourceComposition)
        {
            var InstanceController = EntityInstanceController.AssignInstanceController(SourceComposition.CompositeContentDomain.GenerationConfiguration);
            InstanceController.StartEdit();

            var ConfigPanel = new GeneratorSelectionEditor(SourceComposition);
            InstanceController.PostValidate = ((curr, prev) =>
                                               {
                                                   if (curr.TargetDirectory.IsAbsent() || !Directory.Exists(curr.TargetDirectory))
                                                       return  "Target directory not set or not found".IntoList();

                                                   return null;
                                               });
            InstanceController.PreApply = ((curr, prev, args) =>
                {
                    curr.ExcludedIdeasGlobalIds.Clear();
                    curr.ExcludedIdeasGlobalIds.AddRange(ConfigPanel.SourceConfiguration.CurrentSelection.GetSelection(false)   // Gets the not-selected Ideas
                                                                .Select(sel => sel.SourceIdea.GlobalId.ToString()));
                    if (curr.Language != null)
                        SourceComposition.CompositeContentDomain.CurrentExternalLanguage = curr.Language;
                    return true;
                });

            var EditPanel = Display.CreateEditPanel(SourceComposition.CompositeContentDomain.GenerationConfiguration, ConfigPanel);

            var Result = InstanceController.Edit(EditPanel, "Generate Files", true, null, 700, 500).IsTrue();

            return Result;
        }

        public static void ShowGenerationFilePreview(Idea Source)
        {
            if (Source == null || Source.EditEngine == null)
                return;

            if (!ProductDirector.ValidateEditionPermission(AppExec.LIC_EDITION_PROFESSIONAL, "Generate Files", false, new DateTime(2013, 6, 22)))
                return;

            try
            {
                EntityEditEngine.ActiveEntityEditor.ReadTechNamesAsProgramIdentifiers =
                    Source.OwnerComposition.CompositeContentDomain.GenerationConfiguration.UseTechNamesAsProgramIdentifiers; ;

                var Language = Source.IdeaDefinitor.OwnerDomain.ExternalLanguages.FirstOrDefault()
                                        .NullDefault(Source.IdeaDefinitor.OwnerDomain.CurrentExternalLanguage);

                if (Source.IdeaDefinitor.OwnerDomain.ExternalLanguages.Count > 1)
                {
                    var LangTechName = Display.DialogMultiOption("Language selection", "Select the Language used to generate the preview", null, null, true,
                                                                 Source.IdeaDefinitor.OwnerDomain.CurrentExternalLanguage.TechName,
                                                                 Source.IdeaDefinitor.OwnerDomain.ExternalLanguages.ToArray());
                    if (LangTechName.IsAbsent())
                        return;

                    Language = Source.IdeaDefinitor.OwnerDomain.ExternalLanguages.First(lang => lang.TechName == LangTechName);
                    Source.IdeaDefinitor.OwnerDomain.CurrentExternalLanguage = Language;
                }

                var Preparation = OutputTemplatePreparationService.PrepareSelection(Source.OwnerComposition, Language, new Idea[] { Source }, "Generation Preview");
                if (GenerationManager.ShowPreparationIssues(Preparation, true))
                    return;

                Preparation.LogToConsole();
                Language = Preparation.Language;
                var Preview = FileGenerator.GenerateFilePreview(Source, Language);

                DialogOptionsWindow GenDialog = null;
                var Previewer = new FileGenerationPreviewer(Preview.FileName, Preview.GeneratedText);
                Display.OpenContentDialogWindow(ref GenDialog, "Generate from: " + Source.Name,
                                                Previewer, 600, 700);
            }
            catch (Exception Problem)
            {
                Display.DialogMessage("Error!", "Cannot generate preview from the supplied Template and source object.\n\nProblem: " +
                                      Problem.Message, EMessageType.Warning);
                return;
            }
            finally
            {
                EntityEditEngine.ActiveEntityEditor.ReadTechNamesAsProgramIdentifiers = false;
            }
        }

        public static void RefreshOutputTemplates(Composition Source)
        {
            if (Source == null || Source.EditEngine == null)
                return;

            var Configuration = Source.CompositeContentDomain.GenerationConfiguration;
            var Language = Configuration.Language.NullDefault(Source.CompositeContentDomain.CurrentExternalLanguage);
            var Preparation = OutputTemplatePreparationService.PrepareComposition(Source, Language, "Refresh Output Templates");

            Preparation.LogToConsole();

            if (Preparation.HasBlockingErrors)
            {
                Display.DialogMessage("Cannot refresh Output Templates", Preparation.BuildBlockingMessage(), EMessageType.Warning);
                return;
            }

            Configuration.Language = Preparation.Language;
            Source.CompositeContentDomain.CurrentExternalLanguage = Preparation.Language;

            Display.DialogMessage("Output Templates Refreshed",
                                  Preparation.Warnings > 0 ? Preparation.BuildWarningsMessage() : Preparation.BuildSummary());
        }

        private static bool ShowPreparationIssues(OutputTemplatePreparationResult Preparation, bool AbortOnErrors)
        {
            if (Preparation == null)
                return false;

            if (Preparation.HasBlockingErrors)
            {
                Display.DialogMessage("Cannot generate Composition output", Preparation.BuildBlockingMessage(), EMessageType.Warning);
                return AbortOnErrors;
            }

            if (Preparation.Warnings > 0)
                Display.DialogMessage("Output template preparation completed with warnings",
                                      Preparation.BuildWarningsMessage(), EMessageType.Warning);

            return false;
        }
    }
}
