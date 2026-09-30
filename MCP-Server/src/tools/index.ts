import type { Tool } from "@modelcontextprotocol/sdk/types.js";
import { defineTool, emptySchema } from "./common.js";
import { registerBimTools } from "./bim-tools.js";
import { registerCombineTools } from "./combine-tools.js";
import { registerFamilyTools } from "./family-tools.js";
import { registerFamilyPlatformTools } from "./family-platform-tools.js";
import { registerMepTools } from "./mep-tools.js";
import { registerModelTransferTools } from "./model-transfer-tools.js";
import { registerReadTools } from "./read-tools.js";

/**
 * The MCP tool registry. This file only composes tool modules; Revit API
 * implementation remains in MCP/Commands and is discovered at runtime.
 */
export function registerRevitTools(): Tool[] {
  return [...registerReadTools(), ...registerMepTools(), ...registerBimTools(), ...registerModelTransferTools(), ...registerFamilyTools(), ...registerFamilyPlatformTools(), ...registerCombineTools()];
}

export const builtInTools: Tool[] = registerRevitTools();

export function toolsFromCapabilities(value: unknown): Tool[] {
  if (!value || typeof value !== "object" || !("tools" in value) || !Array.isArray(value.tools)) return builtInTools;
  const result: Tool[] = [];
  for (const item of value.tools) {
    if (!item || typeof item !== "object") continue;
    const capability = item as {
      name?: unknown; description?: unknown; isWrite?: unknown; isDestructive?: unknown;
      scope?: unknown; requiresFreshContext?: unknown; prerequisites?: unknown; limitations?: unknown;
    };
    if (typeof capability.name !== "string") continue;
    const known = builtInTools.find((candidate) => candidate.name === capability.name);
    const notes: string[] = [];
    if (typeof capability.scope === "string") notes.push(`Scope: ${capability.scope}.`);
    if (capability.requiresFreshContext === true) notes.push("Requires fresh Revit context in this turn.");
    if (Array.isArray(capability.prerequisites) && capability.prerequisites.length > 0)
      notes.push(`Prerequisites: ${capability.prerequisites.filter((value): value is string => typeof value === "string").join(" ")}`);
    if (Array.isArray(capability.limitations) && capability.limitations.length > 0)
      notes.push(`Limits: ${capability.limitations.filter((value): value is string => typeof value === "string").join(" ")}`);
    const baseDescription = String(capability.description ?? known?.description ?? capability.name);
    result.push({
      ...(known ?? defineTool(capability.name, String(capability.description ?? capability.name), emptySchema, Boolean(capability.isWrite))),
      description: notes.length > 0 ? `${baseDescription} ${notes.join(" ")}` : baseDescription,
      annotations: { readOnlyHint: !Boolean(capability.isWrite), destructiveHint: Boolean(capability.isDestructive) },
    });
  }
  // These are implemented in the Node server, not in Revit. Preserve them
  // when an otherwise current bridge advertises only its Revit catalog.
  for (const local of builtInTools.filter((tool) => tool.name === "dscons_knowledge_search" || tool.name === "family_acceptance_matrix" || tool.name === "revit_computer_use_assess" || tool.name === "source_to_revit_proposal" || tool.name === "mepf_evidence_readiness" || tool.name === "cad_annotation_assess" || tool.name === "manufacturer_catalog_inspect" || tool.name === "family_compatibility_assess" || tool.name === "mepf_engineering_review" || tool.name === "source_conflict_assess" || tool.name.startsWith("model_transfer_"))) {
    if (!result.some((tool) => tool.name === local.name)) result.push(local);
  }
  return result.length > 0 ? result : builtInTools;
}
