using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public sealed partial class RepositoryWindow
    {
        private RepositoryStatus repositoryStatus;
        private readonly TextBlock repositoryName = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold };
        private readonly TextBlock trackingInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 6, 12, 10) };
        private readonly TextBlock feedback = new TextBlock { Text = "Select files to review, then stage the changes you want to commit.", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock fileHeading = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14) };
        private readonly TextBlock guidance = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 6), FontSize = 12 };
        private readonly TextBox preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), BorderThickness = new Thickness(0), Padding = new Thickness(14), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly TextBox commitSummary = new TextBox { Margin = new Thickness(0, 4, 0, 8), Padding = new Thickness(7) };
        private readonly TextBox commitDescription = new TextBox { AcceptsReturn = true, Height = 42, Padding = new Thickness(7), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly TextBlock commitHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
        private readonly WrapPanel menuBar = new WrapPanel();
        private readonly Expander operationLog = new Expander { Header = "Operation details", Margin = new Thickness(12, 0, 12, 8) };
        private readonly ListBox history = new ListBox { Margin = new Thickness(6), DisplayMemberPath = "Label" };
        private ContextMenu currentMenu;
        private Button branchButton, fetchButton, pullButton, pushButton, stageButton, commitButton, cancelButton, unstagedButton, stagedButton, changesTab, historyTab;
        private Grid changePage;
        private bool stagedView, historyView;
        private int previewVersion;
        private string displayedSignature;

        private void BuildLayout()
        {
            RepositoryTheme.Apply(this);
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            var root = new DockPanel(); root.SetResourceReference(Panel.BackgroundProperty, SystemColors.WindowBrushKey); Content = root;
            var header = new StackPanel { Margin = new Thickness(16, 12, 16, 8) };
            DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            var toolbar = new DockPanel(); header.Children.Add(toolbar);
            var sync = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(sync, Dock.Right); toolbar.Children.Add(sync);
            branchButton = ActionButton("Current branch ▾", ShowBranches); sync.Children.Add(branchButton);
            fetchButton = ActionButton("Fetch", () => RunCommand("fetch")); fetchButton.ToolTip = "Check the remote for new commits. Your files stay unchanged."; sync.Children.Add(fetchButton);
            pullButton = ActionButton("Pull", () => RunCommand("pull")); sync.Children.Add(pullButton);
            pushButton = ActionButton("Push", Push); sync.Children.Add(pushButton);
            toolbar.Children.Add(repositoryName);
            var pathRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; header.Children.Add(pathRow);
            var open = ActionButton("Open", async () => { displayedSignature = null; await Refresh(); }); DockPanel.SetDock(open, Dock.Right); pathRow.Children.Add(open);
            location.MinWidth = 0; location.ToolTip = "Repository directory. Choose Open after changing this path.";
            location.TextChanged += (s, e) => UpdateActions();
            location.KeyDown += async (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) await Safely(() => Refresh()); };
            pathRow.Children.Add(location);
            header.Children.Add(summary); header.Children.Add(menuBar);

            var footer = new DockPanel { Margin = new Thickness(12, 6, 12, 6) };
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            cancelButton = ActionButton("Cancel operation", () => { if (cancellation != null) cancellation.Cancel(); return Task.FromResult(0); });
            DockPanel.SetDock(cancelButton, Dock.Right); footer.Children.Add(cancelButton); footer.Children.Add(feedback);
            output.Height = 96; output.FontFamily = new FontFamily("Consolas"); operationLog.Content = output;
            DockPanel.SetDock(operationLog, Dock.Bottom); root.Children.Add(operationLog);
            DockPanel.SetDock(trackingInfo, Dock.Bottom); root.Children.Add(trackingInfo);

            var body = new Grid { Margin = new Thickness(12, 0, 12, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360), MinWidth = 300 });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 }); root.Children.Add(body);
            var left = new DockPanel(); body.Children.Add(left);
            var navigation = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(navigation, Dock.Top); left.Children.Add(navigation);
            changesTab = ActionButton("Changes", () => ShowPage(false)); changesTab.FontWeight = FontWeights.Bold;
            historyTab = ActionButton("History", () => ShowPage(true));
            navigation.Children.Add(changesTab);
            navigation.Children.Add(historyTab);
            navigation.Children.Add(ActionButton("Refresh", () => Refresh()));
            var pages = new Grid(); left.Children.Add(pages);
            changePage = new Grid(); pages.Children.Add(changePage);
            changePage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            changePage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            changePage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            changePage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            changePage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var filters = new StackPanel { Orientation = Orientation.Horizontal };
            unstagedButton = ActionButton("Changes (0)", () => Filter(false)); stagedButton = ActionButton("Staged (0)", () => Filter(true));
            filters.Children.Add(unstagedButton); filters.Children.Add(stagedButton); changePage.Children.Add(filters);
            Grid.SetRow(guidance, 1); changePage.Children.Add(guidance);
            changes.MinHeight = 60; changes.DisplayMemberPath = "DisplayLabel";
            changes.SelectionChanged += async (s, e) => { UpdateActions(); await PreviewSelection(); };
            Grid.SetRow(changes, 2); changePage.Children.Add(changes);
            stageButton = ActionButton("Stage selected files", () => Selected(stagedView ? "unstage" : "stage"));
            Grid.SetRow(stageButton, 3); changePage.Children.Add(stageButton);
            var commit = new StackPanel { Margin = new Thickness(10, 4, 10, 6) };
            Grid.SetRow(commit, 4); changePage.Children.Add(commit);
            commit.Children.Add(new TextBlock { Text = "Commit summary (required)", FontWeight = FontWeights.SemiBold }); commit.Children.Add(commitSummary);
            commit.Children.Add(new TextBlock { Text = "Description (optional)", Margin = new Thickness(0, 0, 0, 4) }); commit.Children.Add(commitDescription);
            commit.Children.Add(commitHint);
            commitButton = ActionButton("Commit staged changes", async () =>
            {
                var message = commitSummary.Text.Trim() + (String.IsNullOrWhiteSpace(commitDescription.Text) ? "" : "\n\n" + commitDescription.Text.Trim());
                await RunCommand("commit", "--message", message); commitSummary.Clear(); commitDescription.Clear();
            });
            commitButton.SetResourceReference(BackgroundProperty, SystemColors.HighlightBrushKey); commitButton.SetResourceReference(ForegroundProperty, SystemColors.HighlightTextBrushKey);
            commit.Children.Add(commitButton); commitSummary.TextChanged += (s, e) => UpdateActions();
            history.Visibility = Visibility.Collapsed; pages.Children.Add(history);
            history.SelectionChanged += async (s, e) => await Safely(PreviewCommit);
            var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(splitter, 1); body.Children.Add(splitter);
            var right = new DockPanel(); Grid.SetColumn(right, 2); body.Children.Add(right);
            DockPanel.SetDock(fileHeading, Dock.Top); right.Children.Add(fileHeading); right.Children.Add(preview);
            fileHeading.Text = "Review your changes"; preview.Text = "Select a file on the left to see its changes.\n\n1. Stage the files you want to include.\n2. Enter a summary and commit the staged changes.\n3. Push when you are ready to share your commits.\n\nSaving a model does not stage, commit, or push it.";
        }

        private Button ActionButton(string text, Func<Task> action)
        {
            var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(12, 7, 12, 7) };
            button.Click += async (s, e) => await Safely(action); actions.Add(button); return button;
        }

        private async Task Safely(Func<Task> action)
        {
            try { await action(); }
            catch (OperationCanceledException) { feedback.Text = "Canceled. Your repository status has been refreshed."; }
            catch (Exception problem) { feedback.Text = problem.Message; output.Text = problem.Message; operationLog.IsExpanded = true; }
        }

        private void BeginMenu(string title)
        {
            var menu = new ContextMenu(); menu.Resources.MergedDictionaries.Add(RepositoryTheme.Resources());
            currentMenu = menu;
            var button = ActionButton(title + " ▾", () => { menu.IsOpen = true; return Task.FromResult(0); });
            menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menuBar.Children.Add(button);
            if (title == "Repository") Add("Open repository directory…", async () => { location.Text = Ask("Repository directory", location.Text); await Refresh(); });
        }

        private static bool IsStaged(RepositoryChange c) { return !c.Conflict && c.Index != " " && c.Index != "?"; }
        private static bool IsUnstaged(RepositoryChange c) { return c.Conflict || c.WorkTree != " "; }
        private Task Filter(bool staged) { stagedView = staged; displayedSignature = null; UpdateFileList(); UpdateActions(); return Task.FromResult(0); }

        private void UpdateFileList()
        {
            if (repositoryStatus == null) return;
            var all = repositoryStatus.Changes;
            var staged = all.Count(IsStaged); var unstaged = all.Count(IsUnstaged);
            unstagedButton.Content = "Changes (" + unstaged + ")"; stagedButton.Content = "Staged (" + staged + ")";
            unstagedButton.FontWeight = stagedView ? FontWeights.Normal : FontWeights.Bold;
            stagedButton.FontWeight = stagedView ? FontWeights.Bold : FontWeights.Normal;
            guidance.Text = stagedView ? "These files are ready to commit. Unstage to leave one for later." : unstaged == 0 ? "No unstaged changes. Edit and save to begin." : "Select files to review and stage for your next commit.";
            var items = all.Where(stagedView ? (Func<RepositoryChange, bool>)IsStaged : IsUnstaged).ToList();
            var current = changes.SelectedItem as RepositoryChange;
            var stamp = current == null ? "" : File.GetLastWriteTimeUtc(Path.Combine(repositoryStatus.Root, current.Path)).Ticks.ToString();
            var signature = repositoryStatus.Root + "|" + stagedView + "|" + stamp + "|" + String.Join("\0", items.Select(c => c.Index + c.WorkTree + c.Path));
            if (signature == displayedSignature) return;
            displayedSignature = signature;
            var selected = changes.SelectedItems.Cast<RepositoryChange>().Select(c => c.Path).ToArray();
            changes.ItemsSource = items;
            foreach (var c in items.Where(c => selected.Contains(c.Path))) changes.SelectedItems.Add(c);
            if (changes.SelectedItem == null && items.Count > 0) changes.SelectedItem = items.FirstOrDefault(c => artifact != null && String.Equals(Path.Combine(repositoryStatus.Root, c.Path.Replace('/', Path.DirectorySeparatorChar)), artifact, StringComparison.OrdinalIgnoreCase)) ?? items[0];
            if (items.Count == 0 && !historyView) { fileHeading.Text = stagedView ? "Nothing staged yet" : "No changes to review"; preview.Text = stagedView ? "Choose Changes, select files, and stage them to prepare a commit." : staged > 0 ? "Your changes are staged. Choose Staged to review exactly what will be committed." : "Your working tree is clean. Edit and save a file to begin a new commit."; }
        }

        private void UpdateActions()
        {
            foreach (var control in actions) control.IsEnabled = !busy;
            if (commitButton == null) return;
            var ready = !busy && repositoryStatus != null && String.Equals(location.Text, repositoryStatus.Root, StringComparison.OrdinalIgnoreCase);
            var count = repositoryStatus == null ? 0 : repositoryStatus.Changes.Count(IsStaged);
            commitButton.IsEnabled = ready && (count > 0 || repositoryStatus.Operation == "MERGE_HEAD") && !repositoryStatus.Changes.Any(c => c.Conflict) && !String.IsNullOrWhiteSpace(commitSummary.Text);
            commitButton.Content = "Commit " + count + " staged file" + (count == 1 ? "" : "s");
            commitHint.Text = count == 0 ? "Stage files to enable commit. Push is separate." : "Commits all " + count + " staged files, including those staged in other apps. Push is separate.";
            stageButton.Content = stagedView ? "Unstage selected files" : "Stage selected files";
            stageButton.IsEnabled = ready && !historyView && changes.SelectedItems.Count > 0;
            branchButton.IsEnabled = ready; fetchButton.IsEnabled = ready;
            pullButton.IsEnabled = ready && repositoryStatus.Upstream != null;
            pullButton.Content = repositoryStatus != null && repositoryStatus.Behind > 0 ? "Pull (" + repositoryStatus.Behind + ")" : "Pull";
            pushButton.IsEnabled = ready; pushButton.Content = repositoryStatus != null && repositoryStatus.Upstream == null ? "Publish branch" : repositoryStatus != null && repositoryStatus.Ahead > 0 ? "Push (" + repositoryStatus.Ahead + ")" : "Push";
            cancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; cancelButton.IsEnabled = busy;
            location.IsEnabled = !busy; commitSummary.IsEnabled = !busy; commitDescription.IsEnabled = !busy;
        }

        private Task Push()
        {
            return repositoryStatus != null && repositoryStatus.Upstream == null ? RunCommand("push", "--remote", Ask("Publish to remote", "origin"), "--branch", repositoryStatus.Branch) : RunCommand("push");
        }

        private async Task ShowBranches()
        {
            var root = location.Text;
            var result = await Task.Run(() => RepositoryGit.Run(root, new[] { "for-each-ref", "--format=%(refname:short)", "refs/heads/" }).Text);
            var dialog = new Window { Title = "Switch branch", Owner = this, Width = 460, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            RepositoryTheme.Apply(dialog);
            var panel = new DockPanel { Margin = new Thickness(16) }; dialog.Content = panel;
            var hint = new TextBlock { Text = "Choose a local branch. Save open documents and commit or explicitly stash tracked changes before switching.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(hint, Dock.Top); panel.Children.Add(hint);
            var list = new ListBox { ItemsSource = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) };
            var accept = new Button { Content = "Switch branch", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12, 8, 12, 8), IsEnabled = false };
            DockPanel.SetDock(accept, Dock.Bottom); panel.Children.Add(accept); panel.Children.Add(list);
            list.SelectionChanged += (s, e) => accept.IsEnabled = list.SelectedItem != null;
            accept.Click += (s, e) => dialog.DialogResult = true;
            if (dialog.ShowDialog() == true) await RunCommand("switch", "--branch", (string)list.SelectedItem);
        }

        private async Task PreviewSelection()
        {
            var version = ++previewVersion;
            if (historyView || repositoryStatus == null) return;
            var selected = changes.SelectedItem as RepositoryChange;
            if (selected == null) return;
            var root = repositoryStatus.Root; var staged = stagedView;
            preview.TextWrapping = selected.Conflict || selected.Path.EndsWith(".tcom", StringComparison.OrdinalIgnoreCase) || selected.Path.EndsWith(".tdom", StringComparison.OrdinalIgnoreCase) ? TextWrapping.Wrap : TextWrapping.NoWrap;
            fileHeading.Text = selected.Path + (staged ? " • Staged" : " • Working changes");
            preview.Text = "Loading preview…";
            try
            {
                var text = await Task.Run(() => FilePreview(root, selected, staged));
                if (version == previewVersion) preview.Text = text;
            }
            catch (Exception e) { if (version == previewVersion) preview.Text = e.Message; }
        }

        internal static string FilePreview(string root, RepositoryChange selected, bool staged)
        {
            if (selected.Conflict) return "This file has conflicting changes.\n\nFor a ThinkComposer container, choose Model tracking → Resolve selected model.\nFor other files, resolve them in your editor, then stage the resolved file.\n\nThe commit stays blocked until all conflicts are resolved.";
            if (selected.Path.EndsWith(".tcom", StringComparison.OrdinalIgnoreCase) || selected.Path.EndsWith(".tdom", StringComparison.OrdinalIgnoreCase))
                return (selected.Index == "?" ? "New model — not yet tracked by Git." : staged ? "Model container staged for the next commit." : "Model container changed on disk.") + "\n\n" + selected.Path + "\n\nThinkComposer containers are packaged models, so Git cannot display a line-by-line text diff here. Open the model in ThinkComposer to inspect it.\n\n" + (staged ? "Review the staged file list, enter a summary, and commit when ready." : "Choose Stage selected files to include this saved model in your next commit.") + "\n\nOptional: Model tracking → Enable model tracking adds a reviewable JSON projection and model verification. Ordinary Git use does not require it.";
            if (selected.Index == "?")
            {
                var path = RepositoryGit.FilePath(root, selected.Path);
                if (new FileInfo(path).Length > 512 * 1024) return "New file. Preview is limited to files smaller than 512 KB. Open it in your editor to review it before staging.";
                var bytes = File.ReadAllBytes(path);
                if (bytes.Contains((byte)0)) return "New binary file. Open it in its associated application to review it before staging.";
                return "New file — all content will be added when staged.\n\n" + Encoding.UTF8.GetString(bytes);
            }
            var args = new List<string> { "diff", "--no-ext-diff", "--no-textconv", "--no-color" };
            if (staged) args.Add("--cached"); args.Add("--"); args.Add(selected.Path);
            var diff = RepositoryGit.Run(root, args).Text;
            return String.IsNullOrWhiteSpace(diff) ? "No text changes in this view. The file may have metadata-only changes." : diff;
        }

        private sealed class CommitEntry { public string Id { get; set; } public string Label { get; set; } }
        private async Task ShowPage(bool showHistory)
        {
            changesTab.FontWeight = showHistory ? FontWeights.Normal : FontWeights.Bold;
            historyTab.FontWeight = showHistory ? FontWeights.Bold : FontWeights.Normal;
            historyView = showHistory; ++previewVersion; changePage.Visibility = showHistory ? Visibility.Collapsed : Visibility.Visible; history.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;
            UpdateActions();
            if (!showHistory) { await PreviewSelection(); return; }
            fileHeading.Text = "Commit history"; preview.Text = "Select a commit to review its changes.";
            var root = location.Text;
            var result = await Task.Run(() => RepositoryGit.Run(root, new[] { "log", "-50", "--date=short", "--format=%H%x09%h  %ad  %s" }, check: false));
            history.ItemsSource = result.ExitCode == 0 ? result.Text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => { var pair = line.TrimEnd('\r').Split(new[] { '\t' }, 2); return new CommitEntry { Id = pair[0], Label = pair.Length > 1 ? pair[1] : pair[0] }; }).ToList() : new List<CommitEntry>();
            if (history.Items.Count == 0) preview.Text = "No commits to display. Stage your first files and create a commit from Changes.";
        }

        private async Task PreviewCommit()
        {
            var entry = history.SelectedItem as CommitEntry; if (entry == null || !historyView) return;
            var version = ++previewVersion; var root = location.Text;
            fileHeading.Text = entry.Label; preview.Text = "Loading commit…";
            preview.TextWrapping = TextWrapping.NoWrap;
            var text = await Task.Run(() => RepositoryGit.Run(root, new[] { "show", "--no-ext-diff", "--no-textconv", "--no-color", "--stat", "--patch", entry.Id, "--" }).Text);
            if (version == previewVersion) preview.Text = text;
        }
    }

    // Native Windows button chrome ignores dark application brushes in some states.
    // Use the application's dynamic palette for every state, including popup windows.
    internal static class RepositoryTheme
    {
        internal static void Apply(Window window)
        {
            window.Resources.MergedDictionaries.Add(Resources());
            window.SetResourceReference(Control.BackgroundProperty, SystemColors.WindowBrushKey);
            window.SetResourceReference(Control.ForegroundProperty, SystemColors.WindowTextBrushKey);
        }
        internal static ResourceDictionary Resources()
        {
            return (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='Button'>
  <Setter Property='Background' Value='{DynamicResource {x:Static SystemColors.ControlBrushKey}}'/>
  <Setter Property='Foreground' Value='{DynamicResource {x:Static SystemColors.ControlTextBrushKey}}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource {x:Static SystemColors.ControlDarkBrushKey}}'/>
  <Setter Property='BorderThickness' Value='1'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='4' Padding='{TemplateBinding Padding}'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='{DynamicResource {x:Static SystemColors.HighlightBrushKey}}'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='{DynamicResource {x:Static SystemColors.HighlightBrushKey}}'/><Setter TargetName='Chrome' Property='BorderThickness' Value='2'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='Chrome' Property='Opacity' Value='0.7'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Chrome' Property='Opacity' Value='0.45'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='TextBox'>
  <Setter Property='Background' Value='{DynamicResource {x:Static SystemColors.WindowBrushKey}}'/><Setter Property='Foreground' Value='{DynamicResource {x:Static SystemColors.WindowTextBrushKey}}'/>
  <Setter Property='CaretBrush' Value='{DynamicResource {x:Static SystemColors.WindowTextBrushKey}}'/><Setter Property='BorderBrush' Value='{DynamicResource {x:Static SystemColors.ControlDarkBrushKey}}'/>
 </Style>
 <Style TargetType='ListBox'>
  <Setter Property='Background' Value='{DynamicResource {x:Static SystemColors.WindowBrushKey}}'/><Setter Property='Foreground' Value='{DynamicResource {x:Static SystemColors.WindowTextBrushKey}}'/><Setter Property='BorderThickness' Value='0'/>
  <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Auto'/>
 </Style>
 <Style TargetType='ListBoxItem'>
  <Setter Property='Padding' Value='8'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>
   <Border x:Name='Row' Padding='{TemplateBinding Padding}' Background='Transparent'><ContentPresenter Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' ContentTemplateSelector='{TemplateBinding ContentTemplateSelector}' ContentStringFormat='{TemplateBinding ContentStringFormat}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'/></Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Row' Property='Background' Value='{DynamicResource {x:Static SystemColors.ControlBrushKey}}'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter TargetName='Row' Property='Background' Value='{DynamicResource {x:Static SystemColors.HighlightBrushKey}}'/><Setter Property='Foreground' Value='{DynamicResource {x:Static SystemColors.HighlightTextBrushKey}}'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ContextMenu'><Setter Property='Background' Value='{DynamicResource {x:Static SystemColors.WindowBrushKey}}'/><Setter Property='Foreground' Value='{DynamicResource {x:Static SystemColors.WindowTextBrushKey}}'/></Style>
 <Style TargetType='MenuItem'>
  <Setter Property='Padding' Value='12,7'/><Setter Property='Background' Value='{DynamicResource {x:Static SystemColors.WindowBrushKey}}'/><Setter Property='Foreground' Value='{DynamicResource {x:Static SystemColors.WindowTextBrushKey}}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'><Border Padding='{TemplateBinding Padding}' Background='{TemplateBinding Background}'><ContentPresenter ContentSource='Header' RecognizesAccessKey='True'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter Property='Background' Value='{DynamicResource {x:Static SystemColors.HighlightBrushKey}}'/><Setter Property='Foreground' Value='{DynamicResource {x:Static SystemColors.HighlightTextBrushKey}}'/></Trigger></Style.Triggers>
 </Style>
</ResourceDictionary>");
        }
    }
}
