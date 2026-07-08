// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Generic git.exe based synchronization for JSON-authoritative native packages.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

using Instrumind.Common;
using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.Model;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public static class GitPackageSyncService
    {
        private const string StateFileName = "state.json";

        public static string LinkPackage(string Input, string Output, bool InPlace,
                                         string RemoteUrl, string Branch, string RepositoryPath,
                                         string EmbeddedDomainPath)
        {
            ValidateInputFile(Input);

            var InputPath = Path.GetFullPath(Input);
            var OutputPath = ResolveOutputPath(InputPath, Output, InPlace);
            var Inspection = JsonPackagePersistence.Inspect(InputPath);
            var PackageKind = RequirePackageKind(Inspection);

            if (PackageKind == GitPackageLink.KindDomain && !String.IsNullOrWhiteSpace(EmbeddedDomainPath))
                throw new InvalidOperationException("--domain-path is only valid when linking a .tcom composition.");

            var Link = GitPackageLink.Create(RemoteUrl, Branch, RepositoryPath, PackageKind, EmbeddedDomainPath);
            CopyPackageIfNeeded(InputPath, OutputPath);
            JsonPackagePersistence.WriteGitSyncLink(OutputPath, Link);

            return "Git link written to: " + OutputPath + Environment.NewLine +
                   DescribeLink(Link);
        }

        public static string UnlinkPackage(string Input, string Output, bool InPlace)
        {
            ValidateInputFile(Input);

            var InputPath = Path.GetFullPath(Input);
            var OutputPath = ResolveOutputPath(InputPath, Output, InPlace);
            CopyPackageIfNeeded(InputPath, OutputPath);
            JsonPackagePersistence.WriteGitSyncLink(OutputPath, null);

            return "Git link removed from: " + OutputPath;
        }

        public static string StatusPackage(string Input)
        {
            ValidateInputFile(Input);

            var InputPath = Path.GetFullPath(Input);
            var Inspection = JsonPackagePersistence.Inspect(InputPath);
            var PackageKind = RequirePackageKind(Inspection);
            var Link = RequireGitLink(InputPath);
            var Baseline = RequireSelfBaseline(Link, PackageKind);
            var Repository = EnsureRepository(Link);
            var RemoteHead = GetHeadCommit(Repository);
            var SourcePath = ResolveRepositoryFile(Repository, Baseline.Path);
            var State = LoadState();
            var Entry = State.Get(EntryKey(Link, Baseline));

            State.Put(CreateStateEntry(Link, Baseline, RemoteHead, InputPath));
            SaveState(State);

            var Builder = new StringBuilder();
            Builder.AppendLine("Git sync status:");
            Builder.AppendLine("  package: " + InputPath);
            Builder.AppendLine("  kind: " + PackageKind);
            Builder.AppendLine("  remote: " + GitPackageLink.RedactRemoteUrl(Link.Remote.Url));
            Builder.AppendLine("  branch: " + Link.Remote.Branch);
            Builder.AppendLine("  baseline: " + Baseline.Path);
            Builder.AppendLine("  remoteHead: " + RemoteHead.ToStringAlways("<unknown>"));
            Builder.AppendLine("  baselineExists: " + File.Exists(SourcePath).ToString().ToLowerInvariant());
            Builder.AppendLine("  previousSeenHead: " + (Entry == null ? "<none>" : Entry.RemoteCommit.ToStringAlways("<none>")));
            return Builder.ToString().TrimEnd();
        }

        public static GitPackageRemoteStatus GetRemoteStatus(string Input)
        {
            ValidateInputFile(Input);

            var InputPath = Path.GetFullPath(Input);
            var Inspection = JsonPackagePersistence.Inspect(InputPath);
            var PackageKind = RequirePackageKind(Inspection);
            var Link = RequireGitLink(InputPath);
            var Baseline = RequireSelfBaseline(Link, PackageKind);
            var Repository = EnsureRepository(Link, true);
            var RemoteHead = GetHeadCommit(Repository);
            var SourcePath = ResolveRepositoryFile(Repository, Baseline.Path);
            var SourceExists = File.Exists(SourcePath);
            var LocalHash = JsonPackagePersistence.ComputeAuthoritativeJsonHash(InputPath, PackageKind);
            var RemoteHash = SourceExists
                             ? JsonPackagePersistence.ComputeAuthoritativeJsonHash(SourcePath, PackageKind)
                             : null;
            var State = LoadState();
            var Entry = State.Get(EntryKey(Link, Baseline));

            return new GitPackageRemoteStatus
            {
                PackagePath = InputPath,
                PackageKind = PackageKind,
                RemoteDisplayUrl = GitPackageLink.RedactRemoteUrl(Link.Remote.Url),
                Branch = Link.Remote.Branch,
                BaselinePath = Baseline.Path,
                RemoteHead = RemoteHead,
                PreviousSeenHead = Entry == null ? null : Entry.RemoteCommit,
                BaselineExists = SourceExists,
                LocalAuthoritativeJsonHash = LocalHash,
                RemoteAuthoritativeJsonHash = RemoteHash,
                HasRemoteUpdate = SourceExists &&
                                  !String.IsNullOrWhiteSpace(LocalHash) &&
                                  !String.IsNullOrWhiteSpace(RemoteHash) &&
                                  !String.Equals(LocalHash, RemoteHash, StringComparison.OrdinalIgnoreCase)
            };
        }

        public static GitPackagePullResult PullPackage(string Input, string Output, bool InPlace, string BackupDirectory)
        {
            ValidateInputFile(Input);

            var InputPath = Path.GetFullPath(Input);
            var OutputPath = ResolveOutputPath(InputPath, Output, InPlace);
            var Inspection = JsonPackagePersistence.Inspect(InputPath);
            var PackageKind = RequirePackageKind(Inspection);
            var Link = RequireGitLink(InputPath);
            var Baseline = RequireSelfBaseline(Link, PackageKind);
            var Repository = EnsureRepository(Link);
            var RemoteHead = GetHeadCommit(Repository);
            var SourcePath = ResolveRepositoryFile(Repository, Baseline.Path);

            if (!File.Exists(SourcePath))
                throw new FileNotFoundException("Linked package was not found in the Git repository: " + Baseline.Path, SourcePath);

            EnsureParentDirectory(OutputPath);
            var TempPath = Path.Combine(Path.GetDirectoryName(OutputPath), Path.GetFileName(OutputPath) + ".gitsync.tmp");
            File.Copy(SourcePath, TempPath, true);

            try
            {
                ValidateJsonAuthoritativePackage(TempPath, PackageKind);

                string BackupPath = null;
                if (SamePath(InputPath, OutputPath))
                    BackupPath = CreateBackup(InputPath, BackupDirectory);

                File.Copy(TempPath, OutputPath, true);
                JsonPackagePersistence.WriteGitSyncLink(OutputPath, Link);

                var State = LoadState();
                State.Put(CreateStateEntry(Link, Baseline, RemoteHead, OutputPath));
                SaveState(State);

                return new GitPackagePullResult
                {
                    OutputPath = OutputPath,
                    BackupPath = BackupPath,
                    RemoteHead = RemoteHead,
                    Message = "Git pull completed from " + GitPackageLink.RedactRemoteUrl(Link.Remote.Url) +
                              " [" + Link.Remote.Branch + "] " + Baseline.Path +
                              Environment.NewLine + "Output: " + OutputPath +
                              (String.IsNullOrWhiteSpace(BackupPath) ? "" : Environment.NewLine + "Backup: " + BackupPath)
                };
            }
            finally
            {
                TryDelete(TempPath);
            }
        }

        public static string PushComposition(string Input, string Message)
        {
            ValidateInputFile(Input);

            var InputPath = Path.GetFullPath(Input);
            var Inspection = JsonPackagePersistence.Inspect(InputPath);
            var PackageKind = RequirePackageKind(Inspection);
            if (!String.Equals(PackageKind, GitPackageLink.KindComposition, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Domain push is not supported in this version. Link and pull .tdom files instead.");

            ValidateJsonAuthoritativePackage(InputPath, GitPackageLink.KindComposition);

            var Link = RequireGitLink(InputPath);
            var Baseline = RequireSelfBaseline(Link, GitPackageLink.KindComposition);
            var Repository = EnsureRepository(Link);
            var RemoteHead = GetHeadCommit(Repository);
            var State = LoadState();
            var Existing = State.Get(EntryKey(Link, Baseline));

            if (Existing != null &&
                !String.IsNullOrWhiteSpace(Existing.RemoteCommit) &&
                !String.Equals(Existing.RemoteCommit, RemoteHead, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Remote branch advanced since the last local sync. Pull from Git before pushing.");

            var TargetPath = ResolveRepositoryFile(Repository, Baseline.Path);
            EnsureParentDirectory(TargetPath);
            File.Copy(InputPath, TargetPath, true);

            var RelativePath = Baseline.Path;
            var Status = RunGit(Repository, "status", "--porcelain", "--", RelativePath).Output.Trim();
            if (String.IsNullOrWhiteSpace(Status))
            {
                State.Put(CreateStateEntry(Link, Baseline, RemoteHead, InputPath));
                SaveState(State);
                return "No package changes to push for " + RelativePath + ".";
            }

            RunGit(Repository, "add", "--", RelativePath);
            RunGit(Repository, "commit", "-m", String.IsNullOrWhiteSpace(Message) ? "Update ThinkComposer composition" : Message, "--", RelativePath);
            RunGit(Repository, "push", "origin", Link.Remote.Branch);

            var NewHead = GetHeadCommit(Repository);
            State.Put(CreateStateEntry(Link, Baseline, NewHead, InputPath));
            SaveState(State);

            return "Git push completed." + Environment.NewLine +
                   "  remote: " + GitPackageLink.RedactRemoteUrl(Link.Remote.Url) + Environment.NewLine +
                   "  branch: " + Link.Remote.Branch + Environment.NewLine +
                   "  path: " + RelativePath + Environment.NewLine +
                   "  commit: " + NewHead;
        }

        public static string PullEmbeddedDomainBaseline(string CompositionPackagePath, string OutputDirectory)
        {
            ValidateInputFile(CompositionPackagePath);

            var InputPath = Path.GetFullPath(CompositionPackagePath);
            var Link = RequireGitLink(InputPath);
            var Baseline = Link.FindBaseline(GitPackageLink.KindDomain, GitPackageLink.RoleEmbeddedDomainSource);
            if (Baseline == null)
                throw new InvalidOperationException("This composition is not linked to an embedded Domain baseline.");

            var Repository = EnsureRepository(Link);
            var SourcePath = ResolveRepositoryFile(Repository, Baseline.Path);
            if (!File.Exists(SourcePath))
                throw new FileNotFoundException("Linked Domain was not found in the Git repository: " + Baseline.Path, SourcePath);

            var TargetDirectory = String.IsNullOrWhiteSpace(OutputDirectory)
                                  ? Path.Combine(GitSyncRoot, "pulled-domains")
                                  : Path.GetFullPath(OutputDirectory);
            Directory.CreateDirectory(TargetDirectory);

            var TargetPath = Path.Combine(TargetDirectory,
                                          Path.GetFileNameWithoutExtension(Baseline.Path) + "." +
                                          DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".tdom");
            File.Copy(SourcePath, TargetPath, true);
            ValidateJsonAuthoritativePackage(TargetPath, GitPackageLink.KindDomain);
            return TargetPath;
        }

        public static string DescribeLink(GitPackageLink Link)
        {
            if (Link == null)
                return "gitSync: false";

            var Builder = new StringBuilder();
            Builder.AppendLine("gitSync: true");
            Builder.AppendLine("  remote: " + GitPackageLink.RedactRemoteUrl(Link.Remote == null ? null : Link.Remote.Url));
            Builder.AppendLine("  branch: " + (Link.Remote == null ? "" : Link.Remote.Branch));
            Builder.AppendLine("  baselines:");
            foreach (var Baseline in Link.Baselines)
                Builder.AppendLine("    " + Baseline.Kind + " " + Baseline.Role + ": " + Baseline.Path);

            return Builder.ToString().TrimEnd();
        }

        private static string GitSyncRoot
        {
            get
            {
                var Root = AppExec.ApplicationUserDirectory;
                if (String.IsNullOrWhiteSpace(Root))
                    Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Instrumind", "ThinkComposer");

                return Path.Combine(Root, "GitSync");
            }
        }

        private static string RepositoriesRoot
        {
            get { return Path.Combine(GitSyncRoot, "repositories"); }
        }

        private static string StatePath
        {
            get { return Path.Combine(GitSyncRoot, StateFileName); }
        }

        private static string EnsureRepository(GitPackageLink Link)
        {
            return EnsureRepository(Link, false);
        }

        private static string EnsureRepository(GitPackageLink Link, bool NonInteractive)
        {
            Link.Validate();
            Directory.CreateDirectory(RepositoriesRoot);

            var Repository = Path.Combine(RepositoriesRoot, HashText(Link.Remote.Url + "\n" + Link.Remote.Branch));
            var GitDirectory = Path.Combine(Repository, ".git");

            if (!Directory.Exists(GitDirectory))
            {
                if (Directory.Exists(Repository) && Directory.EnumerateFileSystemEntries(Repository).Any())
                    throw new InvalidOperationException("Git sync cache directory exists but is not a Git repository: " + Repository);

                RunGit(null, NonInteractive, "clone", "--branch", Link.Remote.Branch, "--single-branch", Link.Remote.Url, Repository);
            }
            else
            {
                RunGit(Repository, NonInteractive, "fetch", "origin", Link.Remote.Branch);
                RunGit(Repository, NonInteractive, "checkout", Link.Remote.Branch);
                RunGit(Repository, NonInteractive, "reset", "--hard", "origin/" + Link.Remote.Branch);
                RunGit(Repository, NonInteractive, "clean", "-fd");
            }

            return Repository;
        }

        private static string GetHeadCommit(string Repository)
        {
            return RunGit(Repository, "rev-parse", "HEAD").Output.Trim();
        }

        private static GitPackageBaseline RequireSelfBaseline(GitPackageLink Link, string PackageKind)
        {
            var Baseline = Link.FindBaseline(PackageKind, GitPackageLink.RoleSelf);
            if (Baseline == null)
                throw new InvalidOperationException("gitSync does not define a self baseline for package kind: " + PackageKind + ".");

            return Baseline;
        }

        private static GitPackageLink RequireGitLink(string PackagePath)
        {
            var Link = JsonPackagePersistence.ReadGitSyncLink(PackagePath);
            if (Link == null)
                throw new InvalidOperationException("Package is not linked to Git. Use 'thinkcomposer git link' first.");

            Link.Validate();
            return Link;
        }

        private static string RequirePackageKind(JsonPackagePersistence.PackagePersistenceInspection Inspection)
        {
            if (Inspection == null || String.IsNullOrWhiteSpace(Inspection.PackageKind) || Inspection.PackageKind == "unknown")
                throw new InvalidOperationException("Cannot determine package kind.");

            if (!String.Equals(Inspection.PackageKind, GitPackageLink.KindComposition, StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(Inspection.PackageKind, GitPackageLink.KindDomain, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsupported package kind for Git sync: " + Inspection.PackageKind + ".");

            return Inspection.PackageKind;
        }

        private static void ValidateJsonAuthoritativePackage(string PackagePath, string PackageKind)
        {
            var Inspection = JsonPackagePersistence.Inspect(PackagePath);
            var ActualKind = RequirePackageKind(Inspection);
            if (!String.Equals(ActualKind, PackageKind, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Linked package kind mismatch. Expected " + PackageKind + " but found " + ActualKind + ".");

            if (!Inspection.JsonAuthoritative)
                throw new InvalidOperationException("Git sync requires JSON-authoritative packages.");

            if (String.Equals(PackageKind, GitPackageLink.KindComposition, StringComparison.OrdinalIgnoreCase))
                JsonPackagePersistence.ReadCompositionPackage(PackagePath);
            else
                JsonPackagePersistence.ReadDomainPackage(PackagePath);
        }

        private static string ResolveRepositoryFile(string Repository, string RelativePath)
        {
            GitPackageLink.ValidateRepositoryPath(RelativePath);
            var FullPath = Path.GetFullPath(Path.Combine(Repository, RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var Root = Path.GetFullPath(Repository).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!FullPath.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Linked path resolves outside the Git repository: " + RelativePath);

            return FullPath;
        }

        private static GitProcessResult RunGit(string WorkingDirectory, params string[] Arguments)
        {
            return RunGit(WorkingDirectory, false, Arguments);
        }

        private static GitProcessResult RunGit(string WorkingDirectory, bool NonInteractive, params string[] Arguments)
        {
            var Start = new ProcessStartInfo();
            Start.FileName = "git.exe";
            Start.Arguments = String.Join(" ", Arguments.Select(QuoteArgument).ToArray());
            Start.WorkingDirectory = String.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory;
            Start.UseShellExecute = false;
            Start.RedirectStandardOutput = true;
            Start.RedirectStandardError = true;
            Start.CreateNoWindow = true;
            if (NonInteractive)
            {
                Start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                Start.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
                Start.EnvironmentVariables["GCM_MODAL_PROMPT"] = "false";
            }

            using (var Process = new Process())
            {
                Process.StartInfo = Start;
                try
                {
                    Process.Start();
                }
                catch (Exception Problem)
                {
                    throw new InvalidOperationException("Cannot start git.exe. Ensure Git is installed and on PATH. " + Problem.Message, Problem);
                }

                var Output = Process.StandardOutput.ReadToEnd();
                var Error = Process.StandardError.ReadToEnd();
                Process.WaitForExit();

                if (Process.ExitCode != 0)
                    throw new InvalidOperationException("git " + String.Join(" ", Arguments.Select(RedactArgument).ToArray()) +
                                                        " failed with exit code " + Process.ExitCode + "." +
                                                        Environment.NewLine + RedactText(Output + Error));

                return new GitProcessResult { Output = Output, Error = Error };
            }
        }

        private static string QuoteArgument(string Argument)
        {
            if (Argument == null)
                return "\"\"";

            return "\"" + Argument.Replace("\"", "\\\"") + "\"";
        }

        private static string RedactArgument(string Argument)
        {
            return GitPackageLink.RedactRemoteUrl(Argument);
        }

        private static string RedactText(string Text)
        {
            if (String.IsNullOrWhiteSpace(Text))
                return Text;

            return Regex.Replace(Text, @"\b([A-Za-z][A-Za-z0-9+\-.]*://)([^/\s@]+)@", Match =>
            {
                var Scheme = Match.Groups[1].Value;
                var UserInfo = Match.Groups[2].Value;
                if (String.IsNullOrEmpty(UserInfo))
                    return Match.Value;

                return Scheme + "<redacted>@";
            });
        }

        private static string ResolveOutputPath(string InputPath, string Output, bool InPlace)
        {
            if (InPlace)
            {
                if (!String.IsNullOrWhiteSpace(Output) && !SamePath(InputPath, Output))
                    throw new InvalidOperationException("--in-place requires --output to match --input.");

                return InputPath;
            }

            if (String.IsNullOrWhiteSpace(Output))
                throw new InvalidOperationException("Missing --output. Use --in-place to update the input package.");

            if (SamePath(InputPath, Output))
                throw new InvalidOperationException("Refusing to overwrite input. Use --in-place with --output set to the input path.");

            return Path.GetFullPath(Output);
        }

        private static void ValidateInputFile(string Input)
        {
            if (String.IsNullOrWhiteSpace(Input))
                throw new InvalidOperationException("Missing --input.");

            if (!File.Exists(Input))
                throw new FileNotFoundException("Input file not found: " + Input, Input);

            var Extension = Path.GetExtension(Input).TrimStart('.');
            if (!String.Equals(Extension, Composition.FILE_EXTENSION_COMPOSITION, StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(Extension, Domain.FILE_EXTENSION_DOMAIN, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Input must have .tcom or .tdom extension.");
        }

        private static void CopyPackageIfNeeded(string InputPath, string OutputPath)
        {
            EnsureParentDirectory(OutputPath);
            if (!SamePath(InputPath, OutputPath))
                File.Copy(InputPath, OutputPath, true);
        }

        private static string CreateBackup(string InputPath, string BackupDirectory)
        {
            var DirectoryPath = String.IsNullOrWhiteSpace(BackupDirectory)
                                ? Path.GetDirectoryName(InputPath)
                                : Path.GetFullPath(BackupDirectory);
            Directory.CreateDirectory(DirectoryPath);

            var BackupPath = Path.Combine(DirectoryPath,
                                          Path.GetFileName(InputPath) + "." +
                                          DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) +
                                          ".gitsync.bak");
            File.Copy(InputPath, BackupPath, true);
            return BackupPath;
        }

        private static void EnsureParentDirectory(string FilePath)
        {
            var Parent = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (!String.IsNullOrWhiteSpace(Parent) && !Directory.Exists(Parent))
                Directory.CreateDirectory(Parent);
        }

        private static bool SamePath(string First, string Second)
        {
            if (String.IsNullOrWhiteSpace(First) || String.IsNullOrWhiteSpace(Second))
                return false;

            return String.Equals(Path.GetFullPath(First).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 Path.GetFullPath(Second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string FilePath)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
            }
        }

        private static GitSyncState LoadState()
        {
            var State = new GitSyncState();
            if (!File.Exists(StatePath))
                return State;

            try
            {
                var Serializer = new JavaScriptSerializer();
                var Root = Serializer.DeserializeObject(File.ReadAllText(StatePath, Encoding.UTF8)) as IDictionary<string, object>;
                object EntriesObject;
                var Entries = Root != null && Root.TryGetValue("entries", out EntriesObject) ? EntriesObject as IEnumerable<object> : null;
                if (Entries == null)
                    return State;

                foreach (var EntryObject in Entries)
                {
                    var EntryData = EntryObject as IDictionary<string, object>;
                    if (EntryData == null)
                        continue;

                    var Entry = new GitSyncStateEntry();
                    Entry.Key = GetString(EntryData, "key");
                    Entry.RemoteCommit = GetString(EntryData, "remoteCommit");
                    Entry.PackageHash = GetString(EntryData, "packageHash");
                    Entry.LastSyncUtc = GetString(EntryData, "lastSyncUtc");
                    if (!String.IsNullOrWhiteSpace(Entry.Key))
                        State.Put(Entry);
                }
            }
            catch
            {
                return new GitSyncState();
            }

            return State;
        }

        private static void SaveState(GitSyncState State)
        {
            Directory.CreateDirectory(GitSyncRoot);
            var Entries = State.Entries.OrderBy(Entry => Entry.Key).Select(Entry =>
            {
                var Obj = new Dictionary<string, object>();
                Obj["key"] = Entry.Key;
                Obj["remoteCommit"] = Entry.RemoteCommit;
                Obj["packageHash"] = Entry.PackageHash;
                Obj["lastSyncUtc"] = Entry.LastSyncUtc;
                return Obj;
            }).ToList();

            var Root = new Dictionary<string, object>();
            Root["format"] = "ThinkComposer.GitSyncState";
            Root["formatVersion"] = 1;
            Root["entries"] = Entries;

            var Serializer = new JavaScriptSerializer();
            Serializer.MaxJsonLength = Int32.MaxValue;
            File.WriteAllText(StatePath, Serializer.Serialize(Root), Encoding.UTF8);
        }

        private static GitSyncStateEntry CreateStateEntry(GitPackageLink Link, GitPackageBaseline Baseline, string RemoteCommit, string PackagePath)
        {
            return new GitSyncStateEntry
            {
                Key = EntryKey(Link, Baseline),
                RemoteCommit = RemoteCommit,
                PackageHash = HashFile(PackagePath),
                LastSyncUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        private static string EntryKey(GitPackageLink Link, GitPackageBaseline Baseline)
        {
            return HashText(Link.Remote.Url + "\n" + Link.Remote.Branch + "\n" + Baseline.Kind + "\n" + Baseline.Role + "\n" + Baseline.Path);
        }

        private static string HashText(string Text)
        {
            return HashBytes(Encoding.UTF8.GetBytes(Text ?? ""));
        }

        private static string HashFile(string FilePath)
        {
            return HashBytes(File.ReadAllBytes(FilePath));
        }

        private static string HashBytes(byte[] Bytes)
        {
            using (var Hash = SHA256.Create())
            {
                var BytesHashed = Hash.ComputeHash(Bytes ?? new byte[0]);
                var Builder = new StringBuilder(BytesHashed.Length * 2);
                foreach (var Byte in BytesHashed)
                    Builder.Append(Byte.ToString("x2", CultureInfo.InvariantCulture));
                return Builder.ToString();
            }
        }

        private static string GetString(IDictionary<string, object> Source, string Key)
        {
            object Value;
            return Source != null && Source.TryGetValue(Key, out Value) && Value != null ? Value.ToString() : null;
        }

        private sealed class GitProcessResult
        {
            public string Output;
            public string Error;
        }

        private sealed class GitSyncState
        {
            private readonly Dictionary<string, GitSyncStateEntry> EntriesByKey = new Dictionary<string, GitSyncStateEntry>(StringComparer.OrdinalIgnoreCase);

            public IEnumerable<GitSyncStateEntry> Entries
            {
                get { return this.EntriesByKey.Values; }
            }

            public GitSyncStateEntry Get(string Key)
            {
                GitSyncStateEntry Entry;
                return !String.IsNullOrWhiteSpace(Key) && this.EntriesByKey.TryGetValue(Key, out Entry) ? Entry : null;
            }

            public void Put(GitSyncStateEntry Entry)
            {
                if (Entry != null && !String.IsNullOrWhiteSpace(Entry.Key))
                    this.EntriesByKey[Entry.Key] = Entry;
            }
        }

        private sealed class GitSyncStateEntry
        {
            public string Key;
            public string RemoteCommit;
            public string PackageHash;
            public string LastSyncUtc;
        }
    }

    public sealed class GitPackagePullResult
    {
        public string OutputPath;
        public string BackupPath;
        public string RemoteHead;
        public string Message;
    }

    public sealed class GitPackageRemoteStatus
    {
        public string PackagePath;
        public string PackageKind;
        public string RemoteDisplayUrl;
        public string Branch;
        public string BaselinePath;
        public string RemoteHead;
        public string PreviousSeenHead;
        public bool BaselineExists;
        public string LocalAuthoritativeJsonHash;
        public string RemoteAuthoritativeJsonHash;
        public bool HasRemoteUpdate;
    }
}
