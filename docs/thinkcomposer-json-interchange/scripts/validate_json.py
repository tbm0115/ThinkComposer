#!/usr/bin/env python3
"""Validate a ThinkComposer JSON Interchange document against the schema.

By default this script attempts to fetch the latest schema from the active
feature/UXImprovements branch of tbm0115/ThinkComposer. If that fails, it
falls back to the bundled schema in references/.
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

DEFAULT_SCHEMA_URL = "https://raw.githubusercontent.com/tbm0115/ThinkComposer/feature/UXImprovements/docs/thinkcomposer-json-interchange.schema.json"
ROOT = Path(__file__).resolve().parents[1]
FALLBACK_SCHEMA = ROOT / "references" / "thinkcomposer-json-interchange.schema.json"


def load_json(path: Path) -> Any:
    try:
        with path.open("r", encoding="utf-8-sig") as handle:
            return json.load(handle)
    except json.JSONDecodeError as exc:
        raise SystemExit(f"invalid json in {path}: line {exc.lineno}, column {exc.colno}: {exc.msg}") from exc
    except OSError as exc:
        raise SystemExit(f"could not read {path}: {exc}") from exc


def fetch_schema(url: str) -> Any:
    with urllib.request.urlopen(url, timeout=15) as response:
        raw = response.read().decode("utf-8")
    return json.loads(raw)


def load_schema(args: argparse.Namespace) -> tuple[Any, str]:
    if args.schema:
        path = Path(args.schema)
        return load_json(path), str(path)

    if not args.no_fetch:
        try:
            return fetch_schema(args.schema_url), args.schema_url
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, OSError) as exc:
            print(f"warning: could not fetch latest schema ({exc}); using bundled fallback", file=sys.stderr)

    return load_json(FALLBACK_SCHEMA), str(FALLBACK_SCHEMA)


def validate_with_jsonschema(instance: Any, schema: Any) -> list[str]:
    try:
        import jsonschema
    except ImportError as exc:
        raise SystemExit("missing dependency: install jsonschema to run schema validation") from exc

    validator_cls = jsonschema.validators.validator_for(schema)
    validator_cls.check_schema(schema)
    validator = validator_cls(schema)
    errors = sorted(validator.iter_errors(instance), key=lambda err: list(err.path))
    messages: list[str] = []
    for error in errors:
        location = "$" + "".join(f"[{part!r}]" if isinstance(part, int) else f".{part}" for part in error.path)
        messages.append(f"{location}: {error.message}")
    return messages


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate ThinkComposer JSON Interchange documents.")
    parser.add_argument("document", help="Path to the ThinkComposer JSON document to validate")
    parser.add_argument("--schema", help="Path to a local schema file. Overrides schema fetch and fallback.")
    parser.add_argument("--schema-url", default=DEFAULT_SCHEMA_URL, help="URL for the latest schema")
    parser.add_argument("--no-fetch", action="store_true", help="Skip fetching the latest schema and use local schema fallback")
    args = parser.parse_args()

    document_path = Path(args.document)
    instance = load_json(document_path)
    schema, schema_source = load_schema(args)
    errors = validate_with_jsonschema(instance, schema)

    if errors:
        print(f"INVALID: {document_path}")
        print(f"schema: {schema_source}")
        for message in errors:
            print(f"- {message}")
        return 1

    print(f"VALID: {document_path}")
    print(f"schema: {schema_source}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
