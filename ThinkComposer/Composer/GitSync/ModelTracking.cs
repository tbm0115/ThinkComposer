using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public sealed class TrackingRegistration
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string Kind { get; set; }
        public bool Active { get; set; }
        internal string Folder { get { return (Kind == "composition" ? ".tcom/" : ".tdom/") + Id; } }
    }
    public sealed class ProjectionReport
    {
        public int FormatVersion = 1;
        public List<string> Diagnostics = new List<string>();
        public bool Valid { get { return Diagnostics.Count == 0; } }
        public void RequireValid() { if (!Valid) throw new GitWorkflowException("PROJECTION_INVALID", String.Join("\n", Diagnostics)); }
        public override string ToString() { return Valid ? "All registered container/projection pairs are valid." : String.Join("\n", Diagnostics); }
    }

    public static class DocumentDiskState
    {
        private static readonly Dictionary<string, string> Observed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static void Observe(string path) { lock (Observed) { path = System.IO.Path.GetFullPath(path); if (!Observed.ContainsKey(path) && File.Exists(path)) Observed[path] = ModelJson.FileHash(path); } }
        public static void Forget(string path) { if (path != null) lock (Observed) Observed.Remove(System.IO.Path.GetFullPath(path)); }
        public static bool Changed(string path) { lock (Observed) { string hash; return Observed.TryGetValue(System.IO.Path.GetFullPath(path), out hash) && (!File.Exists(path) || ModelJson.FileHash(path) != hash); } }
        public static void Accept(string path) { lock (Observed) { path = System.IO.Path.GetFullPath(path); if (File.Exists(path)) Observed[path] = ModelJson.FileHash(path); else Observed.Remove(path); } }
        internal static void RequireUnchanged(string path) { if (Changed(path)) throw new GitWorkflowException("EXTERNAL_CHANGE", "The container changed outside ThinkComposer. Reload or reconcile before saving: " + path); }
    }

    public static class ModelTracking
    {
        internal static readonly string[] Roots = { ".tcom", ".tdom" };
        public static List<TrackingRegistration> Registrations(string root, bool includeInactive = false)
        {
            var result = new List<TrackingRegistration>();
            foreach (var dir in Roots)
            {
                var index = RepositoryGit.FilePath(root, dir + "/index.json"); if (!File.Exists(index)) continue;
                var graph = ModelJson.Read(index);
                if (ModelJson.Str(graph, "formatVersion") != "1") throw new GitWorkflowException("UNSUPPORTED_REGISTRY", index);
                foreach (var item in ModelJson.Array(ModelJson.Get(graph, "artifacts")))
                {
                    var id = ModelJson.Str(item, "id"); Guid parsed;
                    if (!Guid.TryParse(id, out parsed)) throw new GitWorkflowException("INVALID_TRACKING_ID", index);
                    var registration = new TrackingRegistration { Id = id, Path = ModelJson.Str(item, "path"), Kind = dir == ".tcom" ? "composition" : "domain", Active = !Object.Equals(ModelJson.Get(item, "active"), false) };
                    RepositoryGit.FilePath(root, registration.Path);
                    if (registration.Active || includeInactive) result.Add(registration);
                }
            }
            if (result.GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) || result.GroupBy(r => r.Id).Any(g => g.Count() > 1))
                throw new GitWorkflowException("DUPLICATE_REGISTRATION", "Duplicate tracking identity or container path.");
            return result;
        }

        internal static void WriteRegistry(string root, string kind, IEnumerable<TrackingRegistration> registrations)
        {
            var folder = kind == "composition" ? ".tcom" : ".tdom";
            ModelJson.Write(RepositoryGit.FilePath(root, folder + "/index.json"), ModelJson.Map("formatVersion", 1, "artifacts", registrations.Where(r => r.Kind == kind).OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => ModelJson.Map("id", r.Id, "path", r.Path, "active", r.Active)).ToArray()));
        }

        public static TrackingRegistration Find(string root, string container)
        {
            var relative = RepositoryGit.Relative(root, container);
            return Registrations(root).FirstOrDefault(r => String.Equals(r.Path, relative, StringComparison.OrdinalIgnoreCase));
        }

        public static string Enable(string input)
        {
            var root = RepositoryGit.Discover(input); input = Path.GetFullPath(input);
            RequireNoTransaction(root);
            var registrations = Registrations(root, true);
            var existing = registrations.FirstOrDefault(r => String.Equals(r.Path, RepositoryGit.Relative(root, input), StringComparison.OrdinalIgnoreCase));
            if (existing != null && existing.Active) throw new GitWorkflowException("ALREADY_ENABLED", "Managed tracking is already enabled.");
            var package = ModelPackage.Read(input); package.Validate();
            var item = existing ?? new TrackingRegistration { Id = Guid.NewGuid().ToString("D"), Path = RepositoryGit.Relative(root, input), Kind = package.Kind };
            item.Active = true; if (existing == null) registrations.Add(item);
            RefreshInternal(root, item, package, true, registrations);
            var readme = RepositoryGit.FilePath(root, (package.Kind == "composition" ? ".tcom" : ".tdom") + "/README.md");
            if (!File.Exists(readme)) File.WriteAllText(readme, Guidance, ModelJson.Utf8);
            return "Managed tracking enabled for " + item.Path + ". Run git setup to install local drivers/hooks; review and stage the generated files.";
        }

        public static string Disable(string input)
        {
            var root = RepositoryGit.Discover(input); RequireNoTransaction(root);
            var all = Registrations(root, true); var item = all.FirstOrDefault(r => r.Active && String.Equals(r.Path, RepositoryGit.Relative(root, input), StringComparison.OrdinalIgnoreCase));
            if (item == null) throw new GitWorkflowException("NOT_ENABLED", "Managed tracking is not enabled.");
            var package = ModelPackage.Read(input);
            item.Active = false; RefreshInternal(root, item, package, false, all);
            return "Tracking disabled. Inactive projection files are retained for explicit review/removal.";
        }

        public static string Relocate(string root, string id, string newPath)
        {
            root = RepositoryGit.Discover(root); var all = Registrations(root, true); var item = all.Single(r => r.Id == id);
            var file = RepositoryGit.FilePath(root, newPath);
            if (ModelPackage.Read(file).TrackingId != id) throw new GitWorkflowException("IDENTITY_MISMATCH", "The destination is not the registered container.");
            if (File.Exists(RepositoryGit.FilePath(root, item.Path)) && !String.Equals(item.Path, newPath, StringComparison.OrdinalIgnoreCase)) throw new GitWorkflowException("COPY_NOT_RENAME", "The original still exists. Enable the copy independently instead.");
            item.Path = newPath; RefreshInternal(root, item, ModelPackage.Read(file), false, all);
            return "Registration moved to " + newPath;
        }

        public static string Refresh(string input, bool discardEdits = false)
        {
            var root = RepositoryGit.Discover(input); RequireNoTransaction(root);
            var item = Find(root, input); if (item == null) throw new GitWorkflowException("NOT_ENABLED", "Enable managed tracking explicitly first.");
            RefreshInternal(root, item, ModelPackage.Read(input), discardEdits);
            return "Projection refreshed for " + item.Path;
        }
        internal static void PublishCandidate(string root, TrackingRegistration item, string candidate, string expectedHash)
        {
            var input = RepositoryGit.FilePath(root, item.Path);
            if ((File.Exists(input) ? ModelJson.FileHash(input) : null) != expectedHash) throw new GitWorkflowException("EXTERNAL_CHANGE", "Container changed before resolution publication.");
            RefreshInternal(root, item, ModelPackage.Read(candidate), true, expectedContainerHash: expectedHash);
        }

        internal static bool ProjectionEdited(string root, TrackingRegistration item)
        {
            var directory = RepositoryGit.FilePath(root, item.Folder);
            if (!Directory.Exists(directory)) return false;
            var manifest = Path.Combine(directory, "projection.json");
            if (!File.Exists(manifest)) return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();
            var graph = ModelJson.Read(manifest);
            var baseline = FindBaseline(root, item, ModelJson.Str(graph, "sourceContainerSha256"));
            if (baseline == null) return true; // Unproven tooling metadata must never authorize overwriting edits.
            var recordedContent = ModelPackage.Read(baseline).Content();
            var catalog = ModelJson.Array(ModelJson.Get(graph, "parts")).ToArray();
            if (catalog.Length != recordedContent.Count || catalog.Any(p => !recordedContent.ContainsKey(ModelJson.Str(p, "path") ?? "") || ModelJson.Str(p, "sha256") != ModelJson.Hash(recordedContent[ModelJson.Str(p, "path")]))) return true;
            var expected = new HashSet<string>(StringComparer.Ordinal) { "projection.json" };
            foreach (var part in ModelJson.Array(ModelJson.Get(graph, "parts")))
            {
                var name = ModelJson.Str(part, "path"); ModelPackage.SafeName(name); expected.Add(name);
                var file = RepositoryGit.FilePath(root, item.Folder + "/" + name);
                if (!File.Exists(file) || ModelJson.FileHash(file) != ModelJson.Str(part, "sha256")) return true;
            }
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any(p => !expected.Contains(RepositoryGit.Relative(directory, p)));
        }

        private static void RefreshInternal(string root, TrackingRegistration item, ModelPackage package, bool discardEdits, List<TrackingRegistration> registryUpdate = null, string expectedContainerHash = null)
        {
            var startingProjection = DirectoryHash(RepositoryGit.FilePath(root, item.Folder));
            if (item.Active && !discardEdits && ProjectionEdited(root, item)) throw new GitWorkflowException("PROJECTION_EDITED", "Projection edits exist. Preview/import them or explicitly refresh with --discard-edits.");
            package.Validate(); package.Stamp(item.Active ? item.Id : null);
            var state = RepositoryGit.LocalState(root); Directory.CreateDirectory(state);
            using (var lease = new FileStream(Path.Combine(state, "projection.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
            var transaction = Path.Combine(state, "transaction");
            if (Directory.Exists(transaction)) throw new GitWorkflowException("RECOVERY_REQUIRED", "Run git recover before changing managed files.");
            Directory.CreateDirectory(transaction);
            var candidate = Path.Combine(transaction, "candidate.tcom"); package.Write(candidate);
            var input = RepositoryGit.FilePath(root, item.Path);
            var originalHash = File.Exists(input) ? ModelJson.FileHash(input) : null;
            var expected = expectedContainerHash ?? (String.Equals(package.SourcePath, input, StringComparison.OrdinalIgnoreCase) ? package.SourceHash : null);
            if (expected != null && expected != originalHash) throw new GitWorkflowException("EXTERNAL_CHANGE", "Container changed after the candidate snapshot was read; publication stopped.");
            // Do not change the container just to refresh an already-correct projection.
            if (File.Exists(input))
            {
                var old = ModelPackage.Read(input);
                if (item.Active && old.TrackingId == item.Id && old.Fingerprint() == package.Fingerprint() && ModelJson.Equal(old.AuthoredMetadata(true), package.AuthoredMetadata(true)) && ModelJson.Str(ModelJson.Get(old.Manifest, "contentFingerprint"), "value") == package.Fingerprint()) File.Copy(input, candidate, true);
            }
            var projection = Path.Combine(transaction, "projection"); Directory.CreateDirectory(projection);
            if (item.Active) WriteProjection(candidate, projection, item);
            else if (Directory.Exists(RepositoryGit.FilePath(root, item.Folder))) CopyDirectory(RepositoryGit.FilePath(root, item.Folder), projection);
            var backup = Path.Combine(transaction, "before.tcom"); if (File.Exists(input)) File.Copy(input, backup);
            var target = RepositoryGit.FilePath(root, item.Folder);
            if (Directory.Exists(target)) CopyDirectory(target, Path.Combine(transaction, "before-projection"));
            var registryPath = (item.Kind == "composition" ? ".tcom" : ".tdom") + "/index.json";
            var registryHash = File.Exists(RepositoryGit.FilePath(root, registryPath)) ? ModelJson.FileHash(RepositoryGit.FilePath(root, registryPath)) : null;
            if (registryUpdate != null)
            {
                if (File.Exists(RepositoryGit.FilePath(root, registryPath))) File.Copy(RepositoryGit.FilePath(root, registryPath), Path.Combine(transaction, "before-registry.json"));
                WriteRegistry(transaction, item.Kind, registryUpdate);
                File.Copy(RepositoryGit.FilePath(transaction, registryPath), Path.Combine(transaction, "registry.json"));
            }
            ModelJson.Write(Path.Combine(transaction, "journal.json"), ModelJson.Map("formatVersion", 1, "container", item.Path, "projection", item.Folder, "registry", registryUpdate == null ? null : registryPath));
            if ((File.Exists(input) ? ModelJson.FileHash(input) : null) != originalHash) throw new GitWorkflowException("EXTERNAL_CHANGE", "Container changed while preparing the transaction. Inputs retained for recovery.");
            if (DirectoryHash(target) != startingProjection) throw new GitWorkflowException("EXTERNAL_CHANGE", "Projection changed while preparing the transaction. Inputs retained for recovery.");
            if (registryUpdate != null && (File.Exists(RepositoryGit.FilePath(root, registryPath)) ? ModelJson.FileHash(RepositoryGit.FilePath(root, registryPath)) : null) != registryHash) throw new GitWorkflowException("EXTERNAL_CHANGE", "Registry changed while preparing the transaction. Inputs retained for recovery.");
            if (originalHash != ModelJson.FileHash(candidate)) { Directory.CreateDirectory(Path.GetDirectoryName(input)); File.Copy(candidate, input, true); }
            if (DirectoryHash(projection) != startingProjection) ReplaceDirectory(projection, target, root);
            if (registryUpdate != null) { Directory.CreateDirectory(Path.GetDirectoryName(RepositoryGit.FilePath(root, registryPath))); File.Copy(Path.Combine(transaction, "registry.json"), RepositoryGit.FilePath(root, registryPath), true); }
            Cache(root, input);
            DeleteControlled(transaction, state);
            DocumentDiskState.Accept(input);
            }
        }

        internal static void WriteProjection(string container, string directory, TrackingRegistration item)
        {
            var package = ModelPackage.Read(container); var parts = new List<object>();
            foreach (var part in package.Content())
            {
                var output = RepositoryGit.FilePath(directory, part.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllBytes(output, part.Value);
                parts.Add(ModelJson.Map("path", part.Key, "sha256", ModelJson.Hash(part.Value)));
            }
            ModelJson.Write(Path.Combine(directory, "projection.json"), ModelJson.Map("formatVersion", 1, "generatorVersion", 1, "trackingId", item.Id,
                "sourceContainer", item.Path, "sourceContainerSha256", ModelJson.FileHash(container), "contentFingerprint", ModelJson.Get(package.Manifest, "contentFingerprint"), "parts", parts));
        }

        internal static void Cache(string root, string input)
        {
            var cache = Path.Combine(RepositoryGit.LocalState(root), "baselines"); Directory.CreateDirectory(cache);
            var path = Path.Combine(cache, ModelJson.FileHash(input) + ".tcom"); if (!File.Exists(path)) File.Copy(input, path);
        }

        public static string Import(string input, bool apply, string baseline = null, string resolutions = null)
        {
            var root = RepositoryGit.Discover(input); RequireNoTransaction(root);
            var item = Find(root, input); if (item == null) throw new GitWorkflowException("NOT_ENABLED", "Tracking is not enabled.");
            var dir = RepositoryGit.FilePath(root, item.Folder); var metadata = ModelJson.Read(Path.Combine(dir, "projection.json"));
            var expectedHash = ModelJson.Str(metadata, "sourceContainerSha256");
            if (baseline == null) baseline = FindBaseline(root, item, expectedHash);
            if (baseline == null || !File.Exists(baseline) || ModelJson.FileHash(baseline) != expectedHash) throw new GitWorkflowException("BASELINE_REQUIRED", "Supply --baseline with the container matching the projection's recorded source hash.");
            var proposed = ModelPackage.Read(baseline);
            var original = proposed.Content();
            foreach (var key in original.Keys) proposed.Parts.Remove(key);
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var name = RepositoryGit.Relative(dir, file); if (name == "projection.json") continue;
                ModelPackage.SafeName(name); if (ModelPackage.Derived(name)) throw new GitWorkflowException("GENERATED_PART_EDIT", name);
                proposed.Parts[name] = File.ReadAllBytes(file);
            }
            var edits = original.Keys.Union(proposed.Content().Keys).Where(k => !original.ContainsKey(k) || !proposed.Content().ContainsKey(k) || ModelJson.Hash(original[k]) != ModelJson.Hash(proposed.Content()[k]))
                .Select(k => (!original.ContainsKey(k) ? "ADD " : !proposed.Parts.ContainsKey(k) ? "DELETE " : "MODIFY ") + k).ToArray();
            var report = String.Join("\n", edits) + "\n" + ModelMerge.DescribePackages(ModelPackage.Read(baseline), proposed);
            try { proposed.Validate(); }
            catch (Exception problem) { if (!apply) return "Projection import preview (invalid candidate; no changes applied):\n" + report + "\nValidation: " + problem.Message; throw; }
            if (edits.Length > 0) ModelMerge.RefreshDerived(proposed);
            if (ModelJson.FileHash(input) != expectedHash)
            {
                var merge = ModelMerge.MergePackages(ModelPackage.Read(baseline), ModelPackage.Read(input), proposed, resolutions == null ? null : ModelJson.Read(resolutions));
                if (merge.Conflicts.Count > 0)
                {
                    var session = Path.Combine(RepositoryGit.LocalState(root), "merges", Guid.NewGuid().ToString("D")); Directory.CreateDirectory(session);
                    File.Copy(baseline, Path.Combine(session, "1.tcom")); File.Copy(input, Path.Combine(session, "2.tcom")); proposed.Write(Path.Combine(session, "3.tcom"));
                    CopyDirectory(dir, Path.Combine(session, "projection-input"));
                    ModelJson.Write(Path.Combine(session, "session.json"), ModelJson.Map("formatVersion", 1, "repository", root, "mode", "projection-import", "path", item.Path, "originalWorkingHash", ModelJson.FileHash(input), "projectionHash", DirectoryHash(dir)));
                    ModelJson.Write(Path.Combine(session, "resolutions.json"), resolutions == null ? ModelJson.Map("formatVersion", 1, "resolutions", new object[0]) : ModelJson.Read(resolutions));
                    ModelJson.Write(Path.Combine(session, "conflicts.json"), merge.ToGraph());
                    throw new GitWorkflowException("MERGE_CONFLICT", "Projection changes and container changes conflict. Inputs retained; resolve using --session " + session + "\n" + ModelJson.Text(merge.ToGraph()));
                }
                proposed = merge.Package;
            }
            if (!apply) return "Projection import preview (snapshot removals are deletions):\n" + report;
            RefreshInternal(root, item, proposed, true);
            return "Projection imported and validated.\n" + report;
        }

        private static string FindBaseline(string root, TrackingRegistration item, string expectedHash)
        {
            if (expectedHash == null || !System.Text.RegularExpressions.Regex.IsMatch(expectedHash, "^[0-9a-f]{64}$")) return null;
            var input = RepositoryGit.FilePath(root, item.Path);
            if (File.Exists(input) && ModelJson.FileHash(input) == expectedHash) return input;
            var baseline = Path.Combine(RepositoryGit.LocalState(root), "baselines", expectedHash + ".tcom");
            if (File.Exists(baseline) && ModelJson.FileHash(baseline) == expectedHash) return baseline;
            var history = RepositoryGit.Run(root, new[] { "rev-list", "--all", "--", item.Path }, check: false).Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var revision in history)
            {
                var candidate = RepositoryGit.Run(root, new[] { "show", revision + ":" + item.Path }, check: false);
                if (candidate.ExitCode != 0 || ModelJson.Hash(candidate.Bytes) != expectedHash) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(baseline)); File.WriteAllBytes(baseline, candidate.Bytes); return baseline;
            }
            return null;
        }

        public static ProjectionReport Verify(string root)
        {
            root = RepositoryGit.Discover(root); return VerifyDirectory(root);
        }
        internal static ProjectionReport VerifyDirectory(string root)
        {
            var report = new ProjectionReport();
            try
            {
                foreach (var item in Registrations(root))
                {
                    try
                    {
                        var file = RepositoryGit.FilePath(root, item.Path); var package = ModelPackage.Read(file); package.Validate();
                        var metadata = ModelJson.Read(RepositoryGit.FilePath(root, item.Folder + "/projection.json"));
                        var fingerprint = package.Fingerprint();
                        if (package.TrackingId != item.Id || ModelJson.Str(metadata, "trackingId") != item.Id || ModelJson.Str(metadata, "sourceContainer") != item.Path) throw new InvalidDataException("Tracking identity/path mismatch.");
                        if (ModelJson.Str(metadata, "formatVersion") != "1" || ModelJson.Str(metadata, "generatorVersion") != "1") throw new InvalidDataException("Unsupported projection format/generator.");
                        if (ModelJson.Str(metadata, "sourceContainerSha256") != ModelJson.FileHash(file)) throw new InvalidDataException("Exact container hash differs.");
                        foreach (var stamp in new[] { ModelJson.Get(metadata, "contentFingerprint"), ModelJson.Get(package.Manifest, "contentFingerprint") })
                            if (ModelJson.Str(stamp, "algorithm") != "sha256" || ModelJson.Str(stamp, "formatVersion") != "1" || ModelJson.Str(stamp, "value") != fingerprint) throw new InvalidDataException("Content fingerprint differs or uses an unsupported algorithm/version.");
                        var content = package.Content(); var entries = ModelJson.Array(ModelJson.Get(metadata, "parts")).ToList();
                        if (entries.Count != content.Count) throw new InvalidDataException("Part catalog differs.");
                        foreach (var part in content)
                        {
                            var disk = RepositoryGit.FilePath(root, item.Folder + "/" + part.Key);
                            if (!File.Exists(disk) || ModelJson.FileHash(disk) != ModelJson.Hash(part.Value)) throw new InvalidDataException("Projection differs: " + part.Key);
                            if (entries.Count(e => ModelJson.Str(e, "path") == part.Key && ModelJson.Str(e, "sha256") == ModelJson.Hash(part.Value)) != 1) throw new InvalidDataException("Part hash catalog differs: " + part.Key);
                        }
                        if (ProjectionEdited(root, item)) throw new InvalidDataException("Independent projection edits detected.");
                    }
                    catch (Exception e) { report.Diagnostics.Add(item.Path + ": " + e.Message); }
                }
            }
            catch (Exception e) { report.Diagnostics.Add(e.Message); }
            return report;
        }

        public static ProjectionReport VerifyIndex(string root) { return VerifySnapshot(root, null); }
        public static ProjectionReport VerifyRevision(string root, string revision) { RepositoryGit.Value(revision); return VerifySnapshot(root, revision); }
        private static ProjectionReport VerifySnapshot(string root, string revision)
        {
            var temp = Path.Combine(Path.GetTempPath(), "thinkcomposer-verify-" + Guid.NewGuid()); Directory.CreateDirectory(temp);
            try
            {
                var entries = revision == null ? RepositoryGit.Run(root, new[] { "ls-files", "--stage", "-z" }).Text.Split('\0') : RepositoryGit.Run(root, new[] { "ls-tree", "-r", "-z", revision }).Text.Split('\0');
                var files = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in entries.Where(s => s.Length > 0))
                {
                    var tab = entry.IndexOf('\t'); var info = entry.Substring(0, tab).Split(' '); var name = entry.Substring(tab + 1);
                    if (revision == null && info[2] != "0") return new ProjectionReport { Diagnostics = new List<string> { "Unresolved Git index entries." } };
                    if (info[0] != "100644" && info[0] != "100755") continue;
                    files[name] = revision == null ? info[1] : info[2];
                }
                foreach (var dir in Roots)
                {
                    string oid; if (!files.TryGetValue(dir + "/index.json", out oid)) continue;
                    WriteBlob(root, temp, dir + "/index.json", oid);
                }
                foreach (var item in Registrations(temp))
                {
                    foreach (var file in files.Where(f => f.Key == item.Path || f.Key.StartsWith(item.Folder + "/", StringComparison.Ordinal))) WriteBlob(root, temp, file.Key, file.Value);
                }
                return VerifyDirectory(temp);
            }
            finally { DeleteControlled(temp, Path.GetTempPath()); }
        }
        private static void WriteBlob(string root, string target, string name, string oid)
        {
            var file = RepositoryGit.FilePath(target, name); Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, RepositoryGit.Run(root, new[] { "cat-file", "blob", oid }).Bytes);
        }

        public static IEnumerable<string> ExpandPaths(string root, IEnumerable<string> paths)
        {
            var result = new HashSet<string>(StringComparer.Ordinal); var registrations = Registrations(root, true);
            foreach (var path in paths)
            {
                RepositoryGit.FilePath(root, path); result.Add(path);
                var item = registrations.FirstOrDefault(r => path == r.Path || path.StartsWith(r.Folder + "/", StringComparison.Ordinal));
                if (item == null) continue;
                result.Add(item.Path); result.Add(item.Folder); result.Add((item.Kind == "composition" ? ".tcom" : ".tdom") + "/index.json");
                var readme = (item.Kind == "composition" ? ".tcom" : ".tdom") + "/README.md"; if (File.Exists(RepositoryGit.FilePath(root, readme))) result.Add(readme);
            }
            return result;
        }

        internal static string Save(Uri location, Func<Uri, string> writer)
        {
            var input = location.LocalPath;
            try
            {
                DocumentDiskState.RequireUnchanged(input);
                var root = RepositoryGit.Discover(input, false); var registration = root == null ? null : Find(root, input);
                if (registration == null) { var result = writer(location); if (String.IsNullOrEmpty(result)) DocumentDiskState.Accept(input); return result; }
                RequireNoTransaction(root);
                if (ProjectionEdited(root, registration)) throw new GitWorkflowException("PROJECTION_EDITED", "Import or explicitly discard projection edits before saving.");
                var temp = Path.Combine(Path.GetDirectoryName(input), ".tc-save-" + Guid.NewGuid() + Path.GetExtension(input));
                var before = File.Exists(input) ? ModelJson.FileHash(input) : null;
                try
                {
                    var error = writer(new Uri(temp)); if (!String.IsNullOrEmpty(error)) return error;
                    if ((File.Exists(input) ? ModelJson.FileHash(input) : null) != before) throw new GitWorkflowException("EXTERNAL_CHANGE", "Container changed while saving; the original was retained.");
                    var candidate = ModelPackage.Read(temp);
                    if (File.Exists(input)) candidate.PreserveExtensions(ModelPackage.Read(input));
                    RefreshInternal(root, registration, candidate, false, expectedContainerHash: before); return null;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            catch (Exception e) { return "Managed save failed: " + e.Message; }
        }

        private static void RequireNoTransaction(string root)
        {
            if (Directory.Exists(Path.Combine(RepositoryGit.LocalState(root), "transaction"))) throw new GitWorkflowException("RECOVERY_REQUIRED", "An interrupted transaction exists. Run git recover --mode complete|rollback.");
        }
        public static string Recover(string root, bool rollback)
        {
            root = RepositoryGit.Discover(root); var state = RepositoryGit.LocalState(root); var tx = Path.Combine(state, "transaction");
            if (!File.Exists(Path.Combine(tx, "journal.json")))
            {
                if (!rollback) throw new GitWorkflowException("RECOVERY_INCOMPLETE", "Preparation did not finish. Use rollback; no published files were changed.");
                DeleteControlled(tx, state); return "Incomplete preparation discarded; published files were unchanged.";
            }
            var journal = ModelJson.Read(Path.Combine(tx, "journal.json"));
            var input = RepositoryGit.FilePath(root, ModelJson.Str(journal, "container")); var projection = RepositoryGit.FilePath(root, ModelJson.Str(journal, "projection"));
            var source = Path.Combine(tx, rollback ? "before.tcom" : "candidate.tcom");
            if (!File.Exists(source)) throw new GitWorkflowException("RECOVERY_INCOMPLETE", "Required recovery input is missing; retained transaction: " + tx);
            var registryCheck = ModelJson.Str(journal, "registry");
            if (registryCheck != null && File.Exists(RepositoryGit.FilePath(root, registryCheck)))
            {
                var registryHash = ModelJson.FileHash(RepositoryGit.FilePath(root, registryCheck));
                if (!new[] { "before-registry.json", "registry.json" }.Any(n => File.Exists(Path.Combine(tx, n)) && ModelJson.FileHash(Path.Combine(tx, n)) == registryHash))
                    throw new GitWorkflowException("RECOVERY_EXTERNAL_CHANGE", "Registration changed after the transaction; reconcile it before recovery.");
            }
            var currentHash = File.Exists(input) ? ModelJson.FileHash(input) : null;
            if (currentHash != null && !new[] { "before.tcom", "candidate.tcom" }.Any(n => File.Exists(Path.Combine(tx, n)) && ModelJson.FileHash(Path.Combine(tx, n)) == currentHash))
                throw new GitWorkflowException("RECOVERY_EXTERNAL_CHANGE", "The container changed after the interrupted transaction. Preserve/reconcile it before recovery; all transaction inputs remain available.");
            if (Directory.Exists(projection))
                foreach (var file in Directory.EnumerateFiles(projection, "*", SearchOption.AllDirectories))
                {
                    var relative = RepositoryGit.Relative(projection, file);
                    if (!new[] { "before-projection", "projection" }.Any(n => File.Exists(Path.Combine(tx, n, relative)) && ModelJson.FileHash(Path.Combine(tx, n, relative)) == ModelJson.FileHash(file)))
                        throw new GitWorkflowException("RECOVERY_EXTERNAL_CHANGE", "Projection has independent edits: " + relative + ". Reconcile these with the retained transaction inputs before recovery.");
                }
            ModelPackage.Read(source).Validate();
            File.Copy(source, input, true); var folder = Path.Combine(tx, rollback ? "before-projection" : "projection");
            if (Directory.Exists(folder)) ReplaceDirectory(folder, projection, root);
            else if (Directory.Exists(projection)) DeleteControlled(projection, root);
            var registry = ModelJson.Str(journal, "registry");
            if (registry != null)
            {
                var savedRegistry = Path.Combine(tx, rollback ? "before-registry.json" : "registry.json");
                var destination = RepositoryGit.FilePath(root, registry);
                if (File.Exists(savedRegistry)) { Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(savedRegistry, destination, true); }
                else if (rollback && File.Exists(destination)) File.Delete(destination);
            }
            DeleteControlled(tx, state); DocumentDiskState.Accept(input); return "Transaction " + (rollback ? "rolled back." : "completed.");
        }
        internal static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            { var target = RepositoryGit.FilePath(to, RepositoryGit.Relative(from, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target, true); }
        }
        internal static string DirectoryHash(string path)
        {
            if (!Directory.Exists(path)) return null;
            var catalog = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Select(p => new { Name = RepositoryGit.Relative(path, p), Hash = ModelJson.FileHash(RepositoryGit.FilePath(path, RepositoryGit.Relative(path, p))) }).OrderBy(p => p.Name, StringComparer.Ordinal);
            return ModelJson.Hash(ModelJson.Utf8.GetBytes(String.Join("\n", catalog.Select(p => p.Name + "\0" + p.Hash))));
        }
        private static void ReplaceDirectory(string from, string to, string root) { if (Directory.Exists(to)) DeleteControlled(to, root); CopyDirectory(from, to); }
        internal static void DeleteControlled(string path, string parent)
        {
            RepositoryGit.Relative(Path.GetFullPath(parent), path);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        private const string Guidance = "# ThinkComposer managed model projections\n\nContainers remain authoritative. This directory exists only because managed tracking was explicitly enabled. Git commands act on the whole containing repository.\n\n`index.json` and each `projection.json` are tooling-owned. Payload JSON and retained package assets are reviewable; preserve IDs and meaningful array ordering. Do not put operations/import directives into snapshot JSON. Do not edit fingerprint/hash fields to hide discrepancies.\n\nAfter editing payloads, run `thinkcomposer git projection import --input <container>` to preview all changes, then repeat with `--apply` to adopt them. Missing snapshot content means deletion and must be reviewed. Both-side edits require the recorded baseline. Never refresh over edits unless deliberately using `--discard-edits`.\n\nUse `git verify --repo <directory> --staged` before committing. Stage the container and its projection together with the registry. `git setup --repo <directory>` installs per-clone integration; cloning never executes setup automatically.\n\nUse `git recover --repo <directory> --mode complete|rollback` after an interrupted managed save. Disabling tracking retains inactive projections for explicit removal. Repository hooks can be bypassed; CI should verify revisions independently.\n";
    }
}
