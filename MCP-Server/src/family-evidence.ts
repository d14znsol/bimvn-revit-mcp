import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, realpathSync, renameSync, statSync, unlinkSync, writeFileSync } from "node:fs";
import { readFile, realpath } from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";
import { createCanvas, loadImage } from "@napi-rs/canvas";
import { getDocument, OPS } from "pdfjs-dist/legacy/build/pdf.mjs";
import { createWorker } from "tesseract.js";
import { validateFamilyBlueprint } from "./family-blueprint.js";

export class FamilyEvidenceError extends Error {
  constructor(public readonly code: "PathBlocked" | "SourceUnreadable" | "SourceEncrypted" | "OcrUnavailable" | "EvidenceInvalid" | "EvidenceConflict", message: string) { super(message); }
}

type Json = Record<string, unknown>;
const RECORD_TTL_MS = 30 * 24 * 60 * 60 * 1000;
const MAX_BYTES = 80 * 1024 * 1024;
const MAX_PAGES = 60;
const MAX_PIXELS = 18_000_000;
const MAX_PDF_VECTOR_PATHS_PER_PAGE = 5_000;
const MAX_PDF_VECTOR_COMMANDS_PER_PAGE = 50_000;
const MAX_IES_BYTES = 5 * 1024 * 1024;
const MAX_APPEARANCE_IMAGE_BYTES = 20 * 1024 * 1024;
// PDF.js needs its bundled standard-font program when it rasterizes a page
// for the bounded OCR/review path. Resolve it from the installed dependency
// beside the emitted server rather than relying on a system font, a browser
// fetch, or a network URL. The trailing separator is required because PDF.js
// appends the font file name itself.
const pdfStandardFontsDirectory = `${path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "node_modules", "pdfjs-dist", "standard_fonts")}${path.sep}`;
const records = new Map<string, { expiresAt: number; value: Json }>();

function sha256(data: Uint8Array): string { return createHash("sha256").update(data).digest("hex"); }
function id(prefix: string, ...values: string[]): string { return `${prefix}-${sha256(Buffer.from(values.join("|"))).slice(0, 24)}`; }
function clone<T>(value: T): T { return value === undefined ? value : JSON.parse(JSON.stringify(value)) as T; }
function sourceBlockSha256(page: number, index: number, block: Json): string {
  // This fingerprint deliberately binds the exact immutable SourceEvidence
  // page/block content without returning that content with a FamilySpec.
  const payload = {
    page, index, raw_text: String(block.raw_text ?? ""), bbox: block.bbox ?? null,
    confidence: block.confidence ?? null, extraction_method: String(block.extraction_method ?? ""),
  };
  return sha256(Buffer.from(JSON.stringify(payload), "utf8"));
}
function recordDirectory(): string | null {
  if (process.env.DSCONS_FAMILY_RECORDS_DISABLE === "1") return null;
  if (process.env.DSCONS_FAMILY_RECORD_DIRECTORY) return path.resolve(process.env.DSCONS_FAMILY_RECORD_DIRECTORY);
  const local = process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local");
  return path.join(local, "DSCons", "RevitMcp", "family-records");
}
function recordId(record: Json): string { return String(record.record_id ?? record.evidence_id ?? record.artifact_id ?? record.spec_id); }
function artifactRecordId(specId: string): string { return `artifact-${sha256(Buffer.from(specId, "utf8")).slice(0, 24)}`; }
function recordFile(id: string): string | null {
  if (!/^[a-z0-9-]{8,96}$/i.test(id)) throw new FamilyEvidenceError("EvidenceInvalid", "Evidence/spec id has an invalid format.");
  const directory = recordDirectory(); return directory ? path.join(directory, `${id}.json`) : null;
}
function persist(record: Json, expiresAt: number): void {
  const id = recordId(record); const target = recordFile(id); if (!target) return;
  const directory = path.dirname(target); mkdirSync(directory, { recursive: true });
  const canonicalValue = JSON.stringify(record);
  const envelope = { format: "dscons_family_record_v1", id, expires_at_utc: new Date(expiresAt).toISOString(), value_sha256: sha256(Buffer.from(canonicalValue, "utf8")), value: record };
  const payload = JSON.stringify(envelope, null, 2); const temporary = path.join(directory, `.${id}-${process.pid}-${Date.now()}.tmp`);
  try { writeFileSync(temporary, payload, { encoding: "utf8", flag: "wx" }); renameSync(temporary, target); }
  finally { if (existsSync(temporary)) try { unlinkSync(temporary); } catch { /* best effort */ } }
}
function keep(record: Json): Json {
  const expiresAt = Date.now() + RECORD_TTL_MS; const id = recordId(record);
  records.set(id, { expiresAt, value: clone(record) }); persist(record, expiresAt); return record;
}
export function readEvidenceRecord(recordId: string): Json {
  let found = records.get(recordId);
  if (!found) {
    const source = recordFile(recordId);
    if (source && existsSync(source)) {
      try {
        const envelope = JSON.parse(readFileSync(source, "utf8")) as { format?: string; id?: string; expires_at_utc?: string; value_sha256?: string; value?: Json };
        const expiresAt = Date.parse(String(envelope.expires_at_utc ?? ""));
        const actualChecksum = envelope.value ? sha256(Buffer.from(JSON.stringify(envelope.value), "utf8")) : "";
        if (envelope.format !== "dscons_family_record_v1" || envelope.id !== recordId || !envelope.value || envelope.value_sha256 !== actualChecksum || !Number.isFinite(expiresAt)) throw new Error("invalid record envelope");
        found = { expiresAt, value: envelope.value }; records.set(recordId, found);
      } catch { throw new FamilyEvidenceError("EvidenceInvalid", "Persisted Family evidence/spec is unreadable or has an invalid envelope."); }
    }
  }
  if (!found || found.expiresAt < Date.now()) throw new FamilyEvidenceError("EvidenceInvalid", "Evidence/spec id is unknown or expired; inspect the source again.");
  return clone(found.value);
}

/**
 * Stores a small, local-only evidence/governance record using the same
 * checksum-protected envelope as SourceEvidence and FamilySpec records.  The
 * caller owns the record schema; this boundary only accepts a stable id and
 * never serializes credentials or remote responses.
 */
export function recordLocalEvidenceRecord(record: Json): Json {
  const id = recordId(record);
  if (!/^[a-z0-9-]{8,96}$/i.test(id)) throw new FamilyEvidenceError("EvidenceInvalid", "Local evidence record requires a valid record_id.");
  return keep(clone(record));
}

/**
 * An artifact becomes eligible for nesting only after its post-reopen read-back
 * proves the parameter interface declared by its Blueprint.  Persisting this
 * small, value-free interface prevents a parent from associating parameters by
 * display name alone (which is unsafe for Shared Parameters).
 */
function verifiedParameterInterfaces(blueprint: Json, reopenedFamily: Json | undefined): Json {
  const declarations = Array.isArray(blueprint.parameters) ? blueprint.parameters as Json[] : [];
  const observed = Array.isArray(reopenedFamily?.parameters) ? reopenedFamily!.parameters as Json[] : [];
  const byName = new Map<string, Json>();
  for (const item of observed) {
    const name = String(item.name ?? "");
    if (!name || byName.has(name)) throw new FamilyEvidenceError("EvidenceConflict", "Revit reopen read-back has a missing or duplicate Family parameter name.");
    byName.set(name, item);
  }
  const interfaces: Json = {};
  for (const declaration of declarations) {
    const key = String(declaration.key ?? ""); const name = String(declaration.name ?? "");
    const dataType = String(declaration.data_type ?? ""); const scope = String(declaration.scope ?? "");
    const actual = byName.get(name);
    if (!key || !name || !dataType || (scope !== "type" && scope !== "instance") || !actual)
      throw new FamilyEvidenceError("EvidenceConflict", `Revit reopen read-back is missing declared Family parameter ${name || key || "(unnamed)"}.`);
    const expectedSharedGuid = typeof declaration.shared_guid === "string" ? declaration.shared_guid.toLowerCase() : undefined;
    const actualShared = actual.is_shared === true;
    const actualGuid = actual.shared_guid === null || actual.shared_guid === undefined ? undefined : String(actual.shared_guid).toLowerCase();
    if (actual.is_instance !== (scope === "instance")) throw new FamilyEvidenceError("EvidenceConflict", `Revit reopen parameter scope mismatch for ${name}.`);
    if (expectedSharedGuid) {
      if (!actualShared || actualGuid !== expectedSharedGuid) throw new FamilyEvidenceError("EvidenceConflict", `Revit reopen Shared GUID mismatch for ${name}.`);
    } else if (actualShared || actualGuid) throw new FamilyEvidenceError("EvidenceConflict", `Revit reopen parameter ${name} unexpectedly became shared.`);
    interfaces[key] = { name, data_type: dataType, scope, is_shared: actualShared, shared_guid: expectedSharedGuid ?? null, verified: true };
  }
  return interfaces;
}

export function recordFamilyBuildArtifact(specId: string, result: Json): Json {
  const spec = readEvidenceRecord(specId);
  if (spec.record_kind !== "family_spec_v3" || spec.status !== "human_confirmed") throw new FamilyEvidenceError("EvidenceInvalid", "A verified FamilySpec v3 is required before recording a nested artifact.");
  if (String(result.spec_id ?? "") !== specId || result.schema_version !== "3.0") throw new FamilyEvidenceError("EvidenceConflict", "Family build result does not match its immutable FamilySpec v3.");
  const familyPath = String(result.family_path ?? ""); const reportedChecksum = String(result.sha256 ?? "");
  if (!path.isAbsolute(familyPath) || !existsSync(familyPath)) throw new FamilyEvidenceError("SourceUnreadable", "Built Family artifact is missing and cannot be registered for nesting.");
  const builtBytes = readFileSync(familyPath);
  const actualChecksum = createHash("sha256").update(builtBytes).digest("base64");
  if (actualChecksum !== reportedChecksum) throw new FamilyEvidenceError("EvidenceConflict", "Built Family checksum does not match the Revit read-back result.");
  const blueprint = spec.blueprint as Json; const blueprintHash = String(blueprint.blueprint_hash ?? "");
  if (!blueprintHash || String(result.blueprint_hash ?? "") !== blueprintHash) throw new FamilyEvidenceError("EvidenceConflict", "Built Family blueprint hash does not match its FamilySpec.");
  const created = result.created as Json | undefined;
  const verification = result.verification as Json | undefined; const reopenedFamily = verification?.family as Json | undefined; const hosting = reopenedFamily?.hosting as Json | undefined;
  const familyPlacementType = String(hosting?.family_placement_type ?? "");
  if (verification?.verified !== true || verification.mode !== "post_commit_reopen_read_back" || hosting?.verified_from_revit !== true || !familyPlacementType)
    throw new FamilyEvidenceError("EvidenceConflict", "Built Family artifact requires Revit reopen read-back of its actual FamilyPlacementType.");
  const parameterInterfaces = verifiedParameterInterfaces(blueprint, reopenedFamily);
  const templateBehavior = String((blueprint.family as Json).template_behavior ?? "");
  const resultComplexity = result.complexity as Json | undefined;
  const resolvedComplexity = blueprint.resolved_complexity as Json | undefined;
  const declaredMetrics = (blueprint.complexity_assessment as Json | undefined)?.metrics as Json | undefined;
  const publicationBytes = Number((result.publication as Json | undefined)?.rfa_bytes);
  const artifactComplexity: Json = {
    nested_depth: Number(resultComplexity?.nested_depth),
    local_complexity_score: Number(declaredMetrics?.complexity_score ?? 0),
    aggregate_complexity_score: Number(resultComplexity?.aggregate_complexity_score),
    rfa_bytes: Number(resultComplexity?.rfa_bytes),
    within_budget: resultComplexity?.within_budget === true,
  };
  if (!resultComplexity || resultComplexity.rfa_size_measured !== true || !Number.isInteger(artifactComplexity.nested_depth) || Number(artifactComplexity.nested_depth) < 0 || artifactComplexity.within_budget !== true || !Number.isFinite(Number(artifactComplexity.aggregate_complexity_score)) || Number(artifactComplexity.aggregate_complexity_score) < 0 || !Number.isInteger(Number(artifactComplexity.rfa_bytes)) || Number(artifactComplexity.rfa_bytes) !== builtBytes.length || publicationBytes !== builtBytes.length || Number(resultComplexity.nested_depth) !== Number(resolvedComplexity?.nested_depth) || Number(resultComplexity.aggregate_complexity_score) !== Number(resolvedComplexity?.aggregate_complexity_score))
    throw new FamilyEvidenceError("EvidenceConflict", "Built Family artifact has invalid or failed complexity evidence.");
  const artifact: Json = {
    record_kind: "family_build_artifact_v3", artifact_id: artifactRecordId(specId), spec_id: specId,
    family_kind: spec.family_kind, category: (blueprint.family as Json).category, template_behavior: templateBehavior, family_placement_type: familyPlacementType, shared: (blueprint.family as Json).shared === true,
    family_path: familyPath, sha256: actualChecksum, blueprint_hash: blueprintHash,
    type_names: Array.isArray(created?.types) ? created.types : [], parameter_interfaces: parameterInterfaces, complexity: artifactComplexity, status: "verified",
  };
  return keep(artifact);
}

function resolveNestedBlueprintArtifacts(blueprint: Json): Json[] {
  const nested = Array.isArray(blueprint.nested_components) ? blueprint.nested_components as Json[] : [];
  const parentParameters = new Map((Array.isArray(blueprint.parameters) ? blueprint.parameters as Json[] : []).map((item) => [String(item.key), item]));
  const citations: Json[] = [];
  let maximumChildDepth = -1;
  let aggregateChildScore = 0;
  const allowedPlacementTypes: Record<string, string> = { level_based: "OneLevelBased", face_based: "WorkPlaneBased", work_plane_based: "WorkPlaneBased", line_based: "CurveBased" };
  const requirePlacementCompatibility = (ownerLabel: string, declaration: Json, evidence: Json): void => {
    const placementMode = String(declaration.placement_mode ?? "level_point"); const templateBehavior = String(evidence.template_behavior ?? ""); const actualPlacement = String(evidence.family_placement_type ?? "");
    const allowedBehaviors = placementMode === "level_point" ? ["level_based"] : placementMode === "host_face_point" ? ["face_based", "work_plane_based"] : placementMode === "host_face_line" ? ["line_based"] : [];
    if (!allowedBehaviors.includes(templateBehavior)) throw new FamilyEvidenceError("EvidenceInvalid", `${ownerLabel} placement_mode ${placementMode} is incompatible with child template_behavior ${templateBehavior || "unknown"}.`);
    if (allowedPlacementTypes[templateBehavior] !== actualPlacement) throw new FamilyEvidenceError("EvidenceConflict", `${ownerLabel} child artifact FamilyPlacementType ${actualPlacement || "unknown"} does not match template_behavior ${templateBehavior}.`);
  };
  const resolveArtifact = (ownerLabel: string, declaration: Json, placementDeclaration: Json): { evidence: Json; childParameters: Map<string, Json>; parameterInterfaces: Map<string, Json>; category: string } => {
    const childSpecId = String(declaration.blueprint_id ?? "");
    const childSpec = readEvidenceRecord(childSpecId);
    if (childSpec.record_kind !== "family_spec_v3" || childSpec.status !== "human_confirmed") throw new FamilyEvidenceError("EvidenceInvalid", `${ownerLabel} must reference a human-confirmed FamilySpec v3.`);
    const childBlueprint = childSpec.blueprint as Json; const childFamily = childBlueprint.family as Json; const templateBehavior = String(childFamily.template_behavior ?? "");
    if (!(templateBehavior in allowedPlacementTypes)) throw new FamilyEvidenceError("EvidenceInvalid", `${ownerLabel} child template_behavior ${templateBehavior || "unknown"} is not supported by the bounded nested placement operations.`);
    const artifact = readEvidenceRecord(artifactRecordId(childSpecId));
    if (artifact.record_kind !== "family_build_artifact_v3" || artifact.status !== "verified" || artifact.spec_id !== childSpecId) throw new FamilyEvidenceError("EvidenceInvalid", `${ownerLabel} has no verified Blueprint-built artifact; build/apply the child Family first.`);
    const familyPath = String(artifact.family_path ?? "");
    if (!path.isAbsolute(familyPath) || !existsSync(familyPath)) throw new FamilyEvidenceError("SourceUnreadable", `${ownerLabel} artifact is missing.`);
    const actualChecksum = createHash("sha256").update(readFileSync(familyPath)).digest("base64");
    if (actualChecksum !== artifact.sha256 || artifact.blueprint_hash !== childBlueprint.blueprint_hash) throw new FamilyEvidenceError("EvidenceConflict", `${ownerLabel} artifact changed or no longer matches its child Blueprint.`);
    const requestedSharing = String(declaration.sharing ?? "embedded");
    if (requestedSharing === "shared" && artifact.shared !== true) throw new FamilyEvidenceError("EvidenceInvalid", `${ownerLabel} requests shared behavior but the child Blueprint artifact is not marked Shared.`);
    if (requestedSharing === "embedded" && artifact.shared === true) throw new FamilyEvidenceError("EvidenceInvalid", `${ownerLabel} requests embedded behavior but the child Blueprint artifact is marked Shared.`);
    const typeName = String(declaration.type_name ?? ""); const typeNames = Array.isArray(artifact.type_names) ? artifact.type_names.map(String) : [];
    if (!typeNames.includes(typeName)) throw new FamilyEvidenceError("EvidenceInvalid", `${ownerLabel} requests unknown child type ${typeName}.`);
    const category = String(childFamily.category ?? "");
    if (artifact.category !== category) throw new FamilyEvidenceError("EvidenceConflict", `${ownerLabel} artifact category no longer matches its child Blueprint.`);
    if (artifact.template_behavior !== templateBehavior) throw new FamilyEvidenceError("EvidenceConflict", `${ownerLabel} artifact template_behavior no longer matches its child Blueprint.`);
    const complexity = artifact.complexity as Json | undefined;
    const childDepth = Number(complexity?.nested_depth);
    const childScore = Number(complexity?.aggregate_complexity_score);
    if (!complexity || complexity.within_budget !== true || !Number.isInteger(childDepth) || childDepth < 0 || !Number.isFinite(childScore) || childScore < 0)
      throw new FamilyEvidenceError("EvidenceConflict", `${ownerLabel} artifact has no valid complexity-budget evidence.`);
    const childParameters = new Map((Array.isArray(childBlueprint.parameters) ? childBlueprint.parameters as Json[] : []).map((item) => [String(item.key), item]));
    const parameterInterfaces = new Map<string, Json>(); const recordedInterfaces = objectValue(artifact.parameter_interfaces, `${ownerLabel} artifact parameter_interfaces`);
    for (const [parameterKey, childParameter] of childParameters) {
      const parameterInterface = objectValue(recordedInterfaces[parameterKey], `${ownerLabel} artifact parameter_interfaces.${parameterKey}`);
      const expectedName = String(childParameter.name ?? ""); const expectedDataType = String(childParameter.data_type ?? ""); const expectedScope = String(childParameter.scope ?? "");
      const expectedGuid = typeof childParameter.shared_guid === "string" ? childParameter.shared_guid.toLowerCase() : null;
      if (parameterInterface.name !== expectedName || parameterInterface.data_type !== expectedDataType || parameterInterface.scope !== expectedScope || parameterInterface.verified !== true
        || parameterInterface.is_shared !== (expectedGuid !== null) || (parameterInterface.shared_guid ?? null) !== expectedGuid)
        throw new FamilyEvidenceError("EvidenceConflict", `${ownerLabel} artifact parameter interface does not match its verified child Blueprint parameter ${parameterKey}.`);
      parameterInterfaces.set(parameterKey, parameterInterface);
    }
    maximumChildDepth = Math.max(maximumChildDepth, childDepth);
    aggregateChildScore += childScore;
    const placementEvidence = { template_behavior: templateBehavior, family_placement_type: artifact.family_placement_type };
    requirePlacementCompatibility(ownerLabel, placementDeclaration, placementEvidence);
    citations.push({ spec_id: childSpecId, artifact_id: artifact.artifact_id, sha256: actualChecksum, blueprint_hash: artifact.blueprint_hash, complexity, ...placementEvidence });
    return {
      evidence: { family_path: familyPath, sha256: actualChecksum, blueprint_hash: artifact.blueprint_hash, family_kind: childSpec.family_kind, category, type_name: typeName, shared: artifact.shared === true, complexity, ...placementEvidence },
      childParameters,
      parameterInterfaces,
      category,
    };
  };
  for (const component of nested) {
    const componentKey = String(component.key ?? "");
    const options = Array.isArray(component.type_options) ? component.type_options as Json[] : [];
    if (options.length) {
      const parameterKey = String(component.family_type_parameter_key ?? ""); const parentParameter = parentParameters.get(parameterKey);
      if (!parentParameter || parentParameter.data_type !== "family_type") throw new FamilyEvidenceError("EvidenceInvalid", `Nested component ${componentKey} has no matching family_type parameter.`);
      const resolvedOptions: Json[] = []; let category = "";
      for (const option of options) {
        const optionKey = String(option.key ?? ""); const resolved = resolveArtifact(`Nested component ${componentKey} option ${optionKey}`, option, component);
        if (!category) category = resolved.category;
        else if (category !== resolved.category) throw new FamilyEvidenceError("EvidenceInvalid", `Nested component ${componentKey} options must use the same Revit category.`);
        resolvedOptions.push({ option_key: optionKey, ...resolved.evidence });
      }
      if (String(parentParameter.family_category ?? "") !== category) throw new FamilyEvidenceError("EvidenceInvalid", `Nested component ${componentKey} family_type parameter category must match child category ${category}.`);
      component.resolved_options = resolvedOptions;
      continue;
    }
    const resolved = resolveArtifact(`Nested component ${componentKey}`, component, component);
    const parameterNames: Json = {}; const parameterInterfaces: Json = {}; const parameterMap = component.parameter_map as Json;
    for (const [childKey, parentValue] of Object.entries(parameterMap)) {
      const parentKey = String(parentValue); const childParameter = resolved.childParameters.get(childKey); const parentParameter = parentParameters.get(parentKey);
      if (!childParameter || childParameter.scope !== "instance") throw new FamilyEvidenceError("EvidenceInvalid", `Nested parameter ${componentKey}.${childKey} must reference an Instance parameter in the child Blueprint.`);
      if (!parentParameter || parentParameter.data_type !== childParameter.data_type) throw new FamilyEvidenceError("EvidenceInvalid", `Nested parameter map ${componentKey}.${childKey} requires a parent parameter with matching data_type.`);
      const childInterface = resolved.parameterInterfaces.get(childKey);
      if (!childInterface) throw new FamilyEvidenceError("EvidenceConflict", `Nested component ${componentKey}.${childKey} has no verified child parameter interface.`);
      if (String(component.sharing ?? "embedded") === "shared") {
        const childGuid = typeof childParameter.shared_guid === "string" ? childParameter.shared_guid.toLowerCase() : "";
        const parentGuid = typeof parentParameter.shared_guid === "string" ? parentParameter.shared_guid.toLowerCase() : "";
        if (parentParameter.scope !== "instance" || !childGuid || !parentGuid || childInterface.is_shared !== true || String(childInterface.shared_guid ?? "").toLowerCase() !== childGuid || childGuid !== parentGuid)
          throw new FamilyEvidenceError("EvidenceInvalid", `Nested shared parameter map ${componentKey}.${childKey} requires child and parent Instance Parameters with the same shared_guid.`);
        if (childParameter.name !== parentParameter.name)
          throw new FamilyEvidenceError("EvidenceInvalid", `Nested shared parameter map ${componentKey}.${childKey} requires one Revit Shared definition identity: child and parent names must match as well as shared_guid.`);
      }
      parameterNames[childKey] = childParameter.name;
      parameterInterfaces[childKey] = {
        child: childInterface,
        parent: { key: parentKey, name: parentParameter.name, data_type: parentParameter.data_type, scope: parentParameter.scope, is_shared: typeof parentParameter.shared_guid === "string", shared_guid: typeof parentParameter.shared_guid === "string" ? parentParameter.shared_guid.toLowerCase() : null },
        shared_identity_required: String(component.sharing ?? "embedded") === "shared",
        verified: true,
      };
    }
    component.resolved_artifact = { ...resolved.evidence, parameter_names: parameterNames, parameter_interfaces: parameterInterfaces };
  }
  const nestedDepth = nested.length === 0 ? 0 : maximumChildDepth + 1;
  const budget = blueprint.performance_budget as Json | undefined;
  const maximumDepth = Number(budget?.max_nested_depth ?? 0);
  if (!Number.isInteger(maximumDepth) || nestedDepth > maximumDepth) throw new FamilyEvidenceError("EvidenceInvalid", `Blueprint nested_depth=${nestedDepth} exceeds performance_budget.max_nested_depth=${maximumDepth}.`);
  const localScore = Number(((blueprint.complexity_assessment as Json | undefined)?.metrics as Json | undefined)?.complexity_score ?? 0);
  const aggregateScore = localScore + aggregateChildScore;
  const maximumScore = Number(budget?.max_complexity_score ?? 0);
  if (!Number.isFinite(maximumScore) || aggregateScore > maximumScore) throw new FamilyEvidenceError("EvidenceInvalid", `Blueprint aggregate_complexity_score=${aggregateScore} exceeds performance_budget.max_complexity_score=${maximumScore}.`);
  blueprint.resolved_complexity = { nested_depth: nestedDepth, local_complexity_score: localScore, aggregate_child_complexity_score: aggregateChildScore, aggregate_complexity_score: aggregateScore, score_boundary: "Conservative sum across declared nested choices/instances; this is not measured Revit regeneration time." };
  delete blueprint.blueprint_hash;
  blueprint.blueprint_hash = sha256(Buffer.from(JSON.stringify(blueprint), "utf8"));
  return citations;
}

async function approvedFile(raw: unknown, approvedDirectory: unknown, extension: string | string[]): Promise<string> {
  if (typeof raw !== "string" || typeof approvedDirectory !== "string" || !path.isAbsolute(raw) || !path.isAbsolute(approvedDirectory) || raw.startsWith("\\\\") || approvedDirectory.startsWith("\\\\"))
    throw new FamilyEvidenceError("PathBlocked", "Source and approved_demo_directory must be local absolute paths.");
  const root = await realpath(approvedDirectory).catch(() => { throw new FamilyEvidenceError("PathBlocked", "approved_demo_directory does not exist."); });
  const source = await realpath(raw).catch(() => { throw new FamilyEvidenceError("SourceUnreadable", "Source file does not exist or cannot be read."); });
  const allowedExtensions = Array.isArray(extension) ? extension : [extension];
  if (!allowedExtensions.includes(path.extname(source).toLowerCase())) throw new FamilyEvidenceError("EvidenceInvalid", `Source must use one of these extensions: ${allowedExtensions.join(", ")}.`);
  if (path.relative(root, source).startsWith("..") || path.isAbsolute(path.relative(root, source))) throw new FamilyEvidenceError("PathBlocked", "Source is outside approved_demo_directory.");
  return source;
}

function resolvePhotometricAssets(blueprint: Json, approvedDirectory: unknown): Json[] {
  const lightSource = blueprint.light_source as Json | undefined;
  if (!lightSource || lightSource.distribution_style !== "photometric_web") return [];
  if (typeof approvedDirectory !== "string" || !path.isAbsolute(approvedDirectory) || approvedDirectory.startsWith("\\\\"))
    throw new FamilyEvidenceError("PathBlocked", "approved_demo_directory is required and must be a local absolute path for photometric-web IES evidence.");
  let root: string;
  try { root = realpathSync(approvedDirectory); }
  catch { throw new FamilyEvidenceError("PathBlocked", "approved_demo_directory does not exist for photometric-web IES evidence."); }
  const citations: Json[] = [];
  const seen = new Set<string>();
  for (const rawSetting of lightSource.type_settings as unknown[] ?? []) {
    const setting = rawSetting as Json; const web = setting.photometric_web as Json;
    const fileName = String(web.file_name ?? "");
    const candidate = path.join(root, fileName);
    let source: string;
    try { source = realpathSync(candidate); }
    catch { throw new FamilyEvidenceError("SourceUnreadable", `Photometric web file ${fileName} does not exist or cannot be read.`); }
    const relative = path.relative(root, source);
    if (relative.startsWith("..") || path.isAbsolute(relative) || path.basename(source) !== fileName || path.extname(source).toLowerCase() !== ".ies")
      throw new FamilyEvidenceError("PathBlocked", `Photometric web file ${fileName} must be a direct .ies file inside approved_demo_directory.`);
    const size = statSync(source).size;
    if (size < 16 || size > MAX_IES_BYTES) throw new FamilyEvidenceError("EvidenceInvalid", `Photometric web file ${fileName} must be between 16 bytes and ${MAX_IES_BYTES / 1024 / 1024} MB.`);
    const bytes = readFileSync(source); const actual = sha256(bytes); const expected = String(web.sha256 ?? "").toLowerCase();
    if (actual !== expected) throw new FamilyEvidenceError("EvidenceConflict", `Photometric web file ${fileName} checksum does not match the immutable Blueprint declaration.`);
    const header = bytes.subarray(0, Math.min(bytes.length, 64 * 1024)).toString("ascii");
    if (header.includes("\0") || !/(^|\r?\n)TILT\s*=\s*(NONE|INCLUDE|[^\r\n]+)/i.test(header))
      throw new FamilyEvidenceError("EvidenceInvalid", `Photometric web file ${fileName} is not a recognizable IES LM-63 payload with a TILT record.`);
    const citationKey = `${fileName.toLowerCase()}|${actual}`;
    if (!seen.has(citationKey)) {
      citations.push({ source_kind: "ies_photometric_web", file_name: fileName, sha256: actual, size_bytes: size, path_policy: "direct_child_of_approved_demo_directory" });
      seen.add(citationKey);
    }
  }
  return citations;
}

function imageFormat(bytes: Buffer, extension: string): string | null {
  const png = bytes.length >= 8 && bytes.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
  const jpeg = bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff;
  const bmp = bytes.length >= 2 && bytes[0] === 0x42 && bytes[1] === 0x4d;
  const tiff = bytes.length >= 4 && ((bytes[0] === 0x49 && bytes[1] === 0x49 && bytes[2] === 0x2a && bytes[3] === 0x00) || (bytes[0] === 0x4d && bytes[1] === 0x4d && bytes[2] === 0x00 && bytes[3] === 0x2a));
  if (extension === ".png" && png) return "png";
  if ((extension === ".jpg" || extension === ".jpeg") && jpeg) return "jpeg";
  if (extension === ".bmp" && bmp) return "bmp";
  if ((extension === ".tif" || extension === ".tiff") && tiff) return "tiff";
  return null;
}

function resolveAppearanceAssets(blueprint: Json, approvedDirectory: unknown): Json[] {
  const materials = Array.isArray(blueprint.materials) ? blueprint.materials as Json[] : [];
  const requiresAssets = materials.some((material) => {
    const appearance = material.appearance as Json | undefined;
    return appearance?.texture !== undefined || appearance?.bump !== undefined;
  });
  if (!requiresAssets) return [];
  if (typeof approvedDirectory !== "string" || !path.isAbsolute(approvedDirectory) || approvedDirectory.startsWith("\\\\"))
    throw new FamilyEvidenceError("PathBlocked", "approved_demo_directory is required and must be a local absolute path for material texture/bump evidence.");
  let root: string;
  try { root = realpathSync(approvedDirectory); }
  catch { throw new FamilyEvidenceError("PathBlocked", "approved_demo_directory does not exist for material texture/bump evidence."); }
  const citations: Json[] = [];
  for (const material of materials) {
    const appearance = material.appearance as Json | undefined;
    if (!appearance) continue;
    for (const role of ["texture", "bump"] as const) {
      const bitmap = appearance[role] as Json | undefined;
      if (!bitmap) continue;
      const fileName = String(bitmap.file_name ?? ""); const extension = path.extname(fileName).toLowerCase();
      if (fileName !== path.basename(fileName) || ![".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"].includes(extension))
        throw new FamilyEvidenceError("PathBlocked", `Material ${String(material.key)} ${role} must be an approved image basename inside approved_demo_directory.`);
      const candidate = path.join(root, fileName);
      let source: string;
      try { source = realpathSync(candidate); }
      catch { throw new FamilyEvidenceError("SourceUnreadable", `Material ${String(material.key)} ${role} image ${fileName} does not exist or cannot be read.`); }
      const relative = path.relative(root, source);
      if (relative.startsWith("..") || path.isAbsolute(relative) || path.basename(source) !== fileName || path.extname(source).toLowerCase() !== extension)
        throw new FamilyEvidenceError("PathBlocked", `Material ${String(material.key)} ${role} image ${fileName} must be a direct child of approved_demo_directory.`);
      const size = statSync(source).size;
      if (size < 16 || size > MAX_APPEARANCE_IMAGE_BYTES)
        throw new FamilyEvidenceError("EvidenceInvalid", `Material ${String(material.key)} ${role} image ${fileName} must be between 16 bytes and ${MAX_APPEARANCE_IMAGE_BYTES / 1024 / 1024} MB.`);
      const bytes = readFileSync(source); const actual = sha256(bytes); const expected = String(bitmap.sha256 ?? "").toLowerCase();
      if (actual !== expected) throw new FamilyEvidenceError("EvidenceConflict", `Material ${String(material.key)} ${role} image ${fileName} checksum does not match the immutable Blueprint declaration.`);
      const format = imageFormat(bytes, extension);
      if (!format) throw new FamilyEvidenceError("EvidenceInvalid", `Material ${String(material.key)} ${role} image ${fileName} does not match its declared approved image format.`);
      citations.push({ source_kind: role === "texture" ? "appearance_texture" : "appearance_bump", material_key: material.key, material_name: material.name, file_name: fileName, sha256: actual, size_bytes: size, image_format: format, path_policy: "direct_child_of_approved_demo_directory" });
    }
  }
  return citations;
}

function bbox(item: { transform?: number[]; width?: number; height?: number }, pageWidth: number, pageHeight: number): Json {
  const transform = item.transform ?? [1, 0, 0, 1, 0, 0];
  const x = Number(transform[4] ?? 0); const y = Number(transform[5] ?? 0);
  const width = Math.max(0, Number(item.width ?? 0)); const height = Math.max(0, Math.abs(Number(item.height ?? transform[0] ?? 0)));
  return { x: Math.round(x * 1000) / 1000, y: Math.round((pageHeight - y - height) * 1000) / 1000, width: Math.round(width * 1000) / 1000, height: Math.round(height * 1000) / 1000, page_width: Math.round(pageWidth * 1000) / 1000, page_height: Math.round(pageHeight * 1000) / 1000 };
}

async function ocrPage(page: { getViewport(options: { scale: number }): { width: number; height: number }; render(options: Json): { promise: Promise<void> } }, pageNumber: number): Promise<Json[]> {
  const assetRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "assets", "ocr");
  const eng = path.join(assetRoot, "eng.traineddata.gz"); const vie = path.join(assetRoot, "vie.traineddata.gz");
  if (!existsSync(eng) || !existsSync(vie)) throw new FamilyEvidenceError("OcrUnavailable", "OCR assets eng/vie are not bundled. Rebuild the learner release with approved OCR assets.");
  const viewport = page.getViewport({ scale: 2 });
  if (viewport.width * viewport.height > MAX_PIXELS) throw new FamilyEvidenceError("EvidenceInvalid", `OCR page ${pageNumber} exceeds the pixel safety limit.`);
  const canvas = createCanvas(Math.ceil(viewport.width), Math.ceil(viewport.height));
  await page.render({ canvasContext: canvas.getContext("2d"), viewport }).promise;
  const worker = await createWorker(["eng", "vie"], 1, { langPath: assetRoot, cacheMethod: "none" });
  try {
    const result = await worker.recognize(canvas.toBuffer("image/png"), {}, { blocks: true });
    const blocks = (result.data as unknown as { blocks?: Array<{ bbox?: { x0: number; y0: number; x1: number; y1: number }; text?: string; confidence?: number }> }).blocks ?? [];
    return blocks.filter((block) => block.text?.trim()).map((block) => ({ raw_text: block.text?.trim(), bbox: { x: block.bbox?.x0 ?? 0, y: block.bbox?.y0 ?? 0, width: (block.bbox?.x1 ?? 0) - (block.bbox?.x0 ?? 0), height: (block.bbox?.y1 ?? 0) - (block.bbox?.y0 ?? 0), page_width: viewport.width, page_height: viewport.height }, confidence: block.confidence ?? null, extraction_method: "rendered_page_ocr" }));
  } finally { await worker.terminate(); }
}

function requestedUnit(raw: unknown): string {
  const value = String(raw ?? "unknown").toLowerCase();
  if (!["mm", "cm", "m", "inch", "ft", "unknown"].includes(value)) throw new FamilyEvidenceError("EvidenceInvalid", "units must be mm, cm, m, inch, ft or unknown.");
  return value;
}

function scaleAnchors(raw: unknown): Json[] {
  if (raw === undefined) return [];
  if (!Array.isArray(raw) || raw.length > 20) throw new FamilyEvidenceError("EvidenceInvalid", "scale_anchors must contain at most 20 human-confirmed reference dimensions.");
  return raw.map((item, index) => {
    const anchor = objectValue(item, `scale_anchors[${index}]`);
    const pixelDistance = Number(anchor.pixel_distance); const realDistance = Number(anchor.real_distance);
    const unit = requestedUnit(anchor.unit);
    if (!Number.isFinite(pixelDistance) || pixelDistance <= 0 || !Number.isFinite(realDistance) || realDistance <= 0 || unit === "unknown")
      throw new FamilyEvidenceError("EvidenceInvalid", `scale_anchors[${index}] requires positive pixel_distance/real_distance and a known unit.`);
    const label = typeof anchor.label === "string" ? anchor.label.trim() : "";
    if (label.length > 120 || /[\u0000-\u001f\u007f]/.test(label)) throw new FamilyEvidenceError("EvidenceInvalid", `scale_anchors[${index}].label is invalid.`);
    return { pixel_distance: pixelDistance, real_distance: realDistance, unit, label: label || null, status: "user_confirmed" };
  });
}

function boundedText(raw: unknown, label: string, maximum = 120): string {
  if (typeof raw !== "string") throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be a string.`);
  const value = raw.trim();
  if (!value || value.length > maximum || /[\u0000-\u001f\u007f]/.test(value)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must contain 1..${maximum} visible characters.`);
  return value;
}

function coordinate(raw: unknown, label: string): number[] {
  if (!Array.isArray(raw) || raw.length !== 3) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must contain three coordinates.`);
  const values = raw.map((value, index) => {
    const number = Number(value);
    if (!Number.isFinite(number) || Math.abs(number) > 1_000_000_000) throw new FamilyEvidenceError("EvidenceInvalid", `${label}[${index}] is outside the supported coordinate range.`);
    return number;
  });
  return values;
}

function pdfSheetMetadata(raw: unknown, pageCount: number): Json[] {
  if (raw === undefined) return [];
  if (!Array.isArray(raw) || raw.length > pageCount) throw new FamilyEvidenceError("EvidenceInvalid", "sheet_metadata must contain at most one entry per PDF page.");
  const disciplines = new Set(["mechanical", "plumbing", "fire_protection", "electrical", "multi_discipline", "architecture", "structure", "unknown"]);
  const views = new Set(["plan", "riser", "schematic", "section", "elevation", "detail", "schedule", "legend", "unknown"]);
  const seen = new Set<number>();
  return raw.map((item, index) => {
    const value = objectValue(item, `sheet_metadata[${index}]`); const page = Number(value.page);
    if (!Number.isInteger(page) || page < 1 || page > pageCount || seen.has(page)) throw new FamilyEvidenceError("EvidenceInvalid", `sheet_metadata[${index}].page is invalid or duplicated.`);
    seen.add(page);
    const discipline = String(value.discipline ?? "unknown").toLowerCase(); const viewKind = String(value.view_kind ?? "unknown").toLowerCase();
    if (!disciplines.has(discipline) || !views.has(viewKind)) throw new FamilyEvidenceError("EvidenceInvalid", `sheet_metadata[${index}] has an unsupported discipline or view_kind.`);
    if (value.user_confirmed !== true) throw new FamilyEvidenceError("EvidenceInvalid", `sheet_metadata[${index}].user_confirmed must be true.`);
    const scale = value.scale_denominator === undefined ? null : Number(value.scale_denominator);
    if (scale !== null && (!Number.isFinite(scale) || scale <= 0 || scale > 100000)) throw new FamilyEvidenceError("EvidenceInvalid", `sheet_metadata[${index}].scale_denominator is invalid.`);
    const north = value.north_rotation_degrees === undefined ? null : Number(value.north_rotation_degrees);
    if (north !== null && (!Number.isFinite(north) || north < -360 || north > 360)) throw new FamilyEvidenceError("EvidenceInvalid", `sheet_metadata[${index}].north_rotation_degrees is invalid.`);
    return {
      page, discipline, view_kind: viewKind,
      drawing_number: value.drawing_number === undefined ? null : boundedText(value.drawing_number, `sheet_metadata[${index}].drawing_number`, 80),
      level_name: value.level_name === undefined ? null : boundedText(value.level_name, `sheet_metadata[${index}].level_name`, 120),
      scale_denominator: scale, north_rotation_degrees: north, status: "user_confirmed",
    };
  });
}

function imageSiteCapture(raw: unknown, width: number, height: number): Json | null {
  if (raw === undefined) return null;
  const value = objectValue(raw, "site_capture");
  if (value.user_confirmed !== true) throw new FamilyEvidenceError("EvidenceInvalid", "site_capture.user_confirmed must be true.");
  const role = String(value.capture_role ?? "unknown").toLowerCase();
  if (!["exterior", "interior", "plant_room", "ceiling_void", "shaft", "roof", "equipment_closeup", "unknown"].includes(role)) throw new FamilyEvidenceError("EvidenceInvalid", "site_capture.capture_role is invalid.");
  const controls = value.control_points === undefined ? [] : Array.isArray(value.control_points) ? value.control_points.map((item, index) => {
    const control = objectValue(item, `site_capture.control_points[${index}]`); const x = Number(control.image_x_px); const y = Number(control.image_y_px);
    if (!Number.isFinite(x) || !Number.isFinite(y) || x < 0 || y < 0 || x > width || y > height) throw new FamilyEvidenceError("EvidenceInvalid", `site_capture.control_points[${index}] is outside the image.`);
    return { image_x_px: x, image_y_px: y, world_mm: coordinate(control.world_mm, `site_capture.control_points[${index}].world_mm`), label: control.label === undefined ? null : boundedText(control.label, `site_capture.control_points[${index}].label`) };
  }) : (() => { throw new FamilyEvidenceError("EvidenceInvalid", "site_capture.control_points must be an array."); })();
  if (controls.length > 50) throw new FamilyEvidenceError("EvidenceInvalid", "site_capture.control_points exceeds 50 records.");
  return {
    capture_set_id: boundedText(value.capture_set_id, "site_capture.capture_set_id", 96),
    camera_id: boundedText(value.camera_id, "site_capture.camera_id", 96), capture_role: role,
    level_name: value.level_name === undefined ? null : boundedText(value.level_name, "site_capture.level_name", 120),
    control_points: controls, status: "user_confirmed",
  };
}

function trustedDwgManifest(raw: unknown, actualSha256: string, requestedUnits: string): Json | null {
  if (raw === undefined) return null;
  const value = objectValue(raw, "trusted_adapter_manifest");
  const adapter = String(value.adapter ?? "").toLowerCase();
  if (!new Set(["autodesk_revit_link", "oda_drawings"]).has(adapter)) throw new FamilyEvidenceError("EvidenceInvalid", "trusted_adapter_manifest.adapter must be autodesk_revit_link or oda_drawings.");
  const sourceHash = boundedText(value.source_sha256, "trusted_adapter_manifest.source_sha256", 64).toLowerCase();
  if (!/^[a-f0-9]{64}$/.test(sourceHash) || sourceHash !== actualSha256) throw new FamilyEvidenceError("EvidenceConflict", "Trusted DWG adapter manifest does not match the immutable source SHA-256.");
  const units = requestedUnit(value.units); if (units === "unknown") throw new FamilyEvidenceError("EvidenceInvalid", "Trusted DWG adapter must report known units.");
  if (requestedUnits !== "unknown" && requestedUnits !== units) throw new FamilyEvidenceError("EvidenceConflict", `Confirmed units ${requestedUnits} conflict with trusted DWG units ${units}.`);
  const layers = Array.isArray(value.layers) ? value.layers.map((item, index) => boundedText(item, `trusted_adapter_manifest.layers[${index}]`, 160)) : [];
  if (layers.length > 5000 || new Set(layers).size !== layers.length) throw new FamilyEvidenceError("EvidenceInvalid", "Trusted DWG layers are duplicated or exceed 5000 records.");
  const layerSet = new Set(layers); const blockNames = new Set<string>();
  const entities = Array.isArray(value.entities) ? value.entities.map((item, index) => {
    const entity = objectValue(item, `trusted_adapter_manifest.entities[${index}]`); const type = String(entity.type ?? "").toUpperCase();
    if (!["LINE", "LWPOLYLINE", "POLYLINE3D", "ARC", "CIRCLE", "INSERT"].includes(type)) throw new FamilyEvidenceError("EvidenceInvalid", `trusted_adapter_manifest.entities[${index}].type is unsupported.`);
    const layer = boundedText(entity.layer, `trusted_adapter_manifest.entities[${index}].layer`, 160);
    if (layerSet.size > 0 && !layerSet.has(layer)) throw new FamilyEvidenceError("EvidenceInvalid", `trusted_adapter_manifest.entities[${index}].layer is absent from layers.`);
    const base: Json = { type, layer, handle: entity.handle === undefined ? null : boundedText(entity.handle, `trusted_adapter_manifest.entities[${index}].handle`, 96) };
    if (type === "LINE") { base.start = coordinate(entity.start, `trusted_adapter_manifest.entities[${index}].start`); base.end = coordinate(entity.end, `trusted_adapter_manifest.entities[${index}].end`); }
    else if (type === "LWPOLYLINE" || type === "POLYLINE3D") {
      if (!Array.isArray(entity.vertices) || entity.vertices.length < 2 || entity.vertices.length > 10000) throw new FamilyEvidenceError("EvidenceInvalid", `trusted_adapter_manifest.entities[${index}].vertices must contain 2..10000 points.`);
      base.vertices = entity.vertices.map((point, pointIndex) => coordinate(point, `trusted_adapter_manifest.entities[${index}].vertices[${pointIndex}]`)); base.closed = entity.closed === true;
    } else if (type === "ARC" || type === "CIRCLE") {
      base.center = coordinate(entity.center, `trusted_adapter_manifest.entities[${index}].center`); base.radius = Number(entity.radius);
      if (!Number.isFinite(base.radius) || Number(base.radius) <= 0) throw new FamilyEvidenceError("EvidenceInvalid", `trusted_adapter_manifest.entities[${index}].radius must be positive.`);
      if (type === "ARC") { base.start_angle = Number(entity.start_angle); base.end_angle = Number(entity.end_angle); if (!Number.isFinite(base.start_angle) || !Number.isFinite(base.end_angle)) throw new FamilyEvidenceError("EvidenceInvalid", `trusted_adapter_manifest.entities[${index}] arc angles are invalid.`); }
    } else {
      const block = boundedText(entity.block, `trusted_adapter_manifest.entities[${index}].block`, 160); blockNames.add(block); base.block = block; base.point = coordinate(entity.point, `trusted_adapter_manifest.entities[${index}].point`); base.rotation = Number(entity.rotation ?? 0);
      if (!Number.isFinite(base.rotation)) throw new FamilyEvidenceError("EvidenceInvalid", `trusted_adapter_manifest.entities[${index}].rotation is invalid.`);
    }
    return base;
  }) : [];
  if (entities.length > 50000) throw new FamilyEvidenceError("EvidenceInvalid", "Trusted DWG entity inventory exceeds 50000 records.");
  const bounds = { min: [Infinity, Infinity, Infinity], max: [-Infinity, -Infinity, -Infinity] };
  const include = (point: unknown): void => { if (Array.isArray(point) && point.length === 3 && point.every((value) => Number.isFinite(Number(value)))) updateBounds(bounds, point.map(Number)); };
  for (const entity of entities) {
    include(entity.start); include(entity.end); include(entity.point);
    if (Array.isArray(entity.vertices)) for (const point of entity.vertices) include(point);
    if (Array.isArray(entity.center) && Number.isFinite(Number(entity.radius))) {
      const center = entity.center.map(Number); const radius = Number(entity.radius);
      include([center[0] - radius, center[1] - radius, center[2]]); include([center[0] + radius, center[1] + radius, center[2]]);
    }
  }
  const resolvedBounds = Number.isFinite(bounds.min[0]) ? { min: bounds.min, max: bounds.max } : null;
  const xrefs = Array.isArray(value.xrefs) ? value.xrefs.map((item, index) => {
    const xref = objectValue(item, `trusted_adapter_manifest.xrefs[${index}]`);
    return { name: boundedText(xref.name, `trusted_adapter_manifest.xrefs[${index}].name`, 160), status: String(xref.status ?? "unknown") };
  }) : [];
  const coordinateSystem = objectValue(value.coordinate_system, "trusted_adapter_manifest.coordinate_system");
  return {
    adapter, adapter_version: boundedText(value.adapter_version, "trusted_adapter_manifest.adapter_version", 80), units,
    coordinate_system: { origin: coordinate(coordinateSystem.origin, "trusted_adapter_manifest.coordinate_system.origin"), rotation_degrees: Number(coordinateSystem.rotation_degrees ?? 0), policy: boundedText(coordinateSystem.policy, "trusted_adapter_manifest.coordinate_system.policy", 80) },
    layers, blocks: [...blockNames].sort(), xrefs, layouts: Array.isArray(value.layouts) ? value.layouts.map((item, index) => boundedText(item, `trusted_adapter_manifest.layouts[${index}]`, 160)) : [], entities, bounds: resolvedBounds,
  };
}

function sourceManifest(source: string, bytes: Buffer, kind: string, units: string, anchors: Json[], orientation: Json): Json {
  return {
    source_kind: kind, canonical_path: source, sha256: sha256(bytes), size_bytes: bytes.byteLength,
    revision: { identity: sha256(bytes), policy: "content_sha256" }, units: { value: units, status: units === "unknown" ? "unknown" : "user_confirmed" },
    scale_anchors: anchors, orientation,
  };
}

type PdfMatrix = [number, number, number, number, number, number];
type PdfOperatorList = { fnArray: number[]; argsArray: unknown[] };

function multiplyPdfMatrix(left: PdfMatrix, right: PdfMatrix): PdfMatrix {
  return [
    left[0] * right[0] + left[2] * right[1],
    left[1] * right[0] + left[3] * right[1],
    left[0] * right[2] + left[2] * right[3],
    left[1] * right[2] + left[3] * right[3],
    left[0] * right[4] + left[2] * right[5] + left[4],
    left[1] * right[4] + left[3] * right[5] + left[5],
  ];
}

function transformPdfPoint(matrix: PdfMatrix, x: number, y: number): number[] {
  const rounded = (value: number): number => Math.round(value * 1_000_000) / 1_000_000;
  return [rounded(matrix[0] * x + matrix[2] * y + matrix[4]), rounded(matrix[1] * x + matrix[3] * y + matrix[5])];
}

function pdfPaintOperator(operator: number): string | null {
  const names = new Map<number, string>([
    [OPS.stroke, "stroke"], [OPS.closeStroke, "close_stroke"], [OPS.fill, "fill"], [OPS.eoFill, "even_odd_fill"],
    [OPS.fillStroke, "fill_stroke"], [OPS.eoFillStroke, "even_odd_fill_stroke"], [OPS.closeFillStroke, "close_fill_stroke"],
    [OPS.closeEOFillStroke, "close_even_odd_fill_stroke"],
  ]);
  return names.get(operator) ?? null;
}

/**
 * Extracts bounded vector evidence in top-left page-point coordinates.  These
 * paths are deliberately geometric evidence only: no layer, MEP service,
 * diameter, elevation or Revit semantics are inferred from line appearance.
 */
function extractPdfVectorPaths(operatorList: PdfOperatorList, viewportTransform: number[], pageNumber: number): Json {
  const viewport = viewportTransform as PdfMatrix;
  let current: PdfMatrix = [1, 0, 0, 1, 0, 0];
  const stack: PdfMatrix[] = []; const paths: Json[] = []; let pending: Json[] = [];
  let encountered = 0; let commandCount = 0; let truncated = false;
  for (let index = 0; index < operatorList.fnArray.length; index += 1) {
    const operator = operatorList.fnArray[index]; const rawArgs = operatorList.argsArray[index];
    if (operator === OPS.save) { stack.push([...current] as PdfMatrix); continue; }
    if (operator === OPS.restore) { current = stack.pop() ?? [1, 0, 0, 1, 0, 0]; continue; }
    if (operator === OPS.transform) {
      if (Array.isArray(rawArgs) && rawArgs.length >= 6 && rawArgs.slice(0, 6).every((value) => Number.isFinite(Number(value))))
        current = multiplyPdfMatrix(current, rawArgs.slice(0, 6).map(Number) as PdfMatrix);
      continue;
    }
    if (operator === OPS.constructPath) {
      const pathIndex = encountered; encountered += 1;
      if (paths.length >= MAX_PDF_VECTOR_PATHS_PER_PAGE || commandCount >= MAX_PDF_VECTOR_COMMANDS_PER_PAGE) { truncated = true; continue; }
      if (!Array.isArray(rawArgs) || !Array.isArray(rawArgs[0]) || !Array.isArray(rawArgs[1])) { truncated = true; continue; }
      const pathOperators = rawArgs[0].map(Number); const values = rawArgs[1].map(Number); const pageMatrix = multiplyPdfMatrix(viewport, current);
      const commands: Json[] = []; const subpaths: Json[] = []; let cursor = 0; let activePoints: number[][] = []; let activeClosed = false; let linearOnly = true;
      const flush = (): void => {
        if (activePoints.length > 0) subpaths.push({ points_page: activePoints, closed: activeClosed });
        activePoints = []; activeClosed = false;
      };
      const takePoint = (): number[] | null => {
        if (cursor + 1 >= values.length || !Number.isFinite(values[cursor]) || !Number.isFinite(values[cursor + 1])) return null;
        const result = transformPdfPoint(pageMatrix, values[cursor], values[cursor + 1]); cursor += 2; return result;
      };
      for (const pathOperator of pathOperators) {
        if (commandCount + commands.length >= MAX_PDF_VECTOR_COMMANDS_PER_PAGE) { truncated = true; break; }
        if (pathOperator === OPS.moveTo) {
          flush(); const point = takePoint(); if (!point) { truncated = true; break; }
          activePoints.push(point); commands.push({ operation: "move_to", points_page: [point] });
        } else if (pathOperator === OPS.lineTo) {
          const point = takePoint(); if (!point) { truncated = true; break; }
          activePoints.push(point); commands.push({ operation: "line_to", points_page: [point] });
        } else if (pathOperator === OPS.rectangle) {
          flush(); if (cursor + 3 >= values.length) { truncated = true; break; }
          const [x, y, width, height] = values.slice(cursor, cursor + 4); cursor += 4;
          const rectangle = [[x, y], [x + width, y], [x + width, y + height], [x, y + height]].map(([px, py]) => transformPdfPoint(pageMatrix, px, py));
          activePoints = rectangle; activeClosed = true; commands.push({ operation: "rectangle", points_page: rectangle }); flush();
        } else if (pathOperator === OPS.curveTo || pathOperator === OPS.curveTo2 || pathOperator === OPS.curveTo3) {
          linearOnly = false; const count = pathOperator === OPS.curveTo ? 3 : 2; const points: number[][] = [];
          for (let pointIndex = 0; pointIndex < count; pointIndex += 1) { const point = takePoint(); if (!point) break; points.push(point); }
          if (points.length !== count) { truncated = true; break; }
          activePoints.push(points[points.length - 1]); commands.push({ operation: pathOperator === OPS.curveTo ? "cubic_curve" : pathOperator === OPS.curveTo2 ? "cubic_curve_current_start" : "cubic_curve_current_end", control_and_end_points_page: points });
        } else if (pathOperator === OPS.closePath) {
          activeClosed = true; commands.push({ operation: "close_path" }); flush();
        } else { linearOnly = false; commands.push({ operation: `unsupported_${pathOperator}` }); }
      }
      flush(); commandCount += commands.length;
      const allPoints = commands.flatMap((command) => {
        if (Array.isArray(command.points_page)) return command.points_page as number[][];
        if (Array.isArray(command.control_and_end_points_page)) return command.control_and_end_points_page as number[][];
        return [];
      });
      if (commands.length === 0 || allPoints.length === 0) continue;
      const xs = allPoints.map((point) => point[0]); const ys = allPoints.map((point) => point[1]);
      const pathPayload = { page: pageNumber, path_index: pathIndex, commands, subpaths, bounds_page: { min_x: Math.min(...xs), min_y: Math.min(...ys), max_x: Math.max(...xs), max_y: Math.max(...ys) }, linear_only: linearOnly };
      const record: Json = { ...pathPayload, path_sha256: sha256(Buffer.from(JSON.stringify(pathPayload), "utf8")), paint_operator: "unpainted_or_clipping", mapping_status: linearOnly ? "linear_reference_candidate" : "curve_engineering_review_required" };
      paths.push(record); pending.push(record);
      continue;
    }
    const paint = pdfPaintOperator(operator);
    if (paint) { for (const path of pending) path.paint_operator = paint; pending = []; }
  }
  return {
    coordinate_space: "top_left_page_points_x_right_y_down", path_count: paths.length, encountered_path_count: encountered,
    command_count: commandCount, truncated, limits: { max_paths_per_page: MAX_PDF_VECTOR_PATHS_PER_PAGE, max_commands_per_page: MAX_PDF_VECTOR_COMMANDS_PER_PAGE }, paths,
    semantic_boundary: "Vector paths are page geometry evidence only. MEP discipline, service, size, elevation, connectivity and design intent require explicit engineer mapping.",
  };
}

async function inspectPdfSource(args: Record<string, unknown>): Promise<Json> {
  const source = await approvedFile(args.source_path, args.approved_demo_directory, ".pdf");
  const bytes = await readFile(source);
  if (bytes.byteLength > MAX_BYTES) throw new FamilyEvidenceError("EvidenceInvalid", `PDF exceeds ${MAX_BYTES / 1024 / 1024} MB safety limit.`);
  let pdf;
  try { pdf = await getDocument({ data: new Uint8Array(bytes), stopAtErrors: true, standardFontDataUrl: pdfStandardFontsDirectory }).promise; }
  catch (error) { const message = error instanceof Error ? error.message : String(error); throw new FamilyEvidenceError(/password|encrypted/i.test(message) ? "SourceEncrypted" : "SourceUnreadable", `PDF cannot be parsed: ${message}`); }
  try {
    if (pdf.numPages > MAX_PAGES) throw new FamilyEvidenceError("EvidenceInvalid", `PDF exceeds ${MAX_PAGES} page safety limit.`);
    const pages: Json[] = []; let nativeTextPages = 0; let ocrPages = 0; let vectorPages = 0;
    for (let number = 1; number <= pdf.numPages; number += 1) {
      const page = await pdf.getPage(number); const viewport = page.getViewport({ scale: 1 });
      const content = await page.getTextContent({ disableNormalization: false });
      const textBlocks = (content.items as Array<{ str?: string; transform?: number[]; width?: number; height?: number }>)
        .filter((item) => item.str?.trim()).map((item) => ({ raw_text: item.str?.trim(), bbox: bbox(item, viewport.width, viewport.height), confidence: 100, extraction_method: "native_text" }));
      const blocks = textBlocks.length > 0 ? textBlocks : await ocrPage(page, number);
      const fingerprintedBlocks = blocks.map((block, index) => ({ ...block, block_sha256: sourceBlockSha256(number, index, block as Json) }));
      if (textBlocks.length > 0) nativeTextPages += 1; else ocrPages += 1;
      const operatorList = await page.getOperatorList();
      const vectorExtraction = extractPdfVectorPaths(operatorList as PdfOperatorList, viewport.transform, number);
      const vectorOperatorCount = operatorList.fnArray.filter((operator) => operator === OPS.constructPath || operator === OPS.paintImageMaskXObject || operator === OPS.paintSolidColorImageMask).length;
      if (vectorOperatorCount > 0) vectorPages += 1;
      pages.push({ page: number, width: viewport.width, height: viewport.height, rotation: page.rotate, extraction_method: textBlocks.length > 0 ? "native_text" : "rendered_page_ocr", content_class: vectorOperatorCount > 0 ? "vector_or_mixed_drawing" : textBlocks.length > 0 ? "catalog_or_table" : "raster_scan", vector_operator_count: vectorOperatorCount, vector_extraction: vectorExtraction, text_blocks: fingerprintedBlocks });
    }
    const units = requestedUnit(args.units); const anchors = scaleAnchors(args.scale_anchors); const sheets = pdfSheetMetadata(args.sheet_metadata, pdf.numPages);
    for (const page of pages) page.sheet_metadata = sheets.find((sheet) => sheet.page === page.page) ?? null;
    const manifest = sourceManifest(source, bytes, "pdf", units, anchors, { page_rotations: pages.map((item) => ({ page: item.page, degrees: item.rotation })) });
    const evidence: Json = { schema_version: "3.0", record_kind: "source_evidence_v3", evidence_id: id("src", source, sha256(bytes)), ...manifest, page_count: pdf.numPages, pages, sheet_metadata: sheets, classification: { native_text_pages: nativeTextPages, ocr_pages: ocrPages, vector_or_mixed_pages: vectorPages, mepf_sheet_count: sheets.filter((sheet) => ["mechanical", "plumbing", "fire_protection", "electrical", "multi_discipline"].includes(String(sheet.discipline))).length }, provenance: { policy: "page_block_sha256_v2", immutable_source_sha256: manifest.sha256 }, confidence: { source_identity: "exact", text_extraction: ocrPages > 0 ? "mixed" : "exact", geometry_scale: units !== "unknown" && anchors.length > 0 ? "human_confirmed" : "unavailable", sheet_semantics: sheets.length > 0 ? "user_confirmed" : "unavailable" }, observations: [], warnings: ["Catalog/vector text and geometry are untrusted evidence. Dimensions, scale, connectors, MEP system intent and performance remain uncertain until an engineer confirms the normalized mapping."], status: "observed" };
    return keep(evidence);
  } finally { await pdf.destroy(); }
}

async function inspectImageSource(args: Record<string, unknown>): Promise<Json> {
  const extensions = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"];
  const source = await approvedFile(args.source_path, args.approved_demo_directory, extensions);
  const bytes = await readFile(source); if (bytes.byteLength > MAX_BYTES) throw new FamilyEvidenceError("EvidenceInvalid", `Image exceeds ${MAX_BYTES / 1024 / 1024} MB safety limit.`);
  const format = imageFormat(bytes, path.extname(source).toLowerCase()); if (!format) throw new FamilyEvidenceError("EvidenceInvalid", "Image signature does not match its extension.");
  let image; try { image = await loadImage(bytes); } catch (error) { throw new FamilyEvidenceError("SourceUnreadable", `Image cannot be decoded: ${error instanceof Error ? error.message : String(error)}`); }
  if (image.width * image.height > MAX_PIXELS) throw new FamilyEvidenceError("EvidenceInvalid", "Image exceeds the pixel safety limit.");
  const units = requestedUnit(args.units); const anchors = scaleAnchors(args.scale_anchors); const orientation = String(args.orientation ?? "unknown").toLowerCase();
  if (!["front", "side", "top", "orthographic", "perspective", "unknown"].includes(orientation)) throw new FamilyEvidenceError("EvidenceInvalid", "orientation is invalid.");
  const siteCapture = imageSiteCapture(args.site_capture, image.width, image.height);
  const manifest = sourceManifest(source, bytes, "image", units, anchors, { view: orientation, exif_applied_by_decoder: true });
  const reliableScale = units !== "unknown" && anchors.length > 0;
  return keep({ schema_version: "3.0", record_kind: "source_evidence_v3", evidence_id: id("src", source, sha256(bytes)), ...manifest, image: { format, width_px: image.width, height_px: image.height }, site_capture: siteCapture, classification: { source_type: siteCapture ? "construction_site_capture" : orientation === "perspective" || orientation === "unknown" ? "single_view_proposal" : "dimensioned_view_candidate" }, provenance: { policy: "whole_image_sha256_v1", immutable_source_sha256: manifest.sha256 }, confidence: { source_identity: "exact", geometry_scale: reliableScale ? "human_confirmed" : "unavailable", world_registration: siteCapture && Array.isArray(siteCapture.control_points) && siteCapture.control_points.length >= 3 ? "control_points_supplied" : "unavailable", hidden_geometry: "unavailable", connectors: "unavailable" }, observations: [], warnings: reliableScale ? ["Image scale is user-confirmed, but hidden geometry, depth, concealed services and connectors are not inferred."] : ["A known reference dimension and unit are required before measured geometry can be proposed. Single-view perspective evidence cannot certify hidden geometry, depth, concealed services or connectors."], status: reliableScale ? "proposal_ready" : "awaiting_scale_confirmation" });
}

type DxfPair = { code: number; value: string };
function parseDxfPairs(text: string): DxfPair[] {
  const lines = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n"); const pairs: DxfPair[] = [];
  for (let index = 0; index + 1 < lines.length; index += 2) {
    const code = Number(lines[index].trim()); if (!Number.isInteger(code)) throw new FamilyEvidenceError("EvidenceInvalid", `DXF group code at line ${index + 1} is invalid.`);
    pairs.push({ code, value: lines[index + 1].trim() });
  }
  return pairs;
}
function dxfNumber(entity: DxfPair[], code: number, fallback?: number): number {
  const pair = entity.find((item) => item.code === code); if (!pair) { if (fallback !== undefined) return fallback; throw new FamilyEvidenceError("EvidenceInvalid", `DXF entity is missing group ${code}.`); }
  const value = Number(pair.value); if (!Number.isFinite(value)) throw new FamilyEvidenceError("EvidenceInvalid", `DXF group ${code} is not numeric.`); return value;
}
function dxfPoint(entity: DxfPair[], xCode: number, yCode: number, zCode: number): number[] { return [dxfNumber(entity, xCode), dxfNumber(entity, yCode), dxfNumber(entity, zCode, 0)]; }
function updateBounds(bounds: { min: number[]; max: number[] }, point: number[]): void { for (let axis = 0; axis < 3; axis += 1) { bounds.min[axis] = Math.min(bounds.min[axis], point[axis]); bounds.max[axis] = Math.max(bounds.max[axis], point[axis]); } }
function inspectDxf(bytes: Buffer): { entities: Json[]; layers: string[]; blocks: string[]; bounds: Json | null; unitsCode: number | null; unsupported: Json[] } {
  if (bytes.includes(0)) throw new FamilyEvidenceError("EvidenceInvalid", "Binary DXF is not supported by the bounded ASCII parser.");
  const pairs = parseDxfPairs(bytes.toString("utf8")); const entities: Json[] = []; const layers = new Set<string>(); const blocks = new Set<string>(); const unsupported = new Map<string, number>();
  const bounds = { min: [Infinity, Infinity, Infinity], max: [-Infinity, -Infinity, -Infinity] }; let unitsCode: number | null = null; let section = "";
  for (let index = 0; index < pairs.length;) {
    const pair = pairs[index];
    if (pair.code === 9 && pair.value === "$INSUNITS") { const next = pairs.slice(index + 1, index + 4).find((item) => item.code === 70); if (next) unitsCode = Number(next.value); index += 1; continue; }
    if (pair.code === 0 && pair.value === "SECTION") { section = pairs[index + 1]?.value ?? ""; index += 2; continue; }
    if (pair.code === 0 && pair.value === "ENDSEC") { section = ""; index += 1; continue; }
    if (pair.code !== 0 || section !== "ENTITIES") { index += 1; continue; }
    const type = pair.value.toUpperCase(); let end = index + 1; while (end < pairs.length && pairs[end].code !== 0) end += 1; const body = pairs.slice(index + 1, end); const layer = body.find((item) => item.code === 8)?.value ?? "0"; layers.add(layer); const handle = body.find((item) => item.code === 5)?.value ?? null;
    try {
      if (type === "LINE") { const start = dxfPoint(body, 10, 20, 30); const finish = dxfPoint(body, 11, 21, 31); updateBounds(bounds, start); updateBounds(bounds, finish); entities.push({ type: "LINE", layer, handle, start, end: finish }); }
      else if (type === "LWPOLYLINE") { const elevation = dxfNumber(body, 38, 0); const vertices: number[][] = []; for (let cursor = 0; cursor < body.length; cursor += 1) if (body[cursor].code === 10) { const x = Number(body[cursor].value); const yPair = body.slice(cursor + 1).find((item) => item.code === 20 || item.code === 10); if (!yPair || yPair.code !== 20) throw new FamilyEvidenceError("EvidenceInvalid", "LWPOLYLINE vertex is missing Y coordinate."); const point = [x, Number(yPair.value), elevation]; if (!point.every(Number.isFinite)) throw new FamilyEvidenceError("EvidenceInvalid", "LWPOLYLINE vertex is invalid."); vertices.push(point); updateBounds(bounds, point); } entities.push({ type: "LWPOLYLINE", layer, handle, closed: (Number(body.find((item) => item.code === 70)?.value ?? 0) & 1) === 1, vertices }); }
      else if (type === "CIRCLE" || type === "ARC") { const center = dxfPoint(body, 10, 20, 30); const radius = dxfNumber(body, 40); if (radius <= 0) throw new FamilyEvidenceError("EvidenceInvalid", `${type} radius must be positive.`); updateBounds(bounds, [center[0] - radius, center[1] - radius, center[2]]); updateBounds(bounds, [center[0] + radius, center[1] + radius, center[2]]); entities.push({ type, layer, handle, center, radius, start_angle: type === "ARC" ? dxfNumber(body, 50) : null, end_angle: type === "ARC" ? dxfNumber(body, 51) : null }); }
      else if (type === "INSERT") { const point = dxfPoint(body, 10, 20, 30); const block = body.find((item) => item.code === 2)?.value ?? ""; if (block) blocks.add(block); updateBounds(bounds, point); entities.push({ type: "INSERT", layer, handle, block, point, scale: [dxfNumber(body, 41, 1), dxfNumber(body, 42, 1), dxfNumber(body, 43, 1)], rotation: dxfNumber(body, 50, 0), attribute_following: Number(body.find((item) => item.code === 66)?.value ?? 0) === 1 }); }
      else if (type === "TEXT" || type === "MTEXT" || type === "ATTRIB") {
        const point = dxfPoint(body, 10, 20, 30); const fragments = body.filter((item) => item.code === 1 || type === "MTEXT" && item.code === 3).map((item) => item.value);
        const rawText = fragments.join(""); if (!rawText.trim()) throw new FamilyEvidenceError("EvidenceInvalid", `${type} is missing text content.`);
        updateBounds(bounds, point);
        entities.push({ type, layer, handle, text: rawText, point, height: dxfNumber(body, 40, 0), rotation: dxfNumber(body, 50, 0), ...(type === "ATTRIB" ? { tag: body.find((item) => item.code === 2)?.value ?? null, owner_handle: body.find((item) => item.code === 330)?.value ?? null } : {}) });
      }
      else if (type === "LEADER") {
        const vertices: number[][] = [];
        for (let cursor = 0; cursor < body.length; cursor += 1) if (body[cursor].code === 10) {
          const y = body.slice(cursor + 1).find((item) => item.code === 20 || item.code === 10); if (!y || y.code !== 20) throw new FamilyEvidenceError("EvidenceInvalid", "LEADER vertex is missing Y coordinate.");
          const vertex = [Number(body[cursor].value), Number(y.value), dxfNumber(body.slice(cursor), 30, 0)]; if (!vertex.every(Number.isFinite)) throw new FamilyEvidenceError("EvidenceInvalid", "LEADER vertex is invalid."); vertices.push(vertex); updateBounds(bounds, vertex);
        }
        if (vertices.length < 2) throw new FamilyEvidenceError("EvidenceInvalid", "LEADER requires at least two vertices.");
        entities.push({ type, layer, handle, vertices, annotation_handle: body.find((item) => item.code === 340)?.value ?? null, mapping_status: "explicit_engineer_target_required" });
      }
      else if (type === "DIMENSION") {
        const point = dxfPoint(body, 10, 20, 30); updateBounds(bounds, point);
        entities.push({ type, layer, handle, point, text_override: body.find((item) => item.code === 1)?.value ?? null, dimension_block: body.find((item) => item.code === 2)?.value ?? null, mapping_status: "dimension_semantics_engineering_review_required" });
      }
      else if (type === "SPLINE" || type === "POLYLINE") { const points: number[][] = []; for (let cursor = 0; cursor < body.length; cursor += 1) if (body[cursor].code === 10) { const point = [Number(body[cursor].value), dxfNumber(body.slice(cursor), 20), dxfNumber(body.slice(cursor), 30, 0)]; points.push(point); updateBounds(bounds, point); } entities.push({ type, layer, handle, control_points: points, mapping_status: "engineering_review_required" }); }
      else unsupported.set(type, (unsupported.get(type) ?? 0) + 1);
    } catch (error) { if (error instanceof FamilyEvidenceError) throw error; throw new FamilyEvidenceError("EvidenceInvalid", `DXF ${type} entity cannot be parsed.`); }
    index = end;
  }
  const resolvedBounds = Number.isFinite(bounds.min[0]) ? { min: bounds.min, max: bounds.max } : null;
  return { entities, layers: [...layers].sort(), blocks: [...blocks].sort(), bounds: resolvedBounds, unitsCode, unsupported: [...unsupported].sort(([left], [right]) => left.localeCompare(right)).map(([type, count]) => ({ type, count })) };
}
function dxfUnits(code: number | null): string { return ({ 1: "inch", 2: "ft", 4: "mm", 5: "cm", 6: "m" } as Record<number, string>)[code ?? -1] ?? "unknown"; }

async function inspectCadSource(args: Record<string, unknown>, requestedKind?: string): Promise<Json> {
  const source = await approvedFile(args.source_path, args.approved_demo_directory, [".dxf", ".dwg"]); const bytes = await readFile(source);
  if (bytes.byteLength > MAX_BYTES) throw new FamilyEvidenceError("EvidenceInvalid", `CAD file exceeds ${MAX_BYTES / 1024 / 1024} MB safety limit.`);
  const kind = requestedKind ?? path.extname(source).slice(1).toLowerCase(); const requested = requestedUnit(args.units);
  if (kind === "dwg") {
    const sourceHash = sha256(bytes); const trusted = trustedDwgManifest(args.trusted_adapter_manifest, sourceHash, requested);
    const units = trusted ? String(trusted.units) : requested;
    const manifest = sourceManifest(source, bytes, "dwg", units, [], trusted ? trusted.coordinate_system as Json : { world_coordinate_system: "unverified" });
    if (!trusted) return keep({ schema_version: "3.0", record_kind: "source_evidence_v3", evidence_id: id("src", source, sourceHash), ...manifest, layers: [], blocks: [], geometry: { status: "trusted_adapter_required", entities: [], bounds: null }, provenance: { policy: "whole_file_sha256_v1", immutable_source_sha256: manifest.sha256 }, confidence: { source_identity: "exact", geometry: "unavailable", scale: requested === "unknown" ? "unavailable" : "user_confirmed" }, warnings: ["Binary DWG is never parsed as text or blind-imported. Use an Autodesk/ODA-compatible trusted inspection adapter or temporary Revit link in an approved disposable document."], status: "requires_trusted_dwg_adapter" });
    manifest.units = { value: units, status: "trusted_adapter_reported" };
    return keep({ schema_version: "3.0", record_kind: "source_evidence_v3", evidence_id: id("src", source, sourceHash), ...manifest, trusted_adapter: { adapter: trusted.adapter, adapter_version: trusted.adapter_version }, layers: trusted.layers, blocks: trusted.blocks, xrefs: trusted.xrefs, layouts: trusted.layouts, geometry: { status: "trusted_adapter_parsed", entity_count: (trusted.entities as Json[]).length, entities: trusted.entities, bounds: trusted.bounds }, provenance: { policy: "trusted_adapter_manifest_sha256_bound_v1", immutable_source_sha256: manifest.sha256 }, confidence: { source_identity: "exact", geometry: "trusted_adapter", scale: "trusted_adapter_reported", coordinates: "trusted_adapter_reported" }, warnings: ["DWG geometry and metadata are evidence from a SHA-256-bound trusted adapter. Layer/block semantics, system/type/level/elevation and routing intent still require MEP engineer confirmation before Preview."], status: "observed" });
  }
  const parsed = inspectDxf(bytes); const declared = dxfUnits(parsed.unitsCode); if (requested !== "unknown" && declared !== "unknown" && requested !== declared) throw new FamilyEvidenceError("EvidenceConflict", `Confirmed units ${requested} conflict with DXF $INSUNITS ${declared}.`);
  const units = requested !== "unknown" ? requested : declared; const manifest = sourceManifest(source, bytes, "dxf", units, [], { coordinate_system: "DXF world", origin: [0, 0, 0] });
  manifest.units = { value: units, status: requested !== "unknown" ? "user_confirmed" : declared !== "unknown" ? "declared_by_source" : "unknown" };
  return keep({ schema_version: "3.0", record_kind: "source_evidence_v3", evidence_id: id("src", source, sha256(bytes)), ...manifest, layers: parsed.layers, blocks: parsed.blocks, geometry: { status: parsed.entities.length > 0 ? "parsed" : "empty_or_unsupported", entity_count: parsed.entities.length, entities: parsed.entities, bounds: parsed.bounds, unsupported_entities: parsed.unsupported }, provenance: { policy: "dxf_entity_whitelist_v1", immutable_source_sha256: manifest.sha256 }, confidence: { source_identity: "exact", geometry: parsed.unsupported.length === 0 ? "exact_for_supported_entities" : "partial", scale: units === "unknown" ? "unavailable" : requested !== "unknown" ? "user_confirmed" : "declared_by_source" }, warnings: ["DXF geometry is evidence only. Unsupported entities, layer intent, system/type/level/elevation and route mapping require engineering review before preview."], status: units === "unknown" ? "awaiting_unit_confirmation" : "observed" });
}

export async function inspectCadGeometry(args: Record<string, unknown>): Promise<Json> { return inspectCadSource(args); }

export async function inspectFamilySource(args: Record<string, unknown>): Promise<Json> {
  const sourceKind = String(args.source_kind ?? "").toLowerCase();
  if (sourceKind === "pdf") return inspectPdfSource(args);
  if (sourceKind === "image") return inspectImageSource(args);
  if (sourceKind === "dxf" || sourceKind === "dwg") return inspectCadSource(args, sourceKind);
  throw new FamilyEvidenceError("EvidenceInvalid", "source_kind must be pdf, image, dxf or dwg.");
}

const required: Record<string, string[]> = {
  // Ebara 3D4 source dimensions. The normalizer deliberately requires the
  // complete published dimension chain instead of silently mapping a partial
  // catalogue table to an approximate pump envelope.
  pump: ["A", "A1", "A2", "B", "C", "D1", "D2", "DN1", "DN2", "K1", "K2", "P1", "P2", "H", "H1", "H2", "H3", "M", "N1", "N2", "R", "S1", "S2", "T", "type_code"],
  panel: ["width_mm", "height_mm", "depth_mm", "type_code", "voltage", "phase", "poles", "rating_ampere"],
};
function hasConfirmedValue(value: unknown): boolean { return value !== undefined && value !== null && value !== ""; }
function fieldState(value: unknown): string {
  if (typeof value === "object" && value !== null && "status" in value) {
    const status = String((value as Json).status ?? "uncertain");
    return status === "confirmed" && hasConfirmedValue((value as Json).value) ? "confirmed" : status;
  }
  return hasConfirmedValue(value) ? "confirmed" : "missing";
}
function objectValue(value: unknown, label: string): Json {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be an object.`);
  return value as Json;
}
function requireFieldStatus(value: unknown, label: string): "confirmed" | "uncertain" | "missing" {
  const state = value === undefined ? "missing" : String(value);
  if (state !== "confirmed" && state !== "uncertain" && state !== "missing") throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be confirmed, uncertain or missing.`);
  return state;
}
function normalizeDocumentLocator(value: unknown, field: string, source: Json): Json {
  const locator = objectValue(value, `confirmed_fields.${field}.provenance`);
  const allowed = new Set(["kind", "page", "block_index", "block_sha256"]);
  for (const property of Object.keys(locator)) if (!allowed.has(property)) throw new FamilyEvidenceError("EvidenceInvalid", `confirmed_fields.${field}.provenance contains unsupported field ${property}.`);
  if (locator.kind !== "source_document") throw new FamilyEvidenceError("EvidenceInvalid", `confirmed_fields.${field}.provenance.kind must be source_document.`);
  const pageNumber = Number(locator.page); const blockIndex = Number(locator.block_index); const requestedHash = String(locator.block_sha256 ?? "").toLowerCase();
  if (!Number.isInteger(pageNumber) || pageNumber < 1 || !Number.isInteger(blockIndex) || blockIndex < 0 || !/^[a-f0-9]{64}$/.test(requestedHash))
    throw new FamilyEvidenceError("EvidenceInvalid", `confirmed_fields.${field}.provenance requires positive page, non-negative block_index and a SHA-256 block fingerprint.`);
  const page = (Array.isArray(source.pages) ? source.pages : []).find((candidate) => Number((candidate as Json).page) === pageNumber) as Json | undefined;
  const block = page && Array.isArray(page.text_blocks) ? page.text_blocks[blockIndex] as Json | undefined : undefined;
  if (!block) throw new FamilyEvidenceError("EvidenceConflict", `confirmed_fields.${field}.provenance does not resolve to a SourceEvidence page/block.`);
  const actualHash = String(block.block_sha256 ?? sourceBlockSha256(pageNumber, blockIndex, block)).toLowerCase();
  if (actualHash !== requestedHash) throw new FamilyEvidenceError("EvidenceConflict", `confirmed_fields.${field}.provenance block fingerprint does not match immutable SourceEvidence.`);
  return { kind: "source_document", source_evidence_sha256: String(source.sha256), page: pageNumber, block_index: blockIndex, block_sha256: actualHash };
}
function normalizeConfirmedFields(raw: unknown, source: Json): { fields: Json; provenance: Json } {
  if (raw === undefined) return { fields: {}, provenance: { policy: "field_level_source_provenance_v1", source_evidence_sha256: String(source.sha256), fields: [], summary: { source_document: 0, explicit_user_confirmed: 0, legacy_user_confirmed: 0 }, boundary: "A source-document locator proves only that the confirmed field was reviewed against this immutable SourceEvidence page/block; it does not prove extraction accuracy, catalog revision applicability, per-type completeness, or Revit runtime behavior." } };
  const input = objectValue(raw, "confirmed_fields"); const fields: Json = {}; const records: Json[] = [];
  let sourced = 0; let explicitUser = 0; let legacyUser = 0;
  for (const [field, rawState] of Object.entries(input)) {
    if (typeof rawState !== "object" || rawState === null || Array.isArray(rawState)) {
      const status = fieldState(rawState); fields[field] = { value: clone(rawState), status, provenance: { kind: "user_confirmed", confirmation_origin: "legacy_scalar" } };
      records.push({ source_field: field, status, provenance_kind: "user_confirmed", confirmation_origin: "legacy_scalar" }); legacyUser += 1; continue;
    }
    const state = objectValue(rawState, `confirmed_fields.${field}`); const allowed = new Set(["value", "status", "provenance"]);
    for (const property of Object.keys(state)) if (!allowed.has(property)) throw new FamilyEvidenceError("EvidenceInvalid", `confirmed_fields.${field} contains unsupported field ${property}.`);
    const status = "status" in state ? requireFieldStatus(state.status, `confirmed_fields.${field}.status`) : hasConfirmedValue(state.value) ? "confirmed" : "missing";
    if (status === "confirmed" && !hasConfirmedValue(state.value)) throw new FamilyEvidenceError("EvidenceInvalid", `confirmed_fields.${field}.value is required when status=confirmed.`);
    if (state.provenance === undefined) {
      fields[field] = { value: clone(state.value), status, provenance: { kind: "user_confirmed", confirmation_origin: "legacy_field_state" } };
      records.push({ source_field: field, status, provenance_kind: "user_confirmed", confirmation_origin: "legacy_field_state" }); legacyUser += 1; continue;
    }
    const provenanceState = objectValue(state.provenance, `confirmed_fields.${field}.provenance`);
    if (provenanceState.kind === "source_document") {
      if (status !== "confirmed") throw new FamilyEvidenceError("EvidenceInvalid", `confirmed_fields.${field}.provenance source_document requires status=confirmed.`);
      const provenance = normalizeDocumentLocator(provenanceState, field, source); fields[field] = { value: clone(state.value), status, provenance };
      records.push({ source_field: field, status, provenance_kind: "source_document", document_locator: { page: provenance.page, block_index: provenance.block_index, block_sha256: provenance.block_sha256 } }); sourced += 1;
    } else if (provenanceState.kind === "user_confirmed") {
      const properties = Object.keys(provenanceState); if (properties.some((property) => property !== "kind")) throw new FamilyEvidenceError("EvidenceInvalid", `confirmed_fields.${field}.provenance user_confirmed accepts only kind.`);
      fields[field] = { value: clone(state.value), status, provenance: { kind: "user_confirmed", confirmation_origin: "explicit" } };
      records.push({ source_field: field, status, provenance_kind: "user_confirmed", confirmation_origin: "explicit" }); explicitUser += 1;
    } else throw new FamilyEvidenceError("EvidenceInvalid", `confirmed_fields.${field}.provenance.kind must be source_document or user_confirmed.`);
  }
  return { fields, provenance: { policy: "field_level_source_provenance_v1", source_evidence_sha256: String(source.sha256), fields: records.sort((left, right) => String(left.source_field).localeCompare(String(right.source_field))), summary: { source_document: sourced, explicit_user_confirmed: explicitUser, legacy_user_confirmed: legacyUser }, boundary: "A source-document locator proves only that the confirmed field was reviewed against this immutable SourceEvidence page/block; it does not prove extraction accuracy, catalog revision applicability, per-type completeness, or Revit runtime behavior." } };
}
function normalizeTypeConfirmedFields(raw: unknown, source: Json, blueprint: Json | undefined, expected: string[]): { fields: Json; provenance: Json[]; missing: string[]; required: boolean } {
  const types = blueprint && Array.isArray(blueprint.types) ? blueprint.types as Json[] : [];
  const required = expected.length > 0 && types.length > 1;
  if (!required) {
    if (raw !== undefined) throw new FamilyEvidenceError("EvidenceInvalid", "type_confirmed_fields is valid only when a Blueprint declares two or more Family Types with required_source_fields.");
    return { fields: {}, provenance: [], missing: [], required: false };
  }
  const input = raw === undefined ? {} : objectValue(raw, "type_confirmed_fields");
  const typeNames = types.map((type) => String(type.name ?? ""));
  for (const name of typeNames) if (!name) throw new FamilyEvidenceError("EvidenceInvalid", "Blueprint Family Type name is missing.");
  for (const name of Object.keys(input)) if (!typeNames.includes(name)) throw new FamilyEvidenceError("EvidenceInvalid", `type_confirmed_fields contains unknown Family Type ${name}.`);
  const fields: Json = {}; const provenance: Json[] = []; const missing: string[] = [];
  for (const name of typeNames) {
    const normalized = normalizeConfirmedFields(input[name], source); const statuses = Object.fromEntries(expected.map((field) => [field, fieldState(normalized.fields[field])]));
    const missingFields = expected.filter((field) => statuses[field] !== "confirmed");
    missing.push(...missingFields.map((field) => `${name}.${field}`));
    fields[name] = normalized.fields;
    provenance.push({ type_name: name, fields: normalized.provenance.fields, summary: normalized.provenance.summary });
  }
  return { fields, provenance, missing, required: true };
}
function catalogText(raw: unknown, label: string, maximum: number): string {
  if (typeof raw !== "string") throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be a non-empty string.`);
  const value = raw.trim();
  if (!value || value.length > maximum || /[\u0000-\u001f\u007f]/.test(value)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} must be 1..${maximum} visible characters.`);
  return value;
}
function catalogIdentityHash(sourceSha256: string, identity: Json): string {
  const provenance = objectValue(identity.revision_provenance, "catalog revision identity.revision_provenance");
  return sha256(Buffer.from([sourceSha256, String(identity.manufacturer), String(identity.catalog_id), String(identity.revision), String(identity.issued_on ?? ""), String(identity.product_series ?? ""), String(provenance.kind), String(provenance.confirmation_origin ?? ""), String(provenance.page ?? ""), String(provenance.block_index ?? ""), String(provenance.block_sha256 ?? "")].join("\u001f")));
}
function normalizeCatalogRevisionEvidence(raw: unknown, label: string, source: Json): Json {
  const provenance = objectValue(raw, `${label}.revision_provenance`);
  if (provenance.kind === "source_document") {
    const locator = normalizeDocumentLocator(provenance, `${label}.revision`, source);
    return { kind: "source_document", source_evidence_sha256: locator.source_evidence_sha256, page: locator.page, block_index: locator.block_index, block_sha256: locator.block_sha256 };
  }
  if (provenance.kind === "user_confirmed") {
    if (Object.keys(provenance).some((property) => property !== "kind")) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.revision_provenance user_confirmed accepts only kind.`);
    return { kind: "user_confirmed", confirmation_origin: "explicit" };
  }
  throw new FamilyEvidenceError("EvidenceInvalid", `${label}.revision_provenance.kind must be source_document or user_confirmed.`);
}
function normalizeCatalogRevision(raw: unknown, label: string, source: Json): Json {
  const input = objectValue(raw, label); const allowed = new Set(["manufacturer", "catalog_id", "revision", "revision_provenance", "issued_on", "product_series"]);
  for (const property of Object.keys(input)) if (!allowed.has(property)) throw new FamilyEvidenceError("EvidenceInvalid", `${label} contains unsupported field ${property}.`);
  const identity: Json = {
    manufacturer: catalogText(input.manufacturer, `${label}.manufacturer`, 120),
    catalog_id: catalogText(input.catalog_id, `${label}.catalog_id`, 120),
    revision: catalogText(input.revision, `${label}.revision`, 80),
    revision_provenance: normalizeCatalogRevisionEvidence(input.revision_provenance, label, source),
    source_evidence_sha256: String(source.sha256),
  };
  if (input.issued_on !== undefined) {
    const issuedOn = catalogText(input.issued_on, `${label}.issued_on`, 10);
    const parsed = /^\d{4}-\d{2}-\d{2}$/.test(issuedOn) ? new Date(`${issuedOn}T00:00:00Z`) : undefined;
    if (!parsed || Number.isNaN(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== issuedOn) throw new FamilyEvidenceError("EvidenceInvalid", `${label}.issued_on must be a calendar date YYYY-MM-DD.`);
    identity.issued_on = issuedOn;
  }
  if (input.product_series !== undefined) identity.product_series = catalogText(input.product_series, `${label}.product_series`, 120);
  identity.catalog_identity_sha256 = catalogIdentityHash(String(source.sha256), identity);
  return identity;
}
function normalizeCatalogRevisionProvenance(familyRaw: unknown, perTypeRaw: unknown, source: Json, blueprint: Json | undefined, expected: string[]): Json {
  const types = blueprint && Array.isArray(blueprint.types) ? blueprint.types as Json[] : [];
  const typeSpecificRequired = expected.length > 0 && types.length > 1;
  const base: Json = {
    policy: "catalog_revision_identity_v2", source_evidence_sha256: String(source.sha256), declared: false, scope: "none",
    boundary: "Catalog identity binds manufacturer/catalog/revision and an explicit revision provenance locator or user confirmation to immutable SourceEvidence. It does not prove PDF text accuracy, revision applicability, catalog completeness, manufacturer approval, Type Catalog load/select behavior, or Revit runtime behavior.",
  };
  if (familyRaw === undefined && perTypeRaw === undefined) return base;
  if (familyRaw !== undefined && perTypeRaw !== undefined) throw new FamilyEvidenceError("EvidenceInvalid", "catalog_revision and type_catalog_revisions cannot be used together.");
  if (familyRaw !== undefined) {
    if (typeSpecificRequired) throw new FamilyEvidenceError("EvidenceInvalid", "Multi-Type Family with required_source_fields must use type_catalog_revisions for every exact Family Type; a shared catalog_revision fallback is blocked.");
    return { ...base, declared: true, scope: "family", family: normalizeCatalogRevision(familyRaw, "catalog_revision", source) };
  }
  if (!typeSpecificRequired) throw new FamilyEvidenceError("EvidenceInvalid", "type_catalog_revisions is valid only for a Blueprint with two or more Family Types and required_source_fields.");
  const input = objectValue(perTypeRaw, "type_catalog_revisions"); const typeNames = types.map((type) => String(type.name ?? ""));
  for (const typeName of typeNames) if (!typeName) throw new FamilyEvidenceError("EvidenceInvalid", "Blueprint Family Type name is missing.");
  for (const typeName of Object.keys(input)) if (!typeNames.includes(typeName)) throw new FamilyEvidenceError("EvidenceInvalid", `type_catalog_revisions contains unknown Family Type ${typeName}.`);
  const missing = typeNames.filter((typeName) => input[typeName] === undefined);
  if (missing.length > 0) throw new FamilyEvidenceError("EvidenceInvalid", `type_catalog_revisions is missing Family Type(s): ${missing.join(", ")}.`);
  return { ...base, declared: true, scope: "per_type", types: typeNames.map((typeName) => ({ type_name: typeName, catalog: normalizeCatalogRevision(input[typeName], `type_catalog_revisions.${typeName}`, source) })) };
}
function resolveManufacturerLookupCatalogues(blueprint: Json | undefined): Json[] {
  if (!blueprint) return [];
  const tables = Array.isArray(blueprint.lookup_tables) ? blueprint.lookup_tables as Json[] : [];
  const citations: Json[] = [];
  for (const table of tables) {
    if (table.catalogue_provenance === undefined) continue;
    const provenance = objectValue(table.catalogue_provenance, `lookup table ${String(table.key)}.catalogue_provenance`);
    const catalogId = String(provenance.catalog_record_id ?? ""); const catalog = readEvidenceRecord(catalogId);
    if (catalog.record_kind !== "manufacturer_catalog_v1" || catalog.status !== "validated") throw new FamilyEvidenceError("EvidenceInvalid", `Lookup table ${String(table.key)} must cite a validated manufacturer_catalog record.`);
    if (String(provenance.catalog_sha256 ?? "").toLowerCase() !== String(catalog.sha256 ?? "").toLowerCase() || provenance.revision !== catalog.revision || provenance.manufacturer !== catalog.manufacturer)
      throw new FamilyEvidenceError("EvidenceConflict", `Lookup table ${String(table.key)} catalogue provenance does not match its immutable manufacturer catalogue record.`);
    const map = objectValue(provenance.column_map, `lookup table ${String(table.key)}.catalogue_provenance.column_map`);
    const sourceKeys = Array.isArray(provenance.lookup_key_source_columns) ? provenance.lookup_key_source_columns.map(String) : [];
    if (sourceKeys.length === 0 || !Array.isArray(catalog.lookup_key_columns) || JSON.stringify(sourceKeys) !== JSON.stringify(catalog.lookup_key_columns)) throw new FamilyEvidenceError("EvidenceConflict", `Lookup table ${String(table.key)} does not preserve the approved manufacturer catalogue key order.`);
    const blueprintRows = Array.isArray(table.rows) ? table.rows as Json[] : []; const catalogRows = Array.isArray(catalog.rows) ? catalog.rows as Json[] : [];
    if (blueprintRows.length !== catalogRows.length) throw new FamilyEvidenceError("EvidenceConflict", `Lookup table ${String(table.key)} row count differs from the approved manufacturer catalogue; version a new catalogue instead of changing it silently.`);
    for (const [index, row] of catalogRows.entries()) {
      const expected: Json = {}; const sourceValues = objectValue(row.values, `manufacturer catalogue row ${index}.values`);
      for (const [sourceColumn, targetColumn] of Object.entries(map)) expected[String(targetColumn)] = sourceValues[sourceColumn];
      const actual = objectValue(blueprintRows[index].values, `lookup table ${String(table.key)}.rows[${index}].values`);
      if (JSON.stringify(expected) !== JSON.stringify(actual)) throw new FamilyEvidenceError("EvidenceConflict", `Lookup table ${String(table.key)} row ${index} differs from the approved manufacturer catalogue mapping.`);
    }
    citations.push({ catalog_record_id: catalog.record_id, catalogue_name: catalog.catalogue_name, manufacturer: catalog.manufacturer, revision: catalog.revision, sha256: catalog.sha256, lookup_table_key: table.key, row_count: catalogRows.length });
  }
  return citations;
}
const FAMILY_READINESS_MAPPINGS = new Set(["family_blueprint", "family_profile", "symbolic_lines", "model_lines", "detail_lines", "image_family", "pdf_catalog_family"]);
function sameEvidenceIdSet(left: string[], right: string[]): boolean { return left.length === right.length && left.every((value) => right.includes(value)); }
function resolveFamilySpecReadiness(recordId: unknown, evidenceIds: string[]): { passed: boolean; gate: Json; record?: Json } {
  if (recordId === undefined) return {
    passed: false,
    gate: {
      gate: "information_readiness",
      status: "needs_information",
      question: "Run mepf_evidence_readiness for target_kind=family and answer its batched questions before FamilySpec can become build-preview-ready.",
    },
  };
  const readiness = readEvidenceRecord(String(recordId));
  if (readiness.record_kind !== "mepf_evidence_readiness_v1" || readiness.status !== "ready_for_proposal")
    throw new FamilyEvidenceError("EvidenceInvalid", "A ready mepf_evidence_readiness record is required before FamilySpec can become build-preview-ready.");
  const mapping = String(readiness.mapping ?? "");
  const readinessEvidenceIds = Array.isArray(readiness.evidence_ids) ? readiness.evidence_ids.map(String) : [];
  if (readiness.target_kind !== "family" || !FAMILY_READINESS_MAPPINGS.has(mapping) || !sameEvidenceIdSet(readinessEvidenceIds, evidenceIds))
    throw new FamilyEvidenceError("EvidenceConflict", "Readiness record does not match this FamilySpec's immutable source evidence or a supported Family mapping.");
  return { passed: true, record: readiness, gate: { gate: "information_readiness", status: "passed", readiness_record_id: readiness.record_id, mapping } };
}
function sameConfirmedValue(left: unknown, right: unknown): boolean { return JSON.stringify(left) === JSON.stringify(right); }
function requirePerTypeSourceValues(blueprint: Json | undefined, expected: string[], typeFields: Json, missing: string[]): void {
  if (!blueprint || missing.length > 0) return;
  const parameters = Array.isArray(blueprint.parameters) ? blueprint.parameters as Json[] : [];
  const types = Array.isArray(blueprint.types) ? blueprint.types as Json[] : [];
  const parameterKeysBySource = new Map<string, string[]>();
  for (const parameter of parameters) {
    const field = typeof parameter.source_field === "string" ? parameter.source_field : undefined;
    if (!field || !expected.includes(field) || parameter.formula !== undefined || parameter.lookup !== undefined) continue;
    const keys = parameterKeysBySource.get(field) ?? []; keys.push(String(parameter.key)); parameterKeysBySource.set(field, keys);
  }
  for (const type of types) {
    const typeName = String(type.name); const typeEvidence = objectValue(typeFields[typeName], `type_confirmed_fields.${typeName}`); const values = objectValue(type.values, `blueprint.types.${typeName}.values`);
    for (const field of expected) {
      const state = objectValue(typeEvidence[field], `type_confirmed_fields.${typeName}.${field}`); const sourceValue = state.value;
      for (const parameterKey of parameterKeysBySource.get(field) ?? []) {
        if (!(parameterKey in values)) throw new FamilyEvidenceError("EvidenceInvalid", `Blueprint Family Type ${typeName} must declare value for source-bound parameter ${parameterKey}; source fallback is blocked for multi-Type Family.`);
        if (!sameConfirmedValue(values[parameterKey], sourceValue)) throw new FamilyEvidenceError("EvidenceConflict", `Blueprint Family Type ${typeName} value for ${parameterKey} does not match confirmed source field ${field}.`);
      }
    }
  }
}
export function previewFamilySpec(args: Record<string, unknown>): Json {
  const evidenceId = String(args.evidence_id ?? ""); const source = readEvidenceRecord(evidenceId);
  if (source.record_kind !== "source_evidence_v2" && source.record_kind !== "source_evidence_v3") throw new FamilyEvidenceError("EvidenceInvalid", "family_spec_preview requires immutable SourceEvidence v2/v3 from family_source_inspect.");
  const supportingIds = args.supporting_evidence_ids === undefined ? [] : Array.isArray(args.supporting_evidence_ids) ? args.supporting_evidence_ids.map(String) : (() => { throw new FamilyEvidenceError("EvidenceInvalid", "supporting_evidence_ids must be an array."); })();
  if (supportingIds.length > 3 || new Set([evidenceId, ...supportingIds]).size !== supportingIds.length + 1) throw new FamilyEvidenceError("EvidenceInvalid", "supporting_evidence_ids must contain at most three unique records different from evidence_id.");
  const supporting = supportingIds.map((id) => readEvidenceRecord(id));
  for (const record of supporting) if (record.record_kind !== "source_evidence_v3") throw new FamilyEvidenceError("EvidenceInvalid", "supporting_evidence_ids require immutable SourceEvidence v3 records.");
  const familyKind = String(args.family_kind ?? "").trim(); if (!familyKind || !/^[a-z][a-z0-9_]{0,63}$/.test(familyKind)) throw new FamilyEvidenceError("EvidenceInvalid", "family_kind must use lower_snake_case and start with a letter.");
  const normalizedFields = normalizeConfirmedFields(args.confirmed_fields, source); const fields = normalizedFields.fields;
  let blueprint: Json | undefined; let assessment: Json | undefined; let photometricCitations: Json[] = []; let appearanceCitations: Json[] = [];
  if (args.blueprint !== undefined) {
    try { const validated = validateFamilyBlueprint(args.blueprint); blueprint = validated.blueprint; assessment = validated.assessment as unknown as Json; }
    catch (error) { throw new FamilyEvidenceError("EvidenceInvalid", error instanceof Error ? error.message : String(error)); }
    const declaredKind = String((blueprint.family as Json).family_key ?? "");
    if (declaredKind !== familyKind) throw new FamilyEvidenceError("EvidenceConflict", "family_kind must match blueprint.family.family_key.");
    photometricCitations = resolvePhotometricAssets(blueprint, args.approved_demo_directory);
    appearanceCitations = resolveAppearanceAssets(blueprint, args.approved_demo_directory);
  }
  const expected = blueprint ? (blueprint.required_source_fields as string[] ?? []) : required[familyKind] ?? [];
  if (!blueprint && expected.length === 0) throw new FamilyEvidenceError("EvidenceInvalid", "An unregistered family_kind requires a validated Family Blueprint v3.");
  const readiness = resolveFamilySpecReadiness(args.readiness_record_id, [evidenceId, ...supportingIds]);
  const statuses = Object.fromEntries(expected.map((name) => [name, fieldState(fields[name])]));
  const typeConfirmed = normalizeTypeConfirmedFields(args.type_confirmed_fields, source, blueprint, expected);
  const missing = typeConfirmed.required ? typeConfirmed.missing : expected.filter((name) => statuses[name] !== "confirmed");
  if (typeConfirmed.required) requirePerTypeSourceValues(blueprint, expected, typeConfirmed.fields, missing);
  const catalogRevisionProvenance = normalizeCatalogRevisionProvenance(args.catalog_revision, args.type_catalog_revisions, source, blueprint, expected);
  const manufacturerCatalogueCitations = resolveManufacturerLookupCatalogues(blueprint);
  const targetLod = blueprint ? String(blueprint.target_lod) : "LOD_300";
  const buildable = !assessment || assessment.buildable_by_api === true;
  const status = !readiness.passed ? "requires_information_readiness" : missing.length === 0 && buildable ? "human_confirmed" : missing.length > 0 ? "awaiting_human_confirmation" : "requires_capability_or_ui_fallback";
  const nestedCitations = blueprint ? resolveNestedBlueprintArtifacts(blueprint) : [];
  const fieldProvenance = { ...normalizedFields.provenance, type_fields: typeConfirmed.provenance, type_specific_required: typeConfirmed.required };
  const spec: Json = { schema_version: blueprint ? "3.0" : "2.0", record_kind: blueprint ? "family_spec_v3" : "family_spec_v2", spec_id: id("spec", evidenceId, JSON.stringify(supportingIds), familyKind, JSON.stringify(fields), JSON.stringify(typeConfirmed.fields), JSON.stringify(catalogRevisionProvenance), JSON.stringify(blueprint ?? null)), source_evidence_id: evidenceId, supporting_source_evidence_ids: supportingIds, source_sha256: source.sha256, family_kind: familyKind, lod: targetLod, detail_profile: targetLod === "LOD_350" ? "dscons_mep_350_v1" : "dscons_mep_300_v1", confirmed_fields: fields, type_confirmed_fields: typeConfirmed.fields, field_status: statuses, field_provenance: fieldProvenance, catalog_revision_provenance: catalogRevisionProvenance, manufacturer_catalogue_citations: manufacturerCatalogueCitations, blueprint, blueprint_assessment: assessment, status, missing_fields: missing, citations: [{ evidence_id: evidenceId, sha256: source.sha256 }, ...supporting.map((record) => ({ evidence_id: record.evidence_id, sha256: record.sha256, supporting: true })), ...manufacturerCatalogueCitations, ...photometricCitations, ...appearanceCitations, ...nestedCitations], next: status === "human_confirmed" ? "Run family_build_preview with this immutable spec_id." : missing.length > 0 ? "Confirm every required source field before family_build_preview." : "Implement the listed primitive/template capability or complete the declared UI fallback before build." };
  spec.spec_id = id("spec", evidenceId, JSON.stringify(supportingIds), familyKind, JSON.stringify(fields), JSON.stringify(typeConfirmed.fields), JSON.stringify(catalogRevisionProvenance), JSON.stringify(readiness.record ?? null), JSON.stringify(blueprint ?? null));
  spec.evidence_governance = { readiness: readiness.gate };
  if (readiness.record) (spec.citations as Json[]).push({ readiness_record_id: readiness.record.record_id, readiness_mapping: readiness.record.mapping });
  if (status === "requires_information_readiness") spec.next = "Run mepf_evidence_readiness, resolve its batched questions, then submit the matching readiness_record_id to family_spec_preview.";
  return keep(spec);
}
