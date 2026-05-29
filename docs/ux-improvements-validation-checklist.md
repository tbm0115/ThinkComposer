# UX Improvements Validation Checklist

Use this checklist before calling the `feature/UXImprovements` layout work stable for a build or pull request.

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

## Documentation and Skill Bundle

- [ ] Parse `docs/thinkcomposer-json-interchange.schema.json` with `ConvertFrom-Json`.
- [ ] Parse every `samples/*.sample.json` file with `ConvertFrom-Json`.
- [ ] Sync the bundled skill references under `docs/thinkcomposer-json-interchange/references/`.
- [ ] Regenerate `docs/thinkcomposer-json-interchange.zip`.
- [ ] List the ZIP contents and confirm `SKILL.md`, `scripts/validate_json.py`, and current references are included without absolute paths.
