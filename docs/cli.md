# ThinkComposer Headless CLI

ThinkComposer now includes a console executable named `ThinkComposer.Cli.exe` for headless automation. The desktop WPF application remains unchanged for normal launches. Installer builds also deploy a `thinkcomposer.cmd` shim beside the executable.

For user-facing installation, PATH, safety, and workflow guidance, see [Command-Line Interface](user-manual/07-command-line-interface.md) in the user manual. This page is the compact technical reference for the command surface.

## Commands

```cmd
thinkcomposer composition export-json --input <file.tcom> --output <file.json>
thinkcomposer composition import-json --input <file.tcom> --json <file.json> --output <file.tcom> [--in-place] [--preview-only]
thinkcomposer composition validate-json-roundtrip --input <file.tcom> --output-dir <dir>
thinkcomposer composition convert-json-persistence --input <file.tcom> --output <file.tcom>
thinkcomposer composition validate-json-persistence --input <file.tcom> --output-dir <dir>
thinkcomposer domain export-json --input <file.tdom|file.tcom> --output <file.json>
thinkcomposer domain import-json --input <file.tdom|file.tcom> --json <file.json> --output <file.tdom|file.tcom> [--in-place] [--preview-only]
thinkcomposer domain validate-json-roundtrip --input <file.tdom|file.tcom> --output-dir <dir>
thinkcomposer domain convert-json-persistence --input <file.tdom> --output <file.tdom>
thinkcomposer domain validate-json-persistence --input <file.tdom> --output-dir <dir>
thinkcomposer package inspect --input <file.tcom|file.tdom>
thinkcomposer report pdf --input <file.tcom> --output <file.pdf|file.xps>
thinkcomposer output generate --input <file.tcom> --output-dir <dir> --language <language-tech-name> [--relationships] [--composition-root-dir] [--use-tech-names] [--exclude <idea-id-or-tech-name>]
```

Use `thinkcomposer --help`, `thinkcomposer composition --help`, or `thinkcomposer composition export-json --help` for command-line help.

## PATH Helper

If `thinkcomposer` is not available from a new Command Prompt after installation, open **Add ThinkComposer to PATH** from the ThinkComposer Start Menu folder, or run this from the install folder:

```cmd
thinkcomposer --add-to-path
```

The helper updates the machine `Path` idempotently and prompts for administrator elevation when needed. A new Command Prompt is required after the update. Related commands:

```cmd
thinkcomposer --path-status
thinkcomposer --remove-from-path
```

Pass `-User` after any helper command to update or check only the current user's `Path` instead of the machine `Path`.

## Import Safety

Imports always require `--output`. The CLI refuses to overwrite the input path unless `--in-place` is also present and `--output` matches `--input`. `--preview-only` validates the input JSON and prints the planned import summary without saving any document.

## Package Persistence

`package inspect` reports whether a `.tcom` or `.tdom` is JSON-authoritative, transitional with a binary fallback, or legacy binary-only.

`composition convert-json-persistence` and `domain convert-json-persistence` open legacy packages through the normal loader and save a modern JSON-authoritative package.

`composition validate-json-persistence` and `domain validate-json-persistence` save a JSON-authoritative package, reopen it through normal load, assert that JSON was used instead of binary fallback, save again, and compare canonical root JSON payloads.

## Exit Codes

`0` means success. `1` means usage, validation, or expected operation failure. `2` means an unexpected exception escaped the operation wrapper.

## Headless Behavior

The CLI initializes ThinkComposer services and WPF resources without setting `StartupUri`, creating `MainWindow`, or rendering the desktop shell. Report generation still uses WPF document primitives internally to produce XPS/PDF output.
