import { createHash } from "node:crypto";
import { readFileSync, realpathSync, statSync } from "node:fs";
import path from "node:path";
import { FamilyEvidenceError, readEvidenceRecord, recordLocalEvidenceRecord } from "./family-evidence.js";

type Json = Record<string, unknown>;

const MAX_CATALOG_BYTES = 20 * 1024 * 1024;
const MAX_CATALOG_ROWS = 10_000;
const MAX_CATALOG_COLUMNS = 80;
const MAX_FIELD_LENGTH = 2_000;
const MAPPINGS = new Set(["family_blueprint", "family_profile", "symbolic_lines", "model_lines", "detail_lines", "route_centerline", "image_family", "pdf_catalog_family", "mep_project_reconstruction"]);
const TARGET_KINDS = new Set(["family", "project", "route", "linework"]);
const FAMILY_DECISIONS = new Set(["compatible_rfa", "manufacturer_catalogue", "approved_blueprint", "unavailable", "not_applicable"]);
const NUMERIC_CATALOG_TYPES = new Set(["length", "number", "integer", "angle"]);
const CATALOG_TYPES = new Set([...NUMERIC_CATALOG_TYPES, "text"]);
const UNITS = new Set(["mm", "cm", "m", "inch", "ft", "degree", "radian", "none"]);

function sha256(value: Uint8Array | string): string { return createHash("sha256").update(value).digest("hex"); }
function identifier(prefix: string, ...parts: unknown[]): string { return `${prefix}-${sha256(parts.map((part) => JSON.stringify(part)).join("\u001f")).slice(0, 24)}`; }
function clone<T>(value: T): T { return JSON.parse(JSON.stringify(value)) as T; }
function object(value: unknown, label: string): Json {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be an object.`);
  return value as Json;
}
function array(value: unknown, label: string, maximum = 1_000): unknown[] {
  if (!Array.isArray(value) || value.length > maximum) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be an array with at most ${maximum} entries.`);
  return value;
}
function text(value: unknown, label: string, maximum = 160): string {
  if (typeof value !== "string") throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be a string.`);
  const result = value.trim();
  if (!result || result.length > maximum || /[\u0000-\u001f\u007f]/.test(result)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must contain 1..${maximum} visible characters.`);
  return result;
}
function optionalText(value: unknown, label: string, maximum = 160): string | undefined { return value === undefined || value === null ? undefined : text(value, label, maximum); }
function finite(value: unknown, label: string, positive = false): number {
  const result = Number(value);
  if (!Number.isFinite(result) || (positive && result <= 0)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be ${positive ? "positive" : "finite"}.`);
  return result;
}
function sourceEvidenceIds(value: unknown): string[] {
  const values = array(value, "evidence_ids", 20).map((item, index) => text(item, `evidence_ids[${index}]`, 96));
  if (values.length === 0 || new Set(values).size !== values.length) throw new FamilyEvidenceError("EvidenceInvalid", "evidence_ids must contain 1..20 unique immutable SourceEvidence ids.");
  for (const id of values) {
    const record = readEvidenceRecord(id);
    if (record.record_kind !== "source_evidence_v3" || record.schema_version !== "3.0") throw new FamilyEvidenceError("EvidenceInvalid", `${id} is not immutable SourceEvidence v3.`);
  }
  return values;
}
function sameSet(left: string[], right: string[]): boolean { return left.length === right.length && left.every((value) => right.includes(value)); }
function sourceCitations(ids: string[]): Json[] { return ids.map((id) => { const record = readEvidenceRecord(id); return { evidence_id: id, sha256: record.sha256, revision: record.revision ?? null, source_kind: record.source_kind }; }); }
function factState(value: unknown, label: string): { status: "confirmed" | "uncertain" | "missing"; value?: unknown } {
  if (value === true || value === "confirmed") return { status: "confirmed", value: true };
  if (value === false || value === "missing" || value === undefined || value === null) return { status: "missing" };
  if (value === "uncertain") return { status: "uncertain" };
  const entry = object(value, label); const state = String(entry.status ?? (entry.value === undefined ? "missing" : "confirmed"));
  if (state !== "confirmed" && state !== "uncertain" && state !== "missing") throw new FamilyEvidenceError("EvidenceInvalid", `${label}.status must be confirmed, uncertain or missing.`);
  const normalizedState = state as "confirmed" | "uncertain" | "missing";
  if (normalizedState === "confirmed" && entry.value === undefined) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.value is required when status=confirmed.`);
  return normalizedState === "confirmed" ? { status: normalizedState, value: clone(entry.value) } : { status: normalizedState };
}
function requirements(targetKind: string, mapping: string, facts: Json): string[] {
  const base = ["source_revision", "source_locator"];
  if (targetKind === "family") {
    const values = [...base, "units_scale", "target_family_category", "family_source_decision"];
    if (mapping === "image_family" || mapping === "pdf_catalog_family") values.push("dimensions_or_catalogue_rows");
    return values;
  }
  if (targetKind === "route") return [...base, "units_scale", "discipline_service", "revit_type", "level_elevation", "nominal_size", "routing_geometry"];
  if (targetKind === "linework") return [...base, "units_scale", "target_family_category"];
  const values = [...base, "coordinate_system", "level_mapping", "type_mapping", "system_mapping", "routing_geometry"];
  if (facts.has_equipment === true) values.push("family_source_decision");
  return values;
}
function familyDecision(value: unknown): { outcome: string; confirmed: boolean; record_id?: string } {
  const entry = object(value, "facts.family_source_decision"); const outcome = text(entry.outcome, "facts.family_source_decision.outcome", 48);
  if (!FAMILY_DECISIONS.has(outcome)) throw new FamilyEvidenceError("EvidenceInvalid", "facts.family_source_decision.outcome is unsupported.");
  if (entry.engineer_confirmed !== true) throw new FamilyEvidenceError("EvidenceInvalid", "facts.family_source_decision.engineer_confirmed must be true.");
  const recordId = optionalText(entry.compatibility_record_id, "facts.family_source_decision.compatibility_record_id", 96);
  if (outcome === "compatible_rfa" && !recordId) throw new FamilyEvidenceError("EvidenceInvalid", "A compatible_rfa decision requires a family_compatibility_record_id.");
  return { outcome, confirmed: true, ...(recordId ? { record_id: recordId } : {}) };
}

/**
 * The one place that turns a short, incomplete source payload into a batched
 * engineering question set. It does not create Revit work or infer facts from
 * layer names, visual resemblance, or prior turns.
 */
export function assessMepfEvidenceReadiness(args: Record<string, unknown>): Json {
  const targetKind = text(args.target_kind, "target_kind", 20); if (!TARGET_KINDS.has(targetKind)) throw new FamilyEvidenceError("EvidenceInvalid", "target_kind must be family, project, route or linework.");
  const mapping = text(args.mapping, "mapping", 48); if (!MAPPINGS.has(mapping)) throw new FamilyEvidenceError("EvidenceInvalid", "mapping is unsupported.");
  const ids = sourceEvidenceIds(args.evidence_ids); const facts = args.facts === undefined ? {} : object(args.facts, "facts");
  const required = requirements(targetKind, mapping, facts); const matrix: Json[] = []; const questions: Json[] = []; const blockers: Json[] = [];
  for (const field of required) {
    let state: { status: "confirmed" | "uncertain" | "missing"; value?: unknown };
    if (field === "family_source_decision" && facts[field] !== undefined) {
      const decision = familyDecision(facts[field]);
      state = decision.outcome === "unavailable" ? { status: "missing" } : { status: "confirmed", value: decision };
      if (decision.outcome === "unavailable") blockers.push({ field, reason: "No compatible RFA, approved manufacturer catalogue or approved Blueprint is available. The dependent Family/equipment scope is blocked." });
    } else state = factState(facts[field], `facts.${field}`);
    matrix.push({ field, required: true, status: state.status, ...(state.value === undefined ? {} : { value: state.value }) });
    if (state.status !== "confirmed") questions.push({ priority: field === "source_revision" || field === "source_locator" || field === "family_source_decision" ? "blocking" : "required", field, question: questionFor(field, targetKind), status: state.status });
  }
  const status = questions.length === 0 && blockers.length === 0 ? "ready_for_proposal" : "needs_information";
  const record: Json = {
    schema_version: "1.0", record_kind: "mepf_evidence_readiness_v1", record_id: identifier("readiness", targetKind, mapping, ids, facts),
    target_kind: targetKind, mapping, evidence_ids: ids, citations: sourceCitations(ids), matrix, questions, blockers, status,
    boundary: "Readiness only proves explicitly confirmed intake facts. It does not prove Family behavior, CAD annotation meaning, engineering calculations or Revit runtime behavior.",
  };
  return recordLocalEvidenceRecord(record);
}
function questionFor(field: string, targetKind: string): string {
  const questions: Record<string, string> = {
    source_revision: "Nguồn/revision nào đang có hiệu lực? Hãy xác nhận đúng file và revision.",
    source_locator: "Hãy chỉ ra locator bất biến (page/path/block, CAD handle/layer, hoặc image region) cho đối tượng này.",
    units_scale: "Đơn vị và scale/anchor nào đã được kỹ sư xác nhận?",
    target_family_category: "Family sẽ thuộc category và hosting/placement behavior nào?",
    family_source_decision: "Anh/chị có RFA đúng phiên bản, catalogue/Type Catalog của hãng, hay Blueprint đã phê duyệt? Nếu không có, phần phụ thuộc sẽ bị chặn.",
    dimensions_or_catalogue_rows: "Kích thước từng Type hoặc các dòng catalogue nào đã được xác nhận và có provenance?",
    discipline_service: "Discipline và service (ví dụ CHWS, SA, drainage, ELV) là gì?",
    revit_type: "Revit Type/System Type/FamilySymbol nào đã được đối chiếu trong model đích?",
    level_elevation: "Level và elevation/invert nào đã được xác nhận?",
    nominal_size: "Nominal size, shape, material/standard nào áp dụng?",
    routing_geometry: "Tim tuyến/hình học, hướng, và ý đồ đầu nối nào đã được xác nhận?",
    coordinate_system: "Datum/origin/rotation nào liên kết nguồn với Project đích?",
    level_mapping: "Mapping Level nguồn → Level Revit đích nào đã được duyệt?",
    type_mapping: "Mapping Type/Family nguồn → resource đích nào đã được duyệt?",
    system_mapping: "System/service mapping nào đã được kỹ sư xác nhận?",
  };
  return questions[field] ?? `Hãy xác nhận trường bắt buộc ${field} cho ${targetKind}.`;
}
export function requireMepfReadiness(recordId: unknown, targetKind: string, mapping: string, evidenceIds: string[]): Json {
  const id = text(recordId, "readiness_record_id", 96); const record = readEvidenceRecord(id);
  if (record.record_kind !== "mepf_evidence_readiness_v1" || record.status !== "ready_for_proposal") throw new FamilyEvidenceError("EvidenceInvalid", "A ready mepf_evidence_readiness record is required before this proposal can be preview-ready.");
  if (record.target_kind !== targetKind || record.mapping !== mapping || !sameSet((record.evidence_ids as unknown[]).map(String), evidenceIds)) throw new FamilyEvidenceError("EvidenceConflict", "Readiness record does not match the target kind, mapping or immutable source evidence.");
  return record;
}

/**
 * Binds CAD callout meaning only when a human supplies an exact handle-based
 * association.  In particular, it deliberately never chooses the nearest
 * curve to a leader endpoint: crowded MEP drawings make that unsafe.
 */
export function assessCadAnnotations(args: Record<string, unknown>): Json {
  const evidenceId = text(args.evidence_id, "evidence_id", 96); const evidence = readEvidenceRecord(evidenceId);
  if (evidence.record_kind !== "source_evidence_v3" || !["dxf", "dwg"].includes(String(evidence.source_kind))) throw new FamilyEvidenceError("EvidenceInvalid", "cad_annotation_assess requires immutable DXF/DWG SourceEvidence v3.");
  const geometry = object(evidence.geometry, "SourceEvidence.geometry"); const entities = array(geometry.entities ?? [], "SourceEvidence.geometry.entities", 50_000).map((item, index) => object(item, `SourceEvidence.geometry.entities[${index}]`));
  const byHandle = new Map<string, Json>(); for (const entity of entities) { const handle = entity.handle === null || entity.handle === undefined ? "" : String(entity.handle); if (handle) byHandle.set(handle, entity); }
  const requested = args.required_metadata === undefined ? [] : array(args.required_metadata, "required_metadata", 20).map((item, index) => text(item, `required_metadata[${index}]`, 48));
  const available = new Set<string>(); for (const entity of entities) {
    const type = String(entity.type); if (type === "TEXT") available.add("text"); if (type === "MTEXT") available.add("mtext"); if (type === "ATTRIB") available.add("block_attribute"); if (type === "DIMENSION") available.add("dimension"); if (type === "LEADER") available.add("leader");
  }
  const unavailable = requested.filter((item) => !available.has(item)); const associations = array(args.associations ?? [], "associations", 500).map((item, index) => object(item, `associations[${index}]`));
  const assessments: Json[] = []; const annotationHandles = new Set<string>();
  for (const [index, association] of associations.entries()) {
    const annotationHandle = text(association.annotation_handle, `associations[${index}].annotation_handle`, 96); const targetHandle = text(association.target_entity_handle, `associations[${index}].target_entity_handle`, 96);
    if (annotationHandles.has(annotationHandle)) throw new FamilyEvidenceError("EvidenceInvalid", `Annotation ${annotationHandle} appears in more than one association.`); annotationHandles.add(annotationHandle);
    const annotation = byHandle.get(annotationHandle); const target = byHandle.get(targetHandle);
    if (!annotation || !target) throw new FamilyEvidenceError("EvidenceInvalid", `associations[${index}] references an annotation or target handle absent from immutable CAD evidence.`);
    if (!new Set(["TEXT", "MTEXT", "ATTRIB", "DIMENSION"]).has(String(annotation.type))) throw new FamilyEvidenceError("EvidenceInvalid", `associations[${index}].annotation_handle must identify TEXT, MTEXT, ATTRIB or DIMENSION.`);
    if (new Set(["TEXT", "MTEXT", "ATTRIB", "DIMENSION", "LEADER", "INSERT"]).has(String(target.type))) throw new FamilyEvidenceError("EvidenceInvalid", `associations[${index}].target_entity_handle must identify drawable geometry, not annotation/block metadata.`);
    const leaderHandle = association.leader_handle === undefined ? undefined : text(association.leader_handle, `associations[${index}].leader_handle`, 96);
    if (leaderHandle !== undefined) { const leader = byHandle.get(leaderHandle); if (!leader || leader.type !== "LEADER") throw new FamilyEvidenceError("EvidenceInvalid", `associations[${index}].leader_handle must identify an immutable LEADER entity.`); }
    if (association.engineer_confirmed !== true) throw new FamilyEvidenceError("EvidenceInvalid", `associations[${index}].engineer_confirmed must be true.`);
    const semantics = object(association.semantics, `associations[${index}].semantics`);
    for (const property of Object.keys(semantics)) if (!new Set(["service", "system", "nominal_size", "elevation", "material", "standard", "description", "tag"]).has(property)) throw new FamilyEvidenceError("EvidenceInvalid", `associations[${index}].semantics.${property} is unsupported.`);
    assessments.push({ annotation_handle: annotationHandle, annotation_type: annotation.type, annotation_text: annotation.text ?? annotation.text_override ?? null, leader_handle: leaderHandle ?? null, target_entity_handle: targetHandle, target_entity_type: target.type, semantics: clone(semantics), status: "engineer_confirmed_handle_mapping" });
  }
  const status = unavailable.length > 0 ? "requires_trusted_adapter" : assessments.length === 0 ? "needs_information" : "assessed";
  const record: Json = { schema_version: "1.0", record_kind: "cad_annotation_assessment_v1", record_id: identifier("cad-annotation", evidenceId, requested, associations), evidence_id: evidenceId, evidence_sha256: evidence.sha256, requested_metadata: requested, available_metadata: [...available].sort(), unavailable_metadata: unavailable, associations: assessments, status, boundary: unavailable.length > 0 ? "DXF parser does not claim MLEADER, table or xref metadata. Provide an SHA-256-bound Autodesk/ODA trusted manifest with that metadata; raster OCR is not a substitute." : "Text/attribute/dimension values are preserved only as human-confirmed semantics. The tool does not infer leader targets from distance or visual layout." };
  return recordLocalEvidenceRecord(record);
}

function catalogFile(sourcePath: unknown, approvedDirectory: unknown): { source: string; bytes: Buffer } {
  const directory = realpathSync(text(approvedDirectory, "approved_catalog_directory", 1024)); const source = realpathSync(text(sourcePath, "source_path", 1024));
  const relative = path.relative(directory, source);
  if (!relative || relative.startsWith("..") || path.isAbsolute(relative)) throw new FamilyEvidenceError("PathBlocked", "Catalogue source_path must be a file below approved_catalog_directory, not the directory itself.");
  const extension = path.extname(source).toLowerCase(); if (extension !== ".csv" && extension !== ".tsv") throw new FamilyEvidenceError("EvidenceInvalid", "Manufacturer catalogue must be a .csv or .tsv file.");
  const stats = statSync(source); if (!stats.isFile() || stats.size <= 0 || stats.size > MAX_CATALOG_BYTES) throw new FamilyEvidenceError("EvidenceInvalid", `Catalogue must be a non-empty file at most ${MAX_CATALOG_BYTES / 1024 / 1024} MB.`);
  return { source, bytes: readFileSync(source) };
}
function parseDelimited(bytes: Buffer, separator: string): string[][] {
  let input = bytes.toString("utf8"); if (input.charCodeAt(0) === 0xfeff) input = input.slice(1);
  if (input.includes("\u0000")) throw new FamilyEvidenceError("EvidenceInvalid", "Catalogue contains a NUL byte.");
  const rows: string[][] = []; let row: string[] = []; let field = ""; let quoted = false;
  const finishField = (): void => { if (field.length > MAX_FIELD_LENGTH) throw new FamilyEvidenceError("EvidenceInvalid", `Catalogue field exceeds ${MAX_FIELD_LENGTH} characters.`); row.push(field); field = ""; };
  const finishRow = (): void => { finishField(); if (row.some((value) => value.length > 0)) rows.push(row); row = []; if (rows.length > MAX_CATALOG_ROWS + 1) throw new FamilyEvidenceError("EvidenceInvalid", `Catalogue exceeds ${MAX_CATALOG_ROWS} data rows.`); };
  for (let index = 0; index < input.length; index += 1) {
    const char = input[index];
    if (quoted) { if (char === '"' && input[index + 1] === '"') { field += '"'; index += 1; } else if (char === '"') quoted = false; else field += char; continue; }
    if (char === '"') { if (field.length !== 0) throw new FamilyEvidenceError("EvidenceInvalid", "Catalogue quote must start at the beginning of a field."); quoted = true; }
    else if (char === separator) finishField();
    else if (char === "\n") finishRow();
    else if (char !== "\r") field += char;
  }
  if (quoted) throw new FamilyEvidenceError("EvidenceInvalid", "Catalogue has an unterminated quoted field.");
  if (field.length > 0 || row.length > 0) finishRow();
  if (rows.length < 2) throw new FamilyEvidenceError("EvidenceInvalid", "Catalogue requires a header and at least one data row.");
  if (rows[0].length < 2 || rows[0].length > MAX_CATALOG_COLUMNS || rows.some((entry) => entry.length !== rows[0].length)) throw new FamilyEvidenceError("EvidenceInvalid", "Catalogue rows must have one consistent 2..80-column shape.");
  return rows;
}
function declaredColumns(value: unknown): Json[] {
  const items = array(value, "columns", MAX_CATALOG_COLUMNS).map((item, index) => object(item, `columns[${index}]`));
  if (items.length < 2) throw new FamilyEvidenceError("EvidenceInvalid", "columns requires at least two declared columns.");
  const names = new Set<string>();
  for (const [index, column] of items.entries()) {
    const sourceColumn = text(column.source_column, `columns[${index}].source_column`, 120); if (names.has(sourceColumn.toLocaleLowerCase())) throw new FamilyEvidenceError("EvidenceInvalid", `columns has duplicate source_column ${sourceColumn}.`); names.add(sourceColumn.toLocaleLowerCase());
    const type = text(column.data_type, `columns[${index}].data_type`, 24); if (!CATALOG_TYPES.has(type)) throw new FamilyEvidenceError("EvidenceInvalid", `columns[${index}].data_type is unsupported.`);
    if (type === "length" || type === "angle") { const unit = text(column.unit, `columns[${index}].unit`, 16); if (!UNITS.has(unit)) throw new FamilyEvidenceError("EvidenceInvalid", `columns[${index}].unit is unsupported.`); }
    if (column.minimum !== undefined && column.maximum !== undefined && finite(column.minimum, `columns[${index}].minimum`) > finite(column.maximum, `columns[${index}].maximum`)) throw new FamilyEvidenceError("EvidenceInvalid", `columns[${index}] minimum cannot exceed maximum.`);
  }
  return items;
}
function numericCell(raw: string, type: string, label: string): number {
  if (!/^-?(?:\d+|\d*\.\d+)(?:[eE][+-]?\d+)?$/.test(raw.trim())) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be a canonical numeric value; unit suffixes and locale commas are not accepted.`);
  const value = Number(raw); if (!Number.isFinite(value) || (type === "integer" && !Number.isInteger(value))) throw new FamilyEvidenceError("EvidenceInvalid", `${label} has an invalid ${type} value.`); return value;
}
function lookupDraft(catalog: Json, declarationRaw: unknown): Json | undefined {
  if (declarationRaw === undefined) return undefined;
  const declaration = object(declarationRaw, "lookup_table"); const tableKey = text(declaration.key, "lookup_table.key", 64); const name = text(declaration.name, "lookup_table.name", 80);
  const lookupKeys = array(declaration.lookup_key_columns, "lookup_table.lookup_key_columns", 20).map((item, index) => text(item, `lookup_table.lookup_key_columns[${index}]`, 120));
  const map = object(declaration.column_keys, "lookup_table.column_keys"); const columns = catalog.columns as Json[];
  const columnBySource = new Map(columns.map((column) => [String(column.source_column), column]));
  if (Object.keys(map).length !== columns.length || columns.some((column) => typeof map[String(column.source_column)] !== "string")) throw new FamilyEvidenceError("EvidenceInvalid", "lookup_table.column_keys must map every declared catalogue source column exactly once.");
  const convertedColumns: Json[] = []; const resultingKeys = new Set<string>();
  for (const column of columns) {
    const dataType = String(column.data_type); if (!NUMERIC_CATALOG_TYPES.has(dataType)) throw new FamilyEvidenceError("EvidenceInvalid", `Catalogue column ${String(column.source_column)} is ${dataType}; it cannot be embedded in a Revit numeric lookup table.`);
    const key = text(map[String(column.source_column)], `lookup_table.column_keys.${String(column.source_column)}`, 64); if (resultingKeys.has(key)) throw new FamilyEvidenceError("EvidenceInvalid", `lookup_table.column_keys maps multiple source columns to ${key}.`); resultingKeys.add(key);
    convertedColumns.push({ key, name: String(column.source_column), data_type: dataType });
  }
  if (lookupKeys.length === 0 || lookupKeys.some((source) => !columnBySource.has(source))) throw new FamilyEvidenceError("EvidenceInvalid", "lookup_table.lookup_key_columns must name declared catalogue columns.");
  const rows = (catalog.rows as Json[]).map((row) => {
    const values: Json = {}; for (const column of columns) values[String(map[String(column.source_column)])] = (row.values as Json)[String(column.source_column)]; return { values };
  });
  return { key: tableKey, name, columns: convertedColumns, lookup_columns: lookupKeys.map((source) => String(map[source])), rows, catalogue_provenance: { catalog_record_id: catalog.record_id, catalog_sha256: catalog.sha256, revision: catalog.revision, manufacturer: catalog.manufacturer, column_map: clone(map), lookup_key_source_columns: lookupKeys } };
}

export function inspectManufacturerCatalog(args: Record<string, unknown>): Json {
  const { source, bytes } = catalogFile(args.source_path, args.approved_catalog_directory); const rows = parseDelimited(bytes, path.extname(source).toLowerCase() === ".tsv" ? "\t" : ",");
  const headers = rows[0].map((item) => text(item, "catalogue header", 120)); if (new Set(headers.map((item) => item.toLocaleLowerCase())).size !== headers.length) throw new FamilyEvidenceError("EvidenceInvalid", "Catalogue header names must be unique case-insensitively.");
  const columns = declaredColumns(args.columns); const declaredByName = new Map(columns.map((column) => [String(column.source_column), column]));
  if (declaredByName.size !== headers.length || headers.some((header) => !declaredByName.has(header))) throw new FamilyEvidenceError("EvidenceInvalid", "Declared columns must match the catalogue header exactly; automatic schema mapping is blocked.");
  const keyColumns = array(args.lookup_key_columns, "lookup_key_columns", 20).map((item, index) => text(item, `lookup_key_columns[${index}]`, 120));
  if (keyColumns.length === 0 || keyColumns.some((key) => !declaredByName.has(key))) throw new FamilyEvidenceError("EvidenceInvalid", "lookup_key_columns must name one or more declared columns.");
  const normalizedRows: Json[] = []; const tuples = new Set<string>();
  for (const [rowIndex, raw] of rows.slice(1).entries()) {
    const values: Json = {};
    for (const [columnIndex, header] of headers.entries()) {
      const column = declaredByName.get(header)!; const rawValue = raw[columnIndex].trim(); if (rawValue.length === 0) throw new FamilyEvidenceError("EvidenceInvalid", `Catalogue row ${rowIndex + 2} column ${header} is empty.`);
      const type = String(column.data_type); const value = type === "text" ? text(rawValue, `Catalogue row ${rowIndex + 2} column ${header}`, MAX_FIELD_LENGTH) : numericCell(rawValue, type, `Catalogue row ${rowIndex + 2} column ${header}`);
      if (type !== "text") { const numericValue = Number(value); if (column.minimum !== undefined && numericValue < finite(column.minimum, `columns.${header}.minimum`)) throw new FamilyEvidenceError("EvidenceInvalid", `Catalogue row ${rowIndex + 2} column ${header} is below the approved minimum.`); if (column.maximum !== undefined && numericValue > finite(column.maximum, `columns.${header}.maximum`)) throw new FamilyEvidenceError("EvidenceInvalid", `Catalogue row ${rowIndex + 2} column ${header} exceeds the approved maximum.`); }
      values[header] = value;
    }
    const tuple = JSON.stringify(keyColumns.map((key) => values[key])); if (tuples.has(tuple)) throw new FamilyEvidenceError("EvidenceInvalid", `Catalogue has duplicate lookup key tuple at data row ${rowIndex + 2}.`); tuples.add(tuple); normalizedRows.push({ source_row: rowIndex + 2, values });
  }
  const name = text(args.catalogue_name, "catalogue_name", 120); const manufacturer = text(args.manufacturer, "manufacturer", 120); const revision = text(args.revision, "revision", 80);
  const sourceHash = sha256(bytes); const base: Json = { schema_version: "1.0", record_kind: "manufacturer_catalog_v1", record_id: identifier("catalog", sourceHash, name, manufacturer, revision, columns, keyColumns), catalogue_name: name, manufacturer, revision, source_path: source, sha256: sourceHash, source_file_name: path.basename(source), lookup_key_columns: keyColumns, columns: clone(columns), rows: normalizedRows, approved_mapping: args.approved_mapping === undefined ? null : clone(object(args.approved_mapping, "approved_mapping")), status: "validated", boundary: "This local record validates exact bytes, schema and explicitly declared units/ranges only. It neither guesses units nor proves manufacturer approval, routing preference, connector behavior or Revit runtime compatibility." };
  let revisionImpact: Json = { status: "first_observed" };
  if (args.previous_catalog_record_id !== undefined) {
    const prior = readEvidenceRecord(text(args.previous_catalog_record_id, "previous_catalog_record_id", 96));
    if (prior.record_kind !== "manufacturer_catalog_v1" || prior.manufacturer !== manufacturer || prior.catalogue_name !== name) throw new FamilyEvidenceError("EvidenceConflict", "previous_catalog_record_id must be a validated record for the same manufacturer and catalogue_name.");
    const priorRows = new Map((prior.rows as Json[]).map((row) => [JSON.stringify((prior.lookup_key_columns as string[]).map((key) => (row.values as Json)[key])), JSON.stringify(row.values)]));
    const changed = normalizedRows.filter((row) => priorRows.get(JSON.stringify(keyColumns.map((key) => (row.values as Json)[key]))) !== JSON.stringify(row.values)).length;
    revisionImpact = { status: prior.sha256 === sourceHash ? "unchanged_bytes" : "revision_changed", previous_catalog_record_id: prior.record_id, changed_or_new_rows: changed, previous_row_count: (prior.rows as Json[]).length, current_row_count: normalizedRows.length, required_action: prior.sha256 === sourceHash ? "none" : "Review affected Family Types/parameters and create a new FamilySpec; no existing Family is altered automatically." };
  }
  const record = { ...base, revision_impact: revisionImpact } as Json; const stored = recordLocalEvidenceRecord(record); const draft = lookupDraft(stored, args.lookup_table);
  return { ...stored, ...(draft ? { lookup_table_draft: draft } : {}) };
}

function contract(raw: unknown, label: string): Json {
  const input = object(raw, label); const evidence = object(input.evidence, `${label}.evidence`); const evidenceKind = text(evidence.kind, `${label}.evidence.kind`, 64);
  const verified = evidence.verified_revit_readback === true || evidence.kind === "family_build_artifact_v3" && evidence.verified === true;
  const category = text(input.category, `${label}.category`, 80); const placement = text(input.placement, `${label}.placement`, 80);
  const partType = optionalText(input.part_type, `${label}.part_type`, 80) ?? null; const routingIntent = optionalText(input.routing_intent, `${label}.routing_intent`, 120) ?? null;
  const parameters = array(input.parameters ?? [], `${label}.parameters`, 500).map((item, index) => { const value = object(item, `${label}.parameters[${index}]`); return { name: text(value.name, `${label}.parameters[${index}].name`, 120), data_type: text(value.data_type, `${label}.parameters[${index}].data_type`, 80), scope: text(value.scope, `${label}.parameters[${index}].scope`, 24), shared_guid: optionalText(value.shared_guid, `${label}.parameters[${index}].shared_guid`, 64) ?? null }; });
  const connectors = array(input.connectors ?? [], `${label}.connectors`, 64).map((item, index) => { const value = object(item, `${label}.connectors[${index}]`); return { domain: text(value.domain, `${label}.connectors[${index}].domain`, 40), shape: text(value.shape, `${label}.connectors[${index}].shape`, 40), direction: text(value.direction, `${label}.connectors[${index}].direction`, 40), system_classification: optionalText(value.system_classification, `${label}.connectors[${index}].system_classification`, 80) ?? null }; });
  const nested = array(input.nested_dependencies ?? [], `${label}.nested_dependencies`, 200).map((item, index) => text(item, `${label}.nested_dependencies[${index}]`, 120)).sort();
  return { evidence: { kind: evidenceKind, verified_revit_readback: verified }, category, placement, part_type: partType, routing_intent: routingIntent, parameters: parameters.sort((a, b) => a.name.localeCompare(b.name)), connectors: connectors.sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b))), nested_dependencies: nested };
}
function differences(left: Json, right: Json): Json[] {
  const fields = ["category", "placement", "part_type", "routing_intent", "parameters", "connectors", "nested_dependencies"]; return fields.filter((field) => JSON.stringify(left[field]) !== JSON.stringify(right[field])).map((field) => ({ field, source: left[field], target: right[field] }));
}
export function assessFamilyCompatibility(args: Record<string, unknown>): Json {
  const source = contract(args.source_family, "source_family"); const target = contract(args.target_family, "target_family"); const findings = differences(source, target);
  const verified = (source.evidence as Json).verified_revit_readback === true && (target.evidence as Json).verified_revit_readback === true;
  const status = !verified ? "needs_information" : findings.length > 0 ? "blocked" : "compatible";
  const record: Json = { schema_version: "1.0", record_kind: "family_compatibility_assessment_v1", record_id: identifier("family-compat", source, target), source_family: source, target_family: target, status, findings, questions: verified ? [] : [{ priority: "blocking", question: "Cần Revit family_inspect hoặc Family artifact reopen evidence cho cả Family nguồn và đích; tên/category không phải bằng chứng tương thích." }], boundary: "Compatibility assessment compares declared interfaces backed by supplied Revit read-back evidence. It does not download, search for or substitute a look-alike Family." };
  return recordLocalEvidenceRecord(record);
}

function planHash(plan: Json): string { const value = typeof plan.plan_sha256 === "string" ? plan.plan_sha256 : sha256(JSON.stringify(plan)); return text(value, "reconstruction_plan.plan_sha256", 64); }
function pointComplete(value: unknown): boolean { return !!value && typeof value === "object" && ["x_mm", "y_mm", "z_mm"].every((key) => Number.isFinite(Number((value as Json)[key]))); }
export function reviewMepfEngineering(args: Record<string, unknown>): Json {
  const plan = object(args.reconstruction_plan, "reconstruction_plan"); const hash = planHash(plan); const routes = array(plan.routes ?? [], "reconstruction_plan.routes", 500).map((item, index) => object(item, `reconstruction_plan.routes[${index}]`)); const equipment = array(plan.equipment ?? [], "reconstruction_plan.equipment", 500).map((item, index) => object(item, `reconstruction_plan.equipment[${index}]`)); const findings: Json[] = [];
  for (const [index, route] of routes.entries()) {
    const kind = String(route.kind); const discipline = String(route.discipline); const key = String(route.name ?? route.key ?? index);
    if (!new Set(["pipe", "duct", "conduit", "cable_tray"]).has(kind) || !new Set(["mechanical", "plumbing", "fire_protection", "electrical"]).has(discipline)) findings.push({ severity: "blocking", key, fact: "kind_discipline", reason: "Unsupported route discipline/kind." });
    if ((kind === "pipe" || kind === "duct") && !(Number(route.system_type_id) > 0)) findings.push({ severity: "blocking", key, fact: "system", reason: "Pipe/Duct requires an explicit System Type." });
    if (!(Number(route.type_id) > 0) || !(Number(route.level_id) > 0)) findings.push({ severity: "blocking", key, fact: "type_level", reason: "Route requires an explicit Type and Level." });
    const points = Array.isArray(route.points) ? route.points : Array.isArray(route.points_mm) ? route.points_mm : []; if (points.length < 2 || points.some((point) => !pointComplete(point))) findings.push({ severity: "blocking", key, fact: "route_geometry", reason: "Route needs at least two finite 3D centerline points." });
    const hasDiameter = Number(route.diameter_mm) > 0; const hasRectangular = Number(route.width_mm) > 0 && Number(route.height_mm) > 0; if (!hasDiameter && !hasRectangular) findings.push({ severity: "blocking", key, fact: "nominal_size", reason: "Route needs a supported nominal size profile." });
    if (route.slope_percent !== undefined) findings.push({ severity: "blocking", key, fact: "slope_invert", reason: "Slope/invert needs its separately certified workflow; this review will not flatten it." });
  }
  for (const [index, item] of equipment.entries()) {
    const key = String(item.key ?? index); if (!(Number(item.symbol_id ?? item.family_symbol_id) > 0) || !(Number(item.level_id) > 0) || !pointComplete(item.point_mm)) findings.push({ severity: "blocking", key, fact: "family_level_location", reason: "Equipment requires confirmed FamilySymbol, Level and finite placement point." });
    if (String(item.placement_mode) !== "level_based_non_hosted") findings.push({ severity: "blocking", key, fact: "hosting", reason: "Hosted equipment requires its own verified host-aware workflow." });
  }
  const acknowledgement = args.calculation_boundary_acknowledged === true; if (!acknowledgement) findings.push({ severity: "blocking", fact: "calculation_boundary", reason: "Confirm that hydraulic, airflow/pressure, electrical load and fire hydraulic calculations are not certified by this reconstruction review." });
  const blockers = findings.filter((item) => item.severity === "blocking"); const status = blockers.length === 0 ? "approved_for_preview" : "needs_information";
  const record: Json = { schema_version: "1.0", record_kind: "mepf_engineering_review_v1", record_id: identifier("engineering", hash, findings, acknowledgement), plan_sha256: hash, status, findings, calculation_boundary_acknowledged: acknowledgement, checked: ["service/system-kind", "nominal size profile", "Level/elevation", "slope/invert boundary", "open/hosted equipment boundary", "route-point completeness"], boundary: "This deterministic review does not perform or certify hydraulic, airflow, pressure-loss, fire-hydraulic, electrical load, voltage-drop, short-circuit or panel calculations." };
  return recordLocalEvidenceRecord(record);
}
export function requireEngineeringReview(recordId: unknown, plan: Json): Json {
  const record = readEvidenceRecord(text(recordId, "engineering_review_record_id", 96)); const hash = planHash(plan);
  if (record.record_kind !== "mepf_engineering_review_v1" || record.status !== "approved_for_preview" || record.plan_sha256 !== hash) throw new FamilyEvidenceError("EvidenceInvalid", "A matching approved mepf_engineering_review record is required before a Project reconstruction proposal can be preview-ready.");
  return record;
}

export function assessSourceConflicts(args: Record<string, unknown>): Json {
  const sources = array(args.sources, "sources", 20).map((item, index) => object(item, `sources[${index}]`)); if (sources.length < 2) throw new FamilyEvidenceError("EvidenceInvalid", "source_conflict_assess requires at least two immutable source declarations.");
  const normalized: Json[] = []; const ids = new Set<string>();
  for (const [index, source] of sources.entries()) {
    const evidenceId = text(source.evidence_id, `sources[${index}].evidence_id`, 96); const evidence = readEvidenceRecord(evidenceId); if (evidence.record_kind !== "source_evidence_v3") throw new FamilyEvidenceError("EvidenceInvalid", `sources[${index}] is not SourceEvidence v3.`);
    if (source.sha256 !== undefined && String(source.sha256).toLowerCase() !== String(evidence.sha256).toLowerCase()) throw new FamilyEvidenceError("EvidenceConflict", `sources[${index}].sha256 does not match immutable SourceEvidence.`);
    if (ids.has(evidenceId)) throw new FamilyEvidenceError("EvidenceInvalid", "sources cannot repeat an evidence_id."); ids.add(evidenceId);
    normalized.push({ evidence_id: evidenceId, sha256: evidence.sha256, revision: optionalText(source.revision, `sources[${index}].revision`, 120) ?? evidence.revision ?? null, common_datum_key: text(source.common_datum_key, `sources[${index}].common_datum_key`, 120), facts: clone(object(source.facts, `sources[${index}].facts`)) });
  }
  const datums = new Set(normalized.map((source) => String(source.common_datum_key))); const conflicts: Json[] = []; if (datums.size !== 1) conflicts.push({ key: "common_datum_key", values: normalized.map((source) => ({ evidence_id: source.evidence_id, value: source.common_datum_key })), reason: "Sources are not aligned to one explicitly declared common datum." });
  const fields = new Map<string, Json[]>(); for (const source of normalized) for (const [key, value] of Object.entries(source.facts as Json)) { const values = fields.get(key) ?? []; values.push({ evidence_id: source.evidence_id, value }); fields.set(key, values); }
  for (const [key, values] of fields) if (new Set(values.map((value) => JSON.stringify(value.value))).size > 1) conflicts.push({ key, values, reason: "Immutable sources declare different values." });
  const resolutions = args.resolutions === undefined ? [] : array(args.resolutions, "resolutions", 200).map((item, index) => object(item, `resolutions[${index}]`)); const resolved = new Set<string>();
  for (const [index, resolution] of resolutions.entries()) { const key = text(resolution.key, `resolutions[${index}].key`, 120); const chosen = text(resolution.chosen_evidence_id, `resolutions[${index}].chosen_evidence_id`, 96); if (!ids.has(chosen) || resolution.engineer_confirmed !== true) throw new FamilyEvidenceError("EvidenceInvalid", `resolutions[${index}] requires a source id and explicit engineer_confirmed=true.`); const conflict = conflicts.find((entry) => entry.key === key); if (!conflict || !(conflict.values as Json[]).some((value) => value.evidence_id === chosen)) throw new FamilyEvidenceError("EvidenceInvalid", `resolutions[${index}] does not resolve an observed conflict with the chosen source.`); resolved.add(key); }
  const unresolved = conflicts.filter((conflict) => !resolved.has(String(conflict.key))); const status = unresolved.length === 0 ? "aligned_for_proposal" : "needs_engineer_resolution";
  const record: Json = { schema_version: "1.0", record_kind: "source_conflict_assessment_v1", record_id: identifier("source-conflict", normalized, resolutions), sources: normalized, conflicts, resolutions: clone(resolutions), unresolved_conflicts: unresolved, status, boundary: "This record exposes conflicts between explicitly supplied immutable-source facts. It never selects a revision, datum or technical value on the engineer's behalf." };
  return recordLocalEvidenceRecord(record);
}
export function requireSourceConflictAssessment(recordId: unknown, evidenceIds: string[]): Json {
  const record = readEvidenceRecord(text(recordId, "source_conflict_record_id", 96)); if (record.record_kind !== "source_conflict_assessment_v1" || record.status !== "aligned_for_proposal") throw new FamilyEvidenceError("EvidenceInvalid", "A resolved source_conflict_assessment record is required for a multi-source Project proposal.");
  const actual = (record.sources as Json[]).map((source) => String(source.evidence_id)); if (!sameSet(actual, evidenceIds)) throw new FamilyEvidenceError("EvidenceConflict", "Source-conflict assessment does not match this proposal's immutable evidence set."); return record;
}
