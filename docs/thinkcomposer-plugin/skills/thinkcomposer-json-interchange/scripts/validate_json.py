#!/usr/bin/env python3
"""Validate a ThinkComposer JSON Interchange document against the schema.

The packaged schema in references/ is authoritative by default, keeping
validation aligned with the installed skill version. An explicit option can
check the configured upstream schema; an incompatible remote Composition
schema is rejected in favor of the packaged v2 contract. Composition and
Domain interchange documents are both supported.
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

DEFAULT_BRANCH = "feature/DcomInterchange"
COMPOSITION_SCHEMA_URL = f"https://raw.githubusercontent.com/tbm0115/ThinkComposer/{DEFAULT_BRANCH}/docs/thinkcomposer-json-interchange.schema.json"
DOMAIN_SCHEMA_URL = f"https://raw.githubusercontent.com/tbm0115/ThinkComposer/{DEFAULT_BRANCH}/docs/thinkcomposer-domain-json-interchange.schema.json"
ROOT = Path(__file__).resolve().parents[1]
COMPOSITION_FALLBACK_SCHEMA = ROOT / "references" / "thinkcomposer-json-interchange.schema.json"
DOMAIN_FALLBACK_SCHEMA = ROOT / "references" / "thinkcomposer-domain-json-interchange.schema.json"


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


def infer_schema_sources(instance: Any, args: argparse.Namespace) -> tuple[str, Path]:
    fmt = instance.get("format") if isinstance(instance, dict) else None
    if fmt == "ThinkComposer.DomainJsonInterchange":
        return args.domain_schema_url, DOMAIN_FALLBACK_SCHEMA

    return args.composition_schema_url, COMPOSITION_FALLBACK_SCHEMA


def load_schema(args: argparse.Namespace, instance: Any) -> tuple[Any, str]:
    if args.schema:
        path = Path(args.schema)
        return load_json(path), str(path)

    schema_url, fallback_schema = infer_schema_sources(instance, args)
    if args.fetch_latest and not args.no_fetch:
        try:
            fetched = fetch_schema(schema_url)
            instance_format = instance.get("format") if isinstance(instance, dict) else None
            if instance_format == "ThinkComposer.JsonInterchange" and not supports_composition_v2(fetched):
                print("warning: fetched Composition schema does not support formatVersion 2; using bundled schema", file=sys.stderr)
            else:
                return fetched, schema_url
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, OSError) as exc:
            print(f"warning: could not fetch latest schema ({exc}); using bundled fallback", file=sys.stderr)

    return load_json(fallback_schema), str(fallback_schema)


def supports_composition_v2(schema: Any) -> bool:
    try:
        version = schema["properties"]["formatVersion"]
    except (KeyError, TypeError):
        return False
    values = version.get("enum") if isinstance(version, dict) else None
    return isinstance(values, list) and 2 in values


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
    parser = argparse.ArgumentParser(description="Validate ThinkComposer Composition or Domain JSON Interchange documents.")
    parser.add_argument("document", help="Path to the ThinkComposer JSON document to validate")
    parser.add_argument("--schema", help="Path to a local schema file. Overrides schema fetch and fallback.")
    parser.add_argument("--composition-schema-url", default=COMPOSITION_SCHEMA_URL, help="URL for the latest composition schema")
    parser.add_argument("--domain-schema-url", default=DOMAIN_SCHEMA_URL, help="URL for the latest domain schema")
    parser.add_argument("--fetch-latest", action="store_true", help="Explicitly fetch the configured upstream schema before falling back to the packaged schema")
    parser.add_argument("--no-fetch", action="store_true", help="Compatibility flag that forces the packaged schema")
    args = parser.parse_args()

    document_path = Path(args.document)
    instance = load_json(document_path)
    schema, schema_source = load_schema(args, instance)
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
