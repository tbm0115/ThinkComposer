using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public static class GitModelIntegration
    {
        public static string Setup(string root, string executable)
        {
            root = RepositoryGit.Discover(root); executable = Path.GetFullPath(executable);
            if (!File.Exists(executable)) throw new GitWorkflowException("CLI_MISSING", "Cannot install drivers without a valid ThinkComposer.Cli.exe.");
            var configured = RepositoryGit.Run(root, new[] { "config", "--get", "core.hooksPath" }, check: false);
            var hooks = configured.ExitCode == 0 ? configured.Text.Trim() : RepositoryGit.Run(root, new[] { "rev-parse", "--git-path", "hooks" }).Text.Trim();
            if (!Path.IsPathRooted(hooks)) hooks = Path.GetFullPath(Path.Combine(root, hooks));
            var names = new[] { "pre-commit", "pre-merge-commit", "post-checkout", "post-merge" };
            foreach (var name in names)
            {
                var existing = Path.Combine(hooks, name);
                if (File.Exists(existing) && !File.ReadAllText(existing).Contains("# ThinkComposer managed integration v1"))
                    throw new GitWorkflowException("EXISTING_HOOK", "Existing hook retained: " + existing + ". Integrate `thinkcomposer git finalize` (pre-commit/pre-merge-commit) and `git verify` (post-checkout/post-merge) manually.");
            }
            var attributes = Path.Combine(root, ".gitattributes"); var text = File.Exists(attributes) ? File.ReadAllText(attributes) : "";
            const string begin = "# BEGIN ThinkComposer managed models"; const string end = "# END ThinkComposer managed models";
            var start = text.IndexOf(begin, StringComparison.Ordinal);
            if (start >= 0)
            {
                var stop = text.IndexOf(end, start, StringComparison.Ordinal); if (stop < 0) throw new GitWorkflowException("ATTRIBUTE_BLOCK", "Incomplete ThinkComposer attributes block.");
                text = text.Remove(start, stop + end.Length - start);
            }
            var lines = new List<string> { begin };
            foreach (var registration in ModelTracking.Registrations(root))
            {
                // Refuse to silently override an existing effective per-path driver.
                var current = RepositoryGit.Run(root, new[] { "check-attr", "merge", "--", registration.Path }).Text.Trim();
                if (!current.EndsWith(": unspecified") && !current.EndsWith(": thinkcomposer-model") && !current.EndsWith(": binary"))
                    throw new GitWorkflowException("ATTRIBUTE_CONFLICT", "Existing merge attribute retained: " + current);
                lines.Add(AttributePath(registration.Path) + " -text diff=thinkcomposer-model merge=thinkcomposer-model");
                lines.Add(AttributePath(registration.Folder, true) + " -text merge=thinkcomposer-projection");
            }
            lines.Add(end);
            var command = ShellQuote(executable.Replace('\\', '/')) + " git ";
            RepositoryGit.Run(root, new[] { "config", "--local", "merge.thinkcomposer-model.name", "ThinkComposer semantic container merge" });
            RepositoryGit.Run(root, new[] { "config", "--local", "merge.thinkcomposer-model.driver", command + "driver --base \"%O\" --current \"%A\" --incoming \"%B\" --path %P" });
            RepositoryGit.Run(root, new[] { "config", "--local", "merge.thinkcomposer-model.recursive", "binary" });
            RepositoryGit.Run(root, new[] { "config", "--local", "merge.thinkcomposer-projection.driver", command + "projection-driver --base \"%O\" --current \"%A\" --incoming \"%B\" --path %P" });
            RepositoryGit.Run(root, new[] { "config", "--local", "merge.thinkcomposer-projection.recursive", "binary" });
            RepositoryGit.Run(root, new[] { "config", "--local", "diff.thinkcomposer-model.textconv", command + "textconv" });
            RepositoryGit.Run(root, new[] { "config", "--local", "diff.thinkcomposer-model.binary", "true" });
            Directory.CreateDirectory(hooks);
            foreach (var name in names)
            {
                var script = "#!/bin/sh\n# ThinkComposer managed integration v1\nexec " + command + (name.StartsWith("pre-") ? "finalize" : "verify") + " --repo ." + (name == "pre-merge-commit" ? " --pre-merge" : "") + "\n";
                File.WriteAllText(Path.Combine(hooks, name), script, ModelJson.Utf8);
            }
            File.WriteAllText(attributes, text.TrimEnd() + "\n\n" + String.Join("\n", lines) + "\n", ModelJson.Utf8);
            return "Local drivers/hooks installed. Review .gitattributes. Repeat setup in each clone after enabling or moving tracked artifacts.";
        }
        private static string AttributePath(string path, bool descendants = false)
        {
            var literal = String.Concat(path.Select(c => "*?[]".Contains(c) ? "\\" + c : c.ToString()));
            return "\"" + (literal + (descendants ? "/**" : "")).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
        private static string ShellQuote(string value) { return "'" + value.Replace("'", "'\"'\"'") + "'"; }

        public static string Driver(string root, string ancestor, string current, string incoming, string path, bool projection)
        {
            root = RepositoryGit.Discover(root); RepositoryGit.FilePath(root, path);
            var session = Path.Combine(RepositoryGit.LocalState(root), "drivers", Guid.NewGuid().ToString("D")); Directory.CreateDirectory(session);
            foreach (var item in new[] { Tuple.Create(ancestor, "base"), Tuple.Create(current, "current"), Tuple.Create(incoming, "incoming") }) File.Copy(item.Item1, Path.Combine(session, item.Item2));
            var metadata = ModelJson.Map("formatVersion", 1, "path", path, "projection", projection, "completed", false);
            ModelJson.Write(Path.Combine(session, "journal.json"), metadata);
            if (projection) return "Projection input versions retained for pre-commit reconciliation.";
            var merged = ModelMerge.MergePackages(ModelPackage.Read(ancestor), ModelPackage.Read(current), ModelPackage.Read(incoming), null);
            ModelJson.Write(Path.Combine(session, "conflicts.json"), merged.ToGraph());
            if (merged.Conflicts.Count > 0) throw new GitWorkflowException("MERGE_CONFLICT", "Semantic conflicts retained at " + session + ". Open the repository resolver.");
            merged.Package.Write(current);
            metadata["resultSha256"] = ModelJson.FileHash(current); ModelJson.Write(Path.Combine(session, "journal.json"), metadata);
            return "Container merged; projections must pass finalization before commit.";
        }

        public static string FinalizeMerge(string root, bool preMerge = false)
        {
            root = RepositoryGit.Discover(root);
            if (Directory.Exists(Path.Combine(RepositoryGit.LocalState(root), "transaction"))) throw new GitWorkflowException("RECOVERY_REQUIRED", "Recover the interrupted model transaction before committing.");
            var beforeTree = preMerge ? RepositoryGit.Run(root, new[] { "write-tree" }).Text.Trim() : null;
            var drivers = Path.Combine(RepositoryGit.LocalState(root), "drivers");
            if (!Directory.Exists(drivers)) { ModelTracking.VerifyIndex(root).RequireValid(); return "Staged model projections verified."; }
            if (!preMerge && RepositoryGit.Operation(root) != "MERGE_HEAD")
            {
                ModelTracking.VerifyIndex(root).RequireValid();
                return "Staged models verified; automatic projection staging requires an active merge session.";
            }
            foreach (var dir in Directory.EnumerateDirectories(drivers).ToArray())
            {
                var journalPath = Path.Combine(dir, "journal.json"); if (!File.Exists(journalPath)) continue;
                var journal = ModelJson.Read(journalPath);
                if (Object.Equals(ModelJson.Get(journal, "completed"), true) || Object.Equals(ModelJson.Get(journal, "projection"), true)) continue;
                var path = ModelJson.Str(journal, "path"); var resultHash = ModelJson.Str(journal, "resultSha256"); if (resultHash == null) continue;
                var staged = RepositoryGit.Run(root, new[] { "show", ":0:" + path }, check: false);
                if (staged.ExitCode != 0 || ModelJson.Hash(staged.Bytes) != resultHash) continue;
                // Verify both source revisions independently. Never bless committed projection edits.
                VerifySourceRevision(root, path, Path.Combine(dir, "current"));
                VerifySourceRevision(root, path, Path.Combine(dir, "incoming"));
                var target = RepositoryGit.FilePath(root, path);
                if (!File.Exists(target) || ModelJson.FileHash(target) != resultHash) throw new GitWorkflowException("UNSTAGED_EDITS", "Working container differs from staged merge result: " + path);
                var registration = ModelTracking.Find(root, target); if (registration == null) throw new GitWorkflowException("REGISTRATION_CONFLICT", "Resolve tracking registration first: " + path);
                var changes = RepositoryGit.Status(root).Changes.Where(c => c.Path == path || c.Path.StartsWith(registration.Folder + "/", StringComparison.Ordinal));
                if (changes.Any(c => c.WorkTree != " " || c.Conflict)) throw new GitWorkflowException("UNSTAGED_EDITS", "Preserve/import unstaged projection edits before merge finalization.");
                ModelTracking.Refresh(target, true);
                RepositoryGit.Run(root, new[] { "add", "--", path, registration.Folder });
                ModelJson.Obj(journal)["completed"] = true; ModelJson.Write(journalPath, journal);
            }
            ModelTracking.VerifyIndex(root).RequireValid();
            if (preMerge && RepositoryGit.Run(root, new[] { "write-tree" }).Text.Trim() != beforeTree)
                throw new GitWorkflowException("MERGE_READY_TO_COMMIT", "Model merge and projection regeneration succeeded. Git prepared its merge tree before this hook, so automatic commit was stopped. Review the staged result and run git commit to complete the merge safely.");
            return "Merge projections finalized and staged index verified.";
        }
        internal static void VerifySourceRevision(string root, string path, string package)
        {
            var oid = RepositoryGit.Run(root, new[] { "hash-object", "--", package }).Text.Trim();
            var refs = RepositoryGit.Run(root, new[] { "for-each-ref", "--format=%(objectname)" }).Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Concat(new[] { "HEAD" });
            var history = RepositoryGit.Run(root, new[] { "rev-list", "--max-count=200", "--all", "--", path }).Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var revision in refs.Concat(history).Distinct())
            {
                var blob = RepositoryGit.Run(root, new[] { "rev-parse", revision + ":" + path }, check: false);
                if (blob.ExitCode != 0 || blob.Text.Trim() != oid) continue;
                ModelTracking.VerifyRevision(root, revision).RequireValid(); return;
            }
            throw new GitWorkflowException("PROVENANCE_REQUIRED", "Cannot establish the source projection revision. Resolve explicitly in ThinkComposer; all driver inputs were retained.");
        }

        public static string TextConversion(string input)
        {
            var package = ModelPackage.Read(input);
            return String.Join("\n", package.Content().Select(p => "=== " + p.Key + " ===\n" + (ModelPackage.JsonParts.Contains(p.Key) ? ModelJson.Utf8.GetString(p.Value) : "sha256 " + ModelJson.Hash(p.Value))));
        }

        public static string Unlink(string input)
        {
            var package = ModelPackage.Read(input); var expected = ModelJson.FileHash(input);
            package.Manifest.Remove("gitSync"); package.Manifest.Remove("embeddedDomainGitSync");
            var candidate = Path.Combine(Path.GetTempPath(), "thinkcomposer-unlink-" + Guid.NewGuid() + ".tcom");
            try
            {
                package.Write(candidate);
                var root = RepositoryGit.Discover(input, false); var registration = root == null ? null : ModelTracking.Find(root, input);
                if (registration != null)
                {
                    if (ModelTracking.ProjectionEdited(root, registration)) throw new GitWorkflowException("PROJECTION_EDITED", "Reconcile projection edits before unlinking legacy metadata.");
                    ModelTracking.PublishCandidate(root, registration, candidate, expected);
                }
                else { if (ModelJson.FileHash(input) != expected) throw new GitWorkflowException("EXTERNAL_CHANGE", input); File.Copy(candidate, input, true); }
                DocumentDiskState.Accept(input); return "Legacy package and embedded-Domain links removed; repository membership is unchanged.";
            }
            finally { if (File.Exists(candidate)) File.Delete(candidate); }
        }

        public static string Migrate(string input, string root, string relative, bool apply)
        {
            root = RepositoryGit.Discover(root); var target = RepositoryGit.FilePath(root, relative);
            if (ModelTracking.Find(root, target) != null) throw new GitWorkflowException("TRACKED_DESTINATION", "Disable managed tracking explicitly before migrating onto this registered container, or choose a new path.");
            ModelPackage.Read(input).Validate();
            if (File.Exists(target) && !String.Equals(Path.GetFullPath(input), target, StringComparison.OrdinalIgnoreCase) && ModelJson.FileHash(input) != ModelJson.FileHash(target))
                throw new GitWorkflowException("MIGRATION_CONFLICT", "The destination already contains a different package. Compare both files and choose a new path; neither was changed.");
            if (!apply) return "Migration preview: " + input + " -> " + target + ". Legacy link metadata will be removed. Managed tracking remains disabled.";
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            var package = ModelPackage.Read(input); package.Manifest.Remove("gitSync"); package.Manifest.Remove("embeddedDomainGitSync"); package.Stamp(null);
            var staging = Path.Combine(Path.GetDirectoryName(target), ".tc-migrate-" + Guid.NewGuid());
            try { package.Write(staging); if (File.Exists(target)) File.Copy(target, target + ".migration-backup-" + Guid.NewGuid(), false); File.Copy(staging, target, true); }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            DocumentDiskState.Accept(target);
            return "Migrated into visible repository: " + target + ". Managed tracking can be enabled separately; legacy caches were left intact.";
        }
    }
}
