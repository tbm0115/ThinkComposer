using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    internal sealed class ModelMergeResult
    {
        internal ModelPackage Package;
        internal List<object> Conflicts = new List<object>();
        internal object ToGraph() { return ModelJson.Map("formatVersion", 1, "conflicts", Conflicts); }
    }

    public static class ModelMerge
    {
        private static readonly object Missing = new object();
        private static readonly HashSet<string> Unordered = new HashSet<string>(new[] { "ideas", "relationships", "conceptDefinitions", "relationshipDefinitions", "markerDefinitions", "externalLanguages", "linkRoleVariants", "conceptDefinitionClusters", "relationshipDefinitionClusters", "markerClusters", "tableDefinitionCategories", "fieldDefinitionCategories", "tableDefinitions" }, StringComparer.Ordinal);

        internal static ModelMergeResult MergePackages(ModelPackage ancestor, ModelPackage current, ModelPackage incoming, object resolutions)
        {
            if (ancestor.Kind != current.Kind || current.Kind != incoming.Kind) throw new GitWorkflowException("KIND_MISMATCH", "Cannot merge different package kinds.");
            var result = new ModelMergeResult { Package = current };
            // The envelope may contain extension metadata unknown to this version.
            // Keep derived bookkeeping from the current package, but merge every other key.
            var envelope = new Dictionary<string, object>(current.Manifest, StringComparer.Ordinal);
            var generated = new HashSet<string>(new[] { "savedAtUtc", "applicationVersion", "authoritativeParts", "sidecars", "source", "legacyBinaryFallback", "modelTracking", "contentFingerprint" });
            foreach (var key in ancestor.Manifest.Keys.Union(current.Manifest.Keys).Union(incoming.Manifest.Keys).Where(k => !generated.Contains(k)))
            {
                var value = MergeValue(Value(ancestor.Manifest, key), Value(current.Manifest, key), Value(incoming.Manifest, key), "/manifest.json/" + Pointer(key), result.Conflicts, resolutions, new HashSet<string>());
                if (value == Missing) envelope.Remove(key); else envelope[key] = value;
            }
            current.Manifest = envelope;
            var output = new Dictionary<string, byte[]>(current.Parts, StringComparer.Ordinal);
            var ancestorContent = ancestor.Content(); var currentContent = current.Content(); var incomingContent = incoming.Content();
            foreach (var name in ancestorContent.Keys.Union(currentContent.Keys).Union(incomingContent.Keys).OrderBy(n => n, StringComparer.Ordinal))
            {
                byte[] a, b, c;
                ancestorContent.TryGetValue(name, out a); currentContent.TryGetValue(name, out b); incomingContent.TryGetValue(name, out c);
                object merged;
                if (ModelPackage.JsonParts.Contains(name))
                {
                    var av = Parse(a); var bv = Parse(b); var cv = Parse(c);
                    foreach (var g in new[] { av, bv, cv }) if (ModelJson.Obj(g) != null) ModelJson.Obj(g).Remove("exportedAtUtc");
                    var knownPaths = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var bytes in new[] { a, b, c }.Where(v => v != null))
                    {
                        var json = ModelJson.Utf8.GetString(bytes);
                        var known = name == "Domain.json" ? Definitor.DomainJsonInterchange.DomainJsonSerializer.Serialize(Definitor.DomainJsonInterchange.DomainJsonSerializer.Deserialize(json)) : JsonInterchange.CompositionJsonSerializer.Serialize(JsonInterchange.CompositionJsonSerializer.Deserialize(json));
                        CollectKnownPaths(ModelJson.Parse(known), "/" + name, knownPaths);
                    }
                    merged = MergeValue(av, bv, cv, "/" + name, result.Conflicts, resolutions, knownPaths);
                    if (merged == Missing) output.Remove(name); else output[name] = ModelJson.Utf8.GetBytes(ModelJson.Text(merged));
                }
                else
                {
                    merged = MergeValue(a == null ? Missing : Convert.ToBase64String(a), b == null ? Missing : Convert.ToBase64String(b), c == null ? Missing : Convert.ToBase64String(c), "/" + name, result.Conflicts, resolutions);
                    if (merged == Missing) output.Remove(name); else output[name] = Convert.FromBase64String((string)merged);
                }
            }
            current.Parts = output;
            // Derived previews/sidecars are never retained as if they describe a merged model.
            foreach (var key in output.Keys.Where(k => k.StartsWith("Previews/", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Interchange/", StringComparison.OrdinalIgnoreCase)).ToArray()) output.Remove(key);
            output.Remove("Composition.bin"); output.Remove("Domain.bin");
            current.Manifest["legacyBinaryFallback"] = ModelJson.Map("present", false);
            current.Manifest["sidecars"] = ModelJson.Map();
            RefreshDerived(current);
            if (current.TrackingId != null) current.Stamp(current.TrackingId);
            if (result.Conflicts.Count == 0)
                try { current.Validate(); }
                catch (Exception e) { result.Conflicts.Add(ModelJson.Map("path", "$validation", "kind", "validation", "message", e.Message)); }
            return result;
        }

        private static object Parse(byte[] bytes) { return bytes == null ? Missing : ModelJson.Parse(ModelJson.Utf8.GetString(bytes)); }
        private static bool Same(object a, object b) { return a == Missing || b == Missing ? a == b : ModelJson.Equal(a, b); }
        private static string Pointer(string name) { return name.Replace("~", "~0").Replace("/", "~1"); }
        private static object Value(Dictionary<string, object> map, string key) { object value; return map != null && map.TryGetValue(key, out value) ? value : Missing; }

        private static object MergeValue(object ancestor, object current, object incoming, string path, List<object> conflicts, object resolutions, HashSet<string> knownPaths = null)
        {
            if (Same(current, incoming)) return current;
            if (Same(ancestor, current)) return incoming;
            if (Same(ancestor, incoming)) return current;
            var a = ModelJson.Obj(ancestor); var b = ModelJson.Obj(current); var c = ModelJson.Obj(incoming);
            if (a != null && b != null && c != null && (knownPaths == null || knownPaths.Contains(path)))
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var key in a.Keys.Union(b.Keys).Union(c.Keys).OrderBy(k => k, StringComparer.Ordinal))
                {
                    // Export metadata is derived, not an independent authored model value.
                    if (key == "exportedAtUtc") continue;
                    if (key == "compatibilitySignature" || key == "relationshipCompatibility" || key == "targetContext" || key == "templateHash") { result[key] = Value(b, key) == Missing ? Value(c, key) : Value(b, key); continue; }
                    if (key == "versionSequence" || key == "modelRevision")
                    {
                        long left, right;
                        if (Int64.TryParse(Convert.ToString(ModelJson.Get(b, key)), out left) && Int64.TryParse(Convert.ToString(ModelJson.Get(c, key)), out right)) { result[key] = Math.Max(left, right) + 1; continue; }
                    }
                    if (key == "lastModification")
                    {
                        DateTimeOffset left, right;
                        if (DateTimeOffset.TryParse(ModelJson.Str(b, key), out left) && DateTimeOffset.TryParse(ModelJson.Str(c, key), out right)) { result[key] = left > right ? ModelJson.Get(b, key) : ModelJson.Get(c, key); continue; }
                    }
                    var merged = MergeValue(Value(a, key), Value(b, key), Value(c, key), path + "/" + Pointer(key), conflicts, resolutions, knownPaths);
                    if (merged != Missing) result[key] = merged;
                }
                return result;
            }
            var al = ModelJson.Array(ancestor).ToList(); var bl = ModelJson.Array(current).ToList(); var cl = ModelJson.Array(incoming).ToList();
            if ((knownPaths == null || knownPaths.Contains(path)) && ancestor is System.Collections.IList && current is System.Collections.IList && incoming is System.Collections.IList && Identified(al) && Identified(bl) && Identified(cl))
            {
                var ai = al.Select(v => MemberId(v)).ToList(); var bi = bl.Select(v => MemberId(v)).ToList(); var ci = cl.Select(v => MemberId(v)).ToList();
                var unordered = Unordered.Contains(path.Substring(path.LastIndexOf('/') + 1));
                if (unordered || ai.SequenceEqual(bi) || ai.SequenceEqual(ci) || bi.SequenceEqual(ci))
                {
                    var order = unordered ? ai.Concat(bi.Union(ci).Except(ai).OrderBy(v => v, StringComparer.Ordinal)).ToList() : ai.SequenceEqual(bi) ? ci : bi;
                    var result = new List<object>();
                    foreach (var id in order.Union(ai).Union(bi).Union(ci))
                    {
                        var merged = MergeValue(al.FirstOrDefault(v => MemberId(v) == id) ?? Missing, bl.FirstOrDefault(v => MemberId(v) == id) ?? Missing, cl.FirstOrDefault(v => MemberId(v) == id) ?? Missing,
                            path + "/@" + Pointer(id), conflicts, resolutions, knownPaths);
                        if (merged != Missing) result.Add(merged);
                    }
                    return result.ToArray();
                }
            }
            var selection = ModelJson.Array(ModelJson.Get(resolutions, "resolutions")).FirstOrDefault(r => ModelJson.Str(r, "path") == path);
            if (selection != null)
            {
                switch (ModelJson.Str(selection, "choice"))
                {
                    case "current": return current;
                    case "incoming": return incoming;
                    case "ancestor": return ancestor;
                    case "delete": return Missing;
                    case "custom": return ModelJson.Get(selection, "value");
                    default: throw new GitWorkflowException("INVALID_RESOLUTION", "Invalid resolution at " + path);
                }
            }
            conflicts.Add(ModelJson.Map("path", path, "kind", current == Missing || incoming == Missing ? "delete-edit" : ancestor == Missing ? "identity-collision" : "competing-edit",
                "ancestorExists", ancestor != Missing, "ancestor", ancestor == Missing ? null : ancestor,
                "currentExists", current != Missing, "current", current == Missing ? null : current,
                "incomingExists", incoming != Missing, "incoming", incoming == Missing ? null : incoming));
            return current;
        }
        private static string MemberId(object item) { return ModelJson.Str(item, "id") ?? ModelJson.Str(item, "representationId"); }
        private static bool Identified(List<object> items) { return items.All(v => !String.IsNullOrEmpty(MemberId(v))) && items.Select(v => MemberId(v)).Distinct().Count() == items.Count; }
        private static void CollectKnownPaths(object graph, string path, HashSet<string> paths)
        {
            paths.Add(path); var map = ModelJson.Obj(graph);
            if (map != null) foreach (var entry in map) CollectKnownPaths(entry.Value, path + "/" + Pointer(entry.Key), paths);
            else foreach (var item in ModelJson.Array(graph)) { var id = MemberId(item); if (id != null) CollectKnownPaths(item, path + "/@" + Pointer(id), paths); }
        }

        // Mirrors the persisted Domain compatibility catalog without rehydrating WPF objects
        // or replacing unknown JSON fields with a typed serializer's reduced schema.
        internal static void RefreshDerived(ModelPackage package)
        {
            var graph = ModelJson.Parse(ModelJson.Utf8.GetString(package.Parts["Domain.json"]));
            var domain = ModelJson.Get(graph, "domain"); var version = ModelJson.Get(domain, "set");
            Func<object, string, string> s = (o, k) => ModelJson.Str(o, k) ?? "";
            Func<object, string, string> boolean = (o, k) => Object.Equals(ModelJson.Get(o, k), true) ? "True" : "False";
            var lines = new List<string> { "domain|id=" + s(domain, "id") + "|tech=" + s(domain, "techName") + "|version=" + s(version, "versionSequence") + "|modified=" + s(version, "lastModification") };
            foreach (var language in ModelJson.Array(ModelJson.Get(graph, "externalLanguages"))) lines.Add("externalLanguage|id=" + s(language, "id") + "|tech=" + s(language, "techName"));
            var summaries = new List<object>();
            foreach (var collection in new[] { "conceptDefinitions", "relationshipDefinitions" })
                foreach (var definition in ModelJson.Array(ModelJson.Get(graph, collection)))
                {
                    var tech = s(definition, "techName"); var relation = collection == "relationshipDefinitions";
                    lines.Add((relation ? "relationshipDefinition" : "conceptDefinition") + "|id=" + s(definition, "id") + "|tech=" + tech +
                        (relation ? "|simple=" + boolean(definition, "isSimple") + "|directional=" + boolean(definition, "isDirectional") + "|hideCentral=" + boolean(definition, "hideCentralSymbolWhenSimple") : "|ancestor=" + s(definition, "ancestorTechName") + "|table=" + s(definition, "dataTypeTechName")));
                    var details = ModelJson.Array(ModelJson.Get(definition, "detailDesignators")).ToArray();
                    for (int i = 0; i < details.Length; i++) lines.Add("detailDesignator|owner=" + tech + "|order=" + i + "|id=" + s(details[i], "id") + "|tech=" + s(details[i], "techName") + "|kind=" + s(details[i], "kind") + "|table=" + s(details[i], "tableDefinitionTechName"));
                    var roles = ModelJson.Array(ModelJson.Get(definition, "roleDefinitions")).ToArray();
                    foreach (var role in roles) lines.Add("relationshipRole|owner=" + tech + "|id=" + s(role, "id") + "|tech=" + s(role, "techName") + "|type=" + s(role, "roleType") + "|allowedIdeas=" + String.Join(",", ModelJson.Array(ModelJson.Get(role, "associableIdeaDefinitionTechNames")).Select(Convert.ToString).OrderBy(t => t)) + "|allowedVariants=" + String.Join(",", ModelJson.Array(ModelJson.Get(role, "allowedVariantTechNames")).Select(Convert.ToString).OrderBy(t => t)));
                    if (relation && roles.Length >= 2)
                    {
                        var origin = roles.FirstOrDefault(r => s(r, "roleType") == "Origin"); var target = roles.FirstOrDefault(r => s(r, "roleType") == "Target");
                        if (origin != null && target != null) summaries.Add(ModelJson.Map("relationshipDefinitionId", s(definition, "id"), "relationshipDefinitionTechName", tech, "relationshipDefinitionName", s(definition, "name"), "originRoleTechName", s(origin, "techName"), "originRoleName", s(origin, "name"), "targetRoleTechName", s(target, "techName"), "targetRoleName", s(target, "name"), "allowedOriginConceptDefinitionTechNames", ModelJson.Array(ModelJson.Get(origin, "associableIdeaDefinitionTechNames")).ToArray(), "allowedTargetConceptDefinitionTechNames", ModelJson.Array(ModelJson.Get(target, "associableIdeaDefinitionTechNames")).ToArray(), "allowedOriginVariantTechNames", ModelJson.Array(ModelJson.Get(origin, "allowedVariantTechNames")).ToArray(), "allowedTargetVariantTechNames", ModelJson.Array(ModelJson.Get(target, "allowedVariantTechNames")).ToArray(), "isDirectional", ModelJson.Get(definition, "isDirectional"), "isSimple", ModelJson.Get(definition, "isSimple"), "hideCentralSymbolWhenSimple", ModelJson.Get(definition, "hideCentralSymbolWhenSimple")));
                    }
                }
            foreach (var table in ModelJson.Array(ModelJson.Get(graph, "tableDefinitions")))
            {
                lines.Add("tableDefinition|id=" + s(table, "id") + "|tech=" + s(table, "techName"));
                foreach (var field in ModelJson.Array(ModelJson.Get(table, "fields"))) lines.Add("fieldDefinition|owner=" + s(table, "techName") + "|id=" + s(field, "id") + "|tech=" + s(field, "techName") + "|type=" + s(field, "dataTypeTechName") + "|order=" + s(field, "order"));
            }
            lines.Sort(StringComparer.Ordinal); var signature = ModelJson.Hash(ModelJson.Utf8.GetBytes(String.Join("\n", lines)));
            ModelJson.Obj(domain)["compatibilitySignature"] = signature;
            ModelJson.Obj(graph)["relationshipCompatibility"] = summaries.OrderBy(r => s(r, "relationshipDefinitionTechName")).ToArray();
            foreach (var obj in Descendants(graph))
            {
                var template = ModelJson.Str(obj, "templateText"); var settings = ModelJson.Obj(ModelJson.Get(obj, "set"));
                if (template != null && settings != null && settings.ContainsKey("templateHash")) settings["templateHash"] = Generation.OutputTemplateDiagnostics.HashText(template).Substring(0, 16);
            }
            package.Parts["Domain.json"] = ModelJson.Utf8.GetBytes(ModelJson.Text(graph));
            foreach (var name in ModelPackage.JsonParts.Where(n => n != "Domain.json" && package.Parts.ContainsKey(n)))
            {
                var composition = ModelJson.Parse(ModelJson.Utf8.GetString(package.Parts[name]));
                var info = ModelJson.Get(composition, "composition"); var embedded = ModelJson.Obj(ModelJson.Get(info, "domain"));
                if (embedded != null) { embedded["compatibilitySignature"] = signature; foreach (var key in new[] { "name", "techName", "summary", "techSpec" }) if (ModelJson.Get(domain, key) != null) embedded[key] = ModelJson.Get(domain, key); }
                var context = ModelJson.Obj(ModelJson.Get(composition, "targetContext"));
                if (context != null)
                    foreach (var pair in new[] { Tuple.Create("domain", domain, version), Tuple.Create("composition", info, ModelJson.Get(info, "version")) })
                    {
                        var element = ModelJson.Obj(ModelJson.Get(context, pair.Item1)); if (element == null) continue;
                        foreach (var key in new[] { "id", "name", "techName", "compatibilitySignature" }) if (ModelJson.Get(pair.Item2, key) != null) element[key] = ModelJson.Get(pair.Item2, key);
                        foreach (var key in new[] { "versionSequence", "versionNumber", "lastModification" }) if (ModelJson.Get(pair.Item3, key) != null) element[key] = ModelJson.Get(pair.Item3, key);
                    }
                package.Parts[name] = ModelJson.Utf8.GetBytes(ModelJson.Text(composition));
            }
        }

        internal static string DescribePackages(ModelPackage before, ModelPackage after)
        {
            var changes = new List<string>();
            foreach (var part in ModelPackage.JsonParts)
            {
                byte[] a, b; before.Parts.TryGetValue(part, out a); after.Parts.TryGetValue(part, out b);
                Describe(Parse(a), Parse(b), "/" + part, changes);
            }
            return String.Join("\n", changes);
        }
        private static void Describe(object a, object b, string path, List<string> changes)
        {
            if (Same(a, b)) return;
            var am = ModelJson.Obj(a); var bm = ModelJson.Obj(b);
            if (am != null && bm != null) { foreach (var k in am.Keys.Union(bm.Keys)) Describe(Value(am, k), Value(bm, k), path + "/" + Pointer(k), changes); return; }
            var al = ModelJson.Array(a).ToList(); var bl = ModelJson.Array(b).ToList();
            if (a is System.Collections.IList && b is System.Collections.IList && Identified(al) && Identified(bl))
            {
                foreach (var id in al.Concat(bl).Select(v => MemberId(v)).Distinct()) Describe(al.FirstOrDefault(v => MemberId(v) == id) ?? Missing, bl.FirstOrDefault(v => MemberId(v) == id) ?? Missing, path + "/@" + id, changes);
                if (!al.Select(v => MemberId(v)).SequenceEqual(bl.Select(v => MemberId(v)))) changes.Add("ORDER/MEMBERS " + path);
                return;
            }
            changes.Add((a == Missing ? "ADD " : b == Missing ? "DELETE " : "MODIFY ") + path + "\n  before: " + (a == Missing ? "<absent>" : ModelJson.Text(a).Trim()) + "\n  after: " + (b == Missing ? "<absent>" : ModelJson.Text(b).Trim()));
        }

        internal static void ValidateGraph(string name, object graph, IDictionary<string, byte[]> parts)
        {
            ValidateCollections(graph, "/" + name);
            if (name == "Domain.json") { ValidateDomain(graph); return; }
            var ideas = ModelJson.Array(ModelJson.Get(graph, "ideas")).Concat(ModelJson.Array(ModelJson.Get(graph, "relationships"))).ToList();
            var ids = new HashSet<string>(ideas.Select(v => MemberId(v)).Where(v => v != null));
            if (ids.Count != ideas.Count) throw new GitWorkflowException("DUPLICATE_ID", "Every model entity must have a unique identity across concepts and relationships.");
            var root = ModelJson.Str(ModelJson.Get(graph, "composition"), "id"); if (root != null && !ids.Add(root)) throw new GitWorkflowException("DUPLICATE_ID", "Composition root identity collides with a contained entity.");
            var domain = ModelJson.Parse(ModelJson.Utf8.GetString(parts["Domain.json"]));
            var domainReference = ModelJson.Str(ModelJson.Get(ModelJson.Get(graph, "composition"), "domain"), "id");
            if (domainReference != null && domainReference != ModelJson.Str(ModelJson.Get(domain, "domain"), "id")) throw new GitWorkflowException("DOMAIN_MISMATCH", name + " does not refer to this embedded Domain.");
            var definitions = ModelJson.Array(ModelJson.Get(domain, "conceptDefinitions")).Concat(ModelJson.Array(ModelJson.Get(domain, "relationshipDefinitions"))).ToList();
            var defIds = new HashSet<string>(definitions.Select(v => MemberId(v)).Where(v => v != null));
            var defNames = new HashSet<string>(definitions.Select(v => ModelJson.Str(v, "techName")).Where(v => v != null));
            foreach (var idea in ideas)
            {
                RequireReference(idea, "containerId", ids);
                var definition = ModelJson.Str(idea, "definitionId");
                if (!String.IsNullOrEmpty(definition) && !defIds.Contains(definition)) throw new GitWorkflowException("INVALID_REFERENCE", "Definition was removed: " + definition);
                if (String.IsNullOrEmpty(definition)) { var tech = ModelJson.Str(idea, "definitionTechName"); if (!String.IsNullOrEmpty(tech) && !defNames.Contains(tech)) throw new GitWorkflowException("INVALID_REFERENCE", "Unknown definition: " + tech); }
                var def = definitions.FirstOrDefault(d => !String.IsNullOrEmpty(definition) ? ModelJson.Str(d, "id") == definition : ModelJson.Str(d, "techName") == ModelJson.Str(idea, "definitionTechName"));
                foreach (var link in ModelJson.Array(ModelJson.Get(idea, "links")))
                {
                    RequireReference(link, "ideaId", ids);
                    var roles = ModelJson.Array(ModelJson.Get(def, "roleDefinitions")).ToList();
                    var role = roles.FirstOrDefault(r => ModelJson.Str(link, "roleDefinitionId") != null ? ModelJson.Str(r, "id") == ModelJson.Str(link, "roleDefinitionId") : ModelJson.Str(r, "roleType") == ModelJson.Str(link, "roleType"));
                    if (role == null) throw new GitWorkflowException("INVALID_ROLE", "Relationship link refers to a missing role: " + ModelJson.Str(idea, "id"));
                    if (ModelJson.Str(link, "roleType") != null && ModelJson.Str(link, "roleType") != ModelJson.Str(role, "roleType")) throw new GitWorkflowException("INVALID_ROLE", "Link role type disagrees with its role definition.");
                    var variant = ModelJson.Str(link, "roleVariantTechName"); var variants = ModelJson.Array(ModelJson.Get(role, "allowedVariantTechNames")).Select(Convert.ToString).ToArray();
                    if (variant != null && variants.Length > 0 && !variants.Contains(variant)) throw new GitWorkflowException("INVALID_ROLE_VARIANT", variant);
                    var allowed = ModelJson.Array(ModelJson.Get(role, "associableIdeaDefinitionTechNames")).Select(Convert.ToString).ToArray();
                    var linked = ideas.FirstOrDefault(i => ModelJson.Str(i, "id") == ModelJson.Str(link, "ideaId"));
                    if (allowed.Length > 0 && linked != null)
                    {
                        var linkedDefinition = definitions.FirstOrDefault(d => ModelJson.Str(d, "id") == ModelJson.Str(linked, "definitionId") || ModelJson.Str(d, "techName") == ModelJson.Str(linked, "definitionTechName"));
                        var compatible = false; var visitedDefinitions = new HashSet<string>();
                        while (linkedDefinition != null && visitedDefinitions.Add(ModelJson.Str(linkedDefinition, "techName")))
                        {
                            if (allowed.Contains(ModelJson.Str(linkedDefinition, "techName"))) { compatible = true; break; }
                            var ancestorName = ModelJson.Str(linkedDefinition, "ancestorTechName");
                            linkedDefinition = ancestorName == null ? null : definitions.FirstOrDefault(d => ModelJson.Str(d, "techName") == ancestorName);
                        }
                        if (!compatible) throw new GitWorkflowException("INCOMPATIBLE_ROLE", "Linked definition is not allowed for this role.");
                    }
                }
                foreach (var detail in ModelJson.Array(ModelJson.Get(idea, "details")))
                {
                    var designator = ModelJson.Array(ModelJson.Get(def, "detailDesignators")).FirstOrDefault(d => ModelJson.Str(detail, "designatorId") != null ? ModelJson.Str(d, "id") == ModelJson.Str(detail, "designatorId") : ModelJson.Str(d, "techName") == ModelJson.Str(detail, "designatorTechName"));
                    if (designator != null && ModelJson.Str(detail, "kind") != ModelJson.Str(designator, "kind")) throw new GitWorkflowException("DETAIL_KIND", "Detail and designator kinds are incompatible.");
                    var fieldNames = new HashSet<string>(ModelJson.Array(ModelJson.Get(detail, "fields")).Select(f => ModelJson.Str(f, "techName")).Where(f => f != null));
                    foreach (var record in ModelJson.Array(ModelJson.Get(detail, "records")))
                    {
                        var row = ModelJson.Obj(record);
                        if (row != null && fieldNames.Count > 0 && row.Keys.Any(k => !fieldNames.Contains(k))) throw new GitWorkflowException("TABLE_FIELD", "A table record refers to a missing field.");
                    }
                }
            }
            var byId = ideas.Where(v => MemberId(v) != null).ToDictionary(v => MemberId(v));
            foreach (var idea in ideas)
            {
                var visited = new HashSet<string>(); var node = idea;
                while (node != null)
                {
                    var id = ModelJson.Str(node, "id"); if (id != null && !visited.Add(id)) throw new GitWorkflowException("OWNERSHIP_CYCLE", id);
                    var owner = ModelJson.Str(node, "containerId"); if (owner == null || !byId.TryGetValue(owner, out node)) break;
                }
            }
            foreach (var view in ModelJson.Array(ModelJson.Get(graph, "views")))
            {
                RequireReference(view, "ownerIdeaId", ids);
                var representations = new HashSet<string>(ModelJson.Array(ModelJson.Get(view, "visuals")).Select(v => ModelJson.Str(v, "representationId")).Where(id => id != null));
                foreach (var obj in Descendants(view))
                {
                    foreach (var field in new[] { "ideaId", "originIdeaId", "targetIdeaId", "associatedIdeaId" }) RequireReference(obj, field, ids);
                    foreach (var field in new[] { "originRepresentationId", "targetRepresentationId" }) RequireReference(obj, field, representations);
                }
            }
        }
        private static void ValidateDomain(object graph)
        {
            var definitions = ModelJson.Array(ModelJson.Get(graph, "conceptDefinitions")).Concat(ModelJson.Array(ModelJson.Get(graph, "relationshipDefinitions"))).ToList();
            var ids = definitions.Select(d => ModelJson.Str(d, "id")).ToArray();
            if (ids.Any(String.IsNullOrEmpty) || ids.Distinct().Count() != ids.Length) throw new GitWorkflowException("DUPLICATE_ID", "Domain definitions require unique identities.");
            var tables = new HashSet<string>(ModelJson.Array(ModelJson.Get(graph, "tableDefinitions")).Select(t => ModelJson.Str(t, "id")));
            foreach (var definition in definitions)
            {
                foreach (var detail in ModelJson.Array(ModelJson.Get(definition, "detailDesignators"))) RequireReference(detail, "tableDefinitionId", tables);
                foreach (var role in ModelJson.Array(ModelJson.Get(definition, "roleDefinitions")))
                {
                    var owner = ModelJson.Str(role, "ownerTechName");
                    if (owner != null && owner != ModelJson.Str(definition, "techName")) throw new GitWorkflowException("ROLE_OWNERSHIP", "Role owner disagrees with its containing definition.");
                }
            }
        }
        private static void RequireReference(object graph, string field, HashSet<string> allowed)
        {
            var value = ModelJson.Str(graph, field);
            if (!String.IsNullOrEmpty(value) && !allowed.Contains(value)) throw new GitWorkflowException("INVALID_REFERENCE", field + " points to missing element " + value);
        }
        private static IEnumerable<object> Descendants(object graph)
        {
            var map = ModelJson.Obj(graph);
            if (map != null) { yield return graph; foreach (var value in map.Values) foreach (var child in Descendants(value)) yield return child; }
            else foreach (var value in ModelJson.Array(graph)) foreach (var child in Descendants(value)) yield return child;
        }
        private static void ValidateCollections(object graph, string path)
        {
            var map = ModelJson.Obj(graph);
            if (map != null) { foreach (var pair in map) ValidateCollections(pair.Value, path + "/" + pair.Key); return; }
            var list = ModelJson.Array(graph).ToList();
            var ids = list.Select(v => MemberId(v)).Where(v => !String.IsNullOrEmpty(v)).ToList();
            if (ids.Distinct().Count() != ids.Count) throw new GitWorkflowException("DUPLICATE_ID", "Duplicate identity at " + path);
            foreach (var item in list) ValidateCollections(item, path);
        }

        public static string Compare(string before, string after) { return DescribePackages(ModelPackage.Read(before), ModelPackage.Read(after)); }

        public static string StartSession(string root, string relative)
        {
            root = RepositoryGit.Discover(root); RepositoryGit.FilePath(root, relative);
            if (RepositoryGit.Run(root, new[] { "ls-files", "--unmerged", "-z", "--", relative }).Bytes.Length == 0) throw new GitWorkflowException("NO_MODEL_CONFLICT", "This path has no unmerged Git stages.");
            var sessions = Path.Combine(RepositoryGit.LocalState(root), "merges");
            if (Directory.Exists(sessions))
                foreach (var existing in Directory.EnumerateDirectories(sessions))
                {
                    var file = Path.Combine(existing, "session.json"); if (!File.Exists(file)) continue;
                    var previous = ModelJson.Read(file);
                    if (ModelJson.Str(previous, "path") == relative && !Object.Equals(ModelJson.Get(previous, "applied"), true) && !Object.Equals(ModelJson.Get(previous, "aborted"), true) && SessionMatches(root, existing)) return existing;
                }
            var session = Path.Combine(RepositoryGit.LocalState(root), "merges", Guid.NewGuid().ToString("D")); Directory.CreateDirectory(session);
            for (int stage = 1; stage <= 3; stage++)
            {
                var blob = RepositoryGit.Run(root, new[] { "show", ":" + stage + ":" + relative }, check: false);
                if (blob.ExitCode == 0) File.WriteAllBytes(Path.Combine(session, stage + ".tcom"), blob.Bytes);
            }
            ModelJson.Write(Path.Combine(session, "session.json"), ModelJson.Map("formatVersion", 1, "repository", root, "path", relative, "originalWorkingHash", File.Exists(RepositoryGit.FilePath(root, relative)) ? ModelJson.FileHash(RepositoryGit.FilePath(root, relative)) : null));
            ModelJson.Write(Path.Combine(session, "resolutions.json"), ModelJson.Map("formatVersion", 1, "resolutions", new object[0]));
            PreviewSession(session); return session;
        }
        public static string PreviewSession(string session)
        {
            var choice = ModelJson.Array(ModelJson.Get(ModelJson.Read(Path.Combine(session, "resolutions.json")), "resolutions")).FirstOrDefault(r => ModelJson.Str(r, "path") == "$package");
            if (choice != null)
            {
                var selection = ModelJson.Str(choice, "choice");
                if (selection != "delete")
                {
                    var chosen = Path.Combine(session, selection == "current" ? "2.tcom" : selection == "incoming" ? "3.tcom" : selection == "ancestor" ? "1.tcom" : "invalid");
                    ModelPackage.Read(chosen).Validate(); File.Copy(chosen, Path.Combine(session, "resolved.tcom"), true);
                }
                var resolved = ModelJson.Map("formatVersion", 1, "conflicts", new object[0]); ModelJson.Write(Path.Combine(session, "conflicts.json"), resolved); return ModelJson.Text(resolved);
            }
            foreach (var n in new[] { "1", "2", "3" }) if (!File.Exists(Path.Combine(session, n + ".tcom")))
            {
                var special = ModelJson.Map("formatVersion", 1, "conflicts", new[] { ModelJson.Map("path", "$package", "kind", "missing-ancestor-or-side", "message", "Choose current, incoming, or delete explicitly for this add/add or delete conflict.") });
                ModelJson.Write(Path.Combine(session, "conflicts.json"), special); return ModelJson.Text(special);
            }
            var result = MergePackages(ModelPackage.Read(Path.Combine(session, "1.tcom")), ModelPackage.Read(Path.Combine(session, "2.tcom")), ModelPackage.Read(Path.Combine(session, "3.tcom")), ModelJson.Read(Path.Combine(session, "resolutions.json")));
            ModelJson.Write(Path.Combine(session, "conflicts.json"), result.ToGraph());
            if (result.Conflicts.Count == 0) result.Package.Write(Path.Combine(session, "resolved.tcom"));
            return ModelJson.Text(result.ToGraph());
        }
        public static string ApplySession(string root, string session)
        {
            root = RepositoryGit.Discover(root); var meta = ModelJson.Read(Path.Combine(session, "session.json")); var path = ModelJson.Str(meta, "path"); var target = RepositoryGit.FilePath(root, path);
            if (!SessionMatches(root, session)) throw new GitWorkflowException("SESSION_CHANGED", "The repository or unmerged inputs changed. Start a new resolver session; previous inputs and decisions are retained.");
            var registration = ModelTracking.Find(root, target);
            var import = ModelJson.Str(meta, "mode") == "projection-import";
            if (registration != null && !import)
            {
                if (RepositoryGit.Status(root).Changes.Any(c => c.Path.StartsWith(registration.Folder + "/", StringComparison.Ordinal) && (c.WorkTree != " " || c.Conflict)))
                    throw new GitWorkflowException("PROJECTION_EDITED", "Independent projection edits or conflicts must be preserved/reconciled before applying this model resolution.");
                foreach (var n in new[] { "2", "3" }) if (File.Exists(Path.Combine(session, n + ".tcom"))) GitModelIntegration.VerifySourceRevision(root, path, Path.Combine(session, n + ".tcom"));
            }
            var recorded = ModelJson.Str(meta, "originalWorkingHash");
            if ((File.Exists(target) ? ModelJson.FileHash(target) : null) != recorded) throw new GitWorkflowException("EXTERNAL_CHANGE", "The working container changed since this merge session began.");
            var whole = ModelJson.Array(ModelJson.Get(ModelJson.Read(Path.Combine(session, "resolutions.json")), "resolutions")).FirstOrDefault(r => ModelJson.Str(r, "path") == "$package");
            string resolvedPath;
            if (whole != null)
            {
                var choice = ModelJson.Str(whole, "choice");
                if (choice == "delete")
                {
                    if (registration != null)
                    {
                        var all = ModelTracking.Registrations(root, true); all.Single(r => r.Id == registration.Id).Active = false; ModelTracking.WriteRegistry(root, registration.Kind, all);
                    }
                    if (File.Exists(target)) File.Delete(target);
                    if (!import) RepositoryGit.Execute(root, "stage", paths: new[] { path });
                    ModelJson.Obj(meta)["applied"] = true; ModelJson.Write(Path.Combine(session, "session.json"), meta);
                    return "Container deletion resolved; registration deactivated and projections retained. Review before committing.";
                }
                resolvedPath = Path.Combine(session, choice == "current" ? "2.tcom" : choice == "incoming" ? "3.tcom" : choice == "ancestor" ? "1.tcom" : "invalid"); ModelPackage.Read(resolvedPath).Validate();
            }
            else
            {
                PreviewSession(session);
                if (ModelJson.Array(ModelJson.Get(ModelJson.Read(Path.Combine(session, "conflicts.json")), "conflicts")).Any()) throw new GitWorkflowException("MERGE_CONFLICT", "Resolve all conflicts before applying.");
                resolvedPath = Path.Combine(session, "resolved.tcom");
            }
            if (registration != null) ModelTracking.PublishCandidate(root, registration, resolvedPath, recorded);
            else File.Copy(resolvedPath, target, true);
            if (!import) RepositoryGit.Run(root, new[] { "add", "--", path });
            if (!import && registration != null) RepositoryGit.Execute(root, "stage", paths: new[] { path });
            ModelJson.Obj(meta)["applied"] = true; ModelJson.Write(Path.Combine(session, "session.json"), meta);
            DocumentDiskState.Accept(target); return import ? "Projection reconciliation imported and validated. Stage explicitly when ready." : "Resolution applied and staged. Review before committing.";
        }
        private static bool SessionMatches(string root, string session)
        {
            var metadata = ModelJson.Read(Path.Combine(session, "session.json"));
            if (Object.Equals(ModelJson.Get(metadata, "aborted"), true) || Object.Equals(ModelJson.Get(metadata, "applied"), true) || !String.Equals(root, ModelJson.Str(metadata, "repository"), StringComparison.OrdinalIgnoreCase)) return false;
            if (ModelJson.Str(metadata, "mode") == "projection-import")
            {
                var input = RepositoryGit.FilePath(root, ModelJson.Str(metadata, "path")); var registration = ModelTracking.Find(root, input);
                return registration != null && File.Exists(input) && ModelJson.FileHash(input) == ModelJson.Str(metadata, "originalWorkingHash") && ModelTracking.DirectoryHash(RepositoryGit.FilePath(root, registration.Folder)) == ModelJson.Str(metadata, "projectionHash");
            }
            for (int stage = 1; stage <= 3; stage++)
            {
                var current = RepositoryGit.Run(root, new[] { "show", ":" + stage + ":" + ModelJson.Str(metadata, "path") }, check: false);
                var saved = Path.Combine(session, stage + ".tcom");
                if (File.Exists(saved) ? current.ExitCode != 0 || ModelJson.FileHash(saved) != ModelJson.Hash(current.Bytes) : current.ExitCode == 0) return false;
            }
            return true;
        }
        public static string AbortSession(string session)
        {
            var file = Path.Combine(session, "session.json"); var metadata = ModelJson.Obj(ModelJson.Read(file)); metadata["aborted"] = true; ModelJson.Write(file, metadata);
            return "Resolution session aborted; input snapshots and decisions retained. The Git operation is unchanged.";
        }
    }
}
