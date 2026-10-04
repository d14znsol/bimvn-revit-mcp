import { createHash } from "node:crypto";

type Json = Record<string, unknown>;

export const FAMILY_BLUEPRINT_SCHEMA_VERSION = "3.0";

export const familyCategories = [
  "mechanical_equipment", "air_terminal", "duct_accessory", "duct_fitting",
  "pipe_accessory", "pipe_fitting", "plumbing_fixture", "sprinkler",
  "electrical_equipment", "electrical_fixture", "lighting_fixture",
  "fire_alarm_device", "data_device", "communication_device", "security_device",
  "nurse_call_device", "telephone_device", "conduit_fitting", "cable_tray_fitting",
  "generic_model", "detail_item", "profile", "annotation", "tag",
] as const;

export const familyTemplateBehaviors = [
  "level_based", "work_plane_based", "face_based", "wall_based", "ceiling_based",
  "floor_based", "roof_based", "line_based", "two_level_based", "adaptive",
  "detail_item", "profile", "annotation", "tag",
] as const;

export const familyPrimitiveKinds = ["extrusion", "revolution", "sweep", "blend", "swept_blend"] as const;
export const familyConnectorDisciplines = ["duct", "pipe", "electrical", "conduit", "cable_tray"] as const;
export const familyCoordinationZonePurposes = [
  "maintenance_clearance", "access_clearance", "service_clearance", "installation_clearance",
  "removal_path", "operation_swing", "connection_interface", "support_interface",
] as const;
export const familyCoordinationZoneSubcategories = [
  "DSCons Coordination Maintenance", "DSCons Coordination Access", "DSCons Coordination Service",
  "DSCons Coordination Installation", "DSCons Coordination Removal Path", "DSCons Coordination Operation Swing",
  "DSCons Coordination Connection Interface", "DSCons Coordination Support Interface",
] as const;
export const familyReferenceTypes = [
  "left", "center_left_right", "right", "front", "center_front_back", "back",
  "bottom", "center_elevation", "top", "strong", "weak", "not_reference",
] as const;
export const familyLightShapeStyles = ["point", "line", "rectangle", "circle"] as const;
export const familyLightDistributionStyles = ["spherical", "hemispherical", "spot", "photometric_web"] as const;
export const familyDetailLevelRepresentationPolicies = ["coarse_medium_2d__fine_3d"] as const;
export const familyLightColorPresets = [
  "d65", "d50", "halogen", "incandescent", "xenon", "quartz", "fluorescent_warm", "fluorescent_cool",
  "fluorescent_white", "fluorescent_daylight", "fluorescent_light_white", "metal_halide", "high_pressure_sodium",
  "low_pressure_sodium", "mercury", "phosphor_mercury",
] as const;
export const familyPartTypes = [
  "normal", "duct_mounted", "junction_box", "attaches_to", "breaks_into",
  "elbow", "tee", "transition", "cross", "cap", "tap_perpendicular",
  "tap_adjustable", "offset", "union", "panel_board", "transformer",
  "switch_board", "other_panel", "equipment_switch", "switch",
  "valve_breaks_into", "spud_perpendicular", "spud_adjustable", "damper",
  "wye", "lateral_tee", "lateral_cross", "pants", "multi_port",
  "valve_normal", "junction_box_tee", "junction_box_cross", "pipe_flange",
  "junction_box_elbow", "channel_cable_tray_elbow",
  "channel_cable_tray_vertical_elbow", "channel_cable_tray_cross",
  "channel_cable_tray_tee", "channel_cable_tray_transition",
  "channel_cable_tray_union", "channel_cable_tray_offset",
  "channel_cable_tray_multi_port", "ladder_cable_tray_elbow",
  "ladder_cable_tray_vertical_elbow", "ladder_cable_tray_cross",
  "ladder_cable_tray_tee", "ladder_cable_tray_transition",
  "ladder_cable_tray_union", "ladder_cable_tray_offset",
  "ladder_cable_tray_multi_port", "inline_sensor", "sensor", "end_cap",
  "pipe_mechanical_coupling",
] as const;
export const familyParameterGroups = [
  "constraints", "geometry", "materials", "mechanical", "mechanical_airflow",
  "electrical", "plumbing", "identity_data", "data", "graphics", "general",
] as const;
export const familyParameterDataTypes = [
  "length", "area", "volume", "number", "integer", "text", "url", "yesno", "angle", "material", "currency",
  "airflow", "flow", "hvac_pressure", "piping_pressure", "electrical_power", "apparent_power",
  "voltage", "current", "frequency", "hvac_temperature", "piping_temperature",
  "number_of_poles", "load_classification", "family_type",
] as const;
// Autodesk documents this portable Type Catalog header vocabulary. MEP-specific
// header/unit tokens must be proven by a real load/select-type run before they
// can be emitted; do not guess a token merely because the Family Parameter has
// a corresponding Revit API data type.
const documentedTypeCatalogParameterDataTypes = new Set<string>([
  "length", "area", "volume", "number", "integer", "text", "url", "yesno", "angle", "material", "currency",
]);
const numericFamilyParameterDataTypes = new Set<string>([
  "length", "area", "volume", "number", "integer", "number_of_poles", "angle", "currency", "airflow", "flow",
  "hvac_pressure", "piping_pressure", "electrical_power", "apparent_power", "voltage",
  "current", "frequency", "hvac_temperature", "piping_temperature",
]);
const connectorClassifications: Record<string, readonly string[]> = {
  duct: ["UndefinedSystemType", "SupplyAir", "ReturnAir", "ExhaustAir", "OtherAir", "Fitting", "Global"],
  pipe: ["UndefinedSystemType", "SupplyHydronic", "ReturnHydronic", "Sanitary", "Vent", "DomesticHotWater", "DomesticColdWater", "OtherPipe", "FireProtectWet", "FireProtectDry", "FireProtectPreaction", "FireProtectOther", "Fitting", "Global"],
  electrical: ["UndefinedSystemType", "Data", "PowerCircuit", "Telephone", "Security", "FireAlarm", "NurseCall", "Controls", "Communication", "PowerBalanced", "PowerUnBalanced"],
};

const keyPattern = /^[a-z][a-z0-9_]{0,63}$/;
const allowedFormula = /^[\p{L}\p{N}_ +\-*/().,<>=]+$/u;
const formulaReservedNames = new Set(["abs", "acos", "and", "asin", "atan", "cos", "if", "not", "or", "pi", "round", "rounddown", "roundup", "sin", "sqrt", "tan"]);
const formulaCapableDataTypes = new Set<string>([...numericFamilyParameterDataTypes, "yesno"]);

function sha256(value: string): string { return createHash("sha256").update(value, "utf8").digest("hex"); }
function clone<T>(value: T): T { return JSON.parse(JSON.stringify(value)) as T; }
function isFormulaIdentifierCharacter(value: string | undefined): boolean { return value !== undefined && /[\p{L}\p{N}_]/u.test(value); }
function formulaParameterReferences(formula: string, parameters: Json[]): string[] {
  const candidates = parameters.map((parameter) => ({ key: String(parameter.key), name: String(parameter.name), normalized: String(parameter.name).toLocaleLowerCase() }))
    .sort((left, right) => right.normalized.length - left.normalized.length || left.key.localeCompare(right.key));
  const normalizedFormula = formula.toLocaleLowerCase(); const references = new Set<string>();
  for (let index = 0; index < normalizedFormula.length;) {
    const candidate = candidates.find((item) => normalizedFormula.startsWith(item.normalized, index)
      && !isFormulaIdentifierCharacter(normalizedFormula[index - 1])
      && !isFormulaIdentifierCharacter(normalizedFormula[index + item.normalized.length]));
    if (!candidate) { index += 1; continue; }
    references.add(candidate.key); index += candidate.normalized.length;
  }
  return [...references].sort();
}
function requireAcyclicFormulaDependencies(dependencies: Map<string, string[]>): void {
  const state = new Map<string, "visiting" | "done">(); const path: string[] = [];
  const visit = (parameterKey: string): void => {
    const current = state.get(parameterKey);
    if (current === "visiting") {
      const start = path.indexOf(parameterKey); throw new Error(`formula dependency cycle detected: ${[...path.slice(start), parameterKey].join(" -> ")}.`);
    }
    if (current === "done") return;
    state.set(parameterKey, "visiting"); path.push(parameterKey);
    for (const dependency of dependencies.get(parameterKey) ?? []) if (dependencies.has(dependency)) visit(dependency);
    path.pop(); state.set(parameterKey, "done");
  };
  for (const parameterKey of dependencies.keys()) visit(parameterKey);
}
function topologicalFormulaApplicationOrder(dependencies: Map<string, string[]>): string[] {
  const visited = new Set<string>(); const ordered: string[] = [];
  const visit = (parameterKey: string): void => {
    if (visited.has(parameterKey)) return;
    visited.add(parameterKey);
    for (const dependency of dependencies.get(parameterKey) ?? []) if (dependencies.has(dependency)) visit(dependency);
    ordered.push(parameterKey);
  };
  for (const parameterKey of dependencies.keys()) visit(parameterKey);
  return ordered;
}
function object(value: unknown, label: string): Json {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error(`${label} must be an object.`);
  return value as Json;
}
function array(value: unknown, label: string): unknown[] {
  if (!Array.isArray(value)) throw new Error(`${label} must be an array.`);
  return value;
}
function string(value: unknown, label: string, max = 120): string {
  if (typeof value !== "string" || !value.trim() || value.length > max) throw new Error(`${label} must be a non-empty string up to ${max} characters.`);
  return value.trim();
}
function key(value: unknown, label: string): string {
  const result = string(value, label, 64);
  if (!keyPattern.test(result)) throw new Error(`${label} must use lower_snake_case and start with a letter.`);
  return result;
}
function oneOf<T extends readonly string[]>(value: unknown, values: T, label: string): T[number] {
  const result = string(value, label);
  if (!values.includes(result as T[number])) throw new Error(`${label} must be one of: ${values.join(", ")}.`);
  return result as T[number];
}
function uniqueKeys(items: Json[], label: string): void {
  const seen = new Set<string>();
  for (const item of items) {
    const itemKey = key(item.key, `${label}.key`);
    if (seen.has(itemKey)) throw new Error(`${label} contains duplicate key: ${itemKey}.`);
    seen.add(itemKey);
  }
}

function finite(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) throw new Error(`${label} must be a finite number.`);
  return value;
}

function positive(value: unknown, label: string): number {
  const result = finite(value, label);
  if (result <= 0) throw new Error(`${label} must be greater than zero.`);
  return result;
}

function boundedInteger(value: unknown, label: string, minimum: number, maximum: number): number {
  const result = finite(value, label);
  if (!Number.isInteger(result) || result < minimum || result > maximum) throw new Error(`${label} must be an integer from ${minimum} to ${maximum}.`);
  return result;
}

type Point3 = { x_mm: number; y_mm: number; z_mm: number };
type Vector3 = { x: number; y: number; z: number };

function point3(value: unknown, label: string): Point3 {
  const point = object(value, label);
  return {
    x_mm: finite(point.x_mm, `${label}.x_mm`),
    y_mm: finite(point.y_mm, `${label}.y_mm`),
    z_mm: finite(point.z_mm, `${label}.z_mm`),
  };
}

function unitVector3(value: unknown, label: string): Vector3 {
  const vector = object(value, label);
  const x = finite(vector.x, `${label}.x`); const y = finite(vector.y, `${label}.y`); const z = finite(vector.z, `${label}.z`);
  const length = Math.hypot(x, y, z);
  if (length < 1e-6) throw new Error(`${label} must be a non-zero direction vector.`);
  return { x: x / length, y: y / length, z: z / length };
}

function distance3(left: Point3, right: Point3): number {
  return Math.hypot(left.x_mm - right.x_mm, left.y_mm - right.y_mm, left.z_mm - right.z_mm);
}

function planeCoordinate(point: Point3, plane: string): number {
  if (plane === "xy") return point.z_mm;
  if (plane === "xz") return point.y_mm;
  return point.x_mm;
}

function planePoint(point: Point3, plane: string): [number, number] {
  if (plane === "xy") return [point.x_mm, point.y_mm];
  if (plane === "xz") return [point.x_mm, point.z_mm];
  return [point.y_mm, point.z_mm];
}

function validateSimpleClosedLoop(points: Point3[], plane: string, label: string): void {
  if (points.length < 3) throw new Error(`${label} requires at least three vertices.`);
  const projected = points.map((point) => planePoint(point, plane));
  const orient = (a: [number, number], b: [number, number], c: [number, number]): number => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
  const onSegment = (a: [number, number], b: [number, number], c: [number, number]): boolean => Math.min(a[0], c[0]) - 1e-6 <= b[0] && b[0] <= Math.max(a[0], c[0]) + 1e-6 && Math.min(a[1], c[1]) - 1e-6 <= b[1] && b[1] <= Math.max(a[1], c[1]) + 1e-6;
  const intersects = (a: [number, number], b: [number, number], c: [number, number], d: [number, number]): boolean => {
    const o1 = orient(a, b, c); const o2 = orient(a, b, d); const o3 = orient(c, d, a); const o4 = orient(c, d, b);
    if ((o1 > 1e-6 && o2 < -1e-6 || o1 < -1e-6 && o2 > 1e-6) && (o3 > 1e-6 && o4 < -1e-6 || o3 < -1e-6 && o4 > 1e-6)) return true;
    return Math.abs(o1) <= 1e-6 && onSegment(a, c, b) || Math.abs(o2) <= 1e-6 && onSegment(a, d, b) || Math.abs(o3) <= 1e-6 && onSegment(c, a, d) || Math.abs(o4) <= 1e-6 && onSegment(c, b, d);
  };
  let twiceArea = 0;
  for (let index = 0; index < projected.length; index += 1) {
    const next = (index + 1) % projected.length;
    if (distance3(points[index], points[next]) < 1e-6) throw new Error(`${label} contains a zero-length closing/profile edge.`);
    twiceArea += projected[index][0] * projected[next][1] - projected[next][0] * projected[index][1];
  }
  for (let first = 0; first < projected.length; first += 1) {
    const firstNext = (first + 1) % projected.length;
    for (let second = first + 1; second < projected.length; second += 1) {
      const secondNext = (second + 1) % projected.length;
      if (first === second || firstNext === second || secondNext === first) continue;
      if (intersects(projected[first], projected[firstNext], projected[second], projected[secondNext])) throw new Error(`${label} self-intersects between edges ${first} and ${second}.`);
    }
  }
  if (Math.abs(twiceArea) < 1e-6) throw new Error(`${label} has zero area.`);
}

export type FamilyBlueprintAssessment = {
  buildable_by_api: boolean;
  supported_features: string[];
  unsupported_features: string[];
  ui_fallback_required: boolean;
  certification_boundary: string;
};

/**
 * Validate a declarative Family blueprint. This is intentionally a bounded
 * data contract; it never accepts scripts, Revit API names, or executable code.
 */
export function validateFamilyBlueprint(raw: unknown): { blueprint: Json; assessment: FamilyBlueprintAssessment } {
  const blueprint = clone(object(raw, "blueprint"));
  // Derived evidence is always recomputed. Never accept a caller-supplied hash
  // or complexity result as proof that a Blueprint is within budget.
  delete blueprint.blueprint_hash;
  delete blueprint.complexity_assessment;
  delete blueprint.resolved_complexity;
  delete blueprint.source_traceability;
  delete blueprint.formula_dependency_graph;
  if (blueprint.schema_version !== FAMILY_BLUEPRINT_SCHEMA_VERSION) throw new Error(`blueprint.schema_version must be ${FAMILY_BLUEPRINT_SCHEMA_VERSION}.`);
  oneOf(blueprint.target_lod, ["LOD_300", "LOD_350"] as const, "blueprint.target_lod");
  const family = object(blueprint.family, "blueprint.family");
  const familyCategory = oneOf(family.category, familyCategories, "blueprint.family.category");
  oneOf(family.template_behavior, familyTemplateBehaviors, "blueprint.family.template_behavior");
  key(family.family_key, "blueprint.family.family_key");
  oneOf(family.primary_axis, ["x", "y", "z"] as const, "blueprint.family.primary_axis");
  const categoriesRequiringPartType = new Set(["duct_accessory", "duct_fitting", "pipe_accessory", "pipe_fitting", "conduit_fitting", "cable_tray_fitting"]);
  const allowedPartTypes: Record<string, readonly string[]> = {
    duct_accessory: ["normal", "duct_mounted", "attaches_to", "breaks_into", "damper", "inline_sensor", "sensor"],
    duct_fitting: ["elbow", "tee", "transition", "cross", "cap", "tap_perpendicular", "tap_adjustable", "offset", "union", "wye", "lateral_tee", "lateral_cross", "pants", "multi_port", "end_cap"],
    pipe_accessory: ["normal", "attaches_to", "breaks_into", "valve_breaks_into", "valve_normal", "inline_sensor", "sensor"],
    pipe_fitting: ["elbow", "tee", "transition", "cross", "cap", "tap_perpendicular", "tap_adjustable", "offset", "union", "wye", "lateral_tee", "lateral_cross", "pants", "multi_port", "spud_perpendicular", "spud_adjustable", "pipe_flange", "pipe_mechanical_coupling", "end_cap"],
    electrical_equipment: ["normal", "junction_box", "panel_board", "transformer", "switch_board", "other_panel", "equipment_switch", "switch"],
    conduit_fitting: ["elbow", "tee", "transition", "cross", "cap", "union", "multi_port", "end_cap", "junction_box", "junction_box_tee", "junction_box_cross", "junction_box_elbow"],
    cable_tray_fitting: familyPartTypes.filter((value) => value.startsWith("channel_cable_tray_") || value.startsWith("ladder_cable_tray_")),
  };
  if (family.part_type !== undefined) {
    const partType = oneOf(family.part_type, familyPartTypes, "blueprint.family.part_type");
    if (!allowedPartTypes[familyCategory]?.includes(partType)) throw new Error(`blueprint.family.part_type ${partType} is not supported for category ${familyCategory}.`);
  }
  if (categoriesRequiringPartType.has(familyCategory) && family.part_type === undefined) throw new Error(`blueprint.family.part_type is required for ${familyCategory} because it controls MEP placement/routing behavior.`);
  for (const setting of ["shared", "work_plane_based", "always_vertical", "cut_with_voids_when_loaded", "maintain_annotation_orientation"]) {
    if (family[setting] !== undefined && typeof family[setting] !== "boolean") throw new Error(`blueprint.family.${setting} must be boolean.`);
  }
  if (family.round_connector_dimension !== undefined) oneOf(family.round_connector_dimension, ["diameter", "radius"] as const, "blueprint.family.round_connector_dimension");

  const requiredSourceFields = array(blueprint.required_source_fields ?? [], "blueprint.required_source_fields").map((item, index) => key(item, `required_source_fields[${index}]`));
  if (new Set(requiredSourceFields).size !== requiredSourceFields.length) throw new Error("blueprint.required_source_fields must be unique.");
  const requiredSourceFieldSet = new Set(requiredSourceFields);

  const parameters = array(blueprint.parameters ?? [], "blueprint.parameters").map((item, index) => object(item, `parameters[${index}]`));
  uniqueKeys(parameters, "blueprint.parameters");
  const parametersByKey = new Map(parameters.map((parameter) => [String(parameter.key), parameter]));
  const parameterNames = new Map<string, string>();
  const sourceParametersByField = new Map<string, string[]>();
  const sharedGuids = new Set<string>();
  for (const parameter of parameters) {
    const parameterName = string(parameter.name, `parameter ${String(parameter.key)}.name`, 120);
    parameter.name = parameterName;
    const normalizedName = parameterName.toLocaleLowerCase();
    const nameOwner = parameterNames.get(normalizedName);
    if (nameOwner) throw new Error(`blueprint.parameters contains duplicate Family Parameter name ${parameterName} on ${nameOwner} and ${String(parameter.key)}.`);
    if (formulaReservedNames.has(normalizedName)) throw new Error(`parameter ${String(parameter.key)}.name ${parameterName} is reserved by Revit formula syntax.`);
    parameterNames.set(normalizedName, String(parameter.key));
    oneOf(parameter.data_type, familyParameterDataTypes, `parameter ${String(parameter.key)}.data_type`);
    oneOf(parameter.scope, ["type", "instance"] as const, `parameter ${String(parameter.key)}.scope`);
    if (parameter.data_type === "load_classification" && (parameter.formula !== undefined || parameter.lookup !== undefined)) throw new Error(`parameter ${String(parameter.key)} with data_type=load_classification cannot use formula or lookup.`);
    if (parameter.data_type === "family_type") {
      oneOf(parameter.family_category, familyCategories, `parameter ${String(parameter.key)}.family_category`);
      if (parameter.scope !== "type") throw new Error(`parameter ${String(parameter.key)} with data_type=family_type currently requires type scope.`);
      if (parameter.shared_guid !== undefined || parameter.formula !== undefined || parameter.lookup !== undefined || parameter.source_field !== undefined || parameter.default !== undefined)
        throw new Error(`parameter ${String(parameter.key)} with data_type=family_type is controlled by nested type options and cannot declare shared/default/source/formula/lookup data.`);
    } else if (parameter.family_category !== undefined) throw new Error(`parameter ${String(parameter.key)}.family_category is only valid for data_type=family_type.`);
    if (parameter.group !== undefined) oneOf(parameter.group, familyParameterGroups, `parameter ${String(parameter.key)}.group`);
    if (parameter.description !== undefined) string(parameter.description, `parameter ${String(parameter.key)}.description`, 500);
    for (const setting of ["visible", "user_modifiable", "hide_when_no_value"]) {
      if (parameter[setting] !== undefined && typeof parameter[setting] !== "boolean") throw new Error(`parameter ${String(parameter.key)}.${setting} must be boolean.`);
      if (parameter[setting] !== undefined && parameter.shared_guid === undefined) throw new Error(`parameter ${String(parameter.key)}.${setting} requires shared_guid because Revit exposes this definition metadata only for shared parameters.`);
    }
    if (parameter.shared_guid !== undefined) {
      const guid = string(parameter.shared_guid, `parameter ${String(parameter.key)}.shared_guid`, 36).toLowerCase();
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(guid)) throw new Error(`parameter ${String(parameter.key)}.shared_guid must be a valid RFC 4122 GUID.`);
      if (sharedGuids.has(guid)) throw new Error(`blueprint.parameters contains duplicate shared_guid ${guid}.`);
      sharedGuids.add(guid);
      parameter.shared_guid = guid;
    }
    if (parameter.formula !== undefined) {
      const formula = string(parameter.formula, `parameter ${String(parameter.key)}.formula`, 240);
      if (!allowedFormula.test(formula) || /[{};\r\n]/.test(formula)) throw new Error(`parameter ${String(parameter.key)}.formula contains unsupported characters.`);
      if (parameter.scope !== "type") throw new Error(`parameter ${String(parameter.key)}.formula requires scope=type because Revit formulas are applied to Family Type parameters.`);
      if (!formulaCapableDataTypes.has(String(parameter.data_type))) throw new Error(`parameter ${String(parameter.key)}.formula is not supported for data_type=${String(parameter.data_type)}.`);
    }
    if (parameter.formula !== undefined && parameter.lookup !== undefined) throw new Error(`parameter ${String(parameter.key)} cannot declare both formula and lookup.`);
    if (parameter.formula === undefined && parameter.formula_dependencies !== undefined) throw new Error(`parameter ${String(parameter.key)}.formula_dependencies requires formula.`);
    if (parameter.source_field !== undefined) {
      const sourceField = key(parameter.source_field, `parameter ${String(parameter.key)}.source_field`);
      if (!requiredSourceFieldSet.has(sourceField)) throw new Error(`parameter ${String(parameter.key)}.source_field=${sourceField} must appear in blueprint.required_source_fields.`);
      if (parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`parameter ${String(parameter.key)}.source_field cannot be combined with formula/lookup because its built value would not be a direct confirmed source value.`);
      parameter.source_field = sourceField;
      const keys = sourceParametersByField.get(sourceField) ?? [];
      keys.push(String(parameter.key)); sourceParametersByField.set(sourceField, keys);
    }
  }
  const formulaDependencyMap = new Map<string, string[]>(); const formulaDependencyEntries: Json[] = [];
  for (const parameter of parameters.filter((item) => item.formula !== undefined)) {
    const parameterKey = String(parameter.key); const formula = String(parameter.formula);
    const inferredDependencies = formulaParameterReferences(formula, parameters);
    const declaredDependencies = parameter.formula_dependencies === undefined ? undefined : array(parameter.formula_dependencies, `parameter ${parameterKey}.formula_dependencies`).map((item, index) => key(item, `parameter ${parameterKey}.formula_dependencies[${index}]`));
    const dependencies = declaredDependencies ?? inferredDependencies;
    if (new Set(dependencies).size !== dependencies.length) throw new Error(`parameter ${parameterKey}.formula_dependencies must be unique.`);
    for (const dependencyKey of dependencies) {
      const dependency = parametersByKey.get(dependencyKey);
      if (!dependency) throw new Error(`parameter ${parameterKey}.formula_dependencies references unknown Family Parameter ${dependencyKey}.`);
      if (dependencyKey === parameterKey) throw new Error(`parameter ${parameterKey}.formula_dependencies cannot reference itself.`);
      if (dependency.scope !== "type") throw new Error(`parameter ${parameterKey}.formula_dependencies ${dependencyKey} must reference a Type Family Parameter.`);
    }
    if (declaredDependencies !== undefined && (dependencies.length !== inferredDependencies.length || dependencies.some((item) => !inferredDependencies.includes(item))))
      throw new Error(`parameter ${parameterKey}.formula_dependencies must exactly match the Family Parameter names referenced by its formula.`);
    parameter.formula_dependencies = dependencies;
    formulaDependencyMap.set(parameterKey, dependencies);
    formulaDependencyEntries.push({ parameter_key: parameterKey, formula, dependency_keys: dependencies, dependency_origin: declaredDependencies === undefined ? "inferred_parameter_name_match" : "declared_and_verified" });
  }
  requireAcyclicFormulaDependencies(formulaDependencyMap);
  blueprint.formula_dependency_graph = { policy: "revit_type_formula_dependency_graph_v1", entries: formulaDependencyEntries, application_order: topologicalFormulaApplicationOrder(formulaDependencyMap), boundary: "Dependency entries are matched to declared Family Parameter names, checked for cycles, then ordered before Revit SetFormula. Revit remains the authority for dimensional-unit syntax and evaluated values on the selected template." };
  for (const sourceField of requiredSourceFields) if (!(sourceParametersByField.get(sourceField)?.length))
    throw new Error(`required_source_field ${sourceField} must map to at least one direct Family Parameter through parameter.source_field.`);
  const parameterOrder = array(blueprint.parameter_order ?? parameters.map((parameter) => parameter.key), "blueprint.parameter_order").map((item, index) => key(item, `parameter_order[${index}]`));
  if (parameterOrder.length !== parameters.length || new Set(parameterOrder).size !== parameterOrder.length || parameterOrder.some((parameterKey) => !parametersByKey.has(parameterKey)))
    throw new Error("blueprint.parameter_order must contain every declared parameter key exactly once.");
  blueprint.parameter_order = parameterOrder;
  const validateVisibilityParameter = (raw: unknown, label: string): string | undefined => {
    if (raw === undefined) return undefined;
    const parameterKey = key(raw, `${label}.visibility_parameter`);
    const parameter = parametersByKey.get(parameterKey);
    if (!parameter || parameter.data_type !== "yesno") throw new Error(`${label}.visibility_parameter must reference a yesno Family Parameter.`);
    return parameterKey;
  };

  const materials = array(blueprint.materials ?? [], "blueprint.materials").map((item, index) => object(item, `materials[${index}]`));
  uniqueKeys(materials, "blueprint.materials");
  const materialKeys = new Set(materials.map((material) => String(material.key)));
  for (const material of materials) {
    material.name = string(material.name, `material ${String(material.key)}.name`, 120);
    const color = object(material.color_rgb, `material ${String(material.key)}.color_rgb`);
    for (const channel of ["r", "g", "b"]) {
      const value = finite(color[channel], `material ${String(material.key)}.color_rgb.${channel}`);
      if (!Number.isInteger(value) || value < 0 || value > 255) throw new Error(`material ${String(material.key)}.color_rgb.${channel} must be an integer from 0 to 255.`);
    }
    const transparency = finite(material.transparency ?? 0, `material ${String(material.key)}.transparency`);
    if (!Number.isInteger(transparency) || transparency < 0 || transparency > 100) throw new Error(`material ${String(material.key)}.transparency must be an integer from 0 to 100.`);
    if (material.use_render_appearance_for_shading !== undefined && typeof material.use_render_appearance_for_shading !== "boolean") throw new Error(`material ${String(material.key)}.use_render_appearance_for_shading must be boolean.`);
    const validatePattern = (raw: unknown, property: string, cut: boolean): void => {
      if (raw === undefined) return;
      const pattern = object(raw, `material ${String(material.key)}.${property}`);
      string(pattern.name, `material ${String(material.key)}.${property}.name`, 120);
      const target = oneOf(pattern.target, ["drafting", "model"] as const, `material ${String(material.key)}.${property}.target`);
      if (cut && target !== "drafting") throw new Error(`material ${String(material.key)}.${property}.target must be drafting because Revit cut patterns do not accept model patterns.`);
      const patternColor = object(pattern.color_rgb, `material ${String(material.key)}.${property}.color_rgb`);
      for (const channel of ["r", "g", "b"]) {
        const value = finite(patternColor[channel], `material ${String(material.key)}.${property}.color_rgb.${channel}`);
        if (!Number.isInteger(value) || value < 0 || value > 255) throw new Error(`material ${String(material.key)}.${property}.color_rgb.${channel} must be an integer from 0 to 255.`);
      }
    };
    validatePattern(material.surface_foreground_pattern, "surface_foreground_pattern", false);
    validatePattern(material.cut_foreground_pattern, "cut_foreground_pattern", true);
    if (material.appearance !== undefined) {
      const appearance = object(material.appearance, `material ${String(material.key)}.appearance`);
      const appearanceColor = object(appearance.color_rgb, `material ${String(material.key)}.appearance.color_rgb`);
      for (const channel of ["r", "g", "b"]) {
        const value = finite(appearanceColor[channel], `material ${String(material.key)}.appearance.color_rgb.${channel}`);
        if (!Number.isInteger(value) || value < 0 || value > 255) throw new Error(`material ${String(material.key)}.appearance.color_rgb.${channel} must be an integer from 0 to 255.`);
      }
      for (const property of ["transparency", "glossiness"]) {
        const value = finite(appearance[property] ?? 0, `material ${String(material.key)}.appearance.${property}`);
        if (value < 0 || value > 1) throw new Error(`material ${String(material.key)}.appearance.${property} must be between 0 and 1.`);
      }
      if (appearance.is_metal !== undefined && typeof appearance.is_metal !== "boolean") throw new Error(`material ${String(material.key)}.appearance.is_metal must be boolean.`);
      const validateBitmap = (raw: unknown, property: "texture" | "bump"): void => {
        if (raw === undefined) return;
        const bitmap = object(raw, `material ${String(material.key)}.appearance.${property}`);
        const fileName = string(bitmap.file_name, `material ${String(material.key)}.appearance.${property}.file_name`, 120);
        if (/[\\/]/.test(fileName) || !/\.(?:png|jpe?g|bmp|tiff?)$/i.test(fileName))
          throw new Error(`material ${String(material.key)}.appearance.${property}.file_name must be an approved image basename (.png, .jpg, .jpeg, .bmp, .tif or .tiff).`);
        const checksum = string(bitmap.sha256, `material ${String(material.key)}.appearance.${property}.sha256`, 64).toLowerCase();
        if (!/^[a-f0-9]{64}$/.test(checksum)) throw new Error(`material ${String(material.key)}.appearance.${property}.sha256 must be a SHA-256 hex digest.`);
        bitmap.file_name = fileName; bitmap.sha256 = checksum;
        if (property === "bump") {
          const amount = finite(bitmap.amount, `material ${String(material.key)}.appearance.bump.amount`);
          if (amount < -1000 || amount > 1000) throw new Error(`material ${String(material.key)}.appearance.bump.amount must be between -1000 and 1000.`);
        }
      };
      validateBitmap(appearance.texture, "texture");
      validateBitmap(appearance.bump, "bump");
      if ((appearance.texture !== undefined || appearance.bump !== undefined) && material.use_render_appearance_for_shading !== true)
        throw new Error(`material ${String(material.key)} with appearance.texture or appearance.bump must set use_render_appearance_for_shading=true.`);
    }
    if (material.use_render_appearance_for_shading === true && material.appearance === undefined)
      throw new Error(`material ${String(material.key)} with use_render_appearance_for_shading=true must declare appearance so its rendered shading colour is deterministic.`);
    if (material.physical_asset !== undefined) {
      const physical = object(material.physical_asset, `material ${String(material.key)}.physical_asset`);
      if (physical.name !== undefined) string(physical.name, `material ${String(material.key)}.physical_asset.name`, 120);
      oneOf(physical.asset_class, ["generic", "concrete", "metal", "wood"] as const, `material ${String(material.key)}.physical_asset.asset_class`);
      oneOf(physical.behavior, ["isotropic"] as const, `material ${String(material.key)}.physical_asset.behavior`);
      const density = finite(physical.density_kg_per_m3, `material ${String(material.key)}.physical_asset.density_kg_per_m3`);
      if (density <= 0 || density > 100000) throw new Error(`material ${String(material.key)}.physical_asset.density_kg_per_m3 must be greater than 0 and at most 100000.`);
      for (const property of ["young_modulus_mpa", "shear_modulus_mpa"]) {
        if (physical[property] === undefined) continue;
        const value = finite(physical[property], `material ${String(material.key)}.physical_asset.${property}`);
        if (value <= 0 || value > 10000000) throw new Error(`material ${String(material.key)}.physical_asset.${property} must be greater than 0 and at most 10000000.`);
      }
      if (physical.poisson_ratio !== undefined) {
        const value = finite(physical.poisson_ratio, `material ${String(material.key)}.physical_asset.poisson_ratio`);
        if (value < 0 || value >= 0.5) throw new Error(`material ${String(material.key)}.physical_asset.poisson_ratio must be at least 0 and less than 0.5.`);
      }
    }
    if (material.thermal_asset !== undefined) {
      const thermal = object(material.thermal_asset, `material ${String(material.key)}.thermal_asset`);
      if (thermal.name !== undefined) string(thermal.name, `material ${String(material.key)}.thermal_asset.name`, 120);
      oneOf(thermal.material_type, ["solid"] as const, `material ${String(material.key)}.thermal_asset.material_type`);
      for (const [property, upper] of [["density_kg_per_m3", 100000], ["thermal_conductivity_w_per_mk", 10000], ["specific_heat_j_per_kgk", 100000]] as const) {
        const value = finite(thermal[property], `material ${String(material.key)}.thermal_asset.${property}`);
        if (value <= 0 || value > upper) throw new Error(`material ${String(material.key)}.thermal_asset.${property} must be greater than 0 and at most ${upper}.`);
      }
      for (const property of ["emissivity", "porosity", "reflectivity"]) {
        if (thermal[property] === undefined && property !== "emissivity") continue;
        const value = finite(thermal[property], `material ${String(material.key)}.thermal_asset.${property}`);
        if (value < 0 || value > 1) throw new Error(`material ${String(material.key)}.thermal_asset.${property} must be between 0 and 1.`);
      }
      if (thermal.transmits_light !== undefined && typeof thermal.transmits_light !== "boolean") throw new Error(`material ${String(material.key)}.thermal_asset.transmits_light must be boolean.`);
    }
  }

  const electricalLoadClassifications = array(blueprint.electrical_load_classifications ?? [], "blueprint.electrical_load_classifications").map((item, index) => object(item, `electrical_load_classifications[${index}]`));
  uniqueKeys(electricalLoadClassifications, "blueprint.electrical_load_classifications");
  const electricalLoadClassificationKeys = new Set(electricalLoadClassifications.map((item) => String(item.key)));
  const electricalLoadClassificationNames = new Set<string>();
  for (const classification of electricalLoadClassifications) {
    const classificationKey = String(classification.key);
    const name = string(classification.name, `electrical load classification ${classificationKey}.name`, 120);
    if (electricalLoadClassificationNames.has(name.toLocaleLowerCase())) throw new Error(`blueprint.electrical_load_classifications contains duplicate name: ${name}.`);
    electricalLoadClassificationNames.add(name.toLocaleLowerCase());
    string(classification.abbreviation, `electrical load classification ${classificationKey}.abbreviation`, 32);
  }

  const lookupTables = array(blueprint.lookup_tables ?? [], "blueprint.lookup_tables").map((item, index) => object(item, `lookup_tables[${index}]`));
  uniqueKeys(lookupTables, "blueprint.lookup_tables");
  const lookupTablesByKey = new Map<string, { table: Json; columns: Map<string, Json>; lookupColumns: string[] }>();
  for (const table of lookupTables) {
    const tableKey = String(table.key);
    const tableName = string(table.name, `lookup table ${tableKey}.name`, 80);
    if (/[",#\r\n]/.test(tableName)) throw new Error(`lookup table ${tableKey}.name cannot contain comma, quote, # or a line break.`);
    if (table.catalogue_provenance !== undefined) {
      const provenance = object(table.catalogue_provenance, `lookup table ${tableKey}.catalogue_provenance`);
      const allowed = new Set(["catalog_record_id", "catalog_sha256", "revision", "manufacturer", "column_map", "lookup_key_source_columns"]);
      for (const property of Object.keys(provenance)) if (!allowed.has(property)) throw new Error(`lookup table ${tableKey}.catalogue_provenance contains unsupported field ${property}.`);
      string(provenance.catalog_record_id, `lookup table ${tableKey}.catalogue_provenance.catalog_record_id`, 96);
      const catalogSha = string(provenance.catalog_sha256, `lookup table ${tableKey}.catalogue_provenance.catalog_sha256`, 64).toLowerCase();
      if (!/^[a-f0-9]{64}$/.test(catalogSha)) throw new Error(`lookup table ${tableKey}.catalogue_provenance.catalog_sha256 must be SHA-256.`);
      string(provenance.revision, `lookup table ${tableKey}.catalogue_provenance.revision`, 80);
      string(provenance.manufacturer, `lookup table ${tableKey}.catalogue_provenance.manufacturer`, 120);
      const columnMap = object(provenance.column_map, `lookup table ${tableKey}.catalogue_provenance.column_map`);
      if (Object.keys(columnMap).length === 0) throw new Error(`lookup table ${tableKey}.catalogue_provenance.column_map cannot be empty.`);
      for (const [sourceColumn, targetKey] of Object.entries(columnMap)) { string(sourceColumn, `lookup table ${tableKey}.catalogue_provenance.column_map source`, 120); key(targetKey, `lookup table ${tableKey}.catalogue_provenance.column_map.${sourceColumn}`); }
      const sourceKeys = array(provenance.lookup_key_source_columns, `lookup table ${tableKey}.catalogue_provenance.lookup_key_source_columns`).map((item, index) => string(item, `lookup table ${tableKey}.catalogue_provenance.lookup_key_source_columns[${index}]`, 120));
      if (sourceKeys.length === 0) throw new Error(`lookup table ${tableKey}.catalogue_provenance.lookup_key_source_columns cannot be empty.`);
    }
    const columns = array(table.columns, `lookup table ${tableKey}.columns`).map((item, index) => object(item, `lookup table ${tableKey}.columns[${index}]`));
    uniqueKeys(columns, `lookup table ${tableKey}.columns`);
    if (columns.length < 2) throw new Error(`lookup table ${tableKey} requires at least one lookup column and one result column.`);
    const columnsByKey = new Map(columns.map((column) => [String(column.key), column]));
    for (const column of columns) {
      const name = string(column.name, `lookup table ${tableKey} column ${String(column.key)}.name`, 80);
      if (/[",#\r\n]/.test(name)) throw new Error(`lookup table ${tableKey} column ${String(column.key)}.name contains an unsupported CSV/formula character.`);
      oneOf(column.data_type, ["length", "number", "integer", "angle"] as const, `lookup table ${tableKey} column ${String(column.key)}.data_type`);
    }
    const lookupColumns = array(table.lookup_columns, `lookup table ${tableKey}.lookup_columns`).map((item, index) => key(item, `lookup table ${tableKey}.lookup_columns[${index}]`));
    if (lookupColumns.length === 0 || lookupColumns.length >= columns.length) throw new Error(`lookup table ${tableKey}.lookup_columns must identify a non-empty prefix smaller than the full column list.`);
    if (lookupColumns.some((columnKey, index) => columnKey !== String(columns[index].key))) throw new Error(`lookup table ${tableKey}.lookup_columns must match the leading columns in order.`);
    const rows = array(table.rows, `lookup table ${tableKey}.rows`).map((item, index) => object(item, `lookup table ${tableKey}.rows[${index}]`));
    if (rows.length === 0) throw new Error(`lookup table ${tableKey} requires at least one row.`);
    const lookupTuples = new Set<string>();
    for (const [rowIndex, row] of rows.entries()) {
      const values = object(row.values, `lookup table ${tableKey}.rows[${rowIndex}].values`);
      for (const column of columns) finite(values[String(column.key)], `lookup table ${tableKey}.rows[${rowIndex}].values.${String(column.key)}`);
      for (const valueKey of Object.keys(values)) if (!columnsByKey.has(valueKey)) throw new Error(`lookup table ${tableKey}.rows[${rowIndex}] contains unknown column ${valueKey}.`);
      const tuple = JSON.stringify(lookupColumns.map((columnKey) => values[columnKey]));
      if (lookupTuples.has(tuple)) throw new Error(`lookup table ${tableKey} contains duplicate lookup values at row ${rowIndex}.`);
      lookupTuples.add(tuple);
    }
    lookupTablesByKey.set(tableKey, { table, columns: columnsByKey, lookupColumns });
  }

  for (const parameter of parameters) {
    if (parameter.lookup === undefined) continue;
    if (parameter.scope !== "instance") throw new Error(`parameter ${String(parameter.key)}.lookup must be instance-scoped for Revit size_lookup.`);
    const lookup = object(parameter.lookup, `parameter ${String(parameter.key)}.lookup`);
    const tableKey = key(lookup.table_key, `parameter ${String(parameter.key)}.lookup.table_key`);
    const table = lookupTablesByKey.get(tableKey);
    if (!table) throw new Error(`parameter ${String(parameter.key)}.lookup references unknown table ${tableKey}.`);
    const resultColumnKey = key(lookup.result_column_key, `parameter ${String(parameter.key)}.lookup.result_column_key`);
    const resultColumn = table.columns.get(resultColumnKey);
    if (!resultColumn || table.lookupColumns.includes(resultColumnKey)) throw new Error(`parameter ${String(parameter.key)}.lookup result column must be a non-lookup column in table ${tableKey}.`);
    if (resultColumn.data_type !== parameter.data_type) throw new Error(`parameter ${String(parameter.key)}.lookup result type must match the parameter data_type.`);
    const defaultParameterKey = key(lookup.default_parameter_key, `parameter ${String(parameter.key)}.lookup.default_parameter_key`);
    if (defaultParameterKey === parameter.key || parametersByKey.get(defaultParameterKey)?.data_type !== parameter.data_type) throw new Error(`parameter ${String(parameter.key)}.lookup default_parameter_key must reference a different parameter with the same data_type.`);
    const lookupParameterKeys = array(lookup.lookup_parameter_keys, `parameter ${String(parameter.key)}.lookup.lookup_parameter_keys`).map((item, index) => key(item, `parameter ${String(parameter.key)}.lookup.lookup_parameter_keys[${index}]`));
    if (lookupParameterKeys.length !== table.lookupColumns.length) throw new Error(`parameter ${String(parameter.key)}.lookup must provide one lookup parameter for each lookup column.`);
    lookupParameterKeys.forEach((parameterKey, index) => {
      const expectedType = table.columns.get(table.lookupColumns[index])?.data_type;
      if (parametersByKey.get(parameterKey)?.data_type !== expectedType) throw new Error(`parameter ${String(parameter.key)}.lookup parameter ${parameterKey} does not match lookup column type.`);
    });
  }

  const validateParameterValue = (parameter: Json, value: unknown, label: string): void => {
    const dataType = String(parameter.data_type);
    if (numericFamilyParameterDataTypes.has(dataType)) {
      const numeric = finite(value, label);
      if (["integer", "number_of_poles"].includes(dataType) && !Number.isInteger(numeric)) throw new Error(`${label} must be an integer.`);
      if (dataType === "number_of_poles" && (numeric < 1 || numeric > 3)) throw new Error(`${label} must be 1, 2 or 3 poles.`);
    }
    if (dataType === "yesno" && typeof value !== "boolean") throw new Error(`${label} must be boolean.`);
    if (["text", "url"].includes(dataType)) string(value, label, 500);
    if (dataType === "material") {
      const materialKey = key(value, label);
      if (!materialKeys.has(materialKey)) throw new Error(`${label} references unknown material ${materialKey}.`);
    }
    if (dataType === "load_classification") {
      const classificationKey = key(value, label);
      if (!electricalLoadClassificationKeys.has(classificationKey)) throw new Error(`${label} references unknown electrical load classification ${classificationKey}.`);
    }
    if (dataType === "family_type") key(value, label);
  };
  for (const parameter of parameters) if (parameter.default !== undefined) validateParameterValue(parameter, parameter.default, `parameter ${String(parameter.key)}.default`);

  const types = array(blueprint.types ?? [], "blueprint.types").map((item, index) => object(item, `types[${index}]`));
  const typeNames = new Set<string>();
  for (const type of types) {
    const name = string(type.name, "blueprint.types.name", 120);
    type.name = name;
    if (typeNames.has(name.toLocaleLowerCase())) throw new Error(`blueprint.types contains duplicate name: ${name}.`);
    typeNames.add(name.toLocaleLowerCase());
    const values = object(type.values ?? {}, `type ${name}.values`);
    for (const [parameterKey, value] of Object.entries(values)) {
      const parameter = parametersByKey.get(parameterKey);
      if (!parameter) throw new Error(`type ${name}.values references unknown parameter ${parameterKey}.`);
      if (parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`type ${name}.values cannot set formula/lookup-driven parameter ${parameterKey}.`);
      validateParameterValue(parameter, value, `type ${name}.values.${parameterKey}`);
    }
  }

  // These are Revit's native Type Identity Data fields, not look-alike custom
  // parameters. Keeping the declaration separate means a Family can be
  // schedule-ready without silently creating another "Manufacturer" field
  // with a different identity. Values remain explicit per Family Type.
  if (blueprint.identity_data !== undefined) {
    const identity = object(blueprint.identity_data, "blueprint.identity_data");
    for (const property of Object.keys(identity)) if (property !== "type_values") throw new Error(`blueprint.identity_data contains unsupported field ${property}.`);
    if (types.length === 0) throw new Error("blueprint.identity_data requires at least one explicitly declared Family Type.");
    const typeValues = array(identity.type_values, "blueprint.identity_data.type_values").map((item, index) => object(item, `identity_data.type_values[${index}]`));
    if (typeValues.length !== types.length) throw new Error("blueprint.identity_data.type_values must declare every Family Type exactly once.");
    const declared = new Set<string>();
    const allowedIdentityFields = ["manufacturer", "model", "description", "url", "type_comments", "classification_number", "classification_title"];
    for (const value of typeValues) {
      const typeName = string(value.type_name, "identity_data.type_values.type_name", 120);
      const normalized = typeName.toLocaleLowerCase();
      if (!typeNames.has(normalized)) throw new Error(`identity_data.type_values references unknown Family Type ${typeName}.`);
      if (declared.has(normalized)) throw new Error(`identity_data.type_values contains duplicate Family Type ${typeName}.`);
      declared.add(normalized); value.type_name = typeName;
      const fieldNames = Object.keys(value).filter((property) => property !== "type_name");
      if (fieldNames.length === 0) throw new Error(`identity_data.type_values ${typeName} must declare at least one native Identity Data field.`);
      for (const property of fieldNames) {
        if (!allowedIdentityFields.includes(property)) throw new Error(`identity_data.type_values ${typeName} contains unsupported field ${property}.`);
        value[property] = string(value[property], `identity_data.type_values ${typeName}.${property}`, property === "url" ? 500 : 240);
      }
    }
    const missingIdentityTypes = types.map((type) => String(type.name)).filter((name) => !declared.has(name.toLocaleLowerCase()));
    if (missingIdentityTypes.length) throw new Error(`identity_data.type_values is missing Family Type(s): ${missingIdentityTypes.join(", ")}.`);
    blueprint.identity_data = { policy: "revit_builtin_type_identity_data_v1", type_values: typeValues, boundary: "Identity Data uses native Revit Type fields, explicit per Type. It does not prove manufacturer approval, catalog revision applicability, Schedule/Tag field exposure, or project procurement/asset requirements." };
  }

  const lightSource = blueprint.light_source === undefined ? undefined : object(blueprint.light_source, "blueprint.light_source");
  if (familyCategory === "lighting_fixture" && !lightSource) throw new Error("blueprint.light_source is required for lighting_fixture so the Autodesk template light source is configured explicitly.");
  if (familyCategory !== "lighting_fixture" && lightSource) throw new Error("blueprint.light_source is only valid for category lighting_fixture.");
  if (lightSource) {
    const shapeStyle = oneOf(lightSource.shape_style, familyLightShapeStyles, "blueprint.light_source.shape_style");
    const distributionStyle = oneOf(lightSource.distribution_style, familyLightDistributionStyles, "blueprint.light_source.distribution_style");
    if (types.length === 0) throw new Error("blueprint.light_source requires at least one explicitly declared Family type.");
    if (distributionStyle === "photometric_web" && shapeStyle !== "circle") throw new Error("blueprint.light_source photometric_web requires shape_style=circle because Revit exposes a circular emitting shape for this distribution.");
    const settings = array(lightSource.type_settings, "blueprint.light_source.type_settings").map((item, index) => object(item, `blueprint.light_source.type_settings[${index}]`));
    if (settings.length !== types.length) throw new Error("blueprint.light_source.type_settings must configure every declared Family type exactly once.");
    const configuredTypes = new Set<string>();
    const presetValues = new Set<string>(familyLightColorPresets);
    const finiteRange = (value: unknown, label: string, minimum: number, maximum: number, exclusiveMinimum = false): number => {
      const result = finite(value, label);
      if ((exclusiveMinimum ? result <= minimum : result < minimum) || result > maximum) throw new Error(`${label} must be ${exclusiveMinimum ? "greater than" : "at least"} ${minimum} and at most ${maximum}.`);
      return result;
    };
    for (const setting of settings) {
      const typeName = string(setting.type_name, "blueprint.light_source.type_settings.type_name", 120);
      const normalizedTypeName = typeName.toLocaleLowerCase();
      if (configuredTypes.has(normalizedTypeName)) throw new Error(`blueprint.light_source.type_settings contains duplicate type_name ${typeName}.`);
      if (!typeNames.has(normalizedTypeName)) throw new Error(`blueprint.light_source.type_settings references unknown Family type ${typeName}.`);
      configuredTypes.add(normalizedTypeName);
      if (shapeStyle === "line") positive(setting.emit_length_mm, `light source type ${typeName}.emit_length_mm`);
      if (shapeStyle === "rectangle") {
        positive(setting.emit_length_mm, `light source type ${typeName}.emit_length_mm`);
        positive(setting.emit_width_mm, `light source type ${typeName}.emit_width_mm`);
      }
      if (shapeStyle === "circle") positive(setting.emit_diameter_mm, `light source type ${typeName}.emit_diameter_mm`);
      for (const property of ["emit_length_mm", "emit_width_mm", "emit_diameter_mm"]) {
        const allowed = property === "emit_length_mm" ? ["line", "rectangle"].includes(shapeStyle) : property === "emit_width_mm" ? shapeStyle === "rectangle" : shapeStyle === "circle";
        if (!allowed && setting[property] !== undefined) throw new Error(`light source type ${typeName}.${property} is not valid for shape_style=${shapeStyle}.`);
      }
      if (distributionStyle === "spot") {
        const spot = object(setting.spot, `light source type ${typeName}.spot`);
        const beam = finiteRange(spot.beam_angle_degrees, `light source type ${typeName}.spot.beam_angle_degrees`, 0, 160, true);
        const field = finiteRange(spot.field_angle_degrees, `light source type ${typeName}.spot.field_angle_degrees`, 0, 160, true);
        finiteRange(spot.tilt_angle_degrees ?? 0, `light source type ${typeName}.spot.tilt_angle_degrees`, -180, 180);
        if (field < beam) throw new Error(`light source type ${typeName}.spot.field_angle_degrees must be greater than or equal to beam_angle_degrees.`);
      } else if (setting.spot !== undefined) throw new Error(`light source type ${typeName}.spot requires distribution_style=spot.`);
      if (distributionStyle === "photometric_web") {
        const web = object(setting.photometric_web, `light source type ${typeName}.photometric_web`);
        const fileName = string(web.file_name, `light source type ${typeName}.photometric_web.file_name`, 120);
        if (fileName !== fileName.split(/[\\/]/).pop() || !fileName.toLowerCase().endsWith(".ies")) throw new Error(`light source type ${typeName}.photometric_web.file_name must be a basename ending in .ies.`);
        const checksum = string(web.sha256, `light source type ${typeName}.photometric_web.sha256`, 64).toLowerCase();
        if (!/^[a-f0-9]{64}$/.test(checksum)) throw new Error(`light source type ${typeName}.photometric_web.sha256 must be a 64-character hexadecimal SHA-256.`);
        web.sha256 = checksum;
        finiteRange(web.tilt_angle_degrees ?? 0, `light source type ${typeName}.photometric_web.tilt_angle_degrees`, -180, 180);
      } else if (setting.photometric_web !== undefined) throw new Error(`light source type ${typeName}.photometric_web requires distribution_style=photometric_web.`);
      const intensity = object(setting.initial_intensity, `light source type ${typeName}.initial_intensity`);
      const intensityMethod = oneOf(intensity.method, ["luminous_flux", "luminous_intensity", "illuminance", "wattage"] as const, `light source type ${typeName}.initial_intensity.method`);
      if (intensityMethod === "luminous_flux") finiteRange(intensity.luminous_flux_lm, `light source type ${typeName}.initial_intensity.luminous_flux_lm`, 0, 1e30, true);
      if (intensityMethod === "luminous_intensity") finiteRange(intensity.luminous_intensity_cd, `light source type ${typeName}.initial_intensity.luminous_intensity_cd`, 0, 1e30, true);
      if (intensityMethod === "illuminance") {
        finiteRange(intensity.illuminance_lux, `light source type ${typeName}.initial_intensity.illuminance_lux`, 0, 1e30, true);
        finiteRange(intensity.distance_mm, `light source type ${typeName}.initial_intensity.distance_mm`, 0, 1e30, true);
      }
      if (intensityMethod === "wattage") {
        finiteRange(intensity.wattage_w, `light source type ${typeName}.initial_intensity.wattage_w`, 0, 1e30, true);
        finiteRange(intensity.efficacy_lm_per_w, `light source type ${typeName}.initial_intensity.efficacy_lm_per_w`, 0, 1e10, true);
      }
      const intensityFields: Record<string, readonly string[]> = { luminous_flux: ["luminous_flux_lm"], luminous_intensity: ["luminous_intensity_cd"], illuminance: ["illuminance_lux", "distance_mm"], wattage: ["wattage_w", "efficacy_lm_per_w"] };
      for (const property of ["luminous_flux_lm", "luminous_intensity_cd", "illuminance_lux", "distance_mm", "wattage_w", "efficacy_lm_per_w"])
        if (intensity[property] !== undefined && !intensityFields[intensityMethod].includes(property)) throw new Error(`light source type ${typeName}.initial_intensity.${property} is not valid for method=${intensityMethod}.`);
      const initialColor = object(setting.initial_color, `light source type ${typeName}.initial_color`);
      const colorMode = oneOf(initialColor.mode, ["temperature", "preset"] as const, `light source type ${typeName}.initial_color.mode`);
      if (colorMode === "temperature") finiteRange(initialColor.temperature_kelvin, `light source type ${typeName}.initial_color.temperature_kelvin`, 1800, 20000);
      else {
        const preset = string(initialColor.preset, `light source type ${typeName}.initial_color.preset`);
        if (!presetValues.has(preset)) throw new Error(`light source type ${typeName}.initial_color.preset must be one of: ${familyLightColorPresets.join(", ")}.`);
      }
      if (colorMode === "temperature" && initialColor.preset !== undefined || colorMode === "preset" && initialColor.temperature_kelvin !== undefined) throw new Error(`light source type ${typeName}.initial_color fields do not match mode=${colorMode}.`);
      const loss = object(setting.loss_factor, `light source type ${typeName}.loss_factor`);
      const lossMode = oneOf(loss.mode, ["basic", "advanced"] as const, `light source type ${typeName}.loss_factor.mode`);
      if (lossMode === "basic") finiteRange(loss.value, `light source type ${typeName}.loss_factor.value`, 0, 4);
      else for (const property of ["ballast", "lamp_lumen_depreciation", "lamp_tilt", "luminaire_dirt_depreciation", "surface_depreciation", "temperature", "voltage"])
        finiteRange(loss[property], `light source type ${typeName}.loss_factor.${property}`, 0, 1);
      if (setting.color_filter_rgb !== undefined) {
        const color = object(setting.color_filter_rgb, `light source type ${typeName}.color_filter_rgb`);
        for (const channel of ["r", "g", "b"]) boundedInteger(color[channel], `light source type ${typeName}.color_filter_rgb.${channel}`, 0, 255);
      }
      if (setting.dimming_color !== undefined) oneOf(setting.dimming_color, ["none", "incandescent"] as const, `light source type ${typeName}.dimming_color`);
    }
  }

  const publication = object(blueprint.publication ?? {}, "blueprint.publication");
  if (publication.compact_rfa !== undefined && typeof publication.compact_rfa !== "boolean") throw new Error("blueprint.publication.compact_rfa must be boolean.");
  if (publication.preview_view !== undefined) oneOf(publication.preview_view, ["auto", "three_dimensional", "none"] as const, "blueprint.publication.preview_view");
  let purgeUnused: Json | undefined;
  if (publication.purge_unused !== undefined) {
    const purge = object(publication.purge_unused, "blueprint.publication.purge_unused");
    for (const property of Object.keys(purge)) if (!["scope", "max_passes", "unsupported_behavior"].includes(property)) throw new Error(`blueprint.publication.purge_unused contains unsupported field ${property}.`);
    purgeUnused = {
      scope: oneOf(purge.scope, ["template_residue"] as const, "blueprint.publication.purge_unused.scope"),
      max_passes: boundedInteger(purge.max_passes ?? 5, "blueprint.publication.purge_unused.max_passes", 1, 10),
      unsupported_behavior: oneOf(purge.unsupported_behavior ?? "fail", ["fail", "skip_with_evidence"] as const, "blueprint.publication.purge_unused.unsupported_behavior"),
    };
  }
  if (publication.type_catalog !== undefined) {
    const catalog = object(publication.type_catalog, "blueprint.publication.type_catalog");
    const catalogKeys = array(catalog.parameter_keys, "blueprint.publication.type_catalog.parameter_keys").map((item, index) => key(item, `blueprint.publication.type_catalog.parameter_keys[${index}]`));
    if (catalogKeys.length === 0 || new Set(catalogKeys).size !== catalogKeys.length) throw new Error("blueprint.publication.type_catalog.parameter_keys must contain unique parameter keys.");
    if (types.length < 2) throw new Error("blueprint.publication.type_catalog requires at least two declared Family types.");
    const requireCatalogCell = (value: unknown, label: string): string => {
      const result = string(value, label, 500);
      if (/[\r\n]/.test(result)) throw new Error(`${label} cannot contain a line break because a Type Catalog row must remain one physical line.`);
      return result;
    };
    for (const type of types) requireCatalogCell(type.name, `type ${String(type.name)}.name`);
    for (const parameterKey of catalogKeys) {
      const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.scope !== "type") throw new Error(`type catalog parameter ${parameterKey} must reference a declared Type parameter.`);
      if (!documentedTypeCatalogParameterDataTypes.has(String(parameter.data_type))) throw new Error(`type catalog parameter ${parameterKey} cannot use ${String(parameter.data_type)} until its Revit Type Catalog header/unit syntax is runtime-certified.`);
      if (parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`type catalog parameter ${parameterKey} cannot be formula/lookup-driven.`);
      const parameterName = requireCatalogCell(parameter.name, `type catalog parameter ${parameterKey}.name`);
      if (parameterName.includes("##")) throw new Error(`type catalog parameter ${parameterKey}.name cannot contain ## because it is reserved by the Revit Type Catalog header syntax.`);
      for (const type of types) {
        const values = object(type.values ?? {}, `type ${String(type.name)}.values`);
        if (values[parameterKey] === undefined && parameter.default === undefined && parameter.source_field === undefined)
          throw new Error(`type catalog parameter ${parameterKey} requires an explicit value or parameter default for every Family type.`);
        const value = values[parameterKey] ?? parameter.default;
        if (["text", "url"].includes(String(parameter.data_type)) && value !== undefined) requireCatalogCell(value, `type ${String(type.name)}.values.${parameterKey}`);
        if (String(parameter.data_type) === "material" && value !== undefined) {
          const materialKey = key(value, `type ${String(type.name)}.values.${parameterKey}`);
          const material = materials.find((item) => String(item.key) === materialKey);
          if (!material) throw new Error(`type catalog parameter ${parameterKey} references unknown material ${materialKey}.`);
          requireCatalogCell(material.name, `material ${materialKey}.name`);
        }
      }
    }
  }
  blueprint.publication = {
    compact_rfa: publication.compact_rfa ?? true,
    preview_view: publication.preview_view ?? "auto",
    ...(purgeUnused === undefined ? {} : { purge_unused: purgeUnused }),
    ...(publication.type_catalog === undefined ? {} : { type_catalog: { parameter_keys: array(object(publication.type_catalog, "blueprint.publication.type_catalog").parameter_keys, "blueprint.publication.type_catalog.parameter_keys") } }),
  };

  const ovalProfiles: { profile: Json; label: string }[] = [];
  const ringProfiles: { profile: Json; label: string }[] = [];
  const validateProfile = (rawProfile: unknown, label: string): { profile: Json; shape: string; hasParameters: boolean } => {
    const profile = object(rawProfile, label);
    const shape = oneOf(profile.shape, ["rectangle", "circle", "ring", "oval", "polygon", "custom_closed"] as const, `${label}.shape`);
    let hasParameters = false;
    const parameterReference = (property: string): string | undefined => {
      if (profile[property] === undefined) return undefined;
      hasParameters = true;
      const parameterKey = key(profile[property], `${label}.${property}`);
      const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== "length") throw new Error(`${label}.${property} must reference a length parameter.`);
      return parameterKey;
    };
    if (shape === "rectangle") {
      const widthParameter = parameterReference("width_parameter"); const heightParameter = parameterReference("height_parameter");
      if (!widthParameter) positive(profile.width_mm, `${label}.width_mm`);
      if (!heightParameter) positive(profile.height_mm, `${label}.height_mm`);
    }
    if (shape === "circle") {
      const diameterParameter = parameterReference("diameter_parameter");
      if (!diameterParameter) positive(profile.diameter_mm, `${label}.diameter_mm`);
    }
    if (shape === "ring") {
      const hasOuterParameter = profile.outer_diameter_parameter !== undefined; const hasInnerParameter = profile.inner_diameter_parameter !== undefined;
      const hasOuterLiteral = profile.outer_diameter_mm !== undefined; const hasInnerLiteral = profile.inner_diameter_mm !== undefined;
      if (hasOuterParameter !== hasInnerParameter || hasOuterLiteral !== hasInnerLiteral || hasOuterParameter === hasOuterLiteral)
        throw new Error(`${label} ring must declare exactly both outer_diameter_parameter/inner_diameter_parameter or both outer_diameter_mm/inner_diameter_mm; mixed or partial dimensions are not supported.`);
      if (["width_mm", "height_mm", "diameter_mm", "width_parameter", "height_parameter", "diameter_parameter"].some((property) => profile[property] !== undefined))
        throw new Error(`${label} ring cannot declare rectangular, oval or single-circle dimensions.`);
      if (hasOuterParameter) {
        for (const property of ["outer_diameter_parameter", "inner_diameter_parameter"] as const) {
          const parameterKey = parameterReference(property)!; const parameter = parametersByKey.get(parameterKey);
          if (!parameter || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined)
            throw new Error(`${label}.${property} must reference a direct Length Type Parameter.`);
        }
      } else {
        const outer = positive(profile.outer_diameter_mm, `${label}.outer_diameter_mm`); const inner = positive(profile.inner_diameter_mm, `${label}.inner_diameter_mm`);
        if (inner >= outer) throw new Error(`${label} ring inner diameter must be smaller than outer diameter.`);
      }
      ringProfiles.push({ profile, label });
    }
    if (shape === "oval") {
      const majorAxis = oneOf(profile.major_axis, ["width", "height"] as const, `${label}.major_axis`);
      const hasWidthParameter = profile.width_parameter !== undefined; const hasHeightParameter = profile.height_parameter !== undefined;
      const hasWidthLiteral = profile.width_mm !== undefined; const hasHeightLiteral = profile.height_mm !== undefined;
      if (hasWidthParameter !== hasHeightParameter || hasWidthLiteral !== hasHeightLiteral || hasWidthParameter === hasWidthLiteral)
        throw new Error(`${label} oval must declare exactly both width_parameter/height_parameter or both width_mm/height_mm; mixed or partial dimensions are not supported.`);
      if (["diameter_mm", "outer_diameter_mm", "inner_diameter_mm", "diameter_parameter", "outer_diameter_parameter", "inner_diameter_parameter"].some((property) => profile[property] !== undefined))
        throw new Error(`${label} oval cannot declare circular or ring dimensions.`);
      if (hasWidthParameter) {
        const widthKey = parameterReference("width_parameter")!; const heightKey = parameterReference("height_parameter")!;
        for (const [parameterKey, property] of [[widthKey, "width_parameter"], [heightKey, "height_parameter"]] as const) {
          const parameter = parametersByKey.get(parameterKey);
          if (!parameter || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined)
            throw new Error(`${label}.${property} must reference a direct Length Type Parameter.`);
        }
      } else {
        const width = positive(profile.width_mm, `${label}.width_mm`); const height = positive(profile.height_mm, `${label}.height_mm`);
        if (majorAxis === "width" && width <= height || majorAxis === "height" && height <= width)
          throw new Error(`${label} oval major_axis=${majorAxis} requires its major dimension to be strictly greater than the minor dimension.`);
      }
      ovalProfiles.push({ profile, label });
    } else if (profile.major_axis !== undefined) throw new Error(`${label}.major_axis is valid only for an oval profile.`);
    return { profile, shape, hasParameters };
  };

  const validatePath = (rawPath: unknown, label: string): { path: Json; points: Point3[] } => {
    const path = object(rawPath, label);
    const kind = oneOf(path.kind, ["line", "polyline", "arc", "offset", "reference_line"] as const, `${label}.kind`);
    if (kind === "reference_line") {
      key(path.reference_line_key, `${label}.reference_line_key`);
      key(path.angular_dimension_key, `${label}.angular_dimension_key`);
      for (const property of ["plane", "points_mm", "tangent_intersection_mm", "start_tangent", "turn_direction", "radius_mm", "radius_parameter", "sweep_angle_degrees", "lead_in_parameter", "lead_out_parameter", "lateral_offset_parameter", "offset_angle_parameter", "offset_direction"])
        if (path[property] !== undefined) throw new Error(`${label}.${property} is not valid for a reference_line path.`);
      return { path, points: [] };
    }
    const plane = oneOf(path.plane, ["arc", "offset"].includes(kind) ? ["xy", "xz"] as const : ["xy", "xz", "yz"] as const, `${label}.plane`);
    if (kind === "arc") {
      if (path.points_mm !== undefined) throw new Error(`${label}.points_mm is not valid for an arc path.`);
      const intersection = point3(path.tangent_intersection_mm, `${label}.tangent_intersection_mm`);
      const tangent = point3(path.start_tangent, `${label}.start_tangent`);
      if (Math.abs(tangent.x_mm - 1) > 1e-6 || Math.abs(tangent.y_mm) > 1e-6 || Math.abs(tangent.z_mm) > 1e-6) throw new Error(`${label}.start_tangent must be the bounded +X unit vector {1,0,0}.`);
      oneOf(path.turn_direction, ["counterclockwise", "clockwise"] as const, `${label}.turn_direction`);
      const hasLiteralRadius = path.radius_mm !== undefined; const hasParameterRadius = path.radius_parameter !== undefined;
      if (hasLiteralRadius === hasParameterRadius) throw new Error(`${label} must declare exactly one radius_mm or radius_parameter.`);
      if (hasLiteralRadius) positive(path.radius_mm, `${label}.radius_mm`);
      if (hasParameterRadius) {
        const parameterKey = key(path.radius_parameter, `${label}.radius_parameter`);
        const parameter = parametersByKey.get(parameterKey);
        if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`${label}.radius_parameter must reference a direct Length Type Parameter.`);
      }
      const angle = finite(path.sweep_angle_degrees, `${label}.sweep_angle_degrees`);
      if (angle <= 0 || angle >= 180) throw new Error(`${label}.sweep_angle_degrees must be greater than zero and less than 180 degrees so its connector tangents have one finite intersection.`);
      if (String(family.part_type ?? "") === "elbow" && distance3(intersection, { x_mm: 0, y_mm: 0, z_mm: 0 }) > 1e-6) throw new Error(`${label}.tangent_intersection_mm must be the Family origin for an elbow fitting.`);
      return { path, points: [] };
    }
    if (kind === "offset") {
      const parameterProperties = ["lead_in_parameter", "lead_out_parameter", "lateral_offset_parameter", "offset_angle_parameter"] as const;
      const parameterKeys = parameterProperties.map((property) => key(path[property], `${label}.${property}`));
      if (new Set(parameterKeys).size !== parameterKeys.length) throw new Error(`${label} requires four distinct direct Type Parameters so every offset path degree of freedom can be flexed independently.`);
      parameterProperties.forEach((property, index) => {
        const parameter = parametersByKey.get(parameterKeys[index]);
        const expectedType = property === "offset_angle_parameter" ? "angle" : "length";
        if (!parameter || parameter.data_type !== expectedType || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined)
          throw new Error(`${label}.${property} must reference a direct ${expectedType === "angle" ? "Angle" : "Length"} Type Parameter.`);
      });
      oneOf(path.offset_direction, ["positive", "negative"] as const, `${label}.offset_direction`);
      return { path, points: [] };
    }
    const points = array(path.points_mm, `${label}.points_mm`).map((item, index) => point3(item, `${label}.points_mm[${index}]`));
    if (points.length < 2 || (kind === "line" && points.length !== 2)) throw new Error(`${label}.points_mm must contain exactly two points for a line and at least two for a polyline.`);
    for (let index = 1; index < points.length; index += 1) {
      if (distance3(points[index - 1], points[index]) < 1e-6) throw new Error(`${label} contains a zero-length segment at index ${index - 1}.`);
    }
    const planeOffset = planeCoordinate(points[0], plane);
    if (points.some((item) => Math.abs(planeCoordinate(item, plane) - planeOffset) > 1e-6)) throw new Error(`${label}.points_mm must all lie in the declared ${plane} plane.`);
    return { path, points };
  };

  // Presentation subcategories are deliberately separate from reference-plane
  // graphics. They control Object Styles/Visibility for physical and 2D Family
  // content, while reference planes retain their own Autodesk category.
  const presentationSubcategories = array(blueprint.presentation_subcategories ?? [], "blueprint.presentation_subcategories").map((item, index) => object(item, `presentation_subcategories[${index}]`));
  uniqueKeys(presentationSubcategories, "blueprint.presentation_subcategories");
  const presentationSubcategoriesByKey = new Map(presentationSubcategories.map((item) => [String(item.key), item]));
  const presentationSubcategoryNames = new Set<string>();
  for (const subcategory of presentationSubcategories) {
    const subcategoryKey = String(subcategory.key);
    const name = string(subcategory.name, `presentation subcategory ${subcategoryKey}.name`, 120);
    if (!name.startsWith("DSCons ")) throw new Error(`presentation subcategory ${subcategoryKey}.name must start with DSCons to avoid overwriting a template or project standard.`);
    if (/[\r\n]/.test(name)) throw new Error(`presentation subcategory ${subcategoryKey}.name cannot contain a line break.`);
    if (presentationSubcategoryNames.has(name.toLowerCase())) throw new Error(`blueprint.presentation_subcategories contains duplicate name: ${name}.`);
    presentationSubcategoryNames.add(name.toLowerCase());
    const color = object(subcategory.color_rgb, `presentation subcategory ${subcategoryKey}.color_rgb`);
    for (const channel of ["r", "g", "b"]) {
      const value = finite(color[channel], `presentation subcategory ${subcategoryKey}.color_rgb.${channel}`);
      if (!Number.isInteger(value) || value < 0 || value > 255) throw new Error(`presentation subcategory ${subcategoryKey}.color_rgb.${channel} must be an integer from 0 to 255.`);
    }
    for (const property of ["projection_line_weight", "cut_line_weight"]) {
      if (subcategory[property] === undefined) continue;
      const weight = finite(subcategory[property], `presentation subcategory ${subcategoryKey}.${property}`);
      if (!Number.isInteger(weight) || weight < 1 || weight > 16) throw new Error(`presentation subcategory ${subcategoryKey}.${property} must be an integer from 1 to 16.`);
    }
    for (const property of ["projection_line_pattern_name", "cut_line_pattern_name"]) if (subcategory[property] !== undefined)
      string(subcategory[property], `presentation subcategory ${subcategoryKey}.${property}`, 120);
  }
  const validatePresentationSubcategoryKey = (raw: unknown, label: string): string | undefined => {
    if (raw === undefined) return undefined;
    const subcategoryKey = key(raw, `${label}.subcategory_key`);
    if (!presentationSubcategoriesByKey.has(subcategoryKey)) throw new Error(`${label}.subcategory_key references unknown presentation subcategory ${subcategoryKey}.`);
    return subcategoryKey;
  };

  const parts = array(blueprint.parts ?? [], "blueprint.parts").map((item, index) => object(item, `parts[${index}]`));
  uniqueKeys(parts, "blueprint.parts");
  const partKeys = new Set(parts.map((part) => String(part.key)));
  const partsByKey = new Map(parts.map((part) => [String(part.key), part]));
  for (const part of parts) {
    const primitive = oneOf(part.primitive, familyPrimitiveKinds, `part ${String(part.key)}.primitive`);
    const operation = oneOf(part.operation ?? "solid", ["solid", "void"] as const, `part ${String(part.key)}.operation`);
    if (part.axis_direction !== undefined) {
      if (primitive !== "extrusion") throw new Error(`part ${String(part.key)}.axis_direction is supported only for extrusion.`);
      if (part.axis !== undefined) throw new Error(`part ${String(part.key)} cannot declare both axis and axis_direction.`);
      part.axis_direction = unitVector3(part.axis_direction, `part ${String(part.key)}.axis_direction`);
    } else part.axis = oneOf(part.axis ?? family.primary_axis, ["x", "y", "z"] as const, `part ${String(part.key)}.axis`);
    const { profile, shape } = validateProfile(part.profile, `part ${String(part.key)}.profile`);
    let start: number | undefined; let end: number | undefined;
    if (["extrusion", "blend"].includes(primitive)) {
      start = finite(part.start_mm, `part ${String(part.key)}.start_mm`);
      end = finite(part.end_mm, `part ${String(part.key)}.end_mm`);
      if (end <= start) throw new Error(`part ${String(part.key)}.end_mm must be greater than start_mm.`);
    }
    if (["blend", "swept_blend"].includes(primitive)) {
      const endProfile = validateProfile(part.end_profile, `part ${String(part.key)}.end_profile`);
      if (endProfile.shape === "ring") throw new Error(`part ${String(part.key)}.end_profile cannot be ring because Revit blend profiles require one loop.`);
    }
    if (primitive === "blend" && shape === "ring") throw new Error(`part ${String(part.key)}.profile cannot be ring because Revit blend profiles require one loop.`);
    if (primitive === "blend") {
      for (const profile of [object(part.profile, `part ${String(part.key)}.profile`), object(part.end_profile, `part ${String(part.key)}.end_profile`)]) {
        for (const property of Object.keys(profile).filter((name) => name.endsWith("_parameter"))) {
          const parameterKey = String(profile[property]); const parameter = parametersByKey.get(parameterKey);
          if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined)
            throw new Error(`part ${String(part.key)} parameterized blend profile ${parameterKey} must reference a direct Length Type Parameter.`);
        }
      }
    }
    if (["sweep", "swept_blend"].includes(primitive)) {
      const path = validatePath(part.path, `part ${String(part.key)}.path`);
      if (primitive === "swept_blend") {
        if (path.path.kind !== "line" || path.points.length !== 2) throw new Error(`part ${String(part.key)} swept_blend currently requires one straight path segment.`);
        const sweptProfiles = [profile, object(part.end_profile, `part ${String(part.key)}.end_profile`)];
        const parameterized = sweptProfiles.some((item) => Object.keys(item).some((name) => name.endsWith("_parameter")));
        if (parameterized) {
          for (const sweptProfile of sweptProfiles) {
            if (!["rectangle", "circle", "oval"].includes(String(sweptProfile.shape))) throw new Error(`part ${String(part.key)} parameterized swept_blend supports rectangle, circle or oval profiles only.`);
            for (const property of Object.keys(sweptProfile).filter((name) => name.endsWith("_parameter"))) {
              const parameterKey = String(sweptProfile[property]); const parameter = parametersByKey.get(parameterKey);
              if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined)
                throw new Error(`part ${String(part.key)} parameterized swept_blend profile ${parameterKey} must reference a direct Length Type Parameter.`);
            }
          }
          const delta = { x: path.points[1].x_mm - path.points[0].x_mm, y: path.points[1].y_mm - path.points[0].y_mm, z: path.points[1].z_mm - path.points[0].z_mm };
          const length = Math.hypot(delta.x, delta.y, delta.z);
          if (Math.abs(delta.x / length - 1) > 1e-6 || Math.abs(delta.y) > 1e-6 || Math.abs(delta.z) > 1e-6)
            throw new Error(`part ${String(part.key)} parameterized swept_blend requires its path to follow +X so both profile constraint planes are deterministic.`);
        }
      }
      if (primitive === "sweep") {
        const profileLocation = oneOf(part.profile_location ?? "start", ["start", "midpoint", "end"] as const, `part ${String(part.key)}.profile_location`);
        const parameterProperties = Object.keys(profile).filter((name) => name.endsWith("_parameter"));
        if (path.path.kind === "reference_line") {
          if (operation !== "solid") throw new Error(`part ${String(part.key)} reference_line sweep must be solid in the bounded angular-geometry compiler.`);
          if (array(part.cut_targets ?? [], `part ${String(part.key)}.cut_targets`).length || array(part.join_with ?? [], `part ${String(part.key)}.join_with`).length)
            throw new Error(`part ${String(part.key)} reference_line sweep must remain independent of cut/join until angular flex is runtime-certified.`);
        }
        if (parameterProperties.length) {
          const parameterizedPathKind = String(path.path.kind);
          const supportedShapes = parameterizedPathKind === "arc" ? ["rectangle", "circle", "oval"] : ["rectangle", "circle", "ring", "oval"];
          if (!supportedShapes.includes(shape)) throw new Error(`part ${String(part.key)} parameterized ${parameterizedPathKind} sweep profile does not support ${shape}.`);
          if (profileLocation !== "start") throw new Error(`part ${String(part.key)} parameterized ${parameterizedPathKind} sweep profile requires profile_location=start.`);
          for (const property of parameterProperties) {
            const parameterKey = String(profile[property]); const parameter = parametersByKey.get(parameterKey);
            if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`part ${String(part.key)} parameterized ${parameterizedPathKind} sweep profile ${parameterKey} must reference a direct Length Type Parameter.`);
          }
          if (["line", "polyline"].includes(parameterizedPathKind)) {
            const legacyCableTrayOffset = familyCategory === "cable_tray_fitting" && String(family.part_type ?? "").endsWith("_offset") && parameterizedPathKind === "polyline";
            const delta = { x: path.points[1].x_mm - path.points[0].x_mm, y: path.points[1].y_mm - path.points[0].y_mm, z: path.points[1].z_mm - path.points[0].z_mm };
            const length = Math.hypot(delta.x, delta.y, delta.z);
            if (!legacyCableTrayOffset && (Math.abs(delta.x / length - 1) > 1e-6 || Math.abs(delta.y) > 1e-6 || Math.abs(delta.z) > 1e-6))
              throw new Error(`part ${String(part.key)} parameterized ${parameterizedPathKind} sweep requires its first path segment to follow +X so the profile constraint plane is deterministic.`);
          }
        }
      }
    }
    if (primitive === "revolution") {
      const plane = oneOf(part.profile_plane, ["xy", "xz", "yz"] as const, `part ${String(part.key)}.profile_plane`);
      const origin = point3(part.profile_origin_mm, `part ${String(part.key)}.profile_origin_mm`);
      const axisStart = point3(part.axis_start_mm, `part ${String(part.key)}.axis_start_mm`);
      const axisEnd = point3(part.axis_end_mm, `part ${String(part.key)}.axis_end_mm`);
      const axisLength = distance3(axisStart, axisEnd);
      if (axisLength < 1e-6) throw new Error(`part ${String(part.key)} revolution axis must have non-zero length.`);
      const planeOffset = planeCoordinate(origin, plane);
      if (Math.abs(planeCoordinate(axisStart, plane) - planeOffset) > 1e-6 || Math.abs(planeCoordinate(axisEnd, plane) - planeOffset) > 1e-6) throw new Error(`part ${String(part.key)} revolution axis must lie in profile_plane.`);
      for (const property of Object.keys(profile).filter((name) => name.endsWith("_parameter"))) {
        const parameterKey = String(profile[property]); const parameter = parametersByKey.get(parameterKey);
        if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined)
          throw new Error(`part ${String(part.key)} parameterized revolution profile ${parameterKey} must reference a direct Length Type Parameter.`);
      }
      if (!Object.keys(profile).some((property) => property.endsWith("_parameter")) && ["rectangle", "circle", "ring", "oval"].includes(shape)) {
        const axis = { x: axisEnd.x_mm - axisStart.x_mm, y: axisEnd.y_mm - axisStart.y_mm, z: axisEnd.z_mm - axisStart.z_mm };
        const offset = { x: origin.x_mm - axisStart.x_mm, y: origin.y_mm - axisStart.y_mm, z: origin.z_mm - axisStart.z_mm };
        const cross = { x: offset.y * axis.z - offset.z * axis.y, y: offset.z * axis.x - offset.x * axis.z, z: offset.x * axis.y - offset.y * axis.x };
        const centerDistance = Math.hypot(cross.x, cross.y, cross.z) / axisLength;
        const profileRadius = shape === "rectangle"
          ? Math.hypot(Number(profile.width_mm) / 2, Number(profile.height_mm) / 2)
          : shape === "oval" ? Math.max(Number(profile.width_mm), Number(profile.height_mm)) / 2
            : Number(shape === "circle" ? profile.diameter_mm : profile.outer_diameter_mm) / 2;
        if (centerDistance <= profileRadius + 1e-6) throw new Error(`part ${String(part.key)} revolution profile touches or crosses its axis; move the literal profile clear of the axis.`);
      }
      const startAngle = finite(part.start_angle_degrees, `part ${String(part.key)}.start_angle_degrees`);
      const endAngle = finite(part.end_angle_degrees, `part ${String(part.key)}.end_angle_degrees`);
      if (endAngle <= startAngle || endAngle - startAngle > 360) throw new Error(`part ${String(part.key)} revolution angle span must be greater than zero and at most 360 degrees.`);
    }
    if (part.depth_parameter !== undefined) {
      if (primitive !== "extrusion") throw new Error(`part ${String(part.key)}.depth_parameter is supported only for extrusion.`);
      const depthParameter = key(part.depth_parameter, `part ${String(part.key)}.depth_parameter`);
      const parameter = parametersByKey.get(depthParameter);
      if (!parameter || parameter.data_type !== "length") throw new Error(`part ${String(part.key)}.depth_parameter must reference a length parameter.`);
      const centered = Math.abs(start! + end!) < 1e-6; const startsAtZero = Math.abs(start!) < 1e-6;
      const pantsInletEndsAtOrigin = String(family.part_type ?? "") === "pants" && Math.abs(end!) < 1e-6;
      if (!centered && !startsAtZero && !pantsInletEndsAtOrigin) throw new Error(`part ${String(part.key)} with depth_parameter must be centered about zero or start at zero (Pants inlet may end at zero).`);
    }
    const cutTargets = array(part.cut_targets ?? [], `part ${String(part.key)}.cut_targets`).map((item, index) => key(item, `part ${String(part.key)}.cut_targets[${index}]`));
    const joinWith = array(part.join_with ?? [], `part ${String(part.key)}.join_with`).map((item, index) => key(item, `part ${String(part.key)}.join_with[${index}]`));
    if (new Set(cutTargets).size !== cutTargets.length || new Set(joinWith).size !== joinWith.length) throw new Error(`part ${String(part.key)} geometry-operation targets must be unique.`);
    if (operation === "void") {
      if (cutTargets.length === 0) throw new Error(`part ${String(part.key)} void form requires at least one cut_target.`);
      if (joinWith.length) throw new Error(`part ${String(part.key)} void form cannot declare join_with.`);
    } else if (cutTargets.length) throw new Error(`part ${String(part.key)} solid part cannot declare cut_targets.`);
    for (const targetKey of [...cutTargets, ...joinWith]) {
      const target = partsByKey.get(targetKey);
      if (!target || targetKey === String(part.key)) throw new Error(`part ${String(part.key)} references invalid geometry-operation target ${targetKey}.`);
      if (String(target.operation ?? "solid") !== "solid") throw new Error(`part ${String(part.key)} geometry-operation target ${targetKey} must be solid.`);
    }
    if (operation === "void" && (part.material_key !== undefined || part.material_parameter !== undefined)) throw new Error(`part ${String(part.key)} void geometry cannot declare a material.`);
    if (operation === "void" && part.visibility_parameter !== undefined) throw new Error(`part ${String(part.key)} void geometry cannot declare visibility_parameter.`);
    if (part.material_key !== undefined && part.material_parameter !== undefined) throw new Error(`part ${String(part.key)} cannot declare both material_key and material_parameter.`);
    if (part.material_key !== undefined) {
      const materialKey = key(part.material_key, `part ${String(part.key)}.material_key`);
      if (!materialKeys.has(materialKey)) throw new Error(`part ${String(part.key)} references unknown material ${materialKey}.`);
    }
    if (part.material_parameter !== undefined) {
      const parameterKey = key(part.material_parameter, `part ${String(part.key)}.material_parameter`);
      if (parametersByKey.get(parameterKey)?.data_type !== "material") throw new Error(`part ${String(part.key)}.material_parameter must reference a material parameter.`);
    }
    string(part.role, `part ${String(part.key)}.role`, 100);
    validatePresentationSubcategoryKey(part.subcategory_key, `part ${String(part.key)}`);
    validateVisibilityParameter(part.visibility_parameter, `part ${String(part.key)}`);
    const visibility = object(part.visibility ?? { coarse: true, medium: true, fine: true }, `part ${String(part.key)}.visibility`);
    for (const level of ["coarse", "medium", "fine"]) if (typeof visibility[level] !== "boolean") throw new Error(`part ${String(part.key)}.visibility.${level} must be boolean.`);
    for (const direction of ["front_back", "left_right", "plan_rcp", "only_when_cut"]) if (visibility[direction] !== undefined && typeof visibility[direction] !== "boolean") throw new Error(`part ${String(part.key)}.visibility.${direction} must be boolean.`);
  }

  const coordinationSubcategoryByPurpose: Record<string, string> = {
    maintenance_clearance: "DSCons Coordination Maintenance",
    access_clearance: "DSCons Coordination Access",
    service_clearance: "DSCons Coordination Service",
    installation_clearance: "DSCons Coordination Installation",
    removal_path: "DSCons Coordination Removal Path",
    operation_swing: "DSCons Coordination Operation Swing",
    connection_interface: "DSCons Coordination Connection Interface",
    support_interface: "DSCons Coordination Support Interface",
  };
  const coordinationZones = array(blueprint.coordination_zones ?? [], "blueprint.coordination_zones").map((item, index) => object(item, `coordination_zones[${index}]`));
  uniqueKeys(coordinationZones, "blueprint.coordination_zones");
  const coordinationZoneKeys = new Set(coordinationZones.map((zone) => String(zone.key)));
  const coordinationZoneParameterKeys = new Set<string>();
  if (coordinationZones.length && blueprint.target_lod !== "LOD_350") throw new Error("blueprint.coordination_zones requires target_lod=LOD_350.");
  for (const zone of coordinationZones) {
    const zoneKey = String(zone.key);
    if (partKeys.has(zoneKey)) throw new Error(`coordination zone ${zoneKey} cannot reuse a physical part key.`);
    const purpose = oneOf(zone.purpose, familyCoordinationZonePurposes, `coordination zone ${zoneKey}.purpose`);
    const shape = oneOf(zone.shape, ["box", "cylinder"] as const, `coordination zone ${zoneKey}.shape`);
    oneOf(zone.axis, ["x", "y", "z"] as const, `coordination zone ${zoneKey}.axis`);
    point3(zone.origin_mm, `coordination zone ${zoneKey}.origin_mm`);
    if (zone.role !== "non_physical_coordination_zone") throw new Error(`coordination zone ${zoneKey}.role must be non_physical_coordination_zone.`);
    const expectedSubcategory = coordinationSubcategoryByPurpose[purpose];
    if (zone.subcategory !== expectedSubcategory) throw new Error(`coordination zone ${zoneKey}.subcategory must be ${expectedSubcategory} for purpose ${purpose}.`);
    const materialKey = key(zone.material_key, `coordination zone ${zoneKey}.material_key`);
    const material = materials.find((candidate) => candidate.key === materialKey);
    if (!material) throw new Error(`coordination zone ${zoneKey} references unknown material ${materialKey}.`);
    if (Number(material.transparency ?? 0) < 70) throw new Error(`coordination zone ${zoneKey} material ${materialKey} must have transparency of at least 70.`);
    const dimension = (literalProperty: string, parameterProperty: string): void => {
      const hasLiteral = zone[literalProperty] !== undefined; const hasParameter = zone[parameterProperty] !== undefined;
      if (hasLiteral === hasParameter) throw new Error(`coordination zone ${zoneKey} must declare exactly one ${literalProperty} or ${parameterProperty}.`);
      if (hasLiteral) positive(zone[literalProperty], `coordination zone ${zoneKey}.${literalProperty}`);
      else {
        const parameterKey = key(zone[parameterProperty], `coordination zone ${zoneKey}.${parameterProperty}`);
        const parameter = parametersByKey.get(parameterKey);
        if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined)
          throw new Error(`coordination zone ${zoneKey}.${parameterProperty} must reference a direct Length Type Parameter.`);
        coordinationZoneParameterKeys.add(parameterKey);
      }
    };
    if (shape === "box") {
      dimension("width_mm", "width_parameter"); dimension("height_mm", "height_parameter"); dimension("depth_mm", "depth_parameter");
      if ((zone.width_parameter === undefined) !== (zone.height_parameter === undefined)) throw new Error(`coordination zone ${zoneKey} box width/height must both be literal or both use direct Length Type Parameters.`);
      if (["diameter_mm", "diameter_parameter", "length_mm", "length_parameter"].some((property) => zone[property] !== undefined)) throw new Error(`coordination zone ${zoneKey} box cannot declare cylinder dimensions.`);
    } else {
      dimension("diameter_mm", "diameter_parameter"); dimension("length_mm", "length_parameter");
      if (["width_mm", "width_parameter", "height_mm", "height_parameter", "depth_mm", "depth_parameter"].some((property) => zone[property] !== undefined)) throw new Error(`coordination zone ${zoneKey} cylinder cannot declare box dimensions.`);
    }
    validateVisibilityParameter(zone.visibility_parameter, `coordination zone ${zoneKey}`);
    const visibility = object(zone.visibility, `coordination zone ${zoneKey}.visibility`);
    for (const level of ["coarse", "medium", "fine"]) if (typeof visibility[level] !== "boolean") throw new Error(`coordination zone ${zoneKey}.visibility.${level} must be boolean.`);
    for (const direction of ["front_back", "left_right", "plan_rcp", "only_when_cut"]) if (visibility[direction] !== undefined && typeof visibility[direction] !== "boolean") throw new Error(`coordination zone ${zoneKey}.visibility.${direction} must be boolean.`);
  }

  const connectors = array(blueprint.connectors ?? [], "blueprint.connectors").map((item, index) => object(item, `connectors[${index}]`));
  uniqueKeys(connectors, "blueprint.connectors");
  for (const connector of connectors) {
    oneOf(connector.discipline, familyConnectorDisciplines, `connector ${String(connector.key)}.discipline`);
    const hostPart = key(connector.host_part, `connector ${String(connector.key)}.host_part`);
    if (!partKeys.has(hostPart)) throw new Error(`connector ${String(connector.key)} references unknown host_part ${hostPart}.`);
    if (String(partsByKey.get(hostPart)?.operation ?? "solid") !== "solid") throw new Error(`connector ${String(connector.key)} cannot use void host_part ${hostPart}.`);
    if ((partsByKey.get(hostPart)?.path as Json | undefined)?.kind === "reference_line") throw new Error(`connector ${String(connector.key)} cannot use an angular reference_line sweep until dynamic connector-face orientation is runtime-certified.`);
    const hostFace = oneOf(connector.host_face, ["start", "end", "positive_y", "negative_y", "positive_z", "negative_z", "path_start", "path_end"] as const, `connector ${String(connector.key)}.host_face`);
    if (["path_start", "path_end"].includes(hostFace)) {
      const host = partsByKey.get(hostPart); const path = host?.path as Json | undefined;
      if (host?.primitive !== "sweep" || !["line", "polyline", "arc", "offset"].includes(String(path?.kind))) throw new Error(`connector ${String(connector.key)}.${hostFace} requires a line, polyline, arc or parameterized offset sweep host_part.`);
    }
    string(connector.role, `connector ${String(connector.key)}.role`, 100);
    const discipline = String(connector.discipline); const profile = connector.profile === undefined ? "logical" : oneOf(connector.profile, ["round", "rectangular", "oval", "logical"] as const, `connector ${String(connector.key)}.profile`);
    const allowedProfiles: Record<string, readonly string[]> = {
      duct: ["round", "rectangular", "oval"], pipe: ["round"], conduit: ["round"], cable_tray: ["rectangular"], electrical: ["logical"],
    };
    if (!allowedProfiles[discipline]?.includes(profile)) throw new Error(`connector ${String(connector.key)} profile ${profile} is not physically valid for ${discipline}; allowed: ${allowedProfiles[discipline]?.join(", ")}.`);
    if (connectorClassifications[discipline]) {
      const classification = string(connector.system_classification, `connector ${String(connector.key)}.system_classification`, 80);
      if (!connectorClassifications[discipline].includes(classification)) throw new Error(`connector ${String(connector.key)}.system_classification is not valid for ${discipline}.`);
    }
    const requireLengthParameter = (property: string): void => {
      const parameterKey = key(connector[property], `connector ${String(connector.key)}.${property}`);
      const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== "length") throw new Error(`connector ${String(connector.key)}.${property} must reference a length parameter.`);
    };
    if (["duct", "pipe", "conduit", "cable_tray"].includes(discipline) && profile === "logical") throw new Error(`connector ${String(connector.key)} requires a physical profile.`);
    if (profile === "round") requireLengthParameter("diameter_parameter");
    if (["rectangular", "oval"].includes(profile)) { requireLengthParameter("width_parameter"); requireLengthParameter("height_parameter"); }
    if (connector.linked_to !== undefined) key(connector.linked_to, `connector ${String(connector.key)}.linked_to`);
    if (connector.primary !== undefined && typeof connector.primary !== "boolean") throw new Error(`connector ${String(connector.key)}.primary must be boolean.`);
    if (connector.flow_direction !== undefined) {
      if (!["duct", "pipe"].includes(discipline)) throw new Error(`connector ${String(connector.key)}.flow_direction is supported only for duct/pipe connectors.`);
      oneOf(connector.flow_direction, ["bidirectional", "in", "out"] as const, `connector ${String(connector.key)}.flow_direction`);
    }
    if (connector.flow_configuration !== undefined) {
      if (!["duct", "pipe"].includes(discipline)) throw new Error(`connector ${String(connector.key)}.flow_configuration is supported only for duct/pipe connectors.`);
      const configuration = oneOf(connector.flow_configuration, ["calculated", "preset", "system", "demand"] as const, `connector ${String(connector.key)}.flow_configuration`);
      if (discipline === "duct" && configuration === "demand") throw new Error(`connector ${String(connector.key)} duct flow_configuration cannot be demand.`);
    }
    const mechanicalValues = (parameterKey: string, parameter: Json): unknown[] =>
      (types.length ? types : [{ name: "default", values: {} }]).map((type) => object(type.values ?? {}, `type ${String(type.name ?? "default")}.values`)[parameterKey] ?? parameter.default);
    const requireMechanicalParameter = (property: string, expectedType: string, requiredConfiguration: string): { parameterKey: string; parameter: Json; values: unknown[] } | undefined => {
      if (connector[property] === undefined) return undefined;
      if (!["duct", "pipe"].includes(discipline)) throw new Error(`connector ${String(connector.key)}.${property} is supported only for duct/pipe connectors.`);
      if (connector.flow_configuration !== requiredConfiguration) throw new Error(`connector ${String(connector.key)}.${property} requires flow_configuration=${requiredConfiguration}.`);
      const parameterKey = key(connector[property], `connector ${String(connector.key)}.${property}`);
      const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== expectedType || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined)
        throw new Error(`connector ${String(connector.key)}.${property} must reference a direct ${expectedType} Type Family Parameter.`);
      const values = mechanicalValues(parameterKey, parameter);
      if (values.some((value) => value === undefined)) throw new Error(`connector ${String(connector.key)}.${property} requires a value/default for every Family type.`);
      return { parameterKey, parameter, values };
    };
    if (connector.flow_parameter !== undefined) requireMechanicalParameter("flow_parameter", discipline === "duct" ? "airflow" : "flow", "preset");
    const flowFactor = requireMechanicalParameter("flow_factor_parameter", "number", "system");
    if (flowFactor && flowFactor.values.some((value) => Number(value) < 0 || Number(value) > 1)) throw new Error(`connector ${String(connector.key)}.flow_factor_parameter must stay between 0 and 1 for every Family type.`);
    if (connector.allow_slope_adjustments !== undefined) {
      if (discipline !== "pipe") throw new Error(`connector ${String(connector.key)}.allow_slope_adjustments is supported only for pipe connectors.`);
      if (typeof connector.allow_slope_adjustments !== "boolean") throw new Error(`connector ${String(connector.key)}.allow_slope_adjustments must be boolean.`);
      if (connector.system_classification !== "Global") throw new Error(`connector ${String(connector.key)}.allow_slope_adjustments requires system_classification=Global because Revit exposes this control for the Global pipe connector workflow.`);
    }
    if (connector.loss_method !== undefined) {
      if (!["duct", "pipe"].includes(discipline)) throw new Error(`connector ${String(connector.key)}.loss_method is supported only for duct/pipe connectors.`);
      const lossMethod = oneOf(connector.loss_method, ["not_defined", "table", "specific_loss", "coefficient"] as const, `connector ${String(connector.key)}.loss_method`);
      if (discipline === "duct" && lossMethod === "table") throw new Error(`connector ${String(connector.key)} duct loss_method cannot be table.`);
      if (lossMethod === "coefficient" && connector.loss_coefficient === undefined) throw new Error(`connector ${String(connector.key)} loss_method coefficient requires loss_coefficient.`);
      if (lossMethod === "specific_loss" && connector.pressure_drop_pa === undefined) throw new Error(`connector ${String(connector.key)} loss_method specific_loss requires pressure_drop_pa.`);
    }
    if (connector.loss_coefficient !== undefined && finite(connector.loss_coefficient, `connector ${String(connector.key)}.loss_coefficient`) < 0) throw new Error(`connector ${String(connector.key)}.loss_coefficient must be non-negative.`);
    if (connector.pressure_drop_pa !== undefined && finite(connector.pressure_drop_pa, `connector ${String(connector.key)}.pressure_drop_pa`) < 0) throw new Error(`connector ${String(connector.key)}.pressure_drop_pa must be non-negative.`);
    if (connector.joint_type !== undefined) oneOf(connector.joint_type, ["undefined", "flanged", "welded", "threaded", "grooved", "glued", "soldered"] as const, `connector ${String(connector.key)}.joint_type`);
    if (connector.gender !== undefined) oneOf(connector.gender, ["undefined", "male", "female"] as const, `connector ${String(connector.key)}.gender`);
    if (connector.engagement_length_mm !== undefined && finite(connector.engagement_length_mm, `connector ${String(connector.key)}.engagement_length_mm`) < 0) throw new Error(`connector ${String(connector.key)}.engagement_length_mm must be non-negative.`);
    const electricalBindings: Record<string, string> = {
      voltage_parameter: "voltage",
      apparent_load_parameter: "apparent_power",
      number_of_poles_parameter: "number_of_poles",
      power_factor_parameter: "number",
      balanced_load_parameter: "yesno",
      load_classification_parameter: "load_classification",
    };
    for (const [property, expectedType] of Object.entries(electricalBindings)) {
      if (connector[property] === undefined) continue;
      if (discipline !== "electrical") throw new Error(`connector ${String(connector.key)}.${property} is supported only for electrical connectors.`);
      const parameterKey = key(connector[property], `connector ${String(connector.key)}.${property}`);
      const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== expectedType) throw new Error(`connector ${String(connector.key)}.${property} must reference a ${expectedType} Family Parameter.`);
      const effectiveValues = (types.length ? types : [{ name: "default", values: {} }]).map((type) => ({
        typeName: String(type.name ?? "default"),
        value: object(type.values ?? {}, `type ${String(type.name ?? "default")}.values`)[parameterKey] ?? parameter.default,
      }));
      if (effectiveValues.some((item) => item.value === undefined)) throw new Error(`connector ${String(connector.key)}.${property} requires a value/default for every Family type.`);
      if (property === "voltage_parameter" && effectiveValues.some((item) => Number(item.value) <= 0)) throw new Error(`connector ${String(connector.key)} voltage must be greater than zero for every Family type.`);
      if (property === "apparent_load_parameter" && effectiveValues.some((item) => Number(item.value) < 0)) throw new Error(`connector ${String(connector.key)} apparent load must be non-negative for every Family type.`);
      if (property === "power_factor_parameter" && effectiveValues.some((item) => Number(item.value) <= 0 || Number(item.value) > 1)) throw new Error(`connector ${String(connector.key)} power factor must be greater than zero and at most 1 for every Family type.`);
      if (property === "balanced_load_parameter" && String(connector.system_classification) === "PowerBalanced" && effectiveValues.some((item) => item.value !== true)) throw new Error(`connector ${String(connector.key)} PowerBalanced classification requires balanced_load=true for every Family type.`);
      if (property === "balanced_load_parameter" && String(connector.system_classification) === "PowerUnBalanced" && effectiveValues.some((item) => item.value !== false)) throw new Error(`connector ${String(connector.key)} PowerUnBalanced classification requires balanced_load=false for every Family type.`);
    }
    if (connector.power_factor_state !== undefined) {
      if (discipline !== "electrical") throw new Error(`connector ${String(connector.key)}.power_factor_state is supported only for electrical connectors.`);
      oneOf(connector.power_factor_state, ["leading", "lagging"] as const, `connector ${String(connector.key)}.power_factor_state`);
    }
    if (discipline === "electrical" && String(connector.system_classification).startsWith("Power")) {
      for (const property of Object.keys(electricalBindings)) if (connector[property] === undefined) throw new Error(`power connector ${String(connector.key)} requires ${property}.`);
      if (connector.power_factor_state === undefined) throw new Error(`power connector ${String(connector.key)} requires power_factor_state.`);
    }
  }
  const connectorKeys = new Set(connectors.map((connector) => String(connector.key)));
  for (const connector of connectors) {
    if (connector.linked_to !== undefined && !connectorKeys.has(String(connector.linked_to))) throw new Error(`connector ${String(connector.key)} references unknown linked_to ${String(connector.linked_to)}.`);
    if (connector.linked_to === connector.key) throw new Error(`connector ${String(connector.key)} cannot link to itself.`);
  }
  for (const discipline of familyConnectorDisciplines) if (connectors.filter((connector) => connector.discipline === discipline && connector.primary === true).length > 1) throw new Error(`only one ${discipline} connector may be declared primary.`);
  const expectedDiscipline: Record<string, string> = { duct_accessory: "duct", duct_fitting: "duct", pipe_accessory: "pipe", pipe_fitting: "pipe", conduit_fitting: "conduit", cable_tray_fitting: "cable_tray" };
  if (expectedDiscipline[familyCategory] && connectors.some((connector) => connector.discipline !== expectedDiscipline[familyCategory])) throw new Error(`all ${familyCategory} connectors must use discipline ${expectedDiscipline[familyCategory]}.`);
  const partType = String(family.part_type ?? "");
  const normalizedPartType = partType
    .replace(/^(channel|ladder)_cable_tray_/, "")
    .replace(/^vertical_elbow$/, "elbow")
    .replace(/^junction_box_(tee|cross|elbow)$/, "$1");
  const exactConnectorCounts: Record<string, number> = {
    cap: 1, end_cap: 1,
    elbow: 2, transition: 2, offset: 2, union: 2, breaks_into: 2, valve_breaks_into: 2, valve_normal: 2, damper: 2, inline_sensor: 2, sensor: 2, pipe_flange: 2, pipe_mechanical_coupling: 2,
    tee: 3, wye: 3, lateral_tee: 3, tap_perpendicular: 3, tap_adjustable: 3, pants: 3,
    cross: 4, lateral_cross: 4,
  };
  if (exactConnectorCounts[normalizedPartType] !== undefined && connectors.length !== exactConnectorCounts[normalizedPartType]) throw new Error(`part_type ${partType} requires exactly ${exactConnectorCounts[normalizedPartType]} connectors; received ${connectors.length}.`);
  if (normalizedPartType === "multi_port" && connectors.length < 3) throw new Error(`part_type ${partType} requires at least three connectors.`);
  if (family.round_connector_dimension !== undefined && !connectors.some((connector) => connector.profile === "round")) throw new Error("blueprint.family.round_connector_dimension requires at least one round connector.");
  if (["breaks_into", "valve_breaks_into"].includes(normalizedPartType)) {
    const fittingLabel = `part_type ${partType}`;
    if (!["duct_accessory", "pipe_accessory"].includes(familyCategory)) throw new Error(`${fittingLabel} is valid only for Duct Accessory or Pipe Accessory.`);
    const inlineBodyKey = String(connectors[0]?.host_part ?? "");
    if (!inlineBodyKey || connectors.some((connector) => String(connector.host_part) !== inlineBodyKey)) throw new Error(`${fittingLabel} connectors must share one inline routing body.`);
    const body = partsByKey.get(inlineBodyKey);
    if (!body || body.primitive !== "extrusion" || String(body.operation ?? "solid") !== "solid" || body.axis !== "x" || body.axis_direction !== undefined) throw new Error(`${fittingLabel} requires one solid X-axis extrusion routing body.`);
    if (!(Number(body.start_mm) < Number(body.end_mm))) throw new Error(`${fittingLabel} routing body must have start_mm < end_mm.`);
    const startConnector = connectors.find((connector) => connector.host_face === "start"); const endConnector = connectors.find((connector) => connector.host_face === "end");
    if (!startConnector || !endConnector || new Set(connectors.map((connector) => String(connector.host_face))).size !== 2) throw new Error(`${fittingLabel} connectors must use start and end faces of the inline routing body.`);
    if (startConnector.primary !== true || connectors.filter((connector) => connector.primary === true).length !== 1) throw new Error(`${fittingLabel} requires exactly one primary connector on the X-axis start face.`);
    if (startConnector.linked_to !== endConnector.key || endConnector.linked_to !== startConnector.key) throw new Error(`${fittingLabel} connectors must be linked reciprocally.`);
    if (startConnector.system_classification !== endConnector.system_classification || !["Fitting", "Global"].includes(String(startConnector.system_classification))) throw new Error(`${fittingLabel} connectors must share system_classification=Fitting or Global.`);
    const profile = object(body.profile, `${fittingLabel} routing body profile`); const shape = String(profile.shape);
    if (!["circle", "rectangle", "oval"].includes(shape)) throw new Error(`${fittingLabel} routing body profile must be circle, rectangle or oval.`);
    if (familyCategory === "pipe_accessory" && shape !== "circle") throw new Error(`${fittingLabel} Pipe Accessory routing body must use a circle profile.`);
    for (const connector of connectors) {
      if (shape === "circle" && (connector.profile !== "round" || connector.diameter_parameter !== profile.diameter_parameter)) throw new Error(`${fittingLabel} round connector diameter_parameter must match the routing body profile.`);
      if (shape === "rectangle" && (connector.profile !== "rectangular" || connector.width_parameter !== profile.width_parameter || connector.height_parameter !== profile.height_parameter)) throw new Error(`${fittingLabel} rectangular connector size parameters must match the routing body profile.`);
      if (shape === "oval" && (connector.profile !== "oval" || connector.width_parameter !== profile.width_parameter || connector.height_parameter !== profile.height_parameter)) throw new Error(`${fittingLabel} oval connector size parameters must match the routing body profile.`);
    }
  }
  if (normalizedPartType === "elbow") {
    const elbowParts = parts.filter((part) => part.primitive === "sweep" && String(part.operation ?? "solid") === "solid" && (part.path as Json | undefined)?.kind === "arc");
    if (elbowParts.length !== 1) throw new Error(`part_type elbow requires exactly one solid arc sweep body; received ${elbowParts.length}.`);
    const elbow = elbowParts[0]; const elbowKey = String(elbow.key); const elbowProfile = object(elbow.profile, `part ${elbowKey}.profile`);
    if (!["circle", "rectangle", "oval"].includes(String(elbowProfile.shape))) throw new Error(`part_type ${partType} requires a circle, rectangle or oval arc sweep profile.`);
    if (partType.endsWith("_vertical_elbow") && (elbow.path as Json).plane !== "xz") throw new Error(`part_type ${partType} requires an xz arc path for vertical routing.`);
    if (/^(channel|ladder)_cable_tray_elbow$/.test(partType) && (elbow.path as Json).plane !== "xy") throw new Error(`part_type ${partType} requires an xy arc path; use the vertical-elbow Part Type for xz routing.`);
    if ((elbow.path as Json).radius_parameter === undefined) throw new Error("part_type elbow requires a parameterized bend radius.");
    const elbowConnectors = connectors.filter((connector) => connector.host_part === elbowKey);
    if (elbowConnectors.length !== 2 || new Set(elbowConnectors.map((connector) => connector.host_face)).size !== 2 || !elbowConnectors.some((connector) => connector.host_face === "path_start") || !elbowConnectors.some((connector) => connector.host_face === "path_end")) throw new Error("part_type elbow connectors must use path_start and path_end on the arc sweep body.");
    const primary = elbowConnectors.filter((connector) => connector.primary === true);
    if (primary.length !== 1 || primary[0].host_face !== "path_start") throw new Error("part_type elbow requires exactly one primary connector on path_start, the X-axis face.");
    for (const connector of elbowConnectors) {
      const peer = elbowConnectors.find((candidate) => candidate.key === connector.linked_to);
      if (!peer || peer.linked_to !== connector.key) throw new Error("part_type elbow connectors must be linked reciprocally.");
      if (elbowProfile.shape === "circle" && connector.diameter_parameter !== elbowProfile.diameter_parameter) throw new Error("part_type elbow round connector diameter_parameter must match the arc sweep profile.");
      if (elbowProfile.shape === "rectangle" && (connector.width_parameter !== elbowProfile.width_parameter || connector.height_parameter !== elbowProfile.height_parameter)) throw new Error("part_type elbow rectangular connector size parameters must match the arc sweep profile.");
      if (elbowProfile.shape === "oval" && (connector.profile !== "oval" || connector.width_parameter !== elbowProfile.width_parameter || connector.height_parameter !== elbowProfile.height_parameter)) throw new Error("part_type elbow oval connector size parameters must match the arc sweep profile.");
    }
  }
  const pathwayCategory = ["conduit_fitting", "cable_tray_fitting"].includes(familyCategory);
  if (pathwayCategory && ["transition", "union", "offset"].includes(normalizedPartType)) {
    const fittingLabel = `part_type ${partType}`;
    const solidParts = parts.filter((part) => String(part.operation ?? "solid") === "solid");
    if (solidParts.length !== 1) throw new Error(`${fittingLabel} requires exactly one solid fitting body; received ${solidParts.length}.`);
    const body = solidParts[0]; const bodyKey = String(body.key);
    const hosted = connectors.filter((connector) => connector.host_part === bodyKey);
    if (hosted.length !== 2 || connectors.length !== 2) throw new Error(`${fittingLabel} requires exactly two connectors on its single fitting body.`);
    const requireDirectLengthType = (raw: unknown, label: string): string => {
      const parameterKey = key(raw, label); const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`${label} must reference a direct Length Type Parameter.`);
      return parameterKey;
    };
    const profileBinding = (profile: Json, label: string): { shape: string; keys: string[] } => {
      const shape = String(profile.shape);
      const expectedShape = familyCategory === "conduit_fitting" ? "circle" : "rectangle";
      if (shape !== expectedShape) throw new Error(`${fittingLabel} ${label} must use a ${expectedShape} profile for ${familyCategory}.`);
      const keys = shape === "circle"
        ? [requireDirectLengthType(profile.diameter_parameter, `${fittingLabel} ${label}.diameter_parameter`)]
        : [requireDirectLengthType(profile.width_parameter, `${fittingLabel} ${label}.width_parameter`), requireDirectLengthType(profile.height_parameter, `${fittingLabel} ${label}.height_parameter`)];
      return { shape, keys };
    };
    const connectorMatches = (connector: Json, binding: { shape: string; keys: string[] }, label: string): void => {
      if (binding.shape === "circle") {
        if (connector.profile !== "round" || connector.diameter_parameter !== binding.keys[0]) throw new Error(`${fittingLabel} ${label} round connector size must match its body profile.`);
      } else if (connector.profile !== "rectangular" || connector.width_parameter !== binding.keys[0] || connector.height_parameter !== binding.keys[1]) throw new Error(`${fittingLabel} ${label} rectangular connector size must match its body profile.`);
    };
    const requireEndpointConnectors = (startFace: string, endFace: string, startBinding: { shape: string; keys: string[] }, endBinding: { shape: string; keys: string[] }): void => {
      const startConnector = hosted.find((connector) => connector.host_face === startFace); const endConnector = hosted.find((connector) => connector.host_face === endFace);
      if (!startConnector || !endConnector || new Set(hosted.map((connector) => connector.host_face)).size !== 2) throw new Error(`${fittingLabel} connectors must use ${startFace} and ${endFace} on the fitting body.`);
      if (hosted.filter((connector) => connector.primary === true).length !== 1 || startConnector.primary !== true) throw new Error(`${fittingLabel} requires exactly one primary connector on ${startFace}, the X-axis inlet.`);
      if (startConnector.linked_to !== endConnector.key || endConnector.linked_to !== startConnector.key) throw new Error(`${fittingLabel} connectors must be linked reciprocally.`);
      connectorMatches(startConnector, startBinding, "start"); connectorMatches(endConnector, endBinding, "end");
    };
    if (normalizedPartType === "transition") {
      if (body.primitive !== "blend" || body.axis !== "x" || body.axis_direction !== undefined) throw new Error(`${fittingLabel} requires one solid X-axis blend body.`);
      const startBinding = profileBinding(object(body.profile, `${fittingLabel} profile`), "start profile");
      const endBinding = profileBinding(object(body.end_profile, `${fittingLabel} end_profile`), "end profile");
      if (startBinding.shape === endBinding.shape && startBinding.keys.length === endBinding.keys.length && startBinding.keys.every((item, index) => item === endBinding.keys[index])) throw new Error(`${fittingLabel} must change at least one size-parameter binding; use union for identical ends.`);
      requireEndpointConnectors("start", "end", startBinding, endBinding);
    } else if (normalizedPartType === "union") {
      if (body.primitive !== "extrusion" || body.axis !== "x" || body.axis_direction !== undefined) throw new Error(`${fittingLabel} requires one solid X-axis extrusion body.`);
      requireDirectLengthType(body.depth_parameter, `${fittingLabel} depth_parameter`);
      const binding = profileBinding(object(body.profile, `${fittingLabel} profile`), "profile");
      requireEndpointConnectors("start", "end", binding, binding);
    } else {
      if (familyCategory !== "cable_tray_fitting") throw new Error(`${fittingLabel} is not an Autodesk Conduit Fitting Part Type; conduit offsets must be modeled/routed without declaring an offset Part Type.`);
      const path = object(body.path, `${fittingLabel} path`);
      if (body.primitive !== "sweep") throw new Error(`${fittingLabel} requires one solid sweep body.`);
      const plane = String(path.plane);
      if (!["xy", "xz"].includes(plane)) throw new Error(`${fittingLabel} path plane must be xy or xz.`);
      if (body.profile_location !== undefined && body.profile_location !== "start") throw new Error(`${fittingLabel} parameterized sweep profile requires profile_location=start.`);
      if (path.kind === "offset") {
        const directParameter = (property: string, expectedType: "length" | "angle"): string => {
          const parameterKey = key(path[property], `${fittingLabel} path.${property}`); const parameter = parametersByKey.get(parameterKey);
          if (!parameter || parameter.data_type !== expectedType || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined)
            throw new Error(`${fittingLabel} path.${property} must reference a direct ${expectedType === "angle" ? "Angle" : "Length"} Type Parameter.`);
          return parameterKey;
        };
        const leadInKey = directParameter("lead_in_parameter", "length"); const leadOutKey = directParameter("lead_out_parameter", "length");
        const lateralKey = directParameter("lateral_offset_parameter", "length"); const angleKey = directParameter("offset_angle_parameter", "angle");
        if (new Set([leadInKey, leadOutKey, lateralKey, angleKey]).size !== 4) throw new Error(`${fittingLabel} parameterized path requires four distinct parameters.`);
        const effectiveTypes = types.length ? types : [{ name: "default", values: {} }];
        for (const type of effectiveTypes) {
          const values = object(type.values ?? {}, `type ${String(type.name ?? "default")}.values`);
          const effective = (parameterKey: string): number => {
            const value = values[parameterKey] ?? parametersByKey.get(parameterKey)?.default;
            if (value === undefined) throw new Error(`${fittingLabel} path parameter ${parameterKey} requires a value/default for Family type ${String(type.name ?? "default")}.`);
            return finite(value, `${fittingLabel} path parameter ${parameterKey} in type ${String(type.name ?? "default")}`);
          };
          for (const parameterKey of [leadInKey, leadOutKey, lateralKey]) if (effective(parameterKey) <= 0) throw new Error(`${fittingLabel} path parameter ${parameterKey} must be greater than zero in every Family type.`);
          const angle = effective(angleKey);
          if (angle < 15 || angle > 75) throw new Error(`${fittingLabel} path offset angle must stay between 15 and 75 degrees in every Family type.`);
        }
      } else {
        const points = array(path.points_mm, `${fittingLabel} path.points_mm`).map((item, index) => point3(item, `${fittingLabel} path.points_mm[${index}]`));
        if (path.kind !== "polyline" || points.length !== 4) throw new Error(`${fittingLabel} requires either a parameterized offset path or one legacy four-point polyline path.`);
        const vector = (first: Point3, second: Point3): Vector3 => ({ x: second.x_mm - first.x_mm, y: second.y_mm - first.y_mm, z: second.z_mm - first.z_mm });
        const isPositiveX = (direction: Vector3): boolean => direction.x > 1e-6 && Math.abs(direction.y) <= 1e-6 && Math.abs(direction.z) <= 1e-6;
        if (!isPositiveX(vector(points[0], points[1])) || !isPositiveX(vector(points[2], points[3]))) throw new Error(`${fittingLabel} first and last path segments must have +X tangents.`);
        if (Math.abs(points[0].y_mm) > 1e-6 || Math.abs(points[0].z_mm) > 1e-6) throw new Error(`${fittingLabel} path_start must lie on the Family X-axis.`);
        const lateralDisplacement = plane === "xy" ? points[3].y_mm - points[0].y_mm : points[3].z_mm - points[0].z_mm;
        if (Math.abs(lateralDisplacement) <= 1e-6) throw new Error(`${fittingLabel} path must have a non-zero lateral displacement.`);
        const diagonal = vector(points[1], points[2]); const diagonalLength = Math.hypot(diagonal.x, diagonal.y, diagonal.z);
        const diagonalAngle = Math.acos(Math.max(-1, Math.min(1, diagonal.x / diagonalLength))) * 180 / Math.PI;
        if (diagonal.x <= 1e-6 || diagonalAngle < 15 - 1e-6 || diagonalAngle > 75 + 1e-6) throw new Error(`${fittingLabel} diagonal path segment must form a forward 15-75 degree angle to +X.`);
      }
      const binding = profileBinding(object(body.profile, `${fittingLabel} profile`), "profile");
      requireEndpointConnectors("path_start", "path_end", binding, binding);
    }
  }
  if (["tee", "wye", "lateral_tee", "tap_perpendicular", "tap_adjustable", "cross", "lateral_cross"].includes(normalizedPartType)) {
    const fittingBehavior = normalizedPartType;
    const connectorsByPart = new Map<string, Json[]>();
    for (const connector of connectors) {
      const hostPart = String(connector.host_part);
      const hosted = connectorsByPart.get(hostPart) ?? [];
      hosted.push(connector);
      connectorsByPart.set(hostPart, hosted);
    }
    const connectorHosts = [...connectorsByPart.entries()];
    if (connectorHosts.length !== 2) throw new Error(`part_type ${partType} requires one run body and one branch body; received connectors on ${connectorHosts.length} parts.`);
    const runEntry = connectorHosts.find(([candidateKey, hosted]) => hosted.length === 2 && partsByKey.get(candidateKey)?.axis === "x");
    const branchEntry = connectorHosts.find(([candidateKey]) => candidateKey !== runEntry?.[0]);
    const branchConnectorCount = ["cross", "lateral_cross"].includes(fittingBehavior) ? 2 : 1;
    if (!runEntry || !branchEntry || branchEntry[1].length !== branchConnectorCount) throw new Error(`part_type ${partType} requires two connectors on the X-axis main run and ${branchConnectorCount} connector(s) on one branch body.`);
    const [runKey, runConnectors] = runEntry; const [branchKey, branchConnectors] = branchEntry;
    const run = partsByKey.get(runKey); const branch = partsByKey.get(branchKey);
    if (!run || run.primitive !== "extrusion" || String(run.operation ?? "solid") !== "solid" || run.axis !== "x" || run.axis_direction !== undefined) throw new Error(`part_type ${partType} main run must be a solid X-axis extrusion.`);
    if (!branch || branch.primitive !== "extrusion" || String(branch.operation ?? "solid") !== "solid") throw new Error(`part_type ${partType} branch must be a solid extrusion.`);
    const runStart = Number(run.start_mm); const runEnd = Number(run.end_mm); const branchStart = Number(branch.start_mm); const branchEnd = Number(branch.end_mm);
    if (!(runStart < 0 && runEnd > 0 && Math.abs(runStart + runEnd) <= 1e-6)) throw new Error(`part_type ${partType} main run must be centered on the Family origin with symmetric start_mm/end_mm.`);
    const branchPositive = Math.abs(branchStart) <= 1e-6 && branchEnd > 0;
    const branchNegative = branchStart < 0 && Math.abs(branchEnd) <= 1e-6;
    const branchCentered = branchStart < 0 && branchEnd > 0 && Math.abs(branchStart + branchEnd) <= 1e-6;
    if (branchConnectorCount === 1 && !branchPositive && !branchNegative) throw new Error(`part_type ${partType} branch must start or end at the Family origin and extend in one direction.`);
    if (branchConnectorCount === 2 && !branchCentered) throw new Error(`part_type ${partType} branch must be centered on the Family origin with symmetric start_mm/end_mm.`);
    const angled = ["wye", "lateral_tee", "tap_adjustable", "lateral_cross"].includes(fittingBehavior);
    if (angled) {
      if (branch.axis !== undefined || branch.axis_direction === undefined) throw new Error(`part_type ${partType} branch requires axis_direction instead of an orthogonal axis.`);
      const direction = branch.axis_direction as Vector3;
      const lateralComponents = [Math.abs(direction.y) > 1e-6, Math.abs(direction.z) > 1e-6].filter(Boolean).length;
      if (lateralComponents !== 1) throw new Error(`part_type ${partType} branch axis_direction must lie in the XY or XZ plane.`);
      const angle = Math.acos(Math.min(1, Math.abs(direction.x))) * 180 / Math.PI;
      if (angle < 15 - 1e-6 || angle > 75 + 1e-6) throw new Error(`part_type ${partType} branch axis_direction must form an oblique 15-75 degree angle to the X-axis.`);
    } else if (branch.axis_direction !== undefined || !["y", "z"].includes(String(branch.axis))) throw new Error(`part_type ${partType} branch must be a solid Y- or Z-axis extrusion.`);
    const requireDirectLengthType = (raw: unknown, label: string): string => {
      const parameterKey = key(raw, label); const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`${label} must reference a direct Length Type Parameter.`);
      return parameterKey;
    };
    requireDirectLengthType(run.depth_parameter, `part_type ${partType} main run depth_parameter`);
    requireDirectLengthType(branch.depth_parameter, `part_type ${partType} branch depth_parameter`);
    const runFaces = new Set(runConnectors.map((connector) => String(connector.host_face)));
    if (runFaces.size !== 2 || !runFaces.has("start") || !runFaces.has("end")) throw new Error(`part_type ${partType} main run connectors must use start and end faces.`);
    const branchFaces = new Set(branchConnectors.map((connector) => String(connector.host_face)));
    if (branchConnectorCount === 1 && (branchFaces.size !== 1 || !branchFaces.has(branchPositive ? "end" : "start"))) throw new Error(`part_type ${partType} branch connector must use the outer branch face, not the junction face.`);
    if (branchConnectorCount === 2 && (branchFaces.size !== 2 || !branchFaces.has("start") || !branchFaces.has("end"))) throw new Error(`part_type ${partType} branch connectors must use its start and end faces.`);
    const primary = runConnectors.filter((connector) => connector.primary === true);
    if (primary.length !== 1 || primary[0].host_face !== "start") throw new Error(`part_type ${partType} requires exactly one primary connector on the X-axis main-run start face.`);
    if (connectors.some((connector) => connector.linked_to !== undefined)) throw new Error(`part_type ${partType} connector linked_to is blocked until Revit multi-port link topology is runtime-certified; geometry and connector placement remain independently verifiable.`);
    const runJoins = array(run.join_with ?? [], `part ${runKey}.join_with`).map(String);
    const branchJoins = array(branch.join_with ?? [], `part ${branchKey}.join_with`).map(String);
    if (!runJoins.includes(branchKey) && !branchJoins.includes(runKey)) throw new Error(`part_type ${partType} run and branch bodies must declare join_with so the junction is one fitting body.`);
    const verifyProfile = (part: Json, hosted: Json[], label: string): void => {
      const profile = object(part.profile, `${label}.profile`); const shape = String(profile.shape);
      if (!['circle', 'rectangle', 'oval'].includes(shape)) throw new Error(`${label} profile must be circle, rectangle or oval.`);
      if (shape === "circle") {
        const diameterKey = key(profile.diameter_parameter, `${label}.profile.diameter_parameter`);
        if (hosted.some((connector) => connector.profile !== "round" || connector.diameter_parameter !== diameterKey)) throw new Error(`${label} round connector diameter_parameter must match its extrusion profile.`);
      } else if (shape === "rectangle") {
        const widthKey = key(profile.width_parameter, `${label}.profile.width_parameter`); const heightKey = key(profile.height_parameter, `${label}.profile.height_parameter`);
        if (hosted.some((connector) => connector.profile !== "rectangular" || connector.width_parameter !== widthKey || connector.height_parameter !== heightKey)) throw new Error(`${label} rectangular connector size parameters must match its extrusion profile.`);
      } else {
        const widthKey = key(profile.width_parameter, `${label}.profile.width_parameter`); const heightKey = key(profile.height_parameter, `${label}.profile.height_parameter`);
        if (hosted.some((connector) => connector.profile !== "oval" || connector.width_parameter !== widthKey || connector.height_parameter !== heightKey)) throw new Error(`${label} oval connector size parameters must match its extrusion profile.`);
      }
    };
    verifyProfile(run, runConnectors, `part_type ${partType} main run`);
    verifyProfile(branch, branchConnectors, `part_type ${partType} branch`);
  }

  // A Pants fitting is not accepted as an arbitrary three-port approximation.
  // The bounded compiler supports one X-axis inlet splitting symmetrically into
  // two forward XY outlets; other pants shapes remain an explicit future gap.
  if (normalizedPartType === "pants") {
    if (!["duct_fitting", "pipe_fitting"].includes(familyCategory)) throw new Error(`part_type ${partType} is supported only for Duct Fitting or Pipe Fitting.`);
    const solidParts = parts.filter((part) => String(part.operation ?? "solid") === "solid");
    if (parts.length !== 3 || solidParts.length !== 3) throw new Error(`part_type ${partType} requires exactly three solid extrusion bodies.`);
    if (solidParts.some((part) => part.primitive !== "extrusion")) throw new Error(`part_type ${partType} requires extrusion bodies in the bounded symmetric topology.`);
    const connectorsByPart = new Map<string, Json[]>();
    for (const connector of connectors) {
      const hostKey = String(connector.host_part); const hosted = connectorsByPart.get(hostKey) ?? [];
      hosted.push(connector); connectorsByPart.set(hostKey, hosted);
    }
    if (connectorsByPart.size !== 3 || [...connectorsByPart.values()].some((hosted) => hosted.length !== 1)) throw new Error(`part_type ${partType} requires one connector on each inlet/outlet body.`);
    const inletEntry = [...connectorsByPart.entries()].find(([hostKey]) => partsByKey.get(hostKey)?.axis === "x");
    if (!inletEntry) throw new Error(`part_type ${partType} requires one X-axis inlet body.`);
    const [inletKey, inletConnectors] = inletEntry; const inlet = partsByKey.get(inletKey)!; const inletConnector = inletConnectors[0];
    if (inlet.axis_direction !== undefined || Number(inlet.start_mm) >= 0 || Math.abs(Number(inlet.end_mm)) > 1e-6 || inletConnector.host_face !== "start" || inletConnector.primary !== true)
      throw new Error(`part_type ${partType} inlet must be a negative X-axis extrusion ending at the Family origin with one primary start-face connector.`);
    const branches = [...connectorsByPart.entries()].filter(([hostKey]) => hostKey !== inletKey).map(([hostKey, hosted]) => ({ key: hostKey, part: partsByKey.get(hostKey)!, connector: hosted[0] }));
    const requireDirectLengthType = (raw: unknown, label: string): string => {
      const parameterKey = key(raw, label); const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`${label} must reference a direct Length Type Parameter.`);
      return parameterKey;
    };
    requireDirectLengthType(inlet.depth_parameter, `part_type ${partType} inlet depth_parameter`);
    const directions: Vector3[] = [];
    for (const branch of branches) {
      const body = branch.part;
      if (body.axis !== undefined || body.axis_direction === undefined || Math.abs(Number(body.start_mm)) > 1e-6 || Number(body.end_mm) <= 0 || branch.connector.host_face !== "end")
        throw new Error(`part_type ${partType} outlet branches must begin at the Family origin, extend outward and use end-face connectors.`);
      const direction = body.axis_direction as Vector3;
      if (direction.x <= 1e-6 || Math.abs(direction.z) > 1e-6 || Math.abs(direction.y) <= 1e-6) throw new Error(`part_type ${partType} outlet branches must use forward non-collinear XY axis_direction vectors.`);
      const angle = Math.acos(Math.min(1, Math.abs(direction.x))) * 180 / Math.PI;
      if (angle < 15 - 1e-6 || angle > 75 + 1e-6) throw new Error(`part_type ${partType} outlet branches must form a 15-75 degree angle to the X-axis.`);
      requireDirectLengthType(body.depth_parameter, `part_type ${partType} outlet depth_parameter`);
      directions.push(direction);
      const joined = array(inlet.join_with ?? [], `part ${inletKey}.join_with`).map(String).includes(branch.key)
        || array(body.join_with ?? [], `part ${branch.key}.join_with`).map(String).includes(inletKey);
      if (!joined) throw new Error(`part_type ${partType} inlet and outlet bodies must declare join_with for every branch.`);
    }
    if (Math.abs(directions[0].x - directions[1].x) > 1e-6 || Math.abs(directions[0].y + directions[1].y) > 1e-6)
      throw new Error(`part_type ${partType} outlet axis_direction vectors must be symmetric about the X-axis.`);
    if (connectors.filter((connector) => connector.primary === true).length !== 1) throw new Error(`part_type ${partType} requires exactly one primary inlet connector.`);
    if (connectors.some((connector) => connector.linked_to !== undefined)) throw new Error(`part_type ${partType} connector linked_to is blocked until Revit multi-port link topology is runtime-certified; geometry and connector placement remain independently verifiable.`);
    const verifyProfile = (body: Json, connector: Json, label: string): void => {
      const profile = object(body.profile, `${label}.profile`); const shape = String(profile.shape);
      if (!["circle", "rectangle", "oval"].includes(shape)) throw new Error(`${label} profile must be circle, rectangle or oval.`);
      if (shape === "circle") {
        const diameter = key(profile.diameter_parameter, `${label}.profile.diameter_parameter`);
        if (connector.profile !== "round" || connector.diameter_parameter !== diameter) throw new Error(`${label} round connector diameter_parameter must match its extrusion profile.`);
      } else {
        const width = key(profile.width_parameter, `${label}.profile.width_parameter`); const height = key(profile.height_parameter, `${label}.profile.height_parameter`);
        const expected = shape === "rectangle" ? "rectangular" : "oval";
        if (connector.profile !== expected || connector.width_parameter !== width || connector.height_parameter !== height) throw new Error(`${label} ${expected} connector size parameters must match its extrusion profile.`);
      }
    };
    verifyProfile(inlet, inletConnector, `part_type ${partType} inlet`);
    for (const branch of branches) verifyProfile(branch.part, branch.connector, `part_type ${partType} outlet`);
  }

  const referencePlaneSubcategories = array(blueprint.reference_plane_subcategories ?? [], "blueprint.reference_plane_subcategories").map((item, index) => object(item, `reference_plane_subcategories[${index}]`));
  uniqueKeys(referencePlaneSubcategories, "blueprint.reference_plane_subcategories");
  const referencePlaneSubcategoriesByKey = new Map(referencePlaneSubcategories.map((item) => [String(item.key), item]));
  const referencePlaneSubcategoryNames = new Set<string>();
  for (const subcategory of referencePlaneSubcategories) {
    const subcategoryKey = String(subcategory.key);
    const name = string(subcategory.name, `reference plane subcategory ${subcategoryKey}.name`, 120);
    if (referencePlaneSubcategoryNames.has(name.toLowerCase())) throw new Error(`blueprint.reference_plane_subcategories contains duplicate name: ${name}.`);
    referencePlaneSubcategoryNames.add(name.toLowerCase());
    const color = object(subcategory.color_rgb, `reference plane subcategory ${subcategoryKey}.color_rgb`);
    for (const channel of ["r", "g", "b"]) {
      const value = finite(color[channel], `reference plane subcategory ${subcategoryKey}.color_rgb.${channel}`);
      if (!Number.isInteger(value) || value < 0 || value > 255) throw new Error(`reference plane subcategory ${subcategoryKey}.color_rgb.${channel} must be an integer from 0 to 255.`);
    }
    const lineWeight = finite(subcategory.projection_line_weight, `reference plane subcategory ${subcategoryKey}.projection_line_weight`);
    if (!Number.isInteger(lineWeight) || lineWeight < 1 || lineWeight > 16) throw new Error(`reference plane subcategory ${subcategoryKey}.projection_line_weight must be an integer from 1 to 16.`);
    if (subcategory.line_pattern_name !== undefined) string(subcategory.line_pattern_name, `reference plane subcategory ${subcategoryKey}.line_pattern_name`, 120);
  }

  const referencePlanes = array(blueprint.reference_planes ?? [], "blueprint.reference_planes").map((item, index) => object(item, `reference_planes[${index}]`));
  uniqueKeys(referencePlanes, "blueprint.reference_planes");
  const referencePlaneKeys = new Set(referencePlanes.map((item) => String(item.key)));
  const referencePlanesByKey = new Map(referencePlanes.map((item) => [String(item.key), item]));
  const referenceNames = new Set<string>();
  const stableReferenceTypes = new Set<string>(["left", "center_left_right", "right", "front", "center_front_back", "back", "bottom", "center_elevation", "top"]);
  const stableReferenceTypeOwners = new Map<string, string>();
  const legacyReferenceTypes: Record<string, string> = { not_reference: "not_reference", weak: "weak", strong: "strong" };
  const originPlanes: { key: string; normal: Point3 }[] = [];
  for (const reference of referencePlanes) {
    const referenceKey = String(reference.key);
    const name = string(reference.name, `reference plane ${referenceKey}.name`, 120);
    if (referenceNames.has(name.toLowerCase())) throw new Error(`blueprint.reference_planes contains duplicate name: ${name}.`);
    referenceNames.add(name.toLowerCase());
    const viewPlane = oneOf(reference.view_plane, ["xy", "xz", "yz"] as const, `reference plane ${referenceKey}.view_plane`);
    const bubble = point3(reference.bubble_end_mm, `reference plane ${referenceKey}.bubble_end_mm`);
    const free = point3(reference.free_end_mm, `reference plane ${referenceKey}.free_end_mm`);
    const cut = point3(reference.cut_vector, `reference plane ${referenceKey}.cut_vector`);
    if (distance3(bubble, free) < 1e-6) throw new Error(`reference plane ${referenceKey} requires distinct bubble/free endpoints.`);
    if (Math.abs(planeCoordinate(bubble, viewPlane) - planeCoordinate(free, viewPlane)) > 1e-6) throw new Error(`reference plane ${referenceKey} endpoints must lie in the declared ${viewPlane} view plane.`);
    const cutLength = Math.hypot(cut.x_mm, cut.y_mm, cut.z_mm);
    if (cutLength < 1e-6) throw new Error(`reference plane ${referenceKey}.cut_vector must be non-zero.`);
    const expectedCut = viewPlane === "xy" ? Math.abs(cut.z_mm) : viewPlane === "xz" ? Math.abs(cut.y_mm) : Math.abs(cut.x_mm);
    if (expectedCut / cutLength < 0.999999) throw new Error(`reference plane ${referenceKey}.cut_vector must be normal to its declared view_plane.`);
    const strength = reference.strength === undefined ? undefined : oneOf(reference.strength, ["not_reference", "weak", "strong"] as const, `reference plane ${referenceKey}.strength`);
    const referenceType = reference.reference_type === undefined ? undefined : oneOf(reference.reference_type, familyReferenceTypes, `reference plane ${referenceKey}.reference_type`);
    if (strength === undefined && referenceType === undefined) throw new Error(`reference plane ${referenceKey} must declare reference_type or legacy strength.`);
    const effectiveReferenceType = referenceType ?? legacyReferenceTypes[strength!];
    if (strength !== undefined && referenceType !== undefined && legacyReferenceTypes[strength] !== referenceType) throw new Error(`reference plane ${referenceKey}.strength conflicts with reference_type ${referenceType}.`);
    reference.reference_type = effectiveReferenceType;
    if (stableReferenceTypes.has(effectiveReferenceType)) {
      const owner = stableReferenceTypeOwners.get(effectiveReferenceType);
      if (owner) throw new Error(`reference plane ${referenceKey}.reference_type ${effectiveReferenceType} duplicates named reference on ${owner}.`);
      stableReferenceTypeOwners.set(effectiveReferenceType, referenceKey);
    }
    if (reference.defines_origin !== undefined && typeof reference.defines_origin !== "boolean") throw new Error(`reference plane ${referenceKey}.defines_origin must be boolean.`);
    reference.defines_origin = reference.defines_origin === true;
    if (reference.subcategory_key !== undefined) {
      const subcategoryKey = key(reference.subcategory_key, `reference plane ${referenceKey}.subcategory_key`);
      if (!referencePlaneSubcategoriesByKey.has(subcategoryKey)) throw new Error(`reference plane ${referenceKey}.subcategory_key references unknown reference plane subcategory ${subcategoryKey}.`);
    }
    if (reference.defines_origin === true) {
      const direction = { x_mm: free.x_mm - bubble.x_mm, y_mm: free.y_mm - bubble.y_mm, z_mm: free.z_mm - bubble.z_mm };
      originPlanes.push({ key: referenceKey, normal: {
        x_mm: direction.y_mm * cut.z_mm - direction.z_mm * cut.y_mm,
        y_mm: direction.z_mm * cut.x_mm - direction.x_mm * cut.z_mm,
        z_mm: direction.x_mm * cut.y_mm - direction.y_mm * cut.x_mm,
      } });
    }
  }
  if (originPlanes.length !== 0 && originPlanes.length !== 2) throw new Error(`blueprint.reference_planes must declare exactly two defines_origin planes when a custom insertion origin is requested; received ${originPlanes.length}.`);
  if (originPlanes.length === 2) {
    const [first, second] = originPlanes;
    const cross = {
      x_mm: first.normal.y_mm * second.normal.z_mm - first.normal.z_mm * second.normal.y_mm,
      y_mm: first.normal.z_mm * second.normal.x_mm - first.normal.x_mm * second.normal.z_mm,
      z_mm: first.normal.x_mm * second.normal.y_mm - first.normal.y_mm * second.normal.x_mm,
    };
    const firstLength = Math.hypot(first.normal.x_mm, first.normal.y_mm, first.normal.z_mm);
    const secondLength = Math.hypot(second.normal.x_mm, second.normal.y_mm, second.normal.z_mm);
    if (Math.hypot(cross.x_mm, cross.y_mm, cross.z_mm) / (firstLength * secondLength) < 1e-6) throw new Error(`defines_origin reference planes ${first.key} and ${second.key} must intersect and cannot have the same orientation.`);
  }

  const referenceLines = array(blueprint.reference_lines ?? [], "blueprint.reference_lines").map((item, index) => object(item, `reference_lines[${index}]`));
  uniqueKeys(referenceLines, "blueprint.reference_lines");
  const referenceLineKeys = new Set(referenceLines.map((item) => String(item.key)));
  const referenceLinesByKey = new Map(referenceLines.map((item) => [String(item.key), item]));
  for (const reference of referenceLines) {
    const referenceKey = String(reference.key);
    const plane = oneOf(reference.plane, ["xy", "xz", "yz"] as const, `reference line ${referenceKey}.plane`);
    const start = point3(reference.start_mm, `reference line ${referenceKey}.start_mm`);
    const end = point3(reference.end_mm, `reference line ${referenceKey}.end_mm`);
    if (distance3(start, end) < 1e-6) throw new Error(`reference line ${referenceKey} requires distinct endpoints.`);
    if (Math.abs(planeCoordinate(start, plane) - planeCoordinate(end, plane)) > 1e-6) throw new Error(`reference line ${referenceKey} endpoints must lie in the declared ${plane} plane.`);
  }
  const allReferenceKeys = new Set([...referencePlaneKeys, ...referenceLineKeys]);
  if (allReferenceKeys.size !== referencePlaneKeys.size + referenceLineKeys.size) throw new Error("Reference plane and reference line keys must be globally unique.");

  const dimensions = array(blueprint.dimensions ?? [], "blueprint.dimensions").map((item, index) => object(item, `dimensions[${index}]`));
  uniqueKeys(dimensions, "blueprint.dimensions");
  for (const dimension of dimensions) {
    const dimensionKey = String(dimension.key);
    const kind = oneOf(dimension.kind ?? "linear", ["linear", "radial", "angular"] as const, `dimension ${dimensionKey}.kind`);
    dimension.kind = kind;
    const viewPlane = oneOf(dimension.view_plane, ["xy", "xz", "yz"] as const, `dimension ${dimensionKey}.view_plane`);
    if (kind === "linear") {
      const references = array(dimension.reference_keys, `dimension ${dimensionKey}.reference_keys`).map((item, index) => key(item, `dimension ${dimensionKey}.reference_keys[${index}]`));
      if (references.length < 2 || new Set(references).size !== references.length) throw new Error(`dimension ${dimensionKey}.reference_keys must contain at least two unique references.`);
      for (const referenceKey of references) if (!allReferenceKeys.has(referenceKey)) throw new Error(`dimension ${dimensionKey} references unknown reference ${referenceKey}.`);
      const start = point3(dimension.line_start_mm, `dimension ${dimensionKey}.line_start_mm`);
      const end = point3(dimension.line_end_mm, `dimension ${dimensionKey}.line_end_mm`);
      if (distance3(start, end) < 1e-6) throw new Error(`dimension ${dimensionKey} requires a non-zero dimension line.`);
      if (Math.abs(planeCoordinate(start, viewPlane) - planeCoordinate(end, viewPlane)) > 1e-6) throw new Error(`dimension ${dimensionKey} line must lie in the declared ${viewPlane} view plane.`);
      if (dimension.equality !== undefined && typeof dimension.equality !== "boolean") throw new Error(`dimension ${dimensionKey}.equality must be boolean.`);
      const equality = dimension.equality === true;
      const hasParameter = dimension.parameter_key !== undefined;
      if (equality === hasParameter) throw new Error(`linear dimension ${dimensionKey} must declare exactly one parameter_key or equality=true.`);
      if (equality && references.length < 3) throw new Error(`equal dimension ${dimensionKey} requires at least three references.`);
      if (hasParameter) {
        const parameterKey = key(dimension.parameter_key, `dimension ${dimensionKey}.parameter_key`);
        const parameter = parametersByKey.get(parameterKey);
        if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`dimension ${dimensionKey}.parameter_key must reference a direct length Type Parameter.`);
      }
      for (const property of ["part_key", "profile_loop_index", "leader_point_mm"]) if (dimension[property] !== undefined) throw new Error(`linear dimension ${dimensionKey} cannot declare ${property}.`);
    } else if (kind === "radial") {
      for (const property of ["reference_keys", "line_start_mm", "line_end_mm", "equality"]) if (dimension[property] !== undefined) throw new Error(`radial dimension ${dimensionKey} cannot declare ${property}.`);
      if (viewPlane !== "yz") throw new Error(`radial dimension ${dimensionKey}.view_plane must be yz for the current X-axis extrusion compiler.`);
      const partKey = key(dimension.part_key, `dimension ${dimensionKey}.part_key`);
      const part = partsByKey.get(partKey);
      const profile = part ? object(part.profile, `part ${partKey}.profile`) : undefined;
      if (!part || part.primitive !== "extrusion" || !profile || !["circle", "ring"].includes(String(profile.shape))) throw new Error(`radial dimension ${dimensionKey}.part_key must reference a circular or ring extrusion.`);
      const loopIndex = dimension.profile_loop_index === undefined ? 0 : Number(dimension.profile_loop_index);
      if (!Number.isInteger(loopIndex) || loopIndex < 0 || loopIndex > (profile.shape === "ring" ? 1 : 0)) throw new Error(`radial dimension ${dimensionKey}.profile_loop_index is outside the declared profile loops.`);
      dimension.profile_loop_index = loopIndex;
      const leader = point3(dimension.leader_point_mm, `dimension ${dimensionKey}.leader_point_mm`);
      if (Math.abs(planeCoordinate(leader, viewPlane)) > 1e-6) throw new Error(`radial dimension ${dimensionKey}.leader_point_mm must lie in the extrusion profile plane.`);
      const parameterKey = key(dimension.parameter_key, `dimension ${dimensionKey}.parameter_key`);
      const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== "length" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`radial dimension ${dimensionKey}.parameter_key must reference a direct length Type Parameter.`);
    } else {
      for (const property of ["line_start_mm", "line_end_mm", "equality", "part_key", "profile_loop_index", "leader_point_mm"]) if (dimension[property] !== undefined) throw new Error(`angular dimension ${dimensionKey} cannot declare ${property}.`);
      const references = array(dimension.reference_keys, `dimension ${dimensionKey}.reference_keys`).map((item, index) => key(item, `dimension ${dimensionKey}.reference_keys[${index}]`));
      if (references.length !== 2 || new Set(references).size !== 2 || references.some((referenceKey) => !referenceLineKeys.has(referenceKey))) throw new Error(`angular dimension ${dimensionKey}.reference_keys must contain exactly two declared Reference Lines.`);
      for (const referenceKey of references) if (referenceLinesByKey.get(referenceKey)?.plane !== viewPlane) throw new Error(`angular dimension ${dimensionKey} Reference Lines must use its ${viewPlane} view_plane.`);
      const center = point3(dimension.arc_center_mm, `dimension ${dimensionKey}.arc_center_mm`);
      if (Math.abs(planeCoordinate(center, viewPlane)) > 1e-6) throw new Error(`angular dimension ${dimensionKey}.arc_center_mm must lie in the default family view plane.`);
      const radius = finite(dimension.arc_radius_mm, `dimension ${dimensionKey}.arc_radius_mm`);
      if (radius <= 0) throw new Error(`angular dimension ${dimensionKey}.arc_radius_mm must be greater than zero.`);
      const startAngle = finite(dimension.start_angle_degrees, `dimension ${dimensionKey}.start_angle_degrees`);
      const endAngle = finite(dimension.end_angle_degrees, `dimension ${dimensionKey}.end_angle_degrees`);
      if (endAngle <= startAngle || endAngle - startAngle >= 360) throw new Error(`angular dimension ${dimensionKey} angle span must be greater than zero and less than 360 degrees.`);
      const parameterKey = key(dimension.parameter_key, `dimension ${dimensionKey}.parameter_key`);
      const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== "angle" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`angular dimension ${dimensionKey}.parameter_key must reference a direct angle Type Parameter.`);
    }
  }

  const angularDrivenSweepParts = parts.filter((part) => part.primitive === "sweep" && (part.path as Json | undefined)?.kind === "reference_line");
  for (const part of angularDrivenSweepParts) {
    const partKey = String(part.key); const path = object(part.path, `part ${partKey}.path`);
    const referenceLineKey = key(path.reference_line_key, `part ${partKey}.path.reference_line_key`);
    const dimensionKey = key(path.angular_dimension_key, `part ${partKey}.path.angular_dimension_key`);
    const rotating = referenceLinesByKey.get(referenceLineKey);
    if (!rotating) throw new Error(`part ${partKey} reference_line path references unknown Reference Line ${referenceLineKey}.`);
    const dimension = dimensions.find((item) => String(item.key) === dimensionKey);
    if (!dimension || String(dimension.kind ?? "linear") !== "angular") throw new Error(`part ${partKey}.path.angular_dimension_key must reference a declared angular dimension.`);
    const referenceKeys = array(dimension.reference_keys, `dimension ${dimensionKey}.reference_keys`).map(String);
    if (referenceKeys[1] !== referenceLineKey) throw new Error(`part ${partKey} angular dimension must list its fixed baseline first and driven Reference Line ${referenceLineKey} second.`);
    const baseline = referenceLinesByKey.get(referenceKeys[0]);
    if (!baseline) throw new Error(`part ${partKey} angular dimension baseline is missing.`);
    const plane = String(rotating.plane); const baselineStart = point3(baseline.start_mm, `reference line ${referenceKeys[0]}.start_mm`); const baselineEnd = point3(baseline.end_mm, `reference line ${referenceKeys[0]}.end_mm`);
    const rotatingStart = point3(rotating.start_mm, `reference line ${referenceLineKey}.start_mm`); const rotatingEnd = point3(rotating.end_mm, `reference line ${referenceLineKey}.end_mm`); const center = point3(dimension.arc_center_mm, `dimension ${dimensionKey}.arc_center_mm`);
    if (distance3(baselineStart, rotatingStart) > 1e-6 || distance3(rotatingStart, center) > 1e-6) throw new Error(`part ${partKey} angular Reference Lines and arc_center_mm must share one pivot.`);
    const components = (start: Point3, end: Point3): [number, number] => plane === "xy" ? [end.x_mm - start.x_mm, end.y_mm - start.y_mm] : plane === "xz" ? [end.x_mm - start.x_mm, end.z_mm - start.z_mm] : [end.y_mm - start.y_mm, end.z_mm - start.z_mm];
    const [bu, bv] = components(baselineStart, baselineEnd); const [ru, rv] = components(rotatingStart, rotatingEnd); const bl = Math.hypot(bu, bv); const rl = Math.hypot(ru, rv);
    if (bl < 1e-6 || rl < 1e-6) throw new Error(`part ${partKey} angular Reference Lines must have non-zero length.`);
    if (Math.abs(bu / bl - 1) > 1e-6 || Math.abs(bv / bl) > 1e-6) throw new Error(`part ${partKey} angular fixed baseline must follow the positive primary axis of its ${plane} plane.`);
    const initialAngle = Math.atan2(bu * rv - bv * ru, bu * ru + bv * rv) * 180 / Math.PI;
    if (initialAngle <= 0 || initialAngle >= 180) throw new Error(`part ${partKey} driven Reference Line must start counterclockwise between 0 and 180 degrees from its fixed baseline.`);
    const declaredSpan = Number(dimension.end_angle_degrees) - Number(dimension.start_angle_degrees);
    if (Math.abs(initialAngle - declaredSpan) > .01) throw new Error(`part ${partKey} driven Reference Line angle must match angular dimension span at the nominal state.`);
    if (Math.abs(Number(dimension.start_angle_degrees)) > .01)
      throw new Error(`part ${partKey} angular sweep requires start_angle_degrees=0 so its driven Reference Line has one absolute angle contract.`);
  }

  const alignments = array(blueprint.alignments ?? [], "blueprint.alignments").map((item, index) => object(item, `alignments[${index}]`));
  uniqueKeys(alignments, "blueprint.alignments");
  for (const alignment of alignments) {
    const alignmentKey = String(alignment.key);
    oneOf(alignment.view_plane, ["xy", "xz", "yz"] as const, `alignment ${alignmentKey}.view_plane`);
    const referenceKey = key(alignment.reference_plane_key, `alignment ${alignmentKey}.reference_plane_key`);
    if (!referencePlaneKeys.has(referenceKey)) throw new Error(`alignment ${alignmentKey} references unknown reference plane ${referenceKey}.`);
    const partKey = key(alignment.part_key, `alignment ${alignmentKey}.part_key`);
    const part = partsByKey.get(partKey);
    if (!part || String(part.operation ?? "solid") !== "solid") throw new Error(`alignment ${alignmentKey} must reference a solid part.`);
    oneOf(alignment.part_face, ["start", "end", "positive_y", "negative_y", "positive_z", "negative_z"] as const, `alignment ${alignmentKey}.part_face`);
  }

  const arrays = array(blueprint.arrays ?? [], "blueprint.arrays").map((item, index) => object(item, `arrays[${index}]`));
  uniqueKeys(arrays, "blueprint.arrays");
  const connectorHostParts = new Set(connectors.map((connector) => String(connector.host_part)));
  const alignedParts = new Set(alignments.map((alignment) => String(alignment.part_key)));
  const geometryOperationParts = new Set<string>();
  for (const part of parts) {
    if (String(part.operation ?? "solid") !== "solid" || array(part.cut_targets ?? [], `part ${String(part.key)}.cut_targets`).length || array(part.join_with ?? [], `part ${String(part.key)}.join_with`).length) geometryOperationParts.add(String(part.key));
    for (const target of [...array(part.cut_targets ?? [], `part ${String(part.key)}.cut_targets`), ...array(part.join_with ?? [], `part ${String(part.key)}.join_with`)]) geometryOperationParts.add(String(target));
  }
  const arrayMemberParts = new Set<string>();
  const arrayNestedMembersByArray = new Map<string, string[]>();
  for (const arrayContract of arrays) {
    const arrayKey = String(arrayContract.key);
    const members = array(arrayContract.member_part_keys ?? [], `array ${arrayKey}.member_part_keys`).map((item, index) => key(item, `array ${arrayKey}.member_part_keys[${index}]`));
    const nestedMembers = array(arrayContract.member_nested_component_keys ?? [], `array ${arrayKey}.member_nested_component_keys`).map((item, index) => key(item, `array ${arrayKey}.member_nested_component_keys[${index}]`));
    if ((members.length > 0) === (nestedMembers.length > 0)) throw new Error(`array ${arrayKey} must declare exactly one non-empty member_part_keys or member_nested_component_keys list.`);
    if (new Set(members).size !== members.length || new Set(nestedMembers).size !== nestedMembers.length) throw new Error(`array ${arrayKey} member keys must be unique.`);
    for (const member of members) {
      if (!partKeys.has(member)) throw new Error(`array ${arrayKey} references unknown part ${member}.`);
      if (arrayMemberParts.has(member)) throw new Error(`part ${member} cannot belong to more than one array.`);
      if (geometryOperationParts.has(member) || connectorHostParts.has(member) || alignedParts.has(member)) throw new Error(`array ${arrayKey} member ${member} must be an independent solid part without join/cut, connector or alignment.`);
      arrayMemberParts.add(member);
    }
    if (nestedMembers.length) arrayNestedMembersByArray.set(arrayKey, nestedMembers);
    const viewPlane = oneOf(arrayContract.view_plane, ["xy", "xz", "yz"] as const, `array ${arrayKey}.view_plane`);
    const direction = point3(arrayContract.direction_mm, `array ${arrayKey}.direction_mm`);
    const directionLength = Math.hypot(direction.x_mm, direction.y_mm, direction.z_mm);
    if (directionLength < 1e-6) throw new Error(`array ${arrayKey}.direction_mm must be non-zero.`);
    if (Math.abs(planeCoordinate(direction, viewPlane)) > 1e-6) throw new Error(`array ${arrayKey}.direction_mm must lie in the declared ${viewPlane} view plane.`);
    oneOf(arrayContract.anchor, ["second", "last"] as const, `array ${arrayKey}.anchor`);
    const hasCount = arrayContract.count !== undefined; const hasCountParameter = arrayContract.count_parameter !== undefined;
    if (hasCount === hasCountParameter) throw new Error(`array ${arrayKey} must declare exactly one of count or count_parameter.`);
    if (hasCount && (!Number.isInteger(arrayContract.count) || Number(arrayContract.count) < 2)) throw new Error(`array ${arrayKey}.count must be an integer of at least 2.`);
    if (hasCountParameter) {
      const parameterKey = key(arrayContract.count_parameter, `array ${arrayKey}.count_parameter`);
      const parameter = parametersByKey.get(parameterKey);
      if (!parameter || parameter.data_type !== "integer" || parameter.scope !== "type" || parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`array ${arrayKey}.count_parameter must reference a direct Integer Type Parameter.`);
    }
  }

  const referencedParameterKeys = new Set<string>();
  const collectReferences = (contract: Json | undefined, properties: readonly string[]): void => {
    if (!contract) return;
    for (const property of properties) {
      if (contract[property] !== undefined) referencedParameterKeys.add(String(contract[property]));
    }
  };
  for (const part of parts) {
    collectReferences(part, ["depth_parameter"]);
    collectReferences(part.path as Json | undefined, ["radius_parameter", "lead_in_parameter", "lead_out_parameter", "lateral_offset_parameter", "offset_angle_parameter"]);
    collectReferences(part.profile as Json | undefined, ["width_parameter", "height_parameter", "diameter_parameter", "outer_diameter_parameter", "inner_diameter_parameter"]);
    collectReferences(part.end_profile as Json | undefined, ["width_parameter", "height_parameter", "diameter_parameter", "outer_diameter_parameter", "inner_diameter_parameter"]);
  }
  for (const connector of connectors) collectReferences(connector, ["diameter_parameter", "width_parameter", "height_parameter", "flow_parameter", "flow_factor_parameter", "voltage_parameter", "apparent_load_parameter", "number_of_poles_parameter", "power_factor_parameter", "balanced_load_parameter", "load_classification_parameter"]);
  for (const dimension of dimensions) collectReferences(dimension, ["parameter_key"]);
  for (const arrayContract of arrays) collectReferences(arrayContract, ["count_parameter"]);
  for (const parameterKey of coordinationZoneParameterKeys) referencedParameterKeys.add(parameterKey);

  const verification = object(blueprint.verification ?? {}, "blueprint.verification");
  const flexCases = array(verification.parameter_flex_cases ?? [], "blueprint.verification.parameter_flex_cases").map((item, index) => object(item, `parameter_flex_cases[${index}]`));
  uniqueKeys(flexCases.map((item) => ({ ...item, key: item.parameter_key })), "blueprint.verification.parameter_flex_cases");
  const flexCasesByKey = new Map<string, Json>();
  for (const flexCase of flexCases) {
    const parameterKey = key(flexCase.parameter_key, "parameter_flex_case.parameter_key");
    const parameter = parametersByKey.get(parameterKey);
    if (!parameter || !["length", "number", "integer", "angle", "airflow", "flow"].includes(String(parameter.data_type))) throw new Error(`parameter_flex_case ${parameterKey} must reference a numeric parameter.`);
    if (parameter.formula !== undefined || parameter.lookup !== undefined) throw new Error(`parameter_flex_case ${parameterKey} cannot directly flex a formula/lookup-driven parameter.`);
    if (parameter.scope !== "type") throw new Error(`parameter_flex_case ${parameterKey} currently requires a type parameter.`);
    const minimum = finite(flexCase.min, `parameter_flex_case ${parameterKey}.min`);
    const nominal = finite(flexCase.nominal, `parameter_flex_case ${parameterKey}.nominal`);
    const maximum = finite(flexCase.max, `parameter_flex_case ${parameterKey}.max`);
    if (!(minimum < nominal && nominal < maximum)) throw new Error(`parameter_flex_case ${parameterKey} must satisfy min < nominal < max.`);
    if (parameter.data_type === "length" && minimum <= 0) throw new Error(`parameter_flex_case ${parameterKey}.min must be greater than zero for length.`);
    if (parameter.data_type === "integer" && ![minimum, nominal, maximum].every(Number.isInteger)) throw new Error(`parameter_flex_case ${parameterKey} values must be integers.`);
    flexCasesByKey.set(parameterKey, flexCase);
  }
  for (const part of angularDrivenSweepParts) {
    const partKey = String(part.key); const path = object(part.path, `part ${partKey}.path`); const dimensionKey = String(path.angular_dimension_key);
    const dimension = dimensions.find((item) => String(item.key) === dimensionKey)!; const parameterKey = String(dimension.parameter_key); const flexCase = flexCasesByKey.get(parameterKey);
    if (!flexCase) throw new Error(`part ${partKey} angular driver ${parameterKey} requires verification.parameter_flex_cases min/nominal/max.`);
    const minimum = Number(flexCase.min); const nominal = Number(flexCase.nominal); const maximum = Number(flexCase.max);
    if (!(minimum > 0 && maximum < 180)) throw new Error(`part ${partKey} angular driver flex range must stay strictly between 0 and 180 degrees.`);
    const span = Number(dimension.end_angle_degrees) - Number(dimension.start_angle_degrees);
    if (Math.abs(span - nominal) > .01) throw new Error(`part ${partKey} angular dimension span must equal the approved nominal flex value.`);
  }
  for (const parameter of parameters) {
    const parameterKey = String(parameter.key);
    const referenced = referencedParameterKeys.has(parameterKey);
    if (referenced && parameter.formula === undefined && parameter.lookup === undefined && ["length", "number", "integer", "angle", "airflow", "flow"].includes(String(parameter.data_type)) && !flexCasesByKey.has(parameterKey)) throw new Error(`geometry/connector parameter ${parameterKey} requires verification.parameter_flex_cases min/nominal/max.`);
  }
  for (const parameterKey of coordinationZoneParameterKeys) if (!flexCasesByKey.has(parameterKey)) throw new Error(`coordination zone parameter ${parameterKey} requires verification.parameter_flex_cases min/nominal/max.`);
  for (const part of parts.filter((item) => item.primitive === "sweep" && (item.path as Json | undefined)?.kind === "offset")) {
    const path = object(part.path, `part ${String(part.key)}.path`);
    for (const property of ["lead_in_parameter", "lead_out_parameter", "lateral_offset_parameter", "offset_angle_parameter"] as const) {
      const parameterKey = String(path[property]); const flexCase = flexCasesByKey.get(parameterKey);
      if (!flexCase) throw new Error(`part ${String(part.key)} parameterized offset path ${property}=${parameterKey} requires verification.parameter_flex_cases min/nominal/max.`);
      if (property === "offset_angle_parameter" && (Number(flexCase.min) < 15 || Number(flexCase.max) > 75)) throw new Error(`part ${String(part.key)} offset-angle flex range must stay between 15 and 75 degrees.`);
    }
    const firstType = types[0];
    if (firstType) {
      const values = object(firstType.values ?? {}, `type ${String(firstType.name)}.values`);
      for (const property of ["lead_in_parameter", "lead_out_parameter", "lateral_offset_parameter", "offset_angle_parameter"] as const) {
        const parameterKey = String(path[property]); const nominal = Number(flexCasesByKey.get(parameterKey)?.nominal);
        const typeValue = values[parameterKey] ?? parametersByKey.get(parameterKey)?.default;
        if (typeValue === undefined || Math.abs(Number(typeValue) - nominal) > 1e-9) throw new Error(`part ${String(part.key)} first Family type value for ${parameterKey} must equal its approved nominal flex value ${nominal}.`);
      }
    }
  }
  for (const { profile, label } of ovalProfiles) {
    if (profile.width_parameter === undefined) continue;
    const widthKey = String(profile.width_parameter); const heightKey = String(profile.height_parameter);
    const majorKey = profile.major_axis === "width" ? widthKey : heightKey; const minorKey = profile.major_axis === "width" ? heightKey : widthKey;
    const majorFlex = flexCasesByKey.get(majorKey); const minorFlex = flexCasesByKey.get(minorKey);
    if (!majorFlex || !minorFlex) throw new Error(`${label} oval width/height parameters require approved flex cases.`);
    if (Number(majorFlex.min) <= Number(minorFlex.max)) throw new Error(`${label} oval major-axis flex minimum must be strictly greater than the minor-axis flex maximum so the flat-oval orientation cannot flip.`);
    for (const type of types) {
      const values = object(type.values ?? {}, `type ${String(type.name)}.values`);
      const value = (parameterKey: string): number => {
        const rawValue = values[parameterKey] ?? parametersByKey.get(parameterKey)?.default;
        if (rawValue === undefined) throw new Error(`${label} oval parameter ${parameterKey} requires a value/default for Family type ${String(type.name)}.`);
        return positive(rawValue, `${label} oval parameter ${parameterKey} in type ${String(type.name)}`);
      };
      if (value(majorKey) <= value(minorKey)) throw new Error(`${label} oval Family type ${String(type.name)} must keep its declared major dimension strictly greater than its minor dimension.`);
    }
  }
  for (const { profile, label } of ringProfiles) {
    if (profile.outer_diameter_parameter === undefined) continue;
    const outerKey = String(profile.outer_diameter_parameter); const innerKey = String(profile.inner_diameter_parameter);
    const outerFlex = flexCasesByKey.get(outerKey); const innerFlex = flexCasesByKey.get(innerKey);
    if (!outerFlex || !innerFlex) throw new Error(`${label} ring outer/inner diameter parameters require approved flex cases.`);
    if (Number(outerFlex.min) <= Number(innerFlex.max)) throw new Error(`${label} ring outer-diameter flex minimum must be strictly greater than the inner-diameter flex maximum so the loops cannot invert.`);
    for (const type of types) {
      const values = object(type.values ?? {}, `type ${String(type.name)}.values`);
      const value = (parameterKey: string): number => {
        const rawValue = values[parameterKey] ?? parametersByKey.get(parameterKey)?.default;
        if (rawValue === undefined) throw new Error(`${label} ring parameter ${parameterKey} requires a value/default for Family type ${String(type.name)}.`);
        return positive(rawValue, `${label} ring parameter ${parameterKey} in type ${String(type.name)}`);
      };
      if (value(outerKey) <= value(innerKey)) throw new Error(`${label} ring Family type ${String(type.name)} must keep outer diameter strictly greater than inner diameter.`);
    }
  }
  const maximumProfileEnvelope = (profile: Json, label: string): number => {
    const maximum = (literal: string, parameterProperty: string): number => {
      if (profile[parameterProperty] !== undefined) {
        const parameterKey = String(profile[parameterProperty]); const flexCase = flexCasesByKey.get(parameterKey);
        if (!flexCase) throw new Error(`${label}.${parameterProperty} requires an approved flex case before arc clearance can be verified.`);
        return Number(flexCase.max);
      }
      return positive(profile[literal], `${label}.${literal}`);
    };
    const shape = String(profile.shape);
    if (shape === "circle") return maximum("diameter_mm", "diameter_parameter") / 2;
    if (shape === "ring") return maximum("outer_diameter_mm", "outer_diameter_parameter") / 2;
    if (shape === "rectangle") return Math.hypot(maximum("width_mm", "width_parameter") / 2, maximum("height_mm", "height_parameter") / 2);
    if (shape === "oval") return Math.max(maximum("width_mm", "width_parameter"), maximum("height_mm", "height_parameter")) / 2;
    throw new Error(`${label} cannot be clearance-checked for an arc sweep.`);
  };
  for (const part of parts.filter((item) => item.primitive === "sweep" && (item.path as Json | undefined)?.kind === "arc")) {
    const path = object(part.path, `part ${String(part.key)}.path`); let minimumRadius: number;
    if (path.radius_parameter !== undefined) {
      const parameterKey = String(path.radius_parameter); const flexCase = flexCasesByKey.get(parameterKey);
      if (!flexCase) throw new Error(`part ${String(part.key)} arc radius_parameter ${parameterKey} requires verification.parameter_flex_cases min/nominal/max.`);
      minimumRadius = Number(flexCase.min);
    } else minimumRadius = Number(path.radius_mm);
    const envelope = maximumProfileEnvelope(object(part.profile, `part ${String(part.key)}.profile`), `part ${String(part.key)}.profile`);
    if (minimumRadius <= envelope + 1e-6) throw new Error(`part ${String(part.key)} minimum bend radius must be greater than the maximum profile envelope (${envelope.toFixed(3)} mm).`);
  }
  for (const part of parts.filter((item) => item.primitive === "revolution" && Object.keys(object(item.profile, `part ${String(item.key)}.profile`)).some((property) => property.endsWith("_parameter")))) {
    const profile = object(part.profile, `part ${String(part.key)}.profile`);
    const origin = point3(part.profile_origin_mm, `part ${String(part.key)}.profile_origin_mm`);
    const axisStart = point3(part.axis_start_mm, `part ${String(part.key)}.axis_start_mm`); const axisEnd = point3(part.axis_end_mm, `part ${String(part.key)}.axis_end_mm`);
    const axis = { x: axisEnd.x_mm - axisStart.x_mm, y: axisEnd.y_mm - axisStart.y_mm, z: axisEnd.z_mm - axisStart.z_mm };
    const offset = { x: origin.x_mm - axisStart.x_mm, y: origin.y_mm - axisStart.y_mm, z: origin.z_mm - axisStart.z_mm };
    const cross = { x: offset.y * axis.z - offset.z * axis.y, y: offset.z * axis.x - offset.x * axis.z, z: offset.x * axis.y - offset.y * axis.x };
    const centerDistance = Math.hypot(cross.x, cross.y, cross.z) / Math.hypot(axis.x, axis.y, axis.z);
    const envelope = maximumProfileEnvelope(profile, `part ${String(part.key)}.profile`);
    if (centerDistance <= envelope + 1e-6) throw new Error(`part ${String(part.key)} parameterized revolution profile touches or crosses its axis across the approved flex range; axis clearance ${centerDistance.toFixed(3)} mm must exceed maximum profile envelope ${envelope.toFixed(3)} mm.`);
    const firstTypeValues = object(types[0]?.values ?? {}, `type ${String(types[0]?.name ?? "first")}.values`);
    for (const property of Object.keys(profile).filter((name) => name.endsWith("_parameter"))) {
      const parameterKey = String(profile[property]); const flexCase = flexCasesByKey.get(parameterKey);
      if (!flexCase) throw new Error(`part ${String(part.key)} parameterized revolution profile ${parameterKey} requires verification.parameter_flex_cases min/nominal/max.`);
      const typeValue = firstTypeValues[parameterKey] ?? parametersByKey.get(parameterKey)?.default; const nominal = Number(flexCase.nominal);
      if (typeValue === undefined || Math.abs(Number(typeValue) - nominal) > 1e-9) throw new Error(`part ${String(part.key)} first Family type value for ${parameterKey} must equal its approved nominal flex value ${nominal}.`);
    }
  }

  const nested = array(blueprint.nested_components ?? [], "blueprint.nested_components").map((item, index) => object(item, `nested_components[${index}]`));
  uniqueKeys(nested, "blueprint.nested_components");
  for (const component of nested) if (coordinationZoneKeys.has(String(component.key))) throw new Error(`nested component ${String(component.key)} cannot reuse a coordination zone key.`);
  const interchangeableNestedKeys = new Set<string>();
  const hostedNestedKeys = new Set<string>();
  const nestedHostParts = new Set<string>();
  const validateLiteralHostFacePoints = (hostPart: Json, hostFace: string, points: Point3[], label: string): void => {
    if (hostPart.primitive !== "extrusion" || hostPart.axis !== "x" && hostPart.axis !== undefined) throw new Error(`${label} currently requires an axis-x extrusion host with a stable planar face.`);
    if (hostPart.depth_parameter !== undefined) return;
    const profile = object(hostPart.profile, `${label} host profile`);
    if (Object.keys(profile).some((property) => property.endsWith("_parameter"))) return;
    const start = finite(hostPart.start_mm, `${label} host start_mm`); const end = finite(hostPart.end_mm, `${label} host end_mm`);
    const tolerance = 1e-6;
    const within = (value: number, minimum: number, maximum: number): boolean => value >= minimum - tolerance && value <= maximum + tolerance;
    for (const point of points) {
      if (profile.shape === "rectangle") {
        const halfY = positive(profile.width_mm, `${label} host width_mm`) / 2; const halfZ = positive(profile.height_mm, `${label} host height_mm`) / 2;
        const onFace = hostFace === "start" ? Math.abs(point.x_mm - start) <= tolerance && within(point.y_mm, -halfY, halfY) && within(point.z_mm, -halfZ, halfZ)
          : hostFace === "end" ? Math.abs(point.x_mm - end) <= tolerance && within(point.y_mm, -halfY, halfY) && within(point.z_mm, -halfZ, halfZ)
            : hostFace === "positive_y" ? Math.abs(point.y_mm - halfY) <= tolerance && within(point.x_mm, start, end) && within(point.z_mm, -halfZ, halfZ)
              : hostFace === "negative_y" ? Math.abs(point.y_mm + halfY) <= tolerance && within(point.x_mm, start, end) && within(point.z_mm, -halfZ, halfZ)
                : hostFace === "positive_z" ? Math.abs(point.z_mm - halfZ) <= tolerance && within(point.x_mm, start, end) && within(point.y_mm, -halfY, halfY)
                  : Math.abs(point.z_mm + halfZ) <= tolerance && within(point.x_mm, start, end) && within(point.y_mm, -halfY, halfY);
        if (!onFace) throw new Error(`${label} point must lie inside the declared host face.`);
        continue;
      }
      if (profile.shape !== "circle" && profile.shape !== "ring") return;
      if (hostFace !== "start" && hostFace !== "end") throw new Error(`${label} circular/ring extrusion currently supports only start/end planar host faces.`);
      const expectedX = hostFace === "start" ? start : end;
      const outer = positive(profile.shape === "circle" ? profile.diameter_mm : profile.outer_diameter_mm, `${label} host diameter`) / 2;
      const inner = profile.shape === "ring" ? positive(profile.inner_diameter_mm, `${label} host inner_diameter_mm`) / 2 : 0;
      const radius = Math.hypot(point.y_mm, point.z_mm);
      if (Math.abs(point.x_mm - expectedX) > tolerance || radius > outer + tolerance || radius < inner - tolerance) throw new Error(`${label} point must lie inside the declared host face.`);
    }
  };
  for (const component of nested) {
    const componentKey = String(component.key);
    validateVisibilityParameter(component.visibility_parameter, `nested component ${componentKey}`);
    const placementMode = oneOf(component.placement_mode ?? "level_point", ["level_point", "host_face_point", "host_face_line"] as const, `nested component ${componentKey}.placement_mode`);
    const typeOptions = array(component.type_options ?? [], `nested component ${componentKey}.type_options`).map((item, index) => object(item, `nested component ${componentKey}.type_options[${index}]`));
    const interchangeable = typeOptions.length > 0 || component.family_type_parameter_key !== undefined;
    if (interchangeable) {
      if (placementMode !== "level_point") throw new Error(`nested component ${componentKey} cannot combine hosted placement with interchangeable type_options until that combination has runtime evidence.`);
      if (typeOptions.length < 2) throw new Error(`nested component ${componentKey}.type_options requires at least two Blueprint-built choices.`);
      uniqueKeys(typeOptions, `nested component ${componentKey}.type_options`);
      if (component.blueprint_id !== undefined || component.created_from_blueprint !== undefined || component.type_name !== undefined || component.sharing !== undefined)
        throw new Error(`nested component ${componentKey} cannot mix direct child fields with type_options.`);
      const parameterKey = key(component.family_type_parameter_key, `nested component ${componentKey}.family_type_parameter_key`);
      const familyTypeParameter = parametersByKey.get(parameterKey);
      if (!familyTypeParameter || familyTypeParameter.data_type !== "family_type" || familyTypeParameter.scope !== "type")
        throw new Error(`nested component ${componentKey}.family_type_parameter_key must reference a family_type Type Parameter.`);
      const optionKeys = new Set<string>();
      for (const option of typeOptions) {
        const optionKey = key(option.key, `nested component ${componentKey} option key`); optionKeys.add(optionKey);
        string(option.blueprint_id, `nested component ${componentKey} option ${optionKey}.blueprint_id`, 100);
        if (option.created_from_blueprint !== true) throw new Error(`nested component ${componentKey} option ${optionKey} must declare created_from_blueprint=true; reference RFA cloning is not allowed.`);
        string(option.type_name, `nested component ${componentKey} option ${optionKey}.type_name`, 120);
        if (option.sharing !== undefined) oneOf(option.sharing, ["embedded", "shared"] as const, `nested component ${componentKey} option ${optionKey}.sharing`);
      }
      if (Object.keys(object(component.parameter_map ?? {}, `nested component ${componentKey}.parameter_map`)).length)
        throw new Error(`nested component ${componentKey} cannot combine interchangeable type_options with child Instance Parameter mapping in this bounded compiler wave.`);
      for (const type of types) {
        const selected = key(object(type.values ?? {}, `type ${String(type.name)}.values`)[parameterKey], `type ${String(type.name)}.values.${parameterKey}`);
        if (!optionKeys.has(selected)) throw new Error(`type ${String(type.name)}.values.${parameterKey} must reference a declared nested option key.`);
      }
      interchangeableNestedKeys.add(componentKey);
    } else {
      string(component.blueprint_id, `nested component ${componentKey}.blueprint_id`, 100);
      if (component.created_from_blueprint !== true) throw new Error(`nested component ${componentKey} must declare created_from_blueprint=true; reference RFA cloning is not allowed.`);
      string(component.type_name, `nested component ${componentKey}.type_name`, 120);
      if (component.sharing !== undefined) oneOf(component.sharing, ["embedded", "shared"] as const, `nested component ${componentKey}.sharing`);
    }
    if (placementMode === "level_point") {
      point3(component.placement_point_mm, `nested component ${componentKey}.placement_point_mm`);
      oneOf(component.rotation_axis, ["x", "y", "z"] as const, `nested component ${componentKey}.rotation_axis`);
      finite(component.rotation_degrees, `nested component ${componentKey}.rotation_degrees`);
      for (const property of ["host_part_key", "host_face", "reference_direction", "placement_line_start_mm", "placement_line_end_mm"]) if (component[property] !== undefined) throw new Error(`nested component ${componentKey}.${property} is valid only for hosted placement.`);
    } else {
      hostedNestedKeys.add(componentKey);
      if (component.rotation_axis !== undefined || component.rotation_degrees !== undefined) throw new Error(`nested component ${componentKey} hosted placement cannot declare free rotation until face-local rotation is runtime-proven.`);
      const hostPartKey = key(component.host_part_key, `nested component ${componentKey}.host_part_key`); const hostPart = partsByKey.get(hostPartKey);
      if (!hostPart || String(hostPart.operation ?? "solid") !== "solid") throw new Error(`nested component ${componentKey} must reference an existing solid host part.`);
      if (geometryOperationParts.has(hostPartKey) || arrayMemberParts.has(hostPartKey)) throw new Error(`nested component ${componentKey} host part ${hostPartKey} must be independent of join/cut and array operations.`);
      nestedHostParts.add(hostPartKey);
      const hostFace = oneOf(component.host_face, ["start", "end", "positive_y", "negative_y", "positive_z", "negative_z"] as const, `nested component ${componentKey}.host_face`);
      if (placementMode === "host_face_point") {
        const point = point3(component.placement_point_mm, `nested component ${componentKey}.placement_point_mm`);
        const direction = point3(component.reference_direction, `nested component ${componentKey}.reference_direction`);
        const length = Math.hypot(direction.x_mm, direction.y_mm, direction.z_mm);
        if (length < 1e-6) throw new Error(`nested component ${componentKey}.reference_direction must be non-zero.`);
        const normal = hostFace === "start" ? [-1, 0, 0] : hostFace === "end" ? [1, 0, 0] : hostFace === "positive_y" ? [0, 1, 0] : hostFace === "negative_y" ? [0, -1, 0] : hostFace === "positive_z" ? [0, 0, 1] : [0, 0, -1];
        const dot = direction.x_mm * normal[0] + direction.y_mm * normal[1] + direction.z_mm * normal[2];
        if (Math.abs(dot) >= length * (1 - 1e-9)) throw new Error(`nested component ${componentKey}.reference_direction cannot be parallel to the host face normal.`);
        if (component.placement_line_start_mm !== undefined || component.placement_line_end_mm !== undefined) throw new Error(`nested component ${componentKey} host_face_point cannot declare a placement line.`);
        validateLiteralHostFacePoints(hostPart, hostFace, [point], `nested component ${componentKey}`);
      } else {
        const start = point3(component.placement_line_start_mm, `nested component ${componentKey}.placement_line_start_mm`); const end = point3(component.placement_line_end_mm, `nested component ${componentKey}.placement_line_end_mm`);
        if (distance3(start, end) < 1e-6) throw new Error(`nested component ${componentKey} placement line must have non-zero length.`);
        if (component.placement_point_mm !== undefined || component.reference_direction !== undefined) throw new Error(`nested component ${componentKey} host_face_line cannot declare point-placement fields.`);
        const samples = Array.from({ length: 9 }, (_, index) => { const ratio = index / 8; return { x_mm: start.x_mm + (end.x_mm - start.x_mm) * ratio, y_mm: start.y_mm + (end.y_mm - start.y_mm) * ratio, z_mm: start.z_mm + (end.z_mm - start.z_mm) * ratio }; });
        validateLiteralHostFacePoints(hostPart, hostFace, samples, `nested component ${componentKey}`);
      }
    }
    const parameterMap = object(component.parameter_map ?? {}, `nested component ${componentKey}.parameter_map`);
    const sharedParameterMapMode = component.shared_parameter_map_mode === undefined
      ? undefined
      : oneOf(component.shared_parameter_map_mode, ["identity_only"] as const, `nested component ${componentKey}.shared_parameter_map_mode`);
    if (sharedParameterMapMode !== undefined && component.sharing !== "shared")
      throw new Error(`nested component ${componentKey}.shared_parameter_map_mode is valid only when sharing is shared.`);
    if (component.sharing === "shared") component.shared_parameter_map_mode = sharedParameterMapMode ?? "identity_only";
    for (const [childKey, parentValue] of Object.entries(parameterMap)) {
      key(childKey, `nested component ${componentKey}.parameter_map child key`);
      const parentKey = key(parentValue, `nested component ${componentKey}.parameter_map.${childKey}`);
      const parentParameter = parametersByKey.get(parentKey);
      if (!parentParameter) throw new Error(`nested component ${componentKey}.parameter_map.${childKey} references unknown parent parameter ${parentKey}.`);
      const sharedIdentityMap = component.sharing === "shared";
      if (sharedIdentityMap) {
        // A matching ExternalDefinition GUID is an identity/scheduling contract only.
        // It is deliberately not treated as a Revit parent-to-child value association:
        // the Project probe must establish any propagation pattern independently.
        if (component.shared_parameter_map_mode !== "identity_only")
          throw new Error(`nested shared component ${componentKey}.parameter_map.${childKey} supports only identity_only mapping until a value-propagation architecture has Project runtime evidence.`);
        if (parentParameter.scope !== "instance" || typeof parentParameter.shared_guid !== "string")
          throw new Error(`nested shared component ${componentKey}.parameter_map.${childKey} requires a parent Instance Parameter with shared_guid.`);
        if (parentParameter.formula !== undefined || parentParameter.lookup !== undefined)
          throw new Error(`nested shared component ${componentKey}.parameter_map.${childKey} cannot use a formula or lookup parent parameter.`);
      } else if (["length", "number", "integer", "angle"].includes(String(parentParameter.data_type)) && parentParameter.formula === undefined && parentParameter.lookup === undefined) {
        if (parentParameter.scope !== "type") throw new Error(`nested numeric parent parameter ${parentKey} currently requires type scope for approved flex verification.`);
        if (!flexCasesByKey.has(parentKey)) throw new Error(`geometry/connector/nested parameter ${parentKey} requires verification.parameter_flex_cases min/nominal/max.`);
      }
    }
  }
  const mirrors = array(blueprint.mirrors ?? [], "blueprint.mirrors").map((item, index) => object(item, `mirrors[${index}]`));
  uniqueKeys(mirrors, "blueprint.mirrors");
  const nestedKeys = new Set(nested.map((item) => String(item.key)));
  const arrayMemberNested = new Set<string>();
  for (const [arrayKey, members] of arrayNestedMembersByArray) for (const member of members) {
    if (!nestedKeys.has(member)) throw new Error(`array ${arrayKey} references unknown nested component ${member}.`);
    if (interchangeableNestedKeys.has(member)) throw new Error(`array ${arrayKey} cannot contain interchangeable nested component ${member} until swap/array runtime is supported.`);
    if (hostedNestedKeys.has(member)) throw new Error(`array ${arrayKey} cannot contain hosted nested component ${member} until hosted array runtime is supported.`);
    if (arrayMemberNested.has(member)) throw new Error(`nested component ${member} cannot belong to more than one array.`);
    arrayMemberNested.add(member);
  }
  const mirroredTargets = new Set<string>();
  for (const mirror of mirrors) {
    const mirrorKey = String(mirror.key); const targetKind = oneOf(mirror.target_kind, ["part", "nested_component"] as const, `mirror ${mirrorKey}.target_kind`);
    const targets = array(mirror.target_keys, `mirror ${mirrorKey}.target_keys`).map((item, index) => key(item, `mirror ${mirrorKey}.target_keys[${index}]`));
    if (targets.length < 1 || new Set(targets).size !== targets.length) throw new Error(`mirror ${mirrorKey}.target_keys must contain at least one unique target.`);
    for (const target of targets) {
      const identity = `${targetKind}:${target}`;
      if (mirroredTargets.has(identity)) throw new Error(`mirror target ${identity} cannot be declared more than once.`);
      mirroredTargets.add(identity);
      if (targetKind === "part") {
        if (!partKeys.has(target)) throw new Error(`mirror ${mirrorKey} references unknown part ${target}.`);
        if (nestedHostParts.has(target)) throw new Error(`mirror ${mirrorKey} cannot target part ${target} because it hosts a nested component.`);
        if (geometryOperationParts.has(target) || connectorHostParts.has(target) || alignedParts.has(target) || arrayMemberParts.has(target)) throw new Error(`mirror ${mirrorKey} part ${target} must be independent of join/cut, connector, alignment and array operations.`);
      } else if (!nestedKeys.has(target)) throw new Error(`mirror ${mirrorKey} references unknown nested component ${target}.`);
      else if (interchangeableNestedKeys.has(target)) throw new Error(`mirror ${mirrorKey} cannot target interchangeable nested component ${target} until constraint-aware swap/mirror runtime is supported.`);
      else if (hostedNestedKeys.has(target)) throw new Error(`mirror ${mirrorKey} cannot target hosted nested component ${target} until face-local mirror runtime is supported.`);
      else if (arrayMemberNested.has(target)) throw new Error(`mirror ${mirrorKey} cannot target nested component ${target} because it belongs to an array.`);
    }
    point3(mirror.plane_origin_mm, `mirror ${mirrorKey}.plane_origin_mm`);
    const normal = point3(mirror.plane_normal, `mirror ${mirrorKey}.plane_normal`);
    if (Math.hypot(normal.x_mm, normal.y_mm, normal.z_mm) < 1e-6) throw new Error(`mirror ${mirrorKey}.plane_normal must be non-zero.`);
    if (typeof mirror.copy !== "boolean") throw new Error(`mirror ${mirrorKey}.copy must be boolean.`);
  }

  const symbolic = array(blueprint.symbolic_lines ?? [], "blueprint.symbolic_lines").map((item, index) => object(item, `symbolic_lines[${index}]`));
  uniqueKeys(symbolic, "blueprint.symbolic_lines");
  for (const line of symbolic) {
    string(line.role, `symbolic line ${String(line.key)}.role`, 100);
    validatePresentationSubcategoryKey(line.subcategory_key, `symbolic line ${String(line.key)}`);
    const path = validatePath({ kind: "polyline", plane: line.plane, points_mm: line.points_mm }, `symbolic line ${String(line.key)}`);
    if (path.points.length < 2) throw new Error(`symbolic line ${String(line.key)} requires at least two points.`);
    validateVisibilityParameter(line.visibility_parameter, `symbolic line ${String(line.key)}`);
    const visibility = object(line.visibility ?? { coarse: true, medium: true, fine: true }, `symbolic line ${String(line.key)}.visibility`);
    for (const level of ["coarse", "medium", "fine"]) if (typeof visibility[level] !== "boolean") throw new Error(`symbolic line ${String(line.key)}.visibility.${level} must be boolean.`);
    for (const direction of ["front_back", "left_right", "plan_rcp", "only_when_cut"]) if (visibility[direction] !== undefined && typeof visibility[direction] !== "boolean") throw new Error(`symbolic line ${String(line.key)}.visibility.${direction} must be boolean.`);
  }
  const modelLines = array(blueprint.model_lines ?? [], "blueprint.model_lines").map((item, index) => object(item, `model_lines[${index}]`));
  uniqueKeys(modelLines, "blueprint.model_lines");
  for (const line of modelLines) {
    const lineKey = String(line.key);
    string(line.role, `model line ${lineKey}.role`, 100);
    validatePresentationSubcategoryKey(line.subcategory_key, `model line ${lineKey}`);
    const path = validatePath({ kind: "polyline", plane: line.plane, points_mm: line.points_mm }, `model line ${lineKey}`);
    if (path.points.length < 2) throw new Error(`model line ${lineKey} requires at least two points.`);
    const endpointBindings = array(line.endpoint_bindings ?? [], `model line ${lineKey}.endpoint_bindings`).map((item, index) => object(item, `model line ${lineKey}.endpoint_bindings[${index}]`));
    const boundEndpoints = new Set<string>();
    for (const binding of endpointBindings) {
      const endpoint = oneOf(binding.endpoint, ["start", "end"] as const, `model line ${lineKey} endpoint binding.endpoint`);
      if (boundEndpoints.has(endpoint)) throw new Error(`model line ${lineKey} endpoint_bindings duplicates ${endpoint}.`);
      boundEndpoints.add(endpoint);
      const referencePlaneKey = key(binding.reference_plane_key, `model line ${lineKey} endpoint binding.reference_plane_key`);
      const referencePlane = referencePlanesByKey.get(referencePlaneKey);
      if (!referencePlane) throw new Error(`model line ${lineKey} endpoint binding references unknown Reference Plane ${referencePlaneKey}.`);
      if (String(referencePlane.view_plane) !== String(line.plane)) throw new Error(`model line ${lineKey} endpoint binding Reference Plane ${referencePlaneKey} must use the same ${String(line.plane)} view_plane.`);
      const literalEndpoint = endpoint === "start" ? path.points[0] : path.points[path.points.length - 1];
      const bubble = point3(referencePlane.bubble_end_mm, `reference plane ${referencePlaneKey}.bubble_end_mm`);
      const free = point3(referencePlane.free_end_mm, `reference plane ${referencePlaneKey}.free_end_mm`);
      const cut = point3(referencePlane.cut_vector, `reference plane ${referencePlaneKey}.cut_vector`);
      const direction = { x_mm: free.x_mm - bubble.x_mm, y_mm: free.y_mm - bubble.y_mm, z_mm: free.z_mm - bubble.z_mm };
      const normal = {
        x_mm: direction.y_mm * cut.z_mm - direction.z_mm * cut.y_mm,
        y_mm: direction.z_mm * cut.x_mm - direction.x_mm * cut.z_mm,
        z_mm: direction.x_mm * cut.y_mm - direction.y_mm * cut.x_mm,
      };
      const normalLength = Math.hypot(normal.x_mm, normal.y_mm, normal.z_mm);
      const offset = (literalEndpoint.x_mm - bubble.x_mm) * normal.x_mm + (literalEndpoint.y_mm - bubble.y_mm) * normal.y_mm + (literalEndpoint.z_mm - bubble.z_mm) * normal.z_mm;
      if (normalLength < 1e-6 || Math.abs(offset) / normalLength > 1e-6) throw new Error(`model line ${lineKey} ${endpoint} endpoint must lie on Reference Plane ${referencePlaneKey}.`);
    }
    validateVisibilityParameter(line.visibility_parameter, `model line ${lineKey}`);
    const visibility = object(line.visibility ?? { coarse: true, medium: true, fine: true }, `model line ${lineKey}.visibility`);
    for (const level of ["coarse", "medium", "fine"]) if (typeof visibility[level] !== "boolean") throw new Error(`model line ${String(line.key)}.visibility.${level} must be boolean.`);
    for (const direction of ["front_back", "left_right", "plan_rcp", "only_when_cut"]) if (visibility[direction] !== undefined && typeof visibility[direction] !== "boolean") throw new Error(`model line ${String(line.key)}.visibility.${direction} must be boolean.`);
  }
  const detailLevelRepresentations = array(blueprint.detail_level_representations ?? [], "blueprint.detail_level_representations").map((item, index) => object(item, "detail_level_representations[" + index + "]"));
  uniqueKeys(detailLevelRepresentations, "blueprint.detail_level_representations");
  const symbolicByKey = new Map(symbolic.map((line) => [String(line.key), line]));
  const modelLinesByKey = new Map(modelLines.map((line) => [String(line.key), line]));
  const representedParts = new Set<string>(); const representedSymbolicLines = new Set<string>(); const representedModelLines = new Set<string>();
  const representedPartRoles = new Set<string>(); const representedSymbolicRoles = new Set<string>(); const representedModelRoles = new Set<string>();
  const visibilityMatches = (candidate: Json, coarse: boolean, medium: boolean, fine: boolean): boolean => candidate.coarse === coarse && candidate.medium === medium && candidate.fine === fine;
  for (const representation of detailLevelRepresentations) {
    const representationKey = String(representation.key);
    oneOf(representation.policy, familyDetailLevelRepresentationPolicies, "detail_level_representation " + representationKey + ".policy");
    if (["detail_item", "annotation", "profile", "tag", "adaptive"].includes(String(family.template_behavior)))
      throw new Error("detail_level_representation " + representationKey + " requires a model Family template with physical 3D forms.");
    const physicalPartKeys = array(representation.physical_part_keys, "detail_level_representation " + representationKey + ".physical_part_keys").map((item, index) => key(item, "detail_level_representation " + representationKey + ".physical_part_keys[" + index + "]"));
    const symbolicLineKeys = array(representation.symbolic_line_keys ?? [], "detail_level_representation " + representationKey + ".symbolic_line_keys").map((item, index) => key(item, "detail_level_representation " + representationKey + ".symbolic_line_keys[" + index + "]"));
    const modelLineKeys = array(representation.model_line_keys ?? [], "detail_level_representation " + representationKey + ".model_line_keys").map((item, index) => key(item, "detail_level_representation " + representationKey + ".model_line_keys[" + index + "]"));
    if (!physicalPartKeys.length) throw new Error("detail_level_representation " + representationKey + " requires at least one physical_part_key.");
    if (!symbolicLineKeys.length && !modelLineKeys.length) throw new Error("detail_level_representation " + representationKey + " requires at least one symbolic_line_key or model_line_key.");
    for (const [kind, keys] of [["physical_part", physicalPartKeys], ["symbolic_line", symbolicLineKeys], ["model_line", modelLineKeys]] as const)
      if (new Set(keys).size !== keys.length) throw new Error("detail_level_representation " + representationKey + "." + kind + "_keys must be unique.");
    for (const partKey of physicalPartKeys) {
      const part = partsByKey.get(partKey);
      if (!part) throw new Error("detail_level_representation " + representationKey + " references unknown physical part " + partKey + ".");
      if (String(part.operation ?? "solid") !== "solid") throw new Error("detail_level_representation " + representationKey + " physical part " + partKey + " must be a solid form.");
      if (representedParts.has(partKey)) throw new Error("physical part " + partKey + " cannot belong to more than one detail_level_representation.");
      representedParts.add(partKey);
      const role = String(part.role);
      if (representedPartRoles.has(role)) throw new Error("detail_level_representation physical part roles must be unique for reopen verification: " + role + ".");
      representedPartRoles.add(role);
      if (!visibilityMatches(object(part.visibility ?? {}, "part " + partKey + ".visibility"), false, false, true))
        throw new Error("detail_level_representation " + representationKey + " physical part " + partKey + " must be visible only at Fine detail level.");
    }
    for (const [kind, keys, lookup, seen, roles] of [["symbolic", symbolicLineKeys, symbolicByKey, representedSymbolicLines, representedSymbolicRoles], ["model", modelLineKeys, modelLinesByKey, representedModelLines, representedModelRoles]] as const) {
      for (const lineKey of keys) {
        const line = lookup.get(lineKey);
        if (!line) throw new Error("detail_level_representation " + representationKey + " references unknown " + kind + " line " + lineKey + ".");
        if (seen.has(lineKey)) throw new Error(kind + " line " + lineKey + " cannot belong to more than one detail_level_representation.");
        seen.add(lineKey);
        const role = String(line.role);
        if (roles.has(role)) throw new Error("detail_level_representation " + kind + " line roles must be unique for reopen verification: " + role + ".");
        roles.add(role);
        if (!visibilityMatches(object(line.visibility ?? {}, kind + " line " + lineKey + ".visibility"), true, true, false))
          throw new Error("detail_level_representation " + representationKey + " " + kind + " line " + lineKey + " must be visible at Coarse/Medium and hidden at Fine.");
      }
    }
  }
  blueprint.detail_level_representations = detailLevelRepresentations;
  const controls = array(blueprint.controls ?? [], "blueprint.controls").map((item, index) => object(item, `controls[${index}]`));
  uniqueKeys(controls, "blueprint.controls");
  for (const control of controls) {
    oneOf(control.shape, ["vertical_arrow", "horizontal_arrow", "double_vertical_arrow", "double_horizontal_arrow"] as const, `control ${String(control.key)}.shape`);
    oneOf(control.view_plane, ["xy", "xz", "yz"] as const, `control ${String(control.key)}.view_plane`);
    point3(control.position_mm, `control ${String(control.key)}.position_mm`);
  }
  const detailLines = array(blueprint.detail_lines ?? [], "blueprint.detail_lines").map((item, index) => object(item, `detail_lines[${index}]`));
  uniqueKeys(detailLines, "blueprint.detail_lines");
  for (const line of detailLines) {
    const lineKey = String(line.key);
    string(line.role, `detail line ${lineKey}.role`, 100);
    validatePresentationSubcategoryKey(line.subcategory_key, `detail line ${lineKey}`);
    const path = validatePath({ kind: "polyline", plane: line.plane, points_mm: line.points_mm }, `detail line ${lineKey}`);
    const endpointBindings = array(line.endpoint_bindings ?? [], `detail line ${lineKey}.endpoint_bindings`).map((item, index) => object(item, `detail line ${lineKey}.endpoint_bindings[${index}]`));
    if (endpointBindings.length && String(family.template_behavior) !== "detail_item") throw new Error(`detail line ${lineKey} endpoint_bindings require template_behavior=detail_item.`);
    const boundEndpoints = new Set<string>();
    for (const binding of endpointBindings) {
      const endpoint = oneOf(binding.endpoint, ["start", "end"] as const, `detail line ${lineKey} endpoint binding.endpoint`);
      if (boundEndpoints.has(endpoint)) throw new Error(`detail line ${lineKey} endpoint_bindings duplicates ${endpoint}.`);
      boundEndpoints.add(endpoint);
      const referencePlaneKey = key(binding.reference_plane_key, `detail line ${lineKey} endpoint binding.reference_plane_key`);
      const referencePlane = referencePlanesByKey.get(referencePlaneKey);
      if (!referencePlane) throw new Error(`detail line ${lineKey} endpoint binding references unknown Reference Plane ${referencePlaneKey}.`);
      if (String(referencePlane.view_plane) !== String(line.plane)) throw new Error(`detail line ${lineKey} endpoint binding Reference Plane ${referencePlaneKey} must use the same ${String(line.plane)} view_plane.`);
      const literalEndpoint = endpoint === "start" ? path.points[0] : path.points[path.points.length - 1];
      const bubble = point3(referencePlane.bubble_end_mm, `reference plane ${referencePlaneKey}.bubble_end_mm`);
      const free = point3(referencePlane.free_end_mm, `reference plane ${referencePlaneKey}.free_end_mm`);
      const cut = point3(referencePlane.cut_vector, `reference plane ${referencePlaneKey}.cut_vector`);
      const direction = { x_mm: free.x_mm - bubble.x_mm, y_mm: free.y_mm - bubble.y_mm, z_mm: free.z_mm - bubble.z_mm };
      const normal = {
        x_mm: direction.y_mm * cut.z_mm - direction.z_mm * cut.y_mm,
        y_mm: direction.z_mm * cut.x_mm - direction.x_mm * cut.z_mm,
        z_mm: direction.x_mm * cut.y_mm - direction.y_mm * cut.x_mm,
      };
      const normalLength = Math.hypot(normal.x_mm, normal.y_mm, normal.z_mm);
      const offset = (literalEndpoint.x_mm - bubble.x_mm) * normal.x_mm + (literalEndpoint.y_mm - bubble.y_mm) * normal.y_mm + (literalEndpoint.z_mm - bubble.z_mm) * normal.z_mm;
      if (normalLength < 1e-6 || Math.abs(offset) / normalLength > 1e-6) throw new Error(`detail line ${lineKey} ${endpoint} endpoint must lie on Reference Plane ${referencePlaneKey}.`);
    }
  }
  const filledRegions = array(blueprint.filled_regions ?? [], "blueprint.filled_regions").map((item, index) => object(item, `filled_regions[${index}]`));
  uniqueKeys(filledRegions, "blueprint.filled_regions");
  for (const region of filledRegions) {
    string(region.role, `filled region ${String(region.key)}.role`, 100);
    string(region.type_name, `filled region ${String(region.key)}.type_name`, 120);
    const plane = oneOf(region.plane, ["xy", "xz", "yz"] as const, `filled region ${String(region.key)}.plane`);
    const loops = array(region.boundary_loops, `filled region ${String(region.key)}.boundary_loops`).map((item, index) => object(item, `filled region ${String(region.key)}.boundary_loops[${index}]`));
    if (loops.length === 0) throw new Error(`filled region ${String(region.key)} requires at least one boundary loop.`);
    let planeOffset: number | undefined;
    for (const [index, loop] of loops.entries()) {
      const points = array(loop.points_mm, `filled region ${String(region.key)} loop ${index}.points_mm`).map((item, pointIndex) => point3(item, `filled region ${String(region.key)} loop ${index}.points_mm[${pointIndex}]`));
      validateSimpleClosedLoop(points, plane, `filled region ${String(region.key)} loop ${index}`);
      const currentOffset = planeCoordinate(points[0], plane);
      if (points.some((point) => Math.abs(planeCoordinate(point, plane) - currentOffset) > 1e-6)) throw new Error(`filled region ${String(region.key)} loop ${index} must lie in the declared ${plane} plane.`);
      if (planeOffset === undefined) planeOffset = currentOffset;
      else if (Math.abs(planeOffset - currentOffset) > 1e-6) throw new Error(`filled region ${String(region.key)} boundary loops must be coplanar.`);
    }
  }
  const profileLoops = array(blueprint.profile_loops ?? [], "blueprint.profile_loops").map((item, index) => object(item, `profile_loops[${index}]`));
  uniqueKeys(profileLoops, "blueprint.profile_loops");
  for (const loop of profileLoops) {
    string(loop.role, `profile loop ${String(loop.key)}.role`, 100);
    validatePresentationSubcategoryKey(loop.subcategory_key, `profile loop ${String(loop.key)}`);
    const path = validatePath({ kind: "polyline", plane: loop.plane, points_mm: loop.points_mm }, `profile loop ${String(loop.key)}`);
    validateSimpleClosedLoop(path.points, String(path.path.plane), `profile loop ${String(loop.key)}`);
  }
  const tagLabels = array(blueprint.tag_labels ?? [], "blueprint.tag_labels").map((item, index) => object(item, `tag_labels[${index}]`));
  uniqueKeys(tagLabels, "blueprint.tag_labels");
  const tagFieldKinds = ["system_abbreviation", "size", "elevation", "bottom_of_duct", "center_of_duct", "top_of_duct", "mark", "type_mark", "family_and_type", "comments", "shared_parameter"] as const;
  for (const label of tagLabels) {
    const labelKey = String(label.key); const semanticField = oneOf(label.semantic_field, tagFieldKinds, `tag label ${labelKey}.semantic_field`);
    for (const property of ["prefix", "suffix"] as const) {
      if (label[property] === undefined) continue;
      const value = string(label[property], `tag label ${labelKey}.${property}`, 64);
      if (/[\r\n]/.test(value)) throw new Error(`tag label ${labelKey}.${property} cannot contain a line break.`);
    }
    if (semanticField === "shared_parameter") {
      string(label.shared_parameter_name, `tag label ${labelKey}.shared_parameter_name`, 120);
      const guid = string(label.shared_parameter_guid, `tag label ${labelKey}.shared_parameter_guid`, 36);
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(guid)) throw new Error(`tag label ${labelKey}.shared_parameter_guid must be a RFC 4122 UUID.`);
    } else if (label.shared_parameter_name !== undefined || label.shared_parameter_guid !== undefined) throw new Error(`tag label ${labelKey} can declare shared_parameter_name/shared_parameter_guid only when semantic_field=shared_parameter.`);
    if (label.value_format !== undefined) {
      const format = oneOf(label.value_format, ["project_default", "custom"] as const, `tag label ${labelKey}.value_format`);
      if (format === "custom" && semanticField !== "size" && !["elevation", "bottom_of_duct", "center_of_duct", "top_of_duct"].includes(semanticField)) throw new Error(`tag label ${labelKey}.value_format=custom is valid only for Size or elevation fields.`);
      if (label.rounding_mm !== undefined && format !== "custom") throw new Error(`tag label ${labelKey}.rounding_mm requires value_format=custom.`);
    } else if (label.rounding_mm !== undefined || label.show_plus !== undefined) throw new Error(`tag label ${labelKey} rounding/show_plus requires value_format=custom.`);
    if (label.rounding_mm !== undefined) positive(label.rounding_mm, `tag label ${labelKey}.rounding_mm`);
    if (label.show_plus !== undefined && typeof label.show_plus !== "boolean") throw new Error(`tag label ${labelKey}.show_plus must be boolean.`);
  }
  const uiFallbacks = array(blueprint.ui_fallbacks ?? [], "blueprint.ui_fallbacks").map((item, index) => object(item, `ui_fallbacks[${index}]`));
  for (const fallback of uiFallbacks) {
    key(fallback.action, "ui_fallback.action");
    string(fallback.reason, "ui_fallback.reason", 240);
    if (fallback.requires_human_checkpoint !== true) throw new Error("Every UI fallback must require_human_checkpoint=true.");
  }
  const templateBehaviorForUi = String(family.template_behavior);
  if (templateBehaviorForUi === "tag") {
    if (familyCategory !== "tag") throw new Error("template_behavior=tag requires family.category=tag.");
    if (tagLabels.length === 0) throw new Error("Tag Family requires at least one tag_labels declaration; a blank Generic Tag is not a usable MEP Tag.");
    if (blueprint.tag_background !== undefined) oneOf(blueprint.tag_background, ["transparent", "opaque"] as const, "blueprint.tag_background");
    const labelFallbacks = uiFallbacks.filter((item) => String(item.action) === "tag_label_editor");
    if (labelFallbacks.length !== 1 || uiFallbacks.length !== 1) throw new Error("Tag Family requires exactly one ui_fallbacks action=tag_label_editor with requires_human_checkpoint=true.");
    if (parts.length || connectors.length || nested.length || modelLines.length || detailLines.length || filledRegions.length || profileLoops.length || coordinationZones.length) throw new Error("Tag Family cannot declare model geometry, connectors, nested components or Detail Item/Profile content; complete labels only in the controlled Tag Editor workflow.");
  } else if (tagLabels.length || blueprint.tag_background !== undefined) throw new Error("tag_labels/tag_background requires template_behavior=tag.");

  const rawBudget = object(blueprint.performance_budget ?? {}, "blueprint.performance_budget");
  const budgetDefaults: Record<string, number> = {
    max_rfa_bytes: 10 * 1024 * 1024,
    max_forms: 120,
    max_void_forms: 30,
    max_parameters: 250,
    max_formula_parameters: 120,
    max_types: 100,
    max_nested_instances: 64,
    max_nested_depth: 4,
    max_array_members: 500,
    max_mirrored_copies: 50,
    max_connectors: 32,
    max_reference_datums: 100,
    max_dimensions: 200,
    max_2d_curves: 1000,
    max_materials: 64,
    max_presentation_subcategories: 64,
    max_light_sources: 1,
    max_lookup_rows: 5000,
    max_complexity_score: 3000,
  };
  const budgetUpperBounds: Record<string, number> = { ...budgetDefaults, max_rfa_bytes: 1024 * 1024 * 1024, max_forms: 2000, max_void_forms: 1000, max_parameters: 2000, max_formula_parameters: 2000, max_types: 1000, max_nested_instances: 1000, max_nested_depth: 16, max_array_members: 10000, max_mirrored_copies: 1000, max_connectors: 256, max_reference_datums: 2000, max_dimensions: 5000, max_2d_curves: 20000, max_materials: 1000, max_presentation_subcategories: 1000, max_light_sources: 1, max_lookup_rows: 100000, max_complexity_score: 100000 };
  const performanceBudget: Json = {};
  for (const [name, defaultValue] of Object.entries(budgetDefaults)) {
    const minimum = name === "max_rfa_bytes" ? 1024 : ["max_forms", "max_types", "max_complexity_score"].includes(name) ? 1 : 0;
    performanceBudget[name] = boundedInteger(rawBudget[name] ?? defaultValue, `blueprint.performance_budget.${name}`, minimum, budgetUpperBounds[name]);
  }
  for (const name of Object.keys(rawBudget)) if (!(name in budgetDefaults)) throw new Error(`blueprint.performance_budget contains unsupported field ${name}.`);

  const arrayMemberCapacity = arrays.reduce((total, contract) => {
    const memberCount = array(contract.member_part_keys ?? [], `array ${String(contract.key)}.member_part_keys`).length + array(contract.member_nested_component_keys ?? [], `array ${String(contract.key)}.member_nested_component_keys`).length;
    const count = contract.count !== undefined ? Number(contract.count) : Number(flexCasesByKey.get(String(contract.count_parameter))?.max ?? 0);
    return total + memberCount * count;
  }, 0);
  const mirroredCopies = mirrors.reduce((total, contract) => total + (contract.copy === true ? array(contract.target_keys, `mirror ${String(contract.key)}.target_keys`).length : 0), 0);
  const polylineSegments = (items: Json[]): number => items.reduce((total, item) => total + Math.max(0, array(item.points_mm, `${String(item.key)}.points_mm`).length - 1), 0);
  const closedLoopSegments = (items: Json[]): number => items.reduce((total, item) => total + array(item.points_mm, `${String(item.key)}.points_mm`).length, 0);
  const filledRegionSegments = filledRegions.reduce((total, item) => {
    const loops = array(item.boundary_loops, `filled region ${String(item.key)}.boundary_loops`);
    return total + loops.reduce<number>((loopTotal, loop, index) => loopTotal + array(object(loop, `filled region ${String(item.key)} loop ${index}`).points_mm, `filled region ${String(item.key)} loop ${index}.points_mm`).length, 0);
  }, 0);
  const twoDimensionalCurves = polylineSegments(symbolic) + polylineSegments(modelLines) + polylineSegments(detailLines) + closedLoopSegments(profileLoops) + filledRegionSegments;
  const formulaParameters = parameters.filter((item) => item.formula !== undefined || item.lookup !== undefined).length;
  const lookupRows = lookupTables.reduce((total, table) => total + array(table.rows, `lookup table ${String(table.key)}.rows`).length, 0);
  const voidFormCount = parts.filter((item) => item.operation === "void").length;
  const metrics: Json = {
    forms: parts.length + coordinationZones.length,
    void_forms: voidFormCount,
    parameters: parameters.length,
    formula_parameters: formulaParameters,
    types: types.length,
    nested_instances: nested.length,
    array_member_capacity: arrayMemberCapacity,
    mirrored_copies: mirroredCopies,
    connectors: connectors.length,
    reference_datums: referencePlanes.length + referenceLines.length,
    dimensions: dimensions.length,
    two_dimensional_curves: twoDimensionalCurves,
    materials: materials.length,
    presentation_subcategories: presentationSubcategories.length,
    light_sources: lightSource ? 1 : 0,
    lookup_rows: lookupRows,
  };
  const complexityScore = (parts.length + coordinationZones.length) * 10 + voidFormCount * 5 + parameters.length * 2 + formulaParameters * 4 + types.length * 3 + nested.length * 15 + arrayMemberCapacity * 4 + mirroredCopies * 6 + connectors.length * 8 + (referencePlanes.length + referenceLines.length) + dimensions.length * 3 + twoDimensionalCurves + materials.length * 2 + presentationSubcategories.length + (lightSource ? 20 : 0) + Math.ceil(lookupRows / 10);
  metrics.complexity_score = complexityScore;
  const metricBudgetMap: Record<string, string> = { forms: "max_forms", void_forms: "max_void_forms", parameters: "max_parameters", formula_parameters: "max_formula_parameters", types: "max_types", nested_instances: "max_nested_instances", array_member_capacity: "max_array_members", mirrored_copies: "max_mirrored_copies", connectors: "max_connectors", reference_datums: "max_reference_datums", dimensions: "max_dimensions", two_dimensional_curves: "max_2d_curves", materials: "max_materials", presentation_subcategories: "max_presentation_subcategories", light_sources: "max_light_sources", lookup_rows: "max_lookup_rows", complexity_score: "max_complexity_score" };
  for (const [metric, budgetName] of Object.entries(metricBudgetMap)) if (Number(metrics[metric]) > Number(performanceBudget[budgetName])) throw new Error(`blueprint complexity metric ${metric}=${String(metrics[metric])} exceeds performance_budget.${budgetName}=${String(performanceBudget[budgetName])}.`);
  blueprint.performance_budget = performanceBudget;
  blueprint.complexity_assessment = { metrics, within_declared_budget: true, nested_depth: "resolved_after_child_artifact_verification", rfa_bytes: "measured_after_compact_save", score_boundary: "Complexity score is a deterministic triage index, not a measured Revit regeneration time." };

  const unsupported: string[] = [];
  const supported = ["parameters_with_group_description_and_shared_definition_metadata", "shared_parameters_with_fixed_guid", "mep_parameter_data_types_and_parameter_order", "native_revit_type_identity_data", "inline_lookup_tables_and_structured_size_lookup", "types", "type_catalog_sidecar", "family_part_type_and_behavior_settings", "approved_min_nominal_max_parameter_flex", "advanced_duct_pipe_connector_flow_factor_and_slope_metadata", "extrusion_rectangle_circle_ring", "parametric_flat_oval_profile_with_orientation_guard", "orthogonal_xyz_extrusion_axes", "arbitrary_direction_extrusion_frame_and_connector_faces", "parameterized_orthogonal_tee_geometry_and_three_connector_placement", "parameterized_angled_wye_lateral_cross_geometry", "normalized_conduit_and_cable_tray_fitting_behaviors", "parameterized_pathway_transition_union_geometry", "bounded_literal_pathway_offset_sweep", "constraint_driven_parameterized_pathway_offset", "revolution_literal_profile", "parameterized_revolution_rectangle_circle_ring_oval_profiles", "sweep_literal_profile_line_polyline_path", "parameterized_elbow_45_90_geometry_and_tangent_connectors", "blend_literal_profiles", "parameterized_blend_rectangle_circle_profiles", "parameterized_blend_rectangle_circle_oval_profiles", "parameterized_fitting_transition_geometry", "swept_blend_literal_profiles_line_path", "parameterized_straight_swept_blend_rectangle_circle_oval_profiles", "void_extrusion_revolution_sweep_blend_swept_blend_cut", "solid_form_join", "reference_plane_line_dimension_alignment_graph", "stable_named_reference_origin_and_reference_plane_graphics", "presentation_subcategory_graphics_for_3d_and_2d_content", "angular_radial_equal_dimension_multibranch_graph", "parameterized_linear_part_and_nested_array", "nested_blueprint_artifact_placement_and_instance_parameter_linking", "placement_aware_nested_blueprint_on_parent_solid_face", "interchangeable_nested_family_type_parameter", "bounded_part_and_nested_component_mirror", "family_material_graphics_appearance_and_form_assignment", "family_material_physical_and_thermal_assets", "lighting_fixture_shape_distribution_photometrics_and_ies", "parameterized_non_physical_coordination_zones", "symbolic_and_model_line_polyline_with_yesno_visibility_association", "detail_line_polyline", "filled_region_literal_loops", "profile_family_closed_polyline", "detail_and_directional_visibility", "family_flip_controls", "compact_save_and_preview_view", "version_aware_template_residue_purge_revit_2024_plus", "declarative_complexity_and_rfa_size_budget", "duct_pipe_electrical_conduit_cable_tray_connectors", "connector_flow_loss_joint_and_engagement_metadata", "electrical_connector_load_and_circuit_parameter_association", "staged_output_read_back"];
  supported.push("reference_line_driven_angular_sweep_rotation");
  supported.push("model_line_endpoint_reference_plane_bindings");
  supported.push("detail_item_detail_line_endpoint_reference_plane_bindings");
  supported.push("coarse_medium_2d__fine_3d_representation_sets");
  supported.push("source_field_to_critical_geometry_and_connector_traceability");
  const parameterizedSweepProfiles = parts
    .map((part) => ({ part, profile: object(part.profile, "part.profile") }))
    .filter(({ part, profile }) => String(part.primitive) === "sweep" && Object.keys(profile).some((property) => property.endsWith("_parameter")))
    .map(({ part, profile }) => ({ part_key: String(part.key), path_kind: String(object(part.path, `part ${String(part.key)}.path`).kind), parameter_keys: Object.keys(profile).filter((property) => property.endsWith("_parameter")).map((property) => String(profile[property])) }));
  if (parameterizedSweepProfiles.length) {
    // Revit 2023 rejects direct dimension references on Sweep.ProfileSketch in a
    // Family document. SketchEditScope is documented for Project documents, so
    // never advertise a label-only/datum-only API path as geometric flex.
    supported.push("parameterized_sweep_profile_ui_preflight");
    unsupported.push("parameterized_sweep_profile_family_editor_ui");
    blueprint.parameterized_sweep_profile_ui_plan = {
      action: "sweep_profile_editor", requires_human_checkpoint: true, parts: parameterizedSweepProfiles,
      verification_required: ["open_the_exact_new_family_document", "edit_each_declared_Sweep_ProfileSketch", "dimension_and_label_each_declared_profile_parameter", "flex_each_parameter_at_approved_min_nominal_max", "finish_the_sketch_and_reopen_inspect_before_rfa_or_load_place"],
      api_boundary: "Family API profile dimensions rejected; no RFA/Project write is authorized by this preflight.",
    };
  }
  if (["tap_perpendicular", "tap_adjustable"].includes(normalizedPartType)) supported.push("bounded_tap_fitting_geometry");
  if (normalizedPartType === "pants") supported.push("bounded_symmetric_pants_fitting_geometry");
  if (["breaks_into", "valve_breaks_into"].includes(normalizedPartType)) supported.push("bounded_two_port_break_into_accessory_routing_contract");
  if (family.primary_axis !== "x") unsupported.push(`primary_axis_${String(family.primary_axis)}`);
  for (const part of parts) {
    const primitive = String(part.primitive);
    const shape = String(object(part.profile, "part.profile").shape);
    if (!["rectangle", "circle", "ring", "oval"].includes(shape)) unsupported.push(`profile_${shape}`);
    if (["blend", "swept_blend"].includes(primitive) && shape === "ring") unsupported.push(`${primitive}_multi_loop_profile`);
    const profileTokens = [object(part.profile, "part.profile"), ...(part.end_profile ? [object(part.end_profile, "part.end_profile")] : [])];
    // Every sweep path has already passed the bounded validator above (line,
    // polyline, arc, parameterized offset, or the constrained Reference Line).
    // A literal profile is buildable by API; a parameterized ProfileSketch is
    // deliberately classified as a Family Editor UI fallback below.
    if (!["extrusion", "blend", "revolution", "swept_blend", "sweep"].includes(primitive) && profileTokens.some((profile) => Object.keys(profile).some((property) => property.endsWith("_parameter")))) unsupported.push(`parameterized_${primitive}_profile`);
    for (const profile of profileTokens) {
      const endShape = String(profile.shape);
      if (!["rectangle", "circle", "ring", "oval"].includes(endShape)) unsupported.push(`profile_${endShape}`);
    }
  }
  const templateBehavior = String(family.template_behavior);
  if (["detail_item", "annotation"].includes(templateBehavior)) {
    if (parts.length) unsupported.push(`${templateBehavior}_model_form_compilation`);
    if (connectors.length) unsupported.push(`${templateBehavior}_connector_compilation`);
    if (symbolic.length + detailLines.length + filledRegions.length === 0) unsupported.push(`${templateBehavior}_requires_2d_declaration`);
  }
  if (modelLines.length && ["detail_item", "annotation", "profile", "tag"].includes(templateBehavior)) unsupported.push("model_lines_require_model_family_template");
  if (coordinationZones.length && ["detail_item", "annotation", "profile", "tag", "adaptive"].includes(templateBehavior)) unsupported.push("coordination_zones_require_model_family_template");
  if (controls.length && ["detail_item", "annotation", "profile"].includes(templateBehavior)) unsupported.push(`controls_require_model_family_template`);
  if ((detailLines.length || filledRegions.length) && !["detail_item", "annotation"].includes(templateBehavior)) unsupported.push("detail_or_filled_region_requires_2d_template");
  if (templateBehavior === "profile") {
    if (parts.length) unsupported.push("profile_model_form_compilation");
    if (connectors.length) unsupported.push("profile_connector_compilation");
    if (profileLoops.length === 0) unsupported.push("profile_requires_closed_loop");
  }
  if (templateBehavior === "adaptive") unsupported.push(`template_behavior_${templateBehavior}`);
  if (templateBehavior === "tag") {
    supported.push("tag_label_field_and_format_preflight");
    unsupported.push("tag_label_editor_requires_controlled_ui");
    blueprint.tag_ui_plan = {
      action: "tag_label_editor", requires_human_checkpoint: true, label_count: tagLabels.length,
      background: blueprint.tag_background ?? "transparent", labels: tagLabels.map((label) => ({ key: label.key, semantic_field: label.semantic_field, shared_parameter_name: label.shared_parameter_name ?? null, shared_parameter_guid: label.shared_parameter_guid ?? null, prefix: label.prefix ?? "", suffix: label.suffix ?? "", value_format: label.value_format ?? "project_default", rounding_mm: label.rounding_mm ?? null, show_plus: label.show_plus ?? false })),
      verification_required: ["open_the_exact_tag_family_document", "select_the_target_tag_category", "add_each_declared_Label_in_Family_Editor", "check_prefix_suffix_and_unit_format", "set_declared_opaque_or_transparent_background", "reopen_and_inspect_each_Label_before_load"],
      boundary: "The Revit API path does not create Dynamic Tag Labels. An external Family Editor/Computer Use workflow must perform and observe this UI operation with a human checkpoint; the MCP panel must not claim UI control.",
    };
  }

  type TraceScope = "geometry" | "connector";
  const criticalReferences = new Map<string, Record<TraceScope, string[]>>();
  const collectCriticalParameterReferences = (value: unknown, path: string, scope: TraceScope): void => {
    if (Array.isArray(value)) { value.forEach((item, index) => collectCriticalParameterReferences(item, `${path}[${index}]`, scope)); return; }
    if (!value || typeof value !== "object") return;
    for (const [property, nestedValue] of Object.entries(value as Json)) {
      const nestedPath = `${path}.${property}`;
      if (property.endsWith("_parameter") && property !== "visibility_parameter") {
        const parameterKey = key(nestedValue, nestedPath);
        if (!parametersByKey.has(parameterKey)) throw new Error(`${nestedPath} references unknown Family Parameter ${parameterKey}.`);
        const references = criticalReferences.get(parameterKey) ?? { geometry: [], connector: [] };
        references[scope].push(nestedPath); criticalReferences.set(parameterKey, references);
      }
      collectCriticalParameterReferences(nestedValue, nestedPath, scope);
    }
  };
  collectCriticalParameterReferences(parts, "parts", "geometry");
  collectCriticalParameterReferences(coordinationZones, "coordination_zones", "geometry");
  collectCriticalParameterReferences(connectors, "connectors", "connector");
  const directUntracedCriticalParameters: string[] = [];
  const formulaOrLookupDerivedCriticalParameters: string[] = [];
  for (const [parameterKey] of criticalReferences) {
    const parameter = parametersByKey.get(parameterKey)!;
    const isDirectDimensionalData = ["length", "area", "volume", "angle"].includes(String(parameter.data_type));
    if (!isDirectDimensionalData || parameter.source_field !== undefined) continue;
    if (parameter.formula !== undefined || parameter.lookup !== undefined) formulaOrLookupDerivedCriticalParameters.push(parameterKey);
    else directUntracedCriticalParameters.push(parameterKey);
  }
  if (requiredSourceFields.length > 0 && directUntracedCriticalParameters.length > 0)
    throw new Error(`Family geometry/connector uses direct critical parameter(s) without source_field traceability: ${directUntracedCriticalParameters.sort().join(", ")}. Declare their source field or make the derivation explicit with a formula/lookup.`);
  const sourceTraceability = {
    policy: "required_source_fields_to_critical_parameter_usage_v1",
    source_evidence_required: requiredSourceFields.length > 0,
    required_field_count: requiredSourceFields.length,
    bindings: requiredSourceFields.map((sourceField) => {
      const parameterKeys = [...(sourceParametersByField.get(sourceField) ?? [])].sort();
      const usage = parameterKeys.reduce<Record<TraceScope, string[]>>((result, parameterKey) => {
        const references = criticalReferences.get(parameterKey);
        result.geometry.push(...(references?.geometry ?? [])); result.connector.push(...(references?.connector ?? [])); return result;
      }, { geometry: [], connector: [] });
      return { source_field: sourceField, parameter_keys: parameterKeys, geometry_target_paths: [...new Set(usage.geometry)].sort(), connector_target_paths: [...new Set(usage.connector)].sort() };
    }),
    critical_parameter_reference_count: [...criticalReferences.values()].reduce((count, references) => count + references.geometry.length + references.connector.length, 0),
    formula_or_lookup_derived_critical_parameter_keys: [...new Set(formulaOrLookupDerivedCriticalParameters)].sort(),
    direct_untraced_critical_parameter_keys: [...new Set(directUntracedCriticalParameters)].sort(),
    boundary: "This links declared source fields to direct Family Parameters and their declared geometry/connector uses. It does not prove the source page/table measurement, per-type catalog completeness, manufacturer revision, or runtime Revit behavior.",
  };

  blueprint.required_source_fields = requiredSourceFields;
  blueprint.source_traceability = sourceTraceability;
  blueprint.blueprint_hash = sha256(JSON.stringify(blueprint));
  return {
    blueprint,
    assessment: {
      buildable_by_api: unsupported.length === 0,
      supported_features: supported,
      unsupported_features: [...new Set(unsupported)].sort(),
      ui_fallback_required: uiFallbacks.length > 0 || unsupported.length > 0,
      certification_boundary: "A successful build proves only the declared blueprint and read-back. LOD certification still requires the per-family runtime matrix.",
    },
  };
}
