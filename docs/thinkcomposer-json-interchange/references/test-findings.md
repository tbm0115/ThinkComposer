# Skill Test Findings

Use these findings to avoid failure modes observed during real ThinkComposer JSON edit tests.

## Schema and docs

- If the user uploads a schema or docs in the current conversation, treat those as the most recent user-provided reference unless the user asks to fetch from GitHub.
- If online schema, uploaded schema, and docs disagree, validate against the schema selected for the current task and explicitly state the conflict in the response.
- Do not assume bundled references are current when the user supplies newer files.

## File loading

- ThinkComposer exports may contain a UTF-8 BOM. Read JSON with `utf-8-sig` or otherwise strip the BOM before parsing.
- Use tolerant file inspection before declaring an export invalid; a BOM parse error is not a document structure error.

## Patch construction

- Prefer patch-operation documents for edits to existing compositions. Avoid rewriting full exports unless the user explicitly asks.
- Use ids from the export when available. Use `techName` when ids are absent or unstable.
- Placeholder strings such as `existing-concept-guid` or `replace-with-root-composition-id` are examples only; they are not schema-valid UUIDs. Replace them before validation or explain that they intentionally require user replacement.
- Some exports may contain `childIdeaIds` that do not appear in the exported `ideas` array. Do not chase or repair those references unless the user asks for consistency repair. Build patches against actual exported objects and include a warning if missing references affect the requested edit.

## Relationships and placement

- Never create a relationship without resolvable origin/target links unless the user is explicitly testing linkless-relationship handling.
- Prefer top-level `originIdeaIds`/`targetIdeaIds` when both endpoints have ids; otherwise use `set.links` with `roleType` and `ideaTechName`.
- A create operation adds model data; it does not guarantee diagram visibility. Use explicit placement fields or a separate `place` operation when visibility matters.
- Do not place a relationship visual in a composite view where one endpoint is the owner of that same view; create or repair model links and warn that the unsafe visual was skipped.

## Validation behavior

- Run the bundled validator when possible. If the validator script is unavailable in the active environment, perform manual structural checks and state that schema validation was not executed.
- Validate the exact JSON artifact that will be returned, not just intermediate snippets.
- When validation fails, report the schema path and the smallest repair, then regenerate and revalidate if possible.
