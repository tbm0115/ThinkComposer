#!/usr/bin/env node

import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const serverPath = path.join(scriptDir, "thinkcomposer-mcp.mjs");
const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "thinkcomposer-mcp-routing-smoke-"));

try {
  const fixturePath = path.join(tempRoot, "repeated-shortcuts.json");
  await fs.writeFile(fixturePath, `${JSON.stringify(buildFixture(), null, 2)}\n`, "utf8");
  const validation = await callTool("thinkcomposer_validate_json", { path: fixturePath, kind: "composition" });
  const geometry = validation?.summary?.routeGeometry;
  const corridorWarnings = (validation?.warnings || [])
    .filter((warning) => /Relationship center outside the local endpoint corridor/i.test(warning));

  assert(validation?.ok === true, `fixture validation failed: ${(validation?.errors || []).join(" ")}`);
  assert(geometry?.connectors === 4, `expected 4 connectors, got ${geometry?.connectors}`);
  assert(geometry?.suspiciousCenters === 0,
    `representation-aware endpoint resolution reported ${geometry?.suspiciousCenters} suspicious centers`);
  assert(corridorWarnings.length === 0, `unexpected corridor warnings: ${corridorWarnings.join(" ")}`);

  process.stdout.write(`${JSON.stringify({
    passed: true,
    scenarios: [
      "connector-representation-id-precedes-repeated-shortcut-identity",
      "repeated-shortcut-fallback-is-nearest-and-identity-safe"
    ],
    connectors: geometry.connectors,
    suspiciousCenters: geometry.suspiciousCenters,
    warnings: validation.warnings
  }, null, 2)}\n`);
} finally {
  const resolvedTemp = path.resolve(tempRoot);
  const resolvedSystemTemp = path.resolve(os.tmpdir());
  if (resolvedTemp.startsWith(`${resolvedSystemTemp}${path.sep}`)) {
    await fs.rm(resolvedTemp, { recursive: true, force: true });
  }
}

function buildFixture() {
  const relationship = (id, techName, leftId, rightId, leftLinkId, rightLinkId) => ({
    id,
    techName,
    links: [
      { id: leftLinkId, ideaId: leftId },
      { id: rightLinkId, ideaId: rightId }
    ]
  });
  const visual = (ideaId, representationId, x, y, connectors) => ({
    ideaId,
    representationId,
    x,
    y,
    width: 20,
    height: 20,
    ...(connectors ? { connectors } : {})
  });

  return {
    format: "ThinkComposer.JsonInterchange",
    formatVersion: 2,
    application: "ThinkComposer",
    composition: { id: "composition", name: "MCP routing smoke", techName: "MCP_Routing_Smoke" },
    ideas: [],
    relationships: [
      relationship("rel-exact", "Rel_Exact", "idea-a", "idea-b", "link-a", "link-b"),
      relationship("rel-fallback", "Rel_Fallback", "idea-c", "idea-d", "link-c", "link-d")
    ],
    views: [
      {
        id: "view-exact",
        techName: "Exact",
        visuals: [
          visual("idea-a", "a-local", 0, 0),
          visual("idea-b", "b-local", 200, 0),
          visual("rel-exact", "rel-exact-rep", 100, 0, [
            {
              id: "connector-a",
              linkId: "link-a",
              associatedIdeaId: "idea-a",
              originRepresentationId: "rel-exact-rep",
              originIdeaId: "rel-exact",
              targetRepresentationId: "a-local",
              targetIdeaId: "idea-a",
              routePoints: []
            },
            {
              id: "connector-b",
              linkId: "link-b",
              associatedIdeaId: "idea-b",
              originRepresentationId: "b-local",
              originIdeaId: "idea-b",
              targetRepresentationId: "rel-exact-rep",
              targetIdeaId: "rel-exact",
              routePoints: []
            }
          ]),
          visual("idea-a", "a-remote", 2000, 2000),
          visual("idea-b", "b-remote", 2300, 2000)
        ]
      },
      {
        id: "view-fallback",
        techName: "Fallback",
        visuals: [
          visual("idea-c", "c-local", 0, 200),
          visual("idea-d", "d-local", 200, 200),
          visual("rel-fallback", "rel-fallback-rep", 100, 200, [
            {
              id: "connector-c",
              linkId: "link-c",
              associatedIdeaId: "idea-c",
              originIdeaId: "rel-fallback",
              targetIdeaId: "idea-c",
              routePoints: []
            },
            {
              id: "connector-d",
              linkId: "link-d",
              associatedIdeaId: "idea-d",
              originIdeaId: "idea-d",
              targetIdeaId: "rel-fallback",
              routePoints: []
            }
          ]),
          visual("unrelated-idea", "unrelated-nearest", 100, 200),
          visual("idea-c", "c-remote", 2000, 2200),
          visual("idea-d", "d-remote", 2300, 2200)
        ]
      }
    ],
    operations: [],
    warnings: []
  };
}

function callTool(name, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [serverPath], { stdio: ["pipe", "pipe", "pipe"] });
    let stdout = "";
    let stderr = "";
    const timeout = setTimeout(() => {
      child.kill();
      reject(new Error(`MCP smoke timed out. ${stderr}`.trim()));
    }, 15000);
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (chunk) => { stdout += chunk; });
    child.stderr.on("data", (chunk) => { stderr += chunk; });
    child.on("error", (error) => {
      clearTimeout(timeout);
      reject(error);
    });
    child.on("close", (code) => {
      clearTimeout(timeout);
      try {
        if (code !== 0) throw new Error(`MCP server exited ${code}. ${stderr}`.trim());
        const line = stdout.split(/\r?\n/).find((candidate) => candidate.trim().length > 0);
        if (!line) throw new Error(`MCP server returned no response. ${stderr}`.trim());
        const response = JSON.parse(line);
        if (response.error) throw new Error(response.error.message || JSON.stringify(response.error));
        resolve(JSON.parse(response.result.content[0].text));
      } catch (error) {
        reject(error);
      }
    });
    child.stdin.end(`${JSON.stringify({
      jsonrpc: "2.0",
      id: 1,
      method: "tools/call",
      params: { name, arguments: args }
    })}\n`);
  });
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
