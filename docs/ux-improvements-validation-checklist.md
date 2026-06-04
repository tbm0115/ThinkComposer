# UX Improvements Validation Checklist

Use this checklist before calling the `feature/UXImprovements` layout work stable for a build or pull request.

For `feature/DcomInterchange`, also run the Domain JSON and embedded-domain checks below before release.

## General

- [ ] Build `ThinkComposer/ThinkComposer.csproj` with Debug configuration.
- [ ] Open an existing composition and confirm `Edit -> Appearance` contains the v1 commands.
- [ ] Confirm each command writes useful detail to the lower-left application log.
- [ ] Confirm every visual mutation is undoable and redoable.
- [ ] Save, close, reopen, and confirm layout changes persist.
- [ ] Export PDF/report and confirm visuals render.

## Fit Concept Width to Text

- [ ] Select one short-label concept and run `Edit -> Appearance -> Fit Concept Width to Text`.
- [ ] Select one long-label concept and run the command.
- [ ] Select multiple concepts and run the command.
- [ ] Select relationship symbols/complements and confirm they are skipped safely.
- [ ] Double-click left/right resize handles and confirm auto-fit runs without breaking normal drag resize.
- [ ] Undo and redo.

## Route Links with Obstacle Avoidance

- [ ] Open `Test__Object_Avoidance.tcom`.
- [ ] Import `samples/obstacle-avoidance-regression.sample.json` if needed.
- [ ] Route Scenario A and confirm hidden-center one-bend routing around the obstacle.
- [ ] Route Scenario B and confirm hidden-center dogleg routing around same-row/same-column blocking.
- [ ] Route Scenario C and confirm clear paths remain straight or are straightened.
- [ ] Run with no selected links and confirm the all-visible prompt appears.
- [ ] Undo and redo.
- [ ] Save, reopen, and export PDF/report.

## Spider Map

- [ ] Open `Test__Spider_Map.tcom`.
- [ ] Import `samples/spider-map-regression.sample.json` if needed.
- [ ] Deselect everything and run `Edit -> Appearance -> Arrange as Spider Map`.
- [ ] Confirm the all-visible prompt appears.
- [ ] Confirm the chosen root is central and connected concepts are radial.
- [ ] Confirm no arranged concept is above or left of the reachable canvas origin.
- [ ] Confirm links route after movement.
- [ ] Undo, redo, save/reopen, and export PDF/report.

## Hierarchy Map

- [ ] Open `Test__Hierarchy_Map.tcom`.
- [ ] Import `samples/hierarchy-map-regression.sample.json` if needed.
- [ ] Deselect everything and run `Edit -> Appearance -> Arrange as Hierarchy Map`.
- [ ] Confirm roots appear at the top and children/second-level concepts appear below.
- [ ] Confirm disconnected components are separated.
- [ ] Confirm visible relationship bubbles do not overlap each other or concepts in the target regression cases.
- [ ] Confirm local relationship bubbles stay near their endpoint corridor.
- [ ] Confirm links route after relationship-bubble movement.
- [ ] Undo, redo, save/reopen, and export PDF/report.

## Flowchart

- [ ] Open `Test__Flowchart.tcom`.
- [ ] Import `samples/flowchart-regression.sample.json` if needed.
- [ ] Deselect everything and run `Edit -> Appearance -> Arrange as Flowchart`.
- [ ] Confirm starts appear on the left and downstream steps flow left-to-right.
- [ ] Confirm branches are vertically separated.
- [ ] Confirm feedback/reverse/cross-link bubbles use an outer lane.
- [ ] Confirm the Error Feedback relationship does not overlap the Invalid concept or Invalid To Error bubble.
- [ ] Confirm forward links remain readable and routed.
- [ ] Undo, redo, save/reopen, and export PDF/report.

## System Map

- [ ] Open `Test__System_Map.tcom`.
- [ ] Import `samples/system-map-regression.sample.json` if needed.
- [ ] Deselect everything and run `Edit -> Appearance -> Arrange as System Map`.
- [ ] Confirm Deployment Manager System is selected as root/header.
- [ ] Confirm Web App, Package Catalog, Job Orchestrator, Configuration Manager, and Audit Log are comfortably inside the Group Region.
- [ ] Confirm User and Package Source are outside left, and Host Agent and OT Network Endpoint are outside right.
- [ ] Confirm the Group Region is visible, behind concepts, and does not alter semantic containment.
- [ ] Confirm Package Source To Catalog does not overlap User.
- [ ] Confirm User To Web App, Package Source To Catalog, and Web App To Audit Log do not overlap each other.
- [ ] Confirm links cross the Group Region boundary cleanly.
- [ ] Undo, redo, save/reopen, and export PDF/report.

## JSON Import Visual Cleanup

- [ ] Import a patch that creates concepts with long names and confirm `autoFitPlacedConcepts` fits newly placed concept widths.
- [ ] Confirm existing concepts that are only text-updated are not resized unless operation `autoFit: true` is set.
- [ ] Confirm operation `autoFit: false` suppresses fitting for that operation.
- [ ] Import a patch with newly placed relationships and confirm `autoRoutePlacedLinks` routes touched links only.
- [ ] Confirm pre-existing unrelated links are not routed by JSON import.
- [ ] Confirm operation `autoRoute: false` suppresses routing for that operation.
- [ ] Undo import and confirm concept widths and connector routes revert.
- [ ] Save, reopen, and export PDF/report.

## Composition JSON TechSpec

- [ ] Export a `.tcom` JSON file from a composition that has TechSpec on the composition, concepts, relationships, or definitions.
- [ ] Confirm exported JSON includes `techSpec` where values exist.
- [ ] Import `samples/json-interchange-regression.sample.json` after adapting tech names to the active composition.
- [ ] Confirm `set.techSpec` updates an existing concept and logs the field-level update.
- [ ] Save, reopen, and confirm TechSpec persists.

## Composition JSON Fixture Imports

- [ ] Create a fresh composition such as `Composition1`.
- [ ] Import `samples/composition-active-root-fallback.sample.json`.
- [ ] Confirm the preview/apply summary creates 2 concepts and 1 relationship, with `Skipped: 0`.
- [ ] Confirm the log includes `JSON import container fallback: requested='Active_Composition_Root'` and an active/root view fallback because the sample omits `viewTechName`.
- [ ] Import each layout fixture sample that declares `importOptions.useActiveCompositionAsContainer=true`.
- [ ] Confirm create counts are greater than zero and root-level fixture containers fall back to the active composition.
- [ ] Confirm the log includes `JSON import container fallback` for samples whose `containerTechName` names a missing `Test__...` composition.
- [ ] Run the matching Appearance layout command for each imported fixture.
- [ ] Confirm samples without the fallback still explain which `containerId`/`containerTechName` values must be replaced before import.
- [ ] For `machine_monitoring_utilization_productivity_composition.json`, import/update the companion MTConnect Domain JSON first, then import the composition patch.
- [ ] Confirm the generated MTConnect patch previews 20 concept creates and 34 relationship creates, with no missing-container skips for `Active_Composition_Root`.
- [ ] Confirm any remaining skips are specific definition, endpoint, role, view, or detail issues rather than container fallback failures.
- [ ] Confirm relationship compatibility skips include endpoint concept definitions, resolved roles, allowed endpoint definitions when available, and a grouped summary by relationship definition.
- [ ] Confirm relationship compatibility failures emit a copyable `BEGIN THINKCOMPOSER RELATIONSHIP COMPATIBILITY REPORT` block in the lower-left log.
- [ ] Import `samples/composition-strict-domain-compatibility.sample.json` into an All-Purpose composition and confirm strict relationship preflight passes with zero compatibility skips.
- [ ] Change the strict sample's `requires.domain.techName` or use a mismatched active domain and confirm `domainCompatibilityPolicy=requireTechName` blocks before apply.
- [ ] Add strict options to the current machine-monitoring generated patch and confirm `strictRelationshipCompatibility=true` plus `abortOnRelationshipCompatibilityFailure=true` blocks before concepts/relationships are created when compatibility failures exist.
- [ ] Import a latest-Skill-generated full-state-style composition JSON without `treatMissingFullStateItemsAsCreates` and confirm the dialog/log explains that missing top-level ideas/relationships were treated as updates, not creates.
- [ ] Import `samples/composition-full-state-create.sample.json` into a blank All-Purpose composition and confirm concepts created=2, relationships created=1, visuals placed > 0, skipped=0.
- [ ] Add `treatMissingFullStateItemsAsCreates=true` to the generated full-state machine-monitoring JSON and confirm concepts are created instead of skipped; relationships either create or report relationship compatibility failures.
- [ ] Import `samples/composition-relationship-fallback.sample.json` in a domain that has the requested strict relationship definition and generic `Relationship` fallback, or read its warning notes when the strict definition is unavailable.
- [ ] Confirm `relationshipDefinitionFallbackTechName` is never used silently: fallback is logged, and `strictDefinition: true` prevents fallback.
- [ ] Import a patch with `set.details` using a missing native detail designator and `detailFallbackMode=appendToTechSpec`; confirm the idea is still created and the detail text/rows are appended to TechSpec with a clear delimiter.

## Domain JSON Interchange

- [ ] Open or create a `.tdom`.
- [ ] Run `Domain -> Export Domain JSON...`.
- [ ] Confirm exported Domain JSON includes `domain.compatibilitySignature` and a `relationshipCompatibility` section when relationship definitions are present.
- [ ] Run `Domain -> Import/Update Domain JSON...` with `samples/domain-json-interchange-patch.sample.json`.
- [ ] Confirm the preview lists planned creates/updates and dangerous skipped deletes.
- [ ] Confirm domain summary/TechSpec updates, and new definitions/tables/fields/templates are created when dependencies exist.
- [ ] Confirm output template text is preserved as text and never executed.
- [ ] Run `Domain -> Import/Update Domain JSON...` with `samples/domain-json-metadata-update.sample.json`.
- [ ] Confirm field-level log lines include domain `summary`/`techSpec` and externalLanguage `summary`/`techSpec`, including match method.
- [ ] Confirm metadata patch result is 2 updates, 0 skips, 0 source warnings, 0 import warnings, and 0 errors after adapting the external language techName if needed.
- [ ] Save/reopen, export Domain JSON again, and verify `domain.techSpec` plus the target `externalLanguages[]` summary/TechSpec persisted.
- [ ] Run `Domain -> Import/Update Domain JSON...` with `samples/domain-json-additive-definition.sample.json`.
- [ ] Confirm additive patch result is 5 creates, 0 skips, 0 source warnings, 0 import warnings, and 0 errors on a fresh test copy.
- [ ] Confirm additive table, fields, concept definition, and output template are created without destructive changes.
- [ ] Confirm field logs show parent table and data type resolution, and template logs show owner scope/owner/language resolution.
- [ ] Save, reopen, and confirm changes persist.

## Output Template Generation

- [ ] Baseline repro: open a composition using a domain with concept/relationship output templates, do not open any Concept/Relationship Definition dialogs, run `Tools -> Output -> Generate Files...`, and confirm generation succeeds or reports only real missing templates/languages/subtemplates.
- [ ] Refresh command: run `Tools -> Output -> Refresh Output Templates` and confirm the dialog reports concept definitions inspected, relationship definitions inspected, templates prepared, warnings, and errors without generating files.
- [ ] Preview scope: clear the selection, run `Tools -> Output -> Generation Preview`, and confirm preview uses the active composition/root scope instead of being disabled.
- [ ] Effective template: select one concept or relationship, run `Generation Preview`, and confirm the preview window shows `Rendered Output`, `Effective Template`, and `Resolution` tabs.
- [ ] Resolution metadata: confirm preview shows target name/techName/id/kind, selected external language, template owner scope/techName, template role, template hash, generated filename, and validation notes.
- [ ] Generate files: confirm each generated file logs `Output template resolution` with file path, source item, language, owner scope, template hash, role, and validation result.
- [ ] Subtemplates: confirm the log contains `Output template subtemplates registered` entries and that explicit Fragment/SubTemplate templates are not emitted as standalone deliverables by default.
- [ ] Validation: generate XML-like or JSON-like output and confirm the summary reports XML/JSON valid/invalid counts without crashing generation.
- [ ] Save/reopen: import Domain JSON containing output templates, save the composition, close/reopen, generate composition output, and confirm no individual Output-Templates tabs need to be opened.
- [ ] MTConnect case: open the MTConnect Machine Monitoring composition, import/update the companion MTConnect Domain JSON, and generate an MTConnectDevices, SHACL, Mermaid, Text, or Use-Case Proposal output if available.
- [ ] Confirm the lower-left log contains `Output template preparation started`, active composition/domain/language context, inspected counts, materialized template counts, lint counts, and per-template warnings/errors.
- [ ] Confirm output templates are not executed during Domain JSON import/export or refresh-only preparation.

## Embedded Domain Update

- [ ] Open an older `.tcom` composition.
- [ ] Run `Composition -> Domain -> Update Embedded Domain...`.
- [ ] Select a newer `.tdom` or `samples/domain-sync-update.sample.json`.
- [ ] Confirm the preview lists added/updated/legacy-retained domain objects.
- [ ] Confirm source warnings, import warnings, skipped operations, dangerous skipped operations, and errors are listed separately.
- [ ] Confirm preserved source/export warnings do not make a clean update look like a failed import.
- [ ] Apply and confirm existing composition ideas remain intact.
- [ ] Confirm new definitions/templates/tables appear in palettes/domain-dependent UI where supported.
- [ ] Undo/redo if practical.
- [ ] Save, reopen, and confirm the embedded domain update persists.

## Documentation and Skill Bundle

- [ ] Parse `docs/thinkcomposer-json-interchange.schema.json` with `ConvertFrom-Json`.
- [ ] Parse `docs/thinkcomposer-domain-json-interchange.schema.json` with `ConvertFrom-Json`.
- [ ] Parse every `samples/*.sample.json` file with `ConvertFrom-Json`.
- [ ] Sync the bundled skill references under `docs/thinkcomposer-json-interchange/references/`.
- [ ] Regenerate `docs/thinkcomposer-json-interchange.zip`.
- [ ] List the ZIP contents and confirm `SKILL.md`, `scripts/validate_json.py`, and current references are included without absolute paths.
