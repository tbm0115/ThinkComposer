// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// User-facing commands for Domain JSON and embedded Domain update.
// -------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public static class DomainJsonInterchangeCommands
    {
        public const string DomainJsonExtension = "tdom.json";
        public const string DomainJsonFilter = "Domain JSON (*.tdom.json;*.json)|*.tdom.json;*.json";
        public const string DomainUpdateFilter = "Domain source (*.tdom;*.tdom.json;*.json)|*.tdom;*.tdom.json;*.json";

        public static void ExportDomainJson(WorkspaceManager WorkspaceDirector)
        {
            var Engine = WorkspaceDirector == null ? null : WorkspaceDirector.ActiveDocumentEngine as CompositionEngine;
            var TargetDomain = ActiveDomain(Engine);
            if (TargetDomain == null)
                return;

            var InitialRoute = Path.Combine(AppExec.UserDataDirectory, SafeFileName(TargetDomain.TechName) + "." + DomainJsonExtension);
            if (Engine.DomainLocation != null && !String.IsNullOrWhiteSpace(Engine.DomainLocation.LocalPath))
                InitialRoute = Path.Combine(Path.GetDirectoryName(Engine.DomainLocation.LocalPath), SafeFileName(TargetDomain.TechName) + "." + DomainJsonExtension);

            var TargetRoute = Display.DialogGetSaveFile("Export Domain JSON", "json", DomainJsonFilter, InitialRoute);
            if (TargetRoute == null)
                return;

            Console.WriteLine("Domain JSON export started. source domain name='" + TargetDomain.Name + "' techName='" + TargetDomain.TechName +
                              "' id=" + TargetDomain.GlobalId.ToString("D") + "; destination='" + TargetRoute.LocalPath + "'");

            try
            {
                var Document = DomainJsonExporter.Export(TargetDomain);
                DomainJsonSerializer.Save(Document, TargetRoute.LocalPath);
                Console.WriteLine("Domain JSON export summary: conceptDefinitions=" + Document.ConceptDefinitions.Count +
                                  ", relationshipDefinitions=" + Document.RelationshipDefinitions.Count +
                                  ", tableDefinitions=" + Document.TableDefinitions.Count +
                                  ", markerDefinitions=" + Document.MarkerDefinitions.Count +
                                  ", templates=" + (Document.ConceptDefinitionOutputTemplates.Count + Document.RelationshipDefinitionOutputTemplates.Count) +
                                  ", export warnings=" + Document.Warnings.Count);
                foreach (var Warning in Document.Warnings)
                    Console.WriteLine("Domain JSON export warning: " + Warning);
                Console.WriteLine("Domain JSON export completed successfully.");
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Domain JSON export failed: " + Problem.Message);
                Console.WriteLine(Problem.ToString());
                AppExec.LogException(Problem);
                Display.DialogMessage("Cannot export Domain JSON", "Problem: " + Problem.Message, EMessageType.Warning);
            }
        }

        public static void ImportDomainJson(WorkspaceManager WorkspaceDirector)
        {
            var Engine = WorkspaceDirector == null ? null : WorkspaceDirector.ActiveDocumentEngine as CompositionEngine;
            var TargetDomain = ActiveDomain(Engine);
            if (TargetDomain == null)
                return;

            var SourceLocation = Display.DialogGetOpenFile("Import/Update Domain JSON", "json", DomainJsonFilter);
            if (SourceLocation == null)
                return;

            try
            {
                var SourceRoute = SourceLocation.LocalPath;
                Console.WriteLine("Domain JSON import started. source='" + SourceRoute + "' target domain name='" +
                                  TargetDomain.Name + "' techName='" + TargetDomain.TechName + "' id=" + TargetDomain.GlobalId.ToString("D"));
                var Document = DomainJsonSerializer.Load(SourceRoute);
                DomainJsonSerializer.Validate(Document);
                var Preview = DomainJsonImporter.Preview(TargetDomain, Document);

                if (!Confirm("Apply Domain JSON changes from:\n" + SourceRoute + "\n\n" + Preview.PreviewSummary() +
                             "\n\nEntity summary: " + Preview.EntitySummary() + FieldUpdatePreview(Preview) + WarningPreview(Preview)))
                    return;

                TargetDomain.EditEngine.StartCommandVariation("Import/Update Domain JSON");
                try
                {
                    var ApplyReport = DomainJsonImporter.Apply(TargetDomain, Document, new DomainJsonImportReport());
                    TargetDomain.UpdateVersion();
                    DomainServices.UpdateDomainDependants(TargetDomain);
                    TargetDomain.EditEngine.CompleteCommandVariation();
                    if (ApplyReport.AppliedCreated > 0 || ApplyReport.AppliedUpdated > 0 || ApplyReport.AppliedDeleted > 0)
                        Engine.ExistenceStatus = EExistenceStatus.Modified;
                    LogPersistenceReminder("Domain JSON import", Engine, TargetDomain);
                Console.WriteLine("Domain JSON import completed. " + ApplyReport.ApplySummary().Replace("\n", "; "));
                Display.DialogMessage("Domain JSON Import", "Import completed.\n\n" + ApplyReport.ApplySummary(), EMessageType.Information);
                }
                catch (Exception)
                {
                    TargetDomain.EditEngine.DiscardCommandVariation();
                    throw;
                }
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Domain JSON import failed: " + Problem.Message);
                Console.WriteLine(Problem.ToString());
                AppExec.LogException(Problem);
                Display.DialogMessage("Cannot import Domain JSON", "Problem: " + Problem.Message, EMessageType.Warning);
            }
        }

        public static void UpdateEmbeddedDomain(CompositionEngine Engine)
        {
            var TargetDomain = ActiveDomain(Engine);
            if (TargetDomain == null)
                return;

            var SourceLocation = Display.DialogGetOpenFile("Update Embedded Domain", Domain.FILE_EXTENSION_DOMAIN, DomainUpdateFilter);
            if (SourceLocation == null)
                return;

            UpdateEmbeddedDomainFromFile(Engine, SourceLocation.LocalPath);
        }

        public static void UpdateEmbeddedDomainFromFile(CompositionEngine Engine, string SourceRoute)
        {
            var TargetDomain = ActiveDomain(Engine);
            if (TargetDomain == null)
                return;

            if (String.IsNullOrWhiteSpace(SourceRoute))
                return;

            try
            {
                Console.WriteLine("Embedded Domain update started. source='" + SourceRoute + "' target composition='" +
                                  Engine.TargetComposition.TechName + "' target domain='" + TargetDomain.TechName + "'");

                var Document = LoadDomainUpdateSource(SourceRoute);
                var Preview = DomainJsonImporter.Preview(TargetDomain, Document);
                if (!Confirm("Update the active Composition's embedded Domain from:\n" + SourceRoute +
                             "\n\n" + Preview.PreviewSummary() +
                             "\n\nThis is an explicit safe merge. Nothing is deleted by omission." +
                             FieldUpdatePreview(Preview) + WarningPreview(Preview)))
                    return;

                Engine.StartCommandVariation("Update Embedded Domain");
                try
                {
                    var ApplyReport = DomainJsonImporter.Apply(TargetDomain, Document, new DomainJsonImportReport());
                    TargetDomain.UpdateVersion();
                    DomainServices.UpdateDomainDependants(TargetDomain);
                    Engine.CompleteCommandVariation();
                    Engine.ExistenceStatus = EExistenceStatus.Modified;
                    LogPersistenceReminder("Embedded Domain update", Engine, TargetDomain);
                    Console.WriteLine("Embedded Domain update completed. " + ApplyReport.ApplySummary().Replace("\n", "; "));
                    Display.DialogMessage("Embedded Domain Update", "Update completed.\n\n" + ApplyReport.ApplySummary(), EMessageType.Information);
                }
                catch (Exception)
                {
                    Engine.DiscardCommandVariation();
                    throw;
                }
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Embedded Domain update failed: " + Problem.Message);
                Console.WriteLine(Problem.ToString());
                AppExec.LogException(Problem);
                Display.DialogMessage("Cannot update embedded Domain", "Problem: " + Problem.Message, EMessageType.Warning);
            }
        }

        private static DomainJsonDocument LoadDomainUpdateSource(string SourceRoute)
        {
            var Extension = Path.GetExtension(SourceRoute).NullDefault("").TrimStart('.').ToLowerInvariant();
            if (Extension == Domain.FILE_EXTENSION_DOMAIN)
            {
                var Load = CompositionEngine.MaterializeDomain(new Uri(SourceRoute, UriKind.Absolute));
                if (Load == null || Load.Item1 == null)
                    throw new InvalidDataException("Cannot load native Domain file. " + (Load == null ? "" : Load.Item2));

                Console.WriteLine("Embedded Domain update source loaded as native .tdom domain '" + Load.Item1.TechName + "'.");
                return DomainJsonExporter.Export(Load.Item1);
            }

            var Document = DomainJsonSerializer.Load(SourceRoute);
            DomainJsonSerializer.Validate(Document);
            Console.WriteLine("Embedded Domain update source loaded as Domain JSON.");
            return Document;
        }

        private static Domain ActiveDomain(CompositionEngine Engine)
        {
            if (Engine == null || Engine.TargetComposition == null || Engine.TargetComposition.CompositeContentDomain == null)
                return null;

            return Engine.TargetComposition.CompositeContentDomain;
        }

        private static bool Confirm(string Message)
        {
            var Confirmation = Display.DialogMessage("Confirmation", Message, EMessageType.Question, MessageBoxButton.YesNo, MessageBoxResult.No);
            return Confirmation == MessageBoxResult.Yes;
        }

        private static void LogPersistenceReminder(string Operation, CompositionEngine Engine, Domain TargetDomain)
        {
            Console.WriteLine(Operation + " modified-state: domain status=" + TargetDomain.ExistenceStatus +
                              "; active document status=" + (Engine == null ? "<none>" : Engine.ExistenceStatus.ToString()));

            if (TargetDomain.OwnerComposition != null)
                Console.WriteLine(Operation + " persistence reminder: domain is embedded in composition '" +
                                  TargetDomain.OwnerComposition.TechName.ToStringAlways() +
                                  "'. Save the composition to persist the domain changes.");
            else
                Console.WriteLine(Operation + " persistence reminder: save the active domain document to persist the changes.");
        }

        private static string WarningPreview(DomainJsonImportReport Report)
        {
            if (Report == null ||
                (Report.SourceWarnings.Count + Report.ImportWarnings.Count + Report.SkippedMessages.Count + Report.Errors.Count) < 1)
                return "";

            var Text = "";
            Text += MessagePreview("Source warnings", Report.SourceWarnings, 5);
            Text += MessagePreview("Import warnings", Report.ImportWarnings, 5);
            Text += MessagePreview("Skipped operations", Report.SkippedMessages, 5);
            Text += MessagePreview("Errors", Report.Errors, 5);
            return Text;
        }

        private static string FieldUpdatePreview(DomainJsonImportReport Report)
        {
            if (Report == null || Report.FieldUpdates.Count < 1 || Report.FieldUpdates.Count > 8)
                return "";

            return "\n\nField updates:\n- " + String.Join("\n- ", Report.FieldUpdates.Select(Line => Line.Replace("Domain JSON planned field update: ", "")));
        }

        private static string MessagePreview(string Title, System.Collections.Generic.IList<string> Messages, int Maximum)
        {
            if (Messages == null || Messages.Count < 1)
                return "";

            return "\n\n" + Title + ":\n- " + String.Join("\n- ", Messages.Take(Maximum)) +
                   (Messages.Count > Maximum ? "\n- ..." : "");
        }

        private static string SafeFileName(string Source)
        {
            Source = Source.NullDefault("Domain").TextToIdentifier();
            foreach (var Character in Path.GetInvalidFileNameChars())
                Source = Source.Replace(Character, '_');
            return Source;
        }
    }
}
