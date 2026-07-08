// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Minimal desktop dialog for editing package Git sync linkage.
// -------------------------------------------------------------------------------------------

using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public sealed class GitSyncLinkDialogResult
    {
        public bool Accepted;
        public string RemoteUrl;
        public string Branch;
        public string RepositoryPath;
        public string EmbeddedDomainPath;
    }

    public sealed class GitSyncLinkDialog : Window
    {
        private readonly TextBox RemoteUrlText;
        private readonly TextBox BranchText;
        private readonly TextBox RepositoryPathText;
        private readonly TextBox EmbeddedDomainPathText;

        private GitSyncLinkDialog(string PackagePath, bool IsComposition, GitPackageLink Existing)
        {
            this.Title = "Link Git Remote";
            this.Width = 560;
            this.Height = IsComposition ? 300 : 255;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.ResizeMode = ResizeMode.NoResize;

            var Root = new DockPanel();
            Root.Margin = new Thickness(12);
            this.Content = Root;

            var Buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(Buttons, Dock.Bottom);
            Root.Children.Add(Buttons);

            var OkButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(4), IsDefault = true };
            var CancelButton = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(4), IsCancel = true };
            Buttons.Children.Add(OkButton);
            Buttons.Children.Add(CancelButton);

            var Form = new Grid();
            Form.Margin = new Thickness(0, 0, 0, 10);
            Form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            Form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Root.Children.Add(Form);

            this.RemoteUrlText = AddRow(Form, 0, "Remote URL", Existing == null || Existing.Remote == null ? "" : Existing.Remote.Url);
            this.BranchText = AddRow(Form, 1, "Branch", Existing == null || Existing.Remote == null ? "main" : Existing.Remote.Branch);
            this.RepositoryPathText = AddRow(Form, 2, IsComposition ? ".tcom path" : ".tdom path", DefaultPath(PackagePath, Existing, IsComposition));

            if (IsComposition)
                this.EmbeddedDomainPathText = AddRow(Form, 3, "Domain path", DefaultDomainPath(Existing));

            OkButton.Click += delegate
            {
                this.DialogResult = true;
                this.Close();
            };
        }

        public static GitSyncLinkDialogResult ShowDialog(Window Owner, string PackagePath, bool IsComposition, GitPackageLink Existing)
        {
            var Dialog = new GitSyncLinkDialog(PackagePath, IsComposition, Existing);
            Dialog.Owner = Owner;
            var Accepted = Dialog.ShowDialog() == true;

            return new GitSyncLinkDialogResult
            {
                Accepted = Accepted,
                RemoteUrl = Dialog.RemoteUrlText.Text,
                Branch = Dialog.BranchText.Text,
                RepositoryPath = Dialog.RepositoryPathText.Text,
                EmbeddedDomainPath = Dialog.EmbeddedDomainPathText == null ? null : Dialog.EmbeddedDomainPathText.Text
            };
        }

        private static TextBox AddRow(Grid Form, int Row, string LabelText, string Value)
        {
            Form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var Label = new Label { Content = LabelText, Margin = new Thickness(0, 3, 8, 3) };
            Grid.SetRow(Label, Row);
            Grid.SetColumn(Label, 0);
            Form.Children.Add(Label);

            var Text = new TextBox { Text = Value ?? "", Margin = new Thickness(0, 3, 0, 3), MinWidth = 360 };
            Grid.SetRow(Text, Row);
            Grid.SetColumn(Text, 1);
            Form.Children.Add(Text);
            return Text;
        }

        private static string DefaultPath(string PackagePath, GitPackageLink Existing, bool IsComposition)
        {
            if (Existing != null)
            {
                var Baseline = Existing.FindBaseline(IsComposition ? GitPackageLink.KindComposition : GitPackageLink.KindDomain, GitPackageLink.RoleSelf);
                if (Baseline != null)
                    return Baseline.Path;
            }

            return Path.GetFileName(PackagePath);
        }

        private static string DefaultDomainPath(GitPackageLink Existing)
        {
            if (Existing == null)
                return "";

            var Baseline = Existing.Baselines.FirstOrDefault(Item =>
                Item.Kind == GitPackageLink.KindDomain &&
                Item.Role == GitPackageLink.RoleEmbeddedDomainSource);
            return Baseline == null ? "" : Baseline.Path;
        }
    }
}
