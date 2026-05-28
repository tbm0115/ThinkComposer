# ThinkComposer JSON Interchange

ThinkComposer JSON interchange exports the active `.tcom` composition to an editable text file, then safely merges edited JSON back into the currently open project.

The native `.tcom` package remains the authoritative format. JSON import/export is an interchange workflow only; it does not replace, convert, or alter native persistence.

## Workflow

1. Open a composition in ThinkComposer.
2. Use `Composition > File > Export JSON...`.
3. Edit the `.json` file manually or with GPT assistance.
4. Reopen or keep the original `.tcom` composition active.
5. Use `Composition > File > Import JSON...`.
6. Review the preview summary and confirm the merge.
7. Save the `.tcom` normally when you want to keep the imported changes.

## Format

Every supported file starts with:

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 1,
  "application": "ThinkComposer"
}
```

`formatVersion` is the interchange schema version. Version `1` supports full-state merge files and patch-operation files. Unknown JSON fields are ignored.

Exports include deterministic, pretty-printed DTO data rather than native binary or WPF object graphs. The main top-level sections are:

- `composition`: document identity, name, tech name, summary, version fields, view prefix, active/root view ids, and domain summary.
- `definitions`: domain definition names and tech names referenced by exported ideas.
- `ideas`: concepts with stable ids, definition references, editable text, container, markers, details, and child idea ids.
- `relationships`: relationships with stable ids, definition references, editable text, role links, origin/target idea ids, and container.
- `views`: view identity plus safe layout data for visible representations.
- `operations`: optional patch instructions.
- `warnings`: export notes for skipped or metadata-only native data.

Import always merges into the active `.tcom` composition. Existing entities are matched by `id` first, then by `techName` when no id is supplied. Missing JSON objects are left untouched.

## Full-State Merge Example

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 1,
  "exportedAtUtc": "2026-05-28T00:00:00Z",
  "application": "ThinkComposer",
  "composition": {
    "id": "00000000-0000-0000-0000-000000000001",
    "name": "Research Map",
    "techName": "ResearchMap",
    "summary": "GPT-edited project summary",
    "viewsPrefix": "View"
  },
  "ideas": [
    {
      "id": "00000000-0000-0000-0000-000000000010",
      "kind": "Concept",
      "definitionTechName": "Concept",
      "name": "Renamed concept",
      "techName": "RenamedConcept",
      "summary": "Editable concept summary",
      "markers": [
        {
          "definitionTechName": "Priority_1",
          "descriptorName": "Review"
        }
      ]
    }
  ],
  "relationships": [],
  "views": [],
  "operations": [],
  "warnings": []
}
```

Full-state import updates matching objects by `id` first. If `id` is absent, it matches by `techName`. Omitted objects are never deleted.

## Patch Operations Example

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 1,
  "operations": [
    {
      "op": "update",
      "entity": "concept",
      "id": "existing-concept-guid",
      "set": {
        "name": "Renamed concept",
        "summary": "New GPT-edited summary"
      }
    },
    {
      "op": "create",
      "entity": "concept",
      "definitionTechName": "Concept",
      "containerId": "root-composition-guid",
      "set": {
        "name": "New concept from GPT",
        "techName": "NewConceptFromGpt",
        "summary": "Created via JSON import"
      }
    }
  ]
}
```

Supported operations are `update`, `create`, and `delete` for `composition`, `concept`, `relationship`, and `view` where the native model can safely apply them. Concept and relationship creates require a usable definition and container. Relationship creates can include `originIdeaIds` and `targetIdeaIds`.

An additional sample file is available at `samples/json-interchange-patch.sample.json`.

## Limits

Images, attachments, styling, custom visual formatting, store-box references, and binary content are preserved in the `.tcom` file but exported as metadata or warnings only. Unsupported details are omitted from editable import behavior rather than failing export. Import does not delete by omission; deletions require explicit `delete: true` or an operation with `op: "delete"`.

For details, text-like content is exported as editable text where possible. Table details are exported as arrays of field/value records when their field metadata can be represented safely. Resource links, internal links, and attachments are exported as metadata; large binary payloads are not inlined.
