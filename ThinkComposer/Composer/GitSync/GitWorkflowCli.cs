using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public static class GitWorkflowCli
    {
        public const string Help = @"Repository Git (commands act on the containing repository):
  thinkcomposer git status|branches|history|remote-list|stash-list|tag-list --repo <directory>
  thinkcomposer git init --repo <directory> --branch <name>
  thinkcomposer git clone --remote <url> --repo <directory>
  thinkcomposer git remote-add|remote-set --repo <directory> --name <remote> --url <url>
  thinkcomposer git branch-create|switch --repo <directory> --branch <name>
  thinkcomposer git stage|unstage|restore --repo <directory> --path <file> [--path <file>]
  thinkcomposer git commit --repo <directory> --message <text>
  thinkcomposer git fetch|pull|push --repo <directory> [--remote <name>] [--branch <name>]
  thinkcomposer git merge|revert --repo <directory> --revision <ref>
  thinkcomposer git merge-abort --repo <directory>
  thinkcomposer git diff --repo <directory> [--staged|--revision <ref>]
  thinkcomposer git diff --base <container> --incoming <container>
  thinkcomposer git stash-save --repo <directory> [--message <text>]
  thinkcomposer git stash-apply|stash-drop --repo <directory> --revision <stash-ref>
  thinkcomposer git tag --repo <directory> --name <tag> --message <text>
  thinkcomposer git tag-push --repo <directory> --remote <remote> --name <tag>
Managed tracking (optional; never enabled by repository membership):
  thinkcomposer git tracking enable|disable --input <container>
  thinkcomposer git tracking relocate --repo <directory> --id <tracking-id> --path <new-path>
  thinkcomposer git projection refresh --input <container> [--discard-edits]
  thinkcomposer git projection import --input <container> [--apply] [--baseline <container>] [--resolutions <json>]
  thinkcomposer git verify --repo <directory> [--staged|--revision <ref>] [--json]
  thinkcomposer git setup --repo <directory> [--executable <ThinkComposer.Cli.exe>]
  thinkcomposer git resolve --repo <directory> --path <conflicted-container>
  thinkcomposer git resolve --repo <directory> --session <directory> [--apply|--abort]
  thinkcomposer git recover --repo <directory> --mode complete|rollback
  thinkcomposer git migrate --input <container> --repo <directory> --path <destination> [--apply]
  thinkcomposer git unlink --input <container>
Use --input instead of --repo to locate a containing repository. Save, stage, commit and push are separate.
Legacy link, push --message, and pull --output workflows are retired. Use migrate; no background checkout is used.";

        public static string Execute(string[] args, CancellationToken cancellation = default(CancellationToken))
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "help") return Help;
            var action = args[0]; var offset = 1;
            if (action == "textconv" && args.Length == 2 && !args[1].StartsWith("--")) return GitModelIntegration.TextConversion(args[1]);
            if (action == "tracking" || action == "projection") { if (args.Length < 2) throw new GitWorkflowException("USAGE", Help); action += "-" + args[1]; offset++; }
            var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = offset; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) throw new GitWorkflowException("USAGE", "Unexpected argument: " + args[i]);
                var key = args[i].Substring(2); var value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : null;
                if (!options.ContainsKey(key)) options[key] = new List<string>(); options[key].Add(value);
            }
            Func<string, string> optional = key => options.ContainsKey(key) ? options[key].Last() : null;
            Func<string, string> required = key => { var v = optional(key); if (String.IsNullOrWhiteSpace(v)) throw new GitWorkflowException("USAGE", "Missing --" + key); return v; };
            var input = optional("input"); var location = optional("repo") ?? input ?? Environment.CurrentDirectory;
            if (action == "link" || (action == "push" && options.ContainsKey("message")) || (action == "pull" && (options.ContainsKey("output") || options.ContainsKey("in-place"))))
                throw new GitWorkflowException("LEGACY_WORKFLOW", "Package-to-remote synchronization is retired. Use git migrate, then repository-scoped commands.\n" + Help);
            string result;
            switch (action)
            {
                case "init": result = RepositoryGit.Initialize(required("repo"), required("branch")); break;
                case "regression-fixtures":
                    var fixtures = Headless.HeadlessThinkComposerOperations.CreateGitRegressionFixtures(required("input"), required("output-dir"));
                    if (!fixtures.WasSuccessful) throw new GitWorkflowException("FIXTURE_FAILED", fixtures.Message);
                    result = fixtures.Message; break;
                case "clone": result = RepositoryGit.Clone(required("remote"), required("repo"), cancellation); break;
                case "status": return Serialize(RepositoryGit.Status(location));
                case "tracking-enable": result = ModelTracking.Enable(required("input")); break;
                case "tracking-disable": result = ModelTracking.Disable(required("input")); break;
                case "tracking-relocate": result = ModelTracking.Relocate(location, required("id"), required("path")); break;
                case "projection-refresh": result = ModelTracking.Refresh(required("input"), options.ContainsKey("discard-edits")); break;
                case "projection-import": result = ModelTracking.Import(required("input"), options.ContainsKey("apply"), optional("baseline"), optional("resolutions")); break;
                case "verify":
                    var root = RepositoryGit.Discover(location);
                    var report = options.ContainsKey("staged") ? ModelTracking.VerifyIndex(root) : options.ContainsKey("revision") ? ModelTracking.VerifyRevision(root, required("revision")) : ModelTracking.Verify(root);
                    report.RequireValid(); return options.ContainsKey("json") ? Serialize(report) : report.ToString();
                case "setup": result = GitModelIntegration.Setup(location, optional("executable") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ThinkComposer.Cli.exe")); break;
                case "finalize": result = GitModelIntegration.FinalizeMerge(location, options.ContainsKey("pre-merge")); break;
                case "driver":
                case "projection-driver": result = GitModelIntegration.Driver(location, required("base"), required("current"), required("incoming"), required("path"), action == "projection-driver"); break;
                case "resolve":
                    var session = optional("session");
                    if (session == null) result = "Merge session: " + ModelMerge.StartSession(location, required("path"));
                    else result = options.ContainsKey("abort") ? ModelMerge.AbortSession(session) : options.ContainsKey("apply") ? ModelMerge.ApplySession(location, session) : ModelMerge.PreviewSession(session); break;
                case "recover": var mode = required("mode"); if (mode != "complete" && mode != "rollback") throw new GitWorkflowException("USAGE", "Recovery mode must be complete or rollback."); result = ModelTracking.Recover(location, mode == "rollback"); break;
                case "migrate": result = GitModelIntegration.Migrate(required("input"), location, required("path"), options.ContainsKey("apply")); break;
                case "unlink": result = GitModelIntegration.Unlink(required("input")); break;
                case "diff":
                    result = options.ContainsKey("base") ? ModelMerge.Compare(required("base"), required("incoming")) : RepositoryGit.Execute(location, action, options.ContainsKey("staged") ? "staged" : optional("revision"), cancellation: cancellation); break;
                default:
                    var value = optional("revision") ?? optional("branch") ?? optional("name") ?? optional("message") ?? optional("remote");
                    var extra = optional("url");
                    if (action == "branch-create") { value = required("branch"); extra = optional("revision"); }
                    if (action == "push") { value = optional("remote"); extra = optional("branch"); }
                    if (action == "tag") { value = required("name"); extra = required("message"); }
                    if (action == "tag-push") { value = required("remote"); extra = required("name"); }
                    result = RepositoryGit.Execute(location, action, value, extra, options.ContainsKey("path") ? options["path"] : null, cancellation); break;
            }
            return options.ContainsKey("json") ? Serialize(new { formatVersion = 1, success = true, message = result }) : result;
        }
        public static string Serialize(object value) { return ModelJson.Text(ModelJson.Parse(new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(value))); }
    }
}
