# Desktop startup and model-only navigation

Later status: [the drill-down and large-model repair](large-model-runtime.md) adds ten populated subviews and verifies full native load/save/reopen/resave on an updated build. The earlier coordinate, five-symbol and timeout evidence below describes the preceding repair stage.

The reported `Sequence contains no matching element` exception at `WidgetNavTree.xaml.cs:218` exposes an application assumption: tree navigation calls `First` to find a representation of the selected model element in the current View. A model-only element can legitimately have no such representation. The interrelations target is set immediately before the failing lookup, so scrolling can safely be skipped when no matching symbol exists.

This is separate from the empty workspace. The initial coordinate-only repair was insufficient: the native importer created **zero** overview symbols in the full MTConnect model. Hidden elements register visual controls by both ID and TechName. An overview element with no explicit control could inherit `display: hidden` from a different, hidden element with the same TechName. MTConnect repeats names such as Model, Component, Observation, DataItem and Device in different namespaces. The IDs were distinct, but the visual-control fallback still hid the overview.

The generator also originally placed overview symbols near the canvas origin. The default desktop viewport is centered on the 11-page canvas, approximately `(7920, 5885)` at 100 percent scale. That earlier coordinate repair remains useful, but coordinates alone do not prove that native symbols exist.

## Package repair

The generated Domains, examples and MTConnect reference packages now give each intended overview visual an explicit `visual: {display: "visible", includeInView: true}` control. This per-view setting takes precedence over a hidden namesake. Other imported elements remain model-only. The generator applies the same rule to future builds. `scripts/repair_overview_visibility.py` patches existing packages without rebuilding the imported model; it proves that removing just the added controls recovers the original payload exactly.

`reports/overview-visibility-repair.json` records before/after hashes and content-addressed backups under `artifacts/sysml-visibility-repair/backups/`. The full import retains all 32,326 concepts, 1,020 relationships, IDs, ownership and Detail data. Existing positions are preserved.

`reports/desktop-view-repair.json` records each before/after package hash and its backup under `artifacts/sysml-desktop-repair/backups/`. `reports/desktop-viewport-check.json` checks all 19 repaired package payloads independently of fitted image export. `scripts/check_desktop_views.py` reproduces these checks. They model the current application defaults; they are not a substitute for a manual WPF test.

Reopen `reference/mtconnect/MTConnect_Imported.tcom` from this checkout to load the repair. An already-open document retains its old in-memory content. Preserve any unsaved edits in a separate copy before reopening; saving that old document over the repaired file would undo the repair.

## Rendering regression

`fixtures/blocked/hidden-namesake.tcom` reproduces the missing visual with two distinct IDs sharing a TechName. `fixtures/hidden-namesake-visible.tcom` differs only in the explicit overview visibility setting. The actual native loader creates zero symbols in the former and one in the latter. `scripts/test_visual_load.ps1` builds a probe that checks native representation counts. With `-Screenshot`, it also exercises the actual `WidgetDocumentVisualizer` in a non-visible test window, creates the normal View background, pumps layout, checks symbol intersection with the initial viewport, and captures its rendered contents.

The corrected fixture is visible at startup with the existing application build. An early test omitted `View.ShowAll()`, leaving the canvas extent unset and falsely suggesting a scroll-position bug. After matching normal View initialization, the test passes without a scroll-handling change. No new application-source fix is required for the visibility repair.

Persistence validation now checks that every expected overview visual survives native save/reopen. A zero-error import or a JSON `visuals` array alone is insufficient.

All 66 small-model native regression checks pass after this repair, including the new visual-preservation assertions. The full MTConnect file also completed native loading, and the probe found exactly the five expected overview representations. `reports/overview-visibility-validation.json` records this evidence. Full-model save/reopen remains unverified.

Reproduce the targeted cases from the repository root:

```powershell
python prototypes/sysml/scripts/visibility_fixtures.py
powershell -ExecutionPolicy Bypass -File prototypes/sysml/scripts/test_visual_load.ps1 -InputPackage prototypes/sysml/fixtures/blocked/hidden-namesake.tcom -ExpectedCount 0
powershell -ExecutionPolicy Bypass -File prototypes/sysml/scripts/test_visual_load.ps1 -Screenshot artifacts/sysml-visibility-repair/desktop-viewport.png
powershell -ExecutionPolicy Bypass -File prototypes/sysml/scripts/test_visual_load.ps1 -InputPackage prototypes/sysml/fixtures/mtconnect-overview-visibility.tcom -ExpectedCount 5 -Screenshot prototypes/sysml/reports/mtconnect-overview-viewport.png
powershell -ExecutionPolicy Bypass -File prototypes/sysml/scripts/test_visual_load.ps1 -InputPackage prototypes/sysml/reference/mtconnect/MTConnect_Imported.tcom -ExpectedCount 5
```

The last command takes several minutes. The MTConnect rendering fixture preserves the full file's five visual records, their model elements, ownership ancestors, and hidden namesakes in a small package. Its screenshot is a startup-viewport test of that fixture, not a capture of the full model or a conformance claim. Reproduce fixtures only in the prototype checkout; these commands overwrite the generated test fixtures.

## Applied application fix

The user authorized the minor application fix on October 9, 2026. `WidgetNavTree.xaml.cs` now uses `FirstOrDefault` and scrolls only when a matching symbol exists. Model-only elements still become the interrelations target. Existing visible-symbol selection and scrolling remain in place. `fixes/model-only-navigation.patch` retains the review diff; it is already applied to source.

The solution CLI target, including the desktop application, built successfully into `artifacts/sysml-navigation-build/`. This avoids replacing binaries loaded by an existing application process. Run `Instrumind.ThinkComposer.exe` from that directory, or rebuild normally after closing the old application. A running older executable does not acquire the fix automatically.

`scripts/test_navigation.ps1` compiles and runs `NavigationRegression.cs` against this build. It invokes the actual WPF click handler with no symbol, a symbol only in another View, and a current-View symbol; it checks inspection targeting, navigation cleanup and visible selection. All cases pass. Against `artifacts/git-ui-build`, the same harness reproduces the exact `Sequence contains no matching element` exception at line 218. No window is shown and no user document is saved. This regression is more specific than a general manual desktop acceptance test, which remains separate.

## Scale remains separate

The full MTConnect Composition contains 32,326 concepts and 1,020 relationships. The earlier bounded native persistence test did not complete. Its report is historical evidence from before these repairs; it is not a successful load/save certification of the repaired package. The repaired package completed native loading with five overview representations and zero import warnings or errors. Full document-structure processing remains expensive. This repair does not claim a loading-performance improvement, and the reported exception is not evidence of an out-of-memory failure.
