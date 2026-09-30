import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";

const records = process.env.DSCONS_MODEL_TRANSFER_RECORD_DIRECTORY ?? mkdtempSync(path.join(os.tmpdir(), "dscons-transfer-"));
process.env.DSCONS_MODEL_TRANSFER_RECORD_DIRECTORY = records;
const transfer = await import("../MCP-Server/build/model-transfer.js");
if (process.argv[2] === "--tampered") {
  const [, , , packageId, catalogId] = process.argv;
  assert.throws(() => transfer.planModelTransfer({ package_id: packageId, catalog_id: catalogId, staged_project_confirmed: true, mappings: [] }), /checksum-valid/);
  process.exit(0);
}
const fail = (action, fragment) => assert.throws(action, (error) => error?.message?.includes(fragment));
const clone = (value) => JSON.parse(JSON.stringify(value));

const packageBase = () => ({ schema_version: "1.1", record_kind: "model_transfer_package_v1", model_changed: false,
  source: { revit_version: "2025", document_fingerprint: "source-fingerprint", document_revision: 2, modified_at_extract: false, snapshot_stable: true, coordinate_transform: { is_identity: true } },
  resources: { levels: [{ source_key: "level_1", kind: "level", elevation_mm: 0 }], route_types: [{ source_key: "route_type_2", kind: "route_type", route_kind: "pipe" }], system_types: [{ source_key: "system_type_3", kind: "system_type", route_kind: "pipe", system_classification: "SupplyHydronic" }], family_symbols: [{ source_key: "family_symbol_4", kind: "family_symbol", category: "mechanical_equipment", placement_type: "OneLevelBased" }] },
  elements: [{ source_key: "route_10", kind: "route", route_kind: "pipe", type_source_key: "route_type_2", system_source_key: "system_type_3", level_source_key: "level_1", diameter_mm: 100, points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 0 }, { x_mm: 1000, y_mm: 0, z_mm: 0 }] }],
  physical_connections: [], dependency_groups: [{ group_key: "network_0001", element_keys: ["route_10"], physical_connection_count: 0 }],
});
const catalogBase = () => ({ schema_version: "1.1", record_kind: "model_transfer_destination_catalog_v1",
  destination: { revit_version: "2023", document_fingerprint: "target-fingerprint", document_revision: 1 },
  resources: [{ destination_id: 101, kind: "level", elevation_mm: 0 }, { destination_id: 102, kind: "route_type", route_kind: "pipe" }, { destination_id: 103, kind: "system_type", route_kind: "pipe", system_classification: "SupplyHydronic" }, { destination_id: 104, kind: "family_symbol", category: "mechanical_equipment", placement_type: "OneLevelBased" }], model_changed: false,
});
const mappings = [{ source_key: "level_1", kind: "level", destination_id: 101 }, { source_key: "route_type_2", kind: "route_type", destination_id: 102 }, { source_key: "system_type_3", kind: "system_type", destination_id: 103 }];
function recordsFor(pkg = packageBase(), catalog = catalogBase()) { return { pkg: transfer.recordModelTransferPackage(pkg), catalog: transfer.recordModelTransferDestinationCatalog(catalog) }; }
function planFor(recordsValue, extra = {}) { return transfer.planModelTransfer({ package_id: recordsValue.pkg.package_id, catalog_id: recordsValue.catalog.catalog_id, staged_project_confirmed: true, mappings, ...extra }); }
const physicalConnector = (origin, direction, connectedElementIds = []) => ({
  domain: "Piping",
  profile: "Round",
  connector_type: "End",
  origin,
  direction,
  radius_mm: 50,
  width_mm: null,
  height_mm: null,
  is_connected: connectedElementIds.length > 0,
  connected_element_ids: connectedElementIds,
});
const elbowPackageBase = () => {
  const value = packageBase();
  const route10Connected = physicalConnector({ x_mm: 900, y_mm: 0, z_mm: 0 }, { x_mm: 1, y_mm: 0, z_mm: 0 }, [20]);
  const elbowAtRoute10 = physicalConnector({ x_mm: 900, y_mm: 0, z_mm: 0 }, { x_mm: -1, y_mm: 0, z_mm: 0 }, [10]);
  const elbowAtRoute11 = physicalConnector({ x_mm: 1000, y_mm: 100, z_mm: 0 }, { x_mm: 0, y_mm: 1, z_mm: 0 }, [11]);
  const route11Connected = physicalConnector({ x_mm: 1000, y_mm: 100, z_mm: 0 }, { x_mm: 0, y_mm: -1, z_mm: 0 }, [20]);
  value.resources.family_symbols.push({ source_key: "family_symbol_elbow", kind: "family_symbol", category: "pipe_fitting", part_type: "elbow", route_kind: "pipe" });
  value.elements = [
    { ...clone(value.elements[0]), source_key: "route_10", points_mm: [{ x_mm: 0, y_mm: 0, z_mm: 0 }, { x_mm: 900, y_mm: 0, z_mm: 0 }] },
    { ...clone(value.elements[0]), source_key: "route_11", points_mm: [{ x_mm: 1000, y_mm: 100, z_mm: 0 }, { x_mm: 1000, y_mm: 1000, z_mm: 0 }] },
    {
      source_key: "fitting_20",
      kind: "fitting",
      fitting_role: "elbow",
      route_kind: "pipe",
      symbol_source_key: "family_symbol_elbow",
      connector_signature: [clone(elbowAtRoute10), clone(elbowAtRoute11)],
      native_detail: { bounding_box: { min: { x_mm: 900, y_mm: 0, z_mm: -50 }, max: { x_mm: 1000, y_mm: 100, z_mm: 50 } } },
    },
  ];
  value.physical_connections = [
    { connection_kind: "physical", from: { element_key: "route_10", connector_index: 1 }, to: { element_key: "fitting_20", connector_index: 0 }, from_connector: route10Connected, to_connector: elbowAtRoute10 },
    { connection_kind: "physical", from: { element_key: "fitting_20", connector_index: 1 }, to: { element_key: "route_11", connector_index: 0 }, from_connector: elbowAtRoute11, to_connector: route11Connected },
  ];
  value.dependency_groups = [{ group_key: "network_elbow", element_keys: ["route_10", "route_11", "fitting_20"], physical_connection_count: 2 }];
  return value;
};
const elbowCatalogBase = () => {
  const value = catalogBase();
  value.resources.push({ destination_id: 105, kind: "family_symbol", category: "pipe_fitting", part_type: "elbow", route_kind: "pipe" });
  return value;
};
const elbowMappings = [...mappings, { source_key: "family_symbol_elbow", kind: "family_symbol", destination_id: 105 }];
function elbowPlanFor(pkg = elbowPackageBase(), catalog = elbowCatalogBase(), selectedMappings = elbowMappings) {
  return planFor(recordsFor(pkg, catalog), { mappings: selectedMappings });
}

const recordsValue = recordsFor(); const plan = planFor(recordsValue);
const renamedGroupPackage = packageBase(); renamedGroupPackage.dependency_groups[0].group_key = "network_renamed"; assert.notEqual(transfer.recordModelTransferPackage(renamedGroupPackage).package_id, recordsValue.pkg.package_id, "dependency-group identity must participate in package_id");
const legacyPackage = packageBase(); legacyPackage.schema_version = "1.0"; fail(() => transfer.recordModelTransferPackage(legacyPackage), "v1.1");
assert.equal(plan.ready_groups.length, 1); assert.equal(plan.blocked_groups.length, 0); assert.equal(plan.changeset_template.operations[0].arguments.routes[0].type_id, 102);
assert.match(plan.changeset_template.operations[0].arguments.routes[0].provenance_marker, new RegExp(recordsValue.pkg.package_id));
assert.equal(transfer.prepareModelTransferPreview({ plan_id: plan.plan_id, context_id: "fresh-context", destination_staging_confirmed: true }).bridge_arguments.operations.length, 1);

const wrongRoute = recordsFor(); fail(() => planFor(wrongRoute, { mappings: mappings.map((item) => item.destination_id === 102 ? { ...item, destination_id: 103 } : item) }), "kind route_type does not match");
const wrongKindCatalog = catalogBase(); wrongKindCatalog.resources[1].route_kind = "duct"; const wrongKind = recordsFor(packageBase(), wrongKindCatalog); fail(() => planFor(wrongKind), "No complete independent dependency group");
const wrongLevelCatalog = catalogBase(); wrongLevelCatalog.resources[0].elevation_mm = 100; const wrongLevel = recordsFor(packageBase(), wrongLevelCatalog); fail(() => planFor(wrongLevel), "No complete independent dependency group");
const wrongSystemCatalog = catalogBase(); wrongSystemCatalog.resources[2].system_classification = "ReturnHydronic"; const wrongSystem = recordsFor(packageBase(), wrongSystemCatalog); fail(() => planFor(wrongSystem), "No complete independent dependency group");
const missing = recordsFor(); fail(() => planFor(missing, { mappings: mappings.slice(1) }), "No complete independent dependency group");
const connected = packageBase(); connected.elements.push({ ...clone(connected.elements[0]), source_key: "route_11", points_mm: [{ x_mm: 1000, y_mm: 0, z_mm: 0 }, { x_mm: 2000, y_mm: 0, z_mm: 0 }] }); connected.physical_connections.push({ connection_kind: "physical", from: { element_key: "route_10", connector_index: 1 }, to: { element_key: "route_11", connector_index: 0 } }); connected.dependency_groups[0].element_keys.push("route_11"); connected.dependency_groups[0].physical_connection_count = 1; const connectedRecords = recordsFor(connected); fail(() => planFor(connectedRecords), "No complete independent dependency group");
const external = packageBase(); external.elements[0].external_physical_connection_count = 1; const externalRecords = recordsFor(external); fail(() => planFor(externalRecords), "No complete independent dependency group");
const diagonal = packageBase(); diagonal.elements[0].points_mm[1].y_mm = 100; const diagonalRecords = recordsFor(diagonal); fail(() => planFor(diagonalRecords), "No complete independent dependency group");
const vertical = packageBase(); vertical.elements[0].points_mm[1] = { x_mm: 0, y_mm: 0, z_mm: 1000 }; const verticalRecords = recordsFor(vertical); fail(() => planFor(verticalRecords), "No complete independent dependency group");
const multiSegment = packageBase(); multiSegment.elements[0].points_mm.push({ x_mm: 2000, y_mm: 0, z_mm: 0 }); const multiSegmentRecords = recordsFor(multiSegment); fail(() => planFor(multiSegmentRecords), "No complete independent dependency group");
const missingSize = packageBase(); delete missingSize.elements[0].diameter_mm; const missingSizeRecords = recordsFor(missingSize); fail(() => planFor(missingSizeRecords), "No complete independent dependency group");
const transformed = packageBase(); transformed.source.coordinate_transform.is_identity = false; const transformedRecords = recordsFor(transformed); fail(() => planFor(transformedRecords), "Non-identity shared-coordinate");
const tolerance = recordsFor(); fail(() => planFor(tolerance, { tolerance_mm: 0.10001 }), "cannot exceed 0.1 mm");
const topologyMismatch = packageBase(); topologyMismatch.dependency_groups[0].physical_connection_count = 1; fail(() => transfer.recordModelTransferPackage(topologyMismatch), "declares 1 physical edges");
const duplicateMembership = packageBase(); duplicateMembership.dependency_groups.push({ group_key: "network_0002", element_keys: ["route_10"], physical_connection_count: 0 }); fail(() => transfer.recordModelTransferPackage(duplicateMembership), "more than one dependency group");
const changedCatalog = catalogBase(); changedCatalog.model_changed = true; fail(() => transfer.recordModelTransferDestinationCatalog(changedCatalog), "read-only snapshot");
const equipment = packageBase(); equipment.elements = [{ source_key: "family_10", kind: "equipment", category: "mechanical_equipment", placement_type: "OneLevelBased", symbol_source_key: "family_symbol_4", level_source_key: "level_1", point_mm: { x_mm: 0, y_mm: 0, z_mm: 0 }, rotation_degrees: 0, connector_signature: [] }]; equipment.dependency_groups[0].element_keys = ["family_10"]; const equipmentRecords = recordsFor(equipment); fail(() => planFor(equipmentRecords, { mappings: [...mappings, { source_key: "family_symbol_4", kind: "family_symbol", destination_id: 104 }] }), "No complete independent dependency group");

const elbowPlan = elbowPlanFor();
const elbowRoute = elbowPlan.ready_groups[0].routes[0];
assert.equal(elbowRoute.transfer_shape, "orthogonal_elbow_chain");
assert.deepEqual(elbowRoute.points, [{ x_mm: 0, y_mm: 0, z_mm: 0 }, { x_mm: 1000, y_mm: 0, z_mm: 0 }, { x_mm: 1000, y_mm: 1000, z_mm: 0 }]);
assert.equal(elbowRoute.source_segments.length, 2);
assert.equal(elbowRoute.source_fitting.type_id, 105);
assert.equal(elbowPlan.changeset_template.operations[0].arguments.routes.length, 1, "one dependency group must be one atomic logical route operation");

for (const role of ["tee", "union", "transition"]) {
  const unsupported = elbowPackageBase();
  unsupported.elements[2].fitting_role = role;
  fail(() => elbowPlanFor(unsupported), "No complete independent dependency group");
}
const tooManyLegs = elbowPackageBase();
tooManyLegs.elements.push({ ...clone(tooManyLegs.elements[0]), source_key: "route_12", points_mm: [{ x_mm: 1000, y_mm: -100, z_mm: 0 }, { x_mm: 1000, y_mm: -1000, z_mm: 0 }] });
tooManyLegs.physical_connections.push({ ...clone(tooManyLegs.physical_connections[0]), from: { element_key: "route_12", connector_index: 1 } });
tooManyLegs.dependency_groups[0].element_keys.push("route_12"); tooManyLegs.dependency_groups[0].physical_connection_count = 3;
fail(() => elbowPlanFor(tooManyLegs), "No complete independent dependency group");
const collinearElbow = elbowPackageBase(); collinearElbow.elements[1].points_mm = [{ x_mm: 100, y_mm: 0, z_mm: 0 }, { x_mm: 800, y_mm: 0, z_mm: 0 }]; fail(() => elbowPlanFor(collinearElbow), "No complete independent dependency group");
const verticalElbow = elbowPackageBase(); verticalElbow.elements[1].points_mm[1].z_mm = 1000; fail(() => elbowPlanFor(verticalElbow), "No complete independent dependency group");
const diagonalElbow = elbowPackageBase(); diagonalElbow.elements[1].points_mm[1].x_mm = 1100; fail(() => elbowPlanFor(diagonalElbow), "No complete independent dependency group");
const differentSize = elbowPackageBase(); differentSize.elements[1].diameter_mm = 80; fail(() => elbowPlanFor(differentSize), "No complete independent dependency group");
const differentType = elbowPackageBase(); differentType.resources.route_types.push({ source_key: "route_type_other", kind: "route_type", route_kind: "pipe" }); differentType.elements[1].type_source_key = "route_type_other";
const differentTypeCatalog = elbowCatalogBase(); differentTypeCatalog.resources.push({ destination_id: 106, kind: "route_type", route_kind: "pipe" });
fail(() => elbowPlanFor(differentType, differentTypeCatalog, [...elbowMappings, { source_key: "route_type_other", kind: "route_type", destination_id: 106 }]), "No complete independent dependency group");
const differentSystem = elbowPackageBase(); differentSystem.resources.system_types.push({ source_key: "system_type_other", kind: "system_type", route_kind: "pipe", system_classification: "ReturnHydronic" }); differentSystem.elements[1].system_source_key = "system_type_other";
const differentSystemCatalog = elbowCatalogBase(); differentSystemCatalog.resources.push({ destination_id: 107, kind: "system_type", route_kind: "pipe", system_classification: "ReturnHydronic" });
fail(() => elbowPlanFor(differentSystem, differentSystemCatalog, [...elbowMappings, { source_key: "system_type_other", kind: "system_type", destination_id: 107 }]), "No complete independent dependency group");
fail(() => elbowPlanFor(elbowPackageBase(), elbowCatalogBase(), mappings), "No complete independent dependency group");
const wrongSourcePartType = elbowPackageBase(); wrongSourcePartType.resources.family_symbols.at(-1).part_type = "tee"; fail(() => elbowPlanFor(wrongSourcePartType), "No complete independent dependency group");
const wrongTargetPartType = elbowCatalogBase(); wrongTargetPartType.resources.at(-1).part_type = "tee"; fail(() => elbowPlanFor(elbowPackageBase(), wrongTargetPartType), "No complete independent dependency group");
const wrongTargetCategory = elbowCatalogBase(); wrongTargetCategory.resources.at(-1).category = "duct_fitting"; fail(() => elbowPlanFor(elbowPackageBase(), wrongTargetCategory), "No complete independent dependency group");
const wrongTargetDomain = elbowCatalogBase(); wrongTargetDomain.resources.at(-1).route_kind = "duct"; fail(() => elbowPlanFor(elbowPackageBase(), wrongTargetDomain), "No complete independent dependency group");
const elbowExternalPeer = elbowPackageBase(); elbowExternalPeer.elements[0].external_physical_connection_count = 1; fail(() => elbowPlanFor(elbowExternalPeer), "No complete independent dependency group");
const offRouteConnector = elbowPackageBase(); offRouteConnector.physical_connections[0].from_connector.origin.y_mm = 5; fail(() => elbowPlanFor(offRouteConnector), "No complete independent dependency group");
const wrongRouteConnectorIndex = elbowPackageBase(); wrongRouteConnectorIndex.physical_connections[0].from.connector_index = 2; fail(() => elbowPlanFor(wrongRouteConnectorIndex), "No complete independent dependency group");
const wrongFittingConnectorIndex = elbowPackageBase(); wrongFittingConnectorIndex.physical_connections[0].to.connector_index = 2; fail(() => elbowPlanFor(wrongFittingConnectorIndex), "No complete independent dependency group");
const reusedFittingConnector = elbowPackageBase(); reusedFittingConnector.physical_connections[1].from.connector_index = 0; fail(() => elbowPlanFor(reusedFittingConnector), "No complete independent dependency group");
const duplicateElbowEdge = elbowPackageBase(); duplicateElbowEdge.physical_connections.push(clone(duplicateElbowEdge.physical_connections[0])); duplicateElbowEdge.dependency_groups[0].physical_connection_count = 3; fail(() => transfer.recordModelTransferPackage(duplicateElbowEdge), "Duplicate physical connector edge");
const crossGroup = packageBase(); crossGroup.elements.push({ ...clone(crossGroup.elements[0]), source_key: "route_11" }); crossGroup.physical_connections.push({ connection_kind: "physical", from: { element_key: "route_10", connector_index: 1 }, to: { element_key: "route_11", connector_index: 0 } }); crossGroup.dependency_groups = [{ group_key: "network_a", element_keys: ["route_10"], physical_connection_count: 0 }, { group_key: "network_b", element_keys: ["route_11"], physical_connection_count: 0 }]; fail(() => transfer.recordModelTransferPackage(crossGroup), "crosses dependency groups");

const preview = transfer.recordModelTransferPreview({ plan, bridge_preview: { preview_id: "bridge-preview", expires_at_utc: new Date(Date.now() + 60000).toISOString(), model_changed: false, validation_level: "revit_transactiongroup_rollback", validation: { status: "passed" } } });
const expectedRoute = plan.ready_groups[0].routes[0];
const applied = { created_element_ids: [999], operation_results: [{ operation: "model_create_batch", result: { route_count: 1, routes: [{ name: "route_10", result: { created_curve_ids: [999] } }] } }], verification: { verified: true, mode: "post_commit_read_back", operation: "bim_changeset", elements: [{ id: 999, type_id: 102, level_id: 101, system_type_id: 103, comments: expectedRoute.provenance_marker, centerline_mm: clone(expectedRoute.points), diameter_mm: 100 }], connector_network: [{ element_id: 999, connectors: [{ connector_type: "End", is_connected: false, connected_element_ids: [] }, { connector_type: "End", is_connected: false, connected_element_ids: [] }] }] } };
const report = transfer.completeModelTransferApply({ transfer_preview_id: preview.transfer_preview_id, bridge_apply: applied });
assert.equal(report.status, "verified_after_apply_reopen_pending"); assert.equal(report.source_to_destination[0].target_element_id, 999);
fail(() => transfer.completeModelTransferApply({ transfer_preview_id: preview.transfer_preview_id, bridge_apply: applied }), "already applied");
const reopened = transfer.completeModelTransferReopenVerification({ report_id: report.report_id, bridge_reopen: { verified: true, model_changed: false, elements: [{ source_key: "route_10", target_element_id: 999 }] } });
assert.equal(reopened.status, "verified_after_reopen");
const mismatchPreview = transfer.recordModelTransferPreview({ plan, bridge_preview: { preview_id: "bridge-preview-mismatch", expires_at_utc: new Date(Date.now() + 60000).toISOString(), model_changed: false, validation_level: "revit_transactiongroup_rollback", validation: { status: "passed" } } });
const mismatch = transfer.completeModelTransferApply({ transfer_preview_id: mismatchPreview.transfer_preview_id, bridge_apply: { ...applied, verification: { verified: false } } }); assert.equal(mismatch.status, "mismatch_after_apply");
assert.equal(mismatch.reopen_status, "blocked_by_post_commit_mismatch");
const semanticMismatchPreview = transfer.recordModelTransferPreview({ plan, bridge_preview: { preview_id: "bridge-preview-semantic-mismatch", expires_at_utc: new Date(Date.now() + 60000).toISOString(), model_changed: false, validation_level: "revit_transactiongroup_rollback", validation: { status: "passed" } } });
const semanticMismatch = transfer.completeModelTransferApply({ transfer_preview_id: semanticMismatchPreview.transfer_preview_id, bridge_apply: { ...applied, verification: { ...applied.verification, elements: [{ ...applied.verification.elements[0], diameter_mm: 80 }] } } }); assert.equal(semanticMismatch.status, "mismatch_after_apply"); assert.match(semanticMismatch.findings.map((item) => item.reason).join(" "), /diameter_mm/);

const elbowMarker = elbowRoute.provenance_marker;
const elbowSimulatedResult = {
  created_element_ids: [201, 202, 203],
  operation_results: [{
    operation: "model_create_batch",
    result: {
      route_count: 1,
      created_element_ids: [201, 202, 203],
      routes: [{ name: "network_elbow", result: { created_element_ids: [201, 202, 203], created_curve_ids: [201, 202], created_fitting_ids: [203] } }],
    },
  }],
};
const elbowVerification = {
  verified: true,
  mode: "post_commit_read_back",
  operation: "bim_changeset",
  elements: [
    { id: 201, type_id: 102, level_id: 101, system_type_id: 103, comments: elbowMarker, centerline_mm: [{ x_mm: 0, y_mm: 0, z_mm: 0 }, { x_mm: 900, y_mm: 0, z_mm: 0 }], diameter_mm: 100 },
    { id: 202, type_id: 102, level_id: 101, system_type_id: 103, comments: elbowMarker, centerline_mm: [{ x_mm: 1000, y_mm: 100, z_mm: 0 }, { x_mm: 1000, y_mm: 1000, z_mm: 0 }], diameter_mm: 100 },
    { id: 203, type_id: 105, level_id: 101, comments: elbowMarker, part_type: "elbow", bounding_box: { min: { x_mm: 900, y_mm: 0, z_mm: -50 }, max: { x_mm: 1000, y_mm: 100, z_mm: 50 } } },
  ],
  connector_network: [
    { element_id: 201, connectors: [physicalConnector({ x_mm: 0, y_mm: 0, z_mm: 0 }, { x_mm: -1, y_mm: 0, z_mm: 0 }), physicalConnector({ x_mm: 900, y_mm: 0, z_mm: 0 }, { x_mm: 1, y_mm: 0, z_mm: 0 }, [203])] },
    { element_id: 202, connectors: [physicalConnector({ x_mm: 1000, y_mm: 100, z_mm: 0 }, { x_mm: 0, y_mm: -1, z_mm: 0 }, [203]), physicalConnector({ x_mm: 1000, y_mm: 1000, z_mm: 0 }, { x_mm: 0, y_mm: 1, z_mm: 0 })] },
    { element_id: 203, connectors: [physicalConnector({ x_mm: 900, y_mm: 0, z_mm: 0 }, { x_mm: -1, y_mm: 0, z_mm: 0 }, [201]), physicalConnector({ x_mm: 1000, y_mm: 100, z_mm: 0 }, { x_mm: 0, y_mm: 1, z_mm: 0 }, [202])] },
  ],
};
const elbowPreviewBridge = (previewId, verification = elbowVerification, simulatedResult = elbowSimulatedResult) => ({
  preview_id: previewId,
  expires_at_utc: new Date(Date.now() + 60000).toISOString(),
  model_changed: false,
  validation_level: "revit_transactiongroup_rollback",
  validation: { status: "passed", simulated_result: clone(simulatedResult), simulated_verification: clone(verification) },
});
const elbowPreview = transfer.recordModelTransferPreview({ plan: elbowPlan, bridge_preview: elbowPreviewBridge("bridge-preview-elbow") });
assert.deepEqual(elbowPreview.semantic_preview_verification, { verified: true, mapped_element_count: 3, fitting_count: 1 });
const badPreviewBounds = clone(elbowVerification); badPreviewBounds.elements[2].bounding_box.max.x_mm += 1;
fail(() => transfer.recordModelTransferPreview({ plan: elbowPlan, bridge_preview: elbowPreviewBridge("bridge-preview-elbow-bounds", badPreviewBounds) }), "native elbow verification failed");
const badPreviewTopology = clone(elbowVerification); badPreviewTopology.connector_network[2].connectors[1].connected_element_ids = [201];
fail(() => transfer.recordModelTransferPreview({ plan: elbowPlan, bridge_preview: elbowPreviewBridge("bridge-preview-elbow-topology", badPreviewTopology) }), "native elbow verification failed");
const elbowReport = transfer.completeModelTransferApply({ transfer_preview_id: elbowPreview.transfer_preview_id, bridge_apply: { ...clone(elbowSimulatedResult), verification: clone(elbowVerification) } });
assert.equal(elbowReport.status, "verified_after_apply_reopen_pending");
assert.deepEqual(elbowReport.source_to_destination.map((item) => [item.source_key, item.target_element_id]), [["route_10", 201], ["route_11", 202], ["fitting_20", 203]]);
const elbowReopened = transfer.completeModelTransferReopenVerification({ report_id: elbowReport.report_id, bridge_reopen: { verified: true, model_changed: false, elements: elbowReport.source_to_destination.map((item) => ({ source_key: item.source_key, target_element_id: item.target_element_id })) } });
assert.equal(elbowReopened.status, "verified_after_reopen");

const missingFittingPreview = transfer.recordModelTransferPreview({ plan: elbowPlan, bridge_preview: elbowPreviewBridge("bridge-preview-elbow-missing-fitting") });
const missingFittingApply = clone(elbowSimulatedResult); missingFittingApply.operation_results[0].result.routes[0].result.created_fitting_ids = [];
const missingFittingReport = transfer.completeModelTransferApply({ transfer_preview_id: missingFittingPreview.transfer_preview_id, bridge_apply: { ...missingFittingApply, verification: clone(elbowVerification) } });
assert.equal(missingFittingReport.status, "mismatch_after_apply"); assert.match(missingFittingReport.findings.map((item) => item.reason).join(" "), /one native fitting/);
const topologyApplyPreview = transfer.recordModelTransferPreview({ plan: elbowPlan, bridge_preview: elbowPreviewBridge("bridge-preview-elbow-apply-topology") });
const wrongApplyTopology = clone(elbowVerification); wrongApplyTopology.connector_network[0].connectors[1].connected_element_ids = [];
const topologyReport = transfer.completeModelTransferApply({ transfer_preview_id: topologyApplyPreview.transfer_preview_id, bridge_apply: { ...clone(elbowSimulatedResult), verification: wrongApplyTopology } });
assert.equal(topologyReport.status, "mismatch_after_apply"); assert.match(topologyReport.findings.map((item) => item.reason).join(" "), /topology/);
const signatureApplyPreview = transfer.recordModelTransferPreview({ plan: elbowPlan, bridge_preview: elbowPreviewBridge("bridge-preview-elbow-apply-signature") });
const wrongApplySignature = clone(elbowVerification); wrongApplySignature.connector_network[2].connectors[0].direction = { x_mm: 0, y_mm: -1, z_mm: 0 };
const signatureReport = transfer.completeModelTransferApply({ transfer_preview_id: signatureApplyPreview.transfer_preview_id, bridge_apply: { ...clone(elbowSimulatedResult), verification: wrongApplySignature } });
assert.equal(signatureReport.status, "mismatch_after_apply"); assert.match(signatureReport.findings.map((item) => item.reason).join(" "), /signature/);

const tampered = recordsFor(); const packageFile = path.join(records, `${tampered.pkg.package_id}.json`); const envelope = JSON.parse(readFileSync(packageFile, "utf8")); envelope.value_sha256 = "tampered"; writeFileSync(packageFile, JSON.stringify(envelope));
const child = spawnSync(process.execPath, [process.argv[1], "--tampered", tampered.pkg.package_id, tampered.catalog.catalog_id], { env: { ...process.env, DSCONS_MODEL_TRANSFER_RECORD_DIRECTORY: records }, encoding: "utf8" }); assert.equal(child.status, 0, child.stderr);
console.log("PASS ModelTransfer: independent route plus atomic native-elbow topology, rollback semantics, exact mapping, post-commit/reopen verification, checksum rejection");
