// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Installer custom action for exposing the CLI shim from a new Command Prompt.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Instrumind.ThinkComposer.InstallerActions
{
    [RunInstaller(true)]
    public sealed class PathEnvironmentInstaller : Installer
    {
        private const int HWND_BROADCAST = 0xffff;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int SMTO_ABORTIFHUNG = 0x0002;

        public override void Install(IDictionary stateSaver)
        {
            base.Install(stateSaver);
            UpdateMachinePath(GetTargetDirectory(), true);
        }

        public override void Rollback(IDictionary savedState)
        {
            UpdateMachinePath(GetTargetDirectory(), false);
            base.Rollback(savedState);
        }

        public override void Uninstall(IDictionary savedState)
        {
            UpdateMachinePath(GetTargetDirectory(), false);
            base.Uninstall(savedState);
        }

        private string GetTargetDirectory()
        {
            var TargetPath = this.Context == null ? null : this.Context.Parameters["targetpath"];
            if (!String.IsNullOrWhiteSpace(TargetPath))
                return NormalizeDirectory(Path.GetDirectoryName(TargetPath));

            var TargetDirectory = this.Context == null ? null : this.Context.Parameters["targetdir"];
            if (String.IsNullOrWhiteSpace(TargetDirectory))
                TargetDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            return NormalizeDirectory(TargetDirectory);
        }

        private void UpdateMachinePath(string InstallDirectory, bool Add)
        {
            if (String.IsNullOrWhiteSpace(InstallDirectory))
                return;

            var CurrentPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? String.Empty;
            var Entries = SplitPath(CurrentPath).ToList();
            var AlreadyPresent = Entries.Any(Entry => SameDirectory(Entry, InstallDirectory));

            if (Add)
            {
                if (AlreadyPresent)
                {
                    Log("ThinkComposer CLI install folder is already present in machine Path: " + InstallDirectory);
                    return;
                }

                Entries.Add(InstallDirectory);
                Log("Adding ThinkComposer CLI install folder to machine Path: " + InstallDirectory);
            }
            else
            {
                if (!AlreadyPresent)
                {
                    Log("ThinkComposer CLI install folder is not present in machine Path: " + InstallDirectory);
                    return;
                }

                Entries = Entries.Where(Entry => !SameDirectory(Entry, InstallDirectory)).ToList();
                Log("Removing ThinkComposer CLI install folder from machine Path: " + InstallDirectory);
            }

            Environment.SetEnvironmentVariable("Path", String.Join(";", Entries), EnvironmentVariableTarget.Machine);
            BroadcastEnvironmentChange();
        }

        private void Log(string Message)
        {
            if (this.Context != null)
                this.Context.LogMessage(Message);
        }

        private static IEnumerable<string> SplitPath(string PathValue)
        {
            return (PathValue ?? String.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Entry => Entry.Trim().Trim('"'))
                .Where(Entry => Entry.Length > 0);
        }

        private static bool SameDirectory(string First, string Second)
        {
            return String.Equals(NormalizeDirectory(First), NormalizeDirectory(Second), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectory(string DirectoryPath)
        {
            if (String.IsNullOrWhiteSpace(DirectoryPath))
                return String.Empty;

            var FullPath = Path.GetFullPath(DirectoryPath.Trim().Trim('"'));
            var Root = Path.GetPathRoot(FullPath);

            while (FullPath.Length > Root.Length &&
                   (FullPath.EndsWith("\\", StringComparison.Ordinal) ||
                    FullPath.EndsWith("/", StringComparison.Ordinal)))
                FullPath = FullPath.Substring(0, FullPath.Length - 1);

            return FullPath;
        }

        private static void BroadcastEnvironmentChange()
        {
            UIntPtr Result;
            SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 5000, out Result);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, UIntPtr wParam, string lParam, int fuFlags, int uTimeout, out UIntPtr lpdwResult);
    }
}
