using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.ApplicationProduct;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public sealed partial class RepositoryWindow : Window
    {
        private readonly WorkspaceManager workspace;
        private string artifact;
        private readonly TextBox location = new TextBox { MinWidth = 400, Margin = new Thickness(4) };
        private readonly TextBlock summary = new TextBlock { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
        private readonly ListBox changes = new ListBox { SelectionMode = SelectionMode.Extended, Margin = new Thickness(6), MinHeight = 160 };
        private readonly TextBox output = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(6) };
        private readonly List<Control> actions = new List<Control>();
        private CancellationTokenSource cancellation;
        private bool busy;
        private bool refreshing;
        internal static bool RepositoryOperationRunning;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

        public static void ShowFor(WorkspaceManager workspace, bool domain)
        {
            var engine = workspace.ActiveDocumentEngine as CompositionEngine;
            var uri = engine == null ? null : domain ? engine.DomainLocation : engine.FullLocation;
            var existing = Application.Current.Windows.OfType<RepositoryWindow>().FirstOrDefault();
            if (existing != null)
            {
                if (existing.busy) { existing.Activate(); return; }
                existing.artifact = uri == null ? null : uri.LocalPath;
                existing.displayedSignature = null;
                if (uri != null) existing.location.Text = Path.GetDirectoryName(uri.LocalPath);
                existing.Activate(); return;
            }
            var window = new RepositoryWindow(workspace, uri == null ? null : uri.LocalPath) { Owner = Display.GetCurrentWindow() };
            window.Show();
        }
        public RepositoryWindow(WorkspaceManager workspace, string artifact)
        {
            this.workspace = workspace; this.artifact = artifact;
            Title = "Git Repository"; Width = 1160; Height = 800; MinWidth = 960; MinHeight = 680;
            BuildLayout();
            location.Text = artifact == null ? Environment.CurrentDirectory : Path.GetDirectoryName(artifact);
            BeginMenu("Repository");
            Add("Initialize", () => RunCommand("init", "--branch", Ask("Initial branch", "main")));
            Add("Clone", () => RunCommand("clone", "--remote", Ask("Remote URL")));
            Add("Remotes", () => RunCommand("remote-list")); Add("Add remote", () => RunCommand("remote-add", "--name", Ask("Remote name", "origin"), "--url", Ask("Remote URL")));
            Add("Change remote", () => RunCommand("remote-set", "--name", Ask("Remote name", "origin"), "--url", Ask("Remote URL")));
            BeginMenu("Branch");
            Add("Switch branch…", ShowBranches);
            Add("Create branch", () => RunCommand("branch-create", "--branch", Ask("New branch name")));
            Add("Publish branch", () => RunCommand("push", "--remote", Ask("Remote name", "origin"), "--branch", Ask("Branch name")));
            Add("Merge", () => RunCommand("merge", "--revision", Ask("Branch or revision to merge", "@{upstream}")));
            Add("Abort merge", () => Confirmed("Abort this merge? Uncommitted merge resolutions will be discarded.", "merge-abort"));
            BeginMenu("Stash");
            Add("Stash changes", () => RunCommand("stash-save", "--message", Ask("Stash description", "ThinkComposer working changes")));
            Add("List stashes", () => RunCommand("stash-list"));
            Add("Apply stash", () => RunCommand("stash-apply", "--revision", Ask("Stash reference", "stash@{0}")));
            Add("Drop stash", () => { var value = Ask("Stash reference", "stash@{0}"); return Confirmed("Delete " + value + "?", "stash-drop", "--revision", value); });
            BeginMenu("More");
            Add("Restore selected files…", () => { if (MessageBox.Show(this, "Discard selected working-file changes and restore their staged versions?", "Restore", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return Task.FromResult(0); return Selected("restore"); });
            Add("Revert commit", () => RunCommand("revert", "--revision", Ask("Commit to revert (result remains uncommitted)")));
            Add("Create release tag", () => RunCommand("tag", "--name", Ask("Tag name"), "--message", Ask("Release description")));
            Add("List tags", () => RunCommand("tag-list")); Add("Publish tag", () => RunCommand("tag-push", "--remote", Ask("Remote", "origin"), "--name", Ask("Tag name")));
            BeginMenu("Model tracking");
            Add("Enable model tracking", () => ArtifactCommand("tracking", "enable"));
            Add("Disable model tracking", () => ArtifactCommand("tracking", "disable"));
            Add("Verify models", () => RunCommand("verify")); Add("Refresh projection", () => ArtifactCommand("projection", "refresh"));
            Add("Preview projection import", () => ArtifactCommand("projection", "import"));
            Add("Apply projection import", () => { if (MessageBox.Show(this, "Apply the reviewed projection edits, including deletions, to the container?", "Import projection", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return Task.FromResult(0); return ArtifactCommand("projection", "import", "--apply"); });
            Add("Install local integration", () => RunCommand("setup", "--executable", Ask("Path to ThinkComposer.Cli.exe", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ThinkComposer.Cli.exe"))));
            Add("Resume resolution", () => { RequireSavedDocuments(); new ModelResolverWindow(location.Text, Ask("Merge session directory")) { Owner = this }.ShowDialog(); return Refresh(); });
            Add("Resolve selected model", ResolveSelected);
            Add("Compare unsaved with disk", () => { output.Text = CompareUnsaved(workspace, artifact); operationLog.IsExpanded = true; return Task.FromResult(0); });
            Add("Migrate legacy model", () => artifact == null ? Task.FromResult(0) : RunCommand("migrate", "--input", artifact, "--path", Ask("Destination path inside this repository", Path.GetFileName(artifact))));
            Add("Apply migration", () => artifact == null ? Task.FromResult(0) : RunCommand("migrate", "--input", artifact, "--path", Ask("Reviewed destination path", Path.GetFileName(artifact)), "--apply"));
            Loaded += async (s, e) => await Refresh();
            timer.Tick += async (s, e) => { if (!busy && !location.IsKeyboardFocusWithin) await Refresh(); }; timer.Start();
            Closed += (s, e) => { timer.Stop(); if (cancellation != null) cancellation.Cancel(); };
            UpdateActions();
        }
        private void Add(string label, Func<Task> action, bool cancellationButton = false)
        {
            var item = new MenuItem { Header = label };
            item.Click += async (s, e) => await Safely(action);
            currentMenu.Items.Add(item); actions.Add(item);
        }
        private string Ask(string title, string initial = "")
        {
            var dialog = new Window { Title = title, Owner = this, Width = 560, Height = 165, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            RepositoryTheme.Apply(dialog);
            var panel = new StackPanel { Margin = new Thickness(12) }; var value = new TextBox { Text = initial, Margin = new Thickness(0, 4, 0, 12) };
            panel.Children.Add(new TextBlock { Text = title }); panel.Children.Add(value);
            var ok = new Button { Content = "Continue", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 4, 12, 4) };
            ok.Click += (s, e) => dialog.DialogResult = true; panel.Children.Add(ok); dialog.Content = panel;
            if (dialog.ShowDialog() != true) throw new OperationCanceledException(); return value.Text;
        }
        private Task Confirmed(string message, params string[] command) { return MessageBox.Show(this, message, "Git Repository", MessageBoxButton.YesNo) == MessageBoxResult.Yes ? RunCommand(command) : Task.FromResult(0); }
        private Task ArtifactCommand(params string[] command)
        {
            if (artifact == null) throw new GitWorkflowException("SAVE_FIRST", "Save a Domain or Composition first.");
            return RunCommand(command.Concat(new[] { "--input", artifact }).ToArray());
        }
        private Task Selected(string command)
        {
            var paths = changes.SelectedItems.Cast<RepositoryChange>().SelectMany(c => new[] { "--path", c.Path }).ToArray();
            if (paths.Length == 0) throw new GitWorkflowException("NO_SELECTION", "Select repository files first.");
            return RunCommand(new[] { command }.Concat(paths).ToArray());
        }
        private async Task RunCommand(params string[] command)
        {
            if (busy || RepositoryOperationRunning) return;
            if (command[0] != "init" && command[0] != "clone" && (repositoryStatus == null || !String.Equals(location.Text, repositoryStatus.Root, StringComparison.OrdinalIgnoreCase)))
                throw new GitWorkflowException("REFRESH_REQUIRED", "Choose Open to load this repository before running an action.");
            if (new[] { "switch", "pull", "merge", "merge-abort", "revert", "restore", "stash-save", "stash-apply", "tracking", "projection", "migrate" }.Contains(command[0])) RequireSavedDocuments();
            var root = location.Text;
            busy = true; RepositoryOperationRunning = true; cancellation = new CancellationTokenSource();
            UpdateActions(); feedback.Text = "Running " + command[0] + "…";
            try { output.Text = await Task.Run(() => GitWorkflowCli.Execute(command.Concat(new[] { "--repo", root }).ToArray(), cancellation.Token)); feedback.Text = command[0] == "stage" ? "Files staged. Review Staged, enter a summary, and commit when ready." : command[0] == "commit" ? "Committed locally. Choose Push when you are ready to share." : command[0] + " completed."; operationLog.IsExpanded = !new[] { "stage", "unstage", "commit", "fetch", "push" }.Contains(command[0]); }
            catch (GitWorkflowException problem)
            {
                if (problem.Code != "DIVERGED") throw;
                feedback.Text = "Branches have diverged. Review Operation details, then choose Branch → Merge."; operationLog.IsExpanded = true;
                output.Text = "Branches diverged. Review this comparison, then choose Merge to prepare an uncommitted merge with @{upstream}.\n" + await Task.Run(() => RepositoryGit.Execute(root, "diff", "HEAD...@{upstream}", cancellation: cancellation.Token));
            }
            finally { busy = false; RepositoryOperationRunning = false; cancellation.Dispose(); cancellation = null; UpdateActions(); await Refresh(); }
        }
        private void RequireSavedDocuments()
        {
            var root = RepositoryGit.Discover(location.Text);
            foreach (var doc in workspace.Documents)
            {
                var engine = doc.DocumentEditEngine as CompositionEngine; var path = DocumentPath(engine);
                if (path != null && engine.ExistenceStatus == EExistenceStatus.Modified && RepositoryGit.Discover(path, false) == root)
                    throw new GitWorkflowException("UNSAVED_DOCUMENT", "Save or explicitly discard changes in all affected documents first: " + path);
            }
        }
        private async Task Refresh()
        {
            if (busy || refreshing) return;
            refreshing = true;
            try
            {
                var path = location.Text; var status = await Task.Run(() => RepositoryGit.Status(path));
                if (path != location.Text) return;
                location.Text = status.Root; repositoryStatus = status;
                repositoryName.Text = Path.GetFileName(status.Root); branchButton.Content = "Branch: " + status.Branch + " ▾";
                summary.Text = status.Upstream == null ? "This branch has not been published. Commit locally, then publish when ready." : status.Upstream + "   •   " + status.Ahead + " to push   •   " + status.Behind + " to pull";
                if (status.Operation != null) summary.Text += "\nGit operation in progress: " + status.Operation + ". Resolve conflicts and review staged changes before committing.";
                if (status.TrackingTransactionPending) summary.Text += "\nInterrupted model transaction: run git recover --mode complete|rollback before repository changes.";
                trackingInfo.Text = artifact == null ? "No active saved model." : Path.GetFileName(artifact) + " • " + (artifact.StartsWith(status.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? (ModelTracking.Find(status.Root, artifact) == null ? "Model tracking is off (optional). Use Git normally, or enable it in Model tracking." : "Model tracking is on. Containers and projections stage together.") : "Outside this repository.");
                UpdateFileList(); UpdateActions();
                ReloadCleanDocuments();
            }
            catch (Exception e) { repositoryStatus = null; changes.ItemsSource = null; repositoryName.Text = "Choose a repository"; summary.Text = e.Message + "\nUse Repository to open, initialize, or clone a repository."; UpdateActions(); }
            finally { refreshing = false; }
        }
        internal static string DocumentPath(CompositionEngine engine) { var uri = engine == null ? null : engine.IsForEditDomain ? engine.DomainLocation : engine.FullLocation; return uri == null ? null : uri.LocalPath; }
        private void ReloadCleanDocuments()
        {
            foreach (var doc in workspace.Documents.ToArray())
            {
                var engine = doc.DocumentEditEngine as CompositionEngine; var path = DocumentPath(engine);
                if (path == null || !DocumentDiskState.Changed(path)) continue;
                if (engine.ExistenceStatus == EExistenceStatus.Modified) { summary.Text += "\nExternal change: " + path + ". Unsaved work is preserved; save a separate copy to compare/reconcile before reloading."; continue; }
                if (!File.Exists(path)) { summary.Text += "\nThe open document was removed externally: " + path; continue; }
                var domain = engine.IsForEditDomain;
                if (engine.Manager.CloseComposition(engine))
                {
                    DocumentDiskState.Accept(path);
                    if (domain) ProductDirector.CompositionDirector.OpenDomainAndCreateCompositionOfIt(true, new Uri(path), false);
                    else CompositionsManager.OpenComposition(new Uri(path), false);
                }
            }
        }
        private async Task ResolveSelected()
        {
            RequireSavedDocuments();
            var selected = changes.SelectedItem as RepositoryChange;
            if (selected == null || !selected.Conflict || !(selected.Path.EndsWith(".tcom", StringComparison.OrdinalIgnoreCase) || selected.Path.EndsWith(".tdom", StringComparison.OrdinalIgnoreCase))) throw new GitWorkflowException("NO_MODEL_CONFLICT", "Select a conflicted container.");
            var root = location.Text; var session = await Task.Run(() => ModelMerge.StartSession(root, selected.Path));
            new ModelResolverWindow(root, session) { Owner = this }.ShowDialog(); await Refresh();
        }
        private static string CompareUnsaved(WorkspaceManager workspace, string path)
        {
            var engine = workspace.Documents.Select(d => d.DocumentEditEngine as CompositionEngine).FirstOrDefault(e => String.Equals(DocumentPath(e), path, StringComparison.OrdinalIgnoreCase));
            if (engine == null) throw new GitWorkflowException("NO_DOCUMENT", "Open the affected document to compare it with disk.");
            var snapshot = Path.Combine(Path.GetTempPath(), "thinkcomposer-unsaved-" + Guid.NewGuid() + (engine.IsForEditDomain ? ".tdom" : ".tcom"));
            var error = engine.IsForEditDomain ? JsonPackagePersistence.StoreDomain(engine.TargetComposition.CompositeContentDomain, new Uri(snapshot), false, true, null, true, true) : JsonPackagePersistence.StoreComposition(engine.TargetComposition, new Uri(snapshot), false, true, null, true);
            if (!String.IsNullOrEmpty(error)) throw new GitWorkflowException("SNAPSHOT_FAILED", error);
            return "Unsaved work preserved at " + snapshot + "\nReview the comparison, save a separate copy, then explicitly close/discard and reopen to adopt disk changes.\n" + ModelMerge.Compare(snapshot, path);
        }

        // Runs independently of the repository panel so external clients are noticed for all open documents.
        public static void StartDocumentMonitor(WorkspaceManager workspace)
        {
            var watcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            var notified = new HashSet<string>(StringComparer.OrdinalIgnoreCase); bool checking = false;
            watcher.Tick += (s, e) =>
            {
                if (checking || RepositoryOperationRunning) return; checking = true;
                try
                {
                    foreach (var doc in workspace.Documents.ToArray())
                    {
                        var engine = doc.DocumentEditEngine as CompositionEngine; var path = DocumentPath(engine);
                        if (path == null || !DocumentDiskState.Changed(path)) { if (path != null) notified.Remove(path); continue; }
                        var root = RepositoryGit.Discover(path, false); if (root == null) continue;
                        var state = RepositoryGit.Status(root); if (state.Operation != null || state.Changes.Any(c => c.Conflict)) continue;
                        if (engine.ExistenceStatus == EExistenceStatus.Modified || !File.Exists(path))
                        {
                            if (notified.Add(path) && MessageBox.Show("The disk version changed: " + path + "\nUnsaved work is preserved. Open the repository panel to compare/reconcile it?", "External repository change", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                                new RepositoryWindow(workspace, path) { Owner = Display.GetCurrentWindow() }.Show();
                            continue;
                        }
                        var domain = engine.IsForEditDomain;
                        if (!engine.Manager.CloseComposition(engine)) continue;
                        DocumentDiskState.Accept(path);
                        if (domain) ProductDirector.CompositionDirector.OpenDomainAndCreateCompositionOfIt(true, new Uri(path), false);
                        else CompositionsManager.OpenComposition(new Uri(path), false);
                    }
                }
                catch (Exception problem) { Console.WriteLine("Repository document watcher: " + problem.Message); }
                finally { checking = false; }
            };
            watcher.Start();
        }
    }

    public sealed class ModelResolverWindow : Window
    {
        private readonly string root, session;
        private readonly ListBox conflicts = new ListBox { Margin = new Thickness(6), MinWidth = 220 };
        private readonly TextBox details = new TextBox { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
        private readonly TextBox custom = new TextBox { AcceptsReturn = true, Height = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(6), Text = "null" };
        private readonly TextBlock status = new TextBlock { Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap };
        private List<object> items;
        public ModelResolverWindow(string root, string session)
        {
            this.root = root; this.session = session; Title = "Resolve model changes"; Width = 1050; Height = 720;
            RepositoryTheme.Apply(this);
            var panel = new DockPanel(); Content = panel;
            DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
            var controls = new WrapPanel(); DockPanel.SetDock(controls, Dock.Bottom); panel.Children.Add(controls);
            foreach (var choice in new[] { "current", "incoming", "ancestor", "delete", "custom" })
            { var name = choice; var button = new Button { Content = "Use " + choice, Margin = new Thickness(5), Padding = new Thickness(8) }; button.Click += (s, e) => Choose(name); controls.Children.Add(button); }
            var apply = new Button { Content = "Validate and apply", Margin = new Thickness(5), Padding = new Thickness(8) };
            apply.Click += (s, e) => { try { status.Text = ModelMerge.ApplySession(root, session); } catch (Exception ex) { status.Text = ex.Message; } }; controls.Children.Add(apply);
            var close = new Button { Content = "Close / resume later", Margin = new Thickness(5) }; close.Click += (s, e) => Close(); controls.Children.Add(close);
            var reset = new Button { Content = "Reset decisions", Margin = new Thickness(5) };
            reset.Click += (s, e) => { ModelJson.Write(Path.Combine(session, "resolutions.json"), ModelJson.Map("formatVersion", 1, "resolutions", new object[0])); Refresh(); }; controls.Children.Add(reset);
            var abort = new Button { Content = "Abort session", Margin = new Thickness(5) }; abort.Click += (s, e) => { ModelMerge.AbortSession(session); Close(); }; controls.Children.Add(abort);
            DockPanel.SetDock(custom, Dock.Bottom); panel.Children.Add(custom);
            DockPanel.SetDock(conflicts, Dock.Left); panel.Children.Add(conflicts);
            var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Property values / validation", Content = details });
            for (int stage = 1; stage <= 3; stage++)
            {
                var preview = new TabItem { Header = new[] { "Ancestor view", "Current view", "Incoming view" }[stage - 1] };
                var file = Path.Combine(session, stage + ".tcom");
                if (File.Exists(file))
                {
                    var entry = ModelPackage.Read(file).Parts.FirstOrDefault(p => (p.Key.StartsWith("Previews/", StringComparison.OrdinalIgnoreCase) || p.Key.StartsWith("Interchange/Previews/", StringComparison.OrdinalIgnoreCase)) && p.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                    if (entry.Value != null)
                        using (var stream = new MemoryStream(entry.Value)) { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); preview.Content = new Image { Source = bitmap, Stretch = Stretch.Uniform }; }
                }
                if (preview.Content == null) preview.Content = new TextBlock { Text = "This input has no embedded preview. Layout values remain available in the conflict paths under views/visuals.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
                tabs.Items.Add(preview);
            }
            panel.Children.Add(tabs);
            conflicts.SelectionChanged += (s, e) => { if (conflicts.SelectedIndex >= 0) details.Text = ModelJson.Text(items[conflicts.SelectedIndex]); };
            Refresh();
        }
        private void Refresh()
        {
            ModelMerge.PreviewSession(session); items = ModelJson.Array(ModelJson.Get(ModelJson.Read(Path.Combine(session, "conflicts.json")), "conflicts")).ToList();
            conflicts.ItemsSource = items.Select(i => ModelJson.Str(i, "path")).ToArray();
            status.Text = items.Count + " unresolved conflicts. Inputs and decisions are retained in " + session;
            if (items.Count > 0) conflicts.SelectedIndex = 0;
        }
        private void Choose(string choice)
        {
            try
            {
                if (conflicts.SelectedIndex < 0) return;
                var path = ModelJson.Str(items[conflicts.SelectedIndex], "path");
                if (path == "$validation")
                {
                    if (choice != "current" && choice != "incoming" && choice != "ancestor") throw new GitWorkflowException("VALIDATION", "Choose a complete valid input, or reset decisions and reconcile the conflicting properties. Validation cannot be overridden.");
                    if (MessageBox.Show(this, "Replace the entire candidate with the " + choice + " package, then validate it?", "Whole package resolution", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    path = "$package";
                }
                var file = Path.Combine(session, "resolutions.json"); var graph = ModelJson.Read(file);
                var rows = ModelJson.Array(ModelJson.Get(graph, "resolutions")).Where(r => ModelJson.Str(r, "path") != path).ToList();
                rows.Add(ModelJson.Map("path", path, "choice", choice, "value", choice == "custom" ? ModelJson.Parse(custom.Text) : null));
                ModelJson.Obj(graph)["resolutions"] = rows; ModelJson.Write(file, graph); Refresh();
            }
            catch (Exception e) { status.Text = e.Message; }
        }
    }
}
