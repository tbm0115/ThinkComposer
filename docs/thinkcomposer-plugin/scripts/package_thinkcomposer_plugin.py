#!/usr/bin/env python3
"""Package the repo-local ThinkComposer Codex plugin and JSON interchange skill."""

from __future__ import annotations

import argparse
import os
import zipfile
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parent
PLUGIN_ROOT = SCRIPT_DIR.parent
DOCS_ROOT = PLUGIN_ROOT.parent
REPO_ROOT = DOCS_ROOT.parent

JSON_INTERCHANGE_SKILL_ROOT = PLUGIN_ROOT / "skills" / "thinkcomposer-json-interchange"
DEFAULT_SKILL_ZIP = DOCS_ROOT / "thinkcomposer-json-interchange.zip"
DEFAULT_PLUGIN_ZIP = DOCS_ROOT / "thinkcomposer-plugin.zip"

EXCLUDED_DIR_NAMES = {"__pycache__"}
EXCLUDED_SUFFIXES = {".pyc", ".pyo"}


def main() -> int:
    parser = argparse.ArgumentParser(description="Build ThinkComposer Codex plugin ZIP artifacts.")
    parser.add_argument("--skill-zip", default=str(DEFAULT_SKILL_ZIP), help="Output ZIP for the thinkcomposer-json-interchange skill.")
    parser.add_argument("--plugin-zip", default=str(DEFAULT_PLUGIN_ZIP), help="Output ZIP for the full ThinkComposer plugin.")
    parser.add_argument("--skill-only", action="store_true", help="Only package the thinkcomposer-json-interchange skill.")
    parser.add_argument("--plugin-only", action="store_true", help="Only package the full plugin.")
    args = parser.parse_args()

    if args.skill_only and args.plugin_only:
        raise SystemExit("Choose at most one of --skill-only or --plugin-only.")

    outputs: list[Path] = []
    if not args.plugin_only:
        outputs.append(package_directory(JSON_INTERCHANGE_SKILL_ROOT, Path(args.skill_zip), "skill"))
    if not args.skill_only:
        outputs.append(package_directory(PLUGIN_ROOT, Path(args.plugin_zip), "plugin"))

    for output in outputs:
        print(f"wrote {relative_to_repo(output)}")
    return 0


def package_directory(source_dir: Path, output_zip: Path, label: str) -> Path:
    source_dir = source_dir.resolve()
    output_zip = output_zip.resolve()
    if not source_dir.is_dir():
        raise SystemExit(f"{label} source directory does not exist: {source_dir}")

    output_zip.parent.mkdir(parents=True, exist_ok=True)
    temp_zip = output_zip.with_suffix(output_zip.suffix + ".tmp")
    if temp_zip.exists():
        temp_zip.unlink()

    with zipfile.ZipFile(temp_zip, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for file_path in iter_package_files(source_dir, output_zip):
            archive.write(file_path, file_path.relative_to(source_dir).as_posix())

    temp_zip.replace(output_zip)
    return output_zip


def iter_package_files(source_dir: Path, output_zip: Path):
    for file_path in sorted(source_dir.rglob("*"), key=lambda item: item.relative_to(source_dir).as_posix().lower()):
        if not file_path.is_file():
            continue
        if should_exclude(file_path, source_dir, output_zip):
            continue
        yield file_path


def should_exclude(file_path: Path, source_dir: Path, output_zip: Path) -> bool:
    relative = file_path.relative_to(source_dir)
    parts = set(relative.parts)
    if parts & EXCLUDED_DIR_NAMES:
        return True
    if file_path.suffix.lower() in EXCLUDED_SUFFIXES:
        return True
    if file_path.resolve() == output_zip.resolve():
        return True
    if file_path.name.endswith(".tmp"):
        return True
    return False


def relative_to_repo(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return str(path)


if __name__ == "__main__":
    raise SystemExit(main())
