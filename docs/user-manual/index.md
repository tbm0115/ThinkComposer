# ThinkComposer User Manual

This manual is the maintained Markdown source for the ThinkComposer user documentation. It migrates the legacy PDF manual into editable Markdown and updates it with the current documentation in this repository.

This source set is maintained for the `tbm0115/ThinkComposer` fork. See [Fork Maintenance](../../FORK.md) for attribution, support scope, and release policy.

The original source PDF is `Installer/InstrumindThinkComposer_Manual.pdf`, document version `1.5.13.1127`, created in November 2013. The current repository release notes identify the application as `1.5.1619`, so this manual keeps the original product model and application guide while integrating the newer JSON interchange, Domain JSON, layout, output-generation, and command-line workflows.

## Manual Map

1. [Overview](01-overview.md) introduces the product purpose, audience, and conceptual model.
2. [Base Model](02-base-model.md) explains documents, compositions, ideas, symbols, relationships, complements, domains, and definitions.
3. [Application Guide](03-application-guide.md) describes installation, the main window, diagram editing, reporting, and everyday workflows.
4. [Current Features](04-current-features.md) integrates the current Markdown docs for layout tools, JSON interchange, Domain JSON, embedded-domain updates, and output generation.
5. [Command-Line Interface](07-command-line-interface.md) explains headless automation, PATH setup, import/export safety, reports, and output generation from Command Prompt.
6. [Template Language](05-template-language.md) documents Output Template control markup, Liquid markup, filters, tags, and ThinkComposer-specific helpers.
7. [Composition Information Model](06-information-model.md) summarizes the model exposed to Output Templates.

## Source And Assets

Content figures were extracted from the canonical PDF into `assets/manual/`. The extraction skips repeated page headers/footers and tiny page furniture. The extraction manifest is maintained at [assets/manual/manifest.json](assets/manual/manifest.json).

Brand assets are copied from existing repository files into `assets/brand/` so the Markdown and Pandoc theme do not depend on application runtime paths.

## Legacy PDF Coverage

The refreshed chapters preserve the original PDF table of contents while reorganizing wording for current use.

| Legacy PDF section | Refreshed location |
|---|---|
| Overview; Context; Vision | [Overview](01-overview.md) |
| Base Model; Working Documents; Common Objects Properties; File Types | [Base Model](02-base-model.md) |
| Compositions; Views; Ideas; Symbols; Shortcuts | [Base Model](02-base-model.md) |
| Details; Attachments; Links; Tables; Custom-Fields; Detail Designations | [Base Model](02-base-model.md) |
| Concepts; Relationships; Directionality; Relationship Links; Connectors; Link-Roles | [Base Model](02-base-model.md) |
| Markers; Complements; Legend; Info-Card; Image; Text; Stamp; Note; Callout; Quote; Group Region; Group Line | [Base Model](02-base-model.md) |
| Domains; Idea Definitions; Properties; Brushes; Text-Formats; Symbol Format Definition; Details Definitions; Output-Templates | [Base Model](02-base-model.md) |
| Concept Definitions; Relationship Definitions; Link-Role Definitions; Connectors Format Definition; Variant Definitions; Marker Definitions; Table-Structure Definitions; Base Tables; External Languages | [Base Model](02-base-model.md) |
| Application Guide; Setup; Requirements; Install and Uninstall; Version Update; License Activation; User Interface; Main Window | [Application Guide](03-application-guide.md) |
| Working with Compositions; Working with diagram Views; Editing Symbols; Creating Concepts; Creating Relationships | [Application Guide](03-application-guide.md) |
| Extending or Modifying Relationships; Converting Ideas; Assigning Markers to Ideas; Creating Complements; Creating Shortcuts; Selection, Pan and Zoom; Reporting; Composition's Report | [Application Guide](03-application-guide.md) |
| Headless command-line automation; PATH helper; scripted JSON import/export, reporting, and output generation | [Command-Line Interface](07-command-line-interface.md) |
| Appendix A: Template language; Control markup; Output markup; Filters; Tag markup | [Template Language](05-template-language.md) |
| Appendix B: Composition Information Model; Classes Diagrams; Associations; Inheritance Hierarchy; Special cases; Specification of Model Classes | [Composition Information Model](06-information-model.md) |

## Building The PDF

Run the build script from this directory or from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File docs/user-manual/build.ps1
```

The script writes:

```text
docs/user-manual/output/ThinkComposer_User_Manual.pdf
```

The PDF build uses the chapter files directly and does not include this `index.md` source guide. This keeps maintainer notes, source coverage notes, and build instructions out of the end-user PDF.

Pandoc is required. The configured PDF engine is `xelatex`; if it is not installed, the script stops with an actionable dependency message.

## Maintenance Notes

- Keep Markdown usable on GitHub; do not make the PDF the only readable artifact.
- Keep user-facing guidance in this manual and implementation/regression details in the technical docs under `docs/`.
- When current features change, update both the detailed topic doc and the relevant section in [Current Features](04-current-features.md).
- When replacing screenshots, use stable descriptive filenames and update the manifest or note the source.
