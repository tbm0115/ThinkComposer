# ThinkComposer Output Template Generation

ThinkComposer output templates generate text files from a composition, concept, or relationship using the active external language. A Domain owns the composition-level template plus base templates for concepts and relationships. Individual Concept Definitions and Relationship Definitions can then provide local templates that extend or replace those base templates.

## Automatic Preparation

Composition-level generation now prepares output templates before rendering. Users do not need to open every Concept Definition or Relationship Definition dialog, or visit each Output-Templates tab, just to make definition templates available.

Preparation performs the same model-level materialization that the Output-Templates editor previously performed as a UI side effect:

- Ensures Domain output-template collections and external languages are initialized.
- Resolves the selected external language against the active Domain.
- Inspects Concept Definitions used by ideas in the active composition.
- Inspects Relationship Definitions used by relationships in the active composition.
- Creates missing per-language definition template slots without duplicating existing templates.
- Resolves stale template language references by matching Domain external-language tech names.
- Combines base and local template text for diagnostics without rendering it.
- Discovers declared `%%:SubTemplate=` sections and validates `{% inject 'Name' with ... %}` references before rendering.

Preparation is idempotent. Running generation or `Tools -> Output -> Refresh Output Templates` repeatedly should not create duplicate templates.

## Root Cause

Before this fix, `TemplateEditor.CurrentTemplate` created a missing `TextTemplate` for the Domain's current external language when the Output-Templates tab was opened. Generation called `IdeaDefinition.GetGenerationFinalTemplate(...)` directly and skipped that editor-only materialization. As a result, composition generation could miss definition-level templates until the user manually opened each involved definition editor.

The materialization helper now lives in `OutputTemplatePreparationService`, and the editor calls the shared helper instead of owning the behavior itself.

## Generation Flow

`Generate Files...` now follows this flow:

1. The generation configuration dialog saves the selected external language.
2. Output templates are prepared for the active composition.
3. Blocking preparation errors abort generation with a clear dialog.
4. Warnings are shown concisely and logged in detail.
5. Prepared definition templates are compiled before the composition root is rendered so subtemplates are registered in time for composition-level injection.
6. Templates render through the existing DotLiquid generation path.

The preparation service does not execute user templates. Rendering only occurs during the intended generation or preview flow.

## Diagnostics

The lower-left log records the generation command, composition, Domain, scope, selected external language, counts, and per-template warnings or errors.

Blocking errors include:

- Active composition or Domain cannot be resolved.
- Selected external language cannot be resolved.
- A required injected subtemplate is missing.
- A template section declaration cannot be read.
- A template fails compilation during the generation flow.

Warnings include:

- A used definition has no final template text for the selected language.
- A template references an external language that is not present in the active Domain.
- A template has no external language reference.

The refresh command uses the same preparation service and shows the same summary without generating output.

## JSON Interchange

Domain JSON import/export continues to treat output templates as text only. Templates are not executed during export, import, embedded-domain update, or preparation.

Domain JSON should preserve:

- `externalLanguageTechName`
- `ownerScope`
- `ownerTechName`
- `templateText`
- `extendsBaseTemplate`

After Domain JSON import or embedded Domain update, composition generation prepares the imported templates automatically. If the generation goal depends on definition-level output, make sure the Domain JSON includes output templates for the required Concept Definitions and Relationship Definitions, or includes usable Domain base templates for the selected external language.

## Troubleshooting

If generation aborts, check the lower-left log for `Output template preparation` lines. Common causes are an unresolved external language, a misspelled subtemplate name in an `inject` tag, or a definition whose output template body is empty for the selected language.

If a generated file is missing, confirm that the final template text for that idea is not empty. A definition can intentionally rely on the Domain base template, but if both base and local templates are empty then no file is generated for that idea.

If Domain JSON imported a template but generation cannot find it, check that `ownerScope`, `ownerTechName`, and `externalLanguageTechName` resolved during import. The Domain JSON log reports skipped templates with owner and language details.

## Manual Validation

Baseline:

1. Open a composition using a Domain with concept/relationship output templates.
2. Do not open individual Concept Definition or Relationship Definition dialogs.
3. Run `Tools -> Output -> Generate Files...`.
4. Expected: generation succeeds or reports only real missing templates/languages/subtemplates.

Save/reopen:

1. Import Domain JSON containing output templates.
2. Save the composition.
3. Close and reopen it.
4. Generate composition output.
5. Expected: no definition Output-Templates tabs need to be opened.

MTConnect:

1. Open the MTConnect Machine Monitoring composition.
2. Import/update the companion MTConnect Domain JSON.
3. Generate an MTConnectDevices, SHACL, Mermaid, Text, or Use-Case Proposal output if available.
4. Expected: definition-level templates are prepared automatically before rendering.
