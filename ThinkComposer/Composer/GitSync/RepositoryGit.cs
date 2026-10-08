using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public sealed class GitWorkflowException : Exception
    {
        public string Code { get; private set; }
        public GitWorkflowException(string code, string message) : base(message) { Code = code; }
    }

    public sealed class RepositoryChange
    {
        public string Index { get; set; }
        public string WorkTree { get; set; }
        public string Path { get; set; }
        public string OriginalPath { get; set; }
        public bool Conflict { get; set; }
        public string DisplayLabel
        {
            get
            {
                var code = WorkTree != " " ? WorkTree : Index;
                var label = Conflict ? "Conflict" : code == "?" || code == "A" ? "New" : code == "D" ? "Deleted" : code == "R" ? "Renamed" : code == "C" ? "Copied" : "Modified";
                return label + "   " + Path;
            }
        }
        public override string ToString() { return Index + WorkTree + " " + Path; }
    }

    public sealed class RepositoryStatus
    {
        public string Root { get; set; }
        public string Branch { get; set; }
        public string Upstream { get; set; }
        public string Operation { get; set; }
        public int Ahead { get; set; }
        public int Behind { get; set; }
        public bool TrackingTransactionPending { get; set; }
        public List<RepositoryChange> Changes { get; set; }
    }

    public sealed class GitCommandResult
    {
        public int ExitCode;
        public byte[] Bytes;
        public string Error;
        public string Text { get { return Encoding.UTF8.GetString(Bytes); } }
    }

    // All paths refer to a user's visible working repository. No cache clones or resets.
    public static class RepositoryGit
    {
        public static GitCommandResult Run(string directory, IEnumerable<string> arguments,
                                           CancellationToken cancellation = default(CancellationToken), bool check = true)
        {
            var args = new List<string> { "-c", "core.quotepath=false" };
            if (!String.IsNullOrEmpty(directory)) { args.Add("-C"); args.Add(directory); }
            args.AddRange(arguments);
            var start = new ProcessStartInfo("git.exe", String.Join(" ", args.Select(Quote)))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            start.EnvironmentVariables["GIT_LITERAL_PATHSPECS"] = "1";
            using (var process = new Process { StartInfo = start })
            using (var bytes = new MemoryStream())
            {
                try { process.Start(); }
                catch (Exception e) { throw new GitWorkflowException("GIT_UNAVAILABLE", "Install Git for Windows and make git.exe available on PATH. " + e.Message); }
                var copy = process.StandardOutput.BaseStream.CopyToAsync(bytes);
                var errors = process.StandardError.ReadToEndAsync();
                while (!process.WaitForExit(100))
                    if (cancellation.IsCancellationRequested)
                    {
                        try
                        {
                            // .NET Framework lacks Process.Kill(entireProcessTree). Stop Git's
                            // remote/credential children too, so canceled work cannot continue.
                            using (var stop = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                            { var drain = stop.StandardOutput.ReadToEndAsync(); var diagnostics = stop.StandardError.ReadToEndAsync(); stop.WaitForExit(); drain.GetAwaiter().GetResult(); diagnostics.GetAwaiter().GetResult(); }
                        }
                        catch (Exception) { try { process.Kill(); } catch (InvalidOperationException) { } }
                        process.WaitForExit();
                        copy.GetAwaiter().GetResult(); errors.GetAwaiter().GetResult();
                        throw new OperationCanceledException(cancellation);
                    }
                copy.GetAwaiter().GetResult();
                var result = new GitCommandResult { ExitCode = process.ExitCode, Bytes = bytes.ToArray(), Error = errors.GetAwaiter().GetResult() };
                if (check && result.ExitCode != 0)
                    throw new GitWorkflowException("GIT_FAILED", Redact(result.Error.Trim() + "\n" + result.Text.Trim()));
                return result;
            }
        }

        // Windows argv quoting, never shell interpolation.
        internal static string Quote(string value)
        {
            if (value == null || value.IndexOf('\0') >= 0) throw new ArgumentException("Invalid Git argument.");
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') result.Append('\\', slashes * 2 + 1).Append(ch);
                else result.Append('\\', slashes).Append(ch);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        private static string Redact(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text, @"(https?://)[^\s/@]+@", "$1<redacted>@");
        }

        public static string Discover(string path, bool required = true)
        {
            if (String.IsNullOrWhiteSpace(path)) path = Environment.CurrentDirectory;
            path = System.IO.Path.GetFullPath(path);
            if (!Directory.Exists(path)) path = System.IO.Path.GetDirectoryName(path);
            if (!Directory.Exists(path)) { if (!required) return null; throw new GitWorkflowException("NOT_REPOSITORY", "The directory does not exist."); }
            GitCommandResult result;
            try { result = Run(path, new[] { "rev-parse", "--show-toplevel" }, check: false); }
            catch (GitWorkflowException) { if (!required) return null; throw; }
            if (result.ExitCode == 0) return System.IO.Path.GetFullPath(result.Text.Trim());
            if (!required) return null;
            throw new GitWorkflowException("NOT_REPOSITORY", "Choose or create a visible Git working repository.");
        }

        public static string LocalState(string root)
        {
            var path = Run(root, new[] { "rev-parse", "--git-path", "thinkcomposer" }).Text.Trim();
            return System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(root, path));
        }

        public static string Relative(string root, string path)
        {
            var full = System.IO.Path.GetFullPath(path);
            var prefix = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new GitWorkflowException("OUTSIDE_REPOSITORY", "Path is outside this repository: " + path);
            return full.Substring(prefix.Length).Replace('\\', '/');
        }

        public static string FilePath(string root, string relative)
        {
            GitPackageLink.ValidateRepositoryPath(relative);
            var result = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            Relative(root, result);
            // Refuse junction/symlink escapes, including in generated directories.
            for (var current = result; !String.Equals(current, root, StringComparison.OrdinalIgnoreCase); current = System.IO.Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new GitWorkflowException("REPARSE_PATH", "Managed paths may not traverse a junction or symbolic link: " + relative);
                if (String.IsNullOrEmpty(current)) throw new GitWorkflowException("OUTSIDE_REPOSITORY", relative);
            }
            return result;
        }

        public static RepositoryStatus Status(string path)
        {
            var root = Discover(path);
            var result = new RepositoryStatus { Root = root, Changes = new List<RepositoryChange>(), Operation = Operation(root) };
            result.TrackingTransactionPending = Directory.Exists(System.IO.Path.Combine(LocalState(root), "transaction"));
            var branch = Run(root, new[] { "symbolic-ref", "--quiet", "--short", "HEAD" }, check: false);
            result.Branch = branch.ExitCode == 0 ? branch.Text.Trim() : "(detached HEAD)";
            var upstream = Run(root, new[] { "rev-parse", "--abbrev-ref", "@{upstream}" }, check: false);
            result.Upstream = upstream.ExitCode == 0 ? upstream.Text.Trim() : null;
            if (result.Upstream != null)
            {
                var counts = Run(root, new[] { "rev-list", "--left-right", "--count", "HEAD...@{upstream}" }).Text.Trim().Split('\t');
                result.Ahead = Int32.Parse(counts[0]); result.Behind = Int32.Parse(counts[1]);
            }
            var fields = Run(root, new[] { "status", "--porcelain=v1", "-z", "--untracked-files=all" }).Text.Split('\0');
            for (int i = 0; i < fields.Length; i++)
            {
                var item = fields[i]; if (item.Length < 4) continue;
                var change = new RepositoryChange { Index = item.Substring(0, 1), WorkTree = item.Substring(1, 1), Path = item.Substring(3) };
                change.Conflict = new[] { "DD", "AU", "UD", "UA", "DU", "AA", "UU" }.Contains(item.Substring(0, 2));
                if (item[0] == 'R' || item[0] == 'C' || item[1] == 'R' || item[1] == 'C') change.OriginalPath = fields[++i];
                result.Changes.Add(change);
            }
            return result;
        }

        public static string Operation(string root)
        {
            foreach (var name in new[] { "rebase-merge", "rebase-apply", "CHERRY_PICK_HEAD", "REVERT_HEAD", "MERGE_HEAD", "sequencer" })
            {
                var p = Run(root, new[] { "rev-parse", "--git-path", name }).Text.Trim();
                if (!System.IO.Path.IsPathRooted(p)) p = System.IO.Path.Combine(root, p);
                if (File.Exists(p) || Directory.Exists(p)) return name;
            }
            return null;
        }

        public static void RequireIdle(string root, bool clean = false)
        {
            var status = Status(root);
            if (status.TrackingTransactionPending) throw new GitWorkflowException("RECOVERY_REQUIRED", "Complete or roll back the interrupted managed model transaction first.");
            if (status.Operation != null || status.Changes.Any(c => c.Conflict)) throw new GitWorkflowException("OPERATION_IN_PROGRESS", "Finish or abort the current Git operation before continuing.");
            if (clean && status.Changes.Any(c => c.Index != "?")) throw new GitWorkflowException("DIRTY_REPOSITORY", "Commit or explicitly stash tracked changes first.");
        }

        public static string Execute(string path, string action, string value = null, string extra = null,
                                     IEnumerable<string> paths = null, CancellationToken cancellation = default(CancellationToken))
        {
            var root = Discover(path); var args = new List<string>();
            var status = Status(root);
            bool finishing = action == "commit" || action == "stage" || action == "unstage" || action == "diff" || action == "history" || action == "merge-abort";
            if (status.Operation != null && !(finishing && (status.Operation == "MERGE_HEAD" || status.Operation == "REVERT_HEAD")))
                throw new GitWorkflowException("OPERATION_IN_PROGRESS", "External Git operation in progress: " + status.Operation);
            switch (action)
            {
                case "branch-create": RequireIdle(root); Ref(root, value); args.AddRange(new[] { "branch", value, extra ?? "HEAD" }); break;
                case "switch": RequireIdle(root, true); Ref(root, value); args.AddRange(new[] { "switch", value }); break;
                case "fetch": args.AddRange(new[] { "fetch", "--", value ?? Remote(root) }); break;
                case "pull":
                    RequireIdle(root, true);
                    Run(root, new[] { "fetch", "--", Remote(root) }, cancellation);
                    status = Status(root);
                    if (status.Upstream == null) throw new GitWorkflowException("NO_UPSTREAM", "Set an upstream by pushing this branch with a remote and branch name.");
                    if (status.Ahead > 0 && status.Behind > 0) throw new GitWorkflowException("DIVERGED", "Branches diverged. Review HEAD...@{upstream}, then explicitly merge @{upstream}.");
                    args.AddRange(new[] { "merge", "--ff-only", "@{upstream}" }); break;
                case "merge": RequireIdle(root, true); Value(value); args.AddRange(new[] { "merge", "--no-commit", "--no-ff", "--", value }); break;
                case "merge-abort":
                    if (status.Operation != "MERGE_HEAD") throw new GitWorkflowException("NO_MERGE", "No merge is in progress.");
                    args.AddRange(new[] { "merge", "--abort" }); break;
                case "commit":
                    Value(value);
                    if (status.Changes.Any(c => c.Conflict)) throw new GitWorkflowException("UNRESOLVED", "Resolve conflicts before committing.");
                    GitModelIntegration.FinalizeMerge(root);
                    args.AddRange(new[] { "commit", "-m", value }); break;
                case "push":
                    ModelTracking.VerifyRevision(root, "HEAD").RequireValid();
                    if (status.Changes.Any(c => c.Conflict)) throw new GitWorkflowException("UNRESOLVED", "Resolve conflicts first.");
                    args.Add("push"); if (value != null) { Value(value); Value(extra); args.AddRange(new[] { "-u", "--", value, extra }); } break;
                case "stage":
                case "unstage":
                    var selected = ModelTracking.ExpandPaths(root, paths ?? new string[0]).ToArray();
                    if (selected.Length == 0) throw new GitWorkflowException("NO_SELECTION", "Select files first.");
                    if (action == "stage") args.AddRange(new[] { "add", "-A", "--" });
                    else if (Run(root, new[] { "rev-parse", "--verify", "HEAD" }, check: false).ExitCode == 0) args.AddRange(new[] { "restore", "--staged", "--" });
                    else args.AddRange(new[] { "rm", "--cached", "--ignore-unmatch", "--" });
                    args.AddRange(selected); break;
                case "history": args.AddRange(new[] { "log", "-50", "--date=iso-strict", "--format=%h %ad %s" }); break;
                case "diff": args.AddRange(new[] { "diff", "--no-ext-diff" }); if (value == "staged") args.Add("--cached"); else if (value != null) { Value(value); args.Add(value); } break;
                case "stash-save": RequireIdle(root); args.AddRange(new[] { "stash", "push", "-m", value ?? "ThinkComposer working changes" }); break;
                case "stash-list": args.AddRange(new[] { "stash", "list" }); break;
                case "stash-apply": RequireIdle(root, true); Value(value); args.AddRange(new[] { "stash", "apply", value }); break;
                case "stash-drop": Value(value); args.AddRange(new[] { "stash", "drop", value }); break;
                case "restore":
                    RequireIdle(root); args.AddRange(new[] { "restore", "--worktree", "--" });
                    var restore = ModelTracking.ExpandPaths(root, paths ?? new string[0]).ToArray();
                    if (restore.Length == 0) throw new GitWorkflowException("NO_SELECTION", "Select files first."); args.AddRange(restore); break;
                case "revert": RequireIdle(root, true); Value(value); args.AddRange(new[] { "revert", "--no-commit", value }); break;
                case "tag": Value(value); Value(extra); args.AddRange(new[] { "tag", "-a", value, "-m", extra }); break;
                case "tag-list": args.AddRange(new[] { "tag", "--list" }); break;
                case "tag-push": Value(value); Value(extra); ModelTracking.VerifyRevision(root, "refs/tags/" + extra + "^{commit}").RequireValid(); args.AddRange(new[] { "push", "--", value, "refs/tags/" + extra }); break;
                case "remote-list": args.AddRange(new[] { "remote", "-v" }); break;
                case "remote-add":
                case "remote-set":
                    Value(value); ValidateRemote(extra); args.AddRange(new[] { "remote", action == "remote-add" ? "add" : "set-url", value, extra }); break;
                case "branches": args.AddRange(new[] { "branch", "-avv" }); break;
                default: throw new GitWorkflowException("UNKNOWN_COMMAND", "Unknown repository action: " + action);
            }
            var executed = Run(root, args, cancellation);
            var output = Redact(executed.Text + executed.Error);
            if (new[] { "switch", "pull", "merge", "revert", "stash-apply" }.Contains(action))
            {
                var verification = ModelTracking.Verify(root);
                if (!verification.Valid) output += "\nManaged model discrepancies (result was not rewritten):\n" + verification;
            }
            return String.IsNullOrWhiteSpace(output) ? action + " completed." : output;
        }

        private static string Remote(string root)
        {
            var branch = Run(root, new[] { "symbolic-ref", "--short", "HEAD" }).Text.Trim();
            var r = Run(root, new[] { "config", "--get", "branch." + branch + ".remote" }, check: false);
            return r.ExitCode == 0 ? r.Text.Trim() : "origin";
        }
        internal static void Value(string value) { if (String.IsNullOrWhiteSpace(value) || value.StartsWith("-") || value.Contains("\0")) throw new GitWorkflowException("INVALID_ARGUMENT", "A non-option argument is required."); }
        private static void Ref(string root, string value) { Value(value); Run(root, new[] { "check-ref-format", "--branch", value }); }
        public static void ValidateRemote(string value) { Value(value); if (GitPackageLink.ContainsCredentialUserInfo(value)) throw new GitWorkflowException("EMBEDDED_CREDENTIALS", "Use Git Credential Manager or SSH, not credentials in a remote URL."); }
        public static string Initialize(string directory, string branch)
        {
            Directory.CreateDirectory(directory); Value(branch);
            return Run(null, new[] { "init", "--initial-branch=" + branch, "--", System.IO.Path.GetFullPath(directory) }).Text;
        }
        public static string Clone(string remote, string directory, CancellationToken cancellation = default(CancellationToken))
        {
            ValidateRemote(remote);
            return Run(null, new[] { "clone", "--", remote, System.IO.Path.GetFullPath(directory) }, cancellation).Text;
        }
    }
}
