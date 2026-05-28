# Appearance and Layout Tools

ThinkComposer now has a foundation for reusable diagram appearance and layout commands. The first implemented command is available from:

`Edit -> Appearance -> Fit Concept Width to Text`

## Fit Concept Width to Text

Select one or more concept symbols in the active view, then run the command. ThinkComposer measures the visible concept title text with the current symbol text format, applies conservative padding, respects minimum and maximum width limits, and preserves the current symbol height.

The command:

- fits only selected concept symbols
- skips relationships, complements, and other non-concept visuals
- runs as one undoable command variation
- re-renders affected symbols and connectors
- writes detailed counts and warnings to the lower-left application log

You can undo and redo the result with the normal edit history.

## Resize Handle Shortcut

Double-clicking a selected concept's left or right resize handle runs the same width auto-fit service used by the menu command. Normal click-and-drag resizing is unchanged.

## Future Appearance Tools

The Appearance group includes disabled placeholders for planned layout tools:

- Route Links with Obstacle Avoidance
- Arrange as Spider Map
- Arrange as Flowchart
- Arrange as Hierarchy Map
- Arrange as System Map

These commands are intentionally disabled until their layout algorithms are implemented.

## JSON Import Relationship

JSON import now uses the same auto-fit service for concept visuals created or newly placed during import when `importOptions.autoFitPlacedConcepts` is omitted or true. A patch operation can override this with `autoFit: false`, or can force fitting for an updated existing concept with `autoFit: true`.

The auto-fit service and `LayoutSelectionContext` remain UI-independent enough for future JSON import layout passes and manual layout tools to share the same measurement and visible-graph behavior.

## Manual Regression

1. Open an existing composition.
2. Select one concept with a short or long label.
3. Run `Edit -> Appearance -> Fit Concept Width to Text`.
4. Verify the width changes appropriately and connectors update.
5. Undo and redo.
6. Select multiple concept symbols with different label lengths.
7. Run the command again and verify all selected concepts update in one undoable step.
8. Select relationship symbols or complements and verify they are skipped safely.
9. Resize a concept normally and verify drag resizing still works.
10. Double-click a concept's left or right resize handle and verify auto-fit runs without breaking drag resize.
