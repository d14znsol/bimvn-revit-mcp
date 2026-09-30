import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";

const root = await mkdtemp(path.join(os.tmpdir(), "dscons-mepf-governance-"));
const approved = path.join(root, "approved");
await mkdir(approved);
process.env.DSCONS_FAMILY_RECORD_DIRECTORY = path.join(root, "records");

const { inspectCadGeometry, previewFamilySpec } = await import("../MCP-Server/build/family-evidence.js");
const { previewSourceToRevit } = await import("../MCP-Server/build/source-to-revit.js");
const {
  assessCadAnnotations, assessFamilyCompatibility, assessMepfEvidenceReadiness, assessSourceConflicts,
  inspectManufacturerCatalog, reviewMepfEngineering,
} = await import("../MCP-Server/build/mepf-evidence-governance.js");

const dxfPath = path.join(approved, "annotation-source.dxf");
await writeFile(dxfPath, [
  "0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", "4", "0", "ENDSEC",
  "0", "SECTION", "2", "ENTITIES",
  "0", "LINE", "5", "10", "8", "PIPE-CENTER", "10", "0", "20", "0", "30", "0", "11", "1000", "21", "0", "31", "0",
  "0", "TEXT", "5", "20", "8", "ANNO", "10", "300", "20", "100", "30", "0", "40", "25", "1", "CHWS DN100",
  "0", "LEADER", "5", "30", "8", "ANNO", "10", "300", "20", "100", "30", "0", "10", "500", "20", "0", "30", "0",
  "0", "ENDSEC", "0", "EOF", "",
].join("\n"), "utf8");
const cad = await inspectCadGeometry({ source_path: dxfPath, approved_demo_directory: approved });
assert.equal(cad.geometry.entities.filter((entity) => entity.type === "TEXT")[0].text, "CHWS DN100");
assert.equal(cad.geometry.entities.filter((entity) => entity.type === "LEADER")[0].mapping_status, "explicit_engineer_target_required");

const incomplete = assessMepfEvidenceReadiness({ target_kind: "route", mapping: "route_centerline", evidence_ids: [cad.evidence_id], facts: { source_revision: true } });
assert.equal(incomplete.status, "needs_information");
assert.ok(incomplete.questions.some((question) => question.field === "nominal_size"));
const readiness = assessMepfEvidenceReadiness({
  target_kind: "route", mapping: "route_centerline", evidence_ids: [cad.evidence_id],
  facts: { source_revision: true, source_locator: true, units_scale: true, discipline_service: true, revit_type: true, level_elevation: true, nominal_size: true, routing_geometry: true },
});
assert.equal(readiness.status, "ready_for_proposal");
const missingGate = previewSourceToRevit({ evidence_ids: [cad.evidence_id], mapping: "route_centerline", layers: ["PIPE-CENTER"], route: { kind: "pipe", type_id: 1, system_type_id: 2, level_id: 3, elevation_mm: 2700, tolerance_mm: 1 } });
assert.equal(missingGate.status, "requires_information_readiness");
const gatedRoute = previewSourceToRevit({ evidence_ids: [cad.evidence_id], mapping: "route_centerline", readiness_record_id: readiness.record_id, layers: ["PIPE-CENTER"], route: { kind: "pipe", type_id: 1, system_type_id: 2, level_id: 3, elevation_mm: 2700, tolerance_mm: 1 } });
assert.equal(gatedRoute.status, "ready_for_fresh_revit_preview");

const annotation = assessCadAnnotations({ evidence_id: cad.evidence_id, required_metadata: ["text", "leader"], associations: [{ annotation_handle: "20", leader_handle: "30", target_entity_handle: "10", semantics: { service: "CHWS", nominal_size: "DN100" }, engineer_confirmed: true }] });
assert.equal(annotation.status, "assessed");
assert.equal(annotation.associations[0].target_entity_handle, "10");
const unavailableAnnotation = assessCadAnnotations({ evidence_id: cad.evidence_id, required_metadata: ["mleader"] });
assert.equal(unavailableAnnotation.status, "requires_trusted_adapter");
assert.throws(() => assessCadAnnotations({ evidence_id: cad.evidence_id, associations: [{ annotation_handle: "20", target_entity_handle: "999", semantics: {}, engineer_confirmed: true }] }), /absent from immutable CAD evidence/);

const catalogPath = path.join(approved, "pipe-fitting.csv");
await writeFile(catalogPath, "DN,CenterToEnd,PressureClass\n50,75,16\n100,125,16\n", "utf8");
const catalog = inspectManufacturerCatalog({
  source_path: catalogPath, approved_catalog_directory: approved, catalogue_name: "Pipe elbows", manufacturer: "Example MEP", revision: "R1",
  lookup_key_columns: ["DN"], columns: [
    { source_column: "DN", data_type: "length", unit: "mm", minimum: 1 },
    { source_column: "CenterToEnd", data_type: "length", unit: "mm", minimum: 1 },
    { source_column: "PressureClass", data_type: "number", minimum: 0 },
  ],
  lookup_table: { key: "manufacturer_sizes", name: "Manufacturer sizes", lookup_key_columns: ["DN"], column_keys: { DN: "dn", CenterToEnd: "cte", PressureClass: "pressure" } },
});
assert.equal(catalog.status, "validated");
assert.equal(catalog.lookup_table_draft.rows.length, 2);
assert.equal(catalog.lookup_table_draft.catalogue_provenance.revision, "R1");
const lookupBlueprint = {
  schema_version: "3.0", target_lod: "LOD_300",
  family: { family_key: "catalogue_lookup_test", category: "generic_model", template_behavior: "level_based", primary_axis: "x" },
  required_source_fields: [], parameters: [], types: [{ name: "Default", values: {} }], parts: [], connectors: [], nested_components: [],
  lookup_tables: [catalog.lookup_table_draft], symbolic_lines: [], model_lines: [], detail_lines: [], profile_loops: [], ui_fallbacks: [],
};
const missingFamilyReadiness = previewFamilySpec({ evidence_id: cad.evidence_id, family_kind: "catalogue_lookup_test", confirmed_fields: {}, blueprint: lookupBlueprint });
assert.equal(missingFamilyReadiness.status, "requires_information_readiness");
assert.equal(missingFamilyReadiness.evidence_governance.readiness.status, "needs_information");
const familyReadiness = assessMepfEvidenceReadiness({
  target_kind: "family", mapping: "family_blueprint", evidence_ids: [cad.evidence_id],
  facts: {
    source_revision: true, source_locator: true, units_scale: true, target_family_category: true,
    family_source_decision: { outcome: "approved_blueprint", engineer_confirmed: true },
  },
});
assert.equal(familyReadiness.status, "ready_for_proposal");
const lookupSpec = previewFamilySpec({ evidence_id: cad.evidence_id, readiness_record_id: familyReadiness.record_id, family_kind: "catalogue_lookup_test", confirmed_fields: {}, blueprint: lookupBlueprint });
assert.equal(lookupSpec.status, "human_confirmed");
assert.equal(lookupSpec.manufacturer_catalogue_citations[0].catalog_record_id, catalog.record_id);
assert.equal(lookupSpec.evidence_governance.readiness.readiness_record_id, familyReadiness.record_id);
assert.throws(() => previewFamilySpec({ evidence_id: cad.evidence_id, readiness_record_id: readiness.record_id, family_kind: "catalogue_lookup_test", confirmed_fields: {}, blueprint: lookupBlueprint }), /Readiness record does not match/);
const tamperedLookupBlueprint = structuredClone(lookupBlueprint);
tamperedLookupBlueprint.lookup_tables[0].rows[0].values.cte = 999;
assert.throws(() => previewFamilySpec({ evidence_id: cad.evidence_id, readiness_record_id: familyReadiness.record_id, family_kind: "catalogue_lookup_test", confirmed_fields: {}, blueprint: tamperedLookupBlueprint }), /differs from the approved manufacturer catalogue mapping/);
await writeFile(catalogPath, "DN,CenterToEnd,PressureClass\n50,80,16\n100,125,16\n", "utf8");
const catalogR2 = inspectManufacturerCatalog({
  source_path: catalogPath, approved_catalog_directory: approved, catalogue_name: "Pipe elbows", manufacturer: "Example MEP", revision: "R2", previous_catalog_record_id: catalog.record_id,
  lookup_key_columns: ["DN"], columns: [{ source_column: "DN", data_type: "length", unit: "mm" }, { source_column: "CenterToEnd", data_type: "length", unit: "mm" }, { source_column: "PressureClass", data_type: "number" }],
});
assert.equal(catalogR2.revision_impact.status, "revision_changed");
await writeFile(path.join(approved, "duplicate.csv"), "DN,CenterToEnd\n50,75\n50,80\n", "utf8");
assert.throws(() => inspectManufacturerCatalog({ source_path: path.join(approved, "duplicate.csv"), approved_catalog_directory: approved, catalogue_name: "bad", manufacturer: "Example", revision: "R1", lookup_key_columns: ["DN"], columns: [{ source_column: "DN", data_type: "length", unit: "mm" }, { source_column: "CenterToEnd", data_type: "length", unit: "mm" }] }), /duplicate lookup key tuple/);

const profileReadiness = assessMepfEvidenceReadiness({ target_kind: "family", mapping: "family_profile", evidence_ids: [cad.evidence_id], facts: { source_revision: true, source_locator: true, units_scale: true, target_family_category: true, family_source_decision: { outcome: "approved_blueprint", engineer_confirmed: true } } });
const profile = previewSourceToRevit({ evidence_ids: [cad.evidence_id], mapping: "family_profile", readiness_record_id: profileReadiness.record_id, layers: ["PIPE-CENTER"], plane: "xy" });
assert.equal(profile.status, "requires_engineering_review");

const compatible = assessFamilyCompatibility({ source_family: { evidence: { kind: "family_inspect", verified_revit_readback: true }, category: "pipe_fitting", placement: "OneLevelBased", part_type: "elbow", routing_intent: "pipe_elbow", parameters: [{ name: "Nominal Diameter", data_type: "length", scope: "instance", shared_guid: null }], connectors: [{ domain: "piping", shape: "round", direction: "in", system_classification: "HydronicSupply" }], nested_dependencies: [] }, target_family: { evidence: { kind: "family_inspect", verified_revit_readback: true }, category: "pipe_fitting", placement: "OneLevelBased", part_type: "elbow", routing_intent: "pipe_elbow", parameters: [{ name: "Nominal Diameter", data_type: "length", scope: "instance", shared_guid: null }], connectors: [{ domain: "piping", shape: "round", direction: "in", system_classification: "HydronicSupply" }], nested_dependencies: [] } });
assert.equal(compatible.status, "compatible");
const incompatible = assessFamilyCompatibility({ source_family: { evidence: { kind: "family_inspect", verified_revit_readback: true }, category: "pipe_fitting", placement: "OneLevelBased", parameters: [], connectors: [], nested_dependencies: [] }, target_family: { evidence: { kind: "family_inspect", verified_revit_readback: true }, category: "pipe_fitting", placement: "WorkPlaneBased", parameters: [], connectors: [], nested_dependencies: [] } });
assert.equal(incompatible.status, "blocked");

const plan = { plan_sha256: "f".repeat(64), routes: [{ name: "pipe_01", kind: "pipe", discipline: "mechanical", service: "CHWS", type_id: 1, system_type_id: 2, level_id: 3, diameter_mm: 100, points: [{ x_mm: 0, y_mm: 0, z_mm: 2700 }, { x_mm: 1000, y_mm: 0, z_mm: 2700 }] }], equipment: [] };
const engineering = reviewMepfEngineering({ reconstruction_plan: plan, calculation_boundary_acknowledged: true });
assert.equal(engineering.status, "approved_for_preview");
assert.equal(reviewMepfEngineering({ reconstruction_plan: { ...plan, routes: [{ ...plan.routes[0], slope_percent: 1 }] }, calculation_boundary_acknowledged: true }).status, "needs_information");

const secondPath = path.join(approved, "second-source.dxf");
await writeFile(secondPath, ["0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", "4", "0", "ENDSEC", "0", "SECTION", "2", "ENTITIES", "0", "LINE", "5", "40", "8", "PIPE-CENTER", "10", "0", "20", "0", "30", "0", "11", "1", "21", "0", "31", "0", "0", "ENDSEC", "0", "EOF", ""].join("\n"), "utf8");
const cad2 = await inspectCadGeometry({ source_path: secondPath, approved_demo_directory: approved });
const conflict = assessSourceConflicts({ sources: [{ evidence_id: cad.evidence_id, common_datum_key: "L1-origin", facts: { nominal_size: "DN100" } }, { evidence_id: cad2.evidence_id, common_datum_key: "L1-origin", facts: { nominal_size: "DN80" } }] });
assert.equal(conflict.status, "needs_engineer_resolution");
const resolved = assessSourceConflicts({ sources: [{ evidence_id: cad.evidence_id, common_datum_key: "L1-origin", facts: { nominal_size: "DN100" } }, { evidence_id: cad2.evidence_id, common_datum_key: "L1-origin", facts: { nominal_size: "DN80" } }], resolutions: [{ key: "nominal_size", chosen_evidence_id: cad.evidence_id, engineer_confirmed: true }] });
assert.equal(resolved.status, "aligned_for_proposal");

console.log("PASS MEPF evidence governance: readiness, CAD semantics, manufacturer catalogue, Family contract, engineering boundary and source conflict gates");
