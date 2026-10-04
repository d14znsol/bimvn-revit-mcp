import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, renameSync, unlinkSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";

type Json = Record<string, unknown>;
type JsonArray = Json[];

const TTL_MS = 30 * 24 * 60 * 60 * 1000;
const packages = new Map<string, { expiresAt: number; value: Json }>();
const catalogs = new Map<string, { expiresAt: number; value: Json }>();
const plans = new Map<string, { expiresAt: number; value: Json }>();
const previews = new Map<string, { expiresAt: number; value: Json }>();
const reports = new Map<string, { expiresAt: number; value: Json }>();

export class ModelTransferError extends Error {
  constructor(public readonly code: "TransferInvalid" | "TransferBlocked" | "TransferConflict", message: string) { super(message); }
}

function hash(value: unknown): string { return createHash("sha256").update(JSON.stringify(value), "utf8").digest("hex"); }
function clone<T>(value: T): T { return JSON.parse(JSON.stringify(value)) as T; }
function object(value: unknown, label: string): Json {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new ModelTransferError("TransferInvalid", `${label} must be an object.`);
  return value as Json;
}
function array(value: unknown, label: string): JsonArray {
  if (!Array.isArray(value)) throw new ModelTransferError("TransferInvalid", `${label} must be an array.`);
  return value.map((item, index) => object(item, `${label}[${index}]`));
}
function text(value: unknown, label: string, maximum = 160): string {
  if (typeof value !== "string" || !value.trim() || value.trim().length > maximum) throw new ModelTransferError("TransferInvalid", `${label} must be a non-empty string no longer than ${maximum} characters.`);
  return value.trim();
}
function integer(value: unknown, label: string): number {
  const result = Number(value); if (!Number.isSafeInteger(result) || result < 1) throw new ModelTransferError("TransferInvalid", `${label} must be a positive integer.`); return result;
}
function nonNegativeInteger(value: unknown, label: string): number {
  const result = Number(value); if (!Number.isSafeInteger(result) || result < 0) throw new ModelTransferError("TransferInvalid", `${label} must be a non-negative integer.`); return result;
}
function finite(value: unknown, label: string, positive = false): number {
  const result = Number(value); if (!Number.isFinite(result) || (positive && result <= 0)) throw new ModelTransferError("TransferInvalid", `${label} must be ${positive ? "positive" : "finite"}.`); return result;
}
function recordDirectory(): string {
  const base = process.env.DSCONS_MODEL_TRANSFER_RECORD_DIRECTORY || path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local"), "DSCons", "RevitMcp", "model-transfer-records");
  return path.resolve(base);
}
function persist(kind: string, id: string, value: Json, expiresAt: number): void {
  if (!/^[a-z0-9-]{8,96}$/i.test(id)) throw new ModelTransferError("TransferInvalid", "Transfer record id has an invalid format.");
  const directory = recordDirectory(); mkdirSync(directory, { recursive: true });
  const target = path.join(directory, `${id}.json`); const temporary = path.join(directory, `.${kind}-${process.pid}-${Date.now()}.tmp`);
  const payload = { format: "dscons_model_transfer_record_v1", kind, id, expires_at_utc: new Date(expiresAt).toISOString(), value_sha256: hash(value), value };
  try { writeFileSync(temporary, JSON.stringify(payload, null, 2), { encoding: "utf8", flag: "wx" }); renameSync(temporary, target); }
  finally { if (existsSync(temporary)) try { unlinkSync(temporary); } catch { /* best effort */ } }
}
function restore(kind: string, id: string): Json | undefined {
  const file = path.join(recordDirectory(), `${id}.json`); if (!existsSync(file)) return undefined;
  try {
    const envelope = JSON.parse(readFileSync(file, "utf8")) as { format?: string; kind?: string; id?: string; expires_at_utc?: string; value_sha256?: string; value?: Json };
    const expiresAt = Date.parse(String(envelope.expires_at_utc ?? ""));
    if (envelope.format !== "dscons_model_transfer_record_v1" || envelope.kind !== kind || envelope.id !== id || !envelope.value || envelope.value_sha256 !== hash(envelope.value) || !Number.isFinite(expiresAt) || expiresAt < Date.now()) return undefined;
    return envelope.value;
  } catch { throw new ModelTransferError("TransferInvalid", `Persisted ${kind} record is unreadable or has an invalid checksum.`); }
}
function keep(store: Map<string, { expiresAt: number; value: Json }>, kind: string, id: string, value: Json): Json {
  const expiresAt = Date.now() + TTL_MS; store.set(id, { expiresAt, value: clone(value) }); persist(kind, id, value, expiresAt); return clone(value);
}
function read(store: Map<string, { expiresAt: number; value: Json }>, kind: string, id: string): Json {
  const found = store.get(id); if (found && found.expiresAt >= Date.now()) return clone(found.value);
  const restored = restore(kind, id); if (!restored) throw new ModelTransferError("TransferInvalid", `${kind} id is unknown, expired, or no longer checksum-valid.`);
  store.set(id, { expiresAt: Date.now() + TTL_MS, value: clone(restored) }); return clone(restored);
}
function id(prefix: string, value: unknown): string { return `${prefix}-${hash(value).slice(0, 24)}`; }
function sourceKey(value: Json, label: string): string { return text(value.source_key, `${label}.source_key`, 100); }
function entries(value: unknown, label: string): JsonArray { return Array.isArray(value) ? array(value, label) : []; }
function sourceResources(packageValue: Json): Map<string, Json> {
  const resources = object(packageValue.resources, "package.resources"); const result = new Map<string, Json>();
  for (const name of ["levels", "route_types", "system_types", "family_symbols"]) {
    for (const [index, resource] of entries(resources[name], `package.resources.${name}`).entries()) {
      const key = sourceKey(resource, `package.resources.${name}[${index}]`);
      if (result.has(key)) throw new ModelTransferError("TransferInvalid", `Duplicate source resource ${key}.`);
      result.set(key, resource);
    }
  }
  return result;
}

function validatePackageTopology(packageValue: Json, elements: JsonArray): void {
  const elementKeys = new Set(elements.map((item, index) => sourceKey(item, `elements[${index}]`)));
  const connections = array(packageValue.physical_connections, "ModelTransferPackage.physical_connections");
  const edgeKeys = new Set<string>(); const edges: Array<{ from: string; to: string }> = [];
  for (const [index, connection] of connections.entries()) {
    if (text(connection.connection_kind, `physical_connections[${index}].connection_kind`, 32) !== "physical") throw new ModelTransferError("TransferInvalid", `physical_connections[${index}] is not a physical connector edge.`);
    const from = object(connection.from, `physical_connections[${index}].from`); const to = object(connection.to, `physical_connections[${index}].to`);
    const fromKey = text(from.element_key, `physical_connections[${index}].from.element_key`, 100); const toKey = text(to.element_key, `physical_connections[${index}].to.element_key`, 100);
    const fromIndex = nonNegativeInteger(from.connector_index, `physical_connections[${index}].from.connector_index`); const toIndex = nonNegativeInteger(to.connector_index, `physical_connections[${index}].to.connector_index`);
    if (!elementKeys.has(fromKey) || !elementKeys.has(toKey)) throw new ModelTransferError("TransferInvalid", `physical_connections[${index}] references an element outside the package.`);
    if (fromKey === toKey) throw new ModelTransferError("TransferInvalid", `physical_connections[${index}] is a self-edge; connector topology is not trustworthy.`);
    const first = `${fromKey}:${fromIndex}`; const second = `${toKey}:${toIndex}`; const edgeKey = first < second ? `${first}|${second}` : `${second}|${first}`;
    if (edgeKeys.has(edgeKey)) throw new ModelTransferError("TransferInvalid", `Duplicate physical connector edge ${edgeKey}.`); edgeKeys.add(edgeKey);
    edges.push({ from: fromKey, to: toKey });
  }

  const groups = array(packageValue.dependency_groups, "ModelTransferPackage.dependency_groups");
  if (groups.length < 1) throw new ModelTransferError("TransferInvalid", "ModelTransferPackage requires at least one dependency group.");
  const membership = new Map<string, string>(); const groupKeys = new Set<string>();
  for (const [index, group] of groups.entries()) {
    const groupKey = text(group.group_key, `dependency_groups[${index}].group_key`, 100); if (groupKeys.has(groupKey)) throw new ModelTransferError("TransferInvalid", `Duplicate dependency group ${groupKey}.`); groupKeys.add(groupKey);
    if (!Array.isArray(group.element_keys) || group.element_keys.length < 1) throw new ModelTransferError("TransferInvalid", `Dependency group ${groupKey} must contain at least one element.`);
    const keys = group.element_keys.map((value, keyIndex) => text(value, `${groupKey}.element_keys[${keyIndex}]`, 100));
    if (new Set(keys).size !== keys.length) throw new ModelTransferError("TransferInvalid", `Dependency group ${groupKey} contains a duplicate element key.`);
    for (const key of keys) {
      if (!elementKeys.has(key)) throw new ModelTransferError("TransferInvalid", `Dependency group ${groupKey} references absent element ${key}.`);
      if (membership.has(key)) throw new ModelTransferError("TransferInvalid", `Element ${key} belongs to more than one dependency group.`);
      membership.set(key, groupKey);
    }
    const observed = edges.filter((edge) => keys.includes(edge.from) && keys.includes(edge.to)).length;
    const declared = nonNegativeInteger(group.physical_connection_count, `${groupKey}.physical_connection_count`);
    if (declared !== observed) throw new ModelTransferError("TransferInvalid", `Dependency group ${groupKey} declares ${declared} physical edges but the package contains ${observed}.`);
  }
  for (const key of elementKeys) if (!membership.has(key)) throw new ModelTransferError("TransferInvalid", `Element ${key} is absent from dependency_groups.`);
  for (const edge of edges) if (membership.get(edge.from) !== membership.get(edge.to)) throw new ModelTransferError("TransferInvalid", `Physical edge ${edge.from} → ${edge.to} crosses dependency groups.`);
}

/** Stores only a normalized, checksum-bound snapshot emitted by the source Revit adapter. */
export function recordModelTransferPackage(raw: unknown): Json {
  const packageValue = object(raw, "ModelTransferPackage");
  if (packageValue.record_kind !== "model_transfer_package_v1" || packageValue.schema_version !== "1.1") throw new ModelTransferError("TransferInvalid", "Source adapter did not return ModelTransferPackage v1.1.");
  const source = object(packageValue.source, "ModelTransferPackage.source");
  if (text(source.revit_version, "ModelTransferPackage.source.revit_version", 4) !== "2025") throw new ModelTransferError("TransferBlocked", "V1 accepts a source snapshot only from Revit 2025.");
  text(source.document_fingerprint, "ModelTransferPackage.source.document_fingerprint", 128);
  nonNegativeInteger(source.document_revision, "ModelTransferPackage.source.document_revision");
  if (typeof source.modified_at_extract !== "boolean") throw new ModelTransferError("TransferInvalid", "ModelTransferPackage.source.modified_at_extract must be explicit.");
  if (source.snapshot_stable !== true || packageValue.model_changed !== false) throw new ModelTransferError("TransferBlocked", "Source document changed during extraction; discard this package and extract a new stable snapshot.");
  sourceResources(packageValue);
  const elements = array(packageValue.elements, "ModelTransferPackage.elements"); if (elements.length < 1 || elements.length > 500) throw new ModelTransferError("TransferInvalid", "ModelTransferPackage.elements must contain 1..500 items.");
  const unique = new Set<string>(); for (const [index, item] of elements.entries()) { const key = sourceKey(item, `elements[${index}]`); if (unique.has(key)) throw new ModelTransferError("TransferInvalid", `Duplicate source_key ${key}.`); unique.add(key); }
  validatePackageTopology(packageValue, elements);
  const normalized = clone(packageValue); const packageId = id("transfer-package", { schema_version: packageValue.schema_version, source, elements, resources: packageValue.resources, physical_connections: packageValue.physical_connections, dependency_groups: packageValue.dependency_groups });
  normalized.package_id = packageId; normalized.package_sha256 = hash({ ...normalized, package_id: undefined, package_sha256: undefined }); normalized.persisted_local_only = true;
  return keep(packages, "package", packageId, normalized);
}

export function recordModelTransferDestinationCatalog(raw: unknown): Json {
  const catalog = object(raw, "ModelTransferDestinationCatalog");
  if (catalog.record_kind !== "model_transfer_destination_catalog_v1" || catalog.schema_version !== "1.1") throw new ModelTransferError("TransferInvalid", "Destination adapter did not return ModelTransferDestinationCatalog v1.1.");
  const destination = object(catalog.destination, "ModelTransferDestinationCatalog.destination");
  if (text(destination.revit_version, "ModelTransferDestinationCatalog.destination.revit_version", 4) !== "2023") throw new ModelTransferError("TransferBlocked", "V1 accepts a destination catalog only from Revit 2023.");
  text(destination.document_fingerprint, "ModelTransferDestinationCatalog.destination.document_fingerprint", 128);
  nonNegativeInteger(destination.document_revision, "ModelTransferDestinationCatalog.destination.document_revision");
  if (catalog.model_changed !== false) throw new ModelTransferError("TransferBlocked", "Destination catalog did not prove a read-only snapshot; rebuild it in the clean R23 staging Project.");
  const resources = array(catalog.resources, "ModelTransferDestinationCatalog.resources"); if (resources.length > 5000) throw new ModelTransferError("TransferInvalid", "Destination catalog is too large.");
  const ids = new Set<number>(); for (const [index, item] of resources.entries()) { const itemId = integer(item.destination_id, `resources[${index}].destination_id`); if (ids.has(itemId)) throw new ModelTransferError("TransferInvalid", `Duplicate destination resource ${itemId}.`); ids.add(itemId); text(item.kind, `resources[${index}].kind`, 80); }
  const normalized = clone(catalog); const catalogId = id("transfer-catalog", { schema_version: catalog.schema_version, destination, resources }); normalized.catalog_id = catalogId; normalized.catalog_sha256 = hash({ schema_version: catalog.schema_version, destination, resources }); normalized.persisted_local_only = true;
  return keep(catalogs, "catalog", catalogId, normalized);
}

function mappingsBySource(args: Json, catalog: Json): Map<string, Json> {
  const raw = array(args.mappings, "mappings"); if (raw.length < 1 || raw.length > 5000) throw new ModelTransferError("TransferInvalid", "mappings must contain 1..5000 explicit mappings.");
  const targetResources = new Map(entries(catalog.resources, "catalog.resources").map((item) => [integer(item.destination_id, "catalog resource id"), item]));
  const result = new Map<string, Json>();
  for (const [index, mapping] of raw.entries()) {
    const source = text(mapping.source_key, `mappings[${index}].source_key`, 100); const kind = text(mapping.kind, `mappings[${index}].kind`, 80); const targetId = integer(mapping.destination_id, `mappings[${index}].destination_id`);
    const target = targetResources.get(targetId); if (!target) throw new ModelTransferError("TransferConflict", `Mapping ${source} targets absent destination resource ${targetId}.`);
    if (text(target.kind, `destination resource ${targetId}.kind`, 80) !== kind) throw new ModelTransferError("TransferConflict", `Mapping ${source} kind ${kind} does not match destination resource ${targetId}.`);
    if (result.has(source)) throw new ModelTransferError("TransferInvalid", `Duplicate mapping for ${source}.`);
    result.set(source, { ...mapping, destination: target });
  }
  return result;
}
function horizontalStraight(points: JsonArray): boolean {
  if (points.length !== 2) return false;
  const first = points[0]; const second = points[1];
  const dx = Math.abs(finite(second.x_mm, "points[1].x_mm") - finite(first.x_mm, "points[0].x_mm"));
  const dy = Math.abs(finite(second.y_mm, "points[1].y_mm") - finite(first.y_mm, "points[0].y_mm"));
  const dz = Math.abs(finite(second.z_mm, "points[1].z_mm") - finite(first.z_mm, "points[0].z_mm"));
  return dz <= 0.1 && ((dx > 0.1 && dy <= 0.1) || (dy > 0.1 && dx <= 0.1));
}
function point(value: unknown, label: string): Json {
  const result = object(value, label); for (const axis of ["x_mm", "y_mm", "z_mm"]) finite(result[axis], `${label}.${axis}`); return result;
}
function distanceMm(left: Json, right: Json): number {
  return Math.hypot(Number(left.x_mm) - Number(right.x_mm), Number(left.y_mm) - Number(right.y_mm), Number(left.z_mm) - Number(right.z_mm));
}
function routeAxis(points: JsonArray): "x" | "y" | undefined {
  if (!horizontalStraight(points)) return undefined;
  return Math.abs(Number(points[1].x_mm) - Number(points[0].x_mm)) > 0.1 ? "x" : "y";
}
function sameOptionalSize(left: Json, right: Json): boolean {
  for (const name of ["diameter_mm", "width_mm", "height_mm"]) {
    const a = left[name]; const b = right[name]; if ((a === undefined) !== (b === undefined)) return false;
    if (a !== undefined && Math.abs(Number(a) - Number(b)) > 0.1) return false;
  }
  return true;
}
function fittingCategory(routeKind: string): string {
  return ({ pipe: "pipe_fitting", duct: "duct_fitting", conduit: "conduit_fitting", cable_tray: "cable_tray_fitting" } as Record<string, string>)[routeKind] ?? "unsupported";
}
function endpointFor(connection: Json, elementKey: string, label: string): { index: number; detail: Json } {
  const from = object(connection.from, `${label}.from`); const to = object(connection.to, `${label}.to`);
  if (from.element_key === elementKey) return { index: nonNegativeInteger(from.connector_index, `${label}.from.connector_index`), detail: object(connection.from_connector, `${label}.from_connector`) };
  if (to.element_key === elementKey) return { index: nonNegativeInteger(to.connector_index, `${label}.to.connector_index`), detail: object(connection.to_connector, `${label}.to_connector`) };
  throw new ModelTransferError("TransferInvalid", `${label} does not reference ${elementKey}.`);
}
function openEnd(points: JsonArray, connected: Json, label: string): Json {
  const first = point(points[0], `${label}.points[0]`); const second = point(points[1], `${label}.points[1]`); const origin = point(connected.origin, `${label}.connected.origin`);
  const firstDistance = distanceMm(first, origin); const secondDistance = distanceMm(second, origin);
  if (Math.min(firstDistance, secondDistance) > 0.1) throw new ModelTransferError("TransferBlocked", `${label} fitting connector does not coincide with a route endpoint within 0.1 mm.`);
  return firstDistance <= secondDistance ? second : first;
}
function between(value: number, start: number, end: number): boolean { return value >= Math.min(start, end) - 0.1 && value <= Math.max(start, end) + 0.1; }
function edgeConnectorDetailsMatch(left: Json, right: Json, tolerance: number): boolean {
  const leftOrigin = point(left.origin, "physical edge left origin"); const rightOrigin = point(right.origin, "physical edge right origin");
  return distanceMm(leftOrigin, rightOrigin) <= tolerance && left.domain === right.domain && left.profile === right.profile && sameOptionalSize(left, right);
}
function normalizeElbowGroup(groupKey: string, keys: string[], elements: Map<string, Json>, packageValue: Json, mapping: Map<string, Json>, resources: Map<string, Json>): Json {
  const members = keys.map((key) => elements.get(key)).filter((value): value is Json => value !== undefined); const routes = members.filter((item) => item.kind === "route").sort((left, right) => sourceKey(left, "route").localeCompare(sourceKey(right, "route"))); const fittings = members.filter((item) => item.kind === "fitting");
  if (members.length !== 3 || routes.length !== 2 || fittings.length !== 1) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} is not exactly two routes plus one elbow fitting.`);
  const fitting = fittings[0]; const fittingKey = sourceKey(fitting, "elbow"); const routeKind = text(routes[0].route_kind, `${groupKey}.route_kind`, 32);
  if (text(fitting.fitting_role, `${fittingKey}.fitting_role`, 40) !== "elbow" || text(fitting.route_kind, `${fittingKey}.route_kind`, 32) !== routeKind) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} has no certified two-port ${routeKind} elbow.`);
  const normalizedRoutes = routes.map((route) => normalizeRoute(route, mapping, resources, String(packageValue.package_id)));
  if (normalizedRoutes.some((route) => route.kind !== routeKind) || normalizedRoutes[0].type_id !== normalizedRoutes[1].type_id || normalizedRoutes[0].level_id !== normalizedRoutes[1].level_id || normalizedRoutes[0].system_type_id !== normalizedRoutes[1].system_type_id || !sameOptionalSize(normalizedRoutes[0], normalizedRoutes[1])) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} route legs do not share one Type, Level, System and exact size contract.`);
  const axes = routes.map((route) => routeAxis(array(route.points_mm, `${sourceKey(route, "route")}.points_mm`))); if (!axes[0] || !axes[1] || axes[0] === axes[1]) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} is not one horizontal orthogonal elbow path.`);
  const allConnections = array(packageValue.physical_connections, "package.physical_connections"); const groupConnections = allConnections.filter((connection) => {
    const from = String(object(connection.from, "connection.from").element_key); const to = String(object(connection.to, "connection.to").element_key); return keys.includes(from) && keys.includes(to);
  });
  if (groupConnections.length !== 2) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} must contain exactly two physical route-to-elbow edges.`);
  const connectorSignature = array(fitting.connector_signature, `${fittingKey}.connector_signature`);
  if (connectorSignature.length !== 2) throw new ModelTransferError("TransferBlocked", `Elbow ${fittingKey} must expose exactly two physical connectors.`);
  const routeConnections = new Map<string, Json>(); const fittingConnectorIndexes = new Set<number>();
  for (const connection of groupConnections) {
    const from = String(object(connection.from, "connection.from").element_key); const to = String(object(connection.to, "connection.to").element_key); const other = from === fittingKey ? to : to === fittingKey ? from : "";
    if (!other || !routes.some((route) => sourceKey(route, "route") === other) || routeConnections.has(other)) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} does not have one unique elbow edge per route leg.`);
    const routeEndpoint = endpointFor(connection, other, `${groupKey}.${other}`); const fittingEndpoint = endpointFor(connection, fittingKey, `${groupKey}.${fittingKey}`);
    if (routeEndpoint.index > 1) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} route ${other} references connector index ${routeEndpoint.index}; a straight route leg must use endpoint index 0 or 1.`);
    if (fittingEndpoint.index >= connectorSignature.length || fittingConnectorIndexes.has(fittingEndpoint.index)) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} does not bind each route leg to one unique elbow connector index.`);
    if (routeEndpoint.detail.is_connected !== true || fittingEndpoint.detail.is_connected !== true || !edgeConnectorDetailsMatch(routeEndpoint.detail, fittingEndpoint.detail, 0.1)) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} has an inconsistent physical route-to-elbow connector edge.`);
    if (!connectorSignatureMatches([fittingEndpoint.detail], [connectorSignature[fittingEndpoint.index]], 0.1)) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} elbow connector index does not match the extracted fitting signature.`);
    fittingConnectorIndexes.add(fittingEndpoint.index);
    routeConnections.set(other, connection);
  }
  const connected = routes.map((route) => endpointFor(routeConnections.get(sourceKey(route, "route"))!, sourceKey(route, "route"), `${groupKey}.${sourceKey(route, "route")}`));
  const routePoints = routes.map((route) => array(route.points_mm, `${sourceKey(route, "route")}.points_mm`)); const outer = routes.map((route, index) => openEnd(routePoints[index], connected[index].detail, `${groupKey}.${sourceKey(route, "route")}`));
  const firstLine = routePoints[0]; const secondLine = routePoints[1]; const z = Number(firstLine[0].z_mm);
  if ([...firstLine, ...secondLine].some((item) => Math.abs(Number(item.z_mm) - z) > 0.1)) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} route legs are not coplanar within 0.1 mm.`);
  const corner: Json = axes[0] === "x" ? { x_mm: Number(secondLine[0].x_mm), y_mm: Number(firstLine[0].y_mm), z_mm: z } : { x_mm: Number(firstLine[0].x_mm), y_mm: Number(secondLine[0].y_mm), z_mm: z };
  for (let index = 0; index < 2; index += 1) {
    const connectedOrigin = point(connected[index].detail.origin, `${groupKey}.connected[${index}].origin`); const axis = axes[index]!; const cross = axis === "x" ? "y_mm" : "x_mm"; const along = axis === "x" ? "x_mm" : "y_mm";
    if (Math.abs(Number(connectedOrigin[cross]) - Number(corner[cross])) > 0.1 || !between(Number(connectedOrigin[along]), Number(outer[index][along]), Number(corner[along]))) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} elbow connector is not on the bounded source route leg.`);
  }
  const logicalPoints = [outer[0], corner, outer[1]]; if (!horizontalStraight([logicalPoints[0], logicalPoints[1]]) || !horizontalStraight([logicalPoints[1], logicalPoints[2]])) throw new ModelTransferError("TransferBlocked", `Dependency group ${groupKey} cannot be represented as one bounded orthogonal elbow path.`);
  const symbolKey = text(fitting.symbol_source_key, `${fittingKey}.symbol_source_key`, 100); const symbol = mappedResource(mapping, resources, symbolKey, "family_symbol", `Elbow ${fittingKey}`); const sourceSymbol = resources.get(symbolKey)!; const targetSymbol = object(symbol.destination, `Elbow ${fittingKey} destination`); const expectedCategory = fittingCategory(routeKind);
  if (text(sourceSymbol.category, `${fittingKey} source category`, 80) !== expectedCategory || text(sourceSymbol.part_type, `${fittingKey} source part_type`, 80) !== "elbow" || text(targetSymbol.category, `${fittingKey} destination category`, 80) !== expectedCategory || text(targetSymbol.part_type, `${fittingKey} destination part_type`, 80) !== "elbow" || text(targetSymbol.route_kind, `${fittingKey} destination route_kind`, 32) !== routeKind) throw new ModelTransferError("TransferConflict", `Elbow ${fittingKey} mapping does not preserve native category, Part Type and route domain.`);
  const nativeDetail = object(fitting.native_detail, `${fittingKey}.native_detail`); const boundingBox = object(nativeDetail.bounding_box, `${fittingKey}.native_detail.bounding_box`);
  const marker = `DSCons transfer ${String(packageValue.package_id)} ${groupKey}`;
  return {
    name: groupKey, kind: routeKind, type_id: normalizedRoutes[0].type_id, level_id: normalizedRoutes[0].level_id, system_type_id: normalizedRoutes[0].system_type_id,
    points: logicalPoints, diameter_mm: normalizedRoutes[0].diameter_mm, width_mm: normalizedRoutes[0].width_mm, height_mm: normalizedRoutes[0].height_mm,
    demo_tag: marker, provenance_marker: marker, transfer_shape: "orthogonal_elbow_chain",
    source_segments: normalizedRoutes.map((route, index) => ({ ...route, source_key: sourceKey(routes[index], "route"), points_mm: routePoints[index], provenance_marker: marker, expected_open_connector_count: 1, expected_physical_connector_count: 2, expected_connected_source_keys: [fittingKey] })),
    source_fitting: { source_key: fittingKey, kind: "fitting", type_id: integer(symbol.destination_id, `${fittingKey}.destination_id`), level_id: normalizedRoutes[0].level_id, fitting_role: "elbow", provenance_marker: marker, expected_bounding_box: boundingBox, expected_connector_signature: connectorSignature, expected_open_connector_count: 0, expected_physical_connector_count: 2, expected_connected_source_keys: routes.map((route) => sourceKey(route, "route")) }
  };
}
function mappedResource(mapping: Map<string, Json>, resources: Map<string, Json>, key: string, expectedKind: string, label: string): Json {
  const source = resources.get(key); const target = mapping.get(key);
  if (!source || text(source.kind, `${label} source kind`, 80) !== expectedKind) throw new ModelTransferError("TransferBlocked", `${label} has no compatible source resource ${key}.`);
  if (!target || text(target.kind, `${label} mapping kind`, 80) !== expectedKind) throw new ModelTransferError("TransferBlocked", `${label} lacks an explicit compatible ${expectedKind} mapping.`);
  return target;
}
function normalizeRoute(element: Json, mapping: Map<string, Json>, resources: Map<string, Json>, packageId: string): Json {
  const routeKind = text(element.route_kind, `${sourceKey(element, "route")}.route_kind`, 32); if (!new Set(["pipe", "duct", "conduit", "cable_tray"]).has(routeKind)) throw new ModelTransferError("TransferBlocked", `Route ${sourceKey(element, "route")} has unsupported kind ${routeKind}.`);
  const points = array(element.points_mm, `${sourceKey(element, "route")}.points_mm`); if (!horizontalStraight(points)) throw new ModelTransferError("TransferBlocked", `Route ${sourceKey(element, "route")} is not one straight horizontal segment; V1 refuses vertical, diagonal, sloped, curved or multi-segment reconstruction.`);
  const key = sourceKey(element, "route"); const typeKey = text(element.type_source_key, `${key}.type_source_key`, 100); const levelKey = text(element.level_source_key, `${key}.level_source_key`, 100);
  const type = mappedResource(mapping, resources, typeKey, "route_type", `Route ${key}`); const level = mappedResource(mapping, resources, levelKey, "level", `Route ${key}`);
  if (text(object(type.destination, `Route ${key} type destination`).route_kind, `Route ${key} type destination.route_kind`, 32) !== routeKind) throw new ModelTransferError("TransferConflict", `Route ${key} mapped Type has a different route_kind; display-name matches are not compatible.`);
  const sourceLevel = resources.get(levelKey)!; const targetLevel = object(level.destination, `Route ${key} level destination`);
  if (Math.abs(finite(sourceLevel.elevation_mm, `Route ${key} source Level elevation`) - finite(targetLevel.elevation_mm, `Route ${key} destination Level elevation`)) > 0.1) throw new ModelTransferError("TransferConflict", `Route ${key} mapped Level elevation differs by more than 0.1 mm.`);
  const marker = `DSCons transfer ${packageId} ${key}`;
  const result: Json = { name: key, kind: routeKind, type_id: integer(type.destination_id, "route type destination_id"), level_id: integer(level.destination_id, "route level destination_id"), points, demo_tag: marker, provenance_marker: marker };
  if (routeKind === "pipe" || routeKind === "duct") {
    const system = mappedResource(mapping, resources, text(element.system_source_key, `${key}.system_source_key`, 100), "system_type", `Route ${key}`); const sourceSystem = resources.get(text(element.system_source_key, `${key}.system_source_key`, 100))!; const targetSystem = object(system.destination, `Route ${key} system destination`);
    if (text(targetSystem.route_kind, `Route ${key} system destination.route_kind`, 32) !== routeKind) throw new ModelTransferError("TransferConflict", `Route ${key} mapped System Type has a different route_kind.`);
    if (text(sourceSystem.system_classification, `Route ${key} source system classification`, 80) !== text(targetSystem.system_classification, `Route ${key} destination system classification`, 80)) throw new ModelTransferError("TransferConflict", `Route ${key} mapped System Type has a different native system classification.`);
    result.system_type_id = integer(system.destination_id, "route system destination_id");
  }
  const sizes: Json = {}; for (const size of ["diameter_mm", "width_mm", "height_mm"]) if (element[size] !== undefined && element[size] !== null) sizes[size] = finite(element[size], `${key}.${size}`, true);
  const hasDiameter = sizes.diameter_mm !== undefined; const hasWidth = sizes.width_mm !== undefined; const hasHeight = sizes.height_mm !== undefined;
  if ((routeKind === "pipe" || routeKind === "conduit") && (!hasDiameter || hasWidth || hasHeight)) throw new ModelTransferError("TransferBlocked", `Route ${key} lacks one exact round diameter contract.`);
  if (routeKind === "cable_tray" && (hasDiameter || !hasWidth || !hasHeight)) throw new ModelTransferError("TransferBlocked", `Route ${key} lacks one exact rectangular width/height contract.`);
  if (routeKind === "duct" && !((hasDiameter && !hasWidth && !hasHeight) || (!hasDiameter && hasWidth && hasHeight))) throw new ModelTransferError("TransferBlocked", `Route ${key} lacks one unambiguous round or rectangular/oval size contract.`);
  Object.assign(result, sizes);
  return result;
}
function normalizeEquipment(element: Json, mapping: Map<string, Json>, resources: Map<string, Json>, packageId: string): Json {
  const supported = new Set(["mechanical_equipment", "duct_terminal", "duct_accessory", "pipe_accessory", "plumbing_fixture", "sprinkler", "electrical_equipment", "electrical_fixture", "lighting_fixture"]);
  const key = sourceKey(element, "equipment"); const category = text(element.category, `${key}.category`, 80); if (!supported.has(category)) throw new ModelTransferError("TransferBlocked", `Equipment ${key} category ${category} is outside the V1 native placement set.`);
  if (text(element.placement_type, `${key}.placement_type`, 80) !== "OneLevelBased") throw new ModelTransferError("TransferBlocked", `Equipment ${key} is hosted, work-plane based, or otherwise not non-hosted OneLevelBased.`);
  const symbol = mappedResource(mapping, resources, text(element.symbol_source_key, `${key}.symbol_source_key`, 100), "family_symbol", `Equipment ${key}`); const level = mappedResource(mapping, resources, text(element.level_source_key, `${key}.level_source_key`, 100), "level", `Equipment ${key}`);
  const sourceSymbol = resources.get(text(element.symbol_source_key, `${key}.symbol_source_key`, 100))!; const destination = object(symbol.destination, `Equipment ${key} symbol destination`);
  if (text(destination.category, `Equipment ${key} symbol destination.category`, 80) !== category || text(destination.placement_type, `Equipment ${key} symbol destination.placement_type`, 80) !== "OneLevelBased") throw new ModelTransferError("TransferConflict", `Equipment ${key} mapped FamilySymbol has incompatible category or placement behavior.`);
  if (typeof sourceSymbol.compatibility_signature !== "string" || typeof destination.compatibility_signature !== "string" || sourceSymbol.compatibility_signature !== destination.compatibility_signature) throw new ModelTransferError("TransferBlocked", `Equipment ${key} has no exact verified Family geometry/parameter/connector compatibility signature in both versions; V1 refuses a category/name-only substitute.`);
  const sourceLevel = resources.get(text(element.level_source_key, `${key}.level_source_key`, 100))!; const targetLevel = object(level.destination, `Equipment ${key} level destination`);
  if (Math.abs(finite(sourceLevel.elevation_mm, `Equipment ${key} source Level elevation`) - finite(targetLevel.elevation_mm, `Equipment ${key} destination Level elevation`)) > 0.1) throw new ModelTransferError("TransferConflict", `Equipment ${key} mapped Level elevation differs by more than 0.1 mm.`);
  const point = object(element.point_mm, `${key}.point_mm`); ["x_mm", "y_mm", "z_mm"].forEach((axis) => finite(point[axis], `${key}.point_mm.${axis}`));
  const marker = `DSCons transfer ${packageId} ${key}`;
  return { key, category, placement_mode: "level_based_non_hosted", symbol_id: integer(symbol.destination_id, `${key}.symbol_id`), level_id: integer(level.destination_id, `${key}.level_id`), point_mm: point, rotation_degrees: finite(element.rotation_degrees ?? 0, `${key}.rotation_degrees`), source_tag: marker, provenance_marker: marker, expected_connector_signature: element.connector_signature ?? [] };
}
function connectedGroups(packageValue: Json): JsonArray { const groups = entries(packageValue.dependency_groups, "package.dependency_groups"); if (groups.length < 1) throw new ModelTransferError("TransferInvalid", "Source package has no dependency groups."); return groups; }

/** Builds a fail-closed plan; no name-only Family/Type matching is performed. */
export function planModelTransfer(raw: unknown): Json {
  const args = object(raw, "model_transfer_plan"); if (args.staged_project_confirmed !== true) throw new ModelTransferError("TransferBlocked", "staged_project_confirmed=true is required; V1 never targets a production Project.");
  const packageValue = read(packages, "package", text(args.package_id, "package_id", 96)); const catalog = read(catalogs, "catalog", text(args.catalog_id, "catalog_id", 96)); const source = object(packageValue.source, "package.source"); const destination = object(catalog.destination, "catalog.destination");
  if (source.revit_version !== "2025" || destination.revit_version !== "2023") throw new ModelTransferError("TransferBlocked", "Only Revit 2025 → 2023 is enabled in V1.");
  const transform = object(source.coordinate_transform, "package.source.coordinate_transform"); if (transform.is_identity !== true) throw new ModelTransferError("TransferBlocked", "Non-identity shared-coordinate transforms are reported but not applied in V1; transfer is blocked rather than silently relocating MEP.");
  const mapping = mappingsBySource(args, catalog); const resources = sourceResources(packageValue); const elements = new Map(array(packageValue.elements, "package.elements").map((item) => [sourceKey(item, "package element"), item]));
  const approved = args.approved_group_keys === undefined ? undefined : new Set(array(args.approved_group_keys, "approved_group_keys").map((item, index) => text(item.group_key, `approved_group_keys[${index}].group_key`, 100)));
  const readyGroups: JsonArray = []; const blockedGroups: JsonArray = [];
  for (const group of connectedGroups(packageValue)) {
    const groupKey = text(group.group_key, "dependency_groups[].group_key", 100); const keys = Array.isArray(group.element_keys) ? group.element_keys.map((value, index) => text(value, `${groupKey}.element_keys[${index}]`, 100)) : [];
    if (keys.length < 1) throw new ModelTransferError("TransferInvalid", `Dependency group ${groupKey} has no elements.`);
    const reasons: string[] = []; const routes: JsonArray = []; const equipment: JsonArray = [];
    for (const key of keys) { const element = elements.get(key); if (!element) reasons.push(`missing source element ${key}`); else if (finite(element.external_physical_connection_count ?? 0, `${key}.external_physical_connection_count`) > 0) reasons.push(`${key} has physical connections outside the extracted scope; V1 blocks the whole group rather than treating an incomplete network as independent`); }
    const physicalCount = finite(group.physical_connection_count ?? 0, `${groupKey}.physical_connection_count`);
    if (reasons.length === 0 && physicalCount > 0) {
      try { routes.push(normalizeElbowGroup(groupKey, keys, elements, packageValue, mapping, resources)); }
      catch (error) { reasons.push(error instanceof Error ? error.message : String(error)); }
    } else if (physicalCount === 0) {
      for (const key of keys) {
        const element = elements.get(key); if (!element) continue;
        try {
          if (element.kind === "route") routes.push(normalizeRoute(element, mapping, resources, String(packageValue.package_id)));
          else if (element.kind === "equipment") equipment.push(normalizeEquipment(element, mapping, resources, String(packageValue.package_id)));
          else reasons.push(`${key} is ${String(element.kind)}; fittings, fabrication, hosted Families and unknown classes require a separately certified adapter`);
        } catch (error) { reasons.push(error instanceof Error ? error.message : String(error)); }
      }
    }
    if (approved && !approved.has(groupKey)) reasons.push("dependency group was not explicitly approved for this transfer");
    if (reasons.length > 0) blockedGroups.push({ group_key: groupKey, element_keys: keys, status: "blocked", reasons });
    else readyGroups.push({ group_key: groupKey, element_keys: keys, status: "ready_for_preview", routes, equipment });
  }
  if (readyGroups.length === 0) throw new ModelTransferError("TransferBlocked", "No complete independent dependency group is eligible; inspect blocked_groups and provide missing certified mappings instead of forcing a partial network.");
  const operations: JsonArray = [];
  for (const group of readyGroups) {
    const routes = entries(group.routes, "group.routes"); const equipment = entries(group.equipment, "group.equipment");
    if (routes.length > 0) operations.push({ operation: "model_create_batch", arguments: { routes } });
    if (equipment.length > 0) operations.push({ operation: "mep_place_equipment_batch", arguments: { instances: equipment } });
  }
  const toleranceMm = args.tolerance_mm === undefined ? 0.1 : finite(args.tolerance_mm, "tolerance_mm", true); if (toleranceMm > 0.1) throw new ModelTransferError("TransferInvalid", "V1 tolerance_mm cannot exceed 0.1 mm.");
  const core = { schema_version: "1.0", record_kind: "model_transfer_plan_v1", package_id: packageValue.package_id, package_sha256: packageValue.package_sha256, catalog_id: catalog.catalog_id, catalog_sha256: catalog.catalog_sha256, source_document_fingerprint: source.document_fingerprint, destination_document_fingerprint: destination.document_fingerprint, source_revit_version: "2025", target_revit_version: "2023", tolerance_mm: toleranceMm, mapping: [...mapping.entries()].map(([source_key, value]) => ({ source_key, kind: value.kind, destination_id: value.destination_id })), ready_groups: readyGroups, blocked_groups: blockedGroups, changeset_template: { name: `DSCons native MEPF transfer ${String(packageValue.package_id).slice(-12)}`, operations }, boundary: "Experimental native reconstruction. No IFC/DirectShape fallback, no automatic repair, no Save/Sync, and no claim of system calculation/circuit/panel parity." };
  const plan = { ...core, plan_id: id("transfer-plan", core), plan_sha256: hash(core), persisted_local_only: true };
  return keep(plans, "plan", String(plan.plan_id), plan);
}

export function prepareModelTransferPreview(raw: unknown): Json {
  const args = object(raw, "model_transfer_preview"); const plan = read(plans, "plan", text(args.plan_id, "plan_id", 96)); const contextId = text(args.context_id, "context_id", 160);
  if (args.destination_staging_confirmed !== true) throw new ModelTransferError("TransferBlocked", "destination_staging_confirmed=true is required immediately before preview.");
  const template = object(plan.changeset_template, "plan.changeset_template"); return { plan, bridge_arguments: { context_id: contextId, name: template.name, operations: template.operations }, plan_sha256: plan.plan_sha256 };
}

export function recordModelTransferPreview(raw: unknown): Json {
  const value = object(raw, "model transfer preview"); const plan = object(value.plan, "model transfer preview.plan"); const bridge = object(value.bridge_preview, "model transfer preview.bridge_preview"); const previewId = text(bridge.preview_id, "bridge preview.preview_id", 160);
  if (bridge.model_changed !== false || bridge.validation_level !== "revit_transactiongroup_rollback") throw new ModelTransferError("TransferConflict", "Transfer preview did not prove TransactionGroup rollback cleanliness.");
  const expiresAt = Date.parse(text(bridge.expires_at_utc, "bridge preview.expires_at_utc", 80)); if (!Number.isFinite(expiresAt) || expiresAt <= Date.now()) throw new ModelTransferError("TransferConflict", "Transfer preview is already expired or has an invalid expiry.");
  const validation = object(bridge.validation, "bridge preview.validation"); if (validation.status !== "passed") throw new ModelTransferError("TransferConflict", "Transfer preview did not report a passed Revit rollback simulation.");
  const requiresNativeFittingCheck = entries(plan.ready_groups, "plan.ready_groups").some((group) => entries(group.routes, "group.routes").some((route) => route.transfer_shape === "orthogonal_elbow_chain")); let semanticPreview: Json | undefined;
  if (requiresNativeFittingCheck) {
    const simulated = object(validation.simulated_result, "preview validation.simulated_result"); const verification = object(validation.simulated_verification, "preview validation.simulated_verification"); const previewApplied = { ...simulated, verification };
    let mapped: JsonArray; try { mapped = expectedTargets(plan, previewApplied); } catch (error) { throw new ModelTransferError("TransferConflict", `Rollback Preview cannot map every native elbow-chain element: ${error instanceof Error ? error.message : String(error)}`); }
    const findings = postCommitFindings(plan, previewApplied, mapped); if (findings.length > 0) throw new ModelTransferError("TransferConflict", `Rollback Preview native elbow verification failed: ${findings.map((item) => String(item.reason ?? "mismatch")).join("; ")}`);
    semanticPreview = { verified: true, mapped_element_count: mapped.length, fitting_count: mapped.filter((item) => item.kind === "fitting").length };
  }
  const record = { record_kind: "model_transfer_preview_v1", schema_version: "1.0", transfer_preview_id: id("transfer-preview", { plan_id: plan.plan_id, bridge_preview_id: previewId, plan_sha256: plan.plan_sha256 }), plan_id: plan.plan_id, plan_sha256: plan.plan_sha256, bridge_preview_id: previewId, expires_at_utc: bridge.expires_at_utc, validation: bridge.validation, semantic_preview_verification: semanticPreview, model_changed: false };
  return keep(previews, "preview", String(record.transfer_preview_id), record);
}

function operationCount(result: Json, operation: string): number {
  return entries(result.operation_results, "apply.operation_results").filter((item) => item.operation === operation).reduce((total, item) => total + (operation === "model_create_batch" ? Number(object(item.result, "operation result").route_count ?? 0) : Number(object(item.result, "operation result").instance_count ?? 0)), 0);
}
function expectedTargets(plan: Json, applied: Json): JsonArray {
  const expectedRoutes = new Map<string, Json>(); const expectedEquipment = new Map<string, Json>();
  for (const group of entries(plan.ready_groups, "plan.ready_groups")) {
    for (const route of entries(group.routes, "plan.ready_groups.routes")) expectedRoutes.set(text(route.name, "route.name", 100), route);
    for (const equipment of entries(group.equipment, "plan.ready_groups.equipment")) expectedEquipment.set(text(equipment.key, "equipment.key", 100), equipment);
  }
  const mapped: JsonArray = [];
  for (const operation of entries(applied.operation_results, "apply.operation_results")) {
    const result = object(operation.result, "apply operation result");
    if (operation.operation === "model_create_batch") for (const item of entries(result.routes, "model_create_batch.routes")) {
      const key = text(item.name, "model_create_batch.routes[].name", 100); const expected = expectedRoutes.get(key); const created = object(item.result, "model_create_batch.routes[].result"); const ids = created.created_curve_ids;
      if (!expected || !Array.isArray(ids)) throw new ModelTransferError("TransferConflict", `Route ${key} has no exact destination mapping.`);
      if (expected.transfer_shape === "orthogonal_elbow_chain") {
        const segments = entries(expected.source_segments, `${key}.source_segments`); const fitting = object(expected.source_fitting, `${key}.source_fitting`); const fittingIds = created.created_fitting_ids;
        if (segments.length !== 2 || ids.length !== segments.length || !Array.isArray(fittingIds) || fittingIds.length !== 1) throw new ModelTransferError("TransferConflict", `Elbow chain ${key} did not create exactly two route legs and one native fitting.`);
        segments.forEach((segment, index) => mapped.push({ ...segment, target_element_id: integer(ids[index], `Elbow chain ${key} route target id`), kind: "route" }));
        mapped.push({ ...fitting, target_element_id: integer(fittingIds[0], `Elbow chain ${key} fitting target id`), kind: "fitting" });
      } else {
        if (ids.length !== 1) throw new ModelTransferError("TransferConflict", `Route ${key} has no exact one-curve destination mapping.`);
        mapped.push({ source_key: key, target_element_id: integer(ids[0], `Route ${key} target id`), kind: "route", type_id: expected.type_id, level_id: expected.level_id, system_type_id: expected.system_type_id, points_mm: expected.points, diameter_mm: expected.diameter_mm, width_mm: expected.width_mm, height_mm: expected.height_mm, provenance_marker: expected.provenance_marker, expected_open_connector_count: 2, expected_physical_connector_count: 2, expected_connected_source_keys: [] });
      }
    }
    if (operation.operation === "mep_place_equipment_batch") for (const item of entries(result.placement_read_back, "mep_place_equipment_batch.placement_read_back")) {
      const key = text(item.key, "mep_place_equipment_batch.placement_read_back[].key", 100); const expected = expectedEquipment.get(key); if (!expected) throw new ModelTransferError("TransferConflict", `Equipment ${key} has no destination mapping.`);
      mapped.push({ source_key: key, target_element_id: integer(item.element_id, `Equipment ${key} target id`), kind: "equipment", type_id: expected.symbol_id, level_id: expected.level_id, point_mm: expected.point_mm, rotation_degrees: expected.rotation_degrees, provenance_marker: expected.provenance_marker });
    }
  }
  const expectedNativeCount = [...expectedRoutes.values()].reduce((total, route) => total + (route.transfer_shape === "orthogonal_elbow_chain" ? entries(route.source_segments, "route.source_segments").length + 1 : 1), 0) + expectedEquipment.size;
  if (mapped.length !== expectedNativeCount) throw new ModelTransferError("TransferConflict", "Post-commit results cannot be mapped one-to-one to every planned native element.");
  const bySource = new Map(mapped.map((item) => [text(item.source_key, "mapped source_key", 100), integer(item.target_element_id, "mapped target id")]));
  for (const item of mapped) {
    const peerKeys = Array.isArray(item.expected_connected_source_keys) ? item.expected_connected_source_keys.map((value, index) => text(value, `expected_connected_source_keys[${index}]`, 100)) : [];
    item.expected_connected_target_element_ids = peerKeys.map((source) => { const target = bySource.get(source); if (!target) throw new ModelTransferError("TransferConflict", `Mapped physical peer ${source} is missing.`); return target; });
  }
  return mapped;
}

function closeNumber(left: unknown, right: unknown, tolerance: number): boolean { return Number.isFinite(Number(left)) && Number.isFinite(Number(right)) && Math.abs(Number(left) - Number(right)) <= tolerance; }
function pointsEqual(left: Json, right: Json, tolerance: number): boolean { return ["x_mm", "y_mm", "z_mm"].every((axis) => closeNumber(left[axis], right[axis], tolerance)); }
function centerlineEqual(actual: JsonArray, expected: JsonArray, tolerance: number): boolean {
  if (actual.length !== 2 || expected.length !== 2) return false;
  return (pointsEqual(actual[0], expected[0], tolerance) && pointsEqual(actual[1], expected[1], tolerance)) || (pointsEqual(actual[0], expected[1], tolerance) && pointsEqual(actual[1], expected[0], tolerance));
}
function setEqual(left: Set<number>, right: Set<number>): boolean { return left.size === right.size && [...left].every((value) => right.has(value)); }
function directionAngleDegrees(left: Json, right: Json): number {
  const a = [Number(left.x_mm), Number(left.y_mm), Number(left.z_mm)]; const b = [Number(right.x_mm), Number(right.y_mm), Number(right.z_mm)];
  const aLength = Math.hypot(...a); const bLength = Math.hypot(...b); if (aLength <= 0 || bLength <= 0) return Number.POSITIVE_INFINITY;
  const cosine = Math.max(-1, Math.min(1, a.reduce((sum, value, index) => sum + value * b[index], 0) / (aLength * bLength))); return Math.acos(cosine) * 180 / Math.PI;
}
function connectorSignatureMatches(actual: JsonArray, expected: JsonArray, tolerance: number): boolean {
  if (actual.length !== expected.length) return false; const remaining = [...actual];
  for (const signature of expected) {
    const expectedOrigin = object(signature.origin, "expected connector origin"); let bestIndex = -1; let bestDistance = Number.POSITIVE_INFINITY;
    remaining.forEach((connector, index) => { const origin = connector.origin && typeof connector.origin === "object" ? connector.origin as Json : undefined; if (!origin) return; const distance = distanceMm(origin, expectedOrigin); if (distance < bestDistance) { bestDistance = distance; bestIndex = index; } });
    if (bestIndex < 0 || bestDistance > tolerance) return false; const match = remaining[bestIndex];
    if (match.domain !== signature.domain || match.profile !== signature.profile || match.connector_type !== signature.connector_type) return false;
    if (signature.direction && (!match.direction || directionAngleDegrees(object(match.direction, "actual connector direction"), object(signature.direction, "expected connector direction")) > 0.1)) return false;
    for (const size of ["radius_mm", "width_mm", "height_mm"]) if (signature[size] !== null && signature[size] !== undefined && !closeNumber(match[size], signature[size], tolerance)) return false;
    remaining.splice(bestIndex, 1);
  }
  return true;
}
function boundingBoxMatches(actual: unknown, expected: unknown, tolerance: number): boolean {
  try { const left = object(actual, "actual bounding box"); const right = object(expected, "expected bounding box"); return pointsEqual(object(left.min, "actual bounding box min"), object(right.min, "expected bounding box min"), tolerance) && pointsEqual(object(left.max, "actual bounding box max"), object(right.max, "expected bounding box max"), tolerance); }
  catch { return false; }
}
function postCommitFindings(plan: Json, applied: Json, mapped: JsonArray): JsonArray {
  const findings: JsonArray = []; const tolerance = finite(plan.tolerance_mm, "plan.tolerance_mm", true);
  try {
    const verification = object(applied.verification, "bridge_apply.verification");
    if (verification.verified !== true || verification.mode !== "post_commit_read_back" || verification.operation !== "bim_changeset") findings.push({ status: "mismatch", reason: "Revit post-commit verification metadata is incomplete or belongs to another operation." });
    const details = new Map(entries(verification.elements, "verification.elements").map((item) => [integer(item.id, "verification element id"), item]));
    const networks = new Map(entries(verification.connector_network, "verification.connector_network").map((item) => [integer(item.element_id, "verification connector element id"), item]));
    const mappedIds = new Set(mapped.map((item) => integer(item.target_element_id, "mapped target id")));
    if (details.size !== mapped.length) findings.push({ status: "mismatch", reason: `Post-commit element read-back expected ${mapped.length}, received ${details.size}.` });
    for (const expected of mapped) {
      const targetId = integer(expected.target_element_id, "mapped target id"); const detail = details.get(targetId);
      if (!detail) { findings.push({ status: "mismatch", source_key: expected.source_key, reason: `Target ${targetId} is absent from post-commit read-back.` }); continue; }
      if (Number(detail.type_id) !== Number(expected.type_id) || Number(detail.level_id) !== Number(expected.level_id)) findings.push({ status: "mismatch", source_key: expected.source_key, reason: "Type or Level differs immediately after Apply." });
      if (expected.system_type_id !== undefined && Number(detail.system_type_id) !== Number(expected.system_type_id)) findings.push({ status: "mismatch", source_key: expected.source_key, reason: "System Type differs immediately after Apply." });
      if (detail.comments !== expected.provenance_marker) findings.push({ status: "mismatch", source_key: expected.source_key, reason: "Checksum-bound transfer provenance marker is missing or changed immediately after Apply." });
      if (expected.kind === "route") {
        const actualLine = Array.isArray(detail.centerline_mm) ? array(detail.centerline_mm, "detail.centerline_mm") : [];
        const expectedLine = array(expected.points_mm, "expected.points_mm");
        if (!centerlineEqual(actualLine, expectedLine, tolerance)) findings.push({ status: "mismatch", source_key: expected.source_key, reason: `Route centerline differs by more than ${tolerance} mm immediately after Apply.` });
        for (const size of ["diameter_mm", "width_mm", "height_mm"]) if (expected[size] !== undefined && !closeNumber(detail[size], expected[size], tolerance)) findings.push({ status: "mismatch", source_key: expected.source_key, reason: `Route ${size} differs by more than ${tolerance} mm immediately after Apply.` });
        const connectors = networks.has(targetId) ? entries(object(networks.get(targetId), "connector network").connectors, "connector network.connectors").filter((connector) => new Set(["End", "Curve", "Physical"]).has(String(connector.connector_type))) : [];
        const expectedCount = Number(expected.expected_physical_connector_count ?? 2); const expectedOpen = Number(expected.expected_open_connector_count ?? 2);
        const actualPeers = new Set<number>(); for (const connector of connectors) for (const peer of Array.isArray(connector.connected_element_ids) ? connector.connected_element_ids : []) if (mappedIds.has(Number(peer))) actualPeers.add(Number(peer));
        const expectedPeers = new Set((Array.isArray(expected.expected_connected_target_element_ids) ? expected.expected_connected_target_element_ids : []).map(Number));
        if (connectors.length !== expectedCount || connectors.filter((connector) => connector.is_connected === false).length !== expectedOpen || !setEqual(actualPeers, expectedPeers)) findings.push({ status: "mismatch", source_key: expected.source_key, reason: "Route physical connector/open-end/peer topology differs immediately after Apply." });
      } else if (expected.kind === "fitting") {
        const connectors = networks.has(targetId) ? entries(object(networks.get(targetId), "connector network").connectors, "connector network.connectors").filter((connector) => new Set(["End", "Curve", "Physical"]).has(String(connector.connector_type))) : [];
        const actualPeers = new Set<number>(); for (const connector of connectors) for (const peer of Array.isArray(connector.connected_element_ids) ? connector.connected_element_ids : []) if (mappedIds.has(Number(peer))) actualPeers.add(Number(peer));
        const expectedPeers = new Set((Array.isArray(expected.expected_connected_target_element_ids) ? expected.expected_connected_target_element_ids : []).map(Number));
        if (detail.part_type !== expected.fitting_role || connectors.length !== Number(expected.expected_physical_connector_count ?? 2) || connectors.filter((connector) => connector.is_connected === false).length !== Number(expected.expected_open_connector_count ?? 0) || !setEqual(actualPeers, expectedPeers)) findings.push({ status: "mismatch", source_key: expected.source_key, reason: "Native elbow Part Type or physical connector topology differs immediately after Apply." });
        if (!boundingBoxMatches(detail.bounding_box, expected.expected_bounding_box, tolerance)) findings.push({ status: "mismatch", source_key: expected.source_key, reason: `Native elbow bounding box differs by more than ${tolerance} mm immediately after Apply.` });
        if (!connectorSignatureMatches(connectors, array(expected.expected_connector_signature, "expected fitting connector signature"), tolerance)) findings.push({ status: "mismatch", source_key: expected.source_key, reason: "Native elbow connector origin/direction/profile/size signature differs immediately after Apply." });
      } else if (expected.kind === "equipment") {
        if (!pointsEqual(object(detail.location_point_mm, "detail.location_point_mm"), object(expected.point_mm, "expected.point_mm"), tolerance)) findings.push({ status: "mismatch", source_key: expected.source_key, reason: `Equipment position differs by more than ${tolerance} mm immediately after Apply.` });
        const delta = Math.atan2(Math.sin((Number(detail.rotation_degrees) - Number(expected.rotation_degrees)) * Math.PI / 180), Math.cos((Number(detail.rotation_degrees) - Number(expected.rotation_degrees)) * Math.PI / 180)) * 180 / Math.PI;
        if (!Number.isFinite(delta) || Math.abs(delta) > 0.1) findings.push({ status: "mismatch", source_key: expected.source_key, reason: "Equipment rotation differs by more than 0.1 degrees immediately after Apply." });
      }
    }
  } catch (error) { findings.push({ status: "mismatch", reason: `Post-commit evidence is malformed: ${error instanceof Error ? error.message : String(error)}` }); }
  return findings;
}
export function completeModelTransferApply(raw: unknown): Json {
  const args = object(raw, "model_transfer_apply"); const preview = read(previews, "preview", text(args.transfer_preview_id, "transfer_preview_id", 96));
  if (preview.apply_status === "applied") throw new ModelTransferError("TransferConflict", "Transfer preview was already applied; one-time Apply cannot be replayed.");
  const expiresAt = Date.parse(String(preview.expires_at_utc ?? "")); if (!Number.isFinite(expiresAt) || expiresAt <= Date.now()) throw new ModelTransferError("TransferConflict", "Transfer preview expired; run a fresh rollback Preview.");
  const plan = read(plans, "plan", text(preview.plan_id, "preview.plan_id", 96)); const applied = object(args.bridge_apply, "bridge_apply"); const ready = entries(plan.ready_groups, "plan.ready_groups");
  const expectedRoutes = ready.reduce((total, group) => total + entries(group.routes, "group.routes").length, 0); const expectedEquipment = ready.reduce((total, group) => total + entries(group.equipment, "group.equipment").length, 0);
  const actualRoutes = operationCount(applied, "model_create_batch"); const actualEquipment = operationCount(applied, "mep_place_equipment_batch"); const verification = object(applied.verification, "bridge_apply.verification");
  const findings: JsonArray = [];
  if (actualRoutes !== expectedRoutes) findings.push({ status: "mismatch", reason: `Route count expected ${expectedRoutes}, received ${actualRoutes}.` });
  if (actualEquipment !== expectedEquipment) findings.push({ status: "mismatch", reason: `Equipment count expected ${expectedEquipment}, received ${actualEquipment}.` });
  let idMapping: JsonArray = [];
  try { idMapping = expectedTargets(plan, applied); } catch (error) { findings.push({ status: "mismatch", reason: `Applied elements cannot be mapped one-to-one: ${error instanceof Error ? error.message : String(error)}` }); }
  if (idMapping.length > 0) findings.push(...postCommitFindings(plan, applied, idMapping));
  const reportCore = { schema_version: "1.0", record_kind: "model_transfer_report_v1", plan_id: plan.plan_id, plan_sha256: plan.plan_sha256, package_id: plan.package_id, target_revit_version: "2023", status: findings.length === 0 ? "verified_after_apply_reopen_pending" : "mismatch_after_apply", transferred_groups: findings.length === 0 ? ready.map((group) => group.group_key) : [], blocked_groups: plan.blocked_groups, expected: { routes: expectedRoutes, equipment: expectedEquipment }, observed: { routes: actualRoutes, equipment: actualEquipment, target_element_ids: Array.isArray(applied.created_element_ids) ? applied.created_element_ids : [] }, source_to_destination: idMapping, post_commit_verification: verification, findings, reopen_status: findings.length === 0 ? "pending_user_save_and_reopen" : "blocked_by_post_commit_mismatch", unverified_layers: ["system_calculation", "electrical_circuit_and_panel", "hosted_or_nested_family_behavior", "fabrication_parts", "views_tags_dimensions_sheets_groups_phases_worksharing", "reopen_persistence"], boundary: "This report does not certify file persistence. Save a separate staging target without overwriting the source, reopen it in Revit 2023, then run the read-only reopen comparison before claiming final success." };
  const report = { ...reportCore, report_id: id("transfer-report", reportCore), report_sha256: hash(reportCore), persisted_local_only: true };
  keep(previews, "preview", String(preview.transfer_preview_id), { ...preview, apply_status: "applied", report_id: report.report_id });
  return keep(reports, "report", String(report.report_id), report);
}

export function completeModelTransferReopenVerification(raw: unknown): Json {
  const args = object(raw, "model_transfer_reopen_verify"); const report = read(reports, "report", text(args.report_id, "report_id", 96)); const bridge = object(args.bridge_reopen, "bridge_reopen");
  if (bridge.verified !== true || bridge.model_changed !== false) throw new ModelTransferError("TransferConflict", "Reopen verification did not return a clean independent Revit read-back.");
  const expected = entries(report.source_to_destination, "report.source_to_destination"); const actual = entries(bridge.elements, "reopen.elements");
  if (report.status !== "verified_after_apply_reopen_pending") throw new ModelTransferError("TransferConflict", "Reopen verification is blocked because Apply did not finish with an exact post-commit match.");
  if (actual.length !== expected.length) throw new ModelTransferError("TransferConflict", "Reopen read-back element count differs from the one-to-one source-to-destination mapping.");
  const expectedKeys = new Map(expected.map((item) => [text(item.source_key, "expected source_key", 100), integer(item.target_element_id, "expected target_element_id")])); const actualKeys = new Map<string, number>();
  for (const item of actual) { const key = text(item.source_key, "reopen source_key", 100); if (actualKeys.has(key)) throw new ModelTransferError("TransferConflict", `Reopen read-back duplicated ${key}.`); actualKeys.set(key, integer(item.target_element_id, "reopen target_element_id")); }
  for (const [key, targetId] of expectedKeys) if (actualKeys.get(key) !== targetId) throw new ModelTransferError("TransferConflict", `Reopen read-back identity differs for ${key}.`);
  const layers = Array.isArray(report.unverified_layers) ? report.unverified_layers.filter((item): item is string => typeof item === "string") : [];
  const core = { ...report, status: report.status === "verified_after_apply_reopen_pending" ? "verified_after_reopen" : "reopen_checked_with_prior_mismatch", reopen_status: "verified", reopen_verification: bridge, unverified_layers: layers.filter((item) => item !== "reopen_persistence") };
  const updated = { ...core, report_id: String(report.report_id), report_sha256: hash({ ...core, report_id: undefined, report_sha256: undefined }), persisted_local_only: true };
  return keep(reports, "report", String(updated.report_id), updated);
}

export function readModelTransferPlan(planId: string): Json { return read(plans, "plan", planId); }
export function readModelTransferPreview(previewId: string): Json { return read(previews, "preview", previewId); }
export function readModelTransferReport(reportId: string): Json { return read(reports, "report", reportId); }
