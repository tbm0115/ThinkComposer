# Current Features

This chapter integrates the current user-facing features documented in the repository. The detailed technical references remain in the sibling docs, but the workflows below are the practical manual entry points.

Related references:

- [Appearance and Layout Tools](../appearance-layout-tools.md)
- [ThinkComposer JSON Interchange](../json-interchange.md)
- [ThinkComposer Domain JSON Interchange](../domain-json-interchange.md)
- [Domain Sync](../domain-sync.md)
- [Output Template Generation](../output-template-generation.md)
- [Command-Line Interface](07-command-line-interface.md)
- [Layout Services](../layout-services.md)

## Appearance And Layout Tools

Appearance commands live under:

```text
Edit -> Appearance
```

They move, resize, or route visible representations. They do not change concept or relationship meaning.

| Command | Use when | Changes |
|---|---|---|
| Fit Concept Width to Text | Labels are clipped, too wide, or recently edited. | Selected concept symbol widths. |
| Route Links with Obstacle Avoidance | Connector lines cross concept symbols. | Connector intermediate points and hidden relationship junctions. |
| Arrange as Spider Map | One central idea should radiate to related ideas. | Concept positions and routed links. |
| Arrange as Hierarchy Map | Roots and parents should appear above children/dependencies. | Concept rows, relationship bubbles, and routed links. |
| Arrange as Flowchart | A process should read left to right. | Flow steps, feedback lanes, relationship bubbles, and routed links. |
| Arrange as System Map | A system boundary should separate internal components from external actors. | Internal/external positions, Group Region, relationship bubbles, and routed links. |

### Selection And Undo

- If concept symbols are selected, arrangement commands operate on the selected concepts and visible relationships among them.
- If no concepts are selected, arrangement commands ask before arranging all visible concepts in the active view.
- Link routing operates on selected connectors or relationship visuals; if none are selected, it asks before routing all visible links.
- Each command runs as one undoable ThinkComposer command variation.
- Results are native visual model changes and persist after save, close, and reopen.

### Fit Concept Width To Text

This command measures the visible concept title text, applies conservative padding, respects width limits, and preserves symbol height. It also runs when a user double-clicks a selected concept's left or right resize handle.

### Route Links With Obstacle Avoidance

The router uses the existing connector route model. It preserves valid hand-routed connectors, uses straight routes when possible, and otherwise tries simple orthogonal candidates.

For simple relationships with a hidden central symbol, routing treats the relationship as one unit. The hidden relationship symbol may become the route junction, allowing practical dogleg behavior without adding a new multi-point route model.

### Arrange As Spider Map

Spider Map chooses a root, places it near the center, puts first-level neighbors around it, and places remaining concepts on a second ring. It auto-fits labels first and routes in-scope links after movement.

### Arrange As Hierarchy Map

Hierarchy Map interprets relationship direction when available, chooses roots, assigns levels with a guarded breadth-first traversal, places disconnected components side by side, and declutters visible relationship central symbols.

### Arrange As Flowchart

Flowchart arranges concepts as left-to-right process steps. Feedback, reverse, and long cross-link relationships are placed into a feedback lane outside the main process band.

### Arrange As System Map

System Map detects or uses a selected system/root concept, classifies internal and external concepts, places internals inside a visible Group Region, and places external actors on the left or right of the boundary.

The Group Region is visual only. It does not change semantic containment, composite ownership, or relationship links.

## Composition JSON Interchange

Composition JSON Interchange exports an active `.tcom` composition to editable JSON and safely merges edited JSON back into the active project.

The native `.tcom` package remains authoritative. JSON is an interchange workflow, not a replacement persistence format.

### Workflow

1. Open a composition in ThinkComposer.
2. Run `Composition -> File -> Export JSON...`.
3. Edit the JSON manually, with tools, or with AI assistance.
4. Keep or reopen the original `.tcom` composition.
5. Run `Composition -> File -> Import JSON...`.
6. Review the preview summary and diagnostics.
7. Confirm the merge.
8. Save the `.tcom` file when the result is correct.

Every supported document starts with:

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 1,
  "application": "ThinkComposer"
}
```

Unknown JSON fields are ignored. The schema is maintained at [../thinkcomposer-json-interchange.schema.json](../thinkcomposer-json-interchange.schema.json).

### Full-State Merge

Full-state import updates matching objects by `id` first, then by `techName`. Omitted objects are never deleted.

By default, missing top-level ideas, relationships, and views are treated as updates to existing objects, not creates. To use a generated full-state-style document to populate a blank composition, opt in explicitly:

```json
{
  "importOptions": {
    "useActiveCompositionAsContainer": true,
    "treatMissingFullStateItemsAsCreates": true
  }
}
```

Patch operations remain preferred for AI-authored creation because they make intent, order, and safety easier to inspect.

### Patch Operations

Supported patch operations include `update`, `create`, `delete`, and `place` for supported entity types.

Concept creates require a concept definition and a container. Relationship creates require a relationship definition, a container, and valid origin/target links. Place operations create or update visuals in a target view.

Use the root sentinel for generated root-level patches:

```json
{
  "importOptions": {
    "useActiveCompositionAsContainer": true
  },
  "operations": [
    {
      "op": "create",
      "entity": "concept",
      "definitionTechName": "Concept",
      "containerTechName": "Active_Composition_Root",
      "set": {
        "name": "New concept",
        "techName": "NewConcept"
      }
    }
  ]
}
```

### Visual Strategy

Large generated models should not create, auto-fit, and auto-route every visual by default. Use top-level `visualStrategy` to describe how much visual materialization is intended.

Modes include:

| Mode | Use |
|---|---|
| `modelOnly` | Create semantic data while suppressing visual placement. |
| `overviewAndModel` | Create the full model and a capped overview. |
| `optimizedFullVisual` | Create full visuals but defer expensive fitting/routing/refresh work. |
| `exactFullVisual` | Preserve exact placement for small curated diagrams. |
| `auto` | Choose based on configured thresholds. |

### Relationship Center Placement

ThinkComposer relationships can have visible central symbols. Generated diagrams should usually place relationship centers near their endpoints, not in a distant global label row.

Recommended setting:

```json
{
  "importOptions": {
    "relationshipVisualPlacementMode": "endpointCorridor",
    "recomputeSuspiciousRelationshipVisuals": true
  }
}
```

Use `explicit` only when coordinates are intentionally curated.

### Shortcuts

Composition JSON exports shortcut visual representations with:

```json
{
  "isShortcut": true
}
```

Patch-style placement can request the same behavior with `visual.isShortcut: true`. A shortcut is visual identity, not a duplicate concept.

### Intent-Agnostic Import Primitives

The importer does not infer group regions, membership, layout roles, or hidden relationships from source-format names. Use explicit primitives:

- top-level `groups[]`
- concept `visual.role`
- relationship `layoutRole`
- `visual.display`
- `includeInArrangement`
- `includeInRouting`
- `includeInAutoFit`
- per-relationship `relationshipCenterPlacement`

This keeps the importer source-neutral and predictable.

### Diagnostics And Safety

Import and export write detailed diagnostics to the application log. Dialogs distinguish:

- source warnings
- import warnings
- skipped operations
- dangerous skipped operations
- notes
- errors

Strict relationship/detail compatibility options can block partial imports before apply. Non-strict workflows still import compatible objects and report invalid relationships or unsupported details.

## Domain JSON Interchange

Domain JSON Interchange exports a `.tdom` domain to editable JSON and merges edited JSON back into an open domain. It is also the merge source for updating an existing composition's embedded domain snapshot.

Supported domain work includes:

- domain metadata updates
- descriptions and TechSpec
- concept definitions
- relationship definitions
- link role definitions
- marker definitions
- table definitions and fields
- base tables
- external languages
- output templates

Domain JSON documents use:

```json
{
  "format": "ThinkComposer.DomainJsonInterchange",
  "formatVersion": 1
}
```

The schema is maintained at [../thinkcomposer-domain-json-interchange.schema.json](../thinkcomposer-domain-json-interchange.schema.json).

## Embedded Domain Updates

Use `Composition -> Domain -> Update Embedded Domain...` when an existing `.tcom` composition should pick up safe additions or updates from a newer `.tdom` or Domain JSON source.

The update:

- updates the embedded domain snapshot explicitly
- does not create a live sync link
- does not delete legacy embedded-domain objects by omission
- treats output templates as text during import/update
- prepares imported templates automatically during generation

## Output Template Generation

Output templates generate text files from a composition, concept, or relationship using the active external language.

### Preview Scopes

`Tools -> Output -> Generation Preview` works for the active generation scope:

- No selected idea previews the active composition/root scope.
- One selected concept or relationship previews that selected item.
- Multiple selected items preview the first selected item and record the choice in diagnostics.

The preview window includes:

- Rendered Output
- Effective Template
- Resolution

### Generation Flow

`Tools -> Output -> Generate Files...` follows this high-level flow:

1. Save the selected external language and target directory.
2. Prepare output templates for the active composition.
3. Lint templates.
4. Abort before writing if blocking preparation issues exist.
5. Rebuild the subtemplate registry deterministically.
6. Render document-root templates through DotLiquid.
7. Suppress fragments/subtemplates as standalone deliverables by default.
8. Post-process and validate XML/JSON-like output when appropriate.
9. Log per-file resolution and a generation summary.

### Template Roles

Templates can declare roles:

```text
%%:TemplateRole=DocumentRoot
%%:TemplateRole=Fragment
%%:TemplateRole=SubTemplate
%%:TemplateRole=Diagnostic
%%:TemplateRole=NotApplicable
%%:TemplateRole=Disabled
```

`DocumentRoot` templates emit files. Fragment, SubTemplate, Disabled, and NotApplicable templates are not emitted as final files by default.

### Linting And Validation

Template preparation checks:

- missing required subtemplates
- duplicate subtemplate names
- obvious recursive injection
- empty template bodies
- XML declaration whitespace risks
- fragment templates that look like full documents
- XML attributes filled directly from expressions that may become blank
- invalid template section parsing

For XML-like or JSON-like outputs, generated text can be post-processed and parsed for validation warnings.

### Safe Template Helpers

Current helper filters include:

```liquid
{{ Name | EscapeXmlAttribute }}
{{ Summary | EscapeXmlText }}
{{ TechName | NormalizeTechName }}
{{ Value | DefaultIfEmpty: 'unknown' }}
{{ Info | DetailValue: 'FieldTechName' }}
{{ Value | JsonString }}
```

Use these helpers for XML attributes/elements, JSON strings, fallback text, normalized identifiers, and detail lookups.

## Recommended AI-Assisted Workflow

1. Save or copy the native `.tcom` or `.tdom`.
2. Export JSON when a full-state reference is useful.
3. Ask the assistant to produce patch operations against the schema.
4. Preview import/update in ThinkComposer.
5. Confirm only after reviewing skipped and dangerous skipped counts.
6. Save the native file after successful apply.
7. Re-export JSON when another edit cycle needs a fresh snapshot.

For large generated models, prefer `modelOnly` or `overviewAndModel` visual strategies first, then use manual Appearance tools for final diagram layout.
