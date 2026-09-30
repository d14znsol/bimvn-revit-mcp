type Json = Record<string, unknown>;

export const FAMILY_ACCEPTANCE_MATRIX_VERSION = "1.0";
export const familyAcceptanceRuntimeVersions = ["2023", "2025"] as const;

type RuntimeVersion = typeof familyAcceptanceRuntimeVersions[number];

export interface FamilyAcceptanceFixture {
  fixture_id: string;
  group: "hvac" | "plumbing" | "fire_protection" | "electrical" | "elv" | "pathway" | "support" | "annotation" | "platform";
  category: string;
  behavior: string;
  required_gates: readonly string[];
}

const familyRfaGate = "family_rfa_reopen";
const sourceGate = "source_evidence";
const flexGate = "independent_min_nominal_max_flex";
const projectPlaceGate = "project_copy_load_place";

/**
 * This is an acceptance registry, not an adapter registry. A fixture can only
 * be marked PASS after its named Revit-version evidence exists. Keeping it in
 * Node makes the release decision visible even when no Project is open.
 */
export const familyAcceptanceFixtures: readonly FamilyAcceptanceFixture[] = [
  { fixture_id: "hvac_equipment", group: "hvac", category: "mechanical_equipment", behavior: "fan_or_fcu_ahu_with_duct_ports", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_duct", projectPlaceGate] },
  { fixture_id: "hvac_air_terminal", group: "hvac", category: "air_terminal", behavior: "terminal_with_2d_3d_visibility_and_duct_port", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_duct", projectPlaceGate] },
  { fixture_id: "hvac_damper_accessory", group: "hvac", category: "duct_accessory", behavior: "damper_or_inline_accessory", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_duct", projectPlaceGate] },
  { fixture_id: "duct_elbow", group: "hvac", category: "duct_fitting", behavior: "parameterized_45_90_sweep_elbow", required_gates: [sourceGate, "controlled_ui_profile_label", familyRfaGate, flexGate, "connector_duct", "routing_preference", "project_network"] },
  { fixture_id: "duct_branch_fittings", group: "hvac", category: "duct_fitting", behavior: "tee_wye_cross_with_multi_port_topology", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_duct", "routing_preference", "project_network"] },
  { fixture_id: "duct_transition_union_silencer", group: "hvac", category: "duct_fitting", behavior: "transition_union_and_silencer", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_duct", "routing_preference", "project_network"] },
  { fixture_id: "plumbing_equipment", group: "plumbing", category: "mechanical_equipment", behavior: "pump_or_tank_with_pipe_ports", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_pipe", projectPlaceGate] },
  { fixture_id: "plumbing_fixture", group: "plumbing", category: "plumbing_fixture", behavior: "fixture_with_host_or_level_placement", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_pipe", projectPlaceGate, "hosted_placement"] },
  { fixture_id: "pipe_accessory", group: "plumbing", category: "pipe_accessory", behavior: "valve_filter_or_flexible_connector", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_pipe", projectPlaceGate, "break_into_or_valve_break_into"] },
  { fixture_id: "pipe_elbow", group: "plumbing", category: "pipe_fitting", behavior: "parameterized_45_90_sweep_elbow", required_gates: [sourceGate, "controlled_ui_profile_label", familyRfaGate, flexGate, "connector_pipe", "routing_preference", "project_network"] },
  { fixture_id: "pipe_branch_fittings", group: "plumbing", category: "pipe_fitting", behavior: "tee_wye_cross_with_multi_port_topology", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_pipe", "routing_preference", "project_network"] },
  { fixture_id: "pipe_transition_union", group: "plumbing", category: "pipe_fitting", behavior: "transition_union", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_pipe", "routing_preference", "project_network"] },
  { fixture_id: "fire_protection", group: "fire_protection", category: "sprinkler", behavior: "sprinkler_valve_set_or_cabinet", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_pipe", projectPlaceGate, "hosted_placement"] },
  { fixture_id: "electrical_power", group: "electrical", category: "electrical_equipment", behavior: "panel_transformer_or_source", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_electrical", projectPlaceGate, "circuit_panel_load_propagation"] },
  { fixture_id: "lighting_photometric", group: "electrical", category: "lighting_fixture", behavior: "light_source_and_optional_ies", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_electrical", projectPlaceGate, "photometric_project_behavior"] },
  { fixture_id: "elv_device", group: "elv", category: "fire_alarm_data_security", behavior: "elv_device_with_2d_3d_and_tag_data", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_electrical", projectPlaceGate, "dynamic_tag"] },
  { fixture_id: "conduit_fitting", group: "pathway", category: "conduit_fitting", behavior: "round_elbow_tee_cross_transition_union", required_gates: [sourceGate, familyRfaGate, flexGate, "connector_conduit", projectPlaceGate, "project_network"] },
  { fixture_id: "cable_tray_fitting", group: "pathway", category: "cable_tray_fitting", behavior: "channel_or_ladder_elbow_tee_cross_transition_union_offset", required_gates: [sourceGate, "controlled_ui_profile_label", familyRfaGate, flexGate, "connector_cable_tray", projectPlaceGate, "project_network"] },
  { fixture_id: "support_hosted", group: "support", category: "generic_model_or_mechanical_equipment", behavior: "hanger_rod_bracket_base_with_nested_hosting", required_gates: [sourceGate, familyRfaGate, flexGate, projectPlaceGate, "hosted_placement", "void_cut_when_declared"] },
  { fixture_id: "detail_profile_annotation", group: "annotation", category: "detail_item_profile_annotation", behavior: "symbolic_detail_profile_and_visibility", required_gates: [sourceGate, familyRfaGate, flexGate, "detail_representation"] },
  { fixture_id: "dynamic_tag", group: "annotation", category: "tag", behavior: "native_dynamic_label_from_object_data", required_gates: [sourceGate, "controlled_ui_dynamic_label", familyRfaGate, "project_schedule_tag"] },
  { fixture_id: "type_catalog_identity", group: "platform", category: "multi_type_family", behavior: "native_identity_lookup_and_type_catalog", required_gates: [sourceGate, familyRfaGate, "type_catalog_load_select", "identity_schedule_exposure"] },
  { fixture_id: "material_appearance", group: "platform", category: "model_family", behavior: "fixed_and_parameterized_material_visuals", required_gates: [sourceGate, familyRfaGate, "material_shaded_realistic"] },
  { fixture_id: "nested_performance", group: "platform", category: "nested_model_family", behavior: "embedded_shared_and_monolithic_benchmark", required_gates: [sourceGate, familyRfaGate, "nested_schedule_exposure", "nested_monolithic_benchmark"] },
  { fixture_id: "lod300_placement", group: "platform", category: "all_applicable", behavior: "geometry_data_connector_and_placement", required_gates: ["lod300_geometry_data_connector", "lod300_project_placement"] },
  { fixture_id: "lod350_coordination", group: "platform", category: "all_applicable", behavior: "coordination_zone_and_bep_project_evidence", required_gates: ["coordination_zone_reopen", "lod350_project_bep_coordination"] },
] as const;

interface EvidenceRecord {
  fixture_id: string;
  revit_version: RuntimeVersion;
  gate: string;
  outcome: "pass" | "fail" | "blocked";
  record_id: string;
  completed_at_utc: string;
  rfa_sha256?: string;
}

function object(value: unknown, label: string): Json {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error(`${label} must be an object.`);
  return value as Json;
}

function nonEmpty(value: unknown, label: string, maximum = 160): string {
  if (typeof value !== "string" || !value.trim() || value.length > maximum) throw new Error(`${label} must be a non-empty string up to ${maximum} characters.`);
  return value.trim();
}

function timestamp(value: unknown, label: string): string {
  const result = nonEmpty(value, label, 40);
  if (Number.isNaN(Date.parse(result))) throw new Error(`${label} must be an ISO-8601 timestamp.`);
  return result;
}

function evidenceRecords(value: unknown): EvidenceRecord[] {
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.length > 5000) throw new Error("evidence must be an array with at most 5000 records.");
  const fixtureById = new Map(familyAcceptanceFixtures.map((fixture) => [fixture.fixture_id, fixture]));
  const seen = new Set<string>();
  return value.map((item, index) => {
    const itemValue = object(item, `evidence[${index}]`);
    const fixtureId = nonEmpty(itemValue.fixture_id, `evidence[${index}].fixture_id`, 80);
    const fixture = fixtureById.get(fixtureId);
    if (!fixture) throw new Error(`evidence[${index}].fixture_id is not in the Family acceptance matrix: ${fixtureId}.`);
    const version = nonEmpty(itemValue.revit_version, `evidence[${index}].revit_version`, 4) as RuntimeVersion;
    if (!familyAcceptanceRuntimeVersions.includes(version)) throw new Error(`evidence[${index}].revit_version must be Revit 2023 or 2025.`);
    const gate = nonEmpty(itemValue.gate, `evidence[${index}].gate`, 120);
    if (!fixture.required_gates.includes(gate)) throw new Error(`evidence[${index}].gate ${gate} is not required by fixture ${fixtureId}.`);
    const outcome = nonEmpty(itemValue.outcome, `evidence[${index}].outcome`, 8) as EvidenceRecord["outcome"];
    if (outcome !== "pass" && outcome !== "fail" && outcome !== "blocked") throw new Error(`evidence[${index}].outcome must be pass, fail or blocked.`);
    const key = `${fixtureId}|${version}|${gate}`;
    if (seen.has(key)) throw new Error(`evidence contains duplicate result for ${key}.`);
    seen.add(key);
    const rfaHash = itemValue.rfa_sha256 === undefined ? undefined : nonEmpty(itemValue.rfa_sha256, `evidence[${index}].rfa_sha256`, 64);
    if (gate === familyRfaGate && (!rfaHash || !/^[a-fA-F0-9]{64}$/.test(rfaHash))) throw new Error(`evidence[${index}].rfa_sha256 is required for ${familyRfaGate}.`);
    return { fixture_id: fixtureId, revit_version: version, gate, outcome, record_id: nonEmpty(itemValue.record_id, `evidence[${index}].record_id`, 160), completed_at_utc: timestamp(itemValue.completed_at_utc, `evidence[${index}].completed_at_utc`), rfa_sha256: rfaHash };
  });
}

function versionAssessment(version: RuntimeVersion, evidence: readonly EvidenceRecord[]): Json {
  const missing: Json[] = [];
  const failures: Json[] = [];
  for (const fixture of familyAcceptanceFixtures) for (const gate of fixture.required_gates) {
    const record = evidence.find((item) => item.fixture_id === fixture.fixture_id && item.revit_version === version && item.gate === gate);
    if (!record) missing.push({ fixture_id: fixture.fixture_id, gate });
    else if (record.outcome !== "pass") failures.push({ fixture_id: fixture.fixture_id, gate, outcome: record.outcome, record_id: record.record_id });
  }
  return { revit_version: version, pass: missing.length === 0 && failures.length === 0, missing, failures };
}

/**
 * Evaluates supplied records for completeness only. It never fabricates a
 * runtime claim, persists user paths/IDs, or replaces Revit read-back.
 */
export function assessFamilyAcceptance(argumentsValue: Record<string, unknown>): Json {
  const evidence = evidenceRecords(argumentsValue.evidence);
  const revit2023 = versionAssessment("2023", evidence);
  const revit2025 = versionAssessment("2025", evidence);
  const all2023Times = evidence.filter((item) => item.revit_version === "2023" && item.outcome === "pass").map((item) => Date.parse(item.completed_at_utc));
  const all2025Times = evidence.filter((item) => item.revit_version === "2025" && item.outcome === "pass").map((item) => Date.parse(item.completed_at_utc));
  const sequenceValid = all2025Times.length === 0 || (all2023Times.length > 0 && Math.max(...all2023Times) <= Math.min(...all2025Times));
  const pass2023 = revit2023.pass === true;
  const pass2025 = revit2025.pass === true;
  return {
    matrix_version: FAMILY_ACCEPTANCE_MATRIX_VERSION,
    runtime_versions: familyAcceptanceRuntimeVersions,
    fixtures: familyAcceptanceFixtures,
    evidence_validation: {
      records_received: evidence.length,
      revit_2023: revit2023,
      revit_2025: revit2025,
      revit_2025_unlocked: pass2023,
      version_sequence_valid: sequenceValid,
      phase_complete: pass2023 && pass2025 && sequenceValid,
      boundary: "This checks declared evidence coverage only. Each record must still be backed by the named Revit read-back, RFA hash and project-copy record in the private ledger."
    }
  };
}
