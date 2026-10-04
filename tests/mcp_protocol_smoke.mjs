import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..");
const entry = path.join(root, "MCP-Server", "build", "index.js");
const child = spawn(process.execPath, [entry], { cwd: root, env: { ...process.env, DSCONS_MCP_OFFLINE: "1" }, stdio: ["pipe", "pipe", "pipe"] });
let buffer = "";
let stderr = "";
const pending = new Map();
child.stdout.setEncoding("utf8");
child.stderr.setEncoding("utf8");
child.stdout.on("data", (chunk) => {
  buffer += chunk;
  while (true) {
    const newline = buffer.indexOf("\n");
    if (newline < 0) break;
    const line = buffer.slice(0, newline);
    buffer = buffer.slice(newline + 1);
    if (!line.trim()) continue;
    const message = JSON.parse(line);
    if (message.id !== undefined) pending.get(message.id)?.(message);
  }
});
child.stderr.on("data", (chunk) => { stderr += chunk; });

function request(id, method, params = {}) {
  return new Promise((resolve, reject) => {
    // Loading the local PDF/OCR dependencies can take more than five seconds
    // on a cold Windows CI or learner machine. Keep the protocol assertion
    // bounded without treating dependency startup time as a transport failure.
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`Timeout waiting for ${method}`)); }, 15000);
    pending.set(id, (message) => { clearTimeout(timer); pending.delete(id); resolve(message); });
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id, method, params })}\n`);
  });
}

try {
  const initialize = await request(1, "initialize", { protocolVersion: "2024-11-05", capabilities: {}, clientInfo: { name: "dscons-smoke", version: "test" } });
  if (initialize.error || initialize.result?.serverInfo?.name !== "dscons-revit-mcp") throw new Error(`initialize failed: ${JSON.stringify(initialize)}`);
  child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized" })}\n`);

  const listed = await request(2, "tools/list");
  const names = new Set((listed.result?.tools ?? []).map((item) => item.name));
  if (names.size !== 70) throw new Error(`tools/list must expose exactly 70 tools, received ${names.size}`);
  const legacy43 = [
    "system_status", "document_info", "get_active_view", "get_selection", "get_capabilities",
    "mep_element_detail", "mep_connector_network", "mep_filter_elements", "mep_qa_connectivity",
    "mep_preview", "mep_apply_preview", "mep_create_route", "mep_connect", "mep_disconnect",
    "mep_move_route", "mep_change_type", "mep_change_size", "bim_context_snapshot", "bim_model_catalog",
    "mep_network_explore", "coordination_links", "coordination_scan", "quantity_takeoff", "documentation_plan",
    "model_create_batch", "bim_changeset_preview", "bim_changeset_apply", "documentation_apply",
    "family_inspect", "family_axial_fan_preview", "family_axial_fan_apply", "family_load_place_preview",
    "family_load_place_apply", "family_shared_nested_probe_preview", "family_library_benchmark_preview", "family_routing_probe_preview", "family_break_into_ui_preflight", "family_break_into_ui_verify", "family_hosting_ui_preflight", "family_hosting_ui_verify", "family_source_inspect", "family_artifact_inspect", "family_spec_preview", "family_build_preview",
    "family_build_apply", "cad_geometry_inspect", "cad_to_revit_preview", "cad_to_revit_apply",
  ];
  if (legacy43.length !== 48) throw new Error("legacy tool baseline is invalid");
  const additive22 = ["dscons_knowledge_search", "family_acceptance_matrix", "revit_computer_use_assess", "source_to_revit_proposal", "mepf_evidence_readiness", "cad_annotation_assess", "manufacturer_catalog_inspect", "family_compatibility_assess", "mepf_engineering_review", "source_conflict_assess", "coordination_solid_scan", "coordination_issue_report_preview", "coordination_issue_report_apply", "coordination_section_preview", "coordination_section_apply", "model_transfer_extract", "model_transfer_destination_catalog", "model_transfer_plan", "model_transfer_preview", "model_transfer_apply", "model_transfer_reopen_verify", "model_transfer_report"];
  for (const name of [...legacy43, ...additive22]) {
    if (!names.has(name)) throw new Error(`tools/list is missing ${name}`);
  }
  const discontinuedTagPrefix = "family" + "_tag_";
  for (const name of names) if (name.startsWith(discontinuedTagPrefix)) throw new Error(`tools/list must not expose discontinued Family Tag tool ${name}`);
  for (const tool of listed.result?.tools ?? []) if (!tool.inputSchema || tool.inputSchema.type !== "object") throw new Error(`${tool.name} must retain an object input schema`);

  const fanPreview = (listed.result?.tools ?? []).find((item) => item.name === "family_axial_fan_preview");
  const required = new Set(fanPreview?.inputSchema?.required ?? []);
  for (const field of ["approved_demo_directory", "family_name", "type_name", "diameter_mm", "length_mm", "blade_count", "type_code", "connector_mode"])
    if (!required.has(field)) throw new Error(`family_axial_fan_preview schema is missing required ${field}`);
  if (required.has("template_path")) throw new Error("family_axial_fan_preview must auto-resolve template_path for learners");
  if (!fanPreview?.inputSchema?.properties?.template_path) throw new Error("family_axial_fan_preview must retain optional template_path override");

  const solidScan = (listed.result?.tools ?? []).find((item) => item.name === "coordination_solid_scan");
  const solidRequired = new Set(solidScan?.inputSchema?.required ?? []);
  for (const field of ["context_id", "scan_mode"])
    if (!solidRequired.has(field)) throw new Error(`coordination_solid_scan schema is missing required ${field}`);
  const sectionApply = (listed.result?.tools ?? []).find((item) => item.name === "coordination_section_apply");
  if ((sectionApply?.inputSchema?.required ?? []).join(",") !== "preview_id") throw new Error("coordination_section_apply must accept only preview_id");

  // Keep this test fully offline: calling a Revit tool would wait for a live
  // bridge. The local implementation must still return a typed response.
  const called = await request(3, "tools/call", { name: "dscons_knowledge_search", arguments: { approved_knowledge_directory: "\\\\server\\blocked", query: "test" } });
  if (!called.result?.isError || !called.result?.content?.length) throw new Error(`offline tools/call did not return a typed local error: ${JSON.stringify(called)}`);
  const acceptance = await request(4, "tools/call", { name: "family_acceptance_matrix", arguments: {} });
  if (acceptance.result?.isError || acceptance.result?.structuredContent?.evidence_validation?.phase_complete !== false)
    throw new Error(`family_acceptance_matrix must return an incomplete typed matrix without evidence: ${JSON.stringify(acceptance)}`);
  console.log(`PASS MCP protocol: initialize, notification, tools/list (${names.size} tools), tools/call`);
} finally {
  child.kill();
  if (stderr.trim()) console.error(stderr.trim());
}
