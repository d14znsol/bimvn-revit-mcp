import { createHash } from "node:crypto";
import type { Tool } from "@modelcontextprotocol/sdk/types.js";

export interface EmbeddedToolCapability {
  name: string;
  isWrite: boolean;
  inputSchema: unknown;
}

export interface EmbeddedCapabilityManifest {
  schemaVersion: 1;
  toolCount: number;
  tools: EmbeddedToolCapability[];
  canonicalPayload: string;
  schemaFingerprintSha256: string;
}

/** Canonical JSON deliberately has no locale or object-insertion-order dependency. */
export function canonicalJson(value: unknown): string {
  if (value === null || typeof value === "boolean" || typeof value === "number" || typeof value === "string") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (!value || typeof value !== "object") throw new Error("Capability schema must be JSON data.");
  const record = value as Record<string, unknown>;
  return `{${Object.keys(record).sort().filter((key) => record[key] !== undefined).map((key) => `${JSON.stringify(key)}:${canonicalJson(record[key])}`).join(",")}}`;
}

export function createEmbeddedCapabilityManifest(tools: Tool[]): EmbeddedCapabilityManifest {
  const capabilities = tools.map((tool) => ({
    name: tool.name,
    isWrite: tool.annotations?.readOnlyHint !== true,
    inputSchema: tool.inputSchema,
  })).sort((left, right) => left.name.localeCompare(right.name));
  if (capabilities.some((tool, index) => !tool.name || (index > 0 && tool.name === capabilities[index - 1].name)))
    throw new Error("Embedded capability manifest requires unique, non-empty tool names.");
  const canonicalPayload = canonicalJson({ schemaVersion: 1, tools: capabilities });
  return {
    schemaVersion: 1,
    toolCount: capabilities.length,
    tools: capabilities,
    canonicalPayload,
    schemaFingerprintSha256: createHash("sha256").update(canonicalPayload, "utf8").digest("hex"),
  };
}
