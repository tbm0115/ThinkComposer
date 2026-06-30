# ThinkComposer Output Template Generation

ThinkComposer output templates generate text files from a composition, concept, or relationship using the active external language. A Domain owns composition-level templates plus base templates for concepts and relationships. Individual Concept Definitions and Relationship Definitions can extend or replace those base templates.

Output templates are text. Import/export, refresh, preview preparation, and embedded-domain update do not execute them. Rendering occurs only during `Tools -> Output -> Generation Preview` or `Tools -> Output -> Generate Files...`.

## Preview Scopes

`Tools -> Output -> Generation Preview` now works for the active generation scope:

- No selected idea: previews the active composition/root scope, matching what `Generate Files...` would start from.
- One selected concept or relationship: previews that selected item.
- Multiple selected items: previews the first selected item and records that choice in the preview metadata/log.

The preview window has three tabs:

- `Rendered Output`: renders to a temporary buffer and does not write a file.
- `Effective Template`: shows the final template body selected for rendering, including inherited/base template text where applicable.
- `Resolution`: shows the target item, target kind, id, selected external language, resolved owner scope, source collection, template role, hash, text length, subtemplate counts, lint counts, generated filename, and validation/post-processing notes.

This uses the same preparation and rendering path as `Generate Files...`, so preview diagnostics should explain the same template selection that file generation will use.

## Generation Flow

`Generate Files...` follows this flow:

1. The generation configuration dialog saves the selected external language and target directory.
2. Output templates are prepared for the active composition.
3. Template linting checks roles, subtemplates, obvious recursion, XML/JSON risks, and empty bodies.
4. Blocking preparation/lint errors abort generation before files are written.
5. The composition-scoped subtemplate registry is cleared and rebuilt deterministically.
6. Document-root templates render through the existing DotLiquid generation path.
7. Fragment/SubTemplate/Disabled/NotApplicable templates are suppressed as standalone deliverables by default.
8. XML/JSON-like rendered files are post-processed and parsed for validation warnings.
9. The lower-left log records per-file template resolution and a generation summary.

## Template Resolution Logging

Every generated file logs:

- file path
- generation scope
- source item name, techName, and id
- external language
- resolved template name and techName
- template owner scope and owner techName
- template source collection
- template text length and hash
- whether it is a subtemplate or document root
- whether it extends a base template
- output role
- validation result, when validation ran

This makes it clear whether output came from a composition-level template, a definition-level template, an embedded-domain template, a base/fallback template, or an older imported/native template.

## Template Roles

ThinkComposer supports role metadata through directives in the template text:

```text
%%:TemplateRole=DocumentRoot
%%:TemplateRole=Fragment
%%:TemplateRole=SubTemplate
%%:TemplateRole=Diagnostic
%%:TemplateRole=NotApplicable
%%:TemplateRole=Disabled
```

Existing subtemplate declarations still work:

```text
%%:SubTemplate=DeviceTemplate
```

`%%:SubTemplate=Name` is treated as a `SubTemplate` role when the template body is only subtemplate sections. If a legacy template contains a root body plus subtemplate sections, it is inferred as a document root so old domains keep generating as before.

Default generation emits `DocumentRoot` templates. `Fragment`, `SubTemplate`, `Disabled`, and `NotApplicable` templates are not emitted as final files unless a future explicit debug/all-templates mode is added.

## Subtemplate Discovery

Before rendering, ThinkComposer scans prepared templates for the selected external language and registers subtemplates in deterministic order:

1. owner scope
2. owner techName
3. template techName/id when available

The log lists entries like:

```text
Output template subtemplates registered: DeviceTemplate -> Device.Device_Devices_Response_Document hash=...
```

Duplicate subtemplates with identical bodies are warned and resolved deterministically. Duplicate subtemplates with conflicting bodies are blocking because silently choosing one could produce misleading output.

Missing required `{% inject 'Name' with ... %}` subtemplates are blocking preparation errors.

## Linting

Template linting runs during preview, refresh, and generation preparation. It currently checks:

- missing required subtemplates
- duplicate subtemplate names
- direct obvious recursive injection
- empty template bodies
- XML declarations preceded by whitespace
- fragment/subtemplate templates that appear to contain full XML documents
- XML attributes filled directly from expressions that may become blank
- invalid template section parsing

Lint severities are `Info`, `Warning`, `Error`, and `Blocking`. Blocking issues prevent generation; warnings are shown and logged.

## Post-Processing And Validation

Post-processing can be controlled with directives:

```text
%%:outputPostProcess.trimLeadingWhitespace=true
%%:outputPostProcess.normalizeLineEndings=LF
%%:outputPostProcess.writeUtf8NoBom=true
%%:outputPostProcess.ensureTrailingNewline=true
%%:outputValidation=XmlWellFormed
```

For XML-like languages or `.xml` outputs, ThinkComposer trims leading BOM/whitespace before the XML declaration and validates well-formed XML. For JSON-like languages or `.json` outputs, it parses rendered JSON. Validation failures are warnings by default; generation does not crash.

## Safe Template Helpers

The DotLiquid filter set includes safer helpers for common generated text:

```liquid
{{ Name | EscapeXmlAttribute }}
{{ Summary | EscapeXmlText }}
{{ TechName | NormalizeTechName }}
{{ Value | DefaultIfEmpty: 'unknown' }}
{{ Info | DetailValue: 'FieldTechName' }}
{{ Value | JsonString }}
```

Use these helpers for XML attributes/elements, JSON string values, fallback text, normalized identifiers, and simple table-detail field lookup. They are domain-neutral and do not execute external code.

## Domain JSON Interchange

Domain JSON import/export preserves output templates as text only. It also logs template owner scope, language resolution, old/new text length, old/new hash, extends-base flag, role, and target filename/extension hints when templates are created or updated.

After Domain JSON import or embedded-domain update, generation treats template resolution as dirty and rebuilds preparation/subtemplate state on the next preview or generation run. Users should not need to open every definition's Output-Templates tab after import/update.

## Troubleshooting

If generated output comes from an unexpected template, open `Generation Preview` and inspect the `Resolution` and `Effective Template` tabs. Confirm the owner scope, owner techName, language, template hash, and source collection.

If old/fallback/native templates appear to be used, update the embedded Domain from the intended `.tdom` or Domain JSON source, then preview again and compare template hashes in the log.

If fragment files are being produced unexpectedly in an older domain, add explicit `%%:TemplateRole=SubTemplate` or `%%:TemplateRole=Fragment` directives.

If XML output has blank critical attributes, use `DefaultIfEmpty`, `EscapeXmlAttribute`, or `DetailValue` rather than emitting raw expressions inside attributes.

If a subtemplate is missing, check spelling and confirm the template belongs to the same selected external language.

## Limitations

- This is a deterministic v1 diagnostic/lint layer, not a full static analyzer for DotLiquid.
- Mermaid and Markdown validation are not implemented.
- Scope isolation for injected templates is not changed in this pass; recursion is guarded and obvious cycles are linted.
- There is no full embedded-domain output-template diff UI yet. Use Domain JSON export and template hashes as a practical comparison path.

## Manual Validation

Preview scope:

1. Open a composition.
2. Select no concept.
3. Run `Tools -> Output -> Generation Preview`.
4. Expected: preview uses the active composition/root scope.

Effective/rendered preview:

1. Select a concept or relationship.
2. Run `Generation Preview`.
3. Confirm rendered output, effective template, filename, owner scope, language, and validation notes are visible.

Generate files:

1. Run `Tools -> Output -> Generate Files...`.
2. Confirm per-file template resolution lines appear in the lower-left log.
3. Confirm the generation summary reports files generated, fragments suppressed, and XML/JSON validation counts.

MTConnect regression:

1. Import/update the MTConnect Domain JSON patch.
2. Import the matching composition patch.
3. Generate `Devices_Response_Document`.
4. Confirm root/subtemplate roles are explicit in diagnostics, XML validation runs for `.xml` output, and fragment/subtemplates are not emitted as standalone deliverables unless intentionally configured.
