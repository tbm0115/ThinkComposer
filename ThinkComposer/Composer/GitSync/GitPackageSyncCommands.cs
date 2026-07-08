// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Desktop command facade for Git package synchronization.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private static readonly object CompositionStatusSync = new object();
        private static readonly Dictionary<string, GitStatusCacheEntry> CompositionStatusByPath = new Dictionary<string, GitStatusCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object DomainStatusSync = new object();
        private static readonly Dictionary<string, GitStatusCacheEntry> DomainStatusByPath = new Dictionary<string, GitStatusCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan GitStatusRefreshInterval = TimeSpan.FromMinutes(15);
        private const string DomainGitCredentialsUnavailableMessage = "Cannot check remote updates without cached Git credentials. Use Pull from Git to authenticate, or configure Git Credential Manager/SSH.";

        public static void LinkActiveComposition(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            if (Engine == null || !RequireSavedComposition(Engine))
                return;

            LinkPackage(Engine.FullLocation.LocalPath, true);
            ClearCompositionGitStatus(Engine.FullLocation.LocalPath);
        }

        public static void PullActiveComposition(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            if (Engine == null || !RequireSavedComposition(Engine) || !RequireUnmodified(Engine))
                return;

            Run("Pull Composition from Git", delegate
            {
                var Result = GitPackageSyncService.PullPackage(Engine.FullLocation.LocalPath, Engine.FullLocation.LocalPath, true, null);
                ClearCompositionGitStatus(Engine.FullLocation.LocalPath);
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
                ClearCompositionGitStatus(Engine.FullLocation.LocalPath);
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
            var Target = GetActiveDomainGitTarget(Engine);
            if (Engine == null || Target == null || !RequireUnmodified(Engine))
                return;

            Run("Pull Domain from Git", delegate
            {
                if (Target.Kind == DomainGitTargetKind.EmbeddedCompositionPackage)
                {
                    var DomainPath = GitPackageSyncService.PullEmbeddedDomainBaseline(Target.PackagePath, null);
                    DomainJsonInterchangeCommands.UpdateEmbeddedDomainFromFile(Engine, DomainPath);
                    ClearDomainGitStatus(Target.PackagePath);
                    Display.DialogMessage("Git Pull", "Linked Domain pulled from Git and merged into the active Composition.", EMessageType.Information);
                }
                else if (Target.Kind == DomainGitTargetKind.SourceDomainPackageForComposition)
                {
                    var DomainPath = GitPackageSyncService.PullDomainPackageToTemporaryFile(Target.PackagePath, null);
                    DomainJsonInterchangeCommands.UpdateEmbeddedDomainFromFile(Engine, DomainPath);
                    ClearDomainGitStatus(Target.PackagePath);
                    Display.DialogMessage("Git Pull", "Linked Domain pulled from Git and merged into the active Composition.", EMessageType.Information);
                }
                else
                {
                    var Result = GitPackageSyncService.PullPackage(Target.PackagePath, Target.PackagePath, true, null);
                    ClearDomainGitStatus(Target.PackagePath);
                    Display.DialogMessage("Git Pull", Result.Message + "\n\nClose and reopen the Domain to load the pulled package.", EMessageType.Information);
                }
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
            if (Engine == null || !Engine.IsForEditDomain)
                return false;

            var DomainPath = GetActiveDomainPackagePath(Engine);
            return !String.IsNullOrWhiteSpace(DomainPath) &&
                   File.Exists(DomainPath) &&
                   TryReadGitLink(DomainPath) == null;
        }

        public static bool CanPullActiveComposition(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            var CompositionPath = GetActiveCompositionPackagePath(Engine);
            if (String.IsNullOrWhiteSpace(CompositionPath) ||
                !File.Exists(CompositionPath) ||
                TryReadGitLink(CompositionPath) == null ||
                Engine.ExistenceStatus == EExistenceStatus.Modified)
                return false;

            var Entry = GetCompositionGitStatus(CompositionPath);
            if (Entry == null || Entry.IsChecking)
                return true;

            if (!Entry.ErrorMessage.IsAbsent())
                return !IsMissingRemoteBaselineOrBranch(Entry.ErrorMessage);

            if (Entry.Status == null)
                return true;

            return Entry.Status.BaselineExists && Entry.Status.HasRemoteUpdate;
        }

        public static bool CanPushActiveComposition(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            var CompositionPath = GetActiveCompositionPackagePath(Engine);
            if (String.IsNullOrWhiteSpace(CompositionPath) ||
                !File.Exists(CompositionPath) ||
                TryReadGitLink(CompositionPath) == null)
                return false;

            var Entry = GetCompositionGitStatus(CompositionPath);
            if (Engine.ExistenceStatus == EExistenceStatus.Modified)
                return Entry == null || Entry.Status == null || !Entry.Status.HasRemoteUpdate;

            if (Entry == null || Entry.IsChecking || !Entry.ErrorMessage.IsAbsent() || Entry.Status == null)
                return true;

            if (!Entry.Status.BaselineExists)
                return true;

            return Entry.Status.HasLocalChangesToPush;
        }

        public static bool CanPullActiveDomain(WorkspaceManager WorkspaceDirector)
        {
            var Engine = ActiveCompositionEngine(WorkspaceDirector);
            return GetActiveDomainGitTarget(Engine) != null;
        }

        public static WorkCommandVisualStatus GetDomainLinkVisualStatus(DocumentEngine Document)
        {
            var Engine = Document as CompositionEngine;
            var DomainPath = GetActiveDomainPackagePath(Engine);
            var Summary = "Links the current Domain package to a Git remote path.";

            if (Engine == null || !Engine.IsForEditDomain)
            {
                var CompositionPath = GetActiveCompositionPackagePath(Engine);
                Summary = !String.IsNullOrWhiteSpace(CompositionPath) &&
                          File.Exists(CompositionPath) &&
                          HasEmbeddedDomainGitLink(CompositionPath)
                          ? "This embedded Domain is already linked to Git through the Composition package."
                          : "Open the Domain package for editing before linking it to Git.";
            }
            else if (String.IsNullOrWhiteSpace(DomainPath) || !File.Exists(DomainPath))
                Summary = "Save the Domain package before linking it to Git.";
            else if (TryReadGitLink(DomainPath) != null)
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
            var Target = GetActiveDomainGitTarget(Engine);
            var DefaultSummary = Target != null && Target.PullsIntoEmbeddedDomain
                                 ? "Pulls and merges the linked embedded Domain from Git."
                                 : "Pulls the linked Domain package from Git.";

            if (Target == null)
                return CreateDomainPullStatus("Pull from Git", "This Domain is not linked to Git.", "arrow_down.png");

            var Entry = GetDomainGitStatus(Target.PackagePath);

            if (Entry == null)
                return CreateDomainPullStatus("Pull from Git", "Linked Domain package. Waiting to check Git for updates...", "arrow_down.png");

            if (Entry.IsChecking && Entry.Status == null && Entry.ErrorMessage.IsAbsent())
                return CreateDomainPullStatus("Pull from Git", "Checking linked Git remote for Domain updates...", "arrow_refresh.png");

            if (!Entry.ErrorMessage.IsAbsent())
            {
                if (IsDomainGitCredentialsUnavailable(Entry.ErrorMessage))
                    return CreateDomainPullStatus("Pull from Git", Entry.ErrorMessage, "arrow_down.png");

                return CreateDomainPullStatus("Pull from Git !", "Cannot check linked Git remote: " + Entry.ErrorMessage, "exclamation.png");
            }

            if (Entry.Status != null && !Entry.Status.BaselineExists)
                return CreateDomainPullStatus("Pull from Git !", "Linked Domain baseline was not found in the Git repository.", "exclamation.png");

            if (Entry.Status != null && Entry.Status.HasRemoteUpdate)
                return CreateDomainPullStatus("Pull from Git *", "A newer linked Domain package is available from Git.", "bell.png");

            return CreateDomainPullStatus("Pull from Git", DefaultSummary + " The linked Domain package is current.", "arrow_down.png");
        }

        public static WorkCommandVisualStatus GetCompositionPullVisualStatus(DocumentEngine Document)
        {
            var Engine = Document as CompositionEngine;
            var CompositionPath = GetActiveCompositionPackagePath(Engine);
            if (String.IsNullOrWhiteSpace(CompositionPath) ||
                !File.Exists(CompositionPath) ||
                TryReadGitLink(CompositionPath) == null)
                return CreateGitStatus("Pull from Git", "This Composition is not linked to Git.", "arrow_down.png");

            if (Engine.ExistenceStatus == EExistenceStatus.Modified)
                return CreateGitStatus("Pull from Git", "Save or discard pending Composition changes before pulling from Git.", "arrow_down.png");

            var Entry = GetCompositionGitStatus(CompositionPath);
            if (Entry == null)
                return CreateGitStatus("Pull from Git", "Linked Composition package. Waiting to check Git for updates...", "arrow_down.png");

            if (Entry.IsChecking && Entry.Status == null && Entry.ErrorMessage.IsAbsent())
                return CreateGitStatus("Pull from Git", "Checking linked Git remote for Composition updates...", "arrow_refresh.png");

            if (!Entry.ErrorMessage.IsAbsent())
            {
                if (IsMissingRemoteBaselineOrBranch(Entry.ErrorMessage))
                    return CreateGitStatus("Pull from Git", "No remote Composition baseline exists yet. Use Commit and Push to Git first.", "arrow_down.png");

                return CreateGitStatus("Pull from Git !", "Cannot check linked Git remote: " + Entry.ErrorMessage, "exclamation.png");
            }

            if (Entry.Status != null && !Entry.Status.BaselineExists)
                return CreateGitStatus("Pull from Git", "No remote Composition baseline exists yet. Use Commit and Push to Git first.", "arrow_down.png");

            if (Entry.Status != null && Entry.Status.HasRemoteUpdate)
                return CreateGitStatus("Pull from Git *", "A newer linked Composition package is available from Git.", "bell.png");

            if (Entry.Status != null && Entry.Status.HasLocalChangesToPush)
                return CreateGitStatus("Pull from Git", "Local Composition changes are ready to push; no remote update is available.", "arrow_down.png");

            return CreateGitStatus("Pull from Git", "The linked Composition package is current.", "arrow_down.png");
        }

        public static WorkCommandVisualStatus GetCompositionPushVisualStatus(DocumentEngine Document)
        {
            var Engine = Document as CompositionEngine;
            var CompositionPath = GetActiveCompositionPackagePath(Engine);
            if (String.IsNullOrWhiteSpace(CompositionPath) ||
                !File.Exists(CompositionPath) ||
                TryReadGitLink(CompositionPath) == null)
                return CreateGitStatus("Commit and Push to Git", "This Composition is not linked to Git.", "arrow_up.png");

            var Entry = GetCompositionGitStatus(CompositionPath);
            if (Entry != null && Entry.Status != null && Entry.Status.HasRemoteUpdate)
                return CreateGitStatus("Commit and Push to Git", "Remote Composition changes are available. Pull from Git before pushing.", "arrow_up.png");

            if (Engine.ExistenceStatus == EExistenceStatus.Modified)
                return CreateGitStatus("Commit and Push to Git *", "Unsaved Composition changes will be saved, committed, and pushed to Git.", "bell.png");

            if (Entry == null)
                return CreateGitStatus("Commit and Push to Git", "Linked Composition package. Waiting to check Git before enabling push state...", "arrow_up.png");

            if (Entry.IsChecking && Entry.Status == null && Entry.ErrorMessage.IsAbsent())
                return CreateGitStatus("Commit and Push to Git", "Checking linked Git remote before push...", "arrow_refresh.png");

            if (!Entry.ErrorMessage.IsAbsent())
            {
                if (IsMissingRemoteBaselineOrBranch(Entry.ErrorMessage))
                    return CreateGitStatus("Commit and Push to Git", "Publishes this Composition to the linked Git path for the first time.", "arrow_up.png");

                return CreateGitStatus("Commit and Push to Git !", "Cannot check linked Git remote: " + Entry.ErrorMessage, "exclamation.png");
            }

            if (Entry.Status != null && !Entry.Status.BaselineExists)
                return CreateGitStatus("Commit and Push to Git", "Publishes this Composition to the linked Git path for the first time.", "arrow_up.png");

            if (Entry.Status != null && Entry.Status.HasLocalChangesToPush)
                return CreateGitStatus("Commit and Push to Git *", "Local Composition package changes are ready to commit and push.", "bell.png");

            return CreateGitStatus("Commit and Push to Git", "The linked Composition package has no local changes to push.", "arrow_up.png");
        }

        public static void RequestCompositionGitVisualStatusRefresh(DocumentEngine Document)
        {
            var Engine = Document as CompositionEngine;
            var CompositionPath = GetActiveCompositionPackagePath(Engine);
            if (String.IsNullOrWhiteSpace(CompositionPath) ||
                !File.Exists(CompositionPath) ||
                TryReadGitLink(CompositionPath) == null)
                return;

            EnsureCompositionRemoteStatusCheck(CompositionPath);
        }

        public static void RequestDomainPullVisualStatusRefresh(DocumentEngine Document)
        {
            var Engine = Document as CompositionEngine;
            var Target = GetActiveDomainGitTarget(Engine);
            if (Target == null)
                return;

            EnsureDomainRemoteStatusCheck(Target.PackagePath, Target.Kind);
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

        private static string GetActiveCompositionPackagePath(CompositionEngine Engine)
        {
            if (Engine == null || Engine.FullLocation == null || String.IsNullOrWhiteSpace(Engine.FullLocation.LocalPath))
                return null;

            return Engine.FullLocation.LocalPath;
        }

        private static string GetActiveDomainPackagePath(CompositionEngine Engine)
        {
            if (Engine == null || Engine.DomainLocation == null || String.IsNullOrWhiteSpace(Engine.DomainLocation.LocalPath))
                return null;

            return Engine.DomainLocation.LocalPath;
        }

        private static DomainGitTarget GetActiveDomainGitTarget(CompositionEngine Engine)
        {
            var CompositionPath = GetActiveCompositionPackagePath(Engine);
            if (Engine != null && !Engine.IsForEditDomain)
            {
                if (!String.IsNullOrWhiteSpace(CompositionPath) &&
                    File.Exists(CompositionPath) &&
                    HasEmbeddedDomainGitLink(CompositionPath))
                    return new DomainGitTarget { PackagePath = Path.GetFullPath(CompositionPath), Kind = DomainGitTargetKind.EmbeddedCompositionPackage };
            }

            var DomainPath = GetActiveDomainPackagePath(Engine);
            if (!String.IsNullOrWhiteSpace(DomainPath) &&
                File.Exists(DomainPath) &&
                TryReadGitLink(DomainPath) != null)
                return new DomainGitTarget
                {
                    PackagePath = Path.GetFullPath(DomainPath),
                    Kind = Engine != null && Engine.IsForEditDomain
                           ? DomainGitTargetKind.ExternalDomainPackage
                           : DomainGitTargetKind.SourceDomainPackageForComposition
                };

            return null;
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

        private static GitPackageLink TryReadEmbeddedDomainGitLink(string PackagePath)
        {
            try
            {
                return JsonPackagePersistence.ReadEmbeddedDomainGitSyncLink(PackagePath);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Cannot read embeddedDomainGitSync link from '" + PackagePath + "': " + Problem.Message);
                return null;
            }
        }

        private static bool HasEmbeddedDomainGitLink(string CompositionPath)
        {
            var EmbeddedLink = TryReadEmbeddedDomainGitLink(CompositionPath);
            if (EmbeddedLink != null &&
                EmbeddedLink.FindBaseline(GitPackageLink.KindDomain, GitPackageLink.RoleSelf) != null)
                return true;

            var PackageLink = TryReadGitLink(CompositionPath);
            return PackageLink != null &&
                   PackageLink.FindBaseline(GitPackageLink.KindDomain, GitPackageLink.RoleEmbeddedDomainSource) != null;
        }

        private static WorkCommandVisualStatus CreateDomainPullStatus(string Name, string Summary, string Pictogram)
        {
            return CreateGitStatus(Name, Summary, Pictogram);
        }

        private static WorkCommandVisualStatus CreateGitStatus(string Name, string Summary, string Pictogram)
        {
            return new WorkCommandVisualStatus
            {
                Name = Name,
                Summary = Summary,
                ToolTip = Summary,
                Pictogram = Display.GetAppImage(Pictogram)
            };
        }

        private static void EnsureCompositionRemoteStatusCheck(string CompositionPath)
        {
            if (String.IsNullOrWhiteSpace(CompositionPath))
                return;

            var FullPath = Path.GetFullPath(CompositionPath);
            lock (CompositionStatusSync)
            {
                GitStatusCacheEntry Entry;
                if (!CompositionStatusByPath.TryGetValue(FullPath, out Entry))
                {
                    Entry = new GitStatusCacheEntry();
                    CompositionStatusByPath[FullPath] = Entry;
                }

                if (Entry.IsChecking)
                    return;

                if (Entry.CheckedAtUtc != DateTime.MinValue &&
                    DateTime.UtcNow - Entry.CheckedAtUtc < GitStatusRefreshInterval)
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
                    ErrorMessage = CreateGitStatusErrorMessage(Problem);
                    Console.WriteLine("Cannot check linked Composition Git remote: " + ErrorMessage);
                }

                lock (CompositionStatusSync)
                {
                    GitStatusCacheEntry Entry;
                    if (!CompositionStatusByPath.TryGetValue(FullPath, out Entry))
                    {
                        Entry = new GitStatusCacheEntry();
                        CompositionStatusByPath[FullPath] = Entry;
                    }

                    Entry.Status = Status;
                    Entry.ErrorMessage = ErrorMessage;
                    Entry.CheckedAtUtc = DateTime.UtcNow;
                    Entry.IsChecking = false;
                }

                NotifyCommandVisualStatusChanged();
            });
        }

        private static GitStatusCacheEntry GetCompositionGitStatus(string CompositionPath)
        {
            var FullPath = Path.GetFullPath(CompositionPath);
            lock (CompositionStatusSync)
            {
                GitStatusCacheEntry Entry;
                return CompositionStatusByPath.TryGetValue(FullPath, out Entry) ? Entry.CreateSnapshot() : null;
            }
        }

        private static void ClearCompositionGitStatus(string CompositionPath)
        {
            if (String.IsNullOrWhiteSpace(CompositionPath))
                return;

            lock (CompositionStatusSync)
                CompositionStatusByPath.Remove(Path.GetFullPath(CompositionPath));

            NotifyCommandVisualStatusChanged();
        }

        private static void EnsureDomainRemoteStatusCheck(string DomainPath, DomainGitTargetKind TargetKind)
        {
            if (String.IsNullOrWhiteSpace(DomainPath))
                return;

            var FullPath = Path.GetFullPath(DomainPath);
            lock (DomainStatusSync)
            {
                GitStatusCacheEntry Entry;
                if (!DomainStatusByPath.TryGetValue(FullPath, out Entry))
                {
                    Entry = new GitStatusCacheEntry();
                    DomainStatusByPath[FullPath] = Entry;
                }

                if (Entry.IsChecking)
                    return;

                if (Entry.CheckedAtUtc != DateTime.MinValue &&
                    DateTime.UtcNow - Entry.CheckedAtUtc < GitStatusRefreshInterval)
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
                    Status = TargetKind == DomainGitTargetKind.EmbeddedCompositionPackage
                             ? GitPackageSyncService.GetEmbeddedDomainRemoteStatus(FullPath)
                             : GitPackageSyncService.GetRemoteStatus(FullPath);
                }
                catch (Exception Problem)
                {
                    ErrorMessage = CreateDomainStatusErrorMessage(Problem);
                    Console.WriteLine((IsDomainGitCredentialsUnavailable(ErrorMessage)
                                      ? "Linked Domain Git remote check skipped: "
                                      : "Cannot check linked Domain Git remote: ") + ErrorMessage);
                }

                lock (DomainStatusSync)
                {
                    GitStatusCacheEntry Entry;
                    if (!DomainStatusByPath.TryGetValue(FullPath, out Entry))
                    {
                        Entry = new GitStatusCacheEntry();
                        DomainStatusByPath[FullPath] = Entry;
                    }

                    Entry.Status = Status;
                    Entry.ErrorMessage = ErrorMessage;
                    Entry.CheckedAtUtc = DateTime.UtcNow;
                    Entry.IsChecking = false;
                }

                NotifyCommandVisualStatusChanged();
            });
        }

        private static GitStatusCacheEntry GetDomainGitStatus(string DomainPath)
        {
            var FullPath = Path.GetFullPath(DomainPath);
            lock (DomainStatusSync)
            {
                GitStatusCacheEntry Entry;
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

        private static string CreateDomainStatusErrorMessage(Exception Problem)
        {
            var Message = CreateGitStatusErrorMessage(Problem);
            if (IsDomainGitCredentialsUnavailable(Message))
                return DomainGitCredentialsUnavailableMessage;

            return Message;
        }

        private static string CreateGitStatusErrorMessage(Exception Problem)
        {
            var Message = Problem == null ? null : Problem.Message;
            if (String.IsNullOrWhiteSpace(Message))
                return "Unknown Git status check failure.";

            return Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault().ToStringAlways(Message);
        }

        private static bool IsMissingRemoteBaselineOrBranch(string Message)
        {
            if (String.IsNullOrWhiteSpace(Message))
                return false;

            return Message.IndexOf("Linked Git branch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   Message.IndexOf("blank repository", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   Message.IndexOf("baseline was not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   Message.IndexOf("package was not found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDomainGitCredentialsUnavailable(string Message)
        {
            if (String.IsNullOrWhiteSpace(Message))
                return false;

            return String.Equals(Message, DomainGitCredentialsUnavailableMessage, StringComparison.Ordinal) ||
                   Message.IndexOf("Cannot prompt because user interactivity has been disabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   Message.IndexOf("could not read Username", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void NotifyCommandVisualStatusChanged()
        {
            var App = Application.Current;
            if (App == null || App.Dispatcher == null || App.Dispatcher.HasShutdownStarted || App.Dispatcher.HasShutdownFinished)
                return;

            try
            {
                App.Dispatcher.BeginInvoke(new Action(CommandManager.InvalidateRequerySuggested));
            }
            catch (InvalidOperationException Problem)
            {
                Console.WriteLine("Cannot refresh Git sync command status: " + Problem.Message);
            }
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

        private sealed class GitStatusCacheEntry
        {
            public bool IsChecking;
            public DateTime CheckedAtUtc;
            public GitPackageRemoteStatus Status;
            public string ErrorMessage;

            public GitStatusCacheEntry CreateSnapshot()
            {
                return new GitStatusCacheEntry
                {
                    IsChecking = this.IsChecking,
                    CheckedAtUtc = this.CheckedAtUtc,
                    Status = this.Status,
                    ErrorMessage = this.ErrorMessage
                };
            }
        }

        private sealed class DomainGitTarget
        {
            public string PackagePath;
            public DomainGitTargetKind Kind;

            public bool PullsIntoEmbeddedDomain
            {
                get { return this.Kind != DomainGitTargetKind.ExternalDomainPackage; }
            }
        }

        private enum DomainGitTargetKind
        {
            ExternalDomainPackage,
            SourceDomainPackageForComposition,
            EmbeddedCompositionPackage
        }
    }
}
