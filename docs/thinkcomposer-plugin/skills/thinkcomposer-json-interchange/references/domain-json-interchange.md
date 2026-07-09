# ThinkComposer Domain JSON Interchange

Domain JSON Interchange is the text-safe Domain payload used by modern `.tdom` packages and by compatibility merge paths. It is also the merge source used when updating an existing `.tcom` composition's embedded domain snapshot.

The same full-state Domain JSON DTO is also used by modern native `.tdom` persistence. A newly saved `.tdom` package writes root `/Domain.json` as the authoritative domain payload. Normal domain Open/Save uses that root JSON first.

The desktop `Domain > Export Domain JSON...` and `Domain > Import/Update Domain JSON...` buttons are deprecated. The forward path for AI-assisted or external edits is to patch the authoritative root `/Domain.json` inside the `.tdom` or `.tcom` package and refresh `/manifest.json` metadata for the changed authoritative part. CLI Domain JSON import/export remains available for validation, migration, and compatibility scenarios; it is not the same code path as normal package load.

## Native Package Persistence

Modern domain packages use this root-level contract:

- `/manifest.json`: package metadata with `format: "ThinkComposer.Package"`, `packageKind: "domain"`, `persistenceFormat: "json"`, `persistenceFormatVersion`, application version, UTC save timestamp, authoritative part hashes, legacy fallback metadata, and optional `gitSync` linkage.
- `/Domain.json`: authoritative `ThinkComposer.DomainJsonInterchange` full-state domain payload.
- `/TemplateComposition.json`: optional authoritative template composition payload when the domain is saved with a template composition.
- `/Domain.bin`: optional legacy binary fallback retained in transitional packages for recovery and backwards compatibility.
- `/Interchange/*` and `/Previews/views/*.png`: optional AI-readable sidecars generated from the same exporters, never authoritative.

When both JSON and binary payloads exist, ThinkComposer opens the root JSON payload first. If root JSON loading fails and a binary fallback is present, the loader logs a JSON persistence warning and falls back to `/Domain.bin` as a recovery path. If root JSON loading fails with no fallback, open fails with the JSON diagnostic.

Opening an older binary-only `.tdom` still works. Saving it again writes the JSON-authoritative package contract above, so normal save acts as the migration step.

The root package manifest schema is maintained at `docs/thinkcomposer-package-manifest.schema.json`. Optional `gitSync` metadata records a generic Git remote, branch, and repo-relative `.tdom` baseline path. When a Composition is saved from a Git-linked Domain, the `.tcom` manifest can carry that Domain source link separately as `embeddedDomainGitSync`; this keeps the Domain update path available even when the Composition itself is linked to a different Git remote or is not linked at all. Domains are pull-only in the first Git sync version; Composition push remains the write workflow. The root domain payload still validates against this interchange schema; there is no separate Domain persistence payload schema in v1.

## Workflow

1. Copy or save the native `.tdom` or `.tcom` package before editing.
2. Open the package as a ZIP/OPC container.
3. Edit root `/Domain.json` manually or with GPT assistance.
4. Refresh the matching `/manifest.json` `authoritativeParts[]` entry, including `sha256` and `bytes`.
5. Reopen the package in ThinkComposer and review the domain content.
6. Save the package normally to let ThinkComposer rewrite JSON, sidecars, previews, and optional binary fallback consistently.

Use `Composition -> Domain -> Update Embedded Domain...` when an existing `.tcom` should pick up safe additions or updates from a newer native `.tdom`. This UI path remains supported and does not require the deprecated Domain JSON import/export buttons.

Every supported file starts with:

```json
{
  "format": "ThinkComposer.DomainJsonInterchange",
  "formatVersion": 1
}
```

The schema is maintained at `docs/thinkcomposer-domain-json-interchange.schema.json`.

## Export Coverage

The first-pass exporter writes deterministic, pretty-printed JSON for text-safe domain content:

- Domain metadata: id, name, techName, summary, description, TechSpec, model revision, version number, version annotation, version sequence, creator/modifier timestamps, default table references, report configuration, and safe view/grid metadata.
- External languages and link-role variants.
- Concept definition, relationship definition, marker, table, and field clusters/categories.
- Marker definitions, excluding binary image payloads.
- Table definitions and field definitions, including data type/category references and TechSpec where available.
- Concept definitions, including cluster, ancestor, shape/composability/versionability metadata, visual symbol format settings, text formats, WPF brush payloads, custom field table references, detail designator summaries, and attached output templates.
- Relationship definitions, including cluster, ancestor, shape/simple/hidden-central metadata, visual connector format settings, text formats, role definitions, allowed/default variants, and attached output templates.
- Output templates as text, including owner definition and external language references.
- A deterministic domain `compatibilitySignature` for elements that affect Composition JSON import compatibility.
- A `relationshipCompatibility` section summarizing each relationship definition's origin/target roles, allowed endpoint concept definition techNames when discoverable, allowed role variants, and simple/directional flags.

The exporter intentionally reports source/export warnings instead of inlining unsupported domain-level binary image resources, custom domain shapes, or unsafe native object graph details. Supported native visual format settings, including text formats and WPF brushes, are represented as JSON values. Repeated missing-category notices are grouped in summaries with examples so successful exports and imports do not look like failures.

## Compatibility Metadata for Composition Patches

Domain JSON exports include compatibility metadata intended for GPTs and strict Composition JSON imports:

- `domain.compatibilitySignature` is a deterministic fingerprint of domain identity/version plus concept definitions, relationship definitions, roles, allowed endpoint definitions, table/field techNames, and external language techNames.
- `relationshipDefinitions[].roleDefinitions[]` includes role techNames/types and `associableIdeaDefinitionTechNames` when the native domain exposes endpoint restrictions.
- `relationshipCompatibility[]` provides a compact matrix for choosing domain-valid relationship definitions and roles in `.tcom` patches.

The signature is a stale-contract detector, not a security boundary. Native ThinkComposer relationship validation remains authoritative. A GPT generating a domain-correct `.tcom` patch should inspect `relationshipCompatibility` before choosing definitions such as `Subject_Verb`, `MUST_be`, or `Targets_Device_Component`, because those definitions may not accept arbitrary concept-definition pairs.

## Patch Operations

Patch-only files use the top-level `operations` array:

```json
{
  "format": "ThinkComposer.DomainJsonInterchange",
  "formatVersion": 1,
  "operations": [
    {
      "op": "update",
      "entity": "domain",
      "set": {
        "summary": "Updated summary",
        "techSpec": "owner: architecture"
      }
    }
  ]
}
```

Supported operation values are `update`, `create`, and `delete`. Supported entity values include `domain`, `externalLanguage`, `linkRoleVariant`, `markerCluster`, `markerDefinition`, `conceptDefinitionCluster`, `relationshipDefinitionCluster`, `tableDefinitionCategory`, `fieldDefinitionCategory`, `tableDefinition`, `fieldDefinition`, `detailDesignator`, `conceptDefinition`, `relationshipDefinition`, `relationshipRole`, and `outputTemplate`.

Matching order is:

1. `id` / GlobalId when supplied.
2. `techName`.
3. `ownerTechName` plus `techName` for child entities such as fields, roles, and templates.
4. Name-only matches are reported as warnings/manual hints rather than applied silently.

Omitted fields preserve existing values. An explicit empty string clears editable text fields such as `summary`, `description`, or `techSpec`.

External language references for output templates use a tolerant fallback after exact matching. Exact id and exact `techName` are preferred. If those do not resolve, the importer normalizes punctuation, whitespace, repeated underscores, dashes, dots, and case, so values such as `Mermaid.js_Flowchart`, `Mermaid JS Flowchart`, and `Mermaid_JS_Flowchart` can resolve to the same external language. The fallback is applied only when it produces exactly one candidate. Ambiguous normalized matches are skipped with a warning listing the conflicting candidates.

## Safe Merge Rules

The importer is intentionally conservative:

- Updates apply only explicit fields.
- Nothing is deleted by omission.
- Delete operations are skipped by default and reported as dangerous changes unless a future destructive-confirmation workflow is added.
- Missing safe objects can be created when required fields are present.
- Existing legacy domain objects are retained by default.
- Field deletion, incompatible field data type changes, table deletion, and relationship role changes that can invalidate compositions are skipped by default.
- Output template bodies are imported as text only. Templates, scripts, TechSpec, and external language text are never executed.
- If an external language or owner definition is missing for a template, the template is skipped unless that dependency exists or is created earlier in the patch.

After apply through a compatibility import path or after native save/reopen through root JSON, Domain base output-template collections are refreshed for the available external languages. Definition-level template slots are prepared automatically when composition generation or `Tools -> Output -> Refresh Output Templates` runs, so users do not need to open every definition's Output-Templates tab after a Domain JSON update.

Output-template create/update logs include owner scope, owner techName, external language match method, source collection, old/new text length, old/new template hash, `extendsBaseTemplate`, role, and target filename/extension hints. Template bodies are not written to the log. Use `Tools -> Output -> Generation Preview` to inspect the effective template and rendered output before generating files.

The preview dialog summarizes planned changes. Detailed parse, planning, apply, skip, conflict, and rollback diagnostics are written to the lower-left application log.

## Report Categories

Domain JSON compatibility dialogs separate message categories:

- `Source warnings` come from the imported/exported JSON itself, such as text-only template export notes, summarized binary/visual content, or grouped missing-category notices. They are useful context, not new failures from the current import.
- `Import warnings` are generated while planning or applying the current import/merge.
- `Skipped` counts operations intentionally left unchanged, with operation-indexed reasons in the log.
- `Dangerous skipped` counts destructive or unsafe changes skipped by design.
- `Notes` are expected reminders, such as saving the containing `.tcom` when a domain is embedded in a composition.
- `Errors` are actual failures that prevented the operation or part of it.

Missing field/table category warnings are usually non-fatal. They mean `categoryTechName` was omitted from JSON because the native domain object has no category assigned. Output templates remain text-only and are never executed.

For small metadata patches, the confirmation dialog also lists the planned field updates. The application log always includes field-level details such as `field=summary`, `field=techSpec`, old/new values, and the match method when available. External language updates log whether the target matched by `id` or `techName`.

## TechSpec

`techSpec` is supported wherever the target domain object exposes ThinkComposer TechSpec or a text-like equivalent. This includes domain metadata, definition metadata, table/field definitions, roles, markers, and output templates where supported by the native model.

TechSpec is treated as plain text. It is not parsed as code and is never executed.

Example:

```json
{
  "op": "update",
  "entity": "conceptDefinition",
  "techName": "Deployment_Component",
  "set": {
    "techSpec": "shape: rectangle\nsemantics: deployable component"
  }
}
```

## Samples

Sample documents are maintained under `samples/`:

- `domain-json-interchange-regression.sample.json` covers a full-state Domain JSON document with tables, fields, roles, definitions, output templates, and TechSpec.
- `domain-json-interchange-patch.sample.json` covers patch-style create/update operations and a skipped dangerous delete example.
- `domain-json-metadata-update.sample.json` covers a metadata-only Domain and external language TechSpec update with no destructive operations. Expected fresh result after adapting the external language techName if needed: `Applied updated: 2`, `Applied skipped: 0`, `Source warnings: 0`, `Import warnings: 0`, `Errors: 0`.
- `domain-json-additive-definition.sample.json` covers additive creation of one concept definition, one table, two fields, and one output template. Expected fresh result: `Applied created: 5`, `Applied skipped: 0`, `Source warnings: 0`, `Import warnings: 0`, `Errors: 0`. Re-importing the same sample should update or leave existing items unchanged rather than duplicating definitions.
- `domain-sync-update.sample.json` is intended for updating an existing composition's embedded domain snapshot.

## Manual Regression

1. Open or create a `.tdom`.
2. Patch root `/Domain.json` or use the CLI compatibility import path to update domain summary and TechSpec.
3. Add a concept definition, table, field, link role variant, marker definition, relationship definition, roles, and output template.
4. Confirm dangerous delete examples are skipped with warnings when using a merge/import path.
5. Save and reopen the `.tdom`.
6. Confirm changes persist and no omitted native domain objects were deleted.

## Re-Export Verification

Use this path after applying a metadata/TechSpec patch such as `samples/domain-json-metadata-update.sample.json`:

1. Open the target `.tdom` or composition-embedded domain.
2. Patch root `/Domain.json` in the package, or run the CLI compatibility import path and import the metadata patch.
3. When using an import path, confirm the preview shows the planned domain and external language updates.
4. Apply the patch and verify diagnostics include field-level lines for `summary` and `techSpec`, including the external language match method.
5. Save the active `.tdom` or save the containing `.tcom` if the domain is embedded in an open composition.
6. Close and reopen the file.
7. Inspect root `/Domain.json` or use `thinkcomposer domain export-json` for a compatibility export.
8. Verify:
   - `domain.techSpec` contains the imported TechSpec text.
   - The target `externalLanguages[]` item contains the imported `summary`.
   - The target `externalLanguages[]` item contains the imported `techSpec`.
   - No destructive operations were applied by omission.

## Additive Sample Verification

Use `samples/domain-json-additive-definition.sample.json` against a copied test composition/domain:

1. Patch root `/Domain.json` in the copied package, or run the CLI compatibility import path.
2. When using an import path, confirm preview shows five planned creates and no planned skips, import warnings, or errors.
3. Apply and confirm the final summary reports five created items:
   - one `tableDefinition`
   - two `fieldDefinition` objects under `Interchange_Metadata`
   - one `conceptDefinition`
   - one `outputTemplate`
4. Verify the log shows field parent/data type resolution:
   - parent table `Interchange_Metadata`
   - data type `Text`
5. Verify the output template resolves:
   - owner scope `conceptDefinition`
   - owner `Interchange_Component`
   - external language `Text`
6. Save and reopen the `.tdom` or containing `.tcom`.
7. Inspect root `/Domain.json` or export with `thinkcomposer domain export-json` and confirm the table, fields, concept definition, custom fields table reference, and output template are present.

If the output template is skipped, check the log for an unresolved owner scope, owner definition, or external language. The importer accepts `ownerScope` and the alias `ownerKind`; supported scopes are `domainConcept`, `domainRelationship`, `conceptDefinition`, and `relationshipDefinition`.

For embedded-domain updates from older `.tdom` sources, output template language references may contain punctuation variants. For example, `Mermaid.js_Flowchart` should resolve to `Mermaid_JS_Flowchart` when that is the only normalized match in the target domain. If two external languages normalize to the same key, the importer skips the template instead of guessing.

For generation behavior after import, see `docs/output-template-generation.md`. Composition generation prepares imported concept/relationship definition templates before rendering and logs missing languages, missing template text, and missing subtemplates as preparation diagnostics.

## Limits

This pass imports supported native visual format settings, text formats, WPF brush payloads, and report configuration needed by JSON-authoritative package persistence, but it still does not import custom domain shapes, domain-level binary image resources, arbitrary rich visual style object graphs, full destructive migrations, or executable behavior. JSON persistence does not reconstruct metadata-only binary domain payloads. `.tdom` JSON updates are not live sync. Use `docs/domain-sync.md` for the explicit native `.tdom` to `.tcom` embedded-domain update workflow.
