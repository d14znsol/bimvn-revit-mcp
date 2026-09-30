import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdtemp, mkdir, readFile, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
const recordDirectory = await mkdtemp(path.join(os.tmpdir(), "dscons-family-records-"));
process.env.DSCONS_FAMILY_RECORD_DIRECTORY = recordDirectory;
const { FamilyEvidenceError, inspectCadGeometry, inspectFamilySource, previewFamilySpec: rawPreviewFamilySpec, readEvidenceRecord, recordFamilyBuildArtifact } = await import("../MCP-Server/build/family-evidence.js");
const { assessMepfEvidenceReadiness } = await import("../MCP-Server/build/mepf-evidence-governance.js");

// This broad FamilySpec regression intentionally focuses on Blueprint validation.
// Every fixture supplies an explicit ready record instead of relying on the
// production path's former implicit bypass of the information-readiness gate.
function previewFamilySpec(args) {
  const evidenceIds = [args.evidence_id, ...(args.supporting_evidence_ids ?? [])];
  const readiness = assessMepfEvidenceReadiness({
    target_kind: "family", mapping: "family_blueprint", evidence_ids: evidenceIds,
    facts: {
      source_revision: true, source_locator: true, units_scale: true,
      target_family_category: true,
      family_source_decision: { outcome: "approved_blueprint", engineer_confirmed: true },
    },
  });
  assert.equal(readiness.status, "ready_for_proposal", "FamilySpec test fixture must have explicit readiness evidence");
  return rawPreviewFamilySpec({ ...args, readiness_record_id: readiness.record_id });
}

function pdf(objects) {
  let output = "%PDF-1.4\n";
  const offsets = [0];
  for (let index = 0; index < objects.length; index += 1) {
    offsets.push(Buffer.byteLength(output, "binary"));
    output += `${index + 1} 0 obj\n${objects[index]}\nendobj\n`;
  }
  const xref = Buffer.byteLength(output, "binary");
  output += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (let index = 1; index < offsets.length; index += 1) output += `${String(offsets[index]).padStart(10, "0")} 00000 n \n`;
  return Buffer.from(`${output}trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`, "binary");
}

function syntheticArtifactResult(spec, familyPath, bytes, typeName, familyPlacementType) {
  const resolved = spec.blueprint.resolved_complexity;
  const parameters = (spec.blueprint.parameters ?? []).map((parameter) => ({
    name: parameter.name, is_instance: parameter.scope === "instance",
    is_shared: typeof parameter.shared_guid === "string", shared_guid: parameter.shared_guid ?? null,
  }));
  return {
    spec_id: spec.spec_id, schema_version: "3.0", family_path: familyPath,
    sha256: createHash("sha256").update(bytes).digest("base64"), blueprint_hash: spec.blueprint.blueprint_hash,
    publication: { rfa_bytes: bytes.length }, created: { types: [typeName] },
    complexity: { within_budget: true, rfa_size_measured: true, rfa_bytes: bytes.length, nested_depth: resolved.nested_depth, aggregate_complexity_score: resolved.aggregate_complexity_score },
    verification: { verified: true, mode: "post_commit_reopen_read_back", family: { hosting: { family_placement_type: familyPlacementType, verified_from_revit: true }, parameters } },
  };
}

const root = await mkdtemp(path.join(os.tmpdir(), "dscons-family-evidence-"));
const approved = path.join(root, "approved");
await mkdir(approved);
const nativePdf = path.join(approved, "native.pdf");
await writeFile(nativePdf, pdf([
  "<< /Type /Catalog /Pages 2 0 R >>",
  "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
  "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
  "<< /Length 45 >>\nstream\nBT /F1 12 Tf 72 760 Td (Pump A=100 mm) Tj ET\nendstream",
  "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
]));
const scannedPdf = path.join(approved, "scanned.pdf");
await writeFile(scannedPdf, pdf([
  "<< /Type /Catalog /Pages 2 0 R >>",
  "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
  "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>",
]));
const vectorPdf = path.join(approved, "mep-vector-plan.pdf");
const vectorStream = "q 2 0 0 2 10 20 cm 0 0 m 100 0 l 100 50 l h S Q 200 300 40 20 re S";
await writeFile(vectorPdf, pdf([
  "<< /Type /Catalog /Pages 2 0 R >>",
  "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
  "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R >>",
  `<< /Length ${Buffer.byteLength(vectorStream, "binary")} >>\nstream\n${vectorStream}\nendstream`,
]));
const iesFileName = "DSCons-Test-Light.ies";
const iesPath = path.join(approved, iesFileName);
const iesBytes = Buffer.from("IES:LM-63-2002\n[TEST] DSCons bounded fixture\n[MANUFAC] DSCons Test\nTILT=NONE\n1 1000 1 3 1 1 1 0.1 0.1 0\n1 1 10\n0 45 90\n0\n100 100 100\n", "ascii");
await writeFile(iesPath, iesBytes);
const iesSha256 = createHash("sha256").update(iesBytes).digest("hex");
const textureFileName = "DSCons-Powder-Coat.png";
const textureBytes = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADElEQVR42mNk+M/wHwAF/gL+X5Z7WAAAAABJRU5ErkJggg==", "base64");
const texturePath = path.join(approved, textureFileName);
await writeFile(texturePath, textureBytes);
const textureSha256 = createHash("sha256").update(textureBytes).digest("hex");

const evidence = await inspectFamilySource({ source_path: nativePdf, source_kind: "pdf", approved_demo_directory: approved, units: "mm", scale_anchors: [{ pixel_distance: 100, real_distance: 1000, unit: "mm", label: "Grid dimension" }], sheet_metadata: [{ page: 1, discipline: "mechanical", view_kind: "plan", drawing_number: "M-101", level_name: "Level 1", scale_denominator: 100, north_rotation_degrees: 0, user_confirmed: true }] });
assert.equal(evidence.schema_version, "3.0");
assert.equal(evidence.record_kind, "source_evidence_v3");
assert.equal(evidence.page_count, 1);
assert.equal(evidence.pages[0].text_blocks[0].raw_text, "Pump A=100 mm");
assert.equal(evidence.pages[0].text_blocks[0].extraction_method, "native_text");
assert.match(evidence.pages[0].text_blocks[0].block_sha256, /^[a-f0-9]{64}$/);
assert.match(evidence.sha256, /^[a-f0-9]{64}$/);
assert.equal(evidence.provenance.immutable_source_sha256, evidence.sha256);
assert.equal(evidence.classification.native_text_pages, 1);
assert.equal(evidence.classification.mepf_sheet_count, 1);
assert.equal(evidence.pages[0].sheet_metadata.drawing_number, "M-101");
assert.equal(evidence.confidence.sheet_semantics, "user_confirmed");

const vectorEvidence = await inspectFamilySource({ source_path: vectorPdf, source_kind: "pdf", approved_demo_directory: approved, units: "mm", scale_anchors: [{ pixel_distance: 100, real_distance: 1000, unit: "mm", label: "Confirmed drawing scale" }] });
assert.equal(vectorEvidence.pages[0].content_class, "vector_or_mixed_drawing");
assert.equal(vectorEvidence.pages[0].vector_extraction.coordinate_space, "top_left_page_points_x_right_y_down");
assert.equal(vectorEvidence.pages[0].vector_extraction.path_count, 2);
assert.equal(vectorEvidence.pages[0].vector_extraction.truncated, false);
assert.deepEqual(vectorEvidence.pages[0].vector_extraction.paths[0].subpaths[0], { points_page: [[10, 822], [210, 822], [210, 722]], closed: true });
assert.deepEqual(vectorEvidence.pages[0].vector_extraction.paths[1].subpaths[0], { points_page: [[200, 542], [240, 542], [240, 522], [200, 522]], closed: true });
assert.equal(vectorEvidence.pages[0].vector_extraction.paths[0].paint_operator, "stroke");
assert.equal(vectorEvidence.pages[0].vector_extraction.paths[0].linear_only, true);
assert.match(vectorEvidence.pages[0].vector_extraction.paths[0].path_sha256, /^[a-f0-9]{64}$/);
assert.match(vectorEvidence.pages[0].vector_extraction.semantic_boundary, /engineer mapping/);

const unscaledImage = await inspectFamilySource({ source_path: texturePath, source_kind: "image", approved_demo_directory: approved, orientation: "perspective" });
assert.equal(unscaledImage.schema_version, "3.0");
assert.equal(unscaledImage.status, "awaiting_scale_confirmation");
assert.equal(unscaledImage.confidence.hidden_geometry, "unavailable");
const scaledImage = await inspectFamilySource({ source_path: texturePath, source_kind: "image", approved_demo_directory: approved, orientation: "front", units: "mm", scale_anchors: [{ pixel_distance: 1, real_distance: 100, unit: "mm", label: "Known width" }], site_capture: { capture_set_id: "plant-room-a", camera_id: "cam-01", capture_role: "plant_room", level_name: "Level 1", user_confirmed: true, control_points: [{ image_x_px: 0, image_y_px: 0, world_mm: [0, 0, 0], label: "Survey A" }] } });
assert.equal(scaledImage.status, "proposal_ready");
assert.equal(scaledImage.confidence.geometry_scale, "human_confirmed");
assert.equal(scaledImage.classification.source_type, "construction_site_capture");
assert.equal(scaledImage.site_capture.capture_set_id, "plant-room-a");

const dxfPath = path.join(approved, "profile-route.dxf");
await writeFile(dxfPath, [
  "0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", "4", "0", "ENDSEC",
  "0", "SECTION", "2", "ENTITIES",
  "0", "LINE", "8", "PIPE-CENTER", "10", "0", "20", "0", "30", "0", "11", "1000", "21", "0", "31", "0",
  "0", "LWPOLYLINE", "8", "PROFILE", "70", "1", "38", "0", "10", "0", "20", "0", "10", "100", "20", "0", "10", "100", "20", "50",
  "0", "CIRCLE", "8", "OPENING", "10", "250", "20", "100", "30", "0", "40", "25",
  "0", "INSERT", "8", "EQUIPMENT", "2", "PUMP-BLOCK", "10", "500", "20", "200", "30", "0",
  "0", "ENDSEC", "0", "EOF", ""
].join("\n"), "utf8");
const dxfEvidence = await inspectCadGeometry({ source_path: dxfPath, approved_demo_directory: approved });
assert.equal(dxfEvidence.record_kind, "source_evidence_v3");
assert.equal(dxfEvidence.units.value, "mm");
assert.equal(dxfEvidence.units.status, "declared_by_source");
assert.equal(dxfEvidence.geometry.entity_count, 4);
assert.deepEqual(dxfEvidence.layers, ["EQUIPMENT", "OPENING", "PIPE-CENTER", "PROFILE"]);
assert.deepEqual(dxfEvidence.blocks, ["PUMP-BLOCK"]);
assert.deepEqual(dxfEvidence.geometry.bounds, { min: [0, 0, 0], max: [1000, 200, 0] });
await assert.rejects(inspectCadGeometry({ source_path: dxfPath, approved_demo_directory: approved, units: "m" }), /conflict with DXF/);

const dwgPath = path.join(approved, "trusted-required.dwg");
await writeFile(dwgPath, Buffer.from("AC1032\0binary-dwg-fixture", "binary"));
const dwgEvidence = await inspectCadGeometry({ source_path: dwgPath, approved_demo_directory: approved, units: "mm" });
assert.equal(dwgEvidence.status, "requires_trusted_dwg_adapter");
assert.equal(dwgEvidence.geometry.status, "trusted_adapter_required");
assert.equal(dwgEvidence.geometry.entities.length, 0);
const dwgSha256 = createHash("sha256").update(await readFile(dwgPath)).digest("hex");
const trustedDwg = await inspectCadGeometry({
  source_path: dwgPath, approved_demo_directory: approved, units: "mm",
  trusted_adapter_manifest: {
    adapter: "oda_drawings", adapter_version: "2027.8", source_sha256: dwgSha256, units: "mm",
    coordinate_system: { origin: [0, 0, 0], rotation_degrees: 0, policy: "world_coordinate_system" },
    layers: ["M-DUCT", "M-EQUIPMENT"], layouts: ["Model"], xrefs: [{ name: "ARCH", status: "resolved" }],
    entities: [
      { type: "LINE", layer: "M-DUCT", handle: "10A", start: [0, 0, 0], end: [1000, 0, 0] },
      { type: "INSERT", layer: "M-EQUIPMENT", handle: "10B", block: "AHU-01", point: [500, 500, 0], rotation: 90 },
    ],
  },
});
assert.equal(trustedDwg.status, "observed");
assert.equal(trustedDwg.geometry.status, "trusted_adapter_parsed");
assert.equal(trustedDwg.geometry.entity_count, 2);
assert.deepEqual(trustedDwg.geometry.bounds, { min: [0, 0, 0], max: [1000, 500, 0] });
assert.equal(trustedDwg.trusted_adapter.adapter, "oda_drawings");
await assert.rejects(inspectCadGeometry({ source_path: dwgPath, approved_demo_directory: approved, trusted_adapter_manifest: { adapter: "oda_drawings", adapter_version: "2027.8", source_sha256: "0".repeat(64), units: "mm", coordinate_system: { origin: [0, 0, 0], policy: "world" }, layers: [], entities: [] } }), /does not match the immutable source/);

const pumpFields = Object.fromEntries(["A", "A1", "A2", "B", "C", "D1", "D2", "DN1", "DN2", "K1", "K2", "P1", "P2", "H", "H1", "H2", "H3", "M", "N1", "N2", "R", "S1", "S2", "T"].map((key, index) => [key, index + 1]));
const incomplete = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pump", confirmed_fields: { A: 100 } });
assert.equal(incomplete.status, "awaiting_human_confirmation");
assert.ok(incomplete.missing_fields.includes("A1"));
const complete = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pump", confirmed_fields: { ...pumpFields, type_code: "P-1" } });
assert.equal(complete.status, "human_confirmed");
assert.equal(complete.lod, "LOD_300");

const blueprint = {
  schema_version: "3.0", target_lod: "LOD_300",
  family: { family_key: "custom_tank", category: "mechanical_equipment", template_behavior: "level_based", primary_axis: "x" },
  required_source_fields: ["width_mm", "height_mm", "depth_mm"],
  parameters: [
    { key: "width", name: "Width", data_type: "length", scope: "type", source_field: "width_mm" },
    { key: "height", name: "Height", data_type: "length", scope: "type", source_field: "height_mm" },
    { key: "depth", name: "Depth", data_type: "length", scope: "type", source_field: "depth_mm" },
  ],
  types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450 } }],
  parts: [{ key: "body", primitive: "extrusion", operation: "solid", axis: "x", role: "TankBody", profile: { shape: "rectangle", width_mm: 600, height_mm: 900, width_parameter: "width", height_parameter: "height" }, start_mm: 0, end_mm: 450, depth_parameter: "depth", visibility: { coarse: true, medium: true, fine: true } }],
  connectors: [], nested_components: [], symbolic_lines: [], ui_fallbacks: [],
  verification: { parameter_flex_cases: [{ parameter_key: "width", min: 300, nominal: 600, max: 900 }, { parameter_key: "height", min: 450, nominal: 900, max: 1200 }, { parameter_key: "depth", min: 225, nominal: 450, max: 750 }] },
};
const custom = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint });
assert.equal(custom.schema_version, "3.0");
assert.equal(custom.record_kind, "family_spec_v3");
assert.equal(custom.status, "human_confirmed");
assert.equal(custom.blueprint_assessment.buildable_by_api, true);
assert.deepEqual(custom.blueprint.parameter_order, ["width", "height", "depth"]);
assert.deepEqual(custom.blueprint.publication, { compact_rfa: true, preview_view: "auto" });
assert.equal(custom.blueprint.performance_budget.max_rfa_bytes, 10 * 1024 * 1024);
assert.equal(custom.blueprint.complexity_assessment.within_declared_budget, true);
assert.deepEqual(custom.blueprint.complexity_assessment.metrics, { forms: 1, void_forms: 0, parameters: 3, formula_parameters: 0, types: 1, nested_instances: 0, array_member_capacity: 0, mirrored_copies: 0, connectors: 0, reference_datums: 0, dimensions: 0, two_dimensional_curves: 0, materials: 0, presentation_subcategories: 0, light_sources: 0, lookup_rows: 0, complexity_score: 19 });
assert.equal(custom.blueprint.resolved_complexity.nested_depth, 0);
assert.ok(custom.blueprint_assessment.supported_features.includes("declarative_complexity_and_rfa_size_budget"));
assert.ok(custom.blueprint_assessment.supported_features.includes("source_field_to_critical_geometry_and_connector_traceability"));
assert.deepEqual(custom.blueprint.source_traceability.bindings, [
  { source_field: "width_mm", parameter_keys: ["width"], geometry_target_paths: ["parts[0].profile.width_parameter"], connector_target_paths: [] },
  { source_field: "height_mm", parameter_keys: ["height"], geometry_target_paths: ["parts[0].profile.height_parameter"], connector_target_paths: [] },
  { source_field: "depth_mm", parameter_keys: ["depth"], geometry_target_paths: ["parts[0].depth_parameter"], connector_target_paths: [] },
]);
assert.equal(custom.blueprint.source_traceability.direct_untraced_critical_parameter_keys.length, 0);
assert.deepEqual(custom.confirmed_fields.width_mm, { value: 600, status: "confirmed", provenance: { kind: "user_confirmed", confirmation_origin: "legacy_scalar" } });
assert.equal(custom.field_provenance.policy, "field_level_source_provenance_v1");
assert.equal(custom.field_provenance.summary.legacy_user_confirmed, 3);
assert.doesNotMatch(JSON.stringify(custom.field_provenance), /Pump A=100 mm/);

// Native Identity Data must use Revit's built-in Type fields. A parallel
// look-alike custom "Manufacturer" parameter would not be a trustworthy
// schedule/library contract.
const identityBlueprint = {
  ...blueprint,
  identity_data: { type_values: [{ type_name: "T-1", manufacturer: "DSCons", model: "TANK-600", description: "Catalog tank", url: "https://example.invalid/tank-600", type_comments: "Training", classification_number: "23.31.00.00", classification_title: "Storage tanks" }] },
};
const identitySpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: identityBlueprint });
assert.equal(identitySpec.blueprint.identity_data.policy, "revit_builtin_type_identity_data_v1");
assert.equal(identitySpec.blueprint.identity_data.type_values[0].model, "TANK-600");
assert.ok(identitySpec.blueprint_assessment.supported_features.includes("native_revit_type_identity_data"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...identityBlueprint, identity_data: { type_values: [{ type_name: "Unknown", manufacturer: "DSCons" }] } } }), /references unknown Family Type Unknown/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...identityBlueprint, identity_data: { type_values: [{ type_name: "T-1" }] } } }), /must declare at least one native Identity Data field/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...identityBlueprint, identity_data: { type_values: [{ type_name: "T-1", supplier: "DSCons" }] } } }), /contains unsupported field supplier/);

// Formula-bearing Family parameters need an existing Family Type at runtime.
// The Blueprint therefore records exact parameter-name dependencies, rejects
// cycles, and gives the compiler a dependency-safe SetFormula order.
const formulaDrivenBlueprint = {
  ...blueprint,
  parameters: [...blueprint.parameters, { key: "clearance_width", name: "Clearance Width", data_type: "length", scope: "type", formula: "Width + 50", formula_dependencies: ["width"] }],
  parameter_order: [...blueprint.parameters.map((parameter) => parameter.key), "clearance_width"],
  parts: blueprint.parts.map((part) => ({ ...part, profile: { ...part.profile, width_parameter: "clearance_width" } })),
};
const formulaDrivenSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: formulaDrivenBlueprint });
assert.deepEqual(formulaDrivenSpec.blueprint.formula_dependency_graph, {
  policy: "revit_type_formula_dependency_graph_v1",
  entries: [{ parameter_key: "clearance_width", formula: "Width + 50", dependency_keys: ["width"], dependency_origin: "declared_and_verified" }],
  application_order: ["clearance_width"],
  boundary: "Dependency entries are matched to declared Family Parameter names, checked for cycles, then ordered before Revit SetFormula. Revit remains the authority for dimensional-unit syntax and evaluated values on the selected template.",
});
assert.deepEqual(formulaDrivenSpec.blueprint.source_traceability.formula_or_lookup_derived_critical_parameter_keys, ["clearance_width"]);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...formulaDrivenBlueprint, parameters: formulaDrivenBlueprint.parameters.map((parameter) => parameter.key === "clearance_width" ? { ...parameter, formula_dependencies: [] } : parameter) } }), /formula_dependencies must exactly match/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...formulaDrivenBlueprint, parameters: formulaDrivenBlueprint.parameters.map((parameter) => parameter.key === "clearance_width" ? { ...parameter, scope: "instance" } : parameter) } }), /formula requires scope=type/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...formulaDrivenBlueprint, parameters: [...formulaDrivenBlueprint.parameters, { key: "duplicate_width", name: "Width", data_type: "number", scope: "type" }] } }), /duplicate Family Parameter name Width/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "formula_cycle", confirmed_fields: {}, blueprint: {
  schema_version: "3.0", target_lod: "LOD_300", family: { family_key: "formula_cycle", category: "mechanical_equipment", template_behavior: "level_based", primary_axis: "x" }, required_source_fields: [],
  parameters: [{ key: "a", name: "A", data_type: "length", scope: "type", formula: "B + 1" }, { key: "b", name: "B", data_type: "length", scope: "type", formula: "A + 1" }],
  types: [{ name: "Default", values: {} }], parts: [], connectors: [], nested_components: [], symbolic_lines: [], ui_fallbacks: [], verification: { parameter_flex_cases: [] },
} }), /formula dependency cycle detected: a -> b -> a/);
const sourceLocator = { kind: "source_document", page: 1, block_index: 0, block_sha256: evidence.pages[0].text_blocks[0].block_sha256 };
const sourcedCustom = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {
  width_mm: { value: 600, provenance: sourceLocator },
  height_mm: { value: 900, status: "confirmed", provenance: { ...sourceLocator } },
  depth_mm: { value: 450, status: "confirmed", provenance: { ...sourceLocator } },
}, blueprint });
assert.equal(sourcedCustom.status, "human_confirmed");
assert.equal(sourcedCustom.field_provenance.summary.source_document, 3);
assert.deepEqual(sourcedCustom.field_provenance.fields[0], { source_field: "depth_mm", status: "confirmed", provenance_kind: "source_document", document_locator: { page: 1, block_index: 0, block_sha256: sourceLocator.block_sha256 } });
assert.equal(sourcedCustom.confirmed_fields.width_mm.provenance.source_evidence_sha256, evidence.sha256);
assert.doesNotMatch(JSON.stringify(sourcedCustom.field_provenance), /Pump A=100 mm/);
const revisionedCustom = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: { value: 600, provenance: sourceLocator }, height_mm: { value: 900, provenance: sourceLocator }, depth_mm: { value: 450, provenance: sourceLocator } }, catalog_revision: { manufacturer: "DSCons", catalog_id: "TANK-2026", revision: "R03", revision_provenance: sourceLocator, issued_on: "2026-09-01", product_series: "Training Tank" }, blueprint });
assert.deepEqual(Object.keys(revisionedCustom.catalog_revision_provenance.family).sort(), ["catalog_id", "catalog_identity_sha256", "issued_on", "manufacturer", "product_series", "revision", "revision_provenance", "source_evidence_sha256"]);
assert.equal(revisionedCustom.catalog_revision_provenance.policy, "catalog_revision_identity_v2");
assert.equal(revisionedCustom.catalog_revision_provenance.scope, "family");
assert.match(revisionedCustom.catalog_revision_provenance.family.catalog_identity_sha256, /^[a-f0-9]{64}$/);
assert.deepEqual(revisionedCustom.catalog_revision_provenance.family.revision_provenance, { kind: "source_document", source_evidence_sha256: evidence.sha256, page: 1, block_index: 0, block_sha256: sourceLocator.block_sha256 });
assert.doesNotMatch(JSON.stringify(revisionedCustom.catalog_revision_provenance), /Pump A=100 mm/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, catalog_revision: { manufacturer: "DSCons", catalog_id: "TANK-2026", revision: "R03", revision_provenance: { kind: "user_confirmed" }, issued_on: "2026-02-30" }, blueprint }), /calendar date/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, catalog_revision: { manufacturer: "DSCons", catalog_id: "TANK-2026", revision: "R03" }, blueprint }), /revision_provenance must be an object/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {
  width_mm: { value: 600, status: "confirmed", provenance: { ...sourceLocator, block_sha256: "0".repeat(64) } },
  height_mm: { value: 900, status: "confirmed", provenance: { ...sourceLocator } },
  depth_mm: { value: 450, status: "confirmed", provenance: { ...sourceLocator } },
}, blueprint }), /block fingerprint does not match immutable SourceEvidence/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {
  width_mm: { value: 600, status: "confirmed", provenance: { kind: "source_document", page: 2, block_index: 0, block_sha256: sourceLocator.block_sha256 } },
  height_mm: { value: 900, status: "confirmed", provenance: { ...sourceLocator } },
  depth_mm: { value: 450, status: "confirmed", provenance: { ...sourceLocator } },
}, blueprint }), /does not resolve to a SourceEvidence page\/block/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {
  width_mm: { status: "confirmed", provenance: { kind: "user_confirmed" } }, height_mm: 900, depth_mm: 450,
}, blueprint }), /value is required when status=confirmed/);
const multiTypeSourceBlueprint = {
  ...blueprint,
  family: { ...blueprint.family, family_key: "multi_type_tank" },
  types: [
    { name: "T-600", values: { width: 600, height: 900, depth: 450 } },
    { name: "T-900", values: { width: 900, height: 1200, depth: 600 } },
  ],
};
const multiTypeConfirmedFields = {
  "T-600": { width_mm: { value: 600, provenance: { ...sourceLocator } }, height_mm: { value: 900, provenance: { ...sourceLocator } }, depth_mm: { value: 450, provenance: { ...sourceLocator } } },
  "T-900": { width_mm: { value: 900, provenance: { ...sourceLocator } }, height_mm: { value: 1200, provenance: { ...sourceLocator } }, depth_mm: { value: 600, provenance: { ...sourceLocator } } },
};
const multiTypeSourceSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "multi_type_tank", confirmed_fields: {}, type_confirmed_fields: multiTypeConfirmedFields, blueprint: multiTypeSourceBlueprint });
assert.equal(multiTypeSourceSpec.status, "human_confirmed");
assert.equal(multiTypeSourceSpec.field_provenance.type_specific_required, true);
assert.equal(multiTypeSourceSpec.field_provenance.type_fields.length, 2);
assert.equal(multiTypeSourceSpec.type_confirmed_fields["T-900"].width_mm.value, 900);
const revisionedMultiTypeSourceSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "multi_type_tank", confirmed_fields: {}, type_confirmed_fields: multiTypeConfirmedFields, type_catalog_revisions: { "T-600": { manufacturer: "DSCons", catalog_id: "TANK-2026", revision: "R03", revision_provenance: sourceLocator }, "T-900": { manufacturer: "DSCons", catalog_id: "TANK-2026", revision: "R04", revision_provenance: { kind: "user_confirmed" }, product_series: "Training Tank" } }, blueprint: multiTypeSourceBlueprint });
assert.equal(revisionedMultiTypeSourceSpec.catalog_revision_provenance.scope, "per_type");
assert.equal(revisionedMultiTypeSourceSpec.catalog_revision_provenance.types.length, 2);
assert.match(revisionedMultiTypeSourceSpec.catalog_revision_provenance.types[1].catalog.catalog_identity_sha256, /^[a-f0-9]{64}$/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "multi_type_tank", confirmed_fields: {}, type_confirmed_fields: multiTypeConfirmedFields, catalog_revision: { manufacturer: "DSCons", catalog_id: "TANK-2026", revision: "R03", revision_provenance: { kind: "user_confirmed" } }, blueprint: multiTypeSourceBlueprint }), /shared catalog_revision fallback is blocked/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "multi_type_tank", confirmed_fields: {}, type_confirmed_fields: multiTypeConfirmedFields, type_catalog_revisions: { "T-600": { manufacturer: "DSCons", catalog_id: "TANK-2026", revision: "R03", revision_provenance: sourceLocator } }, blueprint: multiTypeSourceBlueprint }), /missing Family Type\(s\): T-900/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "multi_type_tank", confirmed_fields: {}, type_confirmed_fields: {
  ...multiTypeConfirmedFields, "T-900": { ...multiTypeConfirmedFields["T-900"], width_mm: { value: 901, provenance: { ...sourceLocator } } },
}, blueprint: multiTypeSourceBlueprint }), /value for width does not match confirmed source field width_mm/);
const incompleteMultiTypeSourceSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "multi_type_tank", confirmed_fields: {}, type_confirmed_fields: {
  "T-600": multiTypeConfirmedFields["T-600"], "T-900": { width_mm: 900, height_mm: 1200 },
}, blueprint: multiTypeSourceBlueprint });
assert.equal(incompleteMultiTypeSourceSpec.status, "awaiting_human_confirmation");
assert.ok(incompleteMultiTypeSourceSpec.missing_fields.includes("T-900.depth_mm"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, type_confirmed_fields: { "T-1": { width_mm: 600 } }, blueprint }), /valid only when a Blueprint declares two or more Family Types/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parameters: blueprint.parameters.map((parameter) => parameter.key === "width" ? { ...parameter, source_field: "not_declared" } : parameter) } }), /must appear in blueprint\.required_source_fields/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450, manufacturer: "DSCons" }, blueprint: { ...blueprint, required_source_fields: [...blueprint.required_source_fields, "manufacturer"] } }), /required_source_field manufacturer must map to at least one direct Family Parameter/);
const directUntracedDimensionBlueprint = {
  ...blueprint,
  parameters: [...blueprint.parameters, { key: "untraced_width", name: "Untraced Width", data_type: "length", scope: "type", default: 80 }],
  parameter_order: [...blueprint.parameters.map((parameter) => parameter.key), "untraced_width"],
  types: blueprint.types.map((type) => ({ ...type, values: { ...type.values, untraced_width: 80 } })),
  parts: [{ ...blueprint.parts[0], profile: { ...blueprint.parts[0].profile, width_parameter: "untraced_width" } }],
  verification: { parameter_flex_cases: [...blueprint.verification.parameter_flex_cases, { parameter_key: "untraced_width", min: 60, nominal: 80, max: 120 }] },
};
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: directUntracedDimensionBlueprint }), /direct critical parameter\(s\) without source_field traceability: untraced_width/);
const purgeSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, publication: { purge_unused: { scope: "template_residue", max_passes: 7, unsupported_behavior: "skip_with_evidence" } } } });
assert.deepEqual(purgeSpec.blueprint.publication, { compact_rfa: true, preview_view: "auto", purge_unused: { scope: "template_residue", max_passes: 7, unsupported_behavior: "skip_with_evidence" } });
assert.ok(purgeSpec.blueprint_assessment.supported_features.includes("version_aware_template_residue_purge_revit_2024_plus"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, publication: { purge_unused: { scope: "all_document" } } } }), /scope must be one of: template_residue/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, publication: { purge_unused: { scope: "template_residue", max_passes: 11 } } } }), /max_passes must be an integer from 1 to 10/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, publication: { purge_unused: { scope: "template_residue", invented: true } } } }), /contains unsupported field invented/);
assert.deepEqual(readEvidenceRecord(custom.spec_id).blueprint.family, blueprint.family);
const persistedEnvelope = JSON.parse(await readFile(path.join(recordDirectory, `${custom.spec_id}.json`), "utf8"));
assert.match(persistedEnvelope.value_sha256, /^[a-f0-9]{64}$/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, performance_budget: { max_parameters: 2 } } }), /parameters=3 exceeds performance_budget\.max_parameters=2/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, performance_budget: { max_rfa_bytes: 1000 } } }), /max_rfa_bytes must be an integer from 1024/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, performance_budget: { invented_metric: 1 } } }), /unsupported field invented_metric/);
const recomputedComplexity = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, complexity_assessment: { within_declared_budget: false, metrics: { complexity_score: 0 } }, blueprint_hash: "spoofed" } });
assert.equal(recomputedComplexity.blueprint.complexity_assessment.metrics.complexity_score, 19);
assert.notEqual(recomputedComplexity.blueprint.blueprint_hash, "spoofed");

// Presentation subcategories are a first-class graphics contract, not just
// role-derived names.  The same declared Object Style can serve 3D forms and
// all supported 2D line families, while the budget counts only declarations
// made by the Blueprint (not Autodesk template residue).
const presentationSubcategories = [{
  key: "housing_graphics", name: "DSCons Housing Graphics", color_rgb: { r: 45, g: 115, b: 170 },
  projection_line_weight: 3, cut_line_weight: 5, projection_line_pattern_name: "Solid", cut_line_pattern_name: "Solid",
}];
const presentationBlueprint = {
  ...blueprint,
  presentation_subcategories: presentationSubcategories,
  parts: blueprint.parts.map((part) => ({ ...part, subcategory_key: "housing_graphics" })),
  symbolic_lines: [{ key: "symbol_outline", role: "Symbol outline", subcategory_key: "housing_graphics", plane: "xy", points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 0 }, { x_mm: 600, y_mm: 0, z_mm: 0 }], visibility: { coarse: true, medium: true, fine: true } }],
  model_lines: [{ key: "model_outline", role: "Model outline", subcategory_key: "housing_graphics", plane: "xy", points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 0 }, { x_mm: 0, y_mm: 900, z_mm: 0 }], visibility: { coarse: true, medium: true, fine: true } }],
};
const presentationSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: presentationBlueprint });
assert.equal(presentationSpec.status, "human_confirmed");
assert.equal(presentationSpec.blueprint.complexity_assessment.metrics.presentation_subcategories, 1);
assert.equal(presentationSpec.blueprint.complexity_assessment.metrics.two_dimensional_curves, 2);
assert.ok(presentationSpec.blueprint_assessment.supported_features.includes("presentation_subcategory_graphics_for_3d_and_2d_content"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...presentationBlueprint, parts: presentationBlueprint.parts.map((part) => ({ ...part, subcategory_key: "unknown_graphics" })) } }), /references unknown presentation subcategory unknown_graphics/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...presentationBlueprint, presentation_subcategories: [{ ...presentationSubcategories[0], name: "Housing Graphics" }] } }), /must start with DSCons/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...presentationBlueprint, presentation_subcategories: [{ ...presentationSubcategories[0], color_rgb: { r: 256, g: 115, b: 170 } }] } }), /color_rgb\.r must be an integer from 0 to 255/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...presentationBlueprint, presentation_subcategories: [...presentationSubcategories, { ...presentationSubcategories[0], key: "housing_graphics_copy" }] } }), /contains duplicate name/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...presentationBlueprint, performance_budget: { max_presentation_subcategories: 0 } } }), /presentation_subcategories=1 exceeds performance_budget\.max_presentation_subcategories=0/);

const detailPresentationBlueprint = {
  schema_version: "3.0", target_lod: "LOD_300",
  family: { family_key: "custom_detail_symbol", category: "detail_item", template_behavior: "detail_item", primary_axis: "x" },
  parameters: [], types: [{ name: "Default", values: {} }], parts: [], connectors: [], nested_components: [], symbolic_lines: [], model_lines: [],
  presentation_subcategories: [{ key: "detail_graphics", name: "DSCons Detail Graphics", color_rgb: { r: 0, g: 0, b: 0 }, projection_line_weight: 2 }],
  detail_lines: [{ key: "outline", role: "Detail outline", subcategory_key: "detail_graphics", plane: "xy", points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 0 }, { x_mm: 300, y_mm: 0, z_mm: 0 }] }],
  ui_fallbacks: [], verification: { parameter_flex_cases: [] },
};
const detailPresentationSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_detail_symbol", confirmed_fields: {}, blueprint: detailPresentationBlueprint });
assert.equal(detailPresentationSpec.blueprint_assessment.buildable_by_api, true);
assert.equal(detailPresentationSpec.blueprint.complexity_assessment.metrics.presentation_subcategories, 1);

const profilePresentationBlueprint = {
  schema_version: "3.0", target_lod: "LOD_300",
  family: { family_key: "custom_profile", category: "profile", template_behavior: "profile", primary_axis: "x" },
  parameters: [], types: [{ name: "Default", values: {} }], parts: [], connectors: [], nested_components: [], symbolic_lines: [], model_lines: [],
  presentation_subcategories: [{ key: "profile_graphics", name: "DSCons Profile Graphics", color_rgb: { r: 70, g: 70, b: 70 }, projection_line_weight: 1 }],
  profile_loops: [{ key: "profile_outline", role: "Profile outline", subcategory_key: "profile_graphics", plane: "xy", points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 0 }, { x_mm: 400, y_mm: 0, z_mm: 0 }, { x_mm: 400, y_mm: 200, z_mm: 0 }, { x_mm: 0, y_mm: 200, z_mm: 0 }] }],
  ui_fallbacks: [], verification: { parameter_flex_cases: [] },
};
const profilePresentationSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_profile", confirmed_fields: {}, blueprint: profilePresentationBlueprint });
assert.equal(profilePresentationSpec.blueprint_assessment.buildable_by_api, true);
assert.equal(profilePresentationSpec.blueprint.complexity_assessment.metrics.presentation_subcategories, 1);

// Dynamic Tag Labels are intentionally a controlled Family Editor handoff: the
// Blueprint validates every target field/format but must never pretend that the
// API has created a Revit Label.
const tagBlueprint = {
  schema_version: "3.0", target_lod: "LOD_300",
  family: { family_key: "duct_data_tag", category: "tag", template_behavior: "tag", primary_axis: "x" },
  parameters: [], types: [{ name: "Default", values: {} }],
  parts: [], connectors: [], nested_components: [], symbolic_lines: [], model_lines: [], detail_lines: [], profile_loops: [],
  tag_background: "opaque",
  tag_labels: [
    { key: "system", semantic_field: "system_abbreviation", prefix: "SYS: " },
    { key: "size", semantic_field: "size", value_format: "custom", rounding_mm: 1 },
    { key: "cod", semantic_field: "center_of_duct", prefix: "COD ", value_format: "custom", rounding_mm: 5, show_plus: true },
    { key: "asset_id", semantic_field: "shared_parameter", shared_parameter_name: "DSCons Asset ID", shared_parameter_guid: "11111111-1111-4111-8111-111111111111" },
  ],
  ui_fallbacks: [{ action: "tag_label_editor", reason: "Revit Dynamic Label authoring requires the controlled Family Editor UI.", requires_human_checkpoint: true }],
  verification: { parameter_flex_cases: [] },
};
const tagSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "duct_data_tag", confirmed_fields: {}, blueprint: tagBlueprint });
assert.equal(tagSpec.status, "requires_capability_or_ui_fallback");
assert.equal(tagSpec.blueprint_assessment.buildable_by_api, false);
assert.equal(tagSpec.blueprint_assessment.ui_fallback_required, true);
assert.ok(tagSpec.blueprint_assessment.supported_features.includes("tag_label_field_and_format_preflight"));
assert.deepEqual(tagSpec.blueprint_assessment.unsupported_features, ["tag_label_editor_requires_controlled_ui"]);
assert.equal(tagSpec.blueprint.tag_ui_plan.label_count, 4);
assert.equal(tagSpec.blueprint.tag_ui_plan.background, "opaque");
assert.equal(tagSpec.blueprint.tag_ui_plan.labels[2].show_plus, true);
assert.equal(tagSpec.blueprint.tag_ui_plan.labels[3].shared_parameter_guid, "11111111-1111-4111-8111-111111111111");
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "duct_data_tag", confirmed_fields: {}, blueprint: { ...tagBlueprint, ui_fallbacks: [] } }), /requires exactly one ui_fallbacks action=tag_label_editor/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "duct_data_tag", confirmed_fields: {}, blueprint: { ...tagBlueprint, family: { ...tagBlueprint.family, category: "annotation" } } }), /template_behavior=tag requires family.category=tag/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "duct_data_tag", confirmed_fields: {}, blueprint: { ...tagBlueprint, tag_labels: tagBlueprint.tag_labels.map((label) => label.key === "system" ? { ...label, value_format: "custom" } : label) } }), /value_format=custom is valid only for Size or elevation fields/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "duct_data_tag", confirmed_fields: {}, blueprint: { ...tagBlueprint, tag_labels: tagBlueprint.tag_labels.map((label) => label.key === "asset_id" ? { ...label, shared_parameter_guid: "not-a-guid" } : label) } }), /shared_parameter_guid must be a RFC 4122 UUID/);

const lightingBlueprint = {
  ...blueprint,
  family: { family_key: "photometric_troffer", category: "lighting_fixture", template_behavior: "level_based", primary_axis: "x" },
  required_source_fields: [], parameters: [],
  types: [{ name: "4000 lm", values: {} }],
  parts: [{ ...blueprint.parts[0], profile: { shape: "rectangle", width_mm: 600, height_mm: 75 }, start_mm: -600, end_mm: 0, depth_parameter: undefined }],
  light_source: {
    shape_style: "circle", distribution_style: "photometric_web",
    type_settings: [{
      type_name: "4000 lm", emit_diameter_mm: 120,
      photometric_web: { file_name: iesFileName, sha256: iesSha256, tilt_angle_degrees: 0 },
      initial_intensity: { method: "luminous_flux", luminous_flux_lm: 4000 },
      initial_color: { mode: "temperature", temperature_kelvin: 4000 },
      loss_factor: { mode: "advanced", ballast: 0.95, lamp_lumen_depreciation: 0.9, lamp_tilt: 1, luminaire_dirt_depreciation: 0.85, surface_depreciation: 0.95, temperature: 1, voltage: 1 },
      color_filter_rgb: { r: 255, g: 250, b: 240 }, dimming_color: "none",
    }],
  },
  verification: { parameter_flex_cases: [] },
};
const lightingSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "photometric_troffer", confirmed_fields: {}, approved_demo_directory: approved, blueprint: lightingBlueprint });
assert.equal(lightingSpec.status, "human_confirmed");
assert.equal(lightingSpec.blueprint.complexity_assessment.metrics.light_sources, 1);
assert.equal(lightingSpec.blueprint.complexity_assessment.metrics.complexity_score, 33);
assert.ok(lightingSpec.blueprint_assessment.supported_features.includes("lighting_fixture_shape_distribution_photometrics_and_ies"));
assert.deepEqual(lightingSpec.citations.find((item) => item.source_kind === "ies_photometric_web"), { source_kind: "ies_photometric_web", file_name: iesFileName, sha256: iesSha256, size_bytes: iesBytes.length, path_policy: "direct_child_of_approved_demo_directory" });
const spotLightingBlueprint = {
  ...lightingBlueprint,
  family: { ...lightingBlueprint.family, family_key: "spot_downlight" },
  light_source: {
    shape_style: "point", distribution_style: "spot",
    type_settings: [{ type_name: "4000 lm", spot: { beam_angle_degrees: 30, field_angle_degrees: 45, tilt_angle_degrees: 0 }, initial_intensity: { method: "wattage", wattage_w: 35, efficacy_lm_per_w: 110 }, initial_color: { mode: "preset", preset: "metal_halide" }, loss_factor: { mode: "basic", value: 0.9 }, dimming_color: "incandescent" }],
  },
};
const spotLightingSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "spot_downlight", confirmed_fields: {}, blueprint: spotLightingBlueprint });
assert.equal(spotLightingSpec.status, "human_confirmed");

const literalProfile = { shape: "rectangle", width_mm: 300, height_mm: 200 };
const visibility = { coarse: true, medium: true, fine: true };
const point = (x_mm, y_mm, z_mm) => ({ x_mm, y_mm, z_mm });
const primitiveCases = [
  { key: "turned_body", primitive: "revolution", operation: "solid", axis: "x", role: "TurnedBody", profile: literalProfile, profile_plane: "xz", profile_origin_mm: point(0, 0, 300), axis_start_mm: point(-500, 0, 0), axis_end_mm: point(500, 0, 0), start_angle_degrees: 0, end_angle_degrees: 360, visibility },
  { key: "swept_body", primitive: "sweep", operation: "solid", axis: "x", role: "SweptBody", profile: literalProfile, path: { kind: "polyline", plane: "xy", points_mm: [point(0, 0, 0), point(500, 0, 0), point(700, 200, 0)] }, profile_location: "start", visibility },
  { key: "blend_body", primitive: "blend", operation: "solid", axis: "x", role: "BlendBody", profile: literalProfile, end_profile: { shape: "circle", diameter_mm: 180 }, start_mm: 0, end_mm: 500, visibility },
  { key: "swept_blend_body", primitive: "swept_blend", operation: "solid", axis: "x", role: "SweptBlendBody", profile: literalProfile, end_profile: { shape: "circle", diameter_mm: 180 }, path: { kind: "line", plane: "xy", points_mm: [point(0, 0, 0), point(500, 0, 0)] }, visibility },
];
for (const part of primitiveCases) {
  const primitiveSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [part] } });
  assert.equal(primitiveSpec.status, "human_confirmed", `${part.primitive} should be API-buildable with literal geometry`);
  assert.equal(primitiveSpec.blueprint_assessment.buildable_by_api, true);
}
const literalOvalProfile = { shape: "oval", major_axis: "width", width_mm: 240, height_mm: 120 };
for (const part of primitiveCases) {
  const ovalPart = { ...part, profile: literalOvalProfile, ...(part.end_profile ? { end_profile: { shape: "oval", major_axis: "height", width_mm: 100, height_mm: 180 } } : {}) };
  const ovalPrimitiveSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [ovalPart] } });
  assert.equal(ovalPrimitiveSpec.status, "human_confirmed", `${part.primitive} should be API-buildable with a literal flat-oval profile`);
  assert.equal(ovalPrimitiveSpec.blueprint_assessment.buildable_by_api, true);
}
const parameterizedOvalExtrusionBlueprint = {
  ...blueprint,
  family: { ...blueprint.family, family_key: "parameterized_oval_body" },
  required_source_fields: [],
  parameters: [
    { key: "oval_width", name: "Oval Width", data_type: "length", scope: "type", default: 500 },
    { key: "oval_height", name: "Oval Height", data_type: "length", scope: "type", default: 250 },
    { key: "body_length", name: "Body Length", data_type: "length", scope: "type", default: 600 },
  ],
  types: [{ name: "500x250", values: { oval_width: 500, oval_height: 250, body_length: 600 } }],
  parts: [{ key: "oval_body", primitive: "extrusion", operation: "solid", axis: "x", role: "OvalBody", profile: { shape: "oval", major_axis: "width", width_parameter: "oval_width", height_parameter: "oval_height" }, start_mm: -300, end_mm: 300, depth_parameter: "body_length", visibility }],
  verification: { parameter_flex_cases: [
    { parameter_key: "oval_width", min: 450, nominal: 500, max: 650 }, { parameter_key: "oval_height", min: 100, nominal: 250, max: 300 }, { parameter_key: "body_length", min: 400, nominal: 600, max: 800 },
  ] },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "parameterized_oval_body", confirmed_fields: {}, blueprint: parameterizedOvalExtrusionBlueprint }).blueprint_assessment.buildable_by_api, true);
const parameterizedOvalBlendBlueprint = {
  ...parameterizedOvalExtrusionBlueprint,
  family: { ...parameterizedOvalExtrusionBlueprint.family, family_key: "parameterized_oval_transition" },
  parts: [{ key: "oval_transition", primitive: "blend", operation: "solid", axis: "x", role: "OvalTransition", profile: { shape: "oval", major_axis: "width", width_parameter: "oval_width", height_parameter: "oval_height" }, end_profile: { shape: "oval", major_axis: "width", width_parameter: "oval_width", height_parameter: "oval_height" }, start_mm: 0, end_mm: 600, visibility }],
  verification: { parameter_flex_cases: parameterizedOvalExtrusionBlueprint.verification.parameter_flex_cases.filter((item) => item.parameter_key !== "body_length") },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "parameterized_oval_transition", confirmed_fields: {}, blueprint: parameterizedOvalBlendBlueprint }).blueprint_assessment.buildable_by_api, true);
const parameterizedRevolutionBlueprint = {
  ...blueprint,
  family: { ...blueprint.family, family_key: "parameterized_revolution_body" }, required_source_fields: [],
  parameters: [
    { key: "revolve_width", name: "Revolve Width", data_type: "length", scope: "type", default: 160 },
    { key: "revolve_height", name: "Revolve Height", data_type: "length", scope: "type", default: 100 },
  ],
  types: [{ name: "160x100", values: { revolve_width: 160, revolve_height: 100 } }],
  parts: [{ ...primitiveCases[0], key: "parameterized_revolution", role: "ParameterizedRevolution", profile: { shape: "rectangle", width_parameter: "revolve_width", height_parameter: "revolve_height" }, profile_origin_mm: point(0, 0, 400) }],
  verification: { parameter_flex_cases: [
    { parameter_key: "revolve_width", min: 120, nominal: 160, max: 220 },
    { parameter_key: "revolve_height", min: 80, nominal: 100, max: 140 },
  ] },
};
const parameterizedRevolutionSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "parameterized_revolution_body", confirmed_fields: {}, blueprint: parameterizedRevolutionBlueprint });
assert.equal(parameterizedRevolutionSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(parameterizedRevolutionSpec.blueprint_assessment.supported_features.includes("parameterized_revolution_rectangle_circle_ring_oval_profiles"));
const parameterizedRingRevolutionBlueprint = {
  ...parameterizedRevolutionBlueprint,
  family: { ...parameterizedRevolutionBlueprint.family, family_key: "parameterized_ring_revolution" },
  parameters: [
    { key: "outer_diameter", name: "Outer Diameter", data_type: "length", scope: "type", default: 160 },
    { key: "inner_diameter", name: "Inner Diameter", data_type: "length", scope: "type", default: 80 },
  ],
  types: [{ name: "OD160-ID80", values: { outer_diameter: 160, inner_diameter: 80 } }],
  parts: [{ ...primitiveCases[0], key: "ring_revolution", role: "RingRevolution", profile: { shape: "ring", outer_diameter_parameter: "outer_diameter", inner_diameter_parameter: "inner_diameter" }, profile_origin_mm: point(0, 0, 400) }],
  verification: { parameter_flex_cases: [
    { parameter_key: "outer_diameter", min: 140, nominal: 160, max: 220 },
    { parameter_key: "inner_diameter", min: 40, nominal: 80, max: 120 },
  ] },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "parameterized_ring_revolution", confirmed_fields: {}, blueprint: parameterizedRingRevolutionBlueprint }).blueprint_assessment.buildable_by_api, true);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "parameterized_ring_revolution", confirmed_fields: {}, blueprint: { ...parameterizedRingRevolutionBlueprint, verification: { parameter_flex_cases: parameterizedRingRevolutionBlueprint.verification.parameter_flex_cases.map((item) => item.parameter_key === "outer_diameter" ? { ...item, min: 100 } : item) } } }), /outer-diameter flex minimum must be strictly greater/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "parameterized_revolution_body", confirmed_fields: {}, blueprint: { ...parameterizedRevolutionBlueprint, parts: parameterizedRevolutionBlueprint.parts.map((part) => ({ ...part, profile_origin_mm: point(0, 0, 50) })) } }), /touches or crosses its axis across the approved flex range/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "parameterized_revolution_body", confirmed_fields: {}, blueprint: { ...parameterizedRevolutionBlueprint, types: [{ name: "Mismatch", values: { revolve_width: 170, revolve_height: 100 } }] } }), /first Family type value for revolve_width must equal its approved nominal flex value/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "parameterized_revolution_body", confirmed_fields: {}, blueprint: { ...parameterizedRevolutionBlueprint, parameters: parameterizedRevolutionBlueprint.parameters.map((parameter) => parameter.key === "revolve_width" ? { ...parameter, scope: "instance" } : parameter) } }), /parameterized revolution profile revolve_width must reference a direct Length Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "parameterized_ring_revolution", confirmed_fields: {}, blueprint: { ...parameterizedRingRevolutionBlueprint, parts: parameterizedRingRevolutionBlueprint.parts.map((part) => ({ ...part, profile: { ...part.profile, outer_diameter_mm: 160 } })) } }), /ring must declare exactly both/);
const parameterizedSweep = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...primitiveCases[1], profile: blueprint.parts[0].profile }] } });
assert.equal(parameterizedSweep.status, "requires_capability_or_ui_fallback");
assert.equal(parameterizedSweep.blueprint_assessment.buildable_by_api, false);
assert.ok(parameterizedSweep.blueprint_assessment.unsupported_features.includes("parameterized_sweep_profile_family_editor_ui"));
assert.equal(parameterizedSweep.blueprint.parameterized_sweep_profile_ui_plan.action, "sweep_profile_editor");
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...primitiveCases[1], profile: blueprint.parts[0].profile, path: { kind: "polyline", plane: "xy", points_mm: [point(0, 0, 0), point(0, 500, 0), point(200, 700, 0)] } }] } }), /first path segment to follow \+X/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...primitiveCases[1], profile: blueprint.parts[0].profile, profile_location: "midpoint" }] } }), /parameterized polyline sweep profile requires profile_location=start/);
const pipeElbowBlueprint = {
  ...blueprint,
  family: { family_key: "pipe_elbow_90", category: "pipe_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "elbow", round_connector_dimension: "diameter" },
  required_source_fields: [],
  parameters: [
    { key: "bend_radius", name: "Bend Radius", data_type: "length", scope: "type", default: 100 },
    { key: "nominal_diameter", name: "Nominal Diameter", data_type: "length", scope: "type", default: 50, shared_guid: "71a0e20b-3a4d-4f52-ae61-878b0500b690" },
  ],
  types: [{ name: "DN50-R100", values: { bend_radius: 100, nominal_diameter: 50 } }],
  parts: [{ key: "elbow_body", primitive: "sweep", operation: "solid", axis: "x", role: "ElbowBody", profile: { shape: "circle", diameter_parameter: "nominal_diameter" }, path: { kind: "arc", plane: "xy", tangent_intersection_mm: point(0, 0, 0), start_tangent: point(1, 0, 0), turn_direction: "counterclockwise", radius_parameter: "bend_radius", sweep_angle_degrees: 90 }, profile_location: "start", visibility }],
  connectors: [
    { key: "inlet", discipline: "pipe", role: "Inlet", host_part: "elbow_body", host_face: "path_start", system_classification: "Fitting", profile: "round", diameter_parameter: "nominal_diameter", primary: true, linked_to: "outlet", flow_direction: "bidirectional", flow_configuration: "calculated" },
    { key: "outlet", discipline: "pipe", role: "Outlet", host_part: "elbow_body", host_face: "path_end", system_classification: "Fitting", profile: "round", diameter_parameter: "nominal_diameter", linked_to: "inlet", flow_direction: "bidirectional", flow_configuration: "calculated" },
  ],
  verification: { parameter_flex_cases: [
    { parameter_key: "bend_radius", min: 75, nominal: 100, max: 150 },
    { parameter_key: "nominal_diameter", min: 25, nominal: 50, max: 80 },
  ] },
};
const pipeElbowSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: pipeElbowBlueprint });
assert.equal(pipeElbowSpec.status, "requires_capability_or_ui_fallback");
assert.equal(pipeElbowSpec.blueprint_assessment.buildable_by_api, false);
assert.ok(pipeElbowSpec.blueprint_assessment.unsupported_features.includes("parameterized_sweep_profile_family_editor_ui"));
assert.ok(pipeElbowSpec.blueprint_assessment.supported_features.includes("parameterized_elbow_45_90_geometry_and_tangent_connectors"));
const ductElbowBlueprint = {
  ...pipeElbowBlueprint,
  family: { family_key: "duct_elbow_45", category: "duct_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "elbow" },
  parameters: [
    { key: "bend_radius", name: "Bend Radius", data_type: "length", scope: "type", default: 400 },
    { key: "duct_width", name: "Duct Width", data_type: "length", scope: "type", default: 300 },
    { key: "duct_height", name: "Duct Height", data_type: "length", scope: "type", default: 200 },
  ],
  types: [{ name: "300x200-R400", values: { bend_radius: 400, duct_width: 300, duct_height: 200 } }],
  parts: [{ ...pipeElbowBlueprint.parts[0], profile: { shape: "rectangle", width_parameter: "duct_width", height_parameter: "duct_height" }, path: { ...pipeElbowBlueprint.parts[0].path, plane: "xz", sweep_angle_degrees: 45, radius_parameter: "bend_radius" } }],
  connectors: pipeElbowBlueprint.connectors.map((connector) => ({ ...connector, discipline: "duct", profile: "rectangular", diameter_parameter: undefined, width_parameter: "duct_width", height_parameter: "duct_height" })),
  verification: { parameter_flex_cases: [
    { parameter_key: "bend_radius", min: 300, nominal: 400, max: 600 },
    { parameter_key: "duct_width", min: 200, nominal: 300, max: 400 },
    { parameter_key: "duct_height", min: 100, nominal: 200, max: 250 },
  ] },
};
const ductElbowSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "duct_elbow_45", confirmed_fields: {}, blueprint: ductElbowBlueprint });
assert.equal(ductElbowSpec.status, "requires_capability_or_ui_fallback");
assert.equal(ductElbowSpec.blueprint_assessment.buildable_by_api, false);
assert.ok(ductElbowSpec.blueprint_assessment.unsupported_features.includes("parameterized_sweep_profile_family_editor_ui"));
const ovalDuctElbowBlueprint = {
  ...ductElbowBlueprint,
  family: { ...ductElbowBlueprint.family, family_key: "oval_duct_elbow_90" },
  parameters: [
    { key: "bend_radius", name: "Bend Radius", data_type: "length", scope: "type", default: 600 },
    { key: "duct_width", name: "Oval Width", data_type: "length", scope: "type", default: 500, shared_guid: "05255c60-e22d-4865-a7ee-d73a80f702bd" },
    { key: "duct_height", name: "Oval Height", data_type: "length", scope: "type", default: 250, shared_guid: "d7bc64bc-372f-49dd-b0f2-200275c01991" },
  ],
  types: [{ name: "500x250-R600", values: { bend_radius: 600, duct_width: 500, duct_height: 250 } }],
  parts: ductElbowBlueprint.parts.map((part) => ({ ...part, profile: { shape: "oval", major_axis: "width", width_parameter: "duct_width", height_parameter: "duct_height" }, path: { ...part.path, plane: "xy", sweep_angle_degrees: 90, radius_parameter: "bend_radius" } })),
  connectors: ductElbowBlueprint.connectors.map((connector) => ({ ...connector, profile: "oval" })),
  verification: { parameter_flex_cases: [
    { parameter_key: "bend_radius", min: 500, nominal: 600, max: 800 },
    { parameter_key: "duct_width", min: 450, nominal: 500, max: 650 },
    { parameter_key: "duct_height", min: 100, nominal: 250, max: 300 },
  ] },
};
const ovalDuctElbowSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "oval_duct_elbow_90", confirmed_fields: {}, blueprint: ovalDuctElbowBlueprint });
assert.equal(ovalDuctElbowSpec.status, "requires_capability_or_ui_fallback");
assert.equal(ovalDuctElbowSpec.blueprint_assessment.buildable_by_api, false);
assert.ok(ovalDuctElbowSpec.blueprint_assessment.supported_features.includes("parametric_flat_oval_profile_with_orientation_guard"));
assert.ok(ovalDuctElbowSpec.blueprint_assessment.unsupported_features.includes("parameterized_sweep_profile_family_editor_ui"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "oval_duct_elbow_90", confirmed_fields: {}, blueprint: { ...ovalDuctElbowBlueprint, parts: ovalDuctElbowBlueprint.parts.map((part) => ({ ...part, profile: { ...part.profile, major_axis: undefined } })) } }), /major_axis must/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "oval_duct_elbow_90", confirmed_fields: {}, blueprint: { ...ovalDuctElbowBlueprint, parts: ovalDuctElbowBlueprint.parts.map((part) => ({ ...part, profile: { shape: "oval", major_axis: "width", width_parameter: "duct_width", height_mm: 250 } })) } }), /exactly both width_parameter\/height_parameter or both width_mm\/height_mm/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "oval_duct_elbow_90", confirmed_fields: {}, blueprint: { ...ovalDuctElbowBlueprint, parts: ovalDuctElbowBlueprint.parts.map((part) => ({ ...part, profile: { shape: "oval", major_axis: "width", width_mm: 250, height_mm: 500 } })) } }), /major_axis=width requires its major dimension/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "oval_duct_elbow_90", confirmed_fields: {}, blueprint: { ...ovalDuctElbowBlueprint, verification: { parameter_flex_cases: ovalDuctElbowBlueprint.verification.parameter_flex_cases.map((item) => item.parameter_key === "duct_width" ? { ...item, min: 275 } : item) } } }), /major-axis flex minimum must be strictly greater/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "oval_duct_elbow_90", confirmed_fields: {}, blueprint: { ...ovalDuctElbowBlueprint, types: [{ name: "Bad", values: { bend_radius: 600, duct_width: 200, duct_height: 250 } }] } }), /must keep its declared major dimension/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "oval_duct_elbow_90", confirmed_fields: {}, blueprint: { ...ovalDuctElbowBlueprint, connectors: ovalDuctElbowBlueprint.connectors.map((connector) => ({ ...connector, profile: "rectangular" })) } }), /oval connector size parameters must match/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "oval_pipe_elbow", confirmed_fields: {}, blueprint: { ...ovalDuctElbowBlueprint, family: { ...ovalDuctElbowBlueprint.family, family_key: "oval_pipe_elbow", category: "pipe_fitting" }, connectors: ovalDuctElbowBlueprint.connectors.map((connector) => ({ ...connector, discipline: "pipe" })) } }), /profile oval is not physically valid for pipe/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: { ...pipeElbowBlueprint, parts: pipeElbowBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, radius_mm: 100 } })) } }), /exactly one radius_mm or radius_parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: { ...pipeElbowBlueprint, parts: pipeElbowBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, radius_parameter: undefined } })) } }), /exactly one radius_mm or radius_parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: { ...pipeElbowBlueprint, parameters: pipeElbowBlueprint.parameters.map((parameter) => parameter.key === "bend_radius" ? { ...parameter, scope: "instance" } : parameter) } }), /radius_parameter must reference a direct Length Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: { ...pipeElbowBlueprint, verification: { parameter_flex_cases: pipeElbowBlueprint.verification.parameter_flex_cases.filter((item) => item.parameter_key !== "bend_radius") } } }), /bend_radius requires verification\.parameter_flex_cases|geometry\/connector parameter bend_radius requires/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: { ...pipeElbowBlueprint, verification: { parameter_flex_cases: pipeElbowBlueprint.verification.parameter_flex_cases.map((item) => item.parameter_key === "bend_radius" ? { ...item, min: 35 } : item) } } }), /minimum bend radius must be greater/);
for (const angle of [0, 180]) assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: { ...pipeElbowBlueprint, parts: pipeElbowBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, sweep_angle_degrees: angle } })) } }), /greater than zero and less than 180/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: { ...pipeElbowBlueprint, parts: pipeElbowBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, tangent_intersection_mm: point(10, 0, 0) } })) } }), /must be the Family origin/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: { ...pipeElbowBlueprint, connectors: pipeElbowBlueprint.connectors.map((connector) => connector.key === "inlet" ? { ...connector, host_face: "path_end" } : connector) } }), /path_start and path_end/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_elbow_90", confirmed_fields: {}, blueprint: { ...pipeElbowBlueprint, connectors: pipeElbowBlueprint.connectors.map((connector) => connector.key === "outlet" ? { ...connector, diameter_parameter: "bend_radius" } : connector) } }), /diameter_parameter must match/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, connectors: [{ key: "bad_path", discipline: "pipe", role: "Bad", host_part: "body", host_face: "path_start", system_classification: "Fitting", profile: "round", diameter_parameter: "width" }] } }), /requires a line, polyline, arc or parameterized offset sweep host_part/);
const pipeTeeBlueprint = {
  ...blueprint,
  required_source_fields: [],
  family: { family_key: "pipe_tee", category: "pipe_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "tee", round_connector_dimension: "diameter" },
  parameters: [
    { key: "run_diameter", name: "Run Diameter", data_type: "length", scope: "type", default: 50, shared_guid: "a5c9b6a4-27bb-49bd-9847-aa19969f8d81" },
    { key: "branch_diameter", name: "Branch Diameter", data_type: "length", scope: "type", default: 32, shared_guid: "ee3f2211-e728-44e4-a83e-1ea4c91dfd60" },
    { key: "run_length", name: "Run Length", data_type: "length", scope: "type", default: 160 },
    { key: "branch_length", name: "Branch Length", data_type: "length", scope: "type", default: 90 },
  ],
  parameter_order: ["run_diameter", "branch_diameter", "run_length", "branch_length"],
  types: [{ name: "DN50x32", values: { run_diameter: 50, branch_diameter: 32, run_length: 160, branch_length: 90 } }],
  parts: [
    { key: "run", primitive: "extrusion", operation: "solid", axis: "x", role: "TeeRun", profile: { shape: "circle", diameter_parameter: "run_diameter" }, start_mm: -80, end_mm: 80, depth_parameter: "run_length", join_with: ["branch"], visibility },
    { key: "branch", primitive: "extrusion", operation: "solid", axis: "y", role: "TeeBranch", profile: { shape: "circle", diameter_parameter: "branch_diameter" }, start_mm: 0, end_mm: 90, depth_parameter: "branch_length", visibility },
  ],
  connectors: [
    { key: "run_inlet", discipline: "pipe", role: "Run Inlet", host_part: "run", host_face: "start", system_classification: "Fitting", profile: "round", diameter_parameter: "run_diameter", primary: true, flow_direction: "bidirectional", flow_configuration: "calculated" },
    { key: "run_outlet", discipline: "pipe", role: "Run Outlet", host_part: "run", host_face: "end", system_classification: "Fitting", profile: "round", diameter_parameter: "run_diameter", flow_direction: "bidirectional", flow_configuration: "calculated" },
    { key: "branch_outlet", discipline: "pipe", role: "Branch Outlet", host_part: "branch", host_face: "end", system_classification: "Fitting", profile: "round", diameter_parameter: "branch_diameter", flow_direction: "bidirectional", flow_configuration: "calculated" },
  ],
  verification: { parameter_flex_cases: [
    { parameter_key: "run_diameter", min: 25, nominal: 50, max: 80 },
    { parameter_key: "branch_diameter", min: 15, nominal: 32, max: 50 },
    { parameter_key: "run_length", min: 100, nominal: 160, max: 240 },
    { parameter_key: "branch_length", min: 60, nominal: 90, max: 150 },
  ] },
};
const pipeTeeSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tee", confirmed_fields: {}, blueprint: pipeTeeBlueprint });
assert.equal(pipeTeeSpec.status, "human_confirmed");
assert.equal(pipeTeeSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(pipeTeeSpec.blueprint_assessment.supported_features.includes("orthogonal_xyz_extrusion_axes"));
assert.ok(pipeTeeSpec.blueprint_assessment.supported_features.includes("parameterized_orthogonal_tee_geometry_and_three_connector_placement"));
assert.equal(pipeTeeSpec.blueprint.parts.find((part) => part.key === "branch").axis, "y");
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tee", confirmed_fields: {}, blueprint: { ...pipeTeeBlueprint, parts: pipeTeeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis: "x" } : part) } }), /branch must be a solid Y- or Z-axis extrusion/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tee", confirmed_fields: {}, blueprint: { ...pipeTeeBlueprint, parts: pipeTeeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, start_mm: 10, end_mm: 100 } : part) } }), /with depth_parameter must be centered about zero or start at zero|branch must start or end at the Family origin/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tee", confirmed_fields: {}, blueprint: { ...pipeTeeBlueprint, parts: pipeTeeBlueprint.parts.map((part) => part.key === "run" ? { ...part, join_with: [] } : part) } }), /must declare join_with/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tee", confirmed_fields: {}, blueprint: { ...pipeTeeBlueprint, connectors: pipeTeeBlueprint.connectors.map((connector) => connector.key === "branch_outlet" ? { ...connector, host_face: "start" } : connector) } }), /outer branch face/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tee", confirmed_fields: {}, blueprint: { ...pipeTeeBlueprint, connectors: pipeTeeBlueprint.connectors.map((connector) => connector.key === "run_inlet" ? { ...connector, linked_to: "run_outlet" } : connector) } }), /multi-port link topology is runtime-certified/);
const pipeTapPerpendicularBlueprint = {
  ...pipeTeeBlueprint,
  family: { ...pipeTeeBlueprint.family, family_key: "pipe_tap_perpendicular", part_type: "tap_perpendicular" },
};
const pipeTapPerpendicularSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tap_perpendicular", confirmed_fields: {}, blueprint: pipeTapPerpendicularBlueprint });
assert.equal(pipeTapPerpendicularSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(pipeTapPerpendicularSpec.blueprint_assessment.supported_features.includes("bounded_tap_fitting_geometry"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tap_perpendicular", confirmed_fields: {}, blueprint: { ...pipeTapPerpendicularBlueprint, parts: pipeTapPerpendicularBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis_direction: { x: 1, y: 1, z: 0 }, axis: undefined } : part) } }), /branch must be a solid Y- or Z-axis extrusion/);
const pipeTapAdjustableBlueprint = {
  ...pipeTeeBlueprint,
  family: { ...pipeTeeBlueprint.family, family_key: "pipe_tap_adjustable", part_type: "tap_adjustable" },
  parts: pipeTeeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis: undefined, axis_direction: { x: Math.SQRT1_2, y: Math.SQRT1_2, z: 0 }, role: "TapAdjustableBranch" } : part),
};
const pipeTapAdjustableSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tap_adjustable", confirmed_fields: {}, blueprint: pipeTapAdjustableBlueprint });
assert.equal(pipeTapAdjustableSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(pipeTapAdjustableSpec.blueprint_assessment.supported_features.includes("bounded_tap_fitting_geometry"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_tap_adjustable", confirmed_fields: {}, blueprint: { ...pipeTapAdjustableBlueprint, parts: pipeTapAdjustableBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis: "y", axis_direction: undefined } : part) } }), /requires axis_direction instead of an orthogonal axis/);
const pipePantsBlueprint = {
  ...pipeTeeBlueprint,
  family: { ...pipeTeeBlueprint.family, family_key: "pipe_pants_symmetric", part_type: "pants" },
  parts: [
    { key: "inlet", primitive: "extrusion", operation: "solid", axis: "x", role: "PantsInlet", profile: { shape: "circle", diameter_parameter: "run_diameter" }, start_mm: -80, end_mm: 0, depth_parameter: "run_length", join_with: ["outlet_left", "outlet_right"], visibility },
    { key: "outlet_left", primitive: "extrusion", operation: "solid", axis_direction: { x: Math.SQRT1_2, y: Math.SQRT1_2, z: 0 }, role: "PantsOutletLeft", profile: { shape: "circle", diameter_parameter: "branch_diameter" }, start_mm: 0, end_mm: 90, depth_parameter: "branch_length", visibility },
    { key: "outlet_right", primitive: "extrusion", operation: "solid", axis_direction: { x: Math.SQRT1_2, y: -Math.SQRT1_2, z: 0 }, role: "PantsOutletRight", profile: { shape: "circle", diameter_parameter: "branch_diameter" }, start_mm: 0, end_mm: 90, depth_parameter: "branch_length", visibility },
  ],
  connectors: [
    { key: "inlet", discipline: "pipe", role: "Pants Inlet", host_part: "inlet", host_face: "start", system_classification: "Fitting", profile: "round", diameter_parameter: "run_diameter", primary: true, flow_direction: "bidirectional", flow_configuration: "calculated" },
    { key: "outlet_left", discipline: "pipe", role: "Pants Outlet Left", host_part: "outlet_left", host_face: "end", system_classification: "Fitting", profile: "round", diameter_parameter: "branch_diameter", flow_direction: "bidirectional", flow_configuration: "calculated" },
    { key: "outlet_right", discipline: "pipe", role: "Pants Outlet Right", host_part: "outlet_right", host_face: "end", system_classification: "Fitting", profile: "round", diameter_parameter: "branch_diameter", flow_direction: "bidirectional", flow_configuration: "calculated" },
  ],
};
const pipePantsSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_pants_symmetric", confirmed_fields: {}, blueprint: pipePantsBlueprint });
assert.equal(pipePantsSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(pipePantsSpec.blueprint_assessment.supported_features.includes("bounded_symmetric_pants_fitting_geometry"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_pants_symmetric", confirmed_fields: {}, blueprint: { ...pipePantsBlueprint, parts: pipePantsBlueprint.parts.map((part) => part.key === "outlet_right" ? { ...part, axis_direction: { x: Math.sqrt(3) / 2, y: -0.5, z: 0 } } : part) } }), /must be symmetric about the X-axis/);
const pathwayConnector = (connector, discipline) => {
  const { system_classification, flow_direction, flow_configuration, loss_method, loss_coefficient, pressure_drop_pa, joint_type, gender, engagement_length_mm, ...bounded } = connector;
  return { ...bounded, discipline };
};
const conduitElbowBlueprint = {
  ...pipeElbowBlueprint,
  family: { family_key: "conduit_elbow_90", category: "conduit_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "elbow", round_connector_dimension: "diameter" },
  connectors: pipeElbowBlueprint.connectors.map((connector) => pathwayConnector(connector, "conduit")),
};
const conduitElbowSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_elbow_90", confirmed_fields: {}, blueprint: conduitElbowBlueprint });
assert.equal(conduitElbowSpec.blueprint_assessment.buildable_by_api, false);
assert.ok(conduitElbowSpec.blueprint_assessment.unsupported_features.includes("parameterized_sweep_profile_family_editor_ui"));
assert.ok(conduitElbowSpec.blueprint_assessment.supported_features.includes("normalized_conduit_and_cable_tray_fitting_behaviors"));
const conduitTeeBlueprint = {
  ...pipeTeeBlueprint,
  family: { family_key: "conduit_tee", category: "conduit_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "tee", round_connector_dimension: "diameter" },
  connectors: pipeTeeBlueprint.connectors.map((connector) => pathwayConnector(connector, "conduit")),
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_tee", confirmed_fields: {}, blueprint: conduitTeeBlueprint }).blueprint_assessment.buildable_by_api, true);
const conduitTransitionBlueprint = {
  ...blueprint,
  required_source_fields: [],
  family: { family_key: "conduit_transition", category: "conduit_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "transition", round_connector_dimension: "diameter" },
  parameters: [
    { key: "inlet_diameter", name: "Inlet Diameter", data_type: "length", scope: "type", default: 50 },
    { key: "outlet_diameter", name: "Outlet Diameter", data_type: "length", scope: "type", default: 32 },
  ],
  parameter_order: ["inlet_diameter", "outlet_diameter"],
  types: [{ name: "50-32", values: { inlet_diameter: 50, outlet_diameter: 32 } }],
  parts: [{ key: "body", primitive: "blend", operation: "solid", axis: "x", role: "ConduitTransitionBody", profile: { shape: "circle", diameter_parameter: "inlet_diameter" }, end_profile: { shape: "circle", diameter_parameter: "outlet_diameter" }, start_mm: 0, end_mm: 160, visibility }],
  connectors: [
    { key: "inlet", discipline: "conduit", role: "Conduit Inlet", host_part: "body", host_face: "start", profile: "round", diameter_parameter: "inlet_diameter", primary: true, linked_to: "outlet" },
    { key: "outlet", discipline: "conduit", role: "Conduit Outlet", host_part: "body", host_face: "end", profile: "round", diameter_parameter: "outlet_diameter", linked_to: "inlet" },
  ],
  verification: { parameter_flex_cases: [
    { parameter_key: "inlet_diameter", min: 25, nominal: 50, max: 80 },
    { parameter_key: "outlet_diameter", min: 20, nominal: 32, max: 50 },
  ] },
};
const conduitTransitionSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_transition", confirmed_fields: {}, blueprint: conduitTransitionBlueprint });
assert.equal(conduitTransitionSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(conduitTransitionSpec.blueprint_assessment.supported_features.includes("parameterized_pathway_transition_union_geometry"));
const conduitUnionBlueprint = {
  ...conduitTransitionBlueprint,
  family: { ...conduitTransitionBlueprint.family, family_key: "conduit_union", part_type: "union" },
  parameters: [
    { key: "diameter", name: "Diameter", data_type: "length", scope: "type", default: 50 },
    { key: "length", name: "Length", data_type: "length", scope: "type", default: 80 },
  ],
  parameter_order: ["diameter", "length"],
  types: [{ name: "DN50", values: { diameter: 50, length: 80 } }],
  parts: [{ key: "body", primitive: "extrusion", operation: "solid", axis: "x", role: "ConduitUnionBody", profile: { shape: "circle", diameter_parameter: "diameter" }, start_mm: 0, end_mm: 80, depth_parameter: "length", visibility }],
  connectors: conduitTransitionBlueprint.connectors.map((connector) => ({ ...connector, diameter_parameter: "diameter" })),
  verification: { parameter_flex_cases: [
    { parameter_key: "diameter", min: 25, nominal: 50, max: 80 },
    { parameter_key: "length", min: 50, nominal: 80, max: 120 },
  ] },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_union", confirmed_fields: {}, blueprint: conduitUnionBlueprint }).blueprint_assessment.buildable_by_api, true);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_offset", confirmed_fields: {}, blueprint: { ...conduitUnionBlueprint, family: { ...conduitUnionBlueprint.family, family_key: "conduit_offset", part_type: "offset" } } }), /part_type offset is not supported for category conduit_fitting/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_transition", confirmed_fields: {}, blueprint: { ...conduitTransitionBlueprint, parts: conduitTransitionBlueprint.parts.map((part) => ({ ...part, primitive: "extrusion", end_profile: undefined })) } }), /requires one solid X-axis blend body/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_transition", confirmed_fields: {}, blueprint: { ...conduitTransitionBlueprint, parts: conduitTransitionBlueprint.parts.map((part) => ({ ...part, end_profile: { ...part.profile } })), connectors: conduitTransitionBlueprint.connectors.map((connector) => connector.key === "outlet" ? { ...connector, diameter_parameter: "inlet_diameter" } : connector) } }), /must change at least one size-parameter binding/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_transition", confirmed_fields: {}, blueprint: { ...conduitTransitionBlueprint, connectors: conduitTransitionBlueprint.connectors.map((connector) => connector.key === "inlet" ? { ...connector, primary: false } : connector) } }), /requires exactly one primary connector/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_transition", confirmed_fields: {}, blueprint: { ...conduitTransitionBlueprint, connectors: conduitTransitionBlueprint.connectors.map((connector) => connector.key === "outlet" ? { ...connector, linked_to: undefined } : connector) } }), /linked reciprocally/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "conduit_union", confirmed_fields: {}, blueprint: { ...conduitUnionBlueprint, parts: conduitUnionBlueprint.parts.map((part) => ({ ...part, primitive: "blend", end_profile: { ...part.profile }, depth_parameter: undefined })) } }), /requires one solid X-axis extrusion body/);
const diagonal = Math.SQRT1_2;
const pipeWyeBlueprint = {
  ...pipeTeeBlueprint,
  family: { ...pipeTeeBlueprint.family, family_key: "pipe_wye", part_type: "wye" },
  parts: pipeTeeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis: undefined, axis_direction: { x: diagonal, y: diagonal, z: 0 }, role: "WyeBranch" } : part),
};
const pipeWyeSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_wye", confirmed_fields: {}, blueprint: pipeWyeBlueprint });
assert.equal(pipeWyeSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(pipeWyeSpec.blueprint_assessment.supported_features.includes("arbitrary_direction_extrusion_frame_and_connector_faces"));
assert.ok(pipeWyeSpec.blueprint_assessment.supported_features.includes("parameterized_angled_wye_lateral_cross_geometry"));
assert.ok(Math.abs(pipeWyeSpec.blueprint.parts.find((part) => part.key === "branch").axis_direction.x - diagonal) < 1e-6);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_wye", confirmed_fields: {}, blueprint: { ...pipeWyeBlueprint, parts: pipeWyeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis: "y" } : part) } }), /cannot declare both axis and axis_direction/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_wye", confirmed_fields: {}, blueprint: { ...pipeWyeBlueprint, parts: pipeWyeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis_direction: { x: 0, y: 0, z: 0 } } : part) } }), /non-zero direction vector/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_wye", confirmed_fields: {}, blueprint: { ...pipeWyeBlueprint, parts: pipeWyeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis_direction: { x: 1, y: 0, z: 0 } } : part) } }), /must lie in the XY or XZ plane|oblique 15-75 degree angle/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_wye", confirmed_fields: {}, blueprint: { ...pipeWyeBlueprint, parts: pipeWyeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis_direction: { x: 1, y: 1, z: 1 } } : part) } }), /must lie in the XY or XZ plane/);
const ductWyeBlueprint = {
  ...pipeWyeBlueprint,
  family: { family_key: "duct_wye", category: "duct_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "lateral_tee" },
  parameters: [
    { key: "run_width", name: "Run Width", data_type: "length", scope: "type", default: 400 },
    { key: "run_height", name: "Run Height", data_type: "length", scope: "type", default: 250 },
    { key: "branch_width", name: "Branch Width", data_type: "length", scope: "type", default: 250 },
    { key: "branch_height", name: "Branch Height", data_type: "length", scope: "type", default: 200 },
    { key: "run_length", name: "Run Length", data_type: "length", scope: "type", default: 800 },
    { key: "branch_length", name: "Branch Length", data_type: "length", scope: "type", default: 500 },
  ],
  parameter_order: ["run_width", "run_height", "branch_width", "branch_height", "run_length", "branch_length"],
  types: [{ name: "400x250-250x200", values: { run_width: 400, run_height: 250, branch_width: 250, branch_height: 200, run_length: 800, branch_length: 500 } }],
  parts: [
    { key: "run", primitive: "extrusion", operation: "solid", axis: "x", role: "DuctWyeRun", profile: { shape: "rectangle", width_parameter: "run_width", height_parameter: "run_height" }, start_mm: -400, end_mm: 400, depth_parameter: "run_length", join_with: ["branch"], visibility },
    { key: "branch", primitive: "extrusion", operation: "solid", axis_direction: { x: diagonal, y: 0, z: diagonal }, role: "DuctWyeBranch", profile: { shape: "rectangle", width_parameter: "branch_width", height_parameter: "branch_height" }, start_mm: 0, end_mm: 500, depth_parameter: "branch_length", visibility },
  ],
  connectors: [
    { key: "run_inlet", discipline: "duct", role: "Run Inlet", host_part: "run", host_face: "start", system_classification: "Fitting", profile: "rectangular", width_parameter: "run_width", height_parameter: "run_height", primary: true },
    { key: "run_outlet", discipline: "duct", role: "Run Outlet", host_part: "run", host_face: "end", system_classification: "Fitting", profile: "rectangular", width_parameter: "run_width", height_parameter: "run_height" },
    { key: "branch_outlet", discipline: "duct", role: "Branch Outlet", host_part: "branch", host_face: "end", system_classification: "Fitting", profile: "rectangular", width_parameter: "branch_width", height_parameter: "branch_height" },
  ],
  verification: { parameter_flex_cases: [
    { parameter_key: "run_width", min: 300, nominal: 400, max: 600 }, { parameter_key: "run_height", min: 200, nominal: 250, max: 350 },
    { parameter_key: "branch_width", min: 150, nominal: 250, max: 350 }, { parameter_key: "branch_height", min: 100, nominal: 200, max: 300 },
    { parameter_key: "run_length", min: 600, nominal: 800, max: 1000 }, { parameter_key: "branch_length", min: 350, nominal: 500, max: 700 },
  ] },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "duct_wye", confirmed_fields: {}, blueprint: ductWyeBlueprint }).blueprint_assessment.buildable_by_api, true);
const channelCableTrayElbowBlueprint = {
  ...ductElbowBlueprint,
  family: { family_key: "channel_cable_tray_elbow", category: "cable_tray_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "channel_cable_tray_elbow" },
  parts: ductElbowBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, plane: "xy" } })),
  connectors: ductElbowBlueprint.connectors.map((connector) => pathwayConnector(connector, "cable_tray")),
};
const channelCableTrayElbowSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_elbow", confirmed_fields: {}, blueprint: channelCableTrayElbowBlueprint });
assert.equal(channelCableTrayElbowSpec.blueprint_assessment.buildable_by_api, false);
assert.ok(channelCableTrayElbowSpec.blueprint_assessment.unsupported_features.includes("parameterized_sweep_profile_family_editor_ui"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_elbow", confirmed_fields: {}, blueprint: { ...channelCableTrayElbowBlueprint, parts: channelCableTrayElbowBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, plane: "xz" } })) } }), /requires an xy arc path/);
const verticalCableTrayElbowBlueprint = {
  ...channelCableTrayElbowBlueprint,
  family: { ...channelCableTrayElbowBlueprint.family, family_key: "ladder_cable_tray_vertical_elbow", part_type: "ladder_cable_tray_vertical_elbow" },
  parts: channelCableTrayElbowBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, plane: "xz" } })),
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "ladder_cable_tray_vertical_elbow", confirmed_fields: {}, blueprint: verticalCableTrayElbowBlueprint }).blueprint_assessment.buildable_by_api, false);
const ladderCableTrayTeeBlueprint = {
  ...ductWyeBlueprint,
  family: { family_key: "ladder_cable_tray_tee", category: "cable_tray_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "ladder_cable_tray_tee" },
  parts: ductWyeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis: "y", axis_direction: undefined, role: "CableTrayTeeBranch" } : { ...part, role: "CableTrayTeeRun" }),
  connectors: ductWyeBlueprint.connectors.map((connector) => pathwayConnector(connector, "cable_tray")),
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "ladder_cable_tray_tee", confirmed_fields: {}, blueprint: ladderCableTrayTeeBlueprint }).blueprint_assessment.buildable_by_api, true);
const channelCableTrayCrossBlueprint = {
  ...ladderCableTrayTeeBlueprint,
  family: { ...ladderCableTrayTeeBlueprint.family, family_key: "channel_cable_tray_cross", part_type: "channel_cable_tray_cross" },
  parameters: ladderCableTrayTeeBlueprint.parameters.map((parameter) => parameter.key === "branch_length" ? { ...parameter, default: 1000 } : parameter),
  types: [{ name: "400x250-250x200", values: { run_width: 400, run_height: 250, branch_width: 250, branch_height: 200, run_length: 800, branch_length: 1000 } }],
  parts: ladderCableTrayTeeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, start_mm: -500, end_mm: 500 } : part),
  connectors: [...ladderCableTrayTeeBlueprint.connectors.slice(0, 2),
    { ...ladderCableTrayTeeBlueprint.connectors[2], key: "branch_inlet", role: "Branch Inlet", host_face: "start" },
    { ...ladderCableTrayTeeBlueprint.connectors[2], key: "branch_outlet", role: "Branch Outlet", host_face: "end" }],
  verification: { parameter_flex_cases: ladderCableTrayTeeBlueprint.verification.parameter_flex_cases.map((item) => item.parameter_key === "branch_length" ? { ...item, min: 700, nominal: 1000, max: 1400 } : item) },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_cross", confirmed_fields: {}, blueprint: channelCableTrayCrossBlueprint }).blueprint_assessment.buildable_by_api, true);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_elbow", confirmed_fields: {}, blueprint: { ...channelCableTrayElbowBlueprint, connectors: channelCableTrayElbowBlueprint.connectors.map((connector) => ({ ...connector, profile: "round", diameter_parameter: "duct_width", width_parameter: undefined, height_parameter: undefined })) } }), /profile round is not physically valid for cable_tray/);
const cableTrayTransitionBlueprint = {
  ...blueprint,
  required_source_fields: [],
  family: { family_key: "channel_cable_tray_transition", category: "cable_tray_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "channel_cable_tray_transition" },
  parameters: [
    { key: "inlet_width", name: "Inlet Width", data_type: "length", scope: "type", default: 400 },
    { key: "inlet_height", name: "Inlet Height", data_type: "length", scope: "type", default: 100 },
    { key: "outlet_width", name: "Outlet Width", data_type: "length", scope: "type", default: 250 },
    { key: "outlet_height", name: "Outlet Height", data_type: "length", scope: "type", default: 75 },
  ],
  parameter_order: ["inlet_width", "inlet_height", "outlet_width", "outlet_height"],
  types: [{ name: "400x100-250x75", values: { inlet_width: 400, inlet_height: 100, outlet_width: 250, outlet_height: 75 } }],
  parts: [{ key: "body", primitive: "blend", operation: "solid", axis: "x", role: "CableTrayTransitionBody", profile: { shape: "rectangle", width_parameter: "inlet_width", height_parameter: "inlet_height" }, end_profile: { shape: "rectangle", width_parameter: "outlet_width", height_parameter: "outlet_height" }, start_mm: 0, end_mm: 300, visibility }],
  connectors: [
    { key: "inlet", discipline: "cable_tray", role: "Cable Tray Inlet", host_part: "body", host_face: "start", profile: "rectangular", width_parameter: "inlet_width", height_parameter: "inlet_height", primary: true, linked_to: "outlet" },
    { key: "outlet", discipline: "cable_tray", role: "Cable Tray Outlet", host_part: "body", host_face: "end", profile: "rectangular", width_parameter: "outlet_width", height_parameter: "outlet_height", linked_to: "inlet" },
  ],
  verification: { parameter_flex_cases: [
    { parameter_key: "inlet_width", min: 200, nominal: 400, max: 600 }, { parameter_key: "inlet_height", min: 50, nominal: 100, max: 150 },
    { parameter_key: "outlet_width", min: 150, nominal: 250, max: 400 }, { parameter_key: "outlet_height", min: 50, nominal: 75, max: 125 },
  ] },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_transition", confirmed_fields: {}, blueprint: cableTrayTransitionBlueprint }).blueprint_assessment.buildable_by_api, true);
const cableTrayUnionBlueprint = {
  ...cableTrayTransitionBlueprint,
  family: { ...cableTrayTransitionBlueprint.family, family_key: "ladder_cable_tray_union", part_type: "ladder_cable_tray_union" },
  parameters: [
    { key: "width", name: "Width", data_type: "length", scope: "type", default: 400 },
    { key: "height", name: "Height", data_type: "length", scope: "type", default: 100 },
    { key: "length", name: "Length", data_type: "length", scope: "type", default: 120 },
  ],
  parameter_order: ["width", "height", "length"],
  types: [{ name: "400x100", values: { width: 400, height: 100, length: 120 } }],
  parts: [{ key: "body", primitive: "extrusion", operation: "solid", axis: "x", role: "CableTrayUnionBody", profile: { shape: "rectangle", width_parameter: "width", height_parameter: "height" }, start_mm: 0, end_mm: 120, depth_parameter: "length", visibility }],
  connectors: cableTrayTransitionBlueprint.connectors.map((connector) => ({ ...connector, width_parameter: "width", height_parameter: "height" })),
  verification: { parameter_flex_cases: [
    { parameter_key: "width", min: 200, nominal: 400, max: 600 }, { parameter_key: "height", min: 50, nominal: 100, max: 150 },
    { parameter_key: "length", min: 80, nominal: 120, max: 180 },
  ] },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "ladder_cable_tray_union", confirmed_fields: {}, blueprint: cableTrayUnionBlueprint }).blueprint_assessment.buildable_by_api, true);
const cableTrayOffsetBlueprint = {
  ...cableTrayUnionBlueprint,
  family: { ...cableTrayUnionBlueprint.family, family_key: "channel_cable_tray_offset", part_type: "channel_cable_tray_offset" },
  parameters: [
    ...cableTrayUnionBlueprint.parameters.filter((parameter) => parameter.key !== "length"),
    { key: "lead_in", name: "Lead In", data_type: "length", scope: "type", default: 200 },
    { key: "lead_out", name: "Lead Out", data_type: "length", scope: "type", default: 200 },
    { key: "lateral_offset", name: "Lateral Offset", data_type: "length", scope: "type", default: 300 },
    { key: "offset_angle", name: "Offset Angle", data_type: "angle", scope: "type", default: 45 },
  ],
  parameter_order: ["width", "height", "lead_in", "lead_out", "lateral_offset", "offset_angle"],
  types: [{ name: "400x100-O300", values: { width: 400, height: 100, lead_in: 200, lead_out: 200, lateral_offset: 300, offset_angle: 45 } }],
  parts: [{ key: "body", primitive: "sweep", operation: "solid", axis: "x", role: "CableTrayOffsetBody", profile: { shape: "rectangle", width_parameter: "width", height_parameter: "height" }, path: { kind: "offset", plane: "xy", lead_in_parameter: "lead_in", lead_out_parameter: "lead_out", lateral_offset_parameter: "lateral_offset", offset_angle_parameter: "offset_angle", offset_direction: "positive" }, profile_location: "start", visibility }],
  connectors: [
    { key: "inlet", discipline: "cable_tray", role: "Cable Tray Offset Inlet", host_part: "body", host_face: "path_start", profile: "rectangular", width_parameter: "width", height_parameter: "height", primary: true, linked_to: "outlet" },
    { key: "outlet", discipline: "cable_tray", role: "Cable Tray Offset Outlet", host_part: "body", host_face: "path_end", profile: "rectangular", width_parameter: "width", height_parameter: "height", linked_to: "inlet" },
  ],
  verification: { parameter_flex_cases: [
    ...cableTrayUnionBlueprint.verification.parameter_flex_cases.filter((item) => item.parameter_key !== "length"),
    { parameter_key: "lead_in", min: 100, nominal: 200, max: 350 }, { parameter_key: "lead_out", min: 100, nominal: 200, max: 350 },
    { parameter_key: "lateral_offset", min: 150, nominal: 300, max: 600 }, { parameter_key: "offset_angle", min: 30, nominal: 45, max: 60 },
  ] },
};
const cableTrayOffsetSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: cableTrayOffsetBlueprint });
assert.equal(cableTrayOffsetSpec.blueprint_assessment.buildable_by_api, false);
assert.ok(cableTrayOffsetSpec.blueprint_assessment.supported_features.includes("constraint_driven_parameterized_pathway_offset"));
const verticalCableTrayOffsetBlueprint = { ...cableTrayOffsetBlueprint,
  family: { ...cableTrayOffsetBlueprint.family, family_key: "ladder_cable_tray_offset", part_type: "ladder_cable_tray_offset" },
  parts: cableTrayOffsetBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, plane: "xz", offset_direction: "negative" } })),
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "ladder_cable_tray_offset", confirmed_fields: {}, blueprint: verticalCableTrayOffsetBlueprint }).blueprint_assessment.buildable_by_api, false);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: { ...cableTrayOffsetBlueprint, parts: cableTrayOffsetBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, lead_out_parameter: "lead_in" } })) } }), /four distinct/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: { ...cableTrayOffsetBlueprint, parameters: cableTrayOffsetBlueprint.parameters.map((parameter) => parameter.key === "offset_angle" ? { ...parameter, scope: "instance" } : parameter) } }), /direct Angle Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: { ...cableTrayOffsetBlueprint, types: cableTrayOffsetBlueprint.types.map((type) => ({ ...type, values: { ...type.values, offset_angle: 80 } })) } }), /between 15 and 75 degrees/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: { ...cableTrayOffsetBlueprint, verification: { parameter_flex_cases: cableTrayOffsetBlueprint.verification.parameter_flex_cases.map((item) => item.parameter_key === "offset_angle" ? { ...item, min: 10 } : item) } } }), /offset-angle flex range must stay between 15 and 75 degrees/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: { ...cableTrayOffsetBlueprint, types: cableTrayOffsetBlueprint.types.map((type) => ({ ...type, values: { ...type.values, lead_in: 250 } })) } }), /must equal its approved nominal flex value/);
const legacyCableTrayOffsetBlueprint = {
  ...cableTrayOffsetBlueprint,
  parameters: cableTrayOffsetBlueprint.parameters.filter((parameter) => ["width", "height"].includes(parameter.key)),
  parameter_order: ["width", "height"],
  types: [{ name: "400x100-Legacy", values: { width: 400, height: 100 } }],
  parts: cableTrayOffsetBlueprint.parts.map((part) => ({ ...part, path: { kind: "polyline", plane: "xy", points_mm: [point(0, 0, 0), point(200, 0, 0), point(500, 300, 0), point(700, 300, 0)] } })),
  verification: { parameter_flex_cases: cableTrayOffsetBlueprint.verification.parameter_flex_cases.filter((item) => ["width", "height"].includes(item.parameter_key)) },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: legacyCableTrayOffsetBlueprint }).blueprint_assessment.buildable_by_api, false);
const offsetWithPoints = (points_mm, plane = "xy") => ({ ...legacyCableTrayOffsetBlueprint, parts: legacyCableTrayOffsetBlueprint.parts.map((part) => ({ ...part, path: { ...part.path, plane, points_mm } })) });
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: offsetWithPoints([point(0, 0, 0), point(200, 0, 0), point(700, 300, 0)]) }), /four-point polyline path/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: offsetWithPoints([point(0, 0, 0), point(-200, 0, 0), point(500, 300, 0), point(700, 300, 0)]) }), /first and last path segments must have \+X tangents/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: offsetWithPoints([point(0, 0, 0), point(200, 0, 0), point(500, 0, 0), point(700, 0, 0)]) }), /non-zero lateral displacement/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: offsetWithPoints([point(0, 0, 0), point(200, 0, 0), point(500, 300, 0), point(700, 300, 0)], "yz") }), /must all lie in the declared yz plane|must have \+X tangents|path plane must be xy or xz/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: offsetWithPoints([point(0, 0, 0), point(200, 0, 0), point(210, 300, 0), point(410, 300, 0)]) }), /forward 15-75 degree angle/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: { ...cableTrayOffsetBlueprint, parameters: cableTrayOffsetBlueprint.parameters.map((parameter) => parameter.key === "width" ? { ...parameter, scope: "instance" } : parameter) } }), /must reference a direct Length Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "channel_cable_tray_offset", confirmed_fields: {}, blueprint: { ...cableTrayOffsetBlueprint, connectors: cableTrayOffsetBlueprint.connectors.map((connector) => connector.key === "outlet" ? { ...connector, width_parameter: "height" } : connector) } }), /rectangular connector size must match/);
const pipeCrossBlueprint = {
  ...pipeTeeBlueprint,
  family: { ...pipeTeeBlueprint.family, family_key: "pipe_cross", part_type: "cross" },
  parameters: pipeTeeBlueprint.parameters.map((parameter) => parameter.key === "branch_length" ? { ...parameter, default: 180 } : parameter),
  types: [{ name: "DN50x32", values: { run_diameter: 50, branch_diameter: 32, run_length: 160, branch_length: 180 } }],
  parts: pipeTeeBlueprint.parts.map((part) => part.key === "branch" ? { ...part, start_mm: -90, end_mm: 90 } : part),
  connectors: [...pipeTeeBlueprint.connectors.slice(0, 2),
    { ...pipeTeeBlueprint.connectors[2], key: "branch_inlet", role: "Branch Inlet", host_face: "start" },
    { ...pipeTeeBlueprint.connectors[2], key: "branch_outlet", role: "Branch Outlet", host_face: "end" }],
  verification: { parameter_flex_cases: pipeTeeBlueprint.verification.parameter_flex_cases.map((item) => item.parameter_key === "branch_length" ? { ...item, min: 120, nominal: 180, max: 300 } : item) },
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_cross", confirmed_fields: {}, blueprint: pipeCrossBlueprint }).blueprint_assessment.buildable_by_api, true);
const pipeLateralCrossBlueprint = {
  ...pipeCrossBlueprint,
  family: { ...pipeCrossBlueprint.family, family_key: "pipe_lateral_cross", part_type: "lateral_cross" },
  parts: pipeCrossBlueprint.parts.map((part) => part.key === "branch" ? { ...part, axis: undefined, axis_direction: { x: diagonal, y: diagonal, z: 0 }, role: "LateralCrossBranch" } : part),
};
assert.equal(previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_lateral_cross", confirmed_fields: {}, blueprint: pipeLateralCrossBlueprint }).blueprint_assessment.buildable_by_api, true);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pipe_lateral_cross", confirmed_fields: {}, blueprint: { ...pipeLateralCrossBlueprint, parts: pipeLateralCrossBlueprint.parts.map((part) => part.key === "branch" ? { ...part, start_mm: 0, end_mm: 180 } : part) } }), /must be centered on the Family origin/);
const parameterizedBlendBlueprint = {
  ...blueprint,
  required_source_fields: [],
  parameters: [
    { key: "inlet_width", name: "Inlet Width", data_type: "length", scope: "type", default: 600 },
    { key: "inlet_height", name: "Inlet Height", data_type: "length", scope: "type", default: 400 },
    { key: "outlet_diameter", name: "Outlet Diameter", data_type: "length", scope: "type", default: 300 },
  ],
  types: [{ name: "Transition", values: { inlet_width: 600, inlet_height: 400, outlet_diameter: 300 } }],
  parts: [{ key: "transition", primitive: "blend", operation: "solid", axis: "x", role: "TransitionBody", profile: { shape: "rectangle", width_parameter: "inlet_width", height_parameter: "inlet_height" }, end_profile: { shape: "circle", diameter_parameter: "outlet_diameter" }, start_mm: 0, end_mm: 500, visibility }],
  verification: { parameter_flex_cases: [
    { parameter_key: "inlet_width", min: 300, nominal: 600, max: 900 },
    { parameter_key: "inlet_height", min: 200, nominal: 400, max: 600 },
    { parameter_key: "outlet_diameter", min: 150, nominal: 300, max: 450 },
  ] },
};
const parameterizedBlendSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: parameterizedBlendBlueprint });
assert.equal(parameterizedBlendSpec.status, "human_confirmed");
assert.equal(parameterizedBlendSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(parameterizedBlendSpec.blueprint_assessment.supported_features.includes("parameterized_blend_rectangle_circle_profiles"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: {
  ...parameterizedBlendBlueprint,
  parameters: parameterizedBlendBlueprint.parameters.map((parameter) => parameter.key === "inlet_width" ? { ...parameter, scope: "instance" } : parameter),
} }), /parameterized blend profile inlet_width must reference a direct Length Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: {
  ...parameterizedBlendBlueprint,
  parameters: parameterizedBlendBlueprint.parameters.map((parameter) => parameter.key === "outlet_diameter" ? { key: parameter.key, name: parameter.name, data_type: parameter.data_type, scope: parameter.scope, formula: "inlet_width \/ 2" } : parameter),
  types: [{ name: "Transition", values: { inlet_width: 600, inlet_height: 400 } }],
  verification: { parameter_flex_cases: parameterizedBlendBlueprint.verification.parameter_flex_cases.filter((item) => item.parameter_key !== "outlet_diameter") },
} }), /parameterized blend profile outlet_diameter must reference a direct Length Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: {
  ...parameterizedBlendBlueprint,
  parameters: [
    ...parameterizedBlendBlueprint.parameters.filter((parameter) => parameter.key !== "outlet_diameter"),
    { key: "fallback_outlet_diameter", name: "Fallback Outlet Diameter", data_type: "length", scope: "type", default: 300 },
    { key: "outlet_diameter", name: "Outlet Diameter", data_type: "length", scope: "instance", lookup: { table_key: "transition_sizes", result_column_key: "outlet_diameter", default_parameter_key: "fallback_outlet_diameter", lookup_parameter_keys: ["inlet_width"] } },
  ],
  lookup_tables: [{ key: "transition_sizes", name: "Transition Sizes", columns: [{ key: "inlet_width", name: "Inlet Width", data_type: "length" }, { key: "outlet_diameter", name: "Outlet Diameter", data_type: "length" }], lookup_columns: ["inlet_width"], rows: [{ values: { inlet_width: 600, outlet_diameter: 300 } }] }],
  types: [{ name: "Transition", values: { inlet_width: 600, inlet_height: 400, fallback_outlet_diameter: 300 } }],
  verification: { parameter_flex_cases: parameterizedBlendBlueprint.verification.parameter_flex_cases.filter((item) => item.parameter_key !== "outlet_diameter") },
} }), /parameterized blend profile outlet_diameter must reference a direct Length Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: {
  ...parameterizedBlendBlueprint,
  verification: { parameter_flex_cases: parameterizedBlendBlueprint.verification.parameter_flex_cases.filter((item) => item.parameter_key !== "outlet_diameter") },
} }), /geometry\/connector parameter outlet_diameter requires verification\.parameter_flex_cases/);
const ductTransitionBlueprint = {
  ...parameterizedBlendBlueprint,
  family: { family_key: "custom_duct_transition", category: "duct_fitting", template_behavior: "level_based", primary_axis: "x", part_type: "transition" },
  connectors: [
    { key: "rectangular_inlet", discipline: "duct", role: "Inlet", host_part: "transition", host_face: "start", system_classification: "Fitting", profile: "rectangular", width_parameter: "inlet_width", height_parameter: "inlet_height", primary: true, linked_to: "round_outlet", flow_direction: "bidirectional", flow_configuration: "system" },
    { key: "round_outlet", discipline: "duct", role: "Outlet", host_part: "transition", host_face: "end", system_classification: "Fitting", profile: "round", diameter_parameter: "outlet_diameter", linked_to: "rectangular_inlet", flow_direction: "bidirectional", flow_configuration: "system" },
  ],
};
const ductTransitionSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_duct_transition", confirmed_fields: {}, blueprint: ductTransitionBlueprint });
assert.equal(ductTransitionSpec.status, "human_confirmed");
assert.equal(ductTransitionSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(ductTransitionSpec.blueprint_assessment.supported_features.includes("parameterized_fitting_transition_geometry"));
const parameterizedSweptBlend = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...parameterizedBlendBlueprint, parts: [{ ...primitiveCases[3], profile: parameterizedBlendBlueprint.parts[0].profile, end_profile: parameterizedBlendBlueprint.parts[0].end_profile }] } });
assert.equal(parameterizedSweptBlend.status, "human_confirmed");
assert.equal(parameterizedSweptBlend.blueprint_assessment.buildable_by_api, true);
assert.ok(parameterizedSweptBlend.blueprint_assessment.supported_features.includes("parameterized_straight_swept_blend_rectangle_circle_oval_profiles"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...parameterizedBlendBlueprint, parts: [{ ...primitiveCases[3], profile: parameterizedBlendBlueprint.parts[0].profile, end_profile: parameterizedBlendBlueprint.parts[0].end_profile, path: { kind: "line", plane: "xy", points_mm: [point(0, 0, 0), point(0, 500, 0)] } }] } }), /parameterized swept_blend requires its path to follow \+X/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...parameterizedBlendBlueprint, parameters: parameterizedBlendBlueprint.parameters.map((parameter) => parameter.key === "inlet_width" ? { ...parameter, scope: "instance" } : parameter), parts: [{ ...primitiveCases[3], profile: parameterizedBlendBlueprint.parts[0].profile, end_profile: parameterizedBlendBlueprint.parts[0].end_profile }] } }), /parameterized swept_blend profile inlet_width must reference a direct Length Type Parameter/);
const voidCut = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [blueprint.parts[0], { key: "opening", primitive: "extrusion", operation: "void", axis: "x", role: "Opening", profile: { shape: "circle", diameter_mm: 120 }, start_mm: -50, end_mm: 500, cut_targets: ["body"], visibility }] } });
assert.equal(voidCut.status, "human_confirmed");
assert.equal(voidCut.blueprint_assessment.buildable_by_api, true);
assert.ok(voidCut.blueprint_assessment.supported_features.includes("void_extrusion_revolution_sweep_blend_swept_blend_cut"));
for (const source of primitiveCases) {
  const voidPart = { ...source, key: `void_${source.primitive}`, operation: "void", cut_targets: ["body"] };
  const voidSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [blueprint.parts[0], voidPart] } });
  assert.equal(voidSpec.status, "human_confirmed", `void ${source.primitive} should be API-buildable with literal geometry`);
  assert.equal(voidSpec.blueprint_assessment.buildable_by_api, true);
}
const materialBlueprint = {
  ...blueprint,
  parameters: [...blueprint.parameters, { key: "finish", name: "Finish", data_type: "material", scope: "type", default: "powder_coat" }],
  types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450, finish: "powder_coat" } }],
  materials: [{ key: "powder_coat", name: "DSCons Powder Coat", color_rgb: { r: 68, g: 82, b: 92 }, transparency: 0 }],
  parts: [{ ...blueprint.parts[0], material_parameter: "finish" }],
};
const materialSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: materialBlueprint });
assert.equal(materialSpec.status, "human_confirmed");
assert.equal(materialSpec.blueprint_assessment.buildable_by_api, true);
const coordinationBlueprint = {
  ...blueprint,
  target_lod: "LOD_350",
  materials: [{ key: "coordination_transparent", name: "DSCons Coordination Transparent", color_rgb: { r: 255, g: 128, b: 0 }, transparency: 80 }],
  coordination_zones: [
    { key: "maintenance_zone", purpose: "maintenance_clearance", shape: "box", axis: "x", origin_mm: point(650, 0, 0), width_parameter: "width", height_parameter: "height", depth_parameter: "depth", material_key: "coordination_transparent", role: "non_physical_coordination_zone", subcategory: "DSCons Coordination Maintenance", visibility: { coarse: false, medium: true, fine: true, front_back: true, left_right: true, plan_rcp: true, only_when_cut: false } },
    { key: "removal_path_zone", purpose: "removal_path", shape: "cylinder", axis: "x", origin_mm: point(1200, 0, 0), diameter_mm: 500, length_mm: 1500, material_key: "coordination_transparent", role: "non_physical_coordination_zone", subcategory: "DSCons Coordination Removal Path", visibility: { coarse: false, medium: false, fine: true } },
  ],
};
const coordinationSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: coordinationBlueprint });
assert.equal(coordinationSpec.status, "human_confirmed");
assert.equal(coordinationSpec.lod, "LOD_350");
assert.equal(coordinationSpec.blueprint.complexity_assessment.metrics.forms, 3);
assert.equal(coordinationSpec.blueprint.complexity_assessment.metrics.complexity_score, 41);
assert.ok(coordinationSpec.blueprint_assessment.supported_features.includes("parameterized_non_physical_coordination_zones"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, target_lod: "LOD_300" } }), /coordination_zones requires target_lod=LOD_350/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, coordination_zones: [{ ...coordinationBlueprint.coordination_zones[0], material_key: "missing_coordination_material" }] } }), /references unknown material/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, materials: [{ ...coordinationBlueprint.materials[0], transparency: 50 }] } }), /transparency of at least 70/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, parameters: coordinationBlueprint.parameters.map((parameter) => parameter.key === "width" ? { ...parameter, scope: "instance" } : parameter) } }), /width_parameter must reference a direct Length Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, verification: { parameter_flex_cases: coordinationBlueprint.verification.parameter_flex_cases.filter((item) => item.parameter_key !== "width") } } }), /parameter width requires verification\.parameter_flex_cases|geometry\/connector parameter width requires/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, coordination_zones: [{ ...coordinationBlueprint.coordination_zones[0], key: "body" }] } }), /cannot reuse a physical part key/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, coordination_zones: [{ ...coordinationBlueprint.coordination_zones[0], role: "physical_equipment" }] } }), /role must be non_physical_coordination_zone/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, coordination_zones: [{ ...coordinationBlueprint.coordination_zones[0], subcategory: "DSCons Coordination Access" }] } }), /subcategory must be DSCons Coordination Maintenance/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, connectors: [{ key: "bad_zone_connector", discipline: "pipe", role: "Bad", host_part: "maintenance_zone", host_face: "end", system_classification: "Fitting", profile: "round", diameter_parameter: "width" }] } }), /unknown host_part maintenance_zone/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, parts: [{ ...coordinationBlueprint.parts[0], join_with: ["maintenance_zone"] }] } }), /invalid geometry-operation target maintenance_zone/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, arrays: [{ key: "bad_zone_array", member_part_keys: ["maintenance_zone"], view_plane: "xy", count: 2, direction_mm: point(0, 500, 0), anchor: "second" }] } }), /unknown part maintenance_zone/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...coordinationBlueprint, mirrors: [{ key: "bad_zone_mirror", target_kind: "part", target_keys: ["maintenance_zone"], plane_origin_mm: point(0, 0, 0), plane_normal: point(0, 1, 0), copy: true }] } }), /unknown part maintenance_zone/);
const advancedMaterialBlueprint = {
  ...materialBlueprint,
  materials: [{ ...materialBlueprint.materials[0], use_render_appearance_for_shading: true, surface_foreground_pattern: { name: "Solid fill", target: "drafting", color_rgb: { r: 70, g: 80, b: 90 } }, cut_foreground_pattern: { name: "Solid fill", target: "drafting", color_rgb: { r: 30, g: 40, b: 50 } }, appearance: { color_rgb: { r: 68, g: 82, b: 92 }, transparency: 0.1, glossiness: 0.35, is_metal: true, texture: { file_name: textureFileName, sha256: textureSha256 }, bump: { file_name: textureFileName, sha256: textureSha256, amount: 0.35 } }, physical_asset: { name: "DSCons Powder Coat Physical", asset_class: "metal", behavior: "isotropic", density_kg_per_m3: 7850, young_modulus_mpa: 200000, shear_modulus_mpa: 76923, poisson_ratio: 0.3 }, thermal_asset: { name: "DSCons Powder Coat Thermal", material_type: "solid", density_kg_per_m3: 7850, thermal_conductivity_w_per_mk: 50.2, specific_heat_j_per_kgk: 470, emissivity: 0.8, porosity: 0, reflectivity: 0.2, transmits_light: false } }],
};
const advancedMaterialSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: advancedMaterialBlueprint, approved_demo_directory: approved });
assert.equal(advancedMaterialSpec.status, "human_confirmed");
assert.ok(advancedMaterialSpec.blueprint_assessment.supported_features.includes("family_material_graphics_appearance_and_form_assignment"));
assert.ok(advancedMaterialSpec.blueprint_assessment.supported_features.includes("family_material_physical_and_thermal_assets"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...materialBlueprint, materials: [{ ...materialBlueprint.materials[0], use_render_appearance_for_shading: true }] } }), /must declare appearance so its rendered shading colour is deterministic/);
assert.deepEqual(advancedMaterialSpec.citations.filter((item) => typeof item.source_kind === "string" && item.source_kind.startsWith("appearance_")), [
  { source_kind: "appearance_texture", material_key: "powder_coat", material_name: "DSCons Powder Coat", file_name: textureFileName, sha256: textureSha256, size_bytes: textureBytes.length, image_format: "png", path_policy: "direct_child_of_approved_demo_directory" },
  { source_kind: "appearance_bump", material_key: "powder_coat", material_name: "DSCons Powder Coat", file_name: textureFileName, sha256: textureSha256, size_bytes: textureBytes.length, image_format: "png", path_policy: "direct_child_of_approved_demo_directory" },
]);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...advancedMaterialBlueprint, materials: [{ ...advancedMaterialBlueprint.materials[0], use_render_appearance_for_shading: false }] }, approved_demo_directory: approved }), /must set use_render_appearance_for_shading=true/);
const invalidTextureHashBlueprint = { ...advancedMaterialBlueprint, materials: [{ ...advancedMaterialBlueprint.materials[0], appearance: { ...advancedMaterialBlueprint.materials[0].appearance, texture: { file_name: textureFileName, sha256: "0".repeat(64) } } }] };
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: invalidTextureHashBlueprint, approved_demo_directory: approved }), /checksum does not match/);
const missingTextureBlueprint = { ...advancedMaterialBlueprint, materials: [{ ...advancedMaterialBlueprint.materials[0], appearance: { ...advancedMaterialBlueprint.materials[0].appearance, texture: { file_name: "DSCons-Powder-Coat.jpg", sha256: textureSha256 } } }] };
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: missingTextureBlueprint, approved_demo_directory: approved }), /does not exist or cannot be read/);
const catalogParameters = [
  { key: "coefficient", name: "Coefficient", data_type: "number", scope: "type" },
  { key: "enabled", name: "Enabled", data_type: "yesno", scope: "type" },
  { key: "designation", name: "Designation", data_type: "text", scope: "type" },
  { key: "finish", name: "Finish", data_type: "material", scope: "type" },
  { key: "unit_cost", name: "Unit Cost", data_type: "currency", scope: "type" },
];
const catalogKeys = ["width", "height", "depth", ...catalogParameters.map((parameter) => parameter.key)];
const catalogBlueprint = {
  ...blueprint,
  parameters: [...blueprint.parameters, ...catalogParameters],
  parameter_order: ["depth", "height", "width", ...catalogParameters.map((parameter) => parameter.key)],
  materials: [{ key: "powder_coat", name: "DSCons Powder Coat", color_rgb: { r: 68, g: 82, b: 92 }, transparency: 0 }],
  types: [
    { name: "MEP-01", values: { width: 600, height: 900, depth: 450, coefficient: 0.25, enabled: true, designation: "Standard", finish: "powder_coat", unit_cost: 1100 } },
    { name: "MEP-02", values: { width: 900, height: 1200, depth: 600, coefficient: 0.45, enabled: false, designation: "Heavy, Duty", finish: "powder_coat", unit_cost: 2100 } },
  ],
  publication: { compact_rfa: true, preview_view: "three_dimensional", type_catalog: { parameter_keys: catalogKeys } },
};
const catalogSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, type_confirmed_fields: {
  "MEP-01": { width_mm: { value: 600, provenance: { ...sourceLocator } }, height_mm: { value: 900, provenance: { ...sourceLocator } }, depth_mm: { value: 450, provenance: { ...sourceLocator } } },
  "MEP-02": { width_mm: { value: 900, provenance: { ...sourceLocator } }, height_mm: { value: 1200, provenance: { ...sourceLocator } }, depth_mm: { value: 600, provenance: { ...sourceLocator } } },
}, blueprint: catalogBlueprint });
assert.equal(catalogSpec.status, "human_confirmed");
assert.deepEqual(catalogSpec.blueprint.parameter_order, catalogBlueprint.parameter_order);
assert.deepEqual(catalogSpec.blueprint.publication, catalogBlueprint.publication);
assert.ok(catalogSpec.blueprint_assessment.supported_features.includes("mep_parameter_data_types_and_parameter_order"));
assert.ok(catalogSpec.blueprint_assessment.supported_features.includes("type_catalog_sidecar"));
assert.ok(catalogSpec.blueprint_assessment.supported_features.includes("compact_save_and_preview_view"));
const sharedParameterBlueprint = { ...blueprint, parameters: blueprint.parameters.map((parameter, index) => index === 0 ? { ...parameter, shared_guid: "13b9205f-c57a-43a5-9b8d-ccf254da3811", group: "geometry", description: "Overall catalog width", visible: true, user_modifiable: true, hide_when_no_value: false } : parameter) };
const sharedParameterSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: sharedParameterBlueprint });
assert.equal(sharedParameterSpec.status, "human_confirmed");
assert.equal(sharedParameterSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(sharedParameterSpec.blueprint_assessment.supported_features.includes("parameters_with_group_description_and_shared_definition_metadata"));
const fittingBlueprint = { ...blueprint, family: { family_key: "custom_break_in", category: "pipe_accessory", template_behavior: "level_based", primary_axis: "x", part_type: "breaks_into", shared: false, always_vertical: false, round_connector_dimension: "diameter" }, parts: [{ ...blueprint.parts[0], profile: { shape: "circle", diameter_parameter: "width" }, start_mm: -225, end_mm: 225, depth_parameter: "depth" }], connectors: [{ key: "inlet", discipline: "pipe", role: "Inlet", host_part: "body", host_face: "start", system_classification: "Fitting", profile: "round", diameter_parameter: "width", primary: true, linked_to: "outlet", flow_direction: "bidirectional", flow_configuration: "system", loss_method: "coefficient", loss_coefficient: 0.25, joint_type: "flanged", gender: "undefined", engagement_length_mm: 20 }, { key: "outlet", discipline: "pipe", role: "Outlet", host_part: "body", host_face: "end", system_classification: "Fitting", profile: "round", diameter_parameter: "width", linked_to: "inlet", flow_direction: "bidirectional", flow_configuration: "system", joint_type: "flanged", gender: "undefined", engagement_length_mm: 20 }] };
const fittingSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_break_in", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: fittingBlueprint });
assert.equal(fittingSpec.status, "human_confirmed");
assert.ok(fittingSpec.blueprint_assessment.supported_features.includes("family_part_type_and_behavior_settings"));
assert.ok(fittingSpec.blueprint_assessment.supported_features.includes("connector_flow_loss_joint_and_engagement_metadata"));
assert.ok(fittingSpec.blueprint_assessment.supported_features.includes("bounded_two_port_break_into_accessory_routing_contract"));
const advancedPipeConnectorBlueprint = {
  ...fittingBlueprint,
  family: { ...fittingBlueprint.family, family_key: "global_pipe_break_in" },
  parameters: [...fittingBlueprint.parameters, { key: "flow_factor", name: "Flow Factor", data_type: "number", scope: "type", default: 0.75, group: "plumbing" }],
  parameter_order: [...fittingBlueprint.parameters.map((parameter) => parameter.key), "flow_factor"],
  types: fittingBlueprint.types.map((type) => ({ ...type, values: { ...type.values, flow_factor: 0.75 } })),
  connectors: fittingBlueprint.connectors.map((connector) => ({ ...connector, system_classification: "Global", flow_factor_parameter: "flow_factor", ...(connector.key === "inlet" ? { allow_slope_adjustments: true } : {}) })),
  verification: { parameter_flex_cases: [...fittingBlueprint.verification.parameter_flex_cases, { parameter_key: "flow_factor", min: 0.5, nominal: 0.75, max: 1.0 }] },
};
const advancedPipeConnectorSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "global_pipe_break_in", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: advancedPipeConnectorBlueprint });
assert.equal(advancedPipeConnectorSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(advancedPipeConnectorSpec.blueprint_assessment.supported_features.includes("advanced_duct_pipe_connector_flow_factor_and_slope_metadata"), JSON.stringify(advancedPipeConnectorSpec.blueprint_assessment));
const ductPresetFlowBlueprint = {
  ...ductElbowBlueprint,
  family: { ...ductElbowBlueprint.family, family_key: "duct_elbow_preset_flow" },
  parameters: [...ductElbowBlueprint.parameters, { key: "design_airflow", name: "Design Airflow", data_type: "airflow", scope: "type", default: 500, group: "mechanical_airflow" }],
  parameter_order: [...ductElbowBlueprint.parameters.map((parameter) => parameter.key), "design_airflow"],
  types: ductElbowBlueprint.types.map((type) => ({ ...type, values: { ...type.values, design_airflow: 500 } })),
  connectors: ductElbowBlueprint.connectors.map((connector) => connector.key === "inlet" ? { ...connector, flow_configuration: "preset", flow_parameter: "design_airflow" } : connector),
  verification: { parameter_flex_cases: [...ductElbowBlueprint.verification.parameter_flex_cases, { parameter_key: "design_airflow", min: 250, nominal: 500, max: 800 }] },
};
const ductPresetFlowSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "duct_elbow_preset_flow", confirmed_fields: {}, blueprint: ductPresetFlowBlueprint });
assert.equal(ductPresetFlowSpec.blueprint_assessment.buildable_by_api, false);
assert.ok(ductPresetFlowSpec.blueprint_assessment.unsupported_features.includes("parameterized_sweep_profile_family_editor_ui"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "duct_elbow_preset_flow", confirmed_fields: {}, blueprint: { ...ductPresetFlowBlueprint, connectors: ductPresetFlowBlueprint.connectors.map((connector) => connector.key === "inlet" ? { ...connector, flow_configuration: "calculated" } : connector) } }), /flow_parameter requires flow_configuration=preset/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "global_pipe_break_in", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...advancedPipeConnectorBlueprint, types: advancedPipeConnectorBlueprint.types.map((type) => ({ ...type, values: { ...type.values, flow_factor: 1.1 } })) } }), /flow_factor_parameter must stay between 0 and 1/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "global_pipe_break_in", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...advancedPipeConnectorBlueprint, connectors: advancedPipeConnectorBlueprint.connectors.map((connector) => ({ ...connector, system_classification: "Fitting" })) } }), /allow_slope_adjustments requires system_classification=Global/);
const electricalEquipmentBlueprint = {
  ...blueprint,
  family: { family_key: "electrical_power_equipment", category: "electrical_equipment", template_behavior: "level_based", primary_axis: "x", part_type: "normal" },
  required_source_fields: [],
  electrical_load_classifications: [{ key: "hvac_load", name: "DSCons HVAC", abbreviation: "HVAC" }],
  parameters: [
    { key: "voltage", name: "Voltage", data_type: "voltage", scope: "type", default: 400, group: "electrical" },
    { key: "apparent_load", name: "Apparent Load", data_type: "apparent_power", scope: "type", default: 12500, group: "electrical" },
    { key: "number_of_poles", name: "Number of Poles", data_type: "number_of_poles", scope: "type", default: 3, group: "electrical" },
    { key: "power_factor", name: "Power Factor", data_type: "number", scope: "type", default: 0.9, group: "electrical" },
    { key: "balanced_load", name: "Balanced Load", data_type: "yesno", scope: "type", default: true, group: "electrical" },
    { key: "load_classification", name: "Load Classification", data_type: "load_classification", scope: "type", default: "hvac_load", group: "electrical" },
  ],
  parameter_order: ["voltage", "apparent_load", "number_of_poles", "power_factor", "balanced_load", "load_classification"],
  types: [{ name: "400V-12.5kVA", values: { voltage: 400, apparent_load: 12500, number_of_poles: 3, power_factor: 0.9, balanced_load: true, load_classification: "hvac_load" } }],
  parts: [{ key: "body", primitive: "extrusion", operation: "solid", axis: "x", role: "ElectricalBody", profile: { shape: "rectangle", width_mm: 600, height_mm: 900 }, start_mm: -225, end_mm: 225, visibility }],
  connectors: [{ key: "power", discipline: "electrical", role: "Power", host_part: "body", host_face: "end", system_classification: "PowerBalanced", profile: "logical", primary: true, voltage_parameter: "voltage", apparent_load_parameter: "apparent_load", number_of_poles_parameter: "number_of_poles", power_factor_parameter: "power_factor", balanced_load_parameter: "balanced_load", load_classification_parameter: "load_classification", power_factor_state: "lagging" }],
  verification: { parameter_flex_cases: [{ parameter_key: "power_factor", min: 0.8, nominal: 0.9, max: 1.0 }] },
};
const electricalEquipmentSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "electrical_power_equipment", confirmed_fields: {}, blueprint: electricalEquipmentBlueprint });
assert.equal(electricalEquipmentSpec.status, "human_confirmed");
assert.equal(electricalEquipmentSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(electricalEquipmentSpec.blueprint_assessment.supported_features.includes("electrical_connector_load_and_circuit_parameter_association"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "electrical_power_equipment", confirmed_fields: {}, blueprint: { ...electricalEquipmentBlueprint, connectors: electricalEquipmentBlueprint.connectors.map(({ voltage_parameter, ...item }) => item) } }), /requires voltage_parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "electrical_power_equipment", confirmed_fields: {}, blueprint: { ...electricalEquipmentBlueprint, parameters: electricalEquipmentBlueprint.parameters.map((item) => item.key === "voltage" ? { ...item, data_type: "number" } : item) } }), /voltage_parameter must reference a voltage Family Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "electrical_power_equipment", confirmed_fields: {}, blueprint: { ...electricalEquipmentBlueprint, types: [{ name: "Bad Poles", values: { ...electricalEquipmentBlueprint.types[0].values, number_of_poles: 4 } }] } }), /must be 1, 2 or 3 poles/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "electrical_power_equipment", confirmed_fields: {}, blueprint: { ...electricalEquipmentBlueprint, types: [{ name: "Bad Load", values: { ...electricalEquipmentBlueprint.types[0].values, load_classification: "missing_load" } }] } }), /unknown electrical load classification/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "electrical_power_equipment", confirmed_fields: {}, blueprint: { ...electricalEquipmentBlueprint, connectors: electricalEquipmentBlueprint.connectors.map((item) => ({ ...item, power_factor_state: "reactive" })) } }), /power_factor_state must be one of/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "electrical_power_equipment", confirmed_fields: {}, blueprint: { ...electricalEquipmentBlueprint, types: [{ name: "Bad PF", values: { ...electricalEquipmentBlueprint.types[0].values, power_factor: 1.1 } }] } }), /power factor must be greater than zero and at most 1/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "electrical_power_equipment", confirmed_fields: {}, blueprint: { ...electricalEquipmentBlueprint, types: [{ name: "Bad Balance", values: { ...electricalEquipmentBlueprint.types[0].values, balanced_load: false } }] } }), /PowerBalanced classification requires balanced_load=true/);
const lookupBlueprint = {
  ...blueprint,
  parameters: [
    ...blueprint.parameters,
    { key: "fallback_radius", name: "Fallback Radius", data_type: "length", scope: "type", default: 50 },
    { key: "radius", name: "Radius", data_type: "length", scope: "instance", lookup: { table_key: "sizes", result_column_key: "radius", default_parameter_key: "fallback_radius", lookup_parameter_keys: ["width"] } },
  ],
  lookup_tables: [{ key: "sizes", name: "DSCons Tank Sizes", columns: [{ key: "width", name: "Width", data_type: "length" }, { key: "radius", name: "Radius", data_type: "length" }], lookup_columns: ["width"], rows: [{ values: { width: 600, radius: 50 } }, { values: { width: 900, radius: 75 } }] }],
};
const lookupSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: lookupBlueprint });
assert.equal(lookupSpec.status, "human_confirmed");
assert.equal(lookupSpec.blueprint_assessment.buildable_by_api, true);
const symbolicBlueprint = { ...blueprint, symbolic_lines: [{ key: "plan_cross", role: "PlanSymbol", plane: "xy", points_mm: [point(-100, 0, 0), point(100, 0, 0), point(0, 0, 0), point(0, 100, 0)], visibility }] };
const symbolicSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: symbolicBlueprint });
assert.equal(symbolicSpec.status, "human_confirmed");
assert.equal(symbolicSpec.blueprint_assessment.buildable_by_api, true);
const parametricVisibilityBlueprint = {
  ...blueprint,
  parameters: [...blueprint.parameters, { key: "show_geometry", name: "Show Geometry", data_type: "yesno", scope: "type", default: true }],
  types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450, show_geometry: true } }],
  parts: blueprint.parts.map((part) => ({ ...part, visibility_parameter: "show_geometry" })),
  symbolic_lines: [{ ...symbolicBlueprint.symbolic_lines[0], visibility_parameter: "show_geometry" }],
  model_lines: [{ key: "service_axis", role: "ServiceAxis", plane: "xy", points_mm: [point(-150, -150, 0), point(150, 150, 0)], visibility_parameter: "show_geometry", visibility }],
};
const parametricVisibilitySpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: parametricVisibilityBlueprint });
assert.equal(parametricVisibilitySpec.status, "human_confirmed");
assert.equal(parametricVisibilitySpec.blueprint_assessment.buildable_by_api, true);
assert.ok(parametricVisibilitySpec.blueprint_assessment.supported_features.includes("symbolic_and_model_line_polyline_with_yesno_visibility_association"));
const directionalControlBlueprint = {
  ...blueprint,
  parts: blueprint.parts.map((part) => ({ ...part, visibility: { coarse: true, medium: true, fine: true, front_back: false, left_right: true, plan_rcp: true, only_when_cut: false } })),
  symbolic_lines: [{ ...symbolicBlueprint.symbolic_lines[0], visibility: { coarse: true, medium: false, fine: true, front_back: true, left_right: false, plan_rcp: true, only_when_cut: false } }],
  controls: [{ key: "flip_horizontal", shape: "double_horizontal_arrow", view_plane: "xy", position_mm: point(0, 0, 0) }],
  publication: { compact_rfa: false, preview_view: "none" },
};
const directionalControlSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: directionalControlBlueprint });
assert.equal(directionalControlSpec.status, "human_confirmed");
assert.equal(directionalControlSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(directionalControlSpec.blueprint_assessment.supported_features.includes("detail_and_directional_visibility"));
assert.ok(directionalControlSpec.blueprint_assessment.supported_features.includes("family_flip_controls"));
const detailLevelRepresentationBlueprint = {
  ...blueprint,
  parts: blueprint.parts.map((part) => ({ ...part, visibility: { coarse: false, medium: false, fine: true } })),
  symbolic_lines: [{ key: "plan_cross", role: "PlanSymbol", plane: "xy", points_mm: [point(-100, 0, 0), point(100, 0, 0), point(0, 0, 0), point(0, 100, 0)], visibility: { coarse: true, medium: true, fine: false } }],
  model_lines: [{ key: "flow_arrow", role: "FlowArrow", plane: "xy", points_mm: [point(-120, -80, 0), point(120, -80, 0)], visibility: { coarse: true, medium: true, fine: false } }],
  detail_level_representations: [{ key: "plan_symbol_vs_fine_geometry", policy: "coarse_medium_2d__fine_3d", physical_part_keys: ["body"], symbolic_line_keys: ["plan_cross"], model_line_keys: ["flow_arrow"] }],
};
const detailLevelRepresentationSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: detailLevelRepresentationBlueprint });
assert.equal(detailLevelRepresentationSpec.status, "human_confirmed");
assert.equal(detailLevelRepresentationSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(detailLevelRepresentationSpec.blueprint_assessment.supported_features.includes("coarse_medium_2d__fine_3d_representation_sets"));
assert.deepEqual(detailLevelRepresentationSpec.blueprint.detail_level_representations[0], detailLevelRepresentationBlueprint.detail_level_representations[0]);
const referenceGraphBlueprint = {
  ...blueprint,
  required_source_fields: ["width_mm"],
  parameters: [{ key: "width", name: "Width", data_type: "length", scope: "type", source_field: "width_mm" }],
  types: [{ name: "T-1", values: { width: 600 } }],
  parts: [{ key: "body", primitive: "extrusion", operation: "solid", axis: "x", role: "TankBody", profile: { shape: "rectangle", width_mm: 600, height_mm: 400 }, start_mm: 0, end_mm: 450, visibility }],
  reference_planes: [
    { key: "left", name: "Left Control", view_plane: "xy", bubble_end_mm: point(-1000, -300, 0), free_end_mm: point(1000, -300, 0), cut_vector: point(0, 0, 1), strength: "strong" },
    { key: "right", name: "Right Control", view_plane: "xy", bubble_end_mm: point(-1000, 300, 0), free_end_mm: point(1000, 300, 0), cut_vector: point(0, 0, 1), strength: "strong" },
  ],
  reference_lines: [{ key: "axis", plane: "xy", start_mm: point(-500, 0, 0), end_mm: point(500, 0, 0) }],
  dimensions: [{ key: "overall_width", view_plane: "xy", reference_keys: ["left", "right"], line_start_mm: point(0, -300, 0), line_end_mm: point(0, 300, 0), parameter_key: "width" }],
  alignments: [
    { key: "lock_left", view_plane: "xy", reference_plane_key: "left", part_key: "body", part_face: "negative_y" },
    { key: "lock_right", view_plane: "xy", reference_plane_key: "right", part_key: "body", part_face: "positive_y" },
  ],
  verification: { parameter_flex_cases: [{ parameter_key: "width", min: 300, nominal: 600, max: 900 }] },
};
const referenceGraphSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: referenceGraphBlueprint });
assert.equal(referenceGraphSpec.status, "human_confirmed");
assert.equal(referenceGraphSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(referenceGraphSpec.blueprint_assessment.supported_features.includes("reference_plane_line_dimension_alignment_graph"));
assert.equal(referenceGraphSpec.blueprint.reference_planes[0].reference_type, "strong", "legacy strength must normalize to the v3 reference_type contract");
const modelLineBindingBlueprint = {
  ...referenceGraphBlueprint,
  model_lines: [{ key: "elbow_outline", role: "ElbowOutline", plane: "xy", points_mm: [point(-150, -300, 0), point(150, 300, 0)], endpoint_bindings: [{ endpoint: "start", reference_plane_key: "left" }, { endpoint: "end", reference_plane_key: "right" }], visibility }],
};
const modelLineBindingSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: modelLineBindingBlueprint });
assert.equal(modelLineBindingSpec.status, "human_confirmed");
assert.equal(modelLineBindingSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(modelLineBindingSpec.blueprint_assessment.supported_features.includes("model_line_endpoint_reference_plane_bindings"));
assert.deepEqual(modelLineBindingSpec.blueprint.model_lines[0].endpoint_bindings, modelLineBindingBlueprint.model_lines[0].endpoint_bindings);
const referenceFrameworkBlueprint = {
  ...blueprint,
  reference_plane_subcategories: [{ key: "origin_controls", name: "DSCons Origin Controls", color_rgb: { r: 0, g: 127, b: 255 }, projection_line_weight: 3, line_pattern_name: "Dash" }],
  reference_planes: [
    { key: "origin_left_right", name: "Center (Left/Right)", view_plane: "xy", bubble_end_mm: point(0, -1000, 0), free_end_mm: point(0, 1000, 0), cut_vector: point(0, 0, 1), reference_type: "center_left_right", defines_origin: true, subcategory_key: "origin_controls" },
    { key: "origin_front_back", name: "Center (Front/Back)", view_plane: "xy", bubble_end_mm: point(-1000, 0, 0), free_end_mm: point(1000, 0, 0), cut_vector: point(0, 0, 1), reference_type: "center_front_back", defines_origin: true, subcategory_key: "origin_controls" },
  ],
};
const referenceFrameworkSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: referenceFrameworkBlueprint });
assert.equal(referenceFrameworkSpec.status, "human_confirmed");
assert.equal(referenceFrameworkSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(referenceFrameworkSpec.blueprint_assessment.supported_features.includes("stable_named_reference_origin_and_reference_plane_graphics"));
assert.equal(referenceFrameworkSpec.blueprint.reference_planes.filter((item) => item.defines_origin).length, 2);
const equalGraphBlueprint = {
  ...referenceGraphBlueprint,
  reference_planes: [...referenceGraphBlueprint.reference_planes, { key: "center", name: "Center Control", view_plane: "xy", bubble_end_mm: point(-1000, 0, 0), free_end_mm: point(1000, 0, 0), cut_vector: point(0, 0, 1), strength: "strong" }],
  dimensions: [...referenceGraphBlueprint.dimensions, { key: "equal_bays", kind: "linear", view_plane: "xy", reference_keys: ["left", "center", "right"], line_start_mm: point(150, -300, 0), line_end_mm: point(150, 300, 0), equality: true }],
};
const equalGraphSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: equalGraphBlueprint });
assert.equal(equalGraphSpec.status, "human_confirmed");
assert.equal(equalGraphSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(equalGraphSpec.blueprint_assessment.supported_features.includes("angular_radial_equal_dimension_multibranch_graph"));
const radialGraphBlueprint = {
  ...blueprint,
  required_source_fields: [],
  parameters: [{ key: "radius", name: "Radius", data_type: "length", scope: "type", default: 100 }],
  types: [{ name: "T-1", values: { radius: 100 } }],
  parts: [{ key: "round_body", primitive: "extrusion", operation: "solid", axis: "x", role: "RoundBody", profile: { shape: "circle", diameter_mm: 200 }, start_mm: 0, end_mm: 300, visibility }],
  dimensions: [{ key: "body_radius", kind: "radial", view_plane: "yz", part_key: "round_body", profile_loop_index: 0, leader_point_mm: point(0, 0, 150), parameter_key: "radius" }],
  verification: { parameter_flex_cases: [{ parameter_key: "radius", min: 50, nominal: 100, max: 150 }] },
};
const radialGraphSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: radialGraphBlueprint });
assert.equal(radialGraphSpec.status, "human_confirmed");
assert.equal(radialGraphSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(radialGraphSpec.blueprint_assessment.supported_features.includes("angular_radial_equal_dimension_multibranch_graph"));
const angularGraphBlueprint = {
  ...blueprint,
  required_source_fields: [],
  parameters: [{ key: "opening_angle", name: "Opening Angle", data_type: "angle", scope: "type", default: 45 }],
  types: [{ name: "T-1", values: { opening_angle: 45 } }],
  parts: [{ key: "anchor_body", primitive: "extrusion", operation: "solid", axis: "x", role: "AnchorBody", profile: { shape: "rectangle", width_mm: 100, height_mm: 100 }, start_mm: 0, end_mm: 100, visibility }],
  reference_lines: [
    { key: "axis_x", plane: "xy", start_mm: point(0, 0, 0), end_mm: point(500, 0, 0) },
    { key: "axis_angle", plane: "xy", start_mm: point(0, 0, 0), end_mm: point(353.553, 353.553, 0) },
  ],
  dimensions: [{ key: "opening_angle_dimension", kind: "angular", view_plane: "xy", reference_keys: ["axis_x", "axis_angle"], arc_center_mm: point(0, 0, 0), arc_radius_mm: 200, start_angle_degrees: 0, end_angle_degrees: 45, parameter_key: "opening_angle" }],
  verification: { parameter_flex_cases: [{ parameter_key: "opening_angle", min: 15, nominal: 45, max: 75 }] },
};
const angularGraphSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: angularGraphBlueprint });
assert.equal(angularGraphSpec.status, "human_confirmed");
assert.equal(angularGraphSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(angularGraphSpec.blueprint_assessment.supported_features.includes("angular_radial_equal_dimension_multibranch_graph"));
const angularSweepBlueprint = {
  ...angularGraphBlueprint,
  parts: [{ key: "rotating_sweep", primitive: "sweep", operation: "solid", role: "Rotating Sweep", profile: { shape: "circle", diameter_mm: 80 }, path: { kind: "reference_line", reference_line_key: "axis_angle", angular_dimension_key: "opening_angle_dimension" }, profile_location: "start", visibility }],
};
const angularSweepSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: angularSweepBlueprint });
assert.equal(angularSweepSpec.status, "human_confirmed");
assert.equal(angularSweepSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(angularSweepSpec.blueprint_assessment.supported_features.includes("reference_line_driven_angular_sweep_rotation"));
const angularParameterizedSweepBlueprint = {
  ...angularSweepBlueprint,
  parameters: [...angularSweepBlueprint.parameters, { key: "sweep_diameter", name: "Sweep Diameter", data_type: "length", scope: "type", default: 80 }],
  types: [{ name: "T-1", values: { opening_angle: 45, sweep_diameter: 80 } }],
  parts: [{ ...angularSweepBlueprint.parts[0], profile: { shape: "circle", diameter_parameter: "sweep_diameter" } }],
  verification: { parameter_flex_cases: [{ parameter_key: "opening_angle", min: 15, nominal: 45, max: 75 }, { parameter_key: "sweep_diameter", min: 50, nominal: 80, max: 120 }] },
};
const angularParameterizedSweepSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: angularParameterizedSweepBlueprint });
assert.equal(angularParameterizedSweepSpec.status, "requires_capability_or_ui_fallback", JSON.stringify(angularParameterizedSweepSpec.blueprint_assessment));
assert.equal(angularParameterizedSweepSpec.blueprint_assessment.buildable_by_api, false);
assert.ok(angularParameterizedSweepSpec.blueprint_assessment.unsupported_features.includes("parameterized_sweep_profile_family_editor_ui"));
assert.ok(angularParameterizedSweepSpec.blueprint_assessment.supported_features.includes("reference_line_driven_angular_sweep_rotation"));
const arrayBlueprint = {
  ...blueprint,
  required_source_fields: [],
  parameters: [{ key: "blade_count", name: "Blade Count", data_type: "integer", scope: "type" }],
  types: [{ name: "T-1", values: { blade_count: 4 } }],
  parts: [{ key: "blade", primitive: "extrusion", operation: "solid", axis: "x", role: "Blade", profile: { shape: "rectangle", width_mm: 80, height_mm: 300 }, start_mm: 0, end_mm: 20, visibility }],
  arrays: [{ key: "blade_array", member_part_keys: ["blade"], view_plane: "xy", count_parameter: "blade_count", direction_mm: point(0, 150, 0), anchor: "second" }],
  verification: { parameter_flex_cases: [{ parameter_key: "blade_count", min: 2, nominal: 4, max: 6 }] },
};
const arraySpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: arrayBlueprint });
assert.equal(arraySpec.status, "human_confirmed");
assert.equal(arraySpec.blueprint_assessment.buildable_by_api, true);
assert.ok(arraySpec.blueprint_assessment.supported_features.includes("parameterized_linear_part_and_nested_array"));
const childBlueprint = {
  ...blueprint,
  family: { family_key: "custom_nested_child", category: "generic_model", template_behavior: "level_based", primary_axis: "x" },
  required_source_fields: [],
  parameters: [{ key: "child_width", name: "Child Width", data_type: "length", scope: "instance", default: 100 }],
  types: [{ name: "ChildType", values: { child_width: 100 } }],
  parts: [{ key: "child_body", primitive: "extrusion", operation: "solid", axis: "x", role: "ChildBody", profile: { shape: "rectangle", width_mm: 100, height_mm: 100 }, start_mm: 0, end_mm: 100, visibility }],
  verification: { parameter_flex_cases: [] },
};
const childSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_child", confirmed_fields: {}, blueprint: childBlueprint });
const childRfa = path.join(approved, "custom_nested_child.rfa"); const childBytes = Buffer.from("synthetic-rfa-for-node-contract-test"); await writeFile(childRfa, childBytes);
assert.throws(() => recordFamilyBuildArtifact(childSpec.spec_id, { spec_id: childSpec.spec_id, schema_version: "3.0", family_path: childRfa, sha256: createHash("sha256").update(childBytes).digest("base64"), blueprint_hash: childSpec.blueprint.blueprint_hash, created: { types: ["ChildType"] } }), /requires Revit reopen read-back/);
assert.throws(() => recordFamilyBuildArtifact(childSpec.spec_id, { ...syntheticArtifactResult(childSpec, childRfa, childBytes, "ChildType", "OneLevelBased"), complexity: undefined }), /invalid or failed complexity evidence/);
const childArtifact = recordFamilyBuildArtifact(childSpec.spec_id, syntheticArtifactResult(childSpec, childRfa, childBytes, "ChildType", "OneLevelBased"));
assert.equal(childArtifact.status, "verified");
assert.equal(childArtifact.category, "generic_model");
assert.equal(childArtifact.family_placement_type, "OneLevelBased");
assert.equal(childArtifact.complexity.nested_depth, 0);
assert.equal(childArtifact.complexity.within_budget, true);
assert.deepEqual(childArtifact.parameter_interfaces.child_width, { name: "Child Width", data_type: "length", scope: "instance", is_shared: false, shared_guid: null, verified: true });
const alternateChildBlueprint = { ...childBlueprint, family: { ...childBlueprint.family, family_key: "custom_nested_child_alt" }, types: [{ name: "AlternateType", values: { child_width: 150 } }] };
const alternateChildSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_child_alt", confirmed_fields: {}, blueprint: alternateChildBlueprint });
const alternateChildRfa = path.join(approved, "custom_nested_child_alt.rfa"); const alternateChildBytes = Buffer.from("synthetic-alternate-rfa-for-node-contract-test"); await writeFile(alternateChildRfa, alternateChildBytes);
const alternateChildArtifact = recordFamilyBuildArtifact(alternateChildSpec.spec_id, syntheticArtifactResult(alternateChildSpec, alternateChildRfa, alternateChildBytes, "AlternateType", "OneLevelBased"));
assert.equal(alternateChildArtifact.category, "generic_model");
const hostedChildBlueprint = { ...childBlueprint, family: { ...childBlueprint.family, family_key: "custom_hosted_child", template_behavior: "face_based", work_plane_based: true }, types: [{ name: "HostedType", values: { child_width: 100 } }] };
const hostedChildSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_hosted_child", confirmed_fields: {}, blueprint: hostedChildBlueprint });
const hostedChildRfa = path.join(approved, "custom_hosted_child.rfa"); const hostedChildBytes = Buffer.from("synthetic-hosted-rfa-for-node-contract-test"); await writeFile(hostedChildRfa, hostedChildBytes);
const hostedChildArtifact = recordFamilyBuildArtifact(hostedChildSpec.spec_id, syntheticArtifactResult(hostedChildSpec, hostedChildRfa, hostedChildBytes, "HostedType", "WorkPlaneBased"));
const lineChildBlueprint = { ...childBlueprint, family: { ...childBlueprint.family, family_key: "custom_line_child", template_behavior: "line_based" }, types: [{ name: "LineType", values: { child_width: 100 } }] };
const lineChildSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_line_child", confirmed_fields: {}, blueprint: lineChildBlueprint });
const lineChildRfa = path.join(approved, "custom_line_child.rfa"); const lineChildBytes = Buffer.from("synthetic-line-rfa-for-node-contract-test"); await writeFile(lineChildRfa, lineChildBytes);
const lineChildArtifact = recordFamilyBuildArtifact(lineChildSpec.spec_id, syntheticArtifactResult(lineChildSpec, lineChildRfa, lineChildBytes, "LineType", "CurveBased"));
const nestedBlueprint = {
  ...blueprint,
  family: { family_key: "custom_nested_parent", category: "mechanical_equipment", template_behavior: "level_based", primary_axis: "x" },
  required_source_fields: [],
  parameters: [{ key: "parent_width", name: "Parent Width", data_type: "length", scope: "type", default: 100 }],
  types: [{ name: "ParentType", values: { parent_width: 100 } }],
  parts: [{ key: "parent_body", primitive: "extrusion", operation: "solid", axis: "x", role: "ParentBody", profile: { shape: "rectangle", width_mm: 200, height_mm: 200 }, start_mm: 0, end_mm: 200, visibility }],
  nested_components: [{ key: "child", blueprint_id: childSpec.spec_id, created_from_blueprint: true, type_name: "ChildType", placement_point_mm: point(0, 0, 0), rotation_axis: "z", rotation_degrees: 0, parameter_map: { child_width: "parent_width" } }],
  mirrors: [{ key: "mirror_child", target_kind: "nested_component", target_keys: ["child"], plane_origin_mm: point(0, 0, 0), plane_normal: point(0, 1, 0), copy: false }],
  verification: { parameter_flex_cases: [{ parameter_key: "parent_width", min: 50, nominal: 100, max: 150 }] },
};
const nestedSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: nestedBlueprint });
assert.equal(nestedSpec.status, "human_confirmed");
assert.equal(nestedSpec.blueprint_assessment.buildable_by_api, true);
assert.equal(nestedSpec.blueprint.nested_components[0].resolved_artifact.sha256, childArtifact.sha256);
assert.equal(nestedSpec.blueprint.nested_components[0].resolved_artifact.template_behavior, "level_based");
assert.equal(nestedSpec.blueprint.nested_components[0].resolved_artifact.family_placement_type, "OneLevelBased");
assert.equal(nestedSpec.blueprint.resolved_complexity.nested_depth, 1);
assert.ok(nestedSpec.blueprint.resolved_complexity.aggregate_complexity_score > nestedSpec.blueprint.resolved_complexity.local_complexity_score);
assert.ok(nestedSpec.citations.some((item) => item.artifact_id === childArtifact.artifact_id));
assert.ok(nestedSpec.blueprint_assessment.supported_features.includes("bounded_part_and_nested_component_mirror"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...nestedBlueprint, performance_budget: { max_nested_depth: 0 } } }), /nested_depth=1 exceeds performance_budget\.max_nested_depth=0/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...nestedBlueprint, performance_budget: { max_complexity_score: 40 } } }), /aggregate_complexity_score=45 exceeds performance_budget\.max_complexity_score=40/);
const nestedVisibilityBlueprint = {
  ...nestedBlueprint,
  parameters: [...nestedBlueprint.parameters, { key: "show_child", name: "Show Child", data_type: "yesno", scope: "type", default: true }],
  types: [{ name: "ParentType", values: { parent_width: 100, show_child: true } }],
  nested_components: nestedBlueprint.nested_components.map((item) => ({ ...item, visibility_parameter: "show_child" })),
};
const nestedVisibilitySpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: nestedVisibilityBlueprint });
assert.equal(nestedVisibilitySpec.status, "human_confirmed");
assert.equal(nestedVisibilitySpec.blueprint_assessment.buildable_by_api, true);
const fixedNestedArrayBlueprint = {
  ...nestedBlueprint,
  arrays: [{ key: "child_array", member_nested_component_keys: ["child"], view_plane: "xy", count: 3, direction_mm: point(0, 250, 0), anchor: "second" }],
  mirrors: [],
};
const fixedNestedArraySpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: fixedNestedArrayBlueprint });
assert.equal(fixedNestedArraySpec.status, "human_confirmed");
assert.equal(fixedNestedArraySpec.blueprint_assessment.buildable_by_api, true);
assert.ok(fixedNestedArraySpec.blueprint_assessment.supported_features.includes("parameterized_linear_part_and_nested_array"));
const parameterizedNestedArrayBlueprint = {
  ...fixedNestedArrayBlueprint,
  parameters: [...nestedBlueprint.parameters, { key: "child_count", name: "Child Count", data_type: "integer", scope: "type" }],
  types: [{ name: "ParentType", values: { parent_width: 100, child_count: 4 } }],
  arrays: [{ ...fixedNestedArrayBlueprint.arrays[0], count: undefined, count_parameter: "child_count" }],
  verification: { parameter_flex_cases: [...nestedBlueprint.verification.parameter_flex_cases, { parameter_key: "child_count", min: 2, nominal: 4, max: 6 }] },
};
const parameterizedNestedArraySpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: parameterizedNestedArrayBlueprint });
assert.equal(parameterizedNestedArraySpec.status, "human_confirmed");
assert.equal(parameterizedNestedArraySpec.blueprint_assessment.buildable_by_api, true);
const interchangeableNestedBlueprint = {
  ...nestedBlueprint,
  parameters: [{ key: "nested_choice", name: "Nested Choice", data_type: "family_type", family_category: "generic_model", scope: "type", group: "mechanical" }],
  types: [{ name: "ParentPrimary", values: { nested_choice: "primary" } }, { name: "ParentAlternate", values: { nested_choice: "alternate" } }],
  nested_components: [{ key: "swappable_child", family_type_parameter_key: "nested_choice", type_options: [{ key: "primary", blueprint_id: childSpec.spec_id, created_from_blueprint: true, type_name: "ChildType", sharing: "embedded" }, { key: "alternate", blueprint_id: alternateChildSpec.spec_id, created_from_blueprint: true, type_name: "AlternateType", sharing: "embedded" }], placement_point_mm: point(0, 0, 0), rotation_axis: "z", rotation_degrees: 0, parameter_map: {} }],
  mirrors: [],
  verification: { parameter_flex_cases: [] },
};
const interchangeableNestedSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: interchangeableNestedBlueprint });
assert.equal(interchangeableNestedSpec.status, "human_confirmed");
assert.equal(interchangeableNestedSpec.blueprint.nested_components[0].resolved_options.length, 2);
assert.ok(interchangeableNestedSpec.blueprint.nested_components[0].resolved_options.every((option) => option.category === "generic_model"));
assert.ok(interchangeableNestedSpec.blueprint_assessment.supported_features.includes("interchangeable_nested_family_type_parameter"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...interchangeableNestedBlueprint, types: [{ name: "ParentBad", values: { nested_choice: "missing" } }] } }), /must reference a declared nested option key/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...interchangeableNestedBlueprint, nested_components: interchangeableNestedBlueprint.nested_components.map((item) => ({ ...item, parameter_map: { child_width: "nested_choice" } })) } }), /cannot combine interchangeable type_options/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...interchangeableNestedBlueprint, mirrors: [{ key: "mirror_swap", target_kind: "nested_component", target_keys: ["swappable_child"], plane_origin_mm: point(0, 0, 0), plane_normal: point(0, 1, 0), copy: true }] } }), /cannot target interchangeable nested component/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...nestedBlueprint, nested_components: nestedBlueprint.nested_components.map((item) => ({ ...item, sharing: "shared" })) } }), /requires a parent Instance Parameter with shared_guid/);
const sharedNestedGuid = "4d516524-0d42-4a9d-b2dc-3456789abcde";
const sharedChildBlueprint = {
  ...childBlueprint,
  family: { ...childBlueprint.family, family_key: "custom_shared_nested_child", shared: true },
  parameters: [{ key: "child_shared_width", name: "Child Shared Width", data_type: "length", scope: "instance", default: 100, shared_guid: sharedNestedGuid }],
  types: [{ name: "SharedChildType", values: { child_shared_width: 100 } }],
};
const sharedChildSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_shared_nested_child", confirmed_fields: {}, blueprint: sharedChildBlueprint });
const sharedChildRfa = path.join(approved, "custom_shared_nested_child.rfa"); const sharedChildBytes = Buffer.from("synthetic-shared-nested-rfa-for-node-contract-test"); await writeFile(sharedChildRfa, sharedChildBytes);
const sharedChildArtifact = recordFamilyBuildArtifact(sharedChildSpec.spec_id, syntheticArtifactResult(sharedChildSpec, sharedChildRfa, sharedChildBytes, "SharedChildType", "OneLevelBased"));
assert.deepEqual(sharedChildArtifact.parameter_interfaces.child_shared_width, { name: "Child Shared Width", data_type: "length", scope: "instance", is_shared: true, shared_guid: sharedNestedGuid, verified: true });
const sharedNestedBlueprint = {
  ...nestedBlueprint,
  parameters: [{ key: "parent_shared_width", name: "Child Shared Width", data_type: "length", scope: "instance", default: 100, shared_guid: sharedNestedGuid }],
  types: [{ name: "SharedParentType", values: {} }], mirrors: [],
  nested_components: [{ key: "shared_child", blueprint_id: sharedChildSpec.spec_id, created_from_blueprint: true, type_name: "SharedChildType", sharing: "shared", shared_parameter_map_mode: "identity_only", placement_point_mm: point(0, 0, 0), rotation_axis: "z", rotation_degrees: 0, parameter_map: { child_shared_width: "parent_shared_width" } }],
  verification: { parameter_flex_cases: [] },
};
const sharedNestedSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: sharedNestedBlueprint });
assert.equal(sharedNestedSpec.status, "human_confirmed");
assert.equal(sharedNestedSpec.blueprint.nested_components[0].resolved_artifact.parameter_interfaces.child_shared_width.child.shared_guid, sharedNestedGuid);
assert.equal(sharedNestedSpec.blueprint.nested_components[0].resolved_artifact.parameter_interfaces.child_shared_width.shared_identity_required, true);
assert.equal(sharedNestedSpec.blueprint.nested_components[0].shared_parameter_map_mode, "identity_only");
const sharedNestedDefaultModeSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...sharedNestedBlueprint, nested_components: sharedNestedBlueprint.nested_components.map(({ shared_parameter_map_mode, ...item }) => item) } });
assert.equal(sharedNestedDefaultModeSpec.blueprint.nested_components[0].shared_parameter_map_mode, "identity_only");
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...sharedNestedBlueprint, parameters: [{ ...sharedNestedBlueprint.parameters[0], shared_guid: "4d516524-0d42-4a9d-b2dc-3456789abcdf" }] } }), /requires child and parent Instance Parameters with the same shared_guid/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...sharedNestedBlueprint, parameters: [{ ...sharedNestedBlueprint.parameters[0], scope: "type" }] } }), /requires a parent Instance Parameter with shared_guid/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...sharedNestedBlueprint, nested_components: sharedNestedBlueprint.nested_components.map((item) => ({ ...item, shared_parameter_map_mode: "value_propagation" })) } }), /shared_parameter_map_mode/);
const hostedPointBlueprint = {
  ...nestedBlueprint,
  nested_components: [{ key: "hosted_child", blueprint_id: hostedChildSpec.spec_id, created_from_blueprint: true, type_name: "HostedType", placement_mode: "host_face_point", host_part_key: "parent_body", host_face: "end", placement_point_mm: point(200, 0, 0), reference_direction: point(0, 1, 0), parameter_map: { child_width: "parent_width" } }],
  mirrors: [],
};
const hostedPointSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: hostedPointBlueprint });
assert.equal(hostedPointSpec.status, "human_confirmed");
assert.equal(hostedPointSpec.blueprint.nested_components[0].resolved_artifact.sha256, hostedChildArtifact.sha256);
assert.equal(hostedPointSpec.blueprint.nested_components[0].resolved_artifact.template_behavior, "face_based");
assert.equal(hostedPointSpec.blueprint.nested_components[0].resolved_artifact.family_placement_type, "WorkPlaneBased");
assert.ok(hostedPointSpec.blueprint_assessment.supported_features.includes("placement_aware_nested_blueprint_on_parent_solid_face"));
const hostedLineBlueprint = {
  ...nestedBlueprint,
  nested_components: [{ key: "line_child", blueprint_id: lineChildSpec.spec_id, created_from_blueprint: true, type_name: "LineType", placement_mode: "host_face_line", host_part_key: "parent_body", host_face: "end", placement_line_start_mm: point(200, -50, 0), placement_line_end_mm: point(200, 50, 0), parameter_map: { child_width: "parent_width" } }],
  mirrors: [],
};
const hostedLineSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: hostedLineBlueprint });
assert.equal(hostedLineSpec.status, "human_confirmed");
assert.equal(hostedLineSpec.blueprint.nested_components[0].resolved_artifact.sha256, lineChildArtifact.sha256);
assert.equal(hostedLineSpec.blueprint.nested_components[0].resolved_artifact.family_placement_type, "CurveBased");
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedPointBlueprint, nested_components: hostedPointBlueprint.nested_components.map((item) => ({ ...item, host_part_key: "missing" })) } }), /existing solid host part/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedPointBlueprint, nested_components: hostedPointBlueprint.nested_components.map(({ host_face, ...item }) => item) } }), /host_face must be/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedPointBlueprint, nested_components: hostedPointBlueprint.nested_components.map((item) => ({ ...item, placement_point_mm: point(200, 500, 0) })) } }), /must lie inside the declared host face/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedLineBlueprint, nested_components: hostedLineBlueprint.nested_components.map((item) => ({ ...item, placement_line_end_mm: point(200, 500, 0) })) } }), /must lie inside the declared host face/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedPointBlueprint, nested_components: hostedPointBlueprint.nested_components.map((item) => ({ ...item, reference_direction: point(0, 0, 0) })) } }), /must be non-zero/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedPointBlueprint, nested_components: hostedPointBlueprint.nested_components.map((item) => ({ ...item, reference_direction: point(1, 0, 0) })) } }), /cannot be parallel/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedPointBlueprint, nested_components: hostedPointBlueprint.nested_components.map((item) => ({ ...item, blueprint_id: childSpec.spec_id, type_name: "ChildType" })) } }), /incompatible with child template_behavior level_based/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...nestedBlueprint, nested_components: [{ ...nestedBlueprint.nested_components[0], blueprint_id: hostedChildSpec.spec_id, type_name: "HostedType" }] } }), /incompatible with child template_behavior face_based/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedPointBlueprint, arrays: [{ key: "hosted_array", member_nested_component_keys: ["hosted_child"], view_plane: "xy", count: 2, direction_mm: point(0, 250, 0), anchor: "second" }] } }), /cannot contain hosted nested component/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedPointBlueprint, mirrors: [{ key: "hosted_mirror", target_kind: "nested_component", target_keys: ["hosted_child"], plane_origin_mm: point(0, 0, 0), plane_normal: point(0, 1, 0), copy: true }] } }), /cannot target hosted nested component/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...interchangeableNestedBlueprint, nested_components: interchangeableNestedBlueprint.nested_components.map((item) => ({ ...item, placement_mode: "host_face_point", host_part_key: "parent_body", host_face: "end", placement_point_mm: point(200, 0, 0), reference_direction: point(0, 1, 0), rotation_axis: undefined, rotation_degrees: undefined })) } }), /cannot combine hosted placement with interchangeable type_options/);
const voidHostBlueprint = { ...hostedPointBlueprint, parts: [...hostedPointBlueprint.parts, { key: "opening", primitive: "extrusion", operation: "void", axis: "x", role: "Opening", profile: { shape: "rectangle", width_mm: 20, height_mm: 20 }, start_mm: 0, end_mm: 200, cut_targets: ["parent_body"], visibility }], nested_components: hostedPointBlueprint.nested_components.map((item) => ({ ...item, host_part_key: "opening" })) };
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: voidHostBlueprint }), /existing solid host part/);
const mismatchChildBlueprint = { ...hostedChildBlueprint, family: { ...hostedChildBlueprint.family, family_key: "custom_mismatch_hosted_child" } };
const mismatchChildSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_mismatch_hosted_child", confirmed_fields: {}, blueprint: mismatchChildBlueprint });
const mismatchChildRfa = path.join(approved, "custom_mismatch_hosted_child.rfa"); const mismatchChildBytes = Buffer.from("synthetic-mismatched-placement-rfa"); await writeFile(mismatchChildRfa, mismatchChildBytes);
recordFamilyBuildArtifact(mismatchChildSpec.spec_id, syntheticArtifactResult(mismatchChildSpec, mismatchChildRfa, mismatchChildBytes, "HostedType", "OneLevelBased"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...hostedPointBlueprint, nested_components: hostedPointBlueprint.nested_components.map((item) => ({ ...item, blueprint_id: mismatchChildSpec.spec_id })) } }), /does not match template_behavior face_based/);
const detailItemBlueprint = { ...blueprint, family: { family_key: "custom_detail_item", category: "detail_item", template_behavior: "detail_item", primary_axis: "x" }, parts: [], parameters: [], types: [{ name: "Standard", values: {} }], symbolic_lines: symbolicBlueprint.symbolic_lines, required_source_fields: [], verification: { parameter_flex_cases: [] } };
const detailItemSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_detail_item", confirmed_fields: {}, blueprint: detailItemBlueprint });
assert.equal(detailItemSpec.status, "human_confirmed");
assert.equal(detailItemSpec.blueprint_assessment.buildable_by_api, true);
const detailAndFillBlueprint = {
  ...detailItemBlueprint,
  symbolic_lines: [],
  detail_lines: [{ key: "center_line", role: "CenterLine", plane: "xy", points_mm: [point(-100, 0, 0), point(100, 0, 0)] }],
  filled_regions: [{ key: "mask", role: "Mask", plane: "xy", type_name: "Solid Fill", boundary_loops: [{ points_mm: [point(-80, -40, 0), point(80, -40, 0), point(80, 40, 0), point(-80, 40, 0)] }] }],
};
const detailAndFillSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_detail_item", confirmed_fields: {}, blueprint: detailAndFillBlueprint });
assert.equal(detailAndFillSpec.status, "human_confirmed");
assert.equal(detailAndFillSpec.blueprint_assessment.buildable_by_api, true);
const parameterizedDetailLineBlueprint = {
  ...detailItemBlueprint,
  parameters: [{ key: "flexible_length", name: "Flexible Length", data_type: "length", scope: "type", default: 600 }],
  types: [{ name: "L-600", values: { flexible_length: 600 } }],
  symbolic_lines: [],
  detail_lines: [{ key: "flexible_axis", role: "FlexibleAxis", plane: "xy", points_mm: [point(-300, 0, 0), point(300, 0, 0)], endpoint_bindings: [{ endpoint: "start", reference_plane_key: "flex_start" }, { endpoint: "end", reference_plane_key: "flex_end" }] }],
  reference_planes: [
    { key: "flex_start", name: "Flexible Start", view_plane: "xy", bubble_end_mm: point(-300, -1000, 0), free_end_mm: point(-300, 1000, 0), cut_vector: point(0, 0, 1), strength: "strong" },
    { key: "flex_end", name: "Flexible End", view_plane: "xy", bubble_end_mm: point(300, -1000, 0), free_end_mm: point(300, 1000, 0), cut_vector: point(0, 0, 1), strength: "strong" },
  ],
  dimensions: [{ key: "flexible_length_dimension", view_plane: "xy", reference_keys: ["flex_start", "flex_end"], line_start_mm: point(-300, 120, 0), line_end_mm: point(300, 120, 0), parameter_key: "flexible_length" }],
  verification: { parameter_flex_cases: [{ parameter_key: "flexible_length", min: 300, nominal: 600, max: 900 }] },
};
const parameterizedDetailLineSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_detail_item", confirmed_fields: {}, blueprint: parameterizedDetailLineBlueprint });
assert.equal(parameterizedDetailLineSpec.status, "human_confirmed");
assert.equal(parameterizedDetailLineSpec.blueprint_assessment.buildable_by_api, true);
assert.ok(parameterizedDetailLineSpec.blueprint_assessment.supported_features.includes("detail_item_detail_line_endpoint_reference_plane_bindings"));
assert.deepEqual(parameterizedDetailLineSpec.blueprint.detail_lines[0].endpoint_bindings, parameterizedDetailLineBlueprint.detail_lines[0].endpoint_bindings);
const profileBlueprint = { ...blueprint, family: { family_key: "custom_profile", category: "profile", template_behavior: "profile", primary_axis: "x" }, parts: [], parameters: [], types: [{ name: "Standard", values: {} }], symbolic_lines: [], profile_loops: [{ key: "main_profile", role: "Profile", plane: "xy", points_mm: [point(-100, -50, 0), point(100, -50, 0), point(100, 50, 0), point(-100, 50, 0)] }], required_source_fields: [], verification: { parameter_flex_cases: [] } };
const profileSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_profile", confirmed_fields: {}, blueprint: profileBlueprint });
assert.equal(profileSpec.status, "human_confirmed");
assert.equal(profileSpec.blueprint_assessment.buildable_by_api, true);
const detailControlSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_detail_item", confirmed_fields: {}, blueprint: { ...detailItemBlueprint, controls: [{ key: "flip", shape: "horizontal_arrow", view_plane: "xy", position_mm: point(0, 0, 0) }] } });
assert.equal(detailControlSpec.status, "requires_capability_or_ui_fallback");
assert.ok(detailControlSpec.blueprint_assessment.unsupported_features.includes("controls_require_model_family_template"));
const profileControlSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_profile", confirmed_fields: {}, blueprint: { ...profileBlueprint, controls: [{ key: "flip", shape: "vertical_arrow", view_plane: "xy", position_mm: point(0, 0, 0) }] } });
assert.equal(profileControlSpec.status, "requires_capability_or_ui_fallback");
assert.ok(profileControlSpec.blueprint_assessment.unsupported_features.includes("controls_require_model_family_template"));
const unrelatedNameCollisionBlueprint = {
  ...blueprint,
  parameters: [...blueprint.parameters, { key: "body", name: "Body Code", data_type: "integer", scope: "type", default: 1 }],
  types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450, body: 1 } }],
};
const unrelatedNameCollisionSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: unrelatedNameCollisionBlueprint });
assert.equal(unrelatedNameCollisionSpec.status, "human_confirmed", "a parameter key matching a part key is not a geometry reference");
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parameter_order: ["width", "height"] } }), /must contain every declared parameter key exactly once/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parameter_order: ["width", "height", "height"] } }), /must contain every declared parameter key exactly once/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parameter_order: ["width", "height", "missing"] } }), /must contain every declared parameter key exactly once/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parameters: [...blueprint.parameters, { key: "bad_default", name: "Bad Default", data_type: "yesno", scope: "type", default: "yes" }] } }), /bad_default.default must be boolean/);
const catalogInstanceBlueprint = { ...blueprint, parameters: [...blueprint.parameters, { key: "catalog_instance", name: "Catalog Instance", data_type: "text", scope: "instance", default: "A" }], types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450 } }, { name: "T-2", values: { width: 900, height: 1200, depth: 600 } }], publication: { type_catalog: { parameter_keys: ["catalog_instance"] } } };
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: catalogInstanceBlueprint }), /must reference a declared Type parameter/);
const catalogFormulaBlueprint = { ...blueprint, parameters: [...blueprint.parameters, { key: "catalog_formula", name: "Catalog Formula", data_type: "length", scope: "type", formula: "Width * 2" }], types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450 } }, { name: "T-2", values: { width: 900, height: 1200, depth: 600 } }], publication: { type_catalog: { parameter_keys: ["catalog_formula"] } } };
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: catalogFormulaBlueprint }), /cannot be formula\/lookup-driven/);
const catalogMissingValueBlueprint = { ...blueprint, parameters: [...blueprint.parameters, { key: "catalog_code", name: "Catalog Code", data_type: "text", scope: "type" }], types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450, catalog_code: "A" } }, { name: "T-2", values: { width: 900, height: 1200, depth: 600 } }], publication: { type_catalog: { parameter_keys: ["catalog_code"] } } };
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: catalogMissingValueBlueprint }), /requires an explicit value or parameter default/);
const catalogMepTokenBlueprint = { ...blueprint, parameters: [...blueprint.parameters, { key: "catalog_airflow", name: "Catalog Air Flow", data_type: "airflow", scope: "type" }], types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450, catalog_airflow: 250 } }, { name: "T-2", values: { width: 900, height: 1200, depth: 600, catalog_airflow: 450 } }], publication: { type_catalog: { parameter_keys: ["catalog_airflow"] } } };
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: catalogMepTokenBlueprint }), /header\/unit syntax is runtime-certified/);
const catalogHeaderDelimiterBlueprint = { ...blueprint, parameters: [...blueprint.parameters, { key: "catalog_code", name: "Catalog##Code", data_type: "text", scope: "type" }], types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450, catalog_code: "A" } }, { name: "T-2", values: { width: 900, height: 1200, depth: 600, catalog_code: "B" } }], publication: { type_catalog: { parameter_keys: ["catalog_code"] } } };
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: catalogHeaderDelimiterBlueprint }), /cannot contain ##/);
const catalogLineBreakBlueprint = { ...blueprint, parameters: [...blueprint.parameters, { key: "catalog_code", name: "Catalog Code", data_type: "text", scope: "type" }], types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450, catalog_code: "A" } }, { name: "T-2", values: { width: 900, height: 1200, depth: 600, catalog_code: "B\nC" } }], publication: { type_catalog: { parameter_keys: ["catalog_code"] } } };
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: catalogLineBreakBlueprint }), /cannot contain a line break/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...directionalControlBlueprint, parts: directionalControlBlueprint.parts.map((part) => ({ ...part, visibility: { ...part.visibility, front_back: "yes" } })) } }), /visibility.front_back must be boolean/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...directionalControlBlueprint, controls: [{ key: "flip", shape: "triple_arrow", view_plane: "xy", position_mm: point(0, 0, 0) }] } }), /control flip.shape/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, types: [{ name: "T-1", values: { missing_parameter: 1 } }] } }), /unknown parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, connectors: [{ key: "supply", discipline: "duct", role: "supply", host_part: "body", host_face: "end", profile: "round", diameter_parameter: "width", system_classification: "NotARealSystem" }] } }), /system_classification/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...primitiveCases[1], path: { kind: "line", plane: "xy", points_mm: [point(0, 0, 0), point(0, 0, 0)] } }] } }), /zero-length segment/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...primitiveCases[2], end_profile: undefined }] } }), /end_profile must be an object/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...primitiveCases[3], path: { kind: "polyline", plane: "xy", points_mm: [point(0, 0, 0), point(100, 0, 0), point(200, 100, 0)] } }] } }), /requires one straight path segment/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...primitiveCases[0], profile_origin_mm: point(0, 0, 0) }] } }), /touches or crosses its axis/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...blueprint.parts[0], operation: "void" }] } }), /requires at least one cut_target/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...blueprint.parts[0], visibility_parameter: "width" }] } }), /must reference a yesno Family Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parts: [{ ...blueprint.parts[0], visibility_parameter: "missing_visibility" }] } }), /must reference a yesno Family Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...parametricVisibilityBlueprint, parts: [blueprint.parts[0], { key: "bad_void", primitive: "extrusion", operation: "void", axis: "x", role: "BadVoid", profile: { shape: "circle", diameter_mm: 120 }, start_mm: -50, end_mm: 500, cut_targets: ["body"], visibility_parameter: "show_geometry", visibility }] } }), /void geometry cannot declare visibility_parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...materialBlueprint, types: [{ name: "T-1", values: { width: 600, height: 900, depth: 450, finish: "missing_finish" } }] } }), /unknown material/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parameters: blueprint.parameters.map((parameter, index) => index === 0 ? { ...parameter, shared_guid: "not-a-guid" } : parameter) } }), /valid RFC 4122 GUID/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_pipe_accessory", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, family: { family_key: "custom_pipe_accessory", category: "pipe_accessory", template_behavior: "level_based", primary_axis: "x" } } }), /part_type is required/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "bad_pipe_accessory", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...fittingBlueprint, family: { ...fittingBlueprint.family, family_key: "bad_pipe_accessory", part_type: "transformer" } } }), /not supported for category pipe_accessory/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "bad_pipe_accessory", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...fittingBlueprint, family: { ...fittingBlueprint.family, family_key: "bad_pipe_accessory" }, connectors: [{ ...fittingBlueprint.connectors[0], linked_to: undefined }] } }), /requires exactly 2 connectors/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "bad_pipe_accessory", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...fittingBlueprint, family: { ...fittingBlueprint.family, family_key: "bad_pipe_accessory" }, connectors: fittingBlueprint.connectors.map((connector) => connector.key === "outlet" ? { ...connector, host_face: "start" } : connector) } }), /must use start and end faces/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "bad_pipe_accessory", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...fittingBlueprint, family: { ...fittingBlueprint.family, family_key: "bad_pipe_accessory" }, connectors: fittingBlueprint.connectors.map((connector) => ({ ...connector, primary: false })) } }), /requires exactly one primary connector/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "bad_pipe_accessory", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...fittingBlueprint, family: { ...fittingBlueprint.family, family_key: "bad_pipe_accessory" }, connectors: fittingBlueprint.connectors.map((connector) => connector.key === "inlet" ? { ...connector, linked_to: undefined } : connector) } }), /must be linked reciprocally/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "bad_pipe_accessory", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...fittingBlueprint, family: { ...fittingBlueprint.family, family_key: "bad_pipe_accessory" }, connectors: fittingBlueprint.connectors.map((connector) => ({ ...connector, system_classification: "DomesticColdWater" })) } }), /must share system_classification=Fitting or Global/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, parameters: blueprint.parameters.map((parameter, index) => index === 0 ? { ...parameter, visible: false } : parameter) } }), /requires shared_guid/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...advancedMaterialBlueprint, materials: [{ ...advancedMaterialBlueprint.materials[0], cut_foreground_pattern: { name: "Grid", target: "model", color_rgb: { r: 0, g: 0, b: 0 } } }] } }), /cut_foreground_pattern.target must be drafting/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...advancedMaterialBlueprint, materials: [{ ...advancedMaterialBlueprint.materials[0], physical_asset: { ...advancedMaterialBlueprint.materials[0].physical_asset, density_kg_per_m3: 0 } }] } }), /density_kg_per_m3 must be greater than 0/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...advancedMaterialBlueprint, materials: [{ ...advancedMaterialBlueprint.materials[0], thermal_asset: { ...advancedMaterialBlueprint.materials[0].thermal_asset, material_type: "liquid" } }] } }), /thermal_asset.material_type must be one of: solid/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...advancedMaterialBlueprint, materials: [{ ...advancedMaterialBlueprint.materials[0], thermal_asset: { ...advancedMaterialBlueprint.materials[0].thermal_asset, emissivity: 1.1 } }] } }), /thermal_asset.emissivity must be between 0 and 1/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "photometric_troffer", confirmed_fields: {}, blueprint: lightingBlueprint }), /approved_demo_directory is required/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "photometric_troffer", confirmed_fields: {}, approved_demo_directory: approved, blueprint: { ...lightingBlueprint, light_source: { ...lightingBlueprint.light_source, type_settings: lightingBlueprint.light_source.type_settings.map((item) => ({ ...item, photometric_web: { ...item.photometric_web, sha256: "0".repeat(64) } })) } } }), /checksum does not match/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "photometric_troffer", confirmed_fields: {}, approved_demo_directory: approved, blueprint: { ...lightingBlueprint, light_source: { ...lightingBlueprint.light_source, shape_style: "rectangle" } } }), /photometric_web requires shape_style=circle/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "photometric_troffer", confirmed_fields: {}, approved_demo_directory: approved, blueprint: { ...lightingBlueprint, light_source: { ...lightingBlueprint.light_source, type_settings: lightingBlueprint.light_source.type_settings.map((item) => ({ ...item, type_name: "Missing Type" })) } } }), /unknown Family type/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "spot_downlight", confirmed_fields: {}, blueprint: { ...spotLightingBlueprint, light_source: { ...spotLightingBlueprint.light_source, type_settings: spotLightingBlueprint.light_source.type_settings.map((item) => ({ ...item, spot: { ...item.spot, beam_angle_degrees: 60, field_angle_degrees: 45 } })) } } }), /field_angle_degrees must be greater than or equal/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "lighting_without_source", confirmed_fields: {}, blueprint: { ...lightingBlueprint, family: { ...lightingBlueprint.family, family_key: "lighting_without_source" }, light_source: undefined } }), /light_source is required for lighting_fixture/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, light_source: spotLightingBlueprint.light_source } }), /only valid for category lighting_fixture/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...lookupBlueprint, lookup_tables: [{ ...lookupBlueprint.lookup_tables[0], rows: [{ values: { width: 600, radius: 50 } }, { values: { width: 600, radius: 75 } }] }] } }), /duplicate lookup values/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...symbolicBlueprint, symbolic_lines: [{ ...symbolicBlueprint.symbolic_lines[0], points_mm: [point(0, 0, 0), point(0, 0, 0)] }] } }), /zero-length segment/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, model_lines: [{ key: "bad_model_line", role: "BadModelLine", plane: "xy", points_mm: [point(0, 0, 0), point(0, 0, 0)], visibility }] } }), /zero-length segment/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...modelLineBindingBlueprint, model_lines: [{ ...modelLineBindingBlueprint.model_lines[0], endpoint_bindings: [{ endpoint: "start", reference_plane_key: "missing_plane" }] }] } }), /references unknown Reference Plane missing_plane/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...modelLineBindingBlueprint, reference_planes: modelLineBindingBlueprint.reference_planes.map((item, index) => index === 0 ? { ...item, view_plane: "xz", cut_vector: point(0, 1, 0) } : item) } }), /must use the same xy view_plane/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...modelLineBindingBlueprint, model_lines: [{ ...modelLineBindingBlueprint.model_lines[0], points_mm: [point(-150, -250, 0), point(150, 300, 0)] }] } }), /start endpoint must lie on Reference Plane left/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...modelLineBindingBlueprint, model_lines: [{ ...modelLineBindingBlueprint.model_lines[0], endpoint_bindings: [{ endpoint: "start", reference_plane_key: "left" }, { endpoint: "start", reference_plane_key: "right" }] }] } }), /endpoint_bindings duplicates start/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...detailLevelRepresentationBlueprint, parts: blueprint.parts.map((part) => ({ ...part, visibility })) } }), /physical part body must be visible only at Fine detail level/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...detailLevelRepresentationBlueprint, model_lines: detailLevelRepresentationBlueprint.model_lines.map((line) => ({ ...line, visibility: { coarse: true, medium: true, fine: true } })) } }), new RegExp("model line flow_arrow must be visible at Coarse/Medium and hidden at Fine"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...detailLevelRepresentationBlueprint, detail_level_representations: [{ ...detailLevelRepresentationBlueprint.detail_level_representations[0], physical_part_keys: ["missing_part"] }] } }), /references unknown physical part missing_part/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...detailLevelRepresentationBlueprint, detail_level_representations: [{ ...detailLevelRepresentationBlueprint.detail_level_representations[0], symbolic_line_keys: [], model_line_keys: [] }] } }), /requires at least one symbolic_line_key or model_line_key/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...detailLevelRepresentationBlueprint, detail_level_representations: [detailLevelRepresentationBlueprint.detail_level_representations[0], { ...detailLevelRepresentationBlueprint.detail_level_representations[0], key: "duplicate_member_set", symbolic_line_keys: [], model_line_keys: ["flow_arrow"] }] } }), /physical part body cannot belong to more than one detail_level_representation/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...referenceGraphBlueprint, reference_planes: referenceGraphBlueprint.reference_planes.map((item, index) => index === 0 ? { ...item, cut_vector: point(1, 0, 0) } : item) } }), /cut_vector must be normal/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...referenceGraphBlueprint, reference_planes: referenceGraphBlueprint.reference_planes.map((item, index) => index === 0 ? { ...item, reference_type: "left" } : item) } }), /strength conflicts with reference_type/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...referenceGraphBlueprint, reference_planes: referenceGraphBlueprint.reference_planes.map((item, index) => index === 0 ? { ...item, strength: undefined } : item) } }), /must declare reference_type or legacy strength/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...referenceFrameworkBlueprint, reference_planes: referenceFrameworkBlueprint.reference_planes.map((item) => ({ ...item, reference_type: "left" })) } }), /duplicates named reference/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...referenceFrameworkBlueprint, reference_planes: referenceFrameworkBlueprint.reference_planes.map((item, index) => ({ ...item, defines_origin: index === 0 })) } }), /exactly two defines_origin planes/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...referenceFrameworkBlueprint, reference_planes: referenceFrameworkBlueprint.reference_planes.map((item, index) => index === 1 ? { ...item, bubble_end_mm: point(100, -1000, 0), free_end_mm: point(100, 1000, 0) } : item) } }), /must intersect and cannot have the same orientation/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...referenceFrameworkBlueprint, reference_plane_subcategories: [{ ...referenceFrameworkBlueprint.reference_plane_subcategories[0], color_rgb: { r: -1, g: 0, b: 0 } }] } }), /color_rgb.r must be an integer from 0 to 255/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...referenceFrameworkBlueprint, reference_plane_subcategories: [{ ...referenceFrameworkBlueprint.reference_plane_subcategories[0], projection_line_weight: 17 }] } }), /projection_line_weight must be an integer from 1 to 16/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...referenceFrameworkBlueprint, reference_plane_subcategories: [{ ...referenceFrameworkBlueprint.reference_plane_subcategories[0], line_pattern_name: "" }] } }), /line_pattern_name must be a non-empty string/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...referenceFrameworkBlueprint, reference_planes: referenceFrameworkBlueprint.reference_planes.map((item, index) => index === 0 ? { ...item, subcategory_key: "missing_style" } : item) } }), /unknown reference plane subcategory missing_style/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...referenceGraphBlueprint, dimensions: [{ ...referenceGraphBlueprint.dimensions[0], reference_keys: ["left", "missing"] }] } }), /unknown reference missing/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...referenceGraphBlueprint, dimensions: [{ ...referenceGraphBlueprint.dimensions[0], parameter_key: undefined, equality: true }] } }), /requires at least three references/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...referenceGraphBlueprint, dimensions: [{ ...referenceGraphBlueprint.dimensions[0], equality: true }] } }), /exactly one parameter_key or equality=true/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...radialGraphBlueprint, parts: [{ ...radialGraphBlueprint.parts[0], profile: { shape: "rectangle", width_mm: 200, height_mm: 200 } }] } }), /must reference a circular or ring extrusion/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...radialGraphBlueprint, dimensions: [{ ...radialGraphBlueprint.dimensions[0], profile_loop_index: 1 }] } }), /outside the declared profile loops/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularGraphBlueprint, dimensions: [{ ...angularGraphBlueprint.dimensions[0], reference_keys: ["axis_x", "missing_line"] }] } }), /exactly two declared Reference Lines/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularGraphBlueprint, dimensions: [{ ...angularGraphBlueprint.dimensions[0], arc_radius_mm: 0 }] } }), /arc_radius_mm must be greater than zero/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularGraphBlueprint, parameters: [{ ...angularGraphBlueprint.parameters[0], data_type: "length" }] } }), /must reference a direct angle Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularSweepBlueprint, parts: [{ ...angularSweepBlueprint.parts[0], path: { ...angularSweepBlueprint.parts[0].path, reference_line_key: "missing_line" } }] } }), /references unknown Reference Line/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularSweepBlueprint, dimensions: [{ ...angularSweepBlueprint.dimensions[0], reference_keys: ["axis_angle", "axis_x"] }] } }), /must list its fixed baseline first/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularSweepBlueprint, reference_lines: [{ ...angularSweepBlueprint.reference_lines[0], start_mm: point(20, 0, 0) }, angularSweepBlueprint.reference_lines[1]] } }), /must share one pivot/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularSweepBlueprint, reference_lines: [{ ...angularSweepBlueprint.reference_lines[0], end_mm: point(0, 500, 0) }, angularSweepBlueprint.reference_lines[1]] } }), /fixed baseline must follow the positive primary axis/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularSweepBlueprint, dimensions: [{ ...angularSweepBlueprint.dimensions[0], start_angle_degrees: 5, end_angle_degrees: 50 }] } }), /requires start_angle_degrees=0/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularSweepBlueprint, verification: { parameter_flex_cases: [{ parameter_key: "opening_angle", min: 0, nominal: 45, max: 75 }] } } }), /flex range must stay strictly between 0 and 180/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularParameterizedSweepBlueprint, parts: [{ ...angularParameterizedSweepBlueprint.parts[0], profile_location: "midpoint" }] } }), /parameterized reference_line sweep profile requires profile_location=start/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularParameterizedSweepBlueprint, parameters: angularParameterizedSweepBlueprint.parameters.map((parameter) => parameter.key === "sweep_diameter" ? { ...parameter, scope: "instance" } : parameter) } }), /must reference a direct Length Type Parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularSweepBlueprint, parts: [{ ...angularSweepBlueprint.parts[0], operation: "void", cut_targets: ["anchor_body"] }] } }), /reference_line sweep must be solid/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...angularSweepBlueprint, connectors: [{ key: "unsupported_angular_connector", discipline: "pipe", host_part: "rotating_sweep" }] } }), /cannot use an angular reference_line sweep/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600 }, blueprint: { ...referenceGraphBlueprint, verification: { parameter_flex_cases: [] } } }), /requires verification.parameter_flex_cases/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...arrayBlueprint, arrays: [{ ...arrayBlueprint.arrays[0], count: 4 }] } }), /exactly one of count or count_parameter/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...arrayBlueprint, connectors: [{ key: "supply", discipline: "duct", role: "supply", host_part: "blade", host_face: "end", profile: "round", diameter_parameter: "blade_count", system_classification: "SupplyAir" }] } }), /must reference a length parameter|without join\/cut, connector or alignment/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: {}, blueprint: { ...arrayBlueprint, mirrors: [{ key: "mirror_blade", target_kind: "part", target_keys: ["blade"], plane_origin_mm: point(0, 0, 0), plane_normal: point(0, 1, 0), copy: true }] } }), /independent of join\/cut, connector, alignment and array operations/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...nestedBlueprint, mirrors: [{ ...nestedBlueprint.mirrors[0], plane_normal: point(0, 0, 0) }] } }), /plane_normal must be non-zero/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...fixedNestedArrayBlueprint, arrays: [{ ...fixedNestedArrayBlueprint.arrays[0], member_nested_component_keys: ["missing_child"] }] } }), /unknown nested component missing_child/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...fixedNestedArrayBlueprint, arrays: [fixedNestedArrayBlueprint.arrays[0], { ...fixedNestedArrayBlueprint.arrays[0], key: "second_child_array" }] } }), /cannot belong to more than one array/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...interchangeableNestedBlueprint, arrays: [{ key: "swappable_array", member_nested_component_keys: ["swappable_child"], view_plane: "xy", count: 3, direction_mm: point(0, 250, 0), anchor: "second" }] } }), /cannot contain interchangeable nested component/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_nested_parent", confirmed_fields: {}, blueprint: { ...fixedNestedArrayBlueprint, mirrors: [{ key: "mirror_array_child", target_kind: "nested_component", target_keys: ["child"], plane_origin_mm: point(0, 0, 0), plane_normal: point(0, 1, 0), copy: true }] } }), /belongs to an array/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_detail_item", confirmed_fields: {}, blueprint: { ...detailAndFillBlueprint, filled_regions: [{ ...detailAndFillBlueprint.filled_regions[0], boundary_loops: [{ points_mm: [point(-80, -40, 0), point(80, 40, 0), point(-80, 40, 0), point(80, -40, 0)] }] }] } }), /self-intersects/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_detail_item", confirmed_fields: {}, blueprint: { ...parameterizedDetailLineBlueprint, detail_lines: [{ ...parameterizedDetailLineBlueprint.detail_lines[0], endpoint_bindings: [{ endpoint: "start", reference_plane_key: "missing_plane" }] }] } }), /references unknown Reference Plane missing_plane/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_detail_item", confirmed_fields: {}, blueprint: { ...parameterizedDetailLineBlueprint, detail_lines: [{ ...parameterizedDetailLineBlueprint.detail_lines[0], points_mm: [point(-250, 0, 0), point(300, 0, 0)] }] } }), /start endpoint must lie on Reference Plane flex_start/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_annotation", confirmed_fields: {}, blueprint: { ...parameterizedDetailLineBlueprint, family: { family_key: "custom_annotation", category: "annotation", template_behavior: "annotation", primary_axis: "x" } } }), /endpoint_bindings require template_behavior=detail_item/);
const invalid3dDetailSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, detail_lines: detailAndFillBlueprint.detail_lines } });
assert.equal(invalid3dDetailSpec.status, "requires_capability_or_ui_fallback");
assert.ok(invalid3dDetailSpec.blueprint_assessment.unsupported_features.includes("detail_or_filled_region_requires_2d_template"));
const invalidModelLineDetailSpec = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_detail_item", confirmed_fields: {}, blueprint: { ...detailItemBlueprint, model_lines: parametricVisibilityBlueprint.model_lines.map((line) => ({ ...line, visibility_parameter: undefined })) } });
assert.equal(invalidModelLineDetailSpec.status, "requires_capability_or_ui_fallback");
assert.ok(invalidModelLineDetailSpec.blueprint_assessment.unsupported_features.includes("model_lines_require_model_family_template"));
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tag", confirmed_fields: {}, blueprint: { ...detailItemBlueprint, family: { family_key: "custom_tag", category: "tag", template_behavior: "tag", primary_axis: "x" }, ui_fallbacks: [{ action: "create_dynamic_label", reason: "Public API does not expose the required label composition.", requires_human_checkpoint: true }] } }), /requires at least one tag_labels declaration/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_profile", confirmed_fields: {}, blueprint: { ...profileBlueprint, profile_loops: [{ ...profileBlueprint.profile_loops[0], points_mm: [point(-100, -50, 0), point(100, 50, 0), point(-100, 50, 0), point(100, -50, 0)] }] } }), /self-intersects/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, verification: { parameter_flex_cases: blueprint.verification.parameter_flex_cases.filter((item) => item.parameter_key !== "width") } } }), /requires verification.parameter_flex_cases/);
assert.throws(() => previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "custom_tank", confirmed_fields: { width_mm: 600, height_mm: 900, depth_mm: 450 }, blueprint: { ...blueprint, verification: { parameter_flex_cases: blueprint.verification.parameter_flex_cases.map((item) => item.parameter_key === "width" ? { ...item, min: 700 } : item) } } }), /min < nominal < max/);

await assert.rejects(
  inspectFamilySource({ source_path: nativePdf, source_kind: "pdf", approved_demo_directory: path.join(root, "outside") }),
  (error) => error instanceof FamilyEvidenceError && error.code === "PathBlocked",
);
await assert.rejects(
  inspectFamilySource({ source_path: "\\\\server\\catalog.pdf", source_kind: "pdf", approved_demo_directory: approved }),
  (error) => error instanceof FamilyEvidenceError && error.code === "PathBlocked",
);
const ocrAssets = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "MCP-Server", "assets", "ocr", "eng.traineddata.gz");
if (existsSync(ocrAssets)) {
  const scanned = await inspectFamilySource({ source_path: scannedPdf, source_kind: "pdf", approved_demo_directory: approved });
  assert.equal(scanned.pages[0].extraction_method, "rendered_page_ocr");
} else {
  await assert.rejects(
    inspectFamilySource({ source_path: scannedPdf, source_kind: "pdf", approved_demo_directory: approved }),
    (error) => error instanceof FamilyEvidenceError && error.code === "OcrUnavailable",
  );
}

console.log("PASS Family Evidence v3: PDF/image/DXF/DWG evidence, native text/bbox/checksum/spec, path guards and OCR/offline-asset guard");
