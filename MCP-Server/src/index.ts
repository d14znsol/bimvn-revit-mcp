#!/usr/bin/env node
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { CallToolRequestSchema, ListToolsRequestSchema } from "@modelcontextprotocol/sdk/types.js";
import { callRevit } from "./socket.js";
import { KnowledgeSearchError, searchKnowledge } from "./knowledge-search.js";
import { FamilyEvidenceError, inspectFamilySource, previewFamilySpec, readEvidenceRecord } from "./family-evidence.js";
import { builtInTools, toolsFromCapabilities } from "./tools/index.js";

const localKnowledgeCapability = {
  name: "dscons_knowledge_search",
  description: "Local-only search of explicitly approved course summaries; returns bounded citation evidence and never uploads course content.",
  isWrite: false,
  isDestructive: false,
  isAvailable: true,
  scope: "local_private_filesystem",
  requiresFreshContext: false,
  prerequisites: ["approved_knowledge_directory must be a local directory explicitly supplied by the user."],
  limitations: ["Reads only TỔNG HỢP KHÓA HỌC.md and *_summary.md; UNC, traversal and raw transcripts are excluded."],
};

function addNodeLocalCapabilities(result: Record<string, unknown>): Record<string, unknown> {
  const tools = Array.isArray(result.tools) ? result.tools : [];
  if (!tools.some((tool) => tool && typeof tool === "object" && (tool as { name?: unknown }).name === localKnowledgeCapability.name))
    tools.push(localKnowledgeCapability);
  return { ...result, tools };
}

const server = new Server(
  { name: "dscons-revit-mcp", version: "0.1.0" },
  { capabilities: { tools: {} } }
);

server.setRequestHandler(ListToolsRequestSchema, async () => {
  // CI/offline protocol checks must exercise the built-in registry rather than
  // an older live Revit session that may still advertise a previous catalog.
  if (process.env.DSCONS_MCP_OFFLINE === "1") return { tools: builtInTools };
  try {
    const response = await callRevit("get_capabilities", {}, "mcp-tool-discovery");
    if (response.success && response.resultJson) return { tools: toolsFromCapabilities(JSON.parse(response.resultJson)) };
  } catch (error) {
    console.error(`[DSCons MCP] Revit unavailable during tools/list: ${error instanceof Error ? error.message : String(error)}`);
  }
  return { tools: builtInTools };
});

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const name = request.params.name;
  try {
    if (name === "dscons_knowledge_search") {
      const result = await searchKnowledge((request.params.arguments ?? {}) as {
        approved_knowledge_directory: string; query: string; course_filter?: string; limit?: number;
      });
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    const argumentsValue = (request.params.arguments ?? {}) as Record<string, unknown>;
    if (name === "family_source_inspect") {
      const result = await inspectFamilySource(argumentsValue);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "family_spec_preview") {
      const result = previewFamilySpec(argumentsValue);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    const bridgeArguments = { ...argumentsValue };
    if (name === "family_build_preview") {
      const specId = String(argumentsValue.spec_id ?? "");
      bridgeArguments.__dscons_spec_payload = readEvidenceRecord(specId);
    }
    const response = await callRevit(name, bridgeArguments, "mcp-tool-call");
    if (!response.success) {
      return { isError: true, content: [{ type: "text", text: `[${response.errorCode ?? "BridgeError"}] ${response.errorMessage ?? "Revit command failed."}` }] };
    }
    const parsed = response.resultJson ? JSON.parse(response.resultJson) : {};
    const result = name === "get_capabilities" && parsed && typeof parsed === "object"
      ? addNodeLocalCapabilities(parsed as Record<string, unknown>)
      : parsed;
    return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
  } catch (error) {
    const prefix = error instanceof KnowledgeSearchError || error instanceof FamilyEvidenceError ? `[${error.code}] ` : "";
    return { isError: true, content: [{ type: "text", text: prefix + (error instanceof Error ? error.message : String(error)) }] };
  }
});

async function main(): Promise<void> {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("[DSCons MCP] stdio server ready; waiting for Revit bridge.");
}

main().catch((error) => {
  console.error("[DSCons MCP] startup failed", error);
  process.exitCode = 1;
});
