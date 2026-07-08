// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Desktop command facade for Git package synchronization.
// -------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Windows;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.Definitor.DomainJsonInterchange;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public static class GitPackageSyncCommands
    {
        public static void LinkActiveComposition(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            if (Engine == null || !RequireSavedComposition(Engine))
                return;

            LinkPackage(Engine.FullLocation.LocalPath, true);
        }

        public static void PullActiveComposition(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            if (Engine == null || !RequireSavedComposition(Engine) || !RequireUnmodified(Engine))
                return;

            Run("Pull Composition from Git", delegate
            {
                var Result = GitPackageSyncService.PullPackage(Engine.FullLocation.LocalPath, Engine.FullLocation.LocalPath, true, null);
                Display.DialogMessage("Git Pull", Result.Message + "\n\nClose and reopen the Composition to load the pulled package.", EMessageType.Information);
            });
        }

        public static void PushActiveComposition(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            if (Engine == null || !RequireSavedComposition(Engine))
                return;

            Run("Commit and Push Composition to Git", delegate
            {
                var SaveResult = Engine.Store();
                if (!String.IsNullOrWhiteSpace(SaveResult))
                    throw new InvalidOperationException(SaveResult);

                var Message = "Update " + Path.GetFileName(Engine.FullLocation.LocalPath);
                var Result = GitPackageSyncService.PushComposition(Engine.FullLocation.LocalPath, Message);
                Display.DialogMessage("Git Push", Result, EMessageType.Information);
            });
        }

        public static void LinkActiveDomain(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            if (Engine == null || !RequireSavedDomain(Engine))
                return;

            LinkPackage(Engine.DomainLocation.LocalPath, false);
        }

        public static void PullActiveDomain(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            if (Engine == null || !RequireSavedDomain(Engine) || !RequireUnmodified(Engine))
                return;

            Run("Pull Domain from Git", delegate
            {
                var Result = GitPackageSyncService.PullPackage(Engine.DomainLocation.LocalPath, Engine.DomainLocation.LocalPath, true, null);
                Display.DialogMessage("Git Pull", Result.Message + "\n\nClose and reopen the Domain to load the pulled package.", EMessageType.Information);
            });
        }

        public static void PullLinkedEmbeddedDomain(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            if (Engine == null || !RequireSavedComposition(Engine))
                return;

            Run("Pull Linked Embedded Domain", delegate
            {
                var DomainPath = GitPackageSyncService.PullEmbeddedDomainBaseline(Engine.FullLocation.LocalPath, null);
                DomainJsonInterchangeCommands.UpdateEmbeddedDomainFromFile(Engine, DomainPath);
            });
        }

        private static void LinkPackage(string PackagePath, bool IsComposition)
        {
            Run("Link Git Remote", delegate
            {
                var Existing = JsonPackagePersistence.ReadGitSyncLink(PackagePath);
                var Selection = GitSyncLinkDialog.ShowDialog(Display.GetCurrentWindow(), PackagePath, IsComposition, Existing);
                if (Selection == null || !Selection.Accepted)
                    return;

                var Result = GitPackageSyncService.LinkPackage(PackagePath, PackagePath, true,
                                                               Selection.RemoteUrl,
                                                               Selection.Branch,
                                                               Selection.RepositoryPath,
                                                               IsComposition ? Selection.EmbeddedDomainPath : null);
                Display.DialogMessage("Git Link", Result, EMessageType.Information);
            });
        }

        private static CompositionEngine ActiveCompositionEngine(WorkspaceManager WorkspaceDirector)
        {
            return WorkspaceDirector == null ? null : WorkspaceDirector.ActiveDocumentEngine as CompositionEngine;
        }

        private static bool RequireSavedComposition(CompositionEngine Engine)
        {
            if (Engine.FullLocation != null && !String.IsNullOrWhiteSpace(Engine.FullLocation.LocalPath))
                return true;

            Display.DialogMessage("Git Sync", "Save the Composition before using Git sync.", EMessageType.Warning);
            return false;
        }

        private static bool RequireSavedDomain(CompositionEngine Engine)
        {
            if (Engine.DomainLocation != null && !String.IsNullOrWhiteSpace(Engine.DomainLocation.LocalPath))
                return true;

            Display.DialogMessage("Git Sync", "Save the Domain before using Git sync.", EMessageType.Warning);
            return false;
        }

        private static bool RequireUnmodified(CompositionEngine Engine)
        {
            if (Engine.ExistenceStatus != EExistenceStatus.Modified)
                return true;

            Display.DialogMessage("Git Sync", "Save or discard pending changes before pulling from Git.", EMessageType.Warning);
            return false;
        }

        private static void Run(string Caption, Action Operation)
        {
            try
            {
                Operation();
            }
            catch (Exception Problem)
            {
                Console.WriteLine(Caption + " failed: " + Problem.Message);
                Console.WriteLine(Problem.ToString());
                Display.DialogMessage(Caption, "Problem: " + Problem.Message, EMessageType.Warning);
            }
        }
    }
}
