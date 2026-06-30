#!/usr/bin/env node

import fs from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";
import os from "node:os";
import { Buffer } from "node:buffer";
import { inflateRawSync } from "node:zlib";
import { fileURLToPath } from "node:url";

const SERVER_NAME = "thinkcomposer";
const SERVER_VERSION = "0.1.0";
const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const PLUGIN_ROOT = path.resolve(SCRIPT_DIR, "..");
const DEFAULT_ROOT = process.env.THINKCOMPOSER_ROOT || findThinkComposerRoot(PLUGIN_ROOT) || process.cwd();
const IMAGE_EXTENSIONS = new Set([".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".xps", ".pdf"]);
const LOG_EXTENSIONS = new Set([".log", ".txt"]);
const CONTAINER_EXTENSIONS = new Set([".tcom"]);
const SKIP_DIRS = new Set([".git", ".vs", "bin", "obj", "node_modules", "packages", ".cache", "TestResults"]);

const tools = [
  {
    name: "thinkcomposer_discover",
    description: "Find recent ThinkComposer .tcom containers, JSON, image, and log artifacts under a root directory.",
    inputSchema: {
      type: "object",
      properties: {
        root: { type: "string", description: "Directory to scan. Defaults to THINKCOMPOSER_ROOT or the server cwd." },
        limit: { type: "integer", minimum: 1, maximum: 50, default: 10 },
        maxDepth: { type: "integer", minimum: 1, maximum: 12, default: 6 }
      }
    }
  },
  {
    name: "thinkcomposer_read_json_summary",
    description: "Read a Composition JSON, Domain JSON, or modern .tcom container and return high-signal counts and metadata.",
    inputSchema: {
      type: "object",
      required: ["path"],
      properties: {
        path: { type: "string" }
      }
    }
  },
  {
    name: "thinkcomposer_read_container_summary",
    description: "Read a modern .tcom container manifest plus embedded interchange JSON and preview screenshot metadata.",
    inputSchema: {
      type: "object",
      required: ["path"],
      properties: {
        path: { type: "string" },
        includeEntries: { type: "boolean", default: false }
      }
    }
  },
  {
    name: "thinkcomposer_validate_json",
    description: "Parse and lightly validate a ThinkComposer JSON interchange file or embedded JSON parts in a modern .tcom container.",
    inputSchema: {
      type: "object",
      required: ["path"],
      properties: {
        path: { type: "string" },
        kind: { type: "string", enum: ["composition", "domain", "auto"], default: "auto" }
      }
    }
  },
  {
    name: "thinkcomposer_write_patch",
    description: "Write a safe patch-style Composition JSON or Domain JSON file for the user to import in ThinkComposer.",
    inputSchema: {
      type: "object",
      required: ["path", "kind", "operations"],
      properties: {
        path: { type: "string" },
        kind: { type: "string", enum: ["composition", "domain"] },
        operations: { type: "array", items: { type: "object" } },
        importOptions: { type: "object" },
        visualStrategy: { type: "object" },
        warnings: { type: "array", items: { type: "string" } },
        extra: { type: "object", description: "Additional top-level JSON fields to merge into the patch." },
        overwrite: { type: "boolean", default: false }
      }
    }
  },
  {
    name: "thinkcomposer_extract_container_artifacts",
    description: "Extract embedded interchange JSON and preview screenshots from a modern .tcom container into a working directory.",
    inputSchema: {
      type: "object",
      required: ["path", "outputDir"],
      properties: {
        path: { type: "string" },
        outputDir: { type: "string" },
        includeJson: { type: "boolean", default: true },
        includePreviews: { type: "boolean", default: true },
        viewTechName: { type: "string", description: "Optional single viewTechName to extract from Previews/views." },
        overwrite: { type: "boolean", default: false }
      }
    }
  },
  {
    name: "thinkcomposer_analyze_log",
    description: "Analyze copied ThinkComposer application log text or a log file for warnings, skipped items, failures, summaries, and exported image paths.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string" },
        text: { type: "string" },
        tailLines: { type: "integer", minimum: 1, maximum: 2000, default: 400 }
      }
    }
  },
  {
    name: "thinkcomposer_latest_image",
    description: "Find the most recent exported ThinkComposer view images under a root directory.",
    inputSchema: {
      type: "object",
      properties: {
        root: { type: "string", description: "Directory to scan. Defaults to THINKCOMPOSER_ROOT or the server cwd." },
        limit: { type: "integer", minimum: 1, maximum: 25, default: 5 },
        maxDepth: { type: "integer", minimum: 1, maximum: 12, default: 6 }
      }
    }
  }
];

async function callTool(name, args = {}) {
  switch (name) {
    case "thinkcomposer_discover":
      return discover(args);
    case "thinkcomposer_read_json_summary":
      return summarizeJsonFile(args.path);
    case "thinkcomposer_read_container_summary":
      return summarizeContainerFile(args.path, { includeEntries: args.includeEntries === true });
    case "thinkcomposer_validate_json":
      return validateJsonFile(args.path, args.kind || "auto");
    case "thinkcomposer_write_patch":
      return writePatch(args);
    case "thinkcomposer_extract_container_artifacts":
      return extractContainerArtifacts(args);
    case "thinkcomposer_analyze_log":
      return analyzeLog(args);
    case "thinkcomposer_latest_image":
      return latestImage(args);
    default:
      throw new Error(`Unknown tool: ${name}`);
  }
}

async function discover(args) {
  const root = await resolveDirectory(args.root || DEFAULT_ROOT);
  const limit = clampInt(args.limit, 10, 1, 50);
  const maxDepth = clampInt(args.maxDepth, 6, 1, 12);
  const files = await walk(root, maxDepth);
  const containers = [];
  const jsonCandidates = [];
  const images = [];
  const logs = [];

  for (const file of files) {
    const ext = path.extname(file).toLowerCase();
    if (CONTAINER_EXTENSIONS.has(ext)) {
      const summary = await trySummarizeContainerFile(file, { includeEmbedded: false });
      if (summary) {
        containers.push(summary);
        for (const preview of summary.previews || []) {
          if (!preview.skipped && preview.partUri) {
            images.push({
              kind: "containerPreview",
              path: file,
              containerPartUri: preview.partUri,
              viewName: preview.viewName || null,
              viewTechName: preview.viewTechName || null,
              width: preview.width || null,
              height: preview.height || null,
              mtimeUtc: summary.mtimeUtc,
              sizeBytes: preview.bytes || 0
            });
          }
        }
      }
      continue;
    }
    if (ext === ".json") {
      const summary = await trySummarizeJsonFile(file);
      if (summary && summary.format && summary.format.startsWith("ThinkComposer.")) {
        jsonCandidates.push({ path: file, ...summary });
      } else if (/\.(tc|tdom)\.json$/i.test(file)) {
        jsonCandidates.push({ path: file, format: "unknown", mtimeUtc: await mtimeUtc(file) });
      }
      continue;
    }
    if (IMAGE_EXTENSIONS.has(ext)) {
      images.push(await fileRecord(file));
      continue;
    }
    if (LOG_EXTENSIONS.has(ext)) {
      const sample = await readFilePrefix(file, 8192);
      if (/JSON import|JSON export|Domain JSON|Embedded Domain|successfully exported|ThinkComposer/i.test(sample)) {
        logs.push(await fileRecord(file));
      }
    }
  }

  containers.sort(newestFirst);
  jsonCandidates.sort(newestFirst);
  images.sort(newestFirst);
  logs.sort(newestFirst);
  return {
    root,
    containers: containers.slice(0, limit),
    compositionJson: jsonCandidates.filter((item) => item.format === "ThinkComposer.JsonInterchange").slice(0, limit),
    domainJson: jsonCandidates.filter((item) => item.format === "ThinkComposer.DomainJsonInterchange").slice(0, limit),
    otherJson: jsonCandidates.filter((item) => !["ThinkComposer.JsonInterchange", "ThinkComposer.DomainJsonInterchange"].includes(item.format)).slice(0, limit),
    images: images.slice(0, limit),
    logs: logs.slice(0, limit)
  };
}

async function summarizeJsonFile(rawPath) {
  const file = await resolveFile(rawPath);
  if (CONTAINER_EXTENSIONS.has(path.extname(file).toLowerCase())) {
    return summarizeContainer(file, { includeEmbedded: true });
  }
  const document = await readJson(file);
  return { path: file, ...(await summarizeJson(document, file)) };
}

async function trySummarizeJsonFile(file) {
  try {
    const document = await readJson(file);
    return await summarizeJson(document, file);
  } catch {
    return null;
  }
}

async function summarizeJson(document, file) {
  const stats = await fs.stat(file);
  return summarizeJsonDocument(document, {
    mtimeUtc: stats.mtime.toISOString(),
    sizeBytes: stats.size
  });
}

function summarizeJsonDocument(document, sourceInfo) {
  const operations = Array.isArray(document.operations) ? document.operations : [];
  const operationGroups = {};
  for (const operation of operations) {
    const key = `${operation?.op || "<missing-op>"}/${operation?.entity || "<missing-entity>"}`;
    operationGroups[key] = (operationGroups[key] || 0) + 1;
  }

  const summary = {
    format: stringOrNull(document.format),
    formatVersion: document.formatVersion ?? null,
    mtimeUtc: sourceInfo.mtimeUtc,
    sizeBytes: sourceInfo.sizeBytes,
    warningsCount: Array.isArray(document.warnings) ? document.warnings.length : 0,
    operationsCount: operations.length,
    operationGroups
  };

  if (document.format === "ThinkComposer.JsonInterchange") {
    summary.composition = pick(document.composition, ["id", "name", "techName", "summary", "activeViewId", "rootViewId", "viewsPrefix"]);
    summary.counts = {
      definitions: count(document.definitions),
      ideas: count(document.ideas),
      relationships: count(document.relationships),
      views: count(document.views),
      operations: operations.length
    };
    summary.importOptions = document.importOptions || null;
    summary.visualStrategy = document.visualStrategy || null;
    summary.requires = document.requires || null;
  } else if (document.format === "ThinkComposer.DomainJsonInterchange") {
    summary.domain = pick(document.domain, ["id", "name", "techName", "summary", "version", "compatibilitySignature"]);
    summary.counts = {
      externalLanguages: count(document.externalLanguages),
      conceptDefinitions: count(document.conceptDefinitions),
      relationshipDefinitions: count(document.relationshipDefinitions),
      tableDefinitions: count(document.tableDefinitions),
      fieldDefinitions: count(document.fieldDefinitions),
      outputTemplates: count(document.outputTemplates) + count(document.conceptDefinitionOutputTemplates) + count(document.relationshipDefinitionOutputTemplates),
      operations: operations.length
    };
    summary.relationshipCompatibilityCount = count(document.relationshipCompatibility);
  }

  return summary;
}

async function summarizeContainerFile(rawPath, options = {}) {
  const file = await resolveFile(rawPath);
  return summarizeContainer(file, options);
}

async function trySummarizeContainerFile(file, options = {}) {
  try {
    return await summarizeContainer(file, options);
  } catch {
    return null;
  }
}

async function summarizeContainer(file, options = {}) {
  const stats = await fs.stat(file);
  const zip = await readZip(file);
  const manifest = await readContainerManifest(zip);
  const jsonParts = Array.isArray(manifest.jsonParts) ? manifest.jsonParts : [];
  const previews = Array.isArray(manifest.previews) ? manifest.previews : [];
  const embedded = {};

  if (options.includeEmbedded !== false) {
    for (const part of jsonParts) {
      const partName = normalizePartUri(part.partUri);
      const entry = findZipEntry(zip, partName);
      if (!entry) continue;
      const document = JSON.parse(readZipEntry(zip, entry).toString("utf8"));
      const key = part.kind === "embeddedDomain" ? "domain" : part.kind || path.basename(partName, ".json");
      embedded[key] = {
        partUri: part.partUri,
        sha256: part.sha256 || null,
        ...(summarizeJsonDocument(document, {
          mtimeUtc: stats.mtime.toISOString(),
          sizeBytes: entry.uncompressedSize
        }))
      };
    }
  }

  const summary = {
    path: file,
    kind: "container",
    format: manifest.format || null,
    formatVersion: manifest.formatVersion ?? null,
    packageKind: manifest.packageKind || null,
    generatedAtUtc: manifest.generatedAtUtc || null,
    application: manifest.application || null,
    applicationVersion: manifest.applicationVersion || null,
    mtimeUtc: stats.mtime.toISOString(),
    sizeBytes: stats.size,
    source: manifest.source || null,
    nativePartUri: manifest.nativePartUri || null,
    nativePartSha256: manifest.nativePartSha256 || null,
    counts: {
      entries: zip.entries.length,
      jsonParts: jsonParts.length,
      previews: previews.length,
      renderablePreviews: previews.filter((preview) => !preview.skipped && preview.partUri).length,
      skippedPreviews: previews.filter((preview) => preview.skipped).length,
      warnings: Array.isArray(manifest.warnings) ? manifest.warnings.length : 0
    },
    jsonParts,
    previews,
    warnings: manifest.warnings || [],
    embedded
  };

  if (options.includeEntries) {
    summary.entries = zip.entries.map((entry) => ({
      name: entry.name,
      method: entry.method,
      compressedSize: entry.compressedSize,
      uncompressedSize: entry.uncompressedSize
    }));
  }

  return summary;
}

async function validateContainerFile(file) {
  const errors = [];
  const warnings = [];
  let summary = null;

  try {
    summary = await summarizeContainer(file, { includeEmbedded: true, includeEntries: true });
  } catch (error) {
    return {
      path: file,
      kind: "container",
      ok: false,
      errors: [error instanceof Error ? error.message : String(error)],
      warnings,
      summary: null
    };
  }

  if (summary.format !== "ThinkComposer.ContainerSnapshot") {
    errors.push("Container manifest format must be ThinkComposer.ContainerSnapshot.");
  }
  if (summary.formatVersion !== 1) {
    errors.push("Container manifest formatVersion must be 1.");
  }
  if (!summary.embedded.composition) {
    errors.push("Container is missing embedded composition JSON.");
  }
  if (!summary.embedded.domain) {
    warnings.push("Container has no embedded Domain JSON part.");
  }
  for (const [key, embedded] of Object.entries(summary.embedded)) {
    if (key === "composition" && embedded.format !== "ThinkComposer.JsonInterchange") {
      errors.push("Embedded composition JSON format must be ThinkComposer.JsonInterchange.");
    }
    if (key === "domain" && embedded.format !== "ThinkComposer.DomainJsonInterchange") {
      errors.push("Embedded Domain JSON format must be ThinkComposer.DomainJsonInterchange.");
    }
    if (embedded.formatVersion !== 1) {
      errors.push(`Embedded ${key} JSON formatVersion must be 1.`);
    }
  }
  if (summary.counts.renderablePreviews < 1) {
    warnings.push("Container has no renderable preview screenshots.");
  }
  const entryNames = new Set((summary.entries || []).map((entry) => entry.name));
  for (const preview of summary.previews) {
    if (!preview.skipped && preview.partUri && !entryNames.has(normalizePartUri(preview.partUri))) {
      errors.push(`Preview part is listed but missing: ${preview.partUri}`);
    }
  }

  return {
    path: file,
    kind: "container",
    ok: errors.length === 0,
    errors,
    warnings,
    summary,
    importMenu: "Open the .tcom in ThinkComposer for native editing; use embedded JSON/previews as Codex context and write JSON patches separately."
  };
}

async function extractContainerArtifacts(args) {
  const file = await resolveFile(args.path);
  const zip = await readZip(file);
  const manifest = await readContainerManifest(zip);
  const outputDir = path.resolve(expandHome(String(args.outputDir)));
  const includeJson = args.includeJson !== false;
  const includePreviews = args.includePreviews !== false;
  const overwrite = args.overwrite === true;
  const extracted = [];
  const entriesToExtract = new Map();

  entriesToExtract.set("Interchange/manifest.json", "Interchange/manifest.json");

  if (includeJson && Array.isArray(manifest.jsonParts)) {
    for (const part of manifest.jsonParts) {
      const partName = normalizePartUri(part.partUri);
      if (partName) entriesToExtract.set(partName, partName);
    }
  }

  if (includePreviews && Array.isArray(manifest.previews)) {
    for (const preview of manifest.previews) {
      if (preview.skipped || !preview.partUri) continue;
      if (args.viewTechName && preview.viewTechName !== args.viewTechName) continue;
      const partName = normalizePartUri(preview.partUri);
      if (partName) entriesToExtract.set(partName, partName);
    }
  }

  await fs.mkdir(outputDir, { recursive: true });
  for (const [partName, relativeTarget] of entriesToExtract) {
    const entry = findZipEntry(zip, partName);
    if (!entry) continue;
    const target = safeExtractPath(outputDir, relativeTarget);
    await fs.mkdir(path.dirname(target), { recursive: true });
    if (!overwrite && await exists(target)) {
      throw new Error(`Refusing to overwrite existing extracted artifact: ${target}`);
    }
    const data = readZipEntry(zip, entry);
    await fs.writeFile(target, data);
    extracted.push({
      partUri: `/${partName}`,
      path: target,
      bytes: data.length
    });
  }

  return {
    source: file,
    outputDir,
    extracted
  };
}

async function validateJsonFile(rawPath, kind) {
  const file = await resolveFile(rawPath);
  if (CONTAINER_EXTENSIONS.has(path.extname(file).toLowerCase())) {
    return validateContainerFile(file);
  }
  const summary = await summarizeJsonFile(rawPath);
  const document = await readJson(summary.path);
  const errors = [];
  const warnings = [];
  const detectedKind = summary.format === "ThinkComposer.DomainJsonInterchange"
    ? "domain"
    : summary.format === "ThinkComposer.JsonInterchange"
      ? "composition"
      : "unknown";

  if (kind !== "auto" && detectedKind !== "unknown" && kind !== detectedKind) {
    errors.push(`Expected ${kind} JSON but file format is ${summary.format}.`);
  }
  if (detectedKind === "unknown") {
    errors.push("Missing or unsupported ThinkComposer format field.");
  }
  if (document.formatVersion !== 1) {
    errors.push("formatVersion must be 1.");
  }
  if (document.operations !== undefined && !Array.isArray(document.operations)) {
    errors.push("operations must be an array when present.");
  }

  const operations = Array.isArray(document.operations) ? document.operations : [];
  operations.forEach((operation, index) => {
    const label = `operations[${index}]`;
    if (!operation || typeof operation !== "object" || Array.isArray(operation)) {
      errors.push(`${label} must be an object.`);
      return;
    }
    if (!isNonEmptyString(operation.op)) {
      errors.push(`${label}.op is required.`);
    }
    if (!isNonEmptyString(operation.entity)) {
      errors.push(`${label}.entity is required.`);
    }
    if (operation.op === "delete" || operation.delete === true) {
      warnings.push(`${label} requests deletion; ThinkComposer skips or requires explicit confirmation for dangerous changes.`);
    }
    if (detectedKind === "composition") {
      validateCompositionOperation(operation, label, errors, warnings);
    }
    if (detectedKind === "domain") {
      validateDomainOperation(operation, label, warnings);
    }
  });

  if (detectedKind === "composition" && Array.isArray(document.ideas) && document.ideas.length > 0) {
    const creates = document.importOptions?.treatMissingFullStateItemsAsCreates === true;
    if (!creates && operations.length === 0) {
      warnings.push("Full-state ideas are update/merge context by default; set importOptions.treatMissingFullStateItemsAsCreates=true only when creating missing top-level items is intended.");
    }
  }

  return {
    path: summary.path,
    kind: detectedKind,
    ok: errors.length === 0,
    errors,
    warnings,
    summary,
    importMenu: detectedKind === "domain" ? "Domain > Import/Update Domain JSON..." : "Composition > File > Import JSON..."
  };
}

function validateCompositionOperation(operation, label, errors, warnings) {
  if (operation.op === "create" && operation.entity === "concept") {
    if (!isNonEmptyString(operation.definitionTechName)) {
      errors.push(`${label} create concept needs definitionTechName.`);
    }
    if (!hasAny(operation, ["containerId", "containerTechName"])) {
      warnings.push(`${label} create concept has no containerId/containerTechName. Use Active_Composition_Root with useActiveCompositionAsContainer=true for root-level patches.`);
    }
  }
  if (operation.op === "create" && operation.entity === "relationship") {
    if (!isNonEmptyString(operation.definitionTechName)) {
      errors.push(`${label} create relationship needs definitionTechName.`);
    }
    const links = Array.isArray(operation.links) ? operation.links : Array.isArray(operation.set?.links) ? operation.set.links : null;
    const hasEndpointArrays = hasAny(operation, ["originIdeaIds", "originIdeaTechNames", "targetIdeaIds", "targetIdeaTechNames"]) ||
      hasAny(operation.set || {}, ["originIdeaIds", "originIdeaTechNames", "targetIdeaIds", "targetIdeaTechNames"]);
    if (!links && !hasEndpointArrays) {
      errors.push(`${label} create relationship needs origin/target links.`);
    }
  }
  if (operation.entity === "relationship" && operation.visual?.relationshipCenterPlacement === "explicit") {
    warnings.push(`${label} preserves explicit relationship-center placement; endpointCorridor or auto is usually better for generated diagrams.`);
  }
}

function validateDomainOperation(operation, label, warnings) {
  if (operation.entity === "outputTemplate" && operation.set?.templateText) {
    warnings.push(`${label} imports output template text. ThinkComposer treats it as text during import; preview generated output before use.`);
  }
}

async function writePatch(args) {
  const kind = args.kind;
  if (!["composition", "domain"].includes(kind)) {
    throw new Error("kind must be composition or domain.");
  }
  if (!Array.isArray(args.operations)) {
    throw new Error("operations must be an array.");
  }

  const target = path.resolve(String(args.path));
  if (path.extname(target).toLowerCase() !== ".json") {
    throw new Error("Patch path must end in .json.");
  }
  await fs.mkdir(path.dirname(target), { recursive: true });
  if (!args.overwrite && await exists(target)) {
    throw new Error(`Refusing to overwrite existing file: ${target}`);
  }

  const document = {
    format: kind === "domain" ? "ThinkComposer.DomainJsonInterchange" : "ThinkComposer.JsonInterchange",
    formatVersion: 1,
    ...(kind === "composition" ? { application: "ThinkComposer" } : {}),
    ...(args.extra && typeof args.extra === "object" && !Array.isArray(args.extra) ? args.extra : {}),
    ...(args.importOptions ? { importOptions: args.importOptions } : {}),
    ...(args.visualStrategy ? { visualStrategy: args.visualStrategy } : {}),
    ...(Array.isArray(args.warnings) ? { warnings: args.warnings } : {}),
    operations: args.operations
  };

  await fs.writeFile(target, `${JSON.stringify(document, null, 2)}\n`, "utf8");
  const validation = await validateJsonFile(target, kind);
  return {
    path: target,
    bytesWritten: Buffer.byteLength(JSON.stringify(document, null, 2), "utf8") + 1,
    validation,
    importMenu: kind === "domain" ? "Domain > Import/Update Domain JSON..." : "Composition > File > Import JSON..."
  };
}

async function analyzeLog(args) {
  let text = args.text;
  if (!text && args.path) {
    const file = await resolveFile(args.path);
    text = await fs.readFile(file, "utf8");
  }
  if (!text) {
    throw new Error("Provide either path or text.");
  }

  const tailLines = clampInt(args.tailLines, 400, 1, 2000);
  const lines = text.split(/\r?\n/).filter(Boolean).slice(-tailLines);
  const groups = {
    errors: [],
    warnings: [],
    skipped: [],
    blocked: [],
    rollback: [],
    summaries: [],
    exports: [],
    affectedViews: []
  };
  const exportedPaths = [];

  for (const line of lines) {
    const lower = line.toLowerCase();
    if (/(error|failed|exception|cannot import|cannot export)/i.test(line)) groups.errors.push(line);
    if (/warning/i.test(line)) groups.warnings.push(line);
    if (/skipped|dangerous skipped/i.test(line)) groups.skipped.push(line);
    if (/blocked|compatibility failure|compatibility policy/i.test(line)) groups.blocked.push(line);
    if (/rollback|discarded|undo/i.test(line)) groups.rollback.push(line);
    if (/summary|completed|succeeded|applied summary|planned summary/i.test(line)) groups.summaries.push(line);
    if (/successfully exported to|export succeeded|exported to/i.test(line)) groups.exports.push(line);
    if (/affected view/i.test(lower)) groups.affectedViews.push(line);
    const match = line.match(/(?:successfully exported to|export succeeded:|exported to:?)\s+'?([^']+\.(?:png|jpg|jpeg|bmp|gif|tif|tiff|webp|xps|pdf))'?/i);
    if (match) exportedPaths.push(match[1].trim());
  }

  return {
    lineCount: lines.length,
    counts: Object.fromEntries(Object.entries(groups).map(([key, value]) => [key, value.length])),
    groups: Object.fromEntries(Object.entries(groups).map(([key, value]) => [key, value.slice(-20)])),
    exportedPaths: [...new Set(exportedPaths)]
  };
}

async function latestImage(args) {
  const root = await resolveDirectory(args.root || DEFAULT_ROOT);
  const limit = clampInt(args.limit, 5, 1, 25);
  const maxDepth = clampInt(args.maxDepth, 6, 1, 12);
  const files = await walk(root, maxDepth);
  const images = [];
  for (const file of files) {
    const ext = path.extname(file).toLowerCase();
    if (IMAGE_EXTENSIONS.has(ext)) {
      images.push(await fileRecord(file));
    } else if (CONTAINER_EXTENSIONS.has(ext)) {
      const summary = await trySummarizeContainerFile(file, { includeEmbedded: false });
      if (!summary) continue;
      for (const preview of summary.previews || []) {
        if (preview.skipped || !preview.partUri) continue;
        images.push({
          kind: "containerPreview",
          path: file,
          containerPartUri: preview.partUri,
          viewName: preview.viewName || null,
          viewTechName: preview.viewTechName || null,
          width: preview.width || null,
          height: preview.height || null,
          mtimeUtc: summary.mtimeUtc,
          sizeBytes: preview.bytes || 0
        });
      }
    }
  }
  images.sort(newestFirst);
  return { root, images: images.slice(0, limit) };
}

async function walk(root, maxDepth) {
  const results = [];
  async function visit(dir, depth) {
    if (depth > maxDepth) return;
    let entries;
    try {
      entries = await fs.readdir(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (!SKIP_DIRS.has(entry.name)) await visit(fullPath, depth + 1);
      } else if (entry.isFile()) {
        results.push(fullPath);
      }
    }
  }
  await visit(root, 0);
  return results;
}

async function readJson(file) {
  const text = await fs.readFile(file, "utf8");
  return JSON.parse(text);
}

async function readZip(file) {
  const buffer = await fs.readFile(file);
  const eocdOffset = findEndOfCentralDirectory(buffer);
  const totalEntries = buffer.readUInt16LE(eocdOffset + 10);
  const centralDirectoryOffset = buffer.readUInt32LE(eocdOffset + 16);
  const entries = [];
  let offset = centralDirectoryOffset;

  for (let index = 0; index < totalEntries; index += 1) {
    if (buffer.readUInt32LE(offset) !== 0x02014b50) {
      throw new Error("Invalid ZIP central directory entry.");
    }
    const method = buffer.readUInt16LE(offset + 10);
    const compressedSize = buffer.readUInt32LE(offset + 20);
    const uncompressedSize = buffer.readUInt32LE(offset + 24);
    const nameLength = buffer.readUInt16LE(offset + 28);
    const extraLength = buffer.readUInt16LE(offset + 30);
    const commentLength = buffer.readUInt16LE(offset + 32);
    const localHeaderOffset = buffer.readUInt32LE(offset + 42);
    const name = buffer.toString("utf8", offset + 46, offset + 46 + nameLength).replace(/\\/g, "/");
    entries.push({
      name,
      method,
      compressedSize,
      uncompressedSize,
      localHeaderOffset
    });
    offset += 46 + nameLength + extraLength + commentLength;
  }

  return { buffer, entries };
}

function findEndOfCentralDirectory(buffer) {
  const minimumOffset = Math.max(0, buffer.length - 0xffff - 22);
  for (let offset = buffer.length - 22; offset >= minimumOffset; offset -= 1) {
    if (buffer.readUInt32LE(offset) === 0x06054b50) {
      return offset;
    }
  }
  throw new Error("Not a readable ZIP container.");
}

async function readContainerManifest(zip) {
  const entry = findZipEntry(zip, "Interchange/manifest.json");
  if (!entry) {
    throw new Error("Missing Interchange/manifest.json in .tcom container.");
  }
  return JSON.parse(readZipEntry(zip, entry).toString("utf8"));
}

function findZipEntry(zip, rawName) {
  const normalized = normalizePartUri(rawName);
  return zip.entries.find((entry) => entry.name === normalized) || null;
}

function readZipEntry(zip, entry) {
  const buffer = zip.buffer;
  const offset = entry.localHeaderOffset;
  if (buffer.readUInt32LE(offset) !== 0x04034b50) {
    throw new Error(`Invalid local ZIP header for ${entry.name}.`);
  }
  const nameLength = buffer.readUInt16LE(offset + 26);
  const extraLength = buffer.readUInt16LE(offset + 28);
  const dataOffset = offset + 30 + nameLength + extraLength;
  const compressed = buffer.subarray(dataOffset, dataOffset + entry.compressedSize);
  if (entry.method === 0) return Buffer.from(compressed);
  if (entry.method === 8) return inflateRawSync(compressed);
  throw new Error(`Unsupported ZIP compression method ${entry.method} for ${entry.name}.`);
}

function normalizePartUri(value) {
  return String(value || "").replace(/\\/g, "/").replace(/^\/+/, "");
}

async function resolveDirectory(rawPath) {
  const resolved = path.resolve(expandHome(String(rawPath)));
  const stats = await fs.stat(resolved);
  if (!stats.isDirectory()) throw new Error(`Not a directory: ${resolved}`);
  return resolved;
}

async function resolveFile(rawPath) {
  const resolved = path.resolve(expandHome(String(rawPath)));
  const stats = await fs.stat(resolved);
  if (!stats.isFile()) throw new Error(`Not a file: ${resolved}`);
  return resolved;
}

function expandHome(value) {
  if (value === "~") return os.homedir();
  if (value.startsWith("~/") || value.startsWith("~\\")) return path.join(os.homedir(), value.slice(2));
  return value;
}

function findThinkComposerRoot(startDir) {
  let current = path.resolve(startDir);
  while (true) {
    if (
      existsSync(path.join(current, "ThinkComposer", "ThinkComposer.csproj")) &&
      existsSync(path.join(current, "docs", "json-interchange.md"))
    ) {
      return current;
    }
    const parent = path.dirname(current);
    if (parent === current) return null;
    current = parent;
  }
}

function safeExtractPath(outputDir, relativeTarget) {
  const root = path.resolve(outputDir);
  const normalized = normalizePartUri(relativeTarget);
  if (!normalized || normalized.split("/").some((part) => part === ".." || part === "")) {
    throw new Error(`Unsafe container part path: ${relativeTarget}`);
  }
  const target = path.resolve(root, normalized);
  const rootWithSeparator = root.endsWith(path.sep) ? root : `${root}${path.sep}`;
  if (target !== root && !target.startsWith(rootWithSeparator)) {
    throw new Error(`Refusing to extract outside outputDir: ${relativeTarget}`);
  }
  return target;
}

async function fileRecord(file) {
  const stats = await fs.stat(file);
  return {
    path: file,
    mtimeUtc: stats.mtime.toISOString(),
    sizeBytes: stats.size
  };
}

async function mtimeUtc(file) {
  return (await fs.stat(file)).mtime.toISOString();
}

async function readFilePrefix(file, bytes) {
  const handle = await fs.open(file, "r");
  try {
    const buffer = Buffer.alloc(bytes);
    const result = await handle.read(buffer, 0, bytes, 0);
    return buffer.subarray(0, result.bytesRead).toString("utf8");
  } finally {
    await handle.close();
  }
}

async function exists(file) {
  try {
    await fs.access(file);
    return true;
  } catch {
    return false;
  }
}

function newestFirst(left, right) {
  return String(right.mtimeUtc || "").localeCompare(String(left.mtimeUtc || ""));
}

function clampInt(value, fallback, min, max) {
  const parsed = Number.parseInt(value, 10);
  if (Number.isNaN(parsed)) return fallback;
  return Math.min(max, Math.max(min, parsed));
}

function count(value) {
  return Array.isArray(value) ? value.length : 0;
}

function pick(source, keys) {
  if (!source || typeof source !== "object") return null;
  const result = {};
  for (const key of keys) {
    if (source[key] !== undefined) result[key] = source[key];
  }
  return result;
}

function hasAny(source, keys) {
  return keys.some((key) => source[key] !== undefined && source[key] !== null);
}

function isNonEmptyString(value) {
  return typeof value === "string" && value.trim().length > 0;
}

function stringOrNull(value) {
  return typeof value === "string" ? value : null;
}

function textResult(data) {
  return {
    content: [
      {
        type: "text",
        text: JSON.stringify(data, null, 2)
      }
    ]
  };
}

async function handleMessage(message, mode) {
  if (!message || message.jsonrpc !== "2.0") return;
  if (!("id" in message)) return;

  try {
    if (message.method === "initialize") {
      send({
        jsonrpc: "2.0",
        id: message.id,
        result: {
          protocolVersion: message.params?.protocolVersion || "2024-11-05",
          capabilities: { tools: {} },
          serverInfo: { name: SERVER_NAME, version: SERVER_VERSION }
        }
      }, mode);
      return;
    }

    if (message.method === "tools/list") {
      send({ jsonrpc: "2.0", id: message.id, result: { tools } }, mode);
      return;
    }

    if (message.method === "tools/call") {
      const result = await callTool(message.params?.name, message.params?.arguments || {});
      send({ jsonrpc: "2.0", id: message.id, result: textResult(result) }, mode);
      return;
    }

    if (message.method === "ping") {
      send({ jsonrpc: "2.0", id: message.id, result: {} }, mode);
      return;
    }

    sendError(message.id, -32601, `Method not found: ${message.method}`, mode);
  } catch (error) {
    sendError(message.id, -32000, error instanceof Error ? error.message : String(error), mode);
  }
}

let inputBuffer = "";
let preferredMode = "line";

process.stdin.setEncoding("utf8");
process.stdin.on("data", (chunk) => {
  inputBuffer += chunk;
  void pumpInput();
});

async function pumpInput() {
  while (inputBuffer.length > 0) {
    inputBuffer = inputBuffer.replace(/^\s+/, "");
    if (inputBuffer.length === 0) return;

    if (/^Content-Length:/i.test(inputBuffer)) {
      const headerEnd = inputBuffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) return;
      const header = inputBuffer.slice(0, headerEnd);
      const match = header.match(/Content-Length:\s*(\d+)/i);
      if (!match) throw new Error("Invalid MCP Content-Length header.");
      const length = Number.parseInt(match[1], 10);
      const bodyStart = headerEnd + 4;
      if (Buffer.byteLength(inputBuffer.slice(bodyStart), "utf8") < length) return;
      const bodyBuffer = Buffer.from(inputBuffer.slice(bodyStart), "utf8");
      const body = bodyBuffer.subarray(0, length).toString("utf8");
      inputBuffer = bodyBuffer.subarray(length).toString("utf8");
      preferredMode = "header";
      await handleMessage(JSON.parse(body), "header");
      continue;
    }

    const newline = inputBuffer.indexOf("\n");
    if (newline < 0) return;
    const line = inputBuffer.slice(0, newline).trim();
    inputBuffer = inputBuffer.slice(newline + 1);
    if (!line) continue;
    preferredMode = "line";
    await handleMessage(JSON.parse(line), "line");
  }
}

function send(payload, mode = preferredMode) {
  const text = JSON.stringify(payload);
  if (mode === "header") {
    process.stdout.write(`Content-Length: ${Buffer.byteLength(text, "utf8")}\r\n\r\n${text}`);
  } else {
    process.stdout.write(`${text}\n`);
  }
}

function sendError(id, code, message, mode = preferredMode) {
  send({ jsonrpc: "2.0", id, error: { code, message } }, mode);
}

process.on("uncaughtException", (error) => {
  console.error(error);
});

process.on("unhandledRejection", (error) => {
  console.error(error);
});
