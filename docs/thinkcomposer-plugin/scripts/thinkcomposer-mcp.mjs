#!/usr/bin/env node

import fs from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";
import os from "node:os";
import { Buffer } from "node:buffer";
import { inflateRawSync } from "node:zlib";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";

const SERVER_NAME = "thinkcomposer";
const SERVER_VERSION = "0.3.0";
const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const PLUGIN_ROOT = path.resolve(SCRIPT_DIR, "..");
const DEFAULT_ROOT = process.env.THINKCOMPOSER_ROOT || findThinkComposerRoot(PLUGIN_ROOT) || process.cwd();
const IMAGE_EXTENSIONS = new Set([".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".xps", ".pdf"]);
const LOG_EXTENSIONS = new Set([".log", ".txt"]);
const CONTAINER_EXTENSIONS = new Set([".tcom", ".tdom"]);
const SKIP_DIRS = new Set([".git", ".vs", "bin", "obj", "node_modules", "packages", ".cache", "TestResults"]);

const tools = [
  {
    name: "thinkcomposer_discover",
    description: "Find recent ThinkComposer .tcom/.tdom containers, JSON, image, and log artifacts under a root directory.",
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
    description: "Read a Composition JSON, Domain JSON, or modern .tcom/.tdom container and return high-signal counts and metadata.",
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
    description: "Read a modern .tcom/.tdom container manifest plus authoritative root JSON, sidecar JSON, and preview screenshot metadata.",
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
    description: "Parse and lightly validate a ThinkComposer JSON file or embedded JSON parts in a modern .tcom/.tdom container.",
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
    description: "Write a standalone, validated Composition or Domain operations patch. Apply it with thinkcomposer_apply_patch; do not splice its directives into authoritative package snapshots.",
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
    name: "thinkcomposer_apply_patch",
    description: "Preview and safely apply a standalone operations patch to a .tcom/.tdom package through the ThinkComposer CLI, producing a canonical saved package.",
    inputSchema: {
      type: "object",
      required: ["input", "patch", "output"],
      properties: {
        input: { type: "string", description: "Source .tcom or .tdom package." },
        patch: { type: "string", description: "Standalone Composition or Domain operations JSON patch." },
        output: { type: "string", description: "Destination package. May equal input only when inPlace is true." },
        kind: { type: "string", enum: ["composition", "domain", "auto"], default: "auto" },
        inPlace: { type: "boolean", default: false },
        previewOnly: { type: "boolean", default: false },
        routingOutputDir: { type: "string", description: "Composition route-health output directory. Defaults beside the canonical output package." },
        layout: { type: "string", enum: ["route", "spider", "hierarchy", "flowchart", "system"], default: "route" },
        imageOutput: { type: "string", description: "Optional PNG/JPEG/etc. path for a post-apply visual export. Defaults to result.png under routingOutputDir for Composition patches." },
        view: { type: "string", description: "Optional view TechName for the post-apply image export." },
        cliPath: { type: "string", description: "Optional ThinkComposer.Cli.exe/thinkcomposer path override." }
      }
    }
  },
  {
    name: "thinkcomposer_extract_container_artifacts",
    description: "Extract authoritative root JSON, sidecar JSON, and preview screenshots from a modern .tcom/.tdom container into a working directory.",
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
    case "thinkcomposer_apply_patch":
      return applyPatch(args);
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
  const hasFullState = hasSnapshotState(document);
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
    documentKind: operations.length > 0 ? (hasFullState ? "hybrid" : "operationsPatch") : "snapshot",
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
    summary.routeGeometry = summarizeCompositionGeometry(document);
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

function summarizeCompositionGeometry(document) {
  const errors = [];
  const warnings = [];
  let connectors = 0;
  let routePoints = 0;
  let routePointFields = 0;
  let suspiciousRoutes = 0;
  let suspiciousCenters = 0;

  const views = Array.isArray(document?.views) ? document.views : [];
  const operations = Array.isArray(document?.operations) ? document.operations : [];
  const relationships = Array.isArray(document?.relationships) ? document.relationships : [];
  const relationshipByKey = new Map();
  for (const relationship of relationships) {
    for (const key of [relationship?.id, relationship?.techName]) {
      if (isNonEmptyString(key)) relationshipByKey.set(String(key).toLowerCase(), relationship);
    }
  }

  for (let viewIndex = 0; viewIndex < views.length; viewIndex += 1) {
    const view = views[viewIndex] || {};
    const visuals = Array.isArray(view.visuals) ? view.visuals : [];
    const visualByRepresentationId = new Map();
    const visualsByIdeaKey = new Map();
    for (const visual of visuals) {
      const representationKey = normalizeIdentityKey(visual?.representationId);
      if (representationKey) visualByRepresentationId.set(representationKey, visual);
      for (const key of [visual?.ideaId, visual?.ideaTechName]) {
        const normalizedKey = normalizeIdentityKey(key);
        if (!normalizedKey) continue;
        const matches = visualsByIdeaKey.get(normalizedKey) || [];
        matches.push(visual);
        visualsByIdeaKey.set(normalizedKey, matches);
      }
    }
    for (const matches of visualsByIdeaKey.values()) matches.sort(compareVisualIdentity);

    for (let visualIndex = 0; visualIndex < visuals.length; visualIndex += 1) {
      const visual = visuals[visualIndex] || {};
      const visualLabel = `views[${viewIndex}].visuals[${visualIndex}]`;
      const visualConnectors = Array.isArray(visual.connectors) ? visual.connectors : [];
      for (let connectorIndex = 0; connectorIndex < visualConnectors.length; connectorIndex += 1) {
        const connector = visualConnectors[connectorIndex] || {};
        const label = `${visualLabel}.connectors[${connectorIndex}]`;
        connectors += 1;

        if (connector.routePoints !== undefined && !Array.isArray(connector.routePoints)) {
          errors.push(`${label}.routePoints must be an array when present.`);
          continue;
        }
        if (connector.routePoints !== undefined) routePointFields += 1;
        if (Array.isArray(connector.routePoints) && connector.routePoints.length > 32) {
          errors.push(`${label}.routePoints has ${connector.routePoints.length} points; maximum is 32.`);
        }
        if (Array.isArray(connector.routePoints) && connector.intermediatePosition !== undefined) {
          warnings.push(`${label} supplies both routePoints and deprecated intermediatePosition; routePoints wins.`);
        }

        const points = Array.isArray(connector.routePoints)
          ? connector.routePoints
          : connector.intermediatePosition ? [connector.intermediatePosition] : [];
        routePoints += points.length;
        let invalid = false;
        for (let pointIndex = 0; pointIndex < points.length; pointIndex += 1) {
          if (!isFinitePoint(points[pointIndex])) {
            errors.push(`${label}.routePoints[${pointIndex}] must contain finite numeric x/y coordinates.`);
            invalid = true;
          }
        }

        if (points.length > 0 && !hasAny(connector, ["id", "linkId", "associatedIdeaId", "associatedIdeaTechName"])) {
          warnings.push(`${label} contains route geometry without a stable connector/link identity.`);
        }
        if (operations.length > 0 && points.length > 0) {
          warnings.push(`${label} contains explicit connector geometry in a generated/edit document; prefer endpointCorridor plus autoRoute.`);
        }

        const origin = finitePointOrNull(connector.originEdgePosition) || finitePointOrNull(connector.originPosition);
        const target = finitePointOrNull(connector.targetEdgePosition) || finitePointOrNull(connector.targetPosition);
        if (!invalid && origin && target && points.length > 0 && isSuspiciousRoute(origin, target, points)) {
          suspiciousRoutes += 1;
          warnings.push(`${label} has an excessive detour or a control point outside its endpoint corridor; queue it for auto-routing unless exact geometry is intentional.`);
        }
      }

      const relationship = relationshipByKey.get(String(visual.ideaId || visual.ideaTechName || "").toLowerCase());
      const relationshipCenter = visualCenter(visual);
      if (!relationship || !relationshipCenter) continue;
      const endpointCenters = resolveRelationshipEndpointCenters(visual, relationship, visualConnectors,
        relationshipCenter, visualByRepresentationId, visualsByIdeaKey);
      if (endpointCenters.length >= 2 && isSuspiciousRelationshipCenter(relationshipCenter, endpointCenters)) {
        suspiciousCenters += 1;
        warnings.push(`${visualLabel} places a Relationship center outside the local endpoint corridor; generated edits should omit its x/y and request endpointCorridor placement.`);
      }
    }
  }

  return { connectors, routePoints, routePointFields, suspiciousRoutes, suspiciousCenters, errors, warnings };
}

function resolveRelationshipEndpointCenters(relationshipVisual, relationship, connectors, relationshipCenter,
  visualByRepresentationId, visualsByIdeaKey) {
  const endpointVisuals = [];
  const usedVisualKeys = new Set();
  const links = Array.isArray(relationship?.links) ? relationship.links : [];
  const linksById = new Map();
  for (const link of links) {
    const key = normalizeIdentityKey(link?.id);
    if (key) linksById.set(key, link);
  }

  const remember = (visual) => {
    if (!visual || visual === relationshipVisual) return;
    const key = stableVisualKey(visual);
    if (usedVisualKeys.has(key)) return;
    usedVisualKeys.add(key);
    endpointVisuals.push(visual);
  };

  for (const connector of connectors || []) {
    remember(resolveConnectorEndpointVisual(relationshipVisual, connector, linksById, relationshipCenter,
      visualByRepresentationId, visualsByIdeaKey, usedVisualKeys));
  }

  // Legacy and hand-authored snapshots may omit connector representation ids.  Fall
  // back only through the semantic endpoint identity, choosing the nearest matching
  // shortcut deterministically instead of whichever duplicate happened to be last.
  for (const link of links) {
    remember(chooseNearestIdentityVisual(endpointIdeaKeys(link), relationshipCenter,
      visualsByIdeaKey, usedVisualKeys, relationshipVisual));
  }

  return endpointVisuals.map(visualCenter).filter(Boolean);
}

function resolveConnectorEndpointVisual(relationshipVisual, connector, linksById, relationshipCenter,
  visualByRepresentationId, visualsByIdeaKey, usedVisualKeys) {
  if (!connector || typeof connector !== "object") return null;
  const relationshipRepresentationKey = normalizeIdentityKey(relationshipVisual?.representationId);
  const originRepresentationKey = normalizeIdentityKey(connector.originRepresentationId);
  const targetRepresentationKey = normalizeIdentityKey(connector.targetRepresentationId);
  const endpointRepresentationKeys = [];

  if (originRepresentationKey === relationshipRepresentationKey && targetRepresentationKey) {
    endpointRepresentationKeys.push(targetRepresentationKey);
  } else if (targetRepresentationKey === relationshipRepresentationKey && originRepresentationKey) {
    endpointRepresentationKeys.push(originRepresentationKey);
  } else {
    for (const key of [originRepresentationKey, targetRepresentationKey]) {
      if (key && key !== relationshipRepresentationKey && !endpointRepresentationKeys.includes(key)) {
        endpointRepresentationKeys.push(key);
      }
    }
  }

  const exactMatches = endpointRepresentationKeys
    .map((key) => visualByRepresentationId.get(key))
    .filter((visual) => visual && visual !== relationshipVisual);
  if (exactMatches.length > 0) {
    return chooseNearestVisual(exactMatches, relationshipCenter, usedVisualKeys);
  }

  const semanticLink = linksById.get(normalizeIdentityKey(connector.linkId));
  const keys = connectorEndpointIdeaKeys(connector, relationshipVisual, semanticLink);
  return chooseNearestIdentityVisual(keys, relationshipCenter, visualsByIdeaKey,
    usedVisualKeys, relationshipVisual);
}

function connectorEndpointIdeaKeys(connector, relationshipVisual, semanticLink) {
  const relationshipKeys = new Set([
    normalizeIdentityKey(relationshipVisual?.ideaId),
    normalizeIdentityKey(relationshipVisual?.ideaTechName)
  ].filter(Boolean));
  const keys = [];
  const add = (value) => {
    const key = normalizeIdentityKey(value);
    if (key && !relationshipKeys.has(key) && !keys.includes(key)) keys.push(key);
  };
  for (const value of [
    connector?.associatedIdeaId, connector?.associatedIdeaTechName,
    connector?.originIdeaId, connector?.originIdeaTechName,
    connector?.targetIdeaId, connector?.targetIdeaTechName,
    ...endpointIdeaKeys(semanticLink)
  ]) add(value);
  return keys;
}

function endpointIdeaKeys(link) {
  return [link?.ideaId, link?.associatedIdeaId, link?.ideaTechName, link?.associatedIdeaTechName]
    .map(normalizeIdentityKey)
    .filter((key, index, keys) => key && keys.indexOf(key) === index);
}

function chooseNearestIdentityVisual(keys, referencePoint, visualsByIdeaKey, usedVisualKeys, excludedVisual) {
  const matches = [];
  const seen = new Set();
  for (const key of keys || []) {
    for (const visual of visualsByIdeaKey.get(normalizeIdentityKey(key)) || []) {
      if (!visual || visual === excludedVisual) continue;
      const visualKey = stableVisualKey(visual);
      if (seen.has(visualKey)) continue;
      seen.add(visualKey);
      matches.push(visual);
    }
  }
  return chooseNearestVisual(matches, referencePoint, usedVisualKeys);
}

function chooseNearestVisual(visuals, referencePoint, usedVisualKeys) {
  const ranked = (visuals || [])
    .map((visual) => ({ visual, center: visualCenter(visual), key: stableVisualKey(visual) }))
    .filter((candidate) => candidate.center)
    .sort((left, right) => {
      const leftUsed = usedVisualKeys?.has(left.key) ? 1 : 0;
      const rightUsed = usedVisualKeys?.has(right.key) ? 1 : 0;
      if (leftUsed !== rightUsed) return leftUsed - rightUsed;
      const distanceDelta = pointDistance(left.center, referencePoint) - pointDistance(right.center, referencePoint);
      if (Math.abs(distanceDelta) > 1e-9) return distanceDelta;
      return left.key.localeCompare(right.key);
    });
  return ranked.length > 0 ? ranked[0].visual : null;
}

function normalizeIdentityKey(value) {
  return isNonEmptyString(value) ? String(value).trim().toLowerCase() : null;
}

function stableVisualKey(visual) {
  const representationKey = normalizeIdentityKey(visual?.representationId);
  if (representationKey) return `representation:${representationKey}`;
  return `visual:${normalizeIdentityKey(visual?.ideaId) || normalizeIdentityKey(visual?.ideaTechName) || "unknown"}:` +
    `${Number(visual?.x) || 0}:${Number(visual?.y) || 0}:${Number(visual?.width) || 0}:${Number(visual?.height) || 0}`;
}

function compareVisualIdentity(left, right) {
  return stableVisualKey(left).localeCompare(stableVisualKey(right));
}

function isFinitePoint(value) {
  return value && Number.isFinite(value.x) && Number.isFinite(value.y);
}

function finitePointOrNull(value) {
  return isFinitePoint(value) ? { x: value.x, y: value.y } : null;
}

function visualCenter(visual) {
  if (!visual || !Number.isFinite(visual.x) || !Number.isFinite(visual.y)) return null;
  return {
    x: visual.x + (Number.isFinite(visual.width) ? visual.width / 2 : 0),
    y: visual.y + (Number.isFinite(visual.height) ? visual.height / 2 : 0)
  };
}

function pointDistance(left, right) {
  return Math.hypot(right.x - left.x, right.y - left.y);
}

function isSuspiciousRoute(origin, target, routePoints) {
  const direct = pointDistance(origin, target);
  const manhattan = Math.abs(target.x - origin.x) + Math.abs(target.y - origin.y);
  const margin = Math.max(96, 0.5 * manhattan);
  const minX = Math.min(origin.x, target.x) - margin;
  const maxX = Math.max(origin.x, target.x) + margin;
  const minY = Math.min(origin.y, target.y) - margin;
  const maxY = Math.max(origin.y, target.y) + margin;
  if (routePoints.some((point) => point.x < minX || point.x > maxX || point.y < minY || point.y > maxY)) return true;
  const path = [origin, ...routePoints, target];
  let length = 0;
  for (let index = 1; index < path.length; index += 1) length += pointDistance(path[index - 1], path[index]);
  return length > Math.max(3 * direct, direct + 384);
}

function isSuspiciousRelationshipCenter(center, endpoints) {
  const xs = endpoints.map((point) => point.x);
  const ys = endpoints.map((point) => point.y);
  const width = Math.max(...xs) - Math.min(...xs);
  const height = Math.max(...ys) - Math.min(...ys);
  const margin = Math.max(64, 0.25 * (width + height));
  return center.x < Math.min(...xs) - margin || center.x > Math.max(...xs) + margin ||
    center.y < Math.min(...ys) - margin || center.y > Math.max(...ys) + margin;
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
  const sidecarManifest = manifest.format === "ThinkComposer.Package" ? tryReadJsonZipPart(zip, "Interchange/manifest.json") : null;
  const authoritativeParts = Array.isArray(manifest.authoritativeParts) ? manifest.authoritativeParts : [];
  const sidecarJsonParts = Array.isArray(manifest.jsonParts) ? manifest.jsonParts : Array.isArray(sidecarManifest?.jsonParts) ? sidecarManifest.jsonParts : [];
  const jsonParts = authoritativeParts.length > 0 ? authoritativeParts : sidecarJsonParts;
  const previews = Array.isArray(manifest.previews) ? manifest.previews : Array.isArray(sidecarManifest?.previews) ? sidecarManifest.previews : [];
  const embedded = {};

  if (options.includeEmbedded !== false) {
    for (const part of jsonParts) {
      const partName = normalizePartUri(part.partUri);
      const entry = findZipEntry(zip, partName);
      if (!entry) continue;
      const document = JSON.parse(readZipEntry(zip, entry).toString("utf8"));
      const key = embeddedKeyForPart(part, partName);
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
    manifestPartUri: manifest._partUri || null,
    packageKind: manifest.packageKind || null,
    persistenceFormat: manifest.persistenceFormat || null,
    persistenceFormatVersion: manifest.persistenceFormatVersion ?? null,
    generatedAtUtc: manifest.generatedAtUtc || null,
    savedAtUtc: manifest.savedAtUtc || null,
    application: manifest.application || null,
    applicationVersion: manifest.applicationVersion || null,
    mtimeUtc: stats.mtime.toISOString(),
    sizeBytes: stats.size,
    source: manifest.source || null,
    nativePartUri: manifest.nativePartUri || null,
    nativePartSha256: manifest.nativePartSha256 || null,
    snapshotManifestFormat: sidecarManifest?.format || (manifest.format === "ThinkComposer.ContainerSnapshot" ? manifest.format : null),
    snapshotManifestFormatVersion: sidecarManifest?.formatVersion ?? (manifest.format === "ThinkComposer.ContainerSnapshot" ? manifest.formatVersion : null),
    legacyBinaryFallback: manifest.legacyBinaryFallback || null,
    sidecars: manifest.sidecars || null,
    counts: {
      entries: zip.entries.length,
      jsonParts: jsonParts.length,
      authoritativeParts: authoritativeParts.length,
      sidecarJsonParts: sidecarJsonParts.length,
      previews: previews.length,
      renderablePreviews: previews.filter((preview) => !preview.skipped && preview.partUri).length,
      skippedPreviews: previews.filter((preview) => preview.skipped).length,
      warnings: Array.isArray(manifest.warnings) ? manifest.warnings.length : 0
    },
    authoritativeParts,
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

  if (summary.format === "ThinkComposer.Package") {
    if (summary.formatVersion !== 1) {
      errors.push("Package manifest formatVersion must be 1.");
    }
    if (summary.persistenceFormat !== "json") {
      errors.push("Package persistenceFormat must be json.");
    }
    if (summary.packageKind === "composition") {
      if (!summary.embedded.composition) {
        errors.push("Composition package is missing authoritative /Composition.json.");
      }
      if (!summary.embedded.domain) {
        errors.push("Composition package is missing authoritative embedded /Domain.json.");
      }
    } else if (summary.packageKind === "domain") {
      if (!summary.embedded.domain) {
        errors.push("Domain package is missing authoritative /Domain.json.");
      }
    } else {
      errors.push("Package manifest packageKind must be composition or domain.");
    }
    if (summary.snapshotManifestFormat && summary.snapshotManifestFormat !== "ThinkComposer.ContainerSnapshot") {
      errors.push("Sidecar manifest format must be ThinkComposer.ContainerSnapshot.");
    }
    if (summary.snapshotManifestFormatVersion != null && ![1, 2].includes(summary.snapshotManifestFormatVersion)) {
      errors.push("Sidecar manifest formatVersion must be 1 or 2.");
    }
  } else if (summary.format === "ThinkComposer.ContainerSnapshot") {
    if (![1, 2].includes(summary.formatVersion)) {
      errors.push("Container manifest formatVersion must be 1 or 2.");
    }
    if (!summary.embedded.composition) {
      errors.push("Container is missing embedded composition JSON.");
    }
    if (!summary.embedded.domain) {
      warnings.push("Container has no embedded Domain JSON part.");
    }
  } else {
    errors.push("Container manifest format must be ThinkComposer.Package or ThinkComposer.ContainerSnapshot.");
  }
  for (const [key, embedded] of Object.entries(summary.embedded)) {
    if (key === "composition" && embedded.format !== "ThinkComposer.JsonInterchange") {
      errors.push("Embedded composition JSON format must be ThinkComposer.JsonInterchange.");
    }
    if (key === "templateComposition" && embedded.format !== "ThinkComposer.JsonInterchange") {
      errors.push("Embedded template composition JSON format must be ThinkComposer.JsonInterchange.");
    }
    if (key === "domain" && embedded.format !== "ThinkComposer.DomainJsonInterchange") {
      errors.push("Embedded Domain JSON format must be ThinkComposer.DomainJsonInterchange.");
    }
    const supportedVersions = key === "domain" ? [1] : [1, 2];
    if (!supportedVersions.includes(embedded.formatVersion)) {
      errors.push(`Embedded ${key} JSON formatVersion must be ${supportedVersions.join(" or ")}.`);
    }
    if ((key === "composition" || key === "templateComposition") && embedded.formatVersion === 1 &&
        (embedded.routeGeometry?.routePointFields || 0) > 0) {
      errors.push(`Embedded ${key} uses routePoints with formatVersion 1. Upgrade it to version 2 to prevent older readers from dropping multi-point geometry.`);
    }
    if ((key === "composition" || key === "templateComposition") && embedded.operationsCount > 0) {
      warnings.push(`Authoritative ${key} JSON contains operations. Treat them as legacy one-shot edit directives and save immediately after opening to consume them.`);
    }
    if ((key === "composition" || key === "templateComposition") &&
        embedded.operationsCount === 0 && (embedded.importOptions || embedded.visualStrategy)) {
      warnings.push(`Authoritative ${key} snapshot contains importOptions/visualStrategy without operations; native snapshot rehydration ignores these edit directives.`);
    }
    for (const problem of embedded.routeGeometry?.errors || []) {
      errors.push(`Embedded ${key}: ${problem}`);
    }
    for (const problem of embedded.routeGeometry?.warnings || []) {
      warnings.push(`Embedded ${key}: ${problem}`);
    }
  }
  if (summary.counts.renderablePreviews < 1) {
    warnings.push("Container has no renderable preview screenshots.");
  }
  const entryNames = new Set((summary.entries || []).map((entry) => entry.name));
  const snapshotVersion = summary.snapshotManifestFormatVersion ??
    (summary.format === "ThinkComposer.ContainerSnapshot" ? summary.formatVersion : null);
  if (snapshotVersion === 1) {
    warnings.push("Container snapshot manifest v1 has no verified preview cache metadata; current saves regenerate it as v2.");
  }
  for (const preview of summary.previews) {
    if (!preview.skipped && preview.partUri && !entryNames.has(normalizePartUri(preview.partUri))) {
      errors.push(`Preview part is listed but missing: ${preview.partUri}`);
    }
    if (snapshotVersion === 2) {
      if (!/^[0-9a-f]{64}$/i.test(preview.inputSha256 || "")) {
        errors.push(`Preview inputSha256 is missing or invalid for view ${preview.viewId || preview.viewTechName || "<unknown>"}.`);
      }
      if (typeof preview.renderProfile !== "string" || preview.renderProfile.length === 0) {
        errors.push(`Preview renderProfile is missing for view ${preview.viewId || preview.viewTechName || "<unknown>"}.`);
      }
      if (preview.disposition != null && !["rendered", "reused", "empty"].includes(preview.disposition)) {
        errors.push(`Preview disposition is invalid for view ${preview.viewId || preview.viewTechName || "<unknown>"}.`);
      }
      if (["rendered", "reused"].includes(preview.disposition)) {
        if (preview.skipped || !preview.partUri || !/^[0-9a-f]{64}$/i.test(preview.sha256 || "") || !Number.isInteger(preview.bytes)) {
          errors.push(`Rendered/reused preview metadata is incomplete for view ${preview.viewId || preview.viewTechName || "<unknown>"}.`);
        }
      } else if (preview.disposition === "empty" && !preview.skipped) {
        errors.push(`Empty preview must be marked skipped for view ${preview.viewId || preview.viewTechName || "<unknown>"}.`);
      }
    }
  }

  return {
    path: file,
    kind: "container",
    ok: errors.length === 0,
    errors,
    warnings,
    summary,
    importMenu: "Keep authoritative root JSON as snapshot state. Write a standalone operations patch, preview/apply it through the CLI, then inspect and validate the canonical saved package."
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

  entriesToExtract.set("manifest.json", "manifest.json");
  if (findZipEntry(zip, "Interchange/manifest.json")) {
    entriesToExtract.set("Interchange/manifest.json", "Interchange/manifest.json");
  }

  if (includeJson) {
    const sidecarManifest = manifest.format === "ThinkComposer.Package" ? tryReadJsonZipPart(zip, "Interchange/manifest.json") : null;
    const jsonParts = [
      ...(Array.isArray(manifest.authoritativeParts) ? manifest.authoritativeParts : []),
      ...(Array.isArray(manifest.jsonParts) ? manifest.jsonParts : []),
      ...(Array.isArray(sidecarManifest?.jsonParts) ? sidecarManifest.jsonParts : [])
    ];
    for (const part of jsonParts) {
      const partName = normalizePartUri(part.partUri);
      if (partName) entriesToExtract.set(partName, partName);
    }
  }

  if (includePreviews) {
    const sidecarManifest = manifest.format === "ThinkComposer.Package" ? tryReadJsonZipPart(zip, "Interchange/manifest.json") : null;
    const previews = Array.isArray(manifest.previews) ? manifest.previews : Array.isArray(sidecarManifest?.previews) ? sidecarManifest.previews : [];
    for (const preview of previews) {
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
  const supportedVersions = detectedKind === "composition" ? [1, 2] : [1];
  if (!supportedVersions.includes(document.formatVersion)) {
    errors.push(`formatVersion must be ${supportedVersions.join(" or ")}.`);
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

  if (detectedKind === "composition") {
    const geometry = summarizeCompositionGeometry(document);
    errors.push(...geometry.errors);
    warnings.push(...geometry.warnings);
    const hasFullState = hasSnapshotState(document);
    if (document.formatVersion === 1 && containsPropertyDeep(document.views, "routePoints")) {
      errors.push("formatVersion 1 cannot contain routePoints. Upgrade the document to version 2 so older applications cannot silently discard multi-point geometry.");
    }
    if (operations.length > 0 && hasFullState) {
      warnings.push("Document mixes full-state snapshot arrays and operations. Prefer a standalone operations-only patch for generated edits.");
    }
    if (operations.length === 0 && (document.importOptions || document.visualStrategy)) {
      warnings.push("Snapshot contains importOptions/visualStrategy without operations. Native snapshot loading deliberately ignores these edit directives.");
    }
  }

  return {
    path: summary.path,
    kind: detectedKind,
    ok: errors.length === 0,
    errors,
    warnings,
    summary,
    importMenu: detectedKind === "domain"
      ? "Apply this standalone patch with thinkcomposer domain import-json preview/apply, then validate the canonical saved package."
      : "Apply this standalone patch with thinkcomposer composition import-json preview/apply, then validate the canonical saved package."
  };
}

function validateCompositionOperation(operation, label, errors, warnings) {
  const placementMode = operationRelationshipPlacement(operation);
  const autoRoute = operationAutoRoute(operation);
  const hasVisualIntent = operationHasRelationshipVisualIntent(operation);
  if (containsRouteGeometry(operation)) {
    errors.push(`${label} supplies routePoints/intermediatePosition. GPT-authored connector geometry is disabled by default; request autoRoute:true instead.`);
  }
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
  if (operation.entity === "relationship" && placementMode === "explicit") {
    warnings.push(`${label} preserves explicit relationship-center placement; endpointCorridor or auto is usually better for generated diagrams.`);
  }
  if (operation.entity === "relationship" && operationHasCoordinate(operation) && placementMode !== "explicit") {
    errors.push(`${label} supplies Relationship x/y without visual.relationshipCenterPlacement:"explicit". Omit the coordinates and request endpointCorridor, or declare explicit placement deliberately.`);
  }
  if (operation.entity === "relationship" && hasVisualIntent && autoRoute === false) {
    warnings.push(`${label} disables auto-routing; generated Relationship edits normally set autoRoute:true.`);
  }
  if (operation.entity === "relationship" && operation.op !== "delete" && hasVisualIntent &&
      !["explicit", "midpoint", "endpointCorridor", "auto", "hideGeneric", "defer"].includes(placementMode)) {
    warnings.push(`${label} has no relationshipCenterPlacement; generated Relationship edits should request endpointCorridor.`);
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
  if (kind === "composition" && args.operations.some(containsRouteGeometry)) {
    throw new Error("Composition operations cannot author routePoints/intermediatePosition by default; request autoRoute:true instead.");
  }
  if (kind === "composition" && args.operations.some((operation) => operation?.entity === "relationship" &&
      operationHasCoordinate(operation) && operationRelationshipPlacement(operation) !== "explicit")) {
    throw new Error("Relationship x/y requires visual.relationshipCenterPlacement:\"explicit\"; otherwise omit hub coordinates and use endpointCorridor.");
  }

  const target = path.resolve(String(args.path));
  if (path.extname(target).toLowerCase() !== ".json") {
    throw new Error("Patch path must end in .json.");
  }
  await fs.mkdir(path.dirname(target), { recursive: true });
  if (!args.overwrite && await exists(target)) {
    throw new Error(`Refusing to overwrite existing file: ${target}`);
  }

  const normalizedOperations = args.operations.map((operation) => {
    if (kind !== "composition" || !operation || operation.entity !== "relationship" ||
        operation.op === "delete" || !operationHasRelationshipVisualIntent(operation)) return operation;
    const normalized = { ...operation };
    if (!operationHasAutoRoute(operation)) normalized.autoRoute = true;
    if (!operationRelationshipPlacement(operation)) {
      // Keep the default in the same visual-control location the author used.  The
      // importer intentionally gives top-level visual precedence over set.visual, so
      // manufacturing a top-level object here would otherwise shadow nested display,
      // participation, and shortcut controls.
      if (operation.visual && typeof operation.visual === "object" && !Array.isArray(operation.visual)) {
        normalized.visual = {
          ...operation.visual,
          relationshipCenterPlacement: "endpointCorridor"
        };
      } else if (operation.set?.visual && typeof operation.set.visual === "object" && !Array.isArray(operation.set.visual)) {
        normalized.set = {
          ...operation.set,
          visual: {
            ...operation.set.visual,
            relationshipCenterPlacement: "endpointCorridor"
          }
        };
      } else {
        normalized.visual = { relationshipCenterPlacement: "endpointCorridor" };
      }
    }
    return normalized;
  });
  const defaultImportOptions = kind === "composition" ? {
    autoRoutePlacedLinks: true,
    relationshipVisualPlacementMode: "endpointCorridor",
    recomputeSuspiciousRelationshipVisuals: true
  } : null;
  const extra = args.extra && typeof args.extra === "object" && !Array.isArray(args.extra) ? args.extra : {};
  if (hasSnapshotState(extra, kind)) {
    throw new Error(`extra cannot add ${kind === "domain" ? "Domain" : "Composition"} snapshot fields to a standalone operations patch.`);
  }
  const document = {
    ...extra,
    format: kind === "domain" ? "ThinkComposer.DomainJsonInterchange" : "ThinkComposer.JsonInterchange",
    formatVersion: kind === "composition" ? 2 : 1,
    ...(kind === "composition" ? { application: "ThinkComposer" } : {}),
    ...(defaultImportOptions || args.importOptions ? { importOptions: { ...(defaultImportOptions || {}), ...(args.importOptions || {}) } } : {}),
    ...(args.visualStrategy ? { visualStrategy: args.visualStrategy } : {}),
    ...(Array.isArray(args.warnings) ? { warnings: args.warnings } : {}),
    operations: normalizedOperations
  };

  await fs.writeFile(target, `${JSON.stringify(document, null, 2)}\n`, "utf8");
  const validation = await validateJsonFile(target, kind);
  return {
    path: target,
    bytesWritten: Buffer.byteLength(JSON.stringify(document, null, 2), "utf8") + 1,
    validation,
    importMenu: kind === "domain"
      ? "Preview and apply this standalone patch with thinkcomposer_apply_patch or the CLI domain import-json command."
      : "Preview and apply this standalone patch with thinkcomposer_apply_patch or the CLI composition import-json command. Do not splice it into /Composition.json."
  };
}

async function applyPatch(args) {
  const input = await resolveFile(args.input);
  const patch = await resolveFile(args.patch);
  const output = path.resolve(expandHome(String(args.output)));
  const inPlace = args.inPlace === true;
  const previewOnly = args.previewOnly === true;
  const inputExtension = path.extname(input).toLowerCase();
  if (!CONTAINER_EXTENSIONS.has(inputExtension)) throw new Error("input must be a .tcom or .tdom package.");
  if (path.extname(output).toLowerCase() !== inputExtension) throw new Error("output must use the same package extension as input.");
  if (path.resolve(input) === output && !inPlace) throw new Error("output may equal input only when inPlace is true.");

  const patchDocument = await readJson(patch);
  const detectedKind = patchDocument.format === "ThinkComposer.DomainJsonInterchange" ? "domain" :
    patchDocument.format === "ThinkComposer.JsonInterchange" ? "composition" : null;
  const requestedKind = args.kind && args.kind !== "auto" ? args.kind : detectedKind;
  if (!requestedKind || !["composition", "domain"].includes(requestedKind)) throw new Error("Cannot determine whether patch is composition or domain JSON.");
  if (detectedKind && requestedKind !== detectedKind) throw new Error(`Patch kind is ${detectedKind}, not ${requestedKind}.`);
  if (!Array.isArray(patchDocument.operations) || patchDocument.operations.length < 1) {
    throw new Error("Apply-patch requires a standalone document with at least one operation.");
  }
  if (hasSnapshotState(patchDocument, requestedKind)) {
    throw new Error("Apply-patch refuses hybrid full-state documents. Supply an operations-only patch.");
  }
  const patchValidation = await validateJsonFile(patch, requestedKind);
  if (!patchValidation.ok) throw new Error(`Patch validation failed: ${patchValidation.errors.join(" ")}`);

  const cli = await resolveThinkComposerCli(args.cliPath);
  await fs.mkdir(path.dirname(output), { recursive: true });
  const commandArgs = [requestedKind, "import-json", "--input", input, "--json", patch, "--output", output];
  if (inPlace) commandArgs.push("--in-place");
  const preview = await runCommand(cli, [...commandArgs, "--preview-only"]);
  if (preview.exitCode !== 0) {
    throw new Error(`ThinkComposer patch preview failed (${preview.exitCode}). ${preview.stderr || preview.stdout}`.trim());
  }
  if (previewOnly) {
    return { input, patch, output, kind: requestedKind, cli, previewOnly: true, patchValidation, preview };
  }

  const apply = await runCommand(cli, commandArgs);
  if (apply.exitCode !== 0) {
    throw new Error(`ThinkComposer patch apply failed (${apply.exitCode}). ${apply.stderr || apply.stdout}`.trim());
  }
  const packageValidation = await validateContainerFile(output);
  let routingValidation = null;
  let routingHealth = null;
  let imageExport = null;
  if (requestedKind === "composition") {
    const routingOutputDir = path.resolve(expandHome(String(args.routingOutputDir || `${output}.routing`)));
    const layout = args.layout || "route";
    await fs.mkdir(routingOutputDir, { recursive: true });
    routingValidation = await runCommand(cli, ["composition", "validate-routing", "--input", output,
      "--output-dir", routingOutputDir, "--layout", layout]);
    const routingReportPath = path.join(routingOutputDir, `routing-report-${layout}.json`);
    if (await exists(routingReportPath)) {
      try {
        routingHealth = await readJson(routingReportPath);
      } catch (error) {
        routingHealth = { parseError: error instanceof Error ? error.message : String(error), path: routingReportPath };
      }
    }

    const imageOutput = path.resolve(expandHome(String(args.imageOutput || path.join(routingOutputDir, "result.png"))));
    await fs.mkdir(path.dirname(imageOutput), { recursive: true });
    const imageArgs = ["composition", "export-image", "--input", output, "--output", imageOutput];
    if (args.view) imageArgs.push("--view", String(args.view));
    imageExport = await runCommand(cli, imageArgs);
  }
  const postApplyWarnings = [];
  if (packageValidation?.ok === false) postApplyWarnings.push("Canonical package validation failed.");
  if (routingValidation && routingValidation.exitCode !== 0) postApplyWarnings.push("Relationship route-health validation failed.");
  if (routingHealth?.before && ((routingHealth.before.invalid || 0) > 0 || (routingHealth.before.suspicious || 0) > 0)) {
    postApplyWarnings.push(`Canonical package route health is not clean: suspicious=${routingHealth.before.suspicious || 0}, invalid=${routingHealth.before.invalid || 0}.`);
  }
  if (routingHealth?.parseError) postApplyWarnings.push("Routing report could not be parsed.");
  if (imageExport && imageExport.exitCode !== 0) postApplyWarnings.push("Post-apply view image export failed.");
  const ok = postApplyWarnings.length === 0;
  return {
    ok,
    status: ok ? "appliedAndVerified" : "appliedWithVerificationFailures",
    input,
    patch,
    output,
    kind: requestedKind,
    cli,
    previewOnly: false,
    patchValidation,
    preview,
    apply,
    packageValidation,
    routingValidation,
    routingHealth,
    imageExport,
    warnings: postApplyWarnings
  };
}

async function resolveThinkComposerCli(rawPath) {
  const explicit = rawPath || process.env.THINKCOMPOSER_CLI;
  if (explicit) return resolveFile(explicit);
  const candidates = [
    path.join(DEFAULT_ROOT, "ThinkComposer.Cli", "bin", "Release", "ThinkComposer.Cli.exe"),
    path.join(DEFAULT_ROOT, "ThinkComposer.Cli", "bin", "Debug", "ThinkComposer.Cli.exe")
  ];
  for (const pathDirectory of String(process.env.PATH || "").split(path.delimiter).filter(Boolean)) {
    candidates.push(path.join(pathDirectory, "ThinkComposer.Cli.exe"));
    candidates.push(path.join(pathDirectory, "thinkcomposer.exe"));
  }
  for (const candidate of candidates) {
    if (await exists(candidate)) return path.resolve(candidate);
  }
  throw new Error("Cannot locate ThinkComposer.Cli.exe. Set THINKCOMPOSER_CLI or pass cliPath.");
}

function runCommand(command, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { windowsHide: true, shell: false });
    let stdout = "";
    let stderr = "";
    const append = (current, chunk) => `${current}${chunk}`.slice(-1024 * 1024);
    child.stdout.on("data", (chunk) => { stdout = append(stdout, chunk.toString()); });
    child.stderr.on("data", (chunk) => { stderr = append(stderr, chunk.toString()); });
    child.on("error", reject);
    child.on("close", (exitCode) => resolve({ exitCode, stdout: stdout.trim(), stderr: stderr.trim() }));
  });
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
  const rootManifest = tryReadJsonZipPart(zip, "manifest.json");
  if (rootManifest) {
    rootManifest._partUri = "/manifest.json";
    return rootManifest;
  }

  const sidecarManifest = tryReadJsonZipPart(zip, "Interchange/manifest.json");
  if (sidecarManifest) {
    sidecarManifest._partUri = "/Interchange/manifest.json";
    return sidecarManifest;
  }

  throw new Error("Missing /manifest.json or /Interchange/manifest.json in ThinkComposer container.");
}

function findZipEntry(zip, rawName) {
  const normalized = normalizePartUri(rawName);
  return zip.entries.find((entry) => entry.name === normalized) || null;
}

function tryReadJsonZipPart(zip, rawName) {
  const entry = findZipEntry(zip, rawName);
  if (!entry) return null;
  return JSON.parse(readZipEntry(zip, entry).toString("utf8"));
}

function embeddedKeyForPart(part, partName) {
  if (part?.kind === "embeddedDomain" || part?.kind === "domain") return "domain";
  if (part?.kind === "templateComposition") return "templateComposition";
  if (part?.kind === "composition") return "composition";
  const baseName = path.basename(partName, ".json");
  if (/^domain$/i.test(baseName)) return "domain";
  if (/^composition$/i.test(baseName)) return "composition";
  if (/^templatecomposition$/i.test(baseName)) return "templateComposition";
  return part?.kind || baseName;
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

function hasSnapshotState(document, expectedKind = null) {
  if (!document || typeof document !== "object" || Array.isArray(document)) return false;
  const kind = expectedKind || (document.format === "ThinkComposer.DomainJsonInterchange" ? "domain" :
    document.format === "ThinkComposer.JsonInterchange" ? "composition" : null);
  const compositionKeys = ["composition", "ideas", "relationships", "views"];
  const domainKeys = ["domain", "externalLanguages", "linkRoleVariants", "conceptDefinitionClusters",
    "relationshipDefinitionClusters", "markerClusters", "markerDefinitions", "tableDefinitionCategories",
    "fieldDefinitionCategories", "tableDefinitions", "conceptDefinitions", "relationshipDefinitions",
    "conceptDefinitionOutputTemplates", "relationshipDefinitionOutputTemplates", "relationshipCompatibility"];
  const keys = kind === "domain" ? domainKeys : kind === "composition" ? compositionKeys : compositionKeys.concat(domainKeys);
  return keys.some((key) => Object.prototype.hasOwnProperty.call(document, key));
}

function operationVisual(operation) {
  if (!operation || typeof operation !== "object") return null;
  if (operation.visual && typeof operation.visual === "object" && !Array.isArray(operation.visual)) return operation.visual;
  const nested = operation.set?.visual;
  return nested && typeof nested === "object" && !Array.isArray(nested) ? nested : null;
}

function operationRelationshipPlacement(operation) {
  const visual = operationVisual(operation);
  return isNonEmptyString(visual?.relationshipCenterPlacement) ? visual.relationshipCenterPlacement : null;
}

function operationHasCoordinate(operation) {
  return Number.isFinite(operation?.x) || Number.isFinite(operation?.y) ||
    Number.isFinite(operation?.set?.x) || Number.isFinite(operation?.set?.y);
}

function operationHasAutoRoute(operation) {
  return typeof operation?.autoRoute === "boolean" || typeof operation?.set?.autoRoute === "boolean";
}

function operationAutoRoute(operation) {
  if (typeof operation?.autoRoute === "boolean") return operation.autoRoute;
  return typeof operation?.set?.autoRoute === "boolean" ? operation.set.autoRoute : null;
}

function operationHasRelationshipVisualIntent(operation) {
  if (!operation || operation.entity !== "relationship" || operation.op === "delete") return false;
  if (["create", "place"].includes(operation.op)) return true;
  if (operationVisual(operation)) return true;
  if (operationAutoRoute(operation) === true) return true;
  if (isNonEmptyString(operation.representationId) || isNonEmptyString(operation.set?.representationId)) return true;
  return operationHasExplicitPlacement(operation);
}

function operationHasExplicitPlacement(operation) {
  if (!operation || typeof operation !== "object") return false;
  if (operationHasCoordinate(operation)) return true;
  if (Number.isFinite(operation.width) || Number.isFinite(operation.height) ||
      Number.isFinite(operation.set?.width) || Number.isFinite(operation.set?.height)) return true;
  return [operation.viewId, operation.viewTechName, operation.set?.viewId, operation.set?.viewTechName]
    .some(isNonEmptyString);
}

function containsPropertyDeep(value, propertyName) {
  if (!value || typeof value !== "object") return false;
  if (Array.isArray(value)) return value.some((item) => containsPropertyDeep(item, propertyName));
  if (Object.prototype.hasOwnProperty.call(value, propertyName)) return true;
  return Object.values(value).some((item) => containsPropertyDeep(item, propertyName));
}

function containsRouteGeometry(value) {
  if (!value || typeof value !== "object") return false;
  if (Array.isArray(value)) return value.some(containsRouteGeometry);
  if (Object.prototype.hasOwnProperty.call(value, "routePoints") ||
      Object.prototype.hasOwnProperty.call(value, "intermediatePosition")) return true;
  return Object.values(value).some(containsRouteGeometry);
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
