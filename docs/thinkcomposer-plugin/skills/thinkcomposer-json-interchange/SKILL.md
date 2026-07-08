---
name: thinkcomposer-json-interchange
description: create, edit, repair, and validate ThinkComposer authoritative package JSON and compatibility interchange documents for a custom ThinkComposer build. Use this skill for .tcom/.tdom root JSON patches, Domain JSON patches, TechSpec updates, visual placement, layout-aware import options, embedded-domain update planning, schema validation, or import troubleshooting.
---

# ThinkComposer JSON Interchange

## Purpose

Help users create, edit, repair, and validate ThinkComposer JSON payloads for the custom `tbm0115/ThinkComposer` build. Prefer direct edits to authoritative package root JSON for native `.tcom` and `.tdom` files; use patch-operation JSON for GPT-authored compatibility imports unless the user explicitly asks for a full-state merge document.

There are two current formats:

- Composition JSON: `format: "ThinkComposer.JsonInterchange"` for root `/Composition.json` in `.tcom` and compatibility composition imports/exports.
- Domain JSON: `format: "ThinkComposer.DomainJsonInterchange"` for root `/Domain.json` in `.tdom` or `.tcom` and compatibility domain imports/exports.

Modern native `.tcom` and `.tdom` packages use root JSON payloads as authoritative persistence. Desktop Composition/Domain JSON import/export controls are deprecated; CLI import/export remains a compatibility, migration, and validation path.

Saved native packages may contain root `/manifest.json`, `/Composition.json`, `/Domain.json`, and optional `/TemplateComposition.json` authoritative payloads, plus AI-readable sidecar snapshots under `/Interchange/` and capped PNG previews under `/Previews/views/`. When a user provides a native `.tcom` or `.tdom`, inspect and patch root JSON first when present; treat `/Interchange/` as synchronized context snapshots. `/Composition.bin` and `/Domain.bin` are legacy fallback/recovery payloads.

When directly editing a native package:

- Patch only authoritative root JSON parts: `.tcom` `/Composition.json` and `/Domain.json`; `.tdom` `/Domain.json` and optional `/TemplateComposition.json`.
- Refresh the corresponding `/manifest.json` `authoritativeParts[]` metadata, especially `sha256` and `bytes`.
- Do not edit `/Composition.bin` or `/Domain.bin`; they are optional legacy fallback parts.
- Do not treat `/Interchange/*` or `/Previews/*` as authoritative. They may be stale until ThinkComposer saves the package again.
- Validate with `package inspect` and the relevant `validate-json-persistence` CLI command after the package is patched.

## Source-of-truth order

Use the most current accessible references in this order:

1. User-provided schema, docs, exports, samples, or instructions in the current conversation.
2. Latest branch references from `tbm0115/ThinkComposer`, currently the active Dcom/Composition JSON import hardening branch, when accessible and the user has not supplied newer files.
3. Bundled fallback references:
   - `references/thinkcomposer-json-interchange.schema.json`
   - `references/thinkcomposer-domain-json-interchange.schema.json`
   - `references/thinkcomposer-package-manifest.schema.json`
   - `references/thinkcomposer-container-manifest.schema.json`
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

If references conflict, follow the schema selected for the current task, mention the conflict, and validate against that schema.

## Additional application context

The repository includes the ThinkComposer user manual at:

`../../Installer/Deploy/InstrumindThinkComposer_Manual.pdf`

Use it as local context for application terminology, existing UI workflows, domain/composition concepts, concept and relationship definitions, tables/details/custom fields, markers, complements such as Group Regions, output templates, reports/exports, domain editing terminology, and general user-facing behavior.

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
  "formatVersion": 1,
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

- Prefer patch-style `operations` for creating content. They make intent, ordering, and diagnostics clearer than full-state arrays.
- Preserve `format` and `formatVersion` exactly.
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
- Use `details` only when the target domain exposes matching native detail designators, or when using a known-field Text detail with `targetPropertyTechName` such as `Description`, `Summary`, or `TechSpec`.
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
- Prefer `importOptions.relationshipVisualPlacementMode: "endpointCorridor"` for generated diagrams, or `visualStrategy.relationshipVisualPlacement: "endpointCorridor"` when using large-import visual strategy.
- `auto` is the default and preserves relationship centers that are already near their endpoint corridor while recomputing suspicious far-away centers.
- Use `explicit` only when the relationship visual coordinates are hand-curated and intentionally close to the relationship endpoints.
- For medium/large diagrams, also consider `autoRoutePlacedLinks: false` or `visualStrategy.deferRouting: true`; users can run Edit -> Appearance -> Route Links with Obstacle Avoidance after import.
- If full-state JSON includes relationship visuals, omit exact relationship visual `x/y` unless exact placement is required, or make sure every relationship center is near the midpoint/corridor between its source and target concepts.

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
  "formatVersion": 1,
  "operations": []
}
```

Rules:

- Update only explicit fields. Omission must not clear or delete native data.
- `delete` operations are dangerous and skipped by default in the current importer.
- Domain create/update targets include `externalLanguage`, `linkRoleVariant`, clusters/categories, `markerDefinition`, `tableDefinition`, `fieldDefinition`, `conceptDefinition`, `relationshipDefinition`, `relationshipRole`, and `outputTemplate`.
- Child entities should include `ownerTechName` when needed, especially fields, roles, and templates.
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

These are deterministic v1 layout helpers, not full graph optimizers. Spider, Hierarchy, Flowchart, and System Map are manual commands only; they are not automatic JSON import `layoutMode` values yet. JSON import currently integrates auto-placement, concept auto-fit, and link auto-route.

## Backlog / not implemented yet

- Custom domain shape import.
- Full multi-bend generic connector route model.
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
