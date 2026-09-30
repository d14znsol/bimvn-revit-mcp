import { createHash } from "node:crypto";
import { FamilyEvidenceError, readEvidenceRecord } from "./family-evidence.js";
import { validateFamilyBlueprint } from "./family-blueprint.js";
import { requireEngineeringReview, requireMepfReadiness, requireSourceConflictAssessment } from "./mepf-evidence-governance.js";

type Json = Record<string, unknown>;
type Point = [number, number, number];

const MAPPINGS = new Set(["family_profile", "symbolic_lines", "model_lines", "detail_lines", "route_centerline", "image_family", "pdf_catalog_family", "mep_project_reconstruction"]);
const PLANES = new Set(["xy", "xz", "yz"]);
const ROUTE_KINDS = new Set(["pipe", "duct", "conduit", "cable_tray"]);
const MEP_DISCIPLINES = new Set(["mechanical", "plumbing", "fire_protection", "electrical"]);
const IMAGE_SHAPES = new Set(["box", "cylinder"]);
const IMAGE_CATEGORIES = new Set(["mechanical_equipment", "electrical_equipment", "electrical_fixture", "plumbing_fixture", "lighting_fixture", "generic_model"]);
const IMAGE_TEMPLATE_BEHAVIORS = new Set(["level_based", "work_plane_based", "face_based"]);

function objectValue(value: unknown, label: string): Json {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be an object.`);
  return value as Json;
}

function textValue(value: unknown, label: string, maximum = 120): string {
  if (typeof value !== "string") throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be a string.`);
  const normalized = value.trim();
  if (!normalized || normalized.length > maximum || /[\u0000-\u001f\u007f]/.test(normalized)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must contain 1..${maximum} visible characters.`);
  return normalized;
}

function finiteNumber(value: unknown, label: string, positive = false): number {
  const normalized = Number(value);
  if (!Number.isFinite(normalized) || (positive && normalized <= 0)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be ${positive ? "positive" : "finite"}.`);
  return normalized;
}

function sourceIds(raw: unknown): string[] {
  if (!Array.isArray(raw) || raw.length < 1 || raw.length > 20) throw new FamilyEvidenceError("EvidenceInvalid", "evidence_ids must contain 1..20 immutable SourceEvidence ids.");
  const values = raw.map((item, index) => textValue(item, `evidence_ids[${index}]`, 96));
  if (new Set(values).size !== values.length) throw new FamilyEvidenceError("EvidenceInvalid", "evidence_ids must not contain duplicates.");
  return values;
}

function sourceRecord(id: string): Json {
  const record = readEvidenceRecord(id);
  if (record.record_kind !== "source_evidence_v3" || record.schema_version !== "3.0") throw new FamilyEvidenceError("EvidenceInvalid", `${id} is not immutable SourceEvidence v3.`);
  return record;
}

function unitScale(record: Json): number {
  const units = objectValue(record.units, "SourceEvidence.units");
  const unit = String(units.value ?? "unknown");
  const scale = ({ mm: 1, cm: 10, m: 1000, inch: 25.4, ft: 304.8 } as Record<string, number>)[unit];
  if (!scale) throw new FamilyEvidenceError("EvidenceInvalid", "A known DXF unit is required before geometry can be proposed.");
  return scale;
}

function point(raw: unknown, scale: number, label: string): Point {
  if (!Array.isArray(raw) || raw.length !== 3) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be a three-coordinate point.`);
  return [finiteNumber(raw[0], `${label}[0]`) * scale, finiteNumber(raw[1], `${label}[1]`) * scale, finiteNumber(raw[2], `${label}[2]`) * scale];
}

function revitPoint(value: Point): Json { return { x_mm: value[0], y_mm: value[1], z_mm: value[2] }; }
function lineKey(index: number): string { return `source_${String(index + 1).padStart(4, "0")}`; }
function visibility(): Json { return { coarse: true, medium: true, fine: true, front_back: true, left_right: true, plan_rcp: true, only_when_cut: false }; }
function governanceTargetKind(mapping: string): string { return mapping === "mep_project_reconstruction" ? "project" : mapping === "route_centerline" ? "route" : mapping === "symbolic_lines" || mapping === "model_lines" || mapping === "detail_lines" ? "linework" : "family"; }
function withGovernanceGates(args: Record<string, unknown>, mapping: string, ids: string[], mapped: Json): Json {
  const targetKind = governanceTargetKind(mapping); const gates: Json[] = [];
  if (args.readiness_record_id === undefined) gates.push({ gate: "information_readiness", status: "needs_information", question: "Run mepf_evidence_readiness and answer its batched questions before this proposal can be preview-ready." });
  else { const readiness = requireMepfReadiness(args.readiness_record_id, targetKind, mapping, ids); gates.push({ gate: "information_readiness", status: "passed", readiness_record_id: readiness.record_id }); }
  if (mapping === "mep_project_reconstruction") {
    const plan = mapped.project_reconstruction_plan as Json | undefined;
    if (!plan) throw new FamilyEvidenceError("EvidenceInvalid", "Project reconstruction proposal did not produce a plan for engineering review.");
    if (ids.length > 1) {
      if (args.source_conflict_record_id === undefined) gates.push({ gate: "cross_source_alignment", status: "needs_engineer_resolution", question: "Run source_conflict_assess for every immutable source and resolve revision/datum/value conflicts explicitly." });
      else { const conflict = requireSourceConflictAssessment(args.source_conflict_record_id, ids); gates.push({ gate: "cross_source_alignment", status: "passed", source_conflict_record_id: conflict.record_id }); }
    }
    if (args.engineering_review_record_id === undefined) gates.push({ gate: "independent_engineering_review", status: "needs_information", question: "Run mepf_engineering_review on this exact plan_sha256, acknowledge its calculation boundary, then submit the approved review record." });
    else { const review = requireEngineeringReview(args.engineering_review_record_id, plan); gates.push({ gate: "independent_engineering_review", status: "passed", engineering_review_record_id: review.record_id }); }
  }
  const blocking = gates.filter((gate) => gate.status !== "passed");
  const inheritedStatus = String(mapped.status ?? "requires_engineering_review");
  const status = blocking.length === 0 ? inheritedStatus : String(blocking[0].gate) === "cross_source_alignment" ? "requires_source_conflict_assessment" : String(blocking[0].gate) === "independent_engineering_review" ? "requires_independent_engineering_review" : "requires_information_readiness";
  const findings = Array.isArray(mapped.findings) ? mapped.findings : [];
  const readinessGate = gates.find((gate) => gate.gate === "information_readiness" && gate.status === "passed");
  const familySpec = targetKind === "family" && mapped.family_spec_input && readinessGate
    ? { ...(mapped.family_spec_input as Json), readiness_record_id: readinessGate.readiness_record_id }
    : mapped.family_spec_input;
  return { ...mapped, ...(familySpec ? { family_spec_input: familySpec } : {}), status, governance_gates: gates, findings: [...findings, ...(blocking.length === 0 ? [] : [{ status: "blocking", reason: "This proposal is not preview-ready until every evidence-governance gate passes. No Revit Change Set or Apply token is produced." }])] };
}

function validatedDraft(value: Json, label: string): { blueprint: Json; assessment: Json } {
  try {
    const validated = validateFamilyBlueprint(value);
    return { blueprint: validated.blueprint, assessment: validated.assessment as unknown as Json };
  } catch (error) {
    throw new FamilyEvidenceError("EvidenceInvalid", `Generated ${label} Blueprint is invalid: ${error instanceof Error ? error.message : String(error)}`);
  }
}

function familySpecInput(records: Json[], familyKind: string, confirmedFields: Json, blueprint: Json, typeConfirmedFields?: Json): Json {
  return {
    evidence_id: records[0].evidence_id,
    supporting_evidence_ids: records.slice(1).map((record) => record.evidence_id),
    family_kind: familyKind,
    confirmed_fields: confirmedFields,
    ...(typeConfirmedFields ? { type_confirmed_fields: typeConfirmedFields } : {}),
    blueprint,
  };
}

function dxfBlueprint(mapping: string, fragment: Json, args: Record<string, unknown>, record: Json): { blueprint_draft: Json; blueprint_assessment: Json; family_spec_input: Json } {
  const defaults: Record<string, { familyKind: string; category: string; behavior: string }> = {
    family_profile: { familyKind: "dxf_profile", category: "profile", behavior: "profile" },
    symbolic_lines: { familyKind: "dxf_symbolic", category: "annotation", behavior: "annotation" },
    model_lines: { familyKind: "dxf_model_lines", category: "generic_model", behavior: "level_based" },
    detail_lines: { familyKind: "dxf_detail_item", category: "detail_item", behavior: "detail_item" },
  };
  const selected = defaults[mapping];
  const familyKind = args.family_kind === undefined ? selected.familyKind : textValue(args.family_kind, "family_kind", 64);
  if (!/^[a-z][a-z0-9_]{0,63}$/.test(familyKind)) throw new FamilyEvidenceError("EvidenceInvalid", "family_kind must use lower_snake_case.");
  const typeName = args.type_name === undefined ? "Default" : textValue(args.type_name, "type_name", 120);
  const draft: Json = {
    schema_version: "3.0", target_lod: "LOD_300",
    family: { family_key: familyKind, category: selected.category, template_behavior: selected.behavior, primary_axis: "x" },
    required_source_fields: [], parameters: [], types: [{ name: typeName, values: {} }], parts: [], connectors: [], nested_components: [],
    symbolic_lines: [], model_lines: [], detail_lines: [], profile_loops: [], ui_fallbacks: [],
    ...fragment,
  };
  const validated = validatedDraft(draft, `DXF ${mapping}`);
  return {
    blueprint_draft: validated.blueprint,
    blueprint_assessment: validated.assessment,
    family_spec_input: familySpecInput([record], familyKind, {}, validated.blueprint),
  };
}

function dxfProposal(mapping: string, record: Json, args: Record<string, unknown>): Json {
  if (record.source_kind !== "dxf") throw new FamilyEvidenceError("EvidenceInvalid", `${mapping} requires exactly one DXF SourceEvidence record.`);
  const scale = unitScale(record); const geometry = objectValue(record.geometry, "SourceEvidence.geometry");
  if (args.confirm_units !== undefined) {
    const confirmed = textValue(args.confirm_units, "confirm_units", 10);
    const observed = String(objectValue(record.units, "SourceEvidence.units").value ?? "unknown");
    if (confirmed !== observed) throw new FamilyEvidenceError("EvidenceConflict", `confirm_units ${confirmed} does not match immutable SourceEvidence units ${observed}.`);
  }
  if (!Array.isArray(geometry.entities)) throw new FamilyEvidenceError("EvidenceInvalid", "DXF SourceEvidence has no parsed entity list.");
  const selectedLayers = args.layers === undefined ? [] : Array.isArray(args.layers) ? args.layers.map((item, index) => textValue(item, `layers[${index}]`)) : (() => { throw new FamilyEvidenceError("EvidenceInvalid", "layers must be an array."); })();
  const sourceLayers = new Set(Array.isArray(record.layers) ? record.layers.map(String) : []);
  for (const layer of selectedLayers) if (!sourceLayers.has(layer)) throw new FamilyEvidenceError("EvidenceInvalid", `Requested layer ${layer} is absent from SourceEvidence.`);
  const included = (geometry.entities as Json[]).filter((entity) => selectedLayers.length === 0 || selectedLayers.includes(String(entity.layer)));
  const findings: Json[] = []; const supported: Json[] = [];
  for (const entity of included) {
    const type = String(entity.type);
    if (type === "LINE" || type === "LWPOLYLINE") supported.push(entity);
    else findings.push({ entity_type: type, layer: entity.layer, handle: entity.handle ?? null, status: "engineering_review_required", reason: "The bounded proposal maps only LINE and LWPOLYLINE entities." });
  }
  const plane = args.plane === undefined ? "xy" : textValue(args.plane, "plane", 2);
  if (!PLANES.has(plane)) throw new FamilyEvidenceError("EvidenceInvalid", "plane must be xy, xz or yz.");

  if (mapping === "family_profile") {
    const loops = supported.filter((entity) => entity.type === "LWPOLYLINE" && entity.closed === true)
      .map((entity, index) => ({ key: lineKey(index), role: "source_dxf_profile", plane, points_mm: (entity.vertices as unknown[]).map((value, pointIndex) => revitPoint(point(value, scale, `entity.vertices[${pointIndex}]`))), source: { layer: entity.layer, handle: entity.handle ?? null } }))
      .filter((loop) => loop.points_mm.length >= 3);
    for (const entity of supported.filter((value) => value.type !== "LWPOLYLINE" || value.closed !== true)) findings.push({ entity_type: entity.type, layer: entity.layer, handle: entity.handle ?? null, status: "not_mapped", reason: "Family profile requires a closed LWPOLYLINE with at least three vertices." });
    const fragment = { profile_loops: loops };
    return { status: loops.length > 0 ? "proposal_ready" : "requires_engineering_review", blueprint_fragment: fragment, ...(loops.length > 0 ? dxfBlueprint(mapping, fragment, args, record) : {}), findings };
  }

  const lines: Json[] = supported.map((entity, index) => {
    const points = entity.type === "LINE" ? [point(entity.start, scale, `entities[${index}].start`), point(entity.end, scale, `entities[${index}].end`)] : (entity.vertices as unknown[]).map((value, pointIndex) => point(value, scale, `entities[${index}].vertices[${pointIndex}]`));
    const base: Json = { key: lineKey(index), role: "source_dxf_linework", plane, points_mm: points.map(revitPoint), source: { layer: entity.layer, handle: entity.handle ?? null, entity_type: entity.type } };
    if (mapping !== "detail_lines") base.visibility = visibility();
    return base;
  }).filter((line) => Array.isArray(line.points_mm) && line.points_mm.length >= 2);

  if (mapping === "route_centerline") {
    const route = objectValue(args.route, "route"); const kind = textValue(route.kind, "route.kind", 20);
    if (!ROUTE_KINDS.has(kind)) throw new FamilyEvidenceError("EvidenceInvalid", "route.kind must be pipe, duct, conduit or cable_tray.");
    const typeId = finiteNumber(route.type_id, "route.type_id", true); const levelId = finiteNumber(route.level_id, "route.level_id", true);
    const systemTypeRequired = kind === "pipe" || kind === "duct"; const systemTypeId = route.system_type_id === undefined ? null : finiteNumber(route.system_type_id, "route.system_type_id", true);
    if (systemTypeRequired && systemTypeId === null) throw new FamilyEvidenceError("EvidenceInvalid", `route.system_type_id is required for ${kind}.`);
    const elevation = finiteNumber(route.elevation_mm, "route.elevation_mm"); const tolerance = finiteNumber(route.tolerance_mm, "route.tolerance_mm", true);
    const segments = lines.filter((line, index) => {
      const entity = supported[index];
      if (entity?.type === "LWPOLYLINE" && entity.closed === true) { findings.push({ entity_type: entity.type, layer: entity.layer, handle: entity.handle ?? null, status: "not_mapped", reason: "Closed polylines are not route centerlines." }); return false; }
      return true;
    }).map((line) => ({ key: line.key, points_mm: (line.points_mm as Json[]).map((value) => ({ ...value, z_mm: elevation })), source: line.source }));
    return {
      status: segments.length > 0 ? "ready_for_fresh_revit_preview" : "requires_engineering_review",
      project_change_set_proposal: { operation: "bounded_cad_centerline_route", kind, type_id: typeId, system_type_id: systemTypeId, level_id: levelId, elevation_mm: elevation, tolerance_mm: tolerance, segments },
      findings: [...findings, { status: "boundary", reason: "Fittings, slope, connector joins, reroute and clash avoidance are not inferred; a fresh Revit preview and post-commit network read-back are mandatory." }],
    };
  }

  const property = mapping === "symbolic_lines" ? "symbolic_lines" : mapping === "model_lines" ? "model_lines" : "detail_lines";
  const fragment = { [property]: lines };
  return { status: lines.length > 0 ? "proposal_ready" : "requires_engineering_review", blueprint_fragment: fragment, ...(lines.length > 0 ? dxfBlueprint(mapping, fragment, args, record) : {}), findings };
}

function pdfBlock(record: Json, raw: unknown, label: string): Json {
  const locator = objectValue(raw, label);
  const pageNumber = finiteNumber(locator.page, `${label}.page`, true);
  const blockIndex = finiteNumber(locator.block_index, `${label}.block_index`);
  if (!Number.isInteger(pageNumber) || !Number.isInteger(blockIndex) || blockIndex < 0) throw new FamilyEvidenceError("EvidenceInvalid", `${label} requires integer page and non-negative block_index.`);
  const pages = Array.isArray(record.pages) ? record.pages as Json[] : [];
  const page = pages.find((item) => Number(item.page) === pageNumber);
  const blocks = page && Array.isArray(page.text_blocks) ? page.text_blocks as Json[] : [];
  const block = blocks[blockIndex];
  if (!block) throw new FamilyEvidenceError("EvidenceInvalid", `${label} does not resolve to a SourceEvidence page/block.`);
  const expected = textValue(locator.block_sha256, `${label}.block_sha256`, 64).toLowerCase();
  if (!/^[a-f0-9]{64}$/.test(expected) || expected !== String(block.block_sha256 ?? "").toLowerCase()) throw new FamilyEvidenceError("EvidenceConflict", `${label} block fingerprint does not match immutable SourceEvidence.`);
  return { kind: "source_document", page: pageNumber, block_index: blockIndex, block_sha256: expected };
}

function pdfCatalogProposal(record: Json, args: Record<string, unknown>): Json {
  if (record.source_kind !== "pdf") throw new FamilyEvidenceError("EvidenceInvalid", "pdf_catalog_family requires exactly one PDF SourceEvidence v3 record.");
  const proposal = objectValue(args.catalog_family, "catalog_family");
  const familyKind = textValue(proposal.family_kind, "catalog_family.family_kind", 64);
  if (!/^[a-z][a-z0-9_]{0,63}$/.test(familyKind)) throw new FamilyEvidenceError("EvidenceInvalid", "catalog_family.family_kind must use lower_snake_case.");
  const category = textValue(proposal.category, "catalog_family.category", 40);
  if (!IMAGE_CATEGORIES.has(category)) throw new FamilyEvidenceError("EvidenceInvalid", "catalog_family.category is outside the bounded simple-equipment set.");
  const templateBehavior = textValue(proposal.template_behavior, "catalog_family.template_behavior", 40);
  if (!IMAGE_TEMPLATE_BEHAVIORS.has(templateBehavior)) throw new FamilyEvidenceError("EvidenceInvalid", "catalog_family.template_behavior must be level_based, work_plane_based or face_based.");
  const shape = textValue(proposal.shape, "catalog_family.shape", 20);
  if (!IMAGE_SHAPES.has(shape)) throw new FamilyEvidenceError("EvidenceInvalid", "catalog_family.shape must be box or cylinder.");
  const rows = Array.isArray(proposal.types) ? proposal.types.map((raw, index) => objectValue(raw, `catalog_family.types[${index}]`)) : [];
  if (rows.length < 2 || rows.length > 100) throw new FamilyEvidenceError("EvidenceInvalid", "catalog_family.types must contain 2..100 source-bound Family Types.");
  const dimensionFields = shape === "box" ? ["width_mm", "height_mm", "depth_mm"] : ["diameter_mm", "height_mm"];
  const parameterKeys = shape === "box" ? ["width", "height", "depth"] : ["diameter", "height"];
  const parameterNames = shape === "box" ? ["Width", "Height", "Depth"] : ["Diameter", "Height"];
  const parameters = parameterKeys.map((key, index) => ({ key, name: parameterNames[index], data_type: "length", scope: "type", source_field: dimensionFields[index] }));
  const seen = new Set<string>(); const types: Json[] = []; const typeConfirmedFields: Json = {};
  for (const [index, row] of rows.entries()) {
    const name = textValue(row.name, `catalog_family.types[${index}].name`, 120);
    if (seen.has(name)) throw new FamilyEvidenceError("EvidenceInvalid", `Duplicate catalog Family Type name ${name}.`);
    seen.add(name);
    const dimensions = objectValue(row.dimensions_mm, `catalog_family.types[${index}].dimensions_mm`);
    const provenance = objectValue(row.provenance, `catalog_family.types[${index}].provenance`);
    const values: Json = {}; const confirmed: Json = {};
    dimensionFields.forEach((field, fieldIndex) => {
      const value = finiteNumber(dimensions[field], `catalog_family.types[${index}].dimensions_mm.${field}`, true);
      values[parameterKeys[fieldIndex]] = value;
      confirmed[field] = { value, status: "confirmed", provenance: pdfBlock(record, provenance[field], `catalog_family.types[${index}].provenance.${field}`) };
    });
    types.push({ name, values }); typeConfirmedFields[name] = confirmed;
  }
  const firstValues = types[0].values as Json;
  const part = shape === "box"
    ? { key: "body", primitive: "extrusion", operation: "solid", axis: "x", role: "CatalogBody", profile: { shape: "rectangle", width_mm: firstValues.width, height_mm: firstValues.height, width_parameter: "width", height_parameter: "height" }, start_mm: 0, end_mm: firstValues.depth, depth_parameter: "depth", visibility: visibility() }
    : { key: "body", primitive: "extrusion", operation: "solid", axis: "x", role: "CatalogBody", profile: { shape: "circle", diameter_mm: firstValues.diameter, diameter_parameter: "diameter" }, start_mm: 0, end_mm: firstValues.height, depth_parameter: "height", visibility: visibility() };
  const flex = parameterKeys.map((key) => {
    const values = types.map((type) => Number((type.values as Json)[key])); const nominal = Number(firstValues[key]);
    return { parameter_key: key, min: Math.min(...values) * 0.5, nominal, max: Math.max(...values) * 1.5 };
  });
  const validated = validatedDraft({
    schema_version: "3.0", target_lod: "LOD_300",
    family: { family_key: familyKind, category, template_behavior: templateBehavior, primary_axis: "x" },
    required_source_fields: dimensionFields, parameters, types, parts: [part], connectors: [], nested_components: [], symbolic_lines: [], ui_fallbacks: [],
    verification: { parameter_flex_cases: flex },
  }, "PDF catalog");
  return {
    status: "proposal_ready_for_family_spec",
    family_proposal: { family_kind: familyKind, category, template_behavior: templateBehavior, primitive: shape === "box" ? "extrusion_box" : "extrusion_cylinder", type_count: types.length, connectors: [] },
    confirmed_fields: {}, type_confirmed_fields: typeConfirmedFields,
    blueprint_draft: validated.blueprint, blueprint_assessment: validated.assessment,
    family_spec_input: familySpecInput([record], familyKind, {}, validated.blueprint, typeConfirmedFields),
    findings: [
      { status: "boundary", reason: "Every source-bound dimension is linked to an immutable PDF page/block fingerprint supplied for review." },
      { status: "unavailable", reason: "Connectors, hidden geometry, materials and performance data are not inferred from the catalog." },
    ],
  };
}

function imageProposal(records: Json[], args: Record<string, unknown>): Json {
  if (records.length < 2 || records.some((record) => record.source_kind !== "image")) throw new FamilyEvidenceError("EvidenceInvalid", "image_family requires 2..4 image SourceEvidence v3 records.");
  const orientations = records.map((record) => String(objectValue(record.orientation, "SourceEvidence.orientation").view ?? "unknown"));
  const usefulViews = new Set(orientations.filter((value) => value === "front" || value === "side" || value === "top" || value === "orthographic"));
  if (usefulViews.size < 2) throw new FamilyEvidenceError("EvidenceInvalid", "image_family requires at least two distinct orthographic/front/side/top views.");
  for (const record of records) if (record.status !== "proposal_ready" || !Array.isArray(record.scale_anchors) || record.scale_anchors.length === 0) throw new FamilyEvidenceError("EvidenceInvalid", "Every image view requires a known unit and at least one human-confirmed scale anchor.");
  const proposal = objectValue(args.image_family, "image_family"); const shape = textValue(proposal.shape, "image_family.shape", 20);
  if (!IMAGE_SHAPES.has(shape)) throw new FamilyEvidenceError("EvidenceInvalid", "image_family.shape must be box or cylinder.");
  if (proposal.dimensions_confirmed_by_user !== true) throw new FamilyEvidenceError("EvidenceInvalid", "image_family.dimensions_confirmed_by_user must be true; dimensions are never inferred silently from images.");
  const dimensions = objectValue(proposal.dimensions_mm, "image_family.dimensions_mm"); const normalizedDimensions: Json = {};
  if (shape === "box") for (const key of ["width", "height", "depth"]) normalizedDimensions[`${key}_mm`] = finiteNumber(dimensions[`${key}_mm`], `image_family.dimensions_mm.${key}_mm`, true);
  else { normalizedDimensions.diameter_mm = finiteNumber(dimensions.diameter_mm, "image_family.dimensions_mm.diameter_mm", true); normalizedDimensions.height_mm = finiteNumber(dimensions.height_mm, "image_family.dimensions_mm.height_mm", true); }
  const familyKind = textValue(proposal.family_kind, "image_family.family_kind", 64);
  if (!/^[a-z][a-z0-9_]{0,63}$/.test(familyKind)) throw new FamilyEvidenceError("EvidenceInvalid", "image_family.family_kind must use lower_snake_case.");
  const category = textValue(proposal.category, "image_family.category", 40);
  if (!IMAGE_CATEGORIES.has(category)) throw new FamilyEvidenceError("EvidenceInvalid", "image_family.category is outside the bounded simple-equipment set.");
  const templateBehavior = textValue(proposal.template_behavior, "image_family.template_behavior", 40);
  if (!IMAGE_TEMPLATE_BEHAVIORS.has(templateBehavior)) throw new FamilyEvidenceError("EvidenceInvalid", "image_family.template_behavior must be level_based, work_plane_based or face_based.");
  const typeName = proposal.type_name === undefined ? "Default" : textValue(proposal.type_name, "image_family.type_name", 120);
  const parameters = shape === "box"
    ? [
        { key: "width", name: "Width", data_type: "length", scope: "type", source_field: "width_mm" },
        { key: "height", name: "Height", data_type: "length", scope: "type", source_field: "height_mm" },
        { key: "depth", name: "Depth", data_type: "length", scope: "type", source_field: "depth_mm" },
      ]
    : [
        { key: "diameter", name: "Diameter", data_type: "length", scope: "type", source_field: "diameter_mm" },
        { key: "height", name: "Height", data_type: "length", scope: "type", source_field: "height_mm" },
      ];
  const typeValues = shape === "box"
    ? { width: normalizedDimensions.width_mm, height: normalizedDimensions.height_mm, depth: normalizedDimensions.depth_mm }
    : { diameter: normalizedDimensions.diameter_mm, height: normalizedDimensions.height_mm };
  const part = shape === "box"
    ? { key: "body", primitive: "extrusion", operation: "solid", axis: "x", role: "ImageProposalBody", profile: { shape: "rectangle", width_mm: normalizedDimensions.width_mm, height_mm: normalizedDimensions.height_mm, width_parameter: "width", height_parameter: "height" }, start_mm: 0, end_mm: normalizedDimensions.depth_mm, depth_parameter: "depth", visibility: visibility() }
    : { key: "body", primitive: "extrusion", operation: "solid", axis: "x", role: "ImageProposalBody", profile: { shape: "circle", diameter_mm: normalizedDimensions.diameter_mm, diameter_parameter: "diameter" }, start_mm: 0, end_mm: normalizedDimensions.height_mm, depth_parameter: "height", visibility: visibility() };
  const flex = parameters.map((parameter) => {
    const nominal = Number(typeValues[parameter.key as keyof typeof typeValues]);
    return { parameter_key: parameter.key, min: nominal * 0.5, nominal, max: nominal * 1.5 };
  });
  const validated = validatedDraft({
      schema_version: "3.0", target_lod: "LOD_300",
      family: { family_key: familyKind, category, template_behavior: templateBehavior, primary_axis: "x" },
      required_source_fields: parameters.map((parameter) => parameter.source_field), parameters,
      types: [{ name: typeName, values: typeValues }], parts: [part], connectors: [], nested_components: [], symbolic_lines: [], ui_fallbacks: [],
      verification: { parameter_flex_cases: flex },
    }, "image");
  const confirmedFields = Object.fromEntries(Object.entries(normalizedDimensions).map(([key, value]) => [key, { value, status: "confirmed", provenance: { kind: "user_confirmed" } }]));
  return {
    status: "proposal_ready_for_blueprint_review",
    family_proposal: { family_kind: familyKind, category, template_behavior: templateBehavior, type_name: typeName, primitive: shape === "box" ? "extrusion_box" : "extrusion_cylinder", dimensions: normalizedDimensions, source_views: orientations, connectors: [] },
    confirmed_fields: confirmedFields,
    blueprint_draft: validated.blueprint,
    blueprint_assessment: validated.assessment,
    family_spec_input: familySpecInput(records, familyKind, confirmedFields, validated.blueprint),
    findings: [
      { status: "boundary", reason: "Dimensions are explicit user confirmations; image pixels are supporting evidence only." },
      { status: "unavailable", reason: "Hidden geometry, wall thickness, host behavior, connectors, materials and performance data are not inferred." },
      { status: "next", reason: "Review the generated bounded Blueprint v3 and confirmed fields before family_spec_preview; do not add inferred connectors or hidden geometry." },
    ],
  };
}

function objectArray(raw: unknown, label: string, maximum: number): Json[] {
  if (raw === undefined) return [];
  if (!Array.isArray(raw) || raw.length > maximum) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be an array with at most ${maximum} records.`);
  return raw.map((item, index) => objectValue(item, `${label}[${index}]`));
}

function confirmed(value: unknown, label: string): void {
  if (value !== true) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be true before a Project reconstruction proposal can become preview-ready.`);
}

function projectPoint(raw: unknown, label: string): Json {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be a point object.`);
  const value = raw as Json; return { x_mm: finiteNumber(value.x_mm, `${label}.x_mm`), y_mm: finiteNumber(value.y_mm, `${label}.y_mm`), z_mm: finiteNumber(value.z_mm, `${label}.z_mm`) };
}

function validateOrthogonalRoute(points: Json[], label: string): void {
  if (points.length < 2 || points.length > 100) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must contain 2..100 points.`);
  let previousAxis = -1;
  for (let index = 1; index < points.length; index += 1) {
    const before = points[index - 1]; const after = points[index];
    const deltas = [Number(after.x_mm) - Number(before.x_mm), Number(after.y_mm) - Number(before.y_mm), Number(after.z_mm) - Number(before.z_mm)];
    const axes = deltas.map((value, axis) => Math.abs(value) > 0.001 ? axis : -1).filter((axis) => axis >= 0);
    if (axes.length !== 1) throw new FamilyEvidenceError("EvidenceInvalid", `${label} segment ${index} must follow exactly one X, Y or Z axis; sloped/diagonal geometry remains engineering-review-only.`);
    if (axes[0] === previousAxis) throw new FamilyEvidenceError("EvidenceInvalid", `${label} contains a redundant collinear corner at point ${index}.`);
    previousAxis = axes[0];
  }
}

function sourceLocator(records: Map<string, Json>, raw: unknown, label: string): Json {
  const locator = objectValue(raw, label); const evidenceId = textValue(locator.evidence_id, `${label}.evidence_id`, 96); const record = records.get(evidenceId);
  if (!record) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.evidence_id is outside this proposal's immutable evidence set.`);
  const kind = String(record.source_kind); const citation: Json = { evidence_id: evidenceId, source_kind: kind, sha256: record.sha256 };
  if (kind === "pdf") {
    const pageNumber = finiteNumber(locator.page, `${label}.page`, true);
    const page = (record.pages as Json[]).find((candidate) => Number(candidate.page) === pageNumber);
    if (!Number.isInteger(pageNumber) || !page) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.page does not exist in the PDF evidence.`);
    citation.page = pageNumber;
    if (locator.block_index !== undefined || locator.block_sha256 !== undefined) Object.assign(citation, pdfBlock(record, locator, label));
    if (locator.path_index !== undefined || locator.path_sha256 !== undefined) {
      const pathIndex = finiteNumber(locator.path_index, `${label}.path_index`);
      if (!Number.isInteger(pathIndex) || pathIndex < 0) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.path_index must be a non-negative integer.`);
      const expectedHash = textValue(locator.path_sha256, `${label}.path_sha256`, 64).toLowerCase();
      if (!/^[a-f0-9]{64}$/.test(expectedHash)) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.path_sha256 must be SHA-256 hex.`);
      const extraction = objectValue(page.vector_extraction, `SourceEvidence.pages[${pageNumber}].vector_extraction`);
      const paths = Array.isArray(extraction.paths) ? extraction.paths as Json[] : [];
      const selected = paths.find((path) => Number(path.path_index) === pathIndex);
      if (!selected || String(selected.path_sha256) !== expectedHash) throw new FamilyEvidenceError("EvidenceConflict", `${label} PDF vector path fingerprint does not match immutable SourceEvidence.`);
      citation.path_index = pathIndex; citation.path_sha256 = expectedHash; citation.path_bounds_page = selected.bounds_page;
    }
  } else if (kind === "dxf" || kind === "dwg") {
    const layer = textValue(locator.layer, `${label}.layer`, 160); const layers = new Set(Array.isArray(record.layers) ? record.layers.map(String) : []);
    if (!layers.has(layer)) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.layer is absent from immutable CAD evidence.`);
    citation.layer = layer;
    if (locator.handles !== undefined) {
      if (!Array.isArray(locator.handles) || locator.handles.length > 500) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.handles must contain at most 500 handles.`);
      const requested = locator.handles.map((handle, index) => textValue(handle, `${label}.handles[${index}]`, 96));
      const entities = (objectValue(record.geometry, "SourceEvidence.geometry").entities as Json[] ?? []); const available = new Set(entities.filter((entity) => String(entity.layer) === layer && entity.handle !== null).map((entity) => String(entity.handle)));
      for (const handle of requested) if (!available.has(handle)) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.handles contains ${handle}, which is absent from the selected CAD layer.`);
      citation.handles = requested;
    }
  } else if (kind === "image") {
    const image = objectValue(record.image, "SourceEvidence.image"); const region = objectValue(locator.image_region, `${label}.image_region`);
    const x = finiteNumber(region.x_px, `${label}.image_region.x_px`); const y = finiteNumber(region.y_px, `${label}.image_region.y_px`); const width = finiteNumber(region.width_px, `${label}.image_region.width_px`, true); const height = finiteNumber(region.height_px, `${label}.image_region.height_px`, true);
    if (x < 0 || y < 0 || x + width > Number(image.width_px) || y + height > Number(image.height_px)) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.image_region is outside the source image.`);
    citation.image_region = { x_px: x, y_px: y, width_px: width, height_px: height };
  } else throw new FamilyEvidenceError("EvidenceInvalid", `${label} uses an unsupported source kind.`);
  return citation;
}

function mepProjectProposal(records: Json[], args: Record<string, unknown>): Json {
  const input = objectValue(args.mep_project, "mep_project"); const confirmations = objectValue(input.confirmations, "mep_project.confirmations");
  for (const key of ["coordinate_system", "level_mapping", "type_mapping", "system_mapping", "routing_geometry"])
    confirmed(confirmations[key], `mep_project.confirmations.${key}`);
  const byId = new Map(records.map((record) => [String(record.evidence_id), record]));
  const evidenceFindings: Json[] = [];
  for (const record of records) {
    const kind = String(record.source_kind); const units = String(objectValue(record.units, "SourceEvidence.units").value ?? "unknown");
    if (kind === "pdf" && (units === "unknown" || !Array.isArray(record.scale_anchors) || record.scale_anchors.length === 0)) evidenceFindings.push({ status: "blocking", evidence_id: record.evidence_id, reason: "PDF drawing reconstruction requires known units and a human-confirmed scale anchor." });
    if (kind === "dwg" && objectValue(record.geometry, "SourceEvidence.geometry").status !== "trusted_adapter_parsed") evidenceFindings.push({ status: "blocking", evidence_id: record.evidence_id, reason: "DWG requires a SHA-256-bound Autodesk/ODA-compatible trusted adapter manifest." });
    if (kind === "dxf" && units === "unknown") evidenceFindings.push({ status: "blocking", evidence_id: record.evidence_id, reason: "DXF units are unknown." });
  }
  const imageRecords = records.filter((record) => record.source_kind === "image");
  if (imageRecords.length > 0) {
    const captures = imageRecords.map((record) => record.site_capture as Json | null); const sets = new Set(captures.filter(Boolean).map((capture) => String(capture!.capture_set_id)));
    const controls = captures.filter(Boolean).reduce((count, capture) => count + (Array.isArray(capture!.control_points) ? capture!.control_points.length : 0), 0);
    if (imageRecords.length < 2 || captures.some((capture) => !capture) || sets.size !== 1 || controls < 3) evidenceFindings.push({ status: "blocking", reason: "Construction-photo reconstruction requires at least two images in one confirmed capture set and at least three world control points in total." });
  }

  const routesInput = objectArray(input.routes, "mep_project.routes", 100); const equipmentInput = objectArray(input.equipment, "mep_project.equipment", 100);
  if (routesInput.length === 0 && equipmentInput.length === 0) throw new FamilyEvidenceError("EvidenceInvalid", "mep_project requires at least one route or equipment record.");
  if (equipmentInput.length > 0) confirmed(confirmations.equipment_mapping, "mep_project.confirmations.equipment_mapping");
  const keys = new Set<string>(); const routeOperations: Json[] = []; const placementOperations: Json[] = []; const sourceCitations: Json[] = [];
  const findings: Json[] = [...evidenceFindings];
  const routeCompatibility: Record<string, Set<string>> = {
    mechanical: new Set(["duct", "pipe"]), plumbing: new Set(["pipe"]), fire_protection: new Set(["pipe"]), electrical: new Set(["conduit", "cable_tray"]),
  };
  for (const [index, route] of routesInput.entries()) {
    const key = textValue(route.key, `mep_project.routes[${index}].key`, 80); if (keys.has(key)) throw new FamilyEvidenceError("EvidenceInvalid", `Duplicate MEP reconstruction key ${key}.`); keys.add(key);
    const discipline = textValue(route.discipline, `mep_project.routes[${index}].discipline`, 40); const kind = textValue(route.kind, `mep_project.routes[${index}].kind`, 20);
    if (!MEP_DISCIPLINES.has(discipline) || !ROUTE_KINDS.has(kind) || !routeCompatibility[discipline].has(kind)) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.routes[${index}] has an incompatible MEP discipline/kind.`);
    for (const field of ["geometry_confirmed", "size_confirmed", "elevation_confirmed", "type_confirmed"]) confirmed(route[field], `mep_project.routes[${index}].${field}`);
    if (kind === "pipe" || kind === "duct") confirmed(route.system_confirmed, `mep_project.routes[${index}].system_confirmed`);
    const typeId = finiteNumber(route.type_id, `mep_project.routes[${index}].type_id`, true); const levelId = finiteNumber(route.level_id, `mep_project.routes[${index}].level_id`, true);
    const systemTypeId = route.system_type_id === undefined ? null : finiteNumber(route.system_type_id, `mep_project.routes[${index}].system_type_id`, true);
    if ((kind === "pipe" || kind === "duct") && systemTypeId === null) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.routes[${index}].system_type_id is required.`);
    const points = Array.isArray(route.points_mm) ? route.points_mm.map((value, pointIndex) => projectPoint(value, `mep_project.routes[${index}].points_mm[${pointIndex}]`)) : [];
    validateOrthogonalRoute(points, `mep_project.routes[${index}].points_mm`);
    const size = objectValue(route.size_mm, `mep_project.routes[${index}].size_mm`); const diameter = size.diameter_mm === undefined ? null : finiteNumber(size.diameter_mm, `mep_project.routes[${index}].size_mm.diameter_mm`, true); const width = size.width_mm === undefined ? null : finiteNumber(size.width_mm, `mep_project.routes[${index}].size_mm.width_mm`, true); const height = size.height_mm === undefined ? null : finiteNumber(size.height_mm, `mep_project.routes[${index}].size_mm.height_mm`, true);
    if ((kind === "pipe" || kind === "conduit") && (diameter === null || width !== null || height !== null)) throw new FamilyEvidenceError("EvidenceInvalid", `${kind} route ${key} requires diameter_mm only.`);
    if (kind === "cable_tray" && (diameter !== null || width === null || height === null)) throw new FamilyEvidenceError("EvidenceInvalid", `cable_tray route ${key} requires width_mm and height_mm.`);
    if (kind === "duct" && !((diameter !== null && width === null && height === null) || (diameter === null && width !== null && height !== null))) throw new FamilyEvidenceError("EvidenceInvalid", `duct route ${key} requires either diameter_mm or width_mm + height_mm.`);
    if (route.slope_percent !== undefined && Math.abs(finiteNumber(route.slope_percent, `mep_project.routes[${index}].slope_percent`)) > 0.0001) findings.push({ status: "blocking", key, reason: "Sloped source routes require a separately certified slope/fitting workflow and are not included in the atomic API batch." });
    const citation = sourceLocator(byId, route.source, `mep_project.routes[${index}].source`); sourceCitations.push({ element_key: key, ...citation });
    routeOperations.push({ name: key, kind, type_id: typeId, system_type_id: systemTypeId, level_id: levelId, points, ...(diameter !== null ? { diameter_mm: diameter } : {}), ...(width !== null ? { width_mm: width } : {}), ...(height !== null ? { height_mm: height } : {}), demo_tag: `DSCons Source MEPF ${key}`, service: textValue(route.service, `mep_project.routes[${index}].service`, 120), discipline, source: citation });
  }
  const allowedCategories = new Set(["mechanical_equipment", "duct_terminal", "duct_accessory", "pipe_accessory", "plumbing_fixture", "sprinkler", "electrical_equipment", "electrical_fixture", "lighting_fixture"]);
  for (const [index, equipment] of equipmentInput.entries()) {
    const key = textValue(equipment.key, `mep_project.equipment[${index}].key`, 80); if (keys.has(key)) throw new FamilyEvidenceError("EvidenceInvalid", `Duplicate MEP reconstruction key ${key}.`); keys.add(key);
    const discipline = textValue(equipment.discipline, `mep_project.equipment[${index}].discipline`, 40); const category = textValue(equipment.category, `mep_project.equipment[${index}].category`, 60);
    if (!MEP_DISCIPLINES.has(discipline) || !allowedCategories.has(category)) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.equipment[${index}] has an unsupported discipline/category.`);
    for (const field of ["location_confirmed", "type_confirmed", "level_confirmed"]) confirmed(equipment[field], `mep_project.equipment[${index}].${field}`);
    const placementMode = textValue(equipment.placement_mode, `mep_project.equipment[${index}].placement_mode`, 60);
    if (placementMode !== "level_based_non_hosted") findings.push({ status: "blocking", key, reason: "Hosted, face-based and work-plane equipment require an explicit host-aware workflow; only level_based_non_hosted is currently batch-buildable." });
    const citation = sourceLocator(byId, equipment.source, `mep_project.equipment[${index}].source`); sourceCitations.push({ element_key: key, ...citation });
    placementOperations.push({ key, discipline, category, symbol_id: finiteNumber(equipment.family_symbol_id, `mep_project.equipment[${index}].family_symbol_id`, true), level_id: finiteNumber(equipment.level_id, `mep_project.equipment[${index}].level_id`, true), point_mm: projectPoint(equipment.point_mm, `mep_project.equipment[${index}].point_mm`), rotation_degrees: equipment.rotation_degrees === undefined ? 0 : finiteNumber(equipment.rotation_degrees, `mep_project.equipment[${index}].rotation_degrees`), placement_mode: placementMode, source_tag: `DSCons Source MEPF ${key}`, source: citation });
  }
  const elementKeys = new Set(keys);
  const endpoint = (raw: unknown, label: string): Json => {
    const value = objectValue(raw, label); const elementKey = textValue(value.element_key, `${label}.element_key`, 80);
    if (!elementKeys.has(elementKey)) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.element_key references unknown logical element ${elementKey}.`);
    const role = textValue(value.connector_role, `${label}.connector_role`, 80);
    const connectorIndex = value.connector_index === undefined ? null : finiteNumber(value.connector_index, `${label}.connector_index`);
    if (connectorIndex !== null && (!Number.isInteger(connectorIndex) || connectorIndex < 0)) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.connector_index must be a non-negative integer.`);
    const route = routeOperations.find((candidate) => candidate.name === elementKey); const equipment = placementOperations.find((candidate) => candidate.key === elementKey);
    if (route && role !== "start" && role !== "end") throw new FamilyEvidenceError("EvidenceInvalid", `${label}.connector_role must be start or end for a created route.`);
    if (equipment && connectorIndex === null) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.connector_index is required for created equipment because Revit connector roles are not a stable public identifier.`);
    return { element_key: elementKey, connector_role: role, ...(connectorIndex === null ? {} : { connector_index: connectorIndex }) };
  };
  const connectionRequests = objectArray(input.connections, "mep_project.connections", 500).map((request, index) => {
    const key = textValue(request.key, `mep_project.connections[${index}].key`, 80); confirmed(request.engineer_confirmed, `mep_project.connections[${index}].engineer_confirmed`);
    const discipline = textValue(request.discipline, `mep_project.connections[${index}].discipline`, 40); if (!MEP_DISCIPLINES.has(discipline)) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.connections[${index}].discipline is unsupported.`);
    const connectionKind = textValue(request.connection_kind, `mep_project.connections[${index}].connection_kind`, 40);
    if (!new Set(["direct", "elbow", "tee", "transition", "union", "tap"]).has(connectionKind)) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.connections[${index}].connection_kind is unsupported.`);
    const executable = connectionKind === "direct" || connectionKind === "elbow";
    return { key, discipline, connection_kind: connectionKind, from: endpoint(request.from, `mep_project.connections[${index}].from`), to: endpoint(request.to, `mep_project.connections[${index}].to`), status: "post_create_connector_resolution_required", execution: executable ? "atomic_changeset_after_created_id_resolution" : "separate_preview_not_implemented", reason: executable ? "Resolve deterministic connectors from elements created earlier in the same rollback-previewed Change Set, then verify connectivity after commit." : "Tee, transition, union and tap require a separately certified connection workflow." };
  });
  const insulationRequests = objectArray(input.insulation, "mep_project.insulation", 500).map((request, index) => {
    const routeKey = textValue(request.route_key, `mep_project.insulation[${index}].route_key`, 80); const route = routeOperations.find((candidate) => candidate.name === routeKey);
    if (!route || !new Set(["pipe", "duct"]).has(String(route.kind))) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.insulation[${index}].route_key must reference a Pipe or Duct route.`);
    confirmed(request.engineer_confirmed, `mep_project.insulation[${index}].engineer_confirmed`);
    return { route_key: routeKey, envelope_kind: "insulation", material_type_id: finiteNumber(request.material_type_id, `mep_project.insulation[${index}].material_type_id`, true), thickness_mm: finiteNumber(request.thickness_mm, `mep_project.insulation[${index}].thickness_mm`, true), status: "created_route_resolution_required", execution: "atomic_changeset_after_created_route_resolution" };
  });
  const liningRequests = objectArray(input.lining, "mep_project.lining", 500).map((request, index) => {
    const routeKey = textValue(request.route_key, `mep_project.lining[${index}].route_key`, 80); const route = routeOperations.find((candidate) => candidate.name === routeKey);
    if (!route || route.kind !== "duct") throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.lining[${index}].route_key must reference a Duct route.`);
    confirmed(request.engineer_confirmed, `mep_project.lining[${index}].engineer_confirmed`);
    return { route_key: routeKey, envelope_kind: "lining", material_type_id: finiteNumber(request.material_type_id, `mep_project.lining[${index}].material_type_id`, true), thickness_mm: finiteNumber(request.thickness_mm, `mep_project.lining[${index}].thickness_mm`, true), status: "created_route_resolution_required", execution: "atomic_changeset_after_created_route_resolution" };
  });
  const slopePlans = objectArray(input.slope_plans, "mep_project.slope_plans", 200).map((request, index) => {
    const routeKey = textValue(request.route_key, `mep_project.slope_plans[${index}].route_key`, 80); const route = routeOperations.find((candidate) => candidate.name === routeKey);
    if (!route || route.kind !== "pipe") throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.slope_plans[${index}].route_key must reference a Pipe route.`);
    confirmed(request.engineer_confirmed, `mep_project.slope_plans[${index}].engineer_confirmed`);
    const slopePercent = finiteNumber(request.slope_percent, `mep_project.slope_plans[${index}].slope_percent`);
    if (Math.abs(slopePercent) < 0.0001) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.slope_plans[${index}].slope_percent must be non-zero.`);
    return { route_key: routeKey, slope_percent: slopePercent, start_invert_mm: finiteNumber(request.start_invert_mm, `mep_project.slope_plans[${index}].start_invert_mm`), end_invert_mm: finiteNumber(request.end_invert_mm, `mep_project.slope_plans[${index}].end_invert_mm`), direction: slopePercent > 0 ? "rising" : "falling", status: "engineering_plan_only", execution: "operator_assisted_until_runtime_certified" };
  });
  const circuitRequests = objectArray(input.electrical_circuits, "mep_project.electrical_circuits", 500).map((request, index) => {
    const key = textValue(request.key, `mep_project.electrical_circuits[${index}].key`, 80); const loadElementKey = textValue(request.load_element_key, `mep_project.electrical_circuits[${index}].load_element_key`, 80);
    if (!elementKeys.has(loadElementKey)) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.electrical_circuits[${index}].load_element_key references an unknown logical element.`);
    const loadEquipment = placementOperations.find((candidate) => candidate.key === loadElementKey);
    if (!loadEquipment || loadEquipment.discipline !== "electrical") throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.electrical_circuits[${index}].load_element_key must reference electrical equipment created by this plan.`);
    confirmed(request.engineer_confirmed, `mep_project.electrical_circuits[${index}].engineer_confirmed`);
    const panelElementKey = request.panel_element_key === undefined ? null : textValue(request.panel_element_key, `mep_project.electrical_circuits[${index}].panel_element_key`, 80);
    const panelElementId = request.panel_element_id === undefined ? null : finiteNumber(request.panel_element_id, `mep_project.electrical_circuits[${index}].panel_element_id`, true);
    if ((panelElementKey === null) === (panelElementId === null)) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.electrical_circuits[${index}] requires exactly one panel_element_key or panel_element_id.`);
    if (panelElementKey !== null) {
      const panelEquipment = placementOperations.find((candidate) => candidate.key === panelElementKey);
      if (!panelEquipment || panelEquipment.discipline !== "electrical" || panelEquipment.category !== "electrical_equipment") throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.electrical_circuits[${index}].panel_element_key must reference electrical_equipment created by this plan.`);
    }
    return { key, load_element_key: loadElementKey, ...(panelElementKey === null ? { panel_element_id: panelElementId } : { panel_element_key: panelElementKey }), circuit_type: textValue(request.circuit_type, `mep_project.electrical_circuits[${index}].circuit_type`, 80), poles: finiteNumber(request.poles, `mep_project.electrical_circuits[${index}].poles`, true), voltage_v: finiteNumber(request.voltage_v, `mep_project.electrical_circuits[${index}].voltage_v`, true), status: "separate_preview_required", execution: "not_implemented", reason: "Circuit creation, panel assignment, phase and load propagation require electrical connector read-back and a separately certified Project workflow." };
  });
  const coordinationRequests: Json[] = objectArray(input.penetrations, "mep_project.penetrations", 500).map((request, index) => {
    const key = textValue(request.key, `mep_project.penetrations[${index}].key`, 80); const hostCategory = textValue(request.host_category, `mep_project.penetrations[${index}].host_category`, 40);
    if (!new Set(["wall", "floor", "roof", "structural_framing", "structural_foundation"]).has(hostCategory)) throw new FamilyEvidenceError("EvidenceInvalid", `mep_project.penetrations[${index}].host_category is unsupported.`);
    return { key, host_category: hostCategory, route_key: request.route_key === undefined ? null : textValue(request.route_key, `mep_project.penetrations[${index}].route_key`, 80), required_before_routing: request.required_before_routing === true, sleeve_required: request.sleeve_required === true, fire_stopping_required: request.fire_stopping_required === true, responsibility: request.responsibility === undefined ? "unassigned" : textValue(request.responsibility, `mep_project.penetrations[${index}].responsibility`, 120), status: "coordination_request_only", reason: "Penetrations, sleeves and fire stopping require a confirmed host element, opening shape, clearance, fire rating and responsibility before a separate Preview." };
  });
  for (const request of coordinationRequests) if (request.route_key !== null && !elementKeys.has(String(request.route_key))) throw new FamilyEvidenceError("EvidenceInvalid", `Penetration ${String(request.key)} references unknown route ${String(request.route_key)}.`);
  if (coordinationRequests.some((request) => request.required_before_routing === true)) findings.push({ status: "blocking", reason: "At least one penetration is marked required_before_routing and must be resolved before route Apply." });
  const executableConnectionRequests = connectionRequests.filter((request) => request.execution === "atomic_changeset_after_created_id_resolution");
  const deferredConnectionRequests = connectionRequests.filter((request) => request.execution !== "atomic_changeset_after_created_id_resolution");
  const deferredRequests = [...deferredConnectionRequests, ...slopePlans, ...circuitRequests];
  if (deferredRequests.length > 0) findings.push({ status: "operator_assisted", reason: `${deferredRequests.length} post-create MEPF request(s) are documented but excluded from executable Change Set operations until their separate runtime workflow is certified.` });
  if (slopePlans.length > 0) findings.push({ status: "blocking", reason: "Slope/invert plans are preserved for engineering review, but horizontal placeholder routes must not be applied before a slope workflow is runtime-certified." });
  const blockers = findings.filter((finding) => finding.status === "blocking");
  const operations: Json[] = [];
  if (routeOperations.length > 0) operations.push({ operation: "model_create_batch", arguments: { routes: routeOperations.map(({ source: _source, discipline: _discipline, service: _service, ...route }) => route) } });
  if (placementOperations.length > 0) operations.push({ operation: "mep_place_equipment_batch", arguments: { instances: placementOperations.map(({ source: _source, discipline: _discipline, ...placement }) => placement) } });
  if (executableConnectionRequests.length > 0) operations.push({ operation: "mep_connect_created_batch", arguments: { connections: executableConnectionRequests.map(({ status: _status, execution: _execution, reason: _reason, discipline: _discipline, ...request }) => request) } });
  if (insulationRequests.length + liningRequests.length > 0) operations.push({ operation: "mep_apply_created_envelopes_batch", arguments: { envelopes: [...insulationRequests, ...liningRequests].map(({ status: _status, execution: _execution, ...request }) => request) } });
  const phases = [
    { phase: 0, key: "reference_alignment", execution: "human_confirmed_input", includes: ["coordinate_system", "levels", "types", "systems"] },
    { phase: 1, key: "route_and_equipment_creation", execution: blockers.length === 0 ? "changeset_preview_candidate" : "blocked", includes: ["routes", "equipment"] },
    { phase: 2, key: "connector_network", execution: connectionRequests.length === 0 ? "not_requested" : deferredConnectionRequests.length === 0 ? "same_atomic_changeset_candidate" : "partially_executable", requests: connectionRequests },
    { phase: 3, key: "slope_insulation_lining", execution: slopePlans.length > 0 ? "slope_blocked_envelopes_not_applied" : insulationRequests.length + liningRequests.length > 0 ? "same_atomic_changeset_candidate" : "not_requested", slope_plans: slopePlans, insulation: insulationRequests, lining: liningRequests },
    { phase: 4, key: "electrical_circuit_and_panel", execution: circuitRequests.length > 0 ? "separate_preview_not_implemented" : "not_requested", requests: circuitRequests },
    { phase: 5, key: "penetration_sleeve_firestop", execution: coordinationRequests.length > 0 ? "coordination_only" : "not_requested", requests: coordinationRequests },
    { phase: 6, key: "independent_verification", execution: "mandatory_after_each_apply", includes: ["element_geometry", "type", "level", "system", "size", "connector_network"] },
  ];
  const planPayload = { discipline_priority: "MEPF", evidence: records.map((record) => ({ evidence_id: record.evidence_id, source_kind: record.source_kind, sha256: record.sha256 })), routes: routeOperations, equipment: placementOperations, connection_requests: connectionRequests, insulation_requests: insulationRequests, lining_requests: liningRequests, slope_plans: slopePlans, electrical_circuit_requests: circuitRequests, coordination_requests: coordinationRequests, phases, source_citations: sourceCitations, confirmations };
  const planHash = createHash("sha256").update(JSON.stringify(planPayload)).digest("hex");
  return {
    status: blockers.length === 0 ? "ready_for_fresh_revit_preview" : "requires_engineering_review",
    project_reconstruction_plan: { schema_version: "1.1", record_kind: "project_reconstruction_plan", plan_id: `mepf-plan-${planHash.slice(0, 24)}`, plan_sha256: planHash, priority: "MEPF", architecture_structure_role: "levels_grids_hosts_clearance_only", routes: routeOperations, equipment: placementOperations, connection_requests: connectionRequests, insulation_requests: insulationRequests, lining_requests: liningRequests, slope_plans: slopePlans, electrical_circuit_requests: circuitRequests, coordination_requests: coordinationRequests, execution_phases: phases, source_citations: sourceCitations, confirmations },
    bim_changeset_template: { name: `MEPF source reconstruction ${planHash.slice(0, 12)}`, operations: blockers.length === 0 ? operations : [], blocked_operation_count: blockers.length === 0 ? 0 : operations.length },
    findings: [...findings, { status: "boundary", reason: "Architecture and structure are used only for Levels/Grids, hosting and clearance. Concealed services, circuiting, slope, fittings beyond routing preferences, penetrations and fire stopping are never inferred from visual similarity." }],
  };
}

export function previewSourceToRevit(args: Record<string, unknown>): Json {
  const mapping = textValue(args.mapping, "mapping", 40);
  if (!MAPPINGS.has(mapping)) throw new FamilyEvidenceError("EvidenceInvalid", "mapping is not supported.");
  const ids = sourceIds(args.evidence_ids); const records = ids.map(sourceRecord);
  if (mapping !== "image_family" && mapping !== "mep_project_reconstruction" && records.length !== 1) throw new FamilyEvidenceError("EvidenceInvalid", `${mapping} requires exactly one SourceEvidence record.`);
  const preliminary = mapping === "mep_project_reconstruction" ? mepProjectProposal(records, args) : mapping === "image_family" ? imageProposal(records, args) : mapping === "pdf_catalog_family" ? pdfCatalogProposal(records[0], args) : dxfProposal(mapping, records[0], args);
  const mapped = withGovernanceGates(args, mapping, ids, preliminary);
  const citations = records.map((record) => ({ evidence_id: record.evidence_id, source_kind: record.source_kind, sha256: record.sha256, revision: record.revision }));
  const proposalHash = createHash("sha256").update(JSON.stringify({ mapping, citations, mapped })).digest("hex");
  return {
    schema_version: "1.0",
    record_kind: "source_to_revit_proposal",
    proposal_id: `proposal-${proposalHash.slice(0, 24)}`,
    mapping,
    citations,
    ...mapped,
    model_changed: false,
    boundary: "This is a read-only proposal. It is not a FamilySpec, Revit preview token, Change Set, runtime verification or authorization to Apply.",
  };
}
