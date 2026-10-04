#!/usr/bin/env node
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { CallToolRequestSchema, ListToolsRequestSchema } from "@modelcontextprotocol/sdk/types.js";
import { callRevit } from "./socket.js";
import { KnowledgeSearchError, searchKnowledge } from "./knowledge-search.js";
import { FamilyEvidenceError, inspectCadGeometry, inspectFamilySource, previewFamilySpec, readEvidenceRecord, recordFamilyBuildArtifact } from "./family-evidence.js";
import { assessFamilyAcceptance } from "./family-acceptance.js";
import { assessComputerUse, ComputerUseEvaluationError } from "./computer-use-evaluation.js";
import { previewSourceToRevit } from "./source-to-revit.js";
import { assessCadAnnotations, assessFamilyCompatibility, assessMepfEvidenceReadiness, assessSourceConflicts, inspectManufacturerCatalog, reviewMepfEngineering } from "./mepf-evidence-governance.js";
import { ModelTransferError, completeModelTransferApply, completeModelTransferReopenVerification, planModelTransfer, prepareModelTransferPreview, readModelTransferPlan, readModelTransferPreview, readModelTransferReport, recordModelTransferDestinationCatalog, recordModelTransferPackage, recordModelTransferPreview } from "./model-transfer.js";
import { builtInTools, toolsFromCapabilities } from "./tools/index.js";
import { EmbeddedApprovalError, requireEmbeddedApproval } from "./embedded-approval.js";

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
const localFamilyAcceptanceCapability = {
  name: "family_acceptance_matrix",
  description: "Read-only Family MEP acceptance matrix and Revit 2023 → 2025 evidence-completeness assessment.",
  isWrite: false,
  isDestructive: false,
  isAvailable: true,
  scope: "local_private_evidence",
  requiresFreshContext: false,
  prerequisites: ["Evidence records must name a matrix fixture, required gate, Revit version, record ID and completion timestamp; RFA reopen records also require SHA-256."],
  limitations: ["Checks evidence coverage only. It does not create RFA/Project evidence, persist paths or element IDs, or replace Revit read-back."],
};
const localComputerUseCapability = {
  name: "revit_computer_use_assess",
  description: "Read-only assessment of supplied Revit Computer Use runtime evidence against the bounded whitelist and promotion gate.",
  isWrite: false,
  isDestructive: false,
  isAvailable: true,
  scope: "local_private_evidence",
  requiresFreshContext: false,
  prerequisites: ["Each run must include fresh anchors, original-resolution screenshot hashes, dialog/security observations, cancel evidence and independent Revit read-back."],
  limitations: ["Assesses evidence only; never controls Revit or promotes a workflow without 20 consecutive safe runs and complete runtime-matrix coverage."],
};
const localSourceProposalCapability = {
  name: "source_to_revit_proposal",
  description: "Read-only MEPF-first mapping from SourceEvidence v3 to reviewed PDF/DWG/DXF/construction-photo Project reconstruction plans, PDF multi-Type Family, linework, image Family or bounded route proposals.",
  isWrite: false,
  isDestructive: false,
  isAvailable: true,
  scope: "local_private_evidence",
  requiresFreshContext: false,
  prerequisites: ["MEPF Project reconstruction requires confirmed coordinates/Levels/types/systems/geometry/sizes, immutable source locators and trusted DWG or registered-photo evidence; Family mappings keep their existing bounded requirements."],
  limitations: ["Returns a proposal only. It never infers concealed services/connectors/penetrations, creates an Apply token, controls Revit, explodes/imports DWG or changes a model."],
};
const localMepfEvidenceGovernanceCapability = {
  name: "mepf_evidence_readiness",
  description: "Local-only MEPF evidence/readiness, CAD annotation, manufacturer catalogue, Family compatibility, engineering-boundary and cross-source conflict assessments.",
  isWrite: false,
  isDestructive: false,
  isAvailable: true,
  scope: "local_private_evidence",
  requiresFreshContext: false,
  prerequisites: ["Every source must be an immutable local SourceEvidence record; all semantic decisions remain explicitly engineer-confirmed."],
  limitations: ["Never downloads catalogue/Family content, guesses annotation targets, performs certified MEP calculations, controls Revit or applies a Change Set."],
};
const localModelTransferCapability = {
  name: "model_transfer_plan",
  description: "Local-only orchestration for checksum-bound Revit 2025 → 2023 native MEPF reconstruction; it blocks incomplete dependency groups and does not upload RVT data.",
  isWrite: false,
  isDestructive: false,
  isAvailable: true,
  scope: "local_private_transfer_records",
  requiresFreshContext: false,
  prerequisites: ["A ModelTransferPackage from Revit 2025, a destination catalog from Revit 2023, explicit compatible mappings, and a confirmed staging Project are required."],
  limitations: ["V1 never down-saves RVT/RFA, substitutes IFC/DirectShape, applies a shared-coordinate transform, repairs a network, or certifies system calculation/circuit/panel parity."],
};
const nodeLocalToolNames = new Set([
  localKnowledgeCapability.name,
  localFamilyAcceptanceCapability.name,
  localComputerUseCapability.name,
  localSourceProposalCapability.name,
  localMepfEvidenceGovernanceCapability.name,
  "cad_annotation_assess", "manufacturer_catalog_inspect", "family_compatibility_assess", "mepf_engineering_review", "source_conflict_assess",
  "model_transfer_extract", "model_transfer_destination_catalog", "model_transfer_plan", "model_transfer_preview", "model_transfer_apply", "model_transfer_reopen_verify", "model_transfer_report",
]);

function addNodeLocalTools(tools: typeof builtInTools): typeof builtInTools {
  const merged = [...tools];
  for (const localTool of builtInTools.filter((tool) => nodeLocalToolNames.has(tool.name)))
    if (!merged.some((tool) => tool.name === localTool.name)) merged.push(localTool);
  return merged;
}

function addNodeLocalCapabilities(result: Record<string, unknown>): Record<string, unknown> {
  const tools = Array.isArray(result.tools) ? result.tools : [];
  for (const capability of [localKnowledgeCapability, localFamilyAcceptanceCapability, localComputerUseCapability, localSourceProposalCapability, localMepfEvidenceGovernanceCapability, localModelTransferCapability]) {
    if (!tools.some((tool) => tool && typeof tool === "object" && (tool as { name?: unknown }).name === capability.name)) tools.push(capability);
  }
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
    if (response.success && response.resultJson) return { tools: addNodeLocalTools(toolsFromCapabilities(JSON.parse(response.resultJson))) };
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
    if (name === "family_acceptance_matrix") {
      const result = assessFamilyAcceptance((request.params.arguments ?? {}) as Record<string, unknown>);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "revit_computer_use_assess") {
      const result = assessComputerUse((request.params.arguments ?? {}) as Record<string, unknown>);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "mepf_evidence_readiness") {
      const result = assessMepfEvidenceReadiness((request.params.arguments ?? {}) as Record<string, unknown>);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "cad_annotation_assess") {
      const result = assessCadAnnotations((request.params.arguments ?? {}) as Record<string, unknown>);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "manufacturer_catalog_inspect") {
      const result = inspectManufacturerCatalog((request.params.arguments ?? {}) as Record<string, unknown>);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "family_compatibility_assess") {
      const result = assessFamilyCompatibility((request.params.arguments ?? {}) as Record<string, unknown>);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "mepf_engineering_review") {
      const result = reviewMepfEngineering((request.params.arguments ?? {}) as Record<string, unknown>);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "source_conflict_assess") {
      const result = assessSourceConflicts((request.params.arguments ?? {}) as Record<string, unknown>);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "source_to_revit_proposal") {
      const result = previewSourceToRevit((request.params.arguments ?? {}) as Record<string, unknown>);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    const argumentsValue = (request.params.arguments ?? {}) as Record<string, unknown>;
    if (name === "model_transfer_plan") {
      const result = planModelTransfer(argumentsValue);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "model_transfer_report") {
      const result = argumentsValue.plan_id !== undefined
        ? readModelTransferPlan(String(argumentsValue.plan_id))
        : argumentsValue.transfer_preview_id !== undefined
          ? readModelTransferPreview(String(argumentsValue.transfer_preview_id))
          : argumentsValue.report_id !== undefined
            ? readModelTransferReport(String(argumentsValue.report_id))
            : (() => { throw new ModelTransferError("TransferInvalid", "model_transfer_report requires plan_id, transfer_preview_id, or report_id."); })();
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "model_transfer_extract" || name === "model_transfer_destination_catalog") {
      const response = await callRevit(name, argumentsValue, "mcp-model-transfer-read");
      if (!response.success || !response.resultJson) throw new ModelTransferError("TransferBlocked", response.errorMessage ?? `${name} did not return a Revit payload.`);
      const result = name === "model_transfer_extract"
        ? recordModelTransferPackage(JSON.parse(response.resultJson))
        : recordModelTransferDestinationCatalog(JSON.parse(response.resultJson));
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "model_transfer_preview") {
      const prepared = prepareModelTransferPreview(argumentsValue);
      const response = await callRevit("bim_changeset_preview", prepared.bridge_arguments as Record<string, unknown>, "mcp-model-transfer-preview");
      if (!response.success || !response.resultJson) throw new ModelTransferError("TransferBlocked", response.errorMessage ?? "Revit rejected the transfer rollback preview.");
      const result = recordModelTransferPreview({ plan: prepared.plan, bridge_preview: JSON.parse(response.resultJson) });
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "model_transfer_apply") {
      const preview = readModelTransferPreview(String(argumentsValue.transfer_preview_id ?? ""));
      await requireEmbeddedApproval("bim_changeset_apply", { preview_id: preview.bridge_preview_id });
      const response = await callRevit("bim_changeset_apply", { preview_id: preview.bridge_preview_id }, "mcp-model-transfer-apply");
      if (!response.success || !response.resultJson) throw new ModelTransferError("TransferBlocked", response.errorMessage ?? "Revit rolled back the transfer apply.");
      const result = completeModelTransferApply({ transfer_preview_id: preview.transfer_preview_id, bridge_apply: JSON.parse(response.resultJson) });
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "model_transfer_reopen_verify") {
      const report = readModelTransferReport(String(argumentsValue.report_id ?? ""));
      const response = await callRevit("model_transfer_reopen_verify", { expected_target_revit_version: "2023", target_elements: report.source_to_destination }, "mcp-model-transfer-reopen-verify");
      if (!response.success || !response.resultJson) throw new ModelTransferError("TransferBlocked", response.errorMessage ?? "Revit rejected the read-only reopen verification.");
      const result = completeModelTransferReopenVerification({ report_id: report.report_id, bridge_reopen: JSON.parse(response.resultJson) });
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "family_source_inspect") {
      const result = await inspectFamilySource(argumentsValue);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "cad_geometry_inspect") {
      let inspectArguments = argumentsValue;
      let revitAdapterEvidence: Record<string, unknown> | undefined;
      if (argumentsValue.use_revit_trusted_adapter === true) {
        const response = await callRevit(name, argumentsValue, "mcp-trusted-dwg-inspection");
        if (!response.success || !response.resultJson) throw new FamilyEvidenceError("SourceUnreadable", `Revit trusted DWG inspection failed: ${response.errorMessage ?? response.errorCode ?? "unknown bridge error"}`);
        revitAdapterEvidence = JSON.parse(response.resultJson) as Record<string, unknown>;
        if (!revitAdapterEvidence.trusted_adapter_manifest || revitAdapterEvidence.active_project_touched !== false || revitAdapterEvidence.temporary_document_rolled_back !== true || revitAdapterEvidence.temporary_document_saved !== false)
          throw new FamilyEvidenceError("EvidenceConflict", "Revit DWG adapter did not prove disposable-document rollback/no-save isolation.");
        inspectArguments = { ...argumentsValue, trusted_adapter_manifest: revitAdapterEvidence.trusted_adapter_manifest };
      }
      const result = await inspectCadGeometry(inspectArguments);
      if (revitAdapterEvidence) result.adapter_runtime_evidence = { active_project_touched: false, temporary_document_rolled_back: true, temporary_document_saved: false, boundary: revitAdapterEvidence.boundary, skipped_geometry: revitAdapterEvidence.skipped_geometry };
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    if (name === "family_spec_preview") {
      const result = previewFamilySpec(argumentsValue);
      return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
    }
    const bridgeArguments = { ...argumentsValue };
    if (name === "family_build_preview") {
      const specId = String(argumentsValue.spec_id ?? "");
      const spec = readEvidenceRecord(specId);
      const readiness = (spec.evidence_governance as Record<string, unknown> | undefined)?.readiness as Record<string, unknown> | undefined;
      if (spec.status !== "human_confirmed" || readiness?.status !== "passed")
        throw new FamilyEvidenceError("EvidenceInvalid", "family_build_preview requires a human-confirmed FamilySpec with matching passed information-readiness evidence.");
      bridgeArguments.__dscons_spec_payload = spec;
    }
    if (name === "cad_to_revit_preview" && argumentsValue.mapping === "route") {
      bridgeArguments.__dscons_source_proposal = previewSourceToRevit({
        evidence_ids: [String(argumentsValue.evidence_id ?? "")], mapping: "route_centerline",
        readiness_record_id: argumentsValue.readiness_record_id,
        layers: argumentsValue.layers, confirm_units: argumentsValue.confirm_units,
        route: {
          kind: argumentsValue.kind, type_id: argumentsValue.type_id, system_type_id: argumentsValue.system_type_id,
          level_id: argumentsValue.level_id, elevation_mm: argumentsValue.elevation_mm, tolerance_mm: argumentsValue.tolerance_mm,
        },
      });
    }
    await requireEmbeddedApproval(name, bridgeArguments);
    const response = await callRevit(name, bridgeArguments, "mcp-tool-call");
    if (!response.success) {
      return { isError: true, content: [{ type: "text", text: `[${response.errorCode ?? "BridgeError"}] ${response.errorMessage ?? "Revit command failed."}` }] };
    }
    const parsed = response.resultJson ? JSON.parse(response.resultJson) : {};
    if (name === "family_build_apply" && parsed && typeof parsed === "object" && (parsed as Record<string, unknown>).schema_version === "3.0" && typeof (parsed as Record<string, unknown>).spec_id === "string") {
      const artifact = recordFamilyBuildArtifact(String((parsed as Record<string, unknown>).spec_id), parsed as Record<string, unknown>);
      (parsed as Record<string, unknown>).artifact_record_id = artifact.artifact_id;
    }
    const result = name === "get_capabilities" && parsed && typeof parsed === "object"
      ? addNodeLocalCapabilities(parsed as Record<string, unknown>)
      : parsed;
    return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], structuredContent: result };
  } catch (error) {
    const prefix = error instanceof KnowledgeSearchError || error instanceof FamilyEvidenceError || error instanceof EmbeddedApprovalError || error instanceof ComputerUseEvaluationError || error instanceof ModelTransferError ? `[${error.code}] ` : "";
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
