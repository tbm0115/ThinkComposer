// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Desktop command facade for Git package synchronization.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.Definitor.DomainJsonInterchange;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public static class GitPackageSyncCommands
    {
        private static readonly object DomainStatusSync = new object();
        private static readonly Dictionary<string, DomainGitStatusCacheEntry> DomainStatusByPath = new Dictionary<string, DomainGitStatusCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan DomainStatusRefreshInterval = TimeSpan.FromMinutes(2);

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
            ClearDomainGitStatus(Engine.DomainLocation.LocalPath);
        }

        public static void PullActiveDomain(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            if (Engine == null || !RequireSavedDomain(Engine) || !RequireUnmodified(Engine))
                return;

            Run("Pull Domain from Git", delegate
            {
                var Result = GitPackageSyncService.PullPackage(Engine.DomainLocation.LocalPath, Engine.DomainLocation.LocalPath, true, null);
                ClearDomainGitStatus(Engine.DomainLocation.LocalPath);
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

        public static bool CanLinkActiveDomain(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            var DomainPath = GetActiveDomainPackagePath(Engine);
            return !String.IsNullOrWhiteSpace(DomainPath) &&
                   File.Exists(DomainPath) &&
                   TryReadGitLink(DomainPath) == null;
        }

        public static bool CanPullActiveDomain(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            var DomainPath = GetActiveDomainPackagePath(Engine);
            return !String.IsNullOrWhiteSpace(DomainPath) &&
                   File.Exists(DomainPath) &&
                   TryReadGitLink(DomainPath) != null;
        }

        public static WorkCommandVisualStatus GetDomainLinkVisualStatus(DocumentEngine Document)
        {
            var Engine = Document as CompositionEngine;
            var DomainPath = GetActiveDomainPackagePath(Engine);
            var Summary = "Links the current Domain package to a Git remote path.";

            if (String.IsNullOrWhiteSpace(DomainPath) || !File.Exists(DomainPath))
                Summary = "Save the Domain package before linking it to Git.";
            else
                if (TryReadGitLink(DomainPath) != null)
                    Summary = "This Domain package is already linked to Git. Use Pull from Git to update it.";

            return new WorkCommandVisualStatus
            {
                Name = "Link Git Remote...",
                Summary = Summary,
                ToolTip = Summary,
                Pictogram = Display.GetAppImage("link.png")
            };
        }

        public static WorkCommandVisualStatus GetDomainPullVisualStatus(DocumentEngine Document)
        {
            var Engine = Document as CompositionEngine;
            var DomainPath = GetActiveDomainPackagePath(Engine);
            var DefaultSummary = "Pulls the linked Domain package from Git.";

            if (String.IsNullOrWhiteSpace(DomainPath) || !File.Exists(DomainPath))
                return CreateDomainPullStatus("Pull from Git", "Save the Domain package before pulling from Git.", "arrow_down.png");

            if (TryReadGitLink(DomainPath) == null)
                return CreateDomainPullStatus("Pull from Git", "This Domain package is not linked to Git.", "arrow_down.png");

            EnsureDomainRemoteStatusCheck(DomainPath);
            var Entry = GetDomainGitStatus(DomainPath);

            if (Entry == null || (Entry.IsChecking && Entry.Status == null && Entry.ErrorMessage.IsAbsent()))
                return CreateDomainPullStatus("Pull from Git", "Checking linked Git remote for Domain updates...", "arrow_refresh.png");

            if (!Entry.ErrorMessage.IsAbsent())
                return CreateDomainPullStatus("Pull from Git !", "Cannot check linked Git remote: " + Entry.ErrorMessage, "exclamation.png");

            if (Entry.Status != null && !Entry.Status.BaselineExists)
                return CreateDomainPullStatus("Pull from Git !", "Linked Domain baseline was not found in the Git repository.", "exclamation.png");

            if (Entry.Status != null && Entry.Status.HasRemoteUpdate)
                return CreateDomainPullStatus("Pull from Git *", "A newer linked Domain package is available from Git.", "bell.png");

            return CreateDomainPullStatus("Pull from Git", DefaultSummary + " The linked Domain package is current.", "arrow_down.png");
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

        private static string GetActiveDomainPackagePath(CompositionEngine Engine)
        {
            if (Engine == null || Engine.DomainLocation == null || String.IsNullOrWhiteSpace(Engine.DomainLocation.LocalPath))
                return null;

            return Engine.DomainLocation.LocalPath;
        }

        private static GitPackageLink TryReadGitLink(string PackagePath)
        {
            try
            {
                return JsonPackagePersistence.ReadGitSyncLink(PackagePath);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Cannot read gitSync link from '" + PackagePath + "': " + Problem.Message);
                return null;
            }
        }

        private static WorkCommandVisualStatus CreateDomainPullStatus(string Name, string Summary, string Pictogram)
        {
            return new WorkCommandVisualStatus
            {
                Name = Name,
                Summary = Summary,
                ToolTip = Summary,
                Pictogram = Display.GetAppImage(Pictogram)
            };
        }

        private static void EnsureDomainRemoteStatusCheck(string DomainPath)
        {
            if (String.IsNullOrWhiteSpace(DomainPath))
                return;

            var FullPath = Path.GetFullPath(DomainPath);
            lock (DomainStatusSync)
            {
                DomainGitStatusCacheEntry Entry;
                if (!DomainStatusByPath.TryGetValue(FullPath, out Entry))
                {
                    Entry = new DomainGitStatusCacheEntry();
                    DomainStatusByPath[FullPath] = Entry;
                }

                if (Entry.IsChecking)
                    return;

                if (Entry.CheckedAtUtc != DateTime.MinValue &&
                    DateTime.UtcNow - Entry.CheckedAtUtc < DomainStatusRefreshInterval)
                    return;

                Entry.IsChecking = true;
                Entry.ErrorMessage = null;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                GitPackageRemoteStatus Status = null;
                string ErrorMessage = null;
                try
                {
                    Status = GitPackageSyncService.GetRemoteStatus(FullPath);
                }
                catch (Exception Problem)
                {
                    ErrorMessage = Problem.Message;
                    Console.WriteLine("Cannot check linked Domain Git remote: " + Problem.Message);
                }

                lock (DomainStatusSync)
                {
                    var Entry = DomainStatusByPath[FullPath];
                    Entry.Status = Status;
                    Entry.ErrorMessage = ErrorMessage;
                    Entry.CheckedAtUtc = DateTime.UtcNow;
                    Entry.IsChecking = false;
                }

                NotifyCommandVisualStatusChanged();
            });
        }

        private static DomainGitStatusCacheEntry GetDomainGitStatus(string DomainPath)
        {
            var FullPath = Path.GetFullPath(DomainPath);
            lock (DomainStatusSync)
            {
                DomainGitStatusCacheEntry Entry;
                return DomainStatusByPath.TryGetValue(FullPath, out Entry) ? Entry.CreateSnapshot() : null;
            }
        }

        private static void ClearDomainGitStatus(string DomainPath)
        {
            if (String.IsNullOrWhiteSpace(DomainPath))
                return;

            lock (DomainStatusSync)
                DomainStatusByPath.Remove(Path.GetFullPath(DomainPath));

            NotifyCommandVisualStatusChanged();
        }

        private static void NotifyCommandVisualStatusChanged()
        {
            var App = Application.Current;
            if (App != null && App.Dispatcher != null)
                App.Dispatcher.BeginInvoke(new Action(CommandManager.InvalidateRequerySuggested));
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

        private sealed class DomainGitStatusCacheEntry
        {
            public bool IsChecking;
            public DateTime CheckedAtUtc;
            public GitPackageRemoteStatus Status;
            public string ErrorMessage;

            public DomainGitStatusCacheEntry CreateSnapshot()
            {
                return new DomainGitStatusCacheEntry
                {
                    IsChecking = this.IsChecking,
                    CheckedAtUtc = this.CheckedAtUtc,
                    Status = this.Status,
                    ErrorMessage = this.ErrorMessage
                };
            }
        }
    }
}
