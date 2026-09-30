import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";

const root = await mkdtemp(path.join(os.tmpdir(), "dscons-source-proposal-"));
const approved = path.join(root, "approved");
await mkdir(approved);
process.env.DSCONS_FAMILY_RECORD_DIRECTORY = path.join(root, "records");

const { inspectCadGeometry, inspectFamilySource, previewFamilySpec } = await import("../MCP-Server/build/family-evidence.js");
const { previewSourceToRevit: rawPreviewSourceToRevit } = await import("../MCP-Server/build/source-to-revit.js");
const { assessMepfEvidenceReadiness, assessSourceConflicts, reviewMepfEngineering } = await import("../MCP-Server/build/mepf-evidence-governance.js");

function previewSourceToRevit(args) {
  const mapping = args.mapping;
  const targetKind = mapping === "mep_project_reconstruction" ? "project" : mapping === "route_centerline" ? "route" : ["symbolic_lines", "model_lines", "detail_lines"].includes(mapping) ? "linework" : "family";
  const facts = {
    source_revision: true, source_locator: true, units_scale: true, target_family_category: true,
    family_source_decision: { outcome: "approved_blueprint", engineer_confirmed: true }, dimensions_or_catalogue_rows: true,
    discipline_service: true, revit_type: true, level_elevation: true, nominal_size: true, routing_geometry: true,
    coordinate_system: true, level_mapping: true, type_mapping: true, system_mapping: true,
    has_equipment: Array.isArray(args.mep_project?.equipment) && args.mep_project.equipment.length > 0,
  };
  const readiness = assessMepfEvidenceReadiness({ target_kind: targetKind, mapping, evidence_ids: args.evidence_ids, facts });
  const initial = rawPreviewSourceToRevit({ ...args, readiness_record_id: readiness.record_id });
  if (mapping !== "mep_project_reconstruction" || !initial.project_reconstruction_plan) return initial;
  const review = reviewMepfEngineering({ reconstruction_plan: initial.project_reconstruction_plan, calculation_boundary_acknowledged: true });
  if (review.status !== "approved_for_preview") return initial;
  const conflict = args.evidence_ids.length < 2 ? undefined : assessSourceConflicts({ sources: args.evidence_ids.map((evidence_id) => ({ evidence_id, common_datum_key: "fixture_datum", facts: { fixture_alignment: "same" } })) });
  return rawPreviewSourceToRevit({ ...args, readiness_record_id: readiness.record_id, engineering_review_record_id: review.record_id, ...(conflict ? { source_conflict_record_id: conflict.record_id } : {}) });
}

function pdf(objects) {
  let output = "%PDF-1.4\n"; const offsets = [0];
  for (let index = 0; index < objects.length; index += 1) { offsets.push(Buffer.byteLength(output, "binary")); output += `${index + 1} 0 obj\n${objects[index]}\nendobj\n`; }
  const xref = Buffer.byteLength(output, "binary"); output += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (let index = 1; index < offsets.length; index += 1) output += `${String(offsets[index]).padStart(10, "0")} 00000 n \n`;
  return Buffer.from(`${output}trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`, "binary");
}

const dxfPath = path.join(approved, "bounded-source.dxf");
await writeFile(dxfPath, [
  "0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", "4", "0", "ENDSEC",
  "0", "SECTION", "2", "ENTITIES",
  "0", "LINE", "8", "PIPE-CENTER", "10", "0", "20", "0", "30", "0", "11", "1200", "21", "0", "31", "0",
  "0", "LWPOLYLINE", "8", "PROFILE", "70", "1", "38", "0", "10", "0", "20", "0", "10", "600", "20", "0", "10", "600", "20", "300", "10", "0", "20", "300",
  "0", "ARC", "8", "REVIEW", "10", "100", "20", "100", "30", "0", "40", "50", "50", "0", "51", "90",
  "0", "ENDSEC", "0", "EOF", "",
].join("\n"), "utf8");

const dxf = await inspectCadGeometry({ source_path: dxfPath, approved_demo_directory: approved });
const profile = previewSourceToRevit({ evidence_ids: [dxf.evidence_id], mapping: "family_profile", layers: ["PROFILE"], plane: "xy" });
assert.equal(profile.status, "proposal_ready");
assert.equal(profile.model_changed, false);
assert.equal(profile.blueprint_fragment.profile_loops.length, 1);
assert.deepEqual(profile.blueprint_fragment.profile_loops[0].points_mm[2], { x_mm: 600, y_mm: 300, z_mm: 0 });
assert.equal(profile.blueprint_draft.family.category, "profile");
assert.equal(profile.blueprint_assessment.buildable_by_api, true);
const profileSpec = previewFamilySpec(profile.family_spec_input);
assert.equal(profileSpec.status, "human_confirmed");

const symbolic = previewSourceToRevit({ evidence_ids: [dxf.evidence_id], mapping: "symbolic_lines", layers: ["PIPE-CENTER"], plane: "xy" });
assert.equal(symbolic.blueprint_fragment.symbolic_lines.length, 1);
assert.equal(symbolic.blueprint_fragment.symbolic_lines[0].visibility.fine, true);
assert.equal(symbolic.blueprint_draft.family.category, "annotation");

const route = previewSourceToRevit({ evidence_ids: [dxf.evidence_id], mapping: "route_centerline", layers: ["PIPE-CENTER"], route: { kind: "pipe", type_id: 11, system_type_id: 22, level_id: 33, elevation_mm: 2700, tolerance_mm: 5 } });
assert.equal(route.status, "ready_for_fresh_revit_preview");
assert.equal(route.project_change_set_proposal.segments.length, 1);
assert.equal(route.project_change_set_proposal.kind, "pipe");
assert.equal(route.project_change_set_proposal.segments[0].points_mm[0].z_mm, 2700);
assert.match(route.boundary, /not a FamilySpec/);
assert.throws(() => previewSourceToRevit({ evidence_ids: [dxf.evidence_id], mapping: "route_centerline", route: { kind: "duct", type_id: 11, level_id: 33, elevation_mm: 2700, tolerance_mm: 5 } }), /system_type_id is required/);
assert.throws(() => previewSourceToRevit({ evidence_ids: [dxf.evidence_id], mapping: "route_centerline", confirm_units: "m", route: { kind: "pipe", type_id: 11, system_type_id: 22, level_id: 33, elevation_mm: 2700, tolerance_mm: 5 } }), /does not match immutable SourceEvidence units/);

const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADElEQVR42mNk+M/wHwAF/gL+X5Z7WAAAAABJRU5ErkJggg==", "base64");
const frontPath = path.join(approved, "front.png"); const sidePath = path.join(approved, "side.png");
await writeFile(frontPath, png); await writeFile(sidePath, png);
const front = await inspectFamilySource({ source_path: frontPath, source_kind: "image", approved_demo_directory: approved, orientation: "front", units: "mm", scale_anchors: [{ pixel_distance: 1, real_distance: 800, unit: "mm" }] });
const side = await inspectFamilySource({ source_path: sidePath, source_kind: "image", approved_demo_directory: approved, orientation: "side", units: "mm", scale_anchors: [{ pixel_distance: 1, real_distance: 500, unit: "mm" }] });
const imageFamily = previewSourceToRevit({ evidence_ids: [front.evidence_id, side.evidence_id], mapping: "image_family", image_family: { family_kind: "simple_equipment", category: "mechanical_equipment", template_behavior: "level_based", type_name: "800x1200x500", shape: "box", dimensions_confirmed_by_user: true, dimensions_mm: { width_mm: 800, height_mm: 1200, depth_mm: 500 } } });
assert.equal(imageFamily.status, "proposal_ready_for_blueprint_review");
assert.equal(imageFamily.family_proposal.primitive, "extrusion_box");
assert.deepEqual(imageFamily.family_proposal.connectors, []);
assert.equal(imageFamily.blueprint_draft.schema_version, "3.0");
assert.equal(imageFamily.blueprint_draft.parts[0].profile.width_parameter, "width");
assert.equal(imageFamily.blueprint_assessment.buildable_by_api, true);
assert.equal(imageFamily.confirmed_fields.depth_mm.value, 500);
const imageSpec = previewFamilySpec(imageFamily.family_spec_input);
assert.equal(imageSpec.status, "human_confirmed");
assert.deepEqual(imageSpec.supporting_source_evidence_ids, [side.evidence_id]);
assert.equal(imageSpec.citations.filter((citation) => citation.supporting === true).length, 1);
assert.match(JSON.stringify(imageFamily.findings), /Hidden geometry/);
assert.throws(() => previewSourceToRevit({ evidence_ids: [front.evidence_id], mapping: "image_family", image_family: { family_kind: "simple_equipment", category: "mechanical_equipment", template_behavior: "level_based", shape: "box", dimensions_confirmed_by_user: true, dimensions_mm: { width_mm: 800, height_mm: 1200, depth_mm: 500 } } }), /requires 2..4 image/);

const frontSite = await inspectFamilySource({ source_path: frontPath, source_kind: "image", approved_demo_directory: approved, orientation: "perspective", units: "mm", scale_anchors: [{ pixel_distance: 1, real_distance: 800, unit: "mm" }], site_capture: { capture_set_id: "plant-room-01", camera_id: "front-camera", capture_role: "plant_room", user_confirmed: true, control_points: [{ image_x_px: 0, image_y_px: 0, world_mm: [0, 0, 0] }, { image_x_px: 1, image_y_px: 0, world_mm: [1000, 0, 0] }] } });
const sideSite = await inspectFamilySource({ source_path: sidePath, source_kind: "image", approved_demo_directory: approved, orientation: "perspective", units: "mm", scale_anchors: [{ pixel_distance: 1, real_distance: 500, unit: "mm" }], site_capture: { capture_set_id: "plant-room-01", camera_id: "side-camera", capture_role: "plant_room", user_confirmed: true, control_points: [{ image_x_px: 0, image_y_px: 1, world_mm: [0, 1000, 0] }] } });
const photoProject = previewSourceToRevit({ evidence_ids: [frontSite.evidence_id, sideSite.evidence_id], mapping: "mep_project_reconstruction", mep_project: { confirmations: { coordinate_system: true, level_mapping: true, type_mapping: true, system_mapping: true, routing_geometry: true, equipment_mapping: true }, equipment: [{ key: "ahu_photo_01", discipline: "mechanical", category: "mechanical_equipment", family_symbol_id: 55, level_id: 33, point_mm: { x_mm: 500, y_mm: 500, z_mm: 0 }, placement_mode: "level_based_non_hosted", location_confirmed: true, type_confirmed: true, level_confirmed: true, source: { evidence_id: frontSite.evidence_id, image_region: { x_px: 0, y_px: 0, width_px: 1, height_px: 1 } } }] } });
assert.equal(photoProject.status, "ready_for_fresh_revit_preview");
assert.equal(photoProject.project_reconstruction_plan.equipment[0].key, "ahu_photo_01");

const catalogPath = path.join(approved, "catalog.pdf");
await writeFile(catalogPath, pdf([
  "<< /Type /Catalog /Pages 2 0 R >>",
  "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
  "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
  "<< /Length 220 >>\nstream\nBT /F1 12 Tf 72 760 Td (Type-A) Tj 0 -20 Td (600) Tj 0 -20 Td (800) Tj 0 -20 Td (400) Tj 0 -30 Td (Type-B) Tj 0 -20 Td (900) Tj 0 -20 Td (1200) Tj 0 -20 Td (500) Tj ET\nendstream",
  "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
]));
const catalog = await inspectFamilySource({ source_path: catalogPath, source_kind: "pdf", approved_demo_directory: approved });
const blocks = catalog.pages[0].text_blocks;
const locator = (rawText) => {
  const blockIndex = blocks.findIndex((block) => block.raw_text === rawText);
  assert.notEqual(blockIndex, -1);
  return { page: 1, block_index: blockIndex, block_sha256: blocks[blockIndex].block_sha256 };
};
const catalogFamily = previewSourceToRevit({
  evidence_ids: [catalog.evidence_id], mapping: "pdf_catalog_family",
  catalog_family: {
    family_kind: "catalog_equipment", category: "mechanical_equipment", template_behavior: "level_based", shape: "box",
    types: [
      { name: "Type-A", dimensions_mm: { width_mm: 600, height_mm: 800, depth_mm: 400 }, provenance: { width_mm: locator("600"), height_mm: locator("800"), depth_mm: locator("400") } },
      { name: "Type-B", dimensions_mm: { width_mm: 900, height_mm: 1200, depth_mm: 500 }, provenance: { width_mm: locator("900"), height_mm: locator("1200"), depth_mm: locator("500") } },
    ],
  },
});
assert.equal(catalogFamily.status, "proposal_ready_for_family_spec");
assert.equal(catalogFamily.blueprint_draft.types.length, 2);
assert.equal(catalogFamily.blueprint_assessment.buildable_by_api, true);
const catalogSpec = previewFamilySpec(catalogFamily.family_spec_input);
assert.equal(catalogSpec.status, "human_confirmed");
assert.equal(catalogSpec.type_confirmed_fields["Type-B"].height_mm.value, 1200);
assert.throws(() => previewSourceToRevit({
  evidence_ids: [catalog.evidence_id], mapping: "pdf_catalog_family",
  catalog_family: { family_kind: "bad_catalog", category: "mechanical_equipment", template_behavior: "level_based", shape: "box", types: [
    { name: "A", dimensions_mm: { width_mm: 1, height_mm: 2, depth_mm: 3 }, provenance: { width_mm: { ...locator("600"), block_sha256: "0".repeat(64) }, height_mm: locator("800"), depth_mm: locator("400") } },
    { name: "B", dimensions_mm: { width_mm: 4, height_mm: 5, depth_mm: 6 }, provenance: { width_mm: locator("900"), height_mm: locator("1200"), depth_mm: locator("500") } },
  ] } }), /block fingerprint does not match/);

const vectorProjectPath = path.join(approved, "mechanical-plan.pdf");
const vectorProjectStream = "100 200 m 500 200 l S";
await writeFile(vectorProjectPath, pdf([
  "<< /Type /Catalog /Pages 2 0 R >>",
  "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
  "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R >>",
  `<< /Length ${Buffer.byteLength(vectorProjectStream, "binary")} >>\nstream\n${vectorProjectStream}\nendstream`,
]));
const vectorProjectEvidence = await inspectFamilySource({ source_path: vectorProjectPath, source_kind: "pdf", approved_demo_directory: approved, units: "mm", scale_anchors: [{ pixel_distance: 100, real_distance: 1000, unit: "mm" }], sheet_metadata: [{ page: 1, discipline: "mechanical", view_kind: "plan", drawing_number: "M-101", level_name: "L1", scale_denominator: 100, user_confirmed: true }] });
const vectorPath = vectorProjectEvidence.pages[0].vector_extraction.paths[0];
const pdfMepfPlan = previewSourceToRevit({
  evidence_ids: [vectorProjectEvidence.evidence_id], mapping: "mep_project_reconstruction",
  mep_project: {
    confirmations: { coordinate_system: true, level_mapping: true, type_mapping: true, system_mapping: true, routing_geometry: true },
    routes: [{ key: "supply_air_pdf_01", discipline: "mechanical", service: "SA", kind: "duct", type_id: 11, system_type_id: 22, level_id: 33, size_mm: { width_mm: 600, height_mm: 300 }, points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 2700 }, { x_mm: 4000, y_mm: 0, z_mm: 2700 }], geometry_confirmed: true, size_confirmed: true, elevation_confirmed: true, type_confirmed: true, system_confirmed: true, source: { evidence_id: vectorProjectEvidence.evidence_id, page: 1, path_index: vectorPath.path_index, path_sha256: vectorPath.path_sha256 } }],
  },
});
assert.equal(pdfMepfPlan.status, "ready_for_fresh_revit_preview");
assert.equal(pdfMepfPlan.project_reconstruction_plan.source_citations[0].path_sha256, vectorPath.path_sha256);
assert.deepEqual(pdfMepfPlan.project_reconstruction_plan.source_citations[0].path_bounds_page, vectorPath.bounds_page);
assert.throws(() => previewSourceToRevit({
  evidence_ids: [vectorProjectEvidence.evidence_id], mapping: "mep_project_reconstruction",
  mep_project: { confirmations: { coordinate_system: true, level_mapping: true, type_mapping: true, system_mapping: true, routing_geometry: true }, routes: [{ key: "bad_pdf_path", discipline: "mechanical", service: "SA", kind: "duct", type_id: 11, system_type_id: 22, level_id: 33, size_mm: { width_mm: 600, height_mm: 300 }, points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 2700 }, { x_mm: 4000, y_mm: 0, z_mm: 2700 }], geometry_confirmed: true, size_confirmed: true, elevation_confirmed: true, type_confirmed: true, system_confirmed: true, source: { evidence_id: vectorProjectEvidence.evidence_id, page: 1, path_index: vectorPath.path_index, path_sha256: "0".repeat(64) } }] },
}), /vector path fingerprint does not match/);

const mepfPlan = previewSourceToRevit({
  evidence_ids: [dxf.evidence_id], mapping: "mep_project_reconstruction",
  mep_project: {
    confirmations: { coordinate_system: true, level_mapping: true, type_mapping: true, system_mapping: true, routing_geometry: true, equipment_mapping: true },
    routes: [
      { key: "chw_supply_01", discipline: "mechanical", service: "CHWS", kind: "pipe", type_id: 11, system_type_id: 22, level_id: 33, size_mm: { diameter_mm: 100 }, points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 2700 }, { x_mm: 1200, y_mm: 0, z_mm: 2700 }], geometry_confirmed: true, size_confirmed: true, elevation_confirmed: true, type_confirmed: true, system_confirmed: true, source: { evidence_id: dxf.evidence_id, layer: "PIPE-CENTER" } },
      { key: "cable_tray_01", discipline: "electrical", service: "ELV", kind: "cable_tray", type_id: 44, level_id: 33, size_mm: { width_mm: 300, height_mm: 50 }, points_mm: [{ x_mm: 0, y_mm: 500, z_mm: 3000 }, { x_mm: 1200, y_mm: 500, z_mm: 3000 }], geometry_confirmed: true, size_confirmed: true, elevation_confirmed: true, type_confirmed: true, source: { evidence_id: dxf.evidence_id, layer: "PIPE-CENTER" } },
    ],
    equipment: [
      { key: "pump_01", discipline: "mechanical", category: "mechanical_equipment", family_symbol_id: 55, level_id: 33, point_mm: { x_mm: 500, y_mm: 200, z_mm: 0 }, rotation_degrees: 90, placement_mode: "level_based_non_hosted", location_confirmed: true, type_confirmed: true, level_confirmed: true, source: { evidence_id: dxf.evidence_id, layer: "PROFILE" } },
      { key: "light_01", discipline: "electrical", category: "lighting_fixture", family_symbol_id: 56, level_id: 33, point_mm: { x_mm: 800, y_mm: 500, z_mm: 3000 }, rotation_degrees: 0, placement_mode: "level_based_non_hosted", location_confirmed: true, type_confirmed: true, level_confirmed: true, source: { evidence_id: dxf.evidence_id, layer: "PROFILE" } },
    ],
    connections: [{ key: "pump_to_chws", discipline: "mechanical", connection_kind: "direct", engineer_confirmed: true, from: { element_key: "pump_01", connector_role: "discharge", connector_index: 1 }, to: { element_key: "chw_supply_01", connector_role: "start", connector_index: 0 } }],
    insulation: [{ route_key: "chw_supply_01", material_type_id: 77, thickness_mm: 25, engineer_confirmed: true }],
    electrical_circuits: [{ key: "lighting_circuit_01", load_element_key: "light_01", panel_element_id: 9001, circuit_type: "power", poles: 1, voltage_v: 230, engineer_confirmed: true }],
    penetrations: [{ key: "sleeve_01", host_category: "wall", route_key: "chw_supply_01", required_before_routing: false, sleeve_required: true, fire_stopping_required: true, responsibility: "MEP contractor" }],
  },
});
assert.equal(mepfPlan.status, "ready_for_fresh_revit_preview");
assert.equal(mepfPlan.project_reconstruction_plan.priority, "MEPF");
assert.equal(mepfPlan.project_reconstruction_plan.architecture_structure_role, "levels_grids_hosts_clearance_only");
assert.equal(mepfPlan.project_reconstruction_plan.routes.length, 2);
assert.equal(mepfPlan.project_reconstruction_plan.equipment.length, 2);
assert.equal(mepfPlan.project_reconstruction_plan.schema_version, "1.1");
assert.equal(mepfPlan.project_reconstruction_plan.connection_requests[0].execution, "atomic_changeset_after_created_id_resolution");
assert.equal(mepfPlan.project_reconstruction_plan.insulation_requests[0].thickness_mm, 25);
assert.equal(mepfPlan.project_reconstruction_plan.insulation_requests[0].execution, "atomic_changeset_after_created_route_resolution");
assert.equal(mepfPlan.project_reconstruction_plan.electrical_circuit_requests[0].panel_element_id, 9001);
assert.deepEqual(mepfPlan.project_reconstruction_plan.execution_phases.map((phase) => phase.key), ["reference_alignment", "route_and_equipment_creation", "connector_network", "slope_insulation_lining", "electrical_circuit_and_panel", "penetration_sleeve_firestop", "independent_verification"]);
assert.deepEqual(mepfPlan.bim_changeset_template.operations.map((operation) => operation.operation), ["model_create_batch", "mep_place_equipment_batch", "mep_connect_created_batch", "mep_apply_created_envelopes_batch"]);
assert.equal(mepfPlan.bim_changeset_template.operations[2].arguments.connections[0].from.element_key, "pump_01");
assert.equal(mepfPlan.bim_changeset_template.operations[3].arguments.envelopes[0].envelope_kind, "insulation");
assert.equal(mepfPlan.bim_changeset_template.operations[0].arguments.routes[0].diameter_mm, 100);
assert.equal(mepfPlan.bim_changeset_template.operations[0].arguments.routes[1].width_mm, 300);
assert.equal(mepfPlan.model_changed, false);

const blockedSlope = previewSourceToRevit({
  evidence_ids: [dxf.evidence_id], mapping: "mep_project_reconstruction",
  mep_project: {
    confirmations: { coordinate_system: true, level_mapping: true, type_mapping: true, system_mapping: true, routing_geometry: true },
    routes: [{ key: "drain_01", discipline: "plumbing", service: "Drainage", kind: "pipe", type_id: 11, system_type_id: 22, level_id: 33, size_mm: { diameter_mm: 75 }, points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 1000 }, { x_mm: 1200, y_mm: 0, z_mm: 1000 }], slope_percent: 1, geometry_confirmed: true, size_confirmed: true, elevation_confirmed: true, type_confirmed: true, system_confirmed: true, source: { evidence_id: dxf.evidence_id, layer: "PIPE-CENTER" } }],
    slope_plans: [{ route_key: "drain_01", slope_percent: -1, start_invert_mm: 1000, end_invert_mm: 988, engineer_confirmed: true }],
  },
});
assert.equal(blockedSlope.status, "requires_engineering_review");
assert.equal(blockedSlope.bim_changeset_template.operations.length, 0);
assert.match(JSON.stringify(blockedSlope.findings), /Sloped source routes/);
assert.equal(blockedSlope.project_reconstruction_plan.slope_plans[0].execution, "operator_assisted_until_runtime_certified");

console.log("PASS Source-to-Revit proposals: PDF multi-Type, DXF Blueprint/route, multi-view image FamilySpec and MEPF Project reconstruction plan");
