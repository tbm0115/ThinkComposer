# Dcom Interchange Release Notes

This branch consolidates the first Domain/Composition JSON Interchange pass for ThinkComposer. Modern `.tcom` and `.tdom` packages now use root JSON as authoritative persistence; the explicit JSON commands remain export, patch, import, and safe merge workflows.

## New Commands

- Composition JSON interchange includes TechSpec-aware composition payloads and clearer source/import warning reporting. The earlier desktop `Composition -> File -> Export JSON...` and `Composition -> File -> Import JSON...` buttons are now deprecated in favor of CLI `thinkcomposer composition export-json` and `thinkcomposer composition import-json`.
- `Domain -> Export Domain JSON...` exports text-safe Domain JSON using `format: "ThinkComposer.DomainJsonInterchange"`.
- `Domain -> Import/Update Domain JSON...` previews and applies safe additive domain merges.
- `Composition -> Domain -> Update Embedded Domain...` safely updates an active `.tcom` composition's embedded domain snapshot from a native `.tdom` or Domain JSON file.
- Composition output generation now prepares concept/relationship definition output templates automatically before rendering, including templates imported through Domain JSON.
- `Tools -> Output -> Generation Preview` now supports the active composition/root scope when nothing is selected and shows rendered output, the effective template, and resolution metadata.

## Supported Workflows

- Update `.tcom` composition, concept, relationship, view, and supported definition summary/description/TechSpec fields through `ThinkComposer.JsonInterchange`.
- Create and visually place composition concepts/relationships with auto-place, auto-fit, auto-route, recursive-composite protection, and root fixture fallback via `importOptions.useActiveCompositionAsContainer`.
- Author GPT-generated root-level `.tcom` patches with the canonical `containerTechName: "Active_Composition_Root"` sentinel so creates can target the active composition safely after preview.
- Import GPT-generated full-state-style `.tcom` documents into blank compositions only when explicitly allowed with `importOptions.treatMissingFullStateItemsAsCreates=true` or per-item `isNew:true`.
- Treat full-state `views[].visuals[]` for newly created ideas/relationships as safe placement requests, while grouping dependent visual skips when owners were not created.
- Use top-level `visualStrategy` for large generated imports so GPTs can request semantic/model-only import, overview-plus-model import, optimized/deferred full visuals, or exact full visuals. Strategy deferral can suppress visual materialization, auto-fit, auto-route, and immediate view refresh to avoid out-of-memory behavior on very large models.
- Inspect saved native `.tcom` and `.tdom` containers directly through AI-readable sidecar snapshots under `/Interchange/`, including deterministic JSON exports, a container manifest, and capped PNG view previews.
- Preserve and import shortcut visual representations with `isShortcut:true`, including patch-style `visual.isShortcut:true`, so one semantic idea can appear in multiple places without creating duplicate concepts.
- Correct imported visible relationship central symbols before routing with endpoint-corridor candidate scoring. Generated diagrams can use `relationshipVisualPlacementMode=auto` or `endpointCorridor` so connectors do not route through distant global relationship-label rows.
- Express source intent through generic, explicit primitives such as top-level `groups[]`, concept `visual.role`, relationship `layoutRole`, `visual.display`, `includeInArrangement`, `includeInRouting`, and per-relationship `relationshipCenterPlacement`. The importer remains intent-agnostic and does not infer behavior from source formats, domains, concept names, or relationship names.
- Diagnose relationship definition compatibility skips with endpoint definitions, role names, native `CanLink` reasons, and grouped skip counts by relationship definition.
- Export domain/composition compatibility metadata, including a deterministic embedded-domain compatibility signature, and use optional `requires` blocks plus import policies to catch stale or mismatched GPT patches.
- Run strict relationship/detail compatibility preflight and block apply before opening a command variation when strict abort options are enabled.
- Emit copyable `BEGIN THINKCOMPOSER RELATIONSHIP COMPATIBILITY REPORT` log blocks to help regenerate domain-valid composition patches.
- Preserve draft graph structure explicitly with `importOptions.relationshipDefinitionFallbackTechName` when a generated relationship uses an over-specific definition and `strictDefinition` is not set.
- Keep unsupported GPT-authored details from blocking concept/relationship creation, with optional `detailFallbackMode` to append detail text/rows to TechSpec or Description.
- Round-trip Composition JSON descriptions alongside summaries and TechSpec; JSON descriptions are plain text and are converted to native rich-text storage internally. Safe Text details can target known fields such as Description, Summary, or TechSpec.
- Export `.tdom` domain metadata, definitions, tables, fields, roles, markers, external languages, and output templates as text-safe Domain JSON.
- Import additive Domain JSON patches for metadata, TechSpec, definitions, tables/fields, external languages, and output templates.
- Generate composition output without opening every involved definition's Output-Templates tab first.
- Diagnose generated files with per-file output-template resolution logs, deterministic subtemplate registration logs, template role inference/directives, XML/JSON post-processing, and XML/JSON validation summaries.
- Update an existing `.tcom` embedded domain snapshot without deleting legacy objects or replacing the domain wholesale.

## Validated Manual Scenarios

- Domain metadata/TechSpec patch import.
- Domain additive import with five created objects and no skips on a fresh test copy.
- Domain save/reopen persistence after import.
- Embedded Domain update from native `.tdom`.
- Output-template external language alias resolution such as `Mermaid.js_Flowchart` to `Mermaid_JS_Flowchart`.
- Output-template generation preview/generate diagnostics, including effective template preview, fragment/subtemplate suppression, subtemplate registry logs, and XML validation for XML-like output.
- Source-warning vs import-warning dialog categorization.
- Composition layout fixture import into a fresh blank composition using `useActiveCompositionAsContainer`.
- Composition active-root fallback import using `samples/composition-active-root-fallback.sample.json`, expecting 2 concepts and 1 relationship created with zero missing-container skips.
- Composition description/detail regression import using `samples/composition-description-details-regression.sample.json`, expecting first-class Description/TechSpec updates and unsupported detail fallback without skipping created ideas.
- Generated MTConnect composition patch workflow after importing/updating the companion Domain JSON first: `machine_monitoring_utilization_productivity_composition.json` should plan root-level concept and relationship creates instead of skipping `Active_Composition_Root`.
- Machine-monitoring composition imports that use incompatible domain-specific relationship definitions now report compatibility skips as domain validation issues, not container/endpoint importer failures.
- Full-state-style generated composition documents now produce a clear note when missing top-level IDs are update-only because full-state-create mode is disabled; enabling the option allows concepts to create before relationship compatibility is evaluated.
- Strict mode on machine-monitoring-style generated patches should block partial imports before apply when domain relationship compatibility failures are detected; non-strict mode still imports compatible concepts/relationships and skips invalid relationships with diagnostics.
- Large generated composition imports should include `visualStrategy.mode=modelOnly` for output-template/model generation, `overviewAndModel` for a small navigable overview plus full semantic model, or `optimizedFullVisual` when visual creation is required but auto-fit/auto-route/view refresh should be deferred.
- Generated visual imports should avoid exact relationship-bubble coordinates unless they are hand-curated. Prefer `importOptions.relationshipVisualPlacementMode=endpointCorridor` or `visualStrategy.relationshipVisualPlacement=endpointCorridor`.
- Shortcut round-trip import using `samples/composition-shortcut-roundtrip.sample.json` should create one semantic concept and a second shortcut visual representation, with export/re-import preserving `isShortcut:true`.
- Intent-agnostic visual-control samples demonstrate explicit Group Region creation, hidden membership edges, summary/deferred concepts, and visible dependency edges without source-specific importer behavior.
- Saved `.tcom` packages should include `/Interchange/Composition.json`, `/Interchange/Domain.json`, `/Interchange/manifest.json`, and capped `/Previews/views/*.png` entries when preview rendering succeeds.
- Saved `.tdom` packages should include `/Interchange/Domain.json` and `/Interchange/manifest.json`; template composition JSON/previews are written only when the Domain save explicitly includes a template composition.
- Existing Appearance layout commands remain manual v1 commands and are separate from JSON import layout modes.

## Warning Model

Dialogs distinguish source warnings, import warnings, skipped operations, dangerous skipped operations, notes, and errors. Preserved export/source warnings such as missing category summaries or text-only template notes are context, not failures of the current operation.

## Recommended User Workflow

1. Save or copy the native `.tcom` or `.tdom`.
2. Export JSON when a full-state reference is useful, or author a patch directly against the schema.
3. Preview the import/update operation.
4. Confirm only after reviewing skipped and dangerous skipped counts.
5. Save the native `.tcom` or `.tdom` after successful apply.
6. Re-export JSON when verification or GPT follow-up work needs a fresh snapshot.

## Bundled Skill

The bundled `thinkcomposer-json-interchange` Skill is maintained with the schemas, docs, and samples. It now references the local ThinkComposer user manual PDF at `../../Installer/Deploy/InstrumindThinkComposer_Manual.pdf` for broader application context. The schemas and Markdown docs remain authoritative for current JSON interchange support; the manual may describe application features that are not yet supported by JSON interchange.

## Remaining Limitations

- Destructive domain cleanup and migrations are skipped by default.
- Live automatic `.tdom` synchronization is not implemented.
- Custom domain shape import is not implemented.
- Rich/binary content is summarized as metadata and warnings unless a JSON field explicitly covers it; binary attachment/image payloads are not reconstructed by JSON persistence.
- Embedded container JSON/previews are synchronized snapshots only. Modern native packages use root `/Composition.json` and `/Domain.json`; `/Composition.bin` and `/Domain.bin` are retained only as legacy fallback/recovery payloads during the transition.
- Embedded previews are PNG v1. SVG/vector previews remain backlog unless a safe WPF Drawing-to-SVG export path is added.
- General full multi-bend connector routing and full graph crossing minimization remain backlog.
- Spider, Hierarchy, Flowchart, and System Map are manual Appearance commands; JSON import currently integrates auto-placement, concept auto-fit, and link auto-route only.
- `visualStrategy.overviewViewTechName` prefers an existing view but does not create new views yet; full overview grouping by `groupBy` is logged as intent and remains a future layout/materialization enhancement.
- Relationship center cleanup uses local endpoint-corridor candidate scoring and overlap penalties. It is not a full edge-label optimizer, edge bundler, or crossing-minimizing graph drawing engine.
- Compatibility signatures detect stale domain contracts but are not security signatures and do not replace native relationship validation.
- Full embedded-domain/output-template diff UI remains backlog; current diagnostics use template hashes, owner metadata, and preview logs.
