---
name: thinkcomposer-json-interchange
description: Create, edit, repair, route, and validate ThinkComposer snapshot JSON and standalone operation patches. Use for .tcom/.tdom Composition JSON v1/v2, Domain JSON, multi-point routePoints, safe CLI patch application, visual placement, layout/routing diagnostics, schema validation, embedded-domain updates, Git sync, reports, or import troubleshooting.
---

# ThinkComposer JSON Interchange

## Purpose

Help users create, edit, repair, route, and validate ThinkComposer JSON payloads. Treat authoritative Composition JSON as canonical snapshot state. For GPT-authored Composition changes, write a standalone operations patch and materialize it through the CLI. Directly edit root `/Composition.json` only for exact snapshot recovery or deliberate expert maintenance.

There are two current formats:

- Composition JSON: `format: "ThinkComposer.JsonInterchange"` for root `/Composition.json` in `.tcom` and compatibility composition imports/exports.
- Domain JSON: `format: "ThinkComposer.DomainJsonInterchange"` for root `/Domain.json` in `.tdom` or `.tcom` and compatibility domain imports/exports.

Modern native `.tcom` and `.tdom` packages use root JSON payloads as authoritative persistence. Composition JSON v2 stores ordered connector `routePoints`; the upgraded application also reads v1 `intermediatePosition`. CLI Composition import is the safe one-shot edit path because normal save consumes directives into snapshot state.

Saved native packages contain root `/manifest.json`, `/Composition.json`, `/Domain.json`, and optional `/TemplateComposition.json` authoritative payloads, plus AI-readable sidecar snapshots under `/Interchange/` and capped PNG previews under `/Previews/views/`. Current saves are JSON-only and emit `legacyBinaryFallback.present:false`; `/Composition.bin` and `/Domain.bin` can exist only in older binary-only/transitional packages and are never edit targets. `/manifest.json` may also include optional package-level `gitSync` metadata with a generic Git remote, branch, and repo-relative baseline package paths; `.tcom` manifests may additionally include `embeddedDomainGitSync` for the embedded Domain's source `.tdom` link. When a user provides a native package, inspect authoritative root JSON first. Apply Composition changes through a standalone operations patch; patch Domain root JSON only for deliberate Domain state changes. Treat `/Interchange/` as synchronized context snapshots. Sidecar manifest v2 preview hashes/profile/disposition support verified PNG reuse but remain non-authoritative.

When inspecting or deliberately repairing a native package:

- Keep `.tcom` `/Composition.json` and optional `/TemplateComposition.json` snapshot-only. Do not embed generated `operations`, `importOptions`, or `visualStrategy`; apply those from a standalone patch.
- Patch root `/Domain.json` directly only when the task requires Domain state changes.
- Refresh the corresponding `/manifest.json` `authoritativeParts[]` metadata, especially `sha256` and `bytes`.
- Preserve or intentionally update `/manifest.json` `gitSync` and `embeddedDomainGitSync` when present. Do not store Git credentials, tokens, last-sync commits, or package hashes in the package; sync state is machine-local.
- Do not edit `/Composition.bin` or `/Domain.bin` when an older package contains them. A normal save migrates the package to JSON-only persistence.
- Do not treat `/Interchange/*` or `/Previews/*` as authoritative. They may be stale until ThinkComposer saves the package again.
- Validate with `package inspect` and the relevant `validate-json-persistence` CLI command after the package is patched.

## CLI Automation

Use the CLI for saved-package validation and headless operations that ThinkComposer exposes safely:

```cmd
thinkcomposer package inspect --input <file.tcom|file.tdom>
thinkcomposer composition import-json --input <file.tcom> --json <patch.json> --output <file.tcom> --preview-only
thinkcomposer composition validate-routing --input <file.tcom> --output-dir <dir> [--layout route|spider|hierarchy|flowchart|system]
thinkcomposer composition validate-json-persistence --input <file.tcom> --output-dir <dir>
thinkcomposer domain validate-json-persistence --input <file.tdom> --output-dir <dir>
thinkcomposer git status --input <file.tcom|file.tdom>
thinkcomposer git pull --input <file.tcom|file.tdom> --output <file> [--in-place]
thinkcomposer git push --input <file.tcom> --message <message>
thinkcomposer report pdf --input <file.tcom> --output <file.pdf|file.xps>
thinkcomposer output generate --input <file.tcom> --output-dir <dir> --language <language-tech-name>
thinkcomposer performance prepare-json-persistence-corpus --source-root <repo> --output-dir <dir> [--mode <development|certification>] [--real-package <sanitized-slow-file>]...
thinkcomposer performance benchmark-json-persistence --corpus <dir>\corpus.json --output <report.json> [--baseline <report.json>] [--minimum-speedup 2.0] [--allow-legacy-baseline-output]
```

Rules:

- Use `package inspect` before and after direct package edits when practical.
- Use `validate-json-persistence` after changing authoritative root JSON.
- Use `git status/pull/push` only when `/manifest.json` has a package `gitSync` link or `.tcom` `embeddedDomainGitSync` for the embedded Domain source. Composition push is supported; Domain push is not supported in v1.
- Use `report pdf` for standard PDF/XPS reports from a saved `.tcom`.
- Use `output generate` to render external-language output templates from a saved `.tcom`. If the requested `--language` techName is unclear, inspect root `/Domain.json` or run a compatibility `domain export-json`.
- Use the `performance` commands only for developer benchmarking. They validate a hash-locked JSON-only corpus and run load/first-save/steady-save samples in fresh processes; a baseline comparison requires the same machine, corpus, and iteration count. `--allow-legacy-baseline-output` may be used only while recording a pre-optimization JSON-authoritative baseline that retains the exact matching legacy binary fallback. Never combine it with `--baseline`; candidate runs remain strict JSON-only.
- Use `composition import-json` as the default generated Composition edit path: preview first, apply second, then validate the canonical output. Use `domain import-json` for standalone Domain operations patches when appropriate.
- When MCP is available, `thinkcomposer_apply_patch` wraps that preview/apply sequence and follows a successful Composition apply with route-health validation and an exported view image. Inspect both results before accepting the package.

## Source-of-truth order

Use the most current accessible references in this order:

1. User-provided schema, docs, exports, samples, or instructions in the current conversation.
2. Bundled references shipped with this skill:
   - `references/thinkcomposer-json-interchange.schema.json`
   - `references/thinkcomposer-domain-json-interchange.schema.json`
   - `references/thinkcomposer-package-manifest.schema.json`
   - `references/thinkcomposer-container-manifest.schema.json`
   - `references/cli.md`
   - `references/json-interchange.md`
   - `references/domain-json-interchange.md`
   - `references/domain-sync.md`
   - `references/container-readable-snapshots.md`
   - `references/output-template-generation.md`
   - `references/dcom-interchange-release-notes.md`
   - `references/appearance-layout-tools.md`
   - `references/layout-services.md`
   - `references/ux-improvements-validation-checklist.md`
   - `references/*.sample.json`
   - `references/test-findings.md`
3. Optionally compare the latest branch references from `tbm0115/ThinkComposer`, currently the active Dcom/Composition JSON import hardening branch, when accessible and the user has not supplied newer files. Never replace a bundled v2 schema with an older fetched schema.

If references conflict, follow the schema selected for the current task, mention the conflict, and validate against that schema.

## Additional application context

When this skill is used from the ThinkComposer source repository, the Markdown user manual under `docs/user-manual/` is optional local context for application terminology, existing UI workflows, domain/composition concepts, concept and relationship definitions, tables/details/custom fields, markers, complements such as Group Regions, output templates, reports/exports, domain editing terminology, and general user-facing behavior. A packaged skill does not assume that repository-only path exists; use the bundled references above.

The manual describes broad ThinkComposer application capabilities. Do not assume a manual feature is supported by JSON interchange unless the current JSON schemas, docs, or samples also document that support. Prefer Markdown docs when available; the PDF manual may later be migrated into Markdown under `docs/` and regenerated with Pandoc.

## Choose the right format

Use Composition JSON when the user wants to edit an existing composition:

- Update composition/concept/relationship names, summaries, descriptions, or TechSpec.
- Create concepts or relationships.
- Place visuals in views.
- Auto-place, auto-fit, or auto-route imported composition items.

Use Domain JSON when the user wants to edit a domain:

- Update domain metadata or TechSpec.
- Add/update concept definitions, relationship definitions, relationship roles, tables, fields, markers, variants, external languages, or output templates.
- Patch root `/Domain.json` in a `.tdom` or the embedded root `/Domain.json` in a `.tcom`.
- Prepare a native `.tdom` source for `Composition -> Domain -> Update Embedded Domain...`.

Do not mix the full Domain JSON schema into a Composition JSON document. Use the embedded-domain update command when a `.tcom` should pick up safe changes from a `.tdom`; patch the `.tcom` root `/Domain.json` directly when the user wants direct package editing.

## Composition patch defaults

Use this top-level shape for most `.tcom` patches when the selected schema supports these fields:

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 2,
  "importOptions": {
    "autoPlaceNewItems": true,
    "layoutMode": "gridNearViewport",
    "preventSelfRecursiveCompositeViews": true,
    "repairRecursiveVisuals": true,
    "useActiveCompositionAsContainer": false,
    "autoFitPlacedConcepts": true,
    "autoRoutePlacedLinks": true
  },
  "operations": []
}
```

Rules:

- Prefer patch-style `operations` for creating content. Keep the patch standalone; never splice it into root `/Composition.json`.
- Emit Composition `formatVersion: 2` and Domain `formatVersion: 2`; accept version 1 documents only as migration inputs.
- Use `update` for text/TechSpec edits, `create` for new model items, `place` for diagram visibility, and `delete` only when explicitly requested.
- Match by stable `id` when available, otherwise by top-level `techName`.
- Put editable fields inside `set`.
- `set.description` is first-class plain text for composition, concepts, relationships, and views. ThinkComposer converts this text to/from native rich-text storage internally. Omission preserves existing Description and explicit empty string clears it.
- `set.techSpec` is plain text; omission preserves existing TechSpec and explicit empty string clears it.
- Relationship creates must include resolvable origin/target links. Linkless relationships are skipped by the importer.
- Relationship links may appear at operation top level or inside `set`; top-level values are preferred.
- Preserve link-level metadata when present: `roleVariantTechName`, `roleVariantName`, `descriptorName`, `descriptorTechName`, and `descriptorSummary`. These belong to the link/connector endpoint between a relationship and a concept, not to the relationship object itself.
- Before generating relationships for a custom domain, inspect the Domain JSON relationship definitions when available. Do not assume a relationship definition can connect arbitrary concepts.
- When a Domain JSON export is available, inspect `domain.compatibilitySignature`, `relationshipDefinitions[].roleDefinitions[]`, `associableIdeaDefinitionTechNames`, and `relationshipCompatibility[]` before choosing relationship definitions and roles.
- For strict domain-correct imports, include a `requires.domain` block with at least domain `techName`, and include `id`, `versionSequence`, and `compatibilitySignature` when available from the user's export.
- Use `importOptions.domainCompatibilityPolicy`, `compositionVersionPolicy`, `strictRelationshipCompatibility`, and `abortOnRelationshipCompatibilityFailure` when the user wants stale/mismatched JSON blocked before apply.
- Domain-specific relationship definitions such as `Subject_Verb`, `MUST_be`, `Targets_Device_Component`, or similar may have strict endpoint concept-definition constraints. Endpoint concepts must use definitions valid for the selected relationship roles.
- If the correct relationship definition is uncertain, use a generic relationship definition such as `Relationship` or `Reference` only when the user wants a draft graph, include an explicit `relationshipDefinitionFallbackTechName` for draft imports, or ask the user which definition to use. Do not use fallback silently.
- Set `strictDefinition: true` on an operation when preserving the requested relationship definition is more important than importing a draft graph edge.
- Use operation-level `autoFit` and `autoRoute` to override top-level import options.
- Use `details` only when the target Domain exposes matching native detail designators. Preserve the exported declaration `id` and put it in the Composition value's `details[].designatorId` (with the matching `designatorTechName`); do not call this field `definitionId` or synthesize an instance-only Table designator. A version 2 Domain snapshot carries declarations under `conceptDefinitions[].detailDesignators` or `relationshipDefinitions[].detailDesignators`.
- Use `detailFallbackMode: "appendToTechSpec"` or `"appendToDescription"` only when preserving generated detail text matters and native detail designators may be missing. Prefer first-class `summary`, `description`, or `techSpec` for important generated text unless the target domain clearly supports matching details.
- Do not place a concept inside its own composite view.
- For normal imports, resolve containers by exact `containerId` or `containerTechName`.
- For root-level GPT patches that should import into whatever composition is active, set `importOptions.useActiveCompositionAsContainer: true` and use the canonical sentinel `containerTechName: "Active_Composition_Root"`.
- Accepted active-root variants include `ACTIVE_COMPOSITION_ROOT`, `activeCompositionRoot`, `active-composition-root`, `active_composition_root`, `__ACTIVE_COMPOSITION_ROOT__`, `Current_Composition`, `CurrentComposition`, `Active_Composition`, `Composition_Root`, and `Root_Composition`; still prefer `Active_Composition_Root` in generated JSON.
- Active-root fallback only applies to safe root-level create/place behavior, not destructive update/delete operations or precise nested-container imports.
- Prefer explicit `viewTechName` when the target view is known. If a root-level create/place omits view fields, the importer may use the active view or composition root view; accepted view sentinels include `Active_View`, `Main_View`, and `Active_Composition_Root_View`.
- If a composition patch depends on custom Domain definitions, update or inspect root `/Domain.json` or refresh the embedded domain from the intended `.tdom` first. Do not assume `definitionTechName` values are present just because they appear in a generated composition patch.
- If an import log contains `BEGIN THINKCOMPOSER RELATIONSHIP COMPATIBILITY REPORT`, use that report to regenerate relationship definitions/endpoints/roles rather than asking the user to debug generic skipped counts.

## Full-state-style composition documents

Full-state `ideas[]`, `relationships[]`, and `views[]` are normally interpreted as updates/merge data for objects that already exist in the active `.tcom`. Do not generate top-level full-state arrays for blank-composition creation unless the user explicitly asks for that shape.

For blank-composition generation, use one of these approaches:

- Preferred: patch operations with `op: "create"` for concepts/relationships and optional `op: "place"` for visuals.
- Explicit full-state-create mode: set `importOptions.treatMissingFullStateItemsAsCreates: true`.
- Per-item full-state create: set `isNew: true` on each missing top-level concept/relationship.

When using full-state-create mode:

- Still set `importOptions.useActiveCompositionAsContainer: true` and `containerTechName: "Active_Composition_Root"` for root-level items.
- Include `definitionTechName`, `name` or `techName`, and valid relationship endpoints.
- Put visual placement in `views[].visuals[]` using `ideaTechName` and optional `x`, `y`, `width`, `height`.
- Expect native relationship compatibility validation to run after concepts are planned/created.
- If full-state missing IDs are skipped, enable the option or regenerate as patch operations.

## Large composition imports

For large generated models, do not hand-place, size, auto-fit, or route every concept and relationship unless the user explicitly asks for an exact full diagram. Use top-level `visualStrategy` to describe visual intent.

Recommended modes:

- `modelOnly`: best for output-template generation, semantic model import, details, TechSpec, and later manual layout. Suppresses visual placement and defers auto-fit, auto-route, and view refresh.
- `overviewAndModel`: create the full semantic model but only a capped overview diagram. Use when the user needs a small navigable map of a much larger model.
- `optimizedFullVisual`: use when the user wants full visual materialization but can defer expensive auto-fit, route, and refresh work.
- `exactFullVisual`: use only for small diagrams or when the user explicitly asks for exact placement/routing.

Use this pattern for large or uncertain imports:

```json
{
  "visualStrategy": {
    "mode": "overviewAndModel",
    "largeModelThresholds": {
      "concepts": 300,
      "relationships": 300,
      "visuals": 600
    },
    "fullModelVisuals": false,
    "overviewView": true,
    "overviewViewTechName": "Overview_View",
    "maxOverviewConcepts": 150,
    "maxOverviewRelationships": 200,
    "groupBy": [
      "Contains_Components",
      "Contains_Data_Item"
    ],
    "relationshipVisualPlacement": "endpointCorridor",
    "deferRouting": true,
    "deferAutoFit": true,
    "deferViewRefresh": true
  }
}
```

Rules:

- For output-template generation workflows, prefer `modelOnly`.
- For large inventories, prefer `overviewAndModel`.
- Do not generate hundreds/thousands of `views[].visuals[]`, exact `x/y`, exact `width/height`, or per-relationship routes by default.
- `overviewViewTechName` is a preference for an existing view. Current JSON import falls back to the active/root view; it does not create arbitrary new views yet.
- `groupBy` records overview intent using relationship-definition techNames. Current import diagnostics preserve the intent; full grouped overview layout is not a full graph optimizer.
- Strategy deferrals are expected notes, not failures. The user can run manual Appearance commands after import when ready.

## Relationship center placement and routing

ThinkComposer relationships may have visible central symbols. Generated JSON should not place these relationship bubbles in a global label row, because connectors route through the visible central symbol and can create long sweeping lines.

Rules:

- For generated flow, architecture, and system diagrams, place concepts deliberately and let the importer place relationship centers.
- Omit generated Relationship `x/y`, `connectors`, `intermediatePosition`, and `routePoints`. GPTs specify routing intent; the application owns generated geometry.
- Prefer `importOptions.relationshipVisualPlacementMode: "endpointCorridor"` for generated diagrams, or `visualStrategy.relationshipVisualPlacement: "endpointCorridor"` when using large-import visual strategy.
- Set operation-level `visual.relationshipCenterPlacement:"endpointCorridor"` and `autoRoute:true` for each created, placed, or moved Relationship.
- `auto` is the default and preserves relationship centers that are already near their endpoint corridor while recomputing suspicious far-away centers.
- Use `explicit` only when the relationship visual coordinates are hand-curated and intentionally close to the relationship endpoints.
- For medium/large diagrams, also consider `autoRoutePlacedLinks: false` or `visualStrategy.deferRouting: true`; users can run Edit -> Appearance -> Route Links with Obstacle Avoidance after import.
- If full-state JSON includes relationship visuals, omit exact relationship visual `x/y` unless exact placement is required, or make sure every relationship center is near the midpoint/corridor between its source and target concepts.

Composition JSON v2 exact snapshots may contain `connectors[].routePoints`, ordered from connector origin to target and excluding endpoint anchors:

- Zero points means straight; maximum 32 points.
- Omitted `routePoints` in a patch leaves a route unchanged; `[]` clears it.
- `routePoints` wins over deprecated `intermediatePosition` when both appear.
- Do not generate route points unless the user explicitly requests exact hand-authored geometry. Prefer `autoRoute:true`.
- Validate finite coordinates, local endpoint corridors, detour length, stable connector identity, and save/reopen parity.

## Shortcuts and duplicate-looking concepts

Do not assume two concepts should be merged only because they share a `techName`. In ThinkComposer, semantic identity is the native idea id. A Shortcut is a visual representation of an existing idea; it is the right JSON/UX primitive when the same semantic concept should appear in more than one place.

Rules:

- Use one semantic concept plus shortcut visuals when the user says the item is the same concept shown in multiple contexts.
- Use separate concepts, optionally with the same or similar `techName`, when summaries/descriptions/types intentionally differ by context.
- For patch-style placement, emit `visual.isShortcut:true` on an `op:"place"` operation targeting the existing concept.
- For full-state visual data, preserve exported `views[].visuals[].isShortcut:true`.
- Include `representationId` when updating a specific visual representation, especially when one concept has both a primary visual and a shortcut visual in the same view.
- Users can create shortcuts from existing duplicates with **Replace with Shortcut...** and navigate from a shortcut back to its primary/original visual with **Go to Original**.
- Do not generate merge/delete operations to clean duplicates unless the user explicitly asks for semantic consolidation.

Example shortcut placement:

```json
{
  "op": "place",
  "entity": "concept",
  "techName": "Shared_Service",
  "viewTechName": "Main_View",
  "x": 460,
  "y": 140,
  "visual": {
    "isShortcut": true
  }
}
```

## Intent-agnostic visual/layout primitives

ThinkComposer import code is intentionally source-neutral. This Skill, not the application importer, is responsible for translating source intent into ThinkComposer primitives.

Do not rely on ThinkComposer to guess that a source-format group, device, subsystem, membership edge, or relationship name implies special behavior. If source intent matters, emit explicit JSON metadata:

- If a source grouping should become a Group Region, emit top-level `groups[]` with `createGroupRegion:true` and member ids/techNames.
- If a relationship is membership/grouping and should not shape the diagram, emit `layoutRole:"Membership"` plus `visual.display:"hidden"` and `includeInArrangement:false`.
- If a large model should be semantic-only, emit `visualStrategy.mode:"modelOnly"` and suppress visual work.
- If a relationship label/center should be recomputed near its endpoints, emit `visual.relationshipCenterPlacement:"endpointCorridor"` or import-wide `relationshipVisualPlacementMode:"endpointCorridor"`.
- If a concept should be an overview placeholder, emit `visual.role:"Summary"` or `visual.role:"GroupHeader"` explicitly.

Source-neutral examples:

Model-only semantic import:

```json
{
  "visualStrategy": {
    "mode": "modelOnly"
  },
  "importOptions": {
    "autoFitPlacedConcepts": false,
    "autoRoutePlacedLinks": false
  }
}
```

Group region with hidden membership edge:

```json
{
  "groups": [
    {
      "name": "Subsystem A",
      "techName": "Subsystem_A_Group",
      "memberTechNames": ["A1", "A2", "A3"],
      "createGroupRegion": true
    }
  ],
  "operations": [
    {
      "op": "create",
      "entity": "relationship",
      "layoutRole": "Membership",
      "visual": {
        "display": "hidden",
        "includeInArrangement": false,
        "includeInRouting": false
      }
    }
  ]
}
```

Visible dependency edge:

```json
{
  "op": "create",
  "entity": "relationship",
  "layoutRole": "Dependency",
  "visual": {
    "display": "visible",
    "relationshipCenterPlacement": "endpointCorridor",
    "includeInArrangement": true,
    "includeInRouting": true
  }
}
```

Summary/overview concept:

```json
{
  "op": "create",
  "entity": "concept",
  "set": {
    "name": "Related Items (112)",
    "techName": "Related_Items_Summary"
  },
  "visual": {
    "role": "Summary",
    "includeInOverview": true,
    "includeInFullView": false
  }
}
```

## Domain patch defaults

Use this top-level shape for most `.tdom` or embedded-domain update patches:

```json
{
  "format": "ThinkComposer.DomainJsonInterchange",
  "formatVersion": 2,
  "operations": []
}
```

Rules:

- Update only explicit fields. Omission must not clear or delete native data.
- `delete` operations are dangerous and skipped by default in the current importer.
- Domain create/update targets include `externalLanguage`, `linkRoleVariant`, clusters/categories, `markerDefinition`, `tableDefinition`, `fieldDefinition`, `detailDesignator`, `conceptDefinition`, `relationshipDefinition`, `relationshipRole`, and `outputTemplate`.
- Child entities should include `ownerTechName` when needed, especially fields, roles, and templates.
- A `detailDesignator` operation must identify its owning definition with `ownerId`/`ownerTechName` and preferably `ownerScope`. Put `kind` in `set`; for a Table Detail also provide `tableDefinitionId` and/or `tableDefinitionTechName` plus `tableDefinitionIsOwned`. After apply, re-export the Domain and use the emitted declaration `id` as `details[].designatorId` in later Composition operations.
- Field data type changes, table deletion, field deletion, and incompatible relationship role changes are skipped by default.
- Output templates are imported as text only and never executed.
- TechSpec is imported as text only and never executed.

For output templates, preserve these fields exactly when present:

- `externalLanguageTechName`
- `ownerScope`
- `ownerTechName`
- `templateText`
- `extendsBaseTemplate`
- `set.targetFileName`
- `set.targetFileExtension`
- `set.templateRole` or explicit `%%:TemplateRole=...` directive

If the user's goal is composition output generation, ensure the Domain JSON contains the required concept-definition and relationship-definition output templates for the selected external language, or usable Domain base templates. ThinkComposer prepares imported output templates during generation, so the user should not need to open every definition's Output-Templates tab after import/update.

Output templates in JSON are text-only and are not executed during import/export, embedded-domain update, refresh, or validation. When generating Domain JSON for templates, prefer explicit role directives:

- `%%:TemplateRole=DocumentRoot` for final deliverables.
- `%%:TemplateRole=SubTemplate` plus `%%:SubTemplate=Name` for reusable injected fragments.
- `%%:TemplateRole=Fragment` for fragments that should not emit standalone files.
- `%%:TemplateRole=Diagnostic` for optional debug text.
- `%%:TemplateRole=NotApplicable` or `%%:TemplateRole=Disabled` for templates that should not generate deliverables.

Do not assume a template is active just because it exists in Domain JSON. Ask the user for `Generation Preview` or `Generate Files...` log output when debugging generation. The current build reports target item, selected language, owner scope, owner techName, source collection, template hash, inferred role, effective template, subtemplate registry entries, rendered output, and XML/JSON validation status.

For XML/JSON generation, prefer safe filters/directives in template text:

- `EscapeXmlAttribute`, `EscapeXmlText`, `DefaultIfEmpty`, `Coalesce`, `NormalizeTechName`, `DetailValue`, and `JsonString`.
- `%%:outputPostProcess.trimLeadingWhitespace=true`
- `%%:outputValidation=XmlWellFormed`

Use MTConnect `Devices_Response_Document` only as a regression example; do not hardcode MTConnect-specific logic into generated JSON.

## Embedded domain update

`Composition -> Domain -> Update Embedded Domain...` updates the active `.tcom` composition's embedded domain snapshot from a native `.tdom`. The equivalent CLI path is `thinkcomposer domain update-embedded --input <file.tcom> --domain <file.tdom> --output <updated-file.tcom>`. Some compatibility paths can still consume Domain JSON files, but the preferred AI/editing workflow is to patch root `/Domain.json` directly.

This is explicit safe merge, not live sync:

- Adds missing safe definitions, tables, fields, roles, markers, variants, external languages, and templates.
- Updates text metadata and TechSpec.
- Retains legacy embedded-domain objects by default.
- Does not delete by omission or replace the embedded Domain object wholesale.
- Preserves existing composition ideas and relationship links.
- Keeps output template bodies text-only; they are not executed during compatibility import/export or embedded-domain update.

## Report categories

ThinkComposer dialogs classify messages so successful operations do not look failed:

- `Source warnings` are preserved notes from an imported/exported JSON or native-domain export, such as fixture notes, grouped missing-category notices, text-only template export notes, or summarized binary/visual content.
- `Import warnings` are generated while planning or applying the current operation.
- `Skipped` means an operation was intentionally not applied; use the log for the indexed reason.
- `Dangerous skipped` means a potentially destructive change was refused by design.
- `Notes` are expected reminders or limitations.
- `Errors` are actual failures.

When helping with import results, do not treat nonzero source warnings as failures when skipped, dangerous skipped, import warnings, and errors are zero.

## Layout and visual import context

Composition JSON import supports:

- first-class `summary`, `description`, and `techSpec` text on composition/concepts/relationships/views where native objects expose those fields
- safe `details` import for known-field Text details and matching native table/resource detail designators
- `importOptions.autoPlaceNewItems`
- `importOptions.layoutMode`
- `importOptions.preventSelfRecursiveCompositeViews`
- `importOptions.repairRecursiveVisuals`
- `importOptions.autoFitPlacedConcepts`
- `importOptions.autoRoutePlacedLinks`
- `importOptions.useActiveCompositionAsContainer`
- `importOptions.treatMissingFullStateItemsAsCreates`
- `importOptions.relationshipDefinitionFallbackTechName`
- `importOptions.detailFallbackMode`
- `importOptions.domainCompatibilityPolicy`
- `importOptions.compositionVersionPolicy`
- `importOptions.strictRelationshipCompatibility`
- `importOptions.abortOnRelationshipCompatibilityFailure`
- `importOptions.strictDetailsCompatibility`
- `importOptions.abortOnDetailCompatibilityFailure`
- `importOptions.relationshipVisualPlacementMode`
- `importOptions.recomputeSuspiciousRelationshipVisuals`
- `importOptions.maxRelationshipCenterDisplacement`
- `importOptions.relationshipCenterObstaclePadding`
- `importOptions.relationshipCenterOverlapPadding`
- top-level `visualStrategy`
- operation-level `autoFit`
- operation-level `autoRoute`
- operation-level or `visual.isShortcut`
- operation-level `fallbackDefinitionTechName`
- operation-level `strictDefinition`

Manual Appearance commands in the current build:

- `Edit -> Appearance -> Fit Concept Width to Text`
- `Edit -> Appearance -> Route Links with Obstacle Avoidance`
- `Edit -> Appearance -> Arrange as Spider Map`
- `Edit -> Appearance -> Arrange as Hierarchy Map`
- `Edit -> Appearance -> Arrange as Flowchart`
- `Edit -> Appearance -> Arrange as System Map`

These are deterministic layout helpers backed by the shared multi-bend obstacle router, not global crossing-free graph optimizers. Spider, Hierarchy, Flowchart, and System Map remain manual layout commands; JSON import integrates endpoint-corridor placement and affected-link routing.

## Backlog / not implemented yet

- Custom domain shape import.
- Full graph crossing minimization.
- Live automatic `.tdom` synchronization.
- Destructive domain cleanup/migrations.
- Full rich/binary content export/import.
- Automatic JSON import modes for Spider, Hierarchy, Flowchart, or System Map layouts.
- Full embedded-domain/output-template diff UI.
- Full DotLiquid static analysis, Mermaid validation, and injected-template scope isolation.

## Validation

Use `scripts/validate_json.py` when validating JSON files:

```bash
python scripts/validate_json.py path/to/document.json
```

The packaged schema is authoritative by default. Fetch an upstream schema only when the user explicitly asks to compare against it:

```bash
python scripts/validate_json.py path/to/document.json --fetch-latest
```

Pass a schema explicitly when the user supplied one:

```bash
python scripts/validate_json.py path/to/document.json --schema path/to/schema.json --no-fetch
```

If the validator script is unavailable, parse JSON with UTF-8 BOM tolerance and manually check the document against the selected schema. Report exactly which schema or reference was used.

## Response conventions

- For import-ready output, provide strict JSON with no comments or trailing commas.
- Preserve user-provided ids and tech names exactly.
- Generate stable, readable, identifier-like `techName` values.
- Generate UUIDs only when stable ids help the workflow or schema validation.
- Preserve meaningful line breaks in summaries, descriptions, TechSpec, and template text.
- Never include executable behavior, binary payloads, images, or unsupported native object graphs in JSON.
- When troubleshooting imports, direct users to the lower-left ThinkComposer log for parse, preview, apply, skip, rollback, visual placement, auto-fit, auto-route, and domain merge diagnostics.
