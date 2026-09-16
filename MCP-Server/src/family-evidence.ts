import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { readFile, realpath } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createCanvas } from "@napi-rs/canvas";
import { getDocument } from "pdfjs-dist/legacy/build/pdf.mjs";
import { createWorker } from "tesseract.js";

export class FamilyEvidenceError extends Error {
  constructor(public readonly code: "PathBlocked" | "SourceUnreadable" | "SourceEncrypted" | "OcrUnavailable" | "EvidenceInvalid" | "EvidenceConflict", message: string) { super(message); }
}

type Json = Record<string, unknown>;
const TTL_MS = 15 * 60 * 1000;
const MAX_BYTES = 80 * 1024 * 1024;
const MAX_PAGES = 60;
const MAX_PIXELS = 18_000_000;
const records = new Map<string, { expiresAt: number; value: Json }>();

function sha256(data: Uint8Array): string { return createHash("sha256").update(data).digest("hex"); }
function id(prefix: string, ...values: string[]): string { return `${prefix}-${sha256(Buffer.from(values.join("|"))).slice(0, 24)}`; }
function clone<T>(value: T): T { return JSON.parse(JSON.stringify(value)) as T; }
function keep(record: Json): Json { records.set(String(record.evidence_id ?? record.spec_id), { expiresAt: Date.now() + TTL_MS, value: clone(record) }); return record; }
export function readEvidenceRecord(recordId: string): Json {
  const found = records.get(recordId);
  if (!found || found.expiresAt < Date.now()) throw new FamilyEvidenceError("EvidenceInvalid", "Evidence/spec id is unknown or expired; inspect the source again.");
  return clone(found.value);
}

async function approvedFile(raw: unknown, approvedDirectory: unknown, extension: string): Promise<string> {
  if (typeof raw !== "string" || typeof approvedDirectory !== "string" || !path.isAbsolute(raw) || !path.isAbsolute(approvedDirectory) || raw.startsWith("\\\\") || approvedDirectory.startsWith("\\\\"))
    throw new FamilyEvidenceError("PathBlocked", "Source and approved_demo_directory must be local absolute paths.");
  const root = await realpath(approvedDirectory).catch(() => { throw new FamilyEvidenceError("PathBlocked", "approved_demo_directory does not exist."); });
  const source = await realpath(raw).catch(() => { throw new FamilyEvidenceError("SourceUnreadable", "Source file does not exist or cannot be read."); });
  if (path.extname(source).toLowerCase() !== extension) throw new FamilyEvidenceError("EvidenceInvalid", `Source must be a ${extension} file.`);
  if (path.relative(root, source).startsWith("..") || path.isAbsolute(path.relative(root, source))) throw new FamilyEvidenceError("PathBlocked", "Source is outside approved_demo_directory.");
  return source;
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

export async function inspectFamilySource(args: Record<string, unknown>): Promise<Json> {
  const sourceKind = String(args.source_kind ?? "").toLowerCase();
  if (sourceKind !== "pdf") throw new FamilyEvidenceError("EvidenceInvalid", "Node-local SourceEvidence v2 currently accepts source_kind=pdf; CAD remains handled by cad_geometry_inspect.");
  const source = await approvedFile(args.source_path, args.approved_demo_directory, ".pdf");
  const bytes = await readFile(source);
  if (bytes.byteLength > MAX_BYTES) throw new FamilyEvidenceError("EvidenceInvalid", `PDF exceeds ${MAX_BYTES / 1024 / 1024} MB safety limit.`);
  let pdf;
  try { pdf = await getDocument({ data: new Uint8Array(bytes), stopAtErrors: true }).promise; }
  catch (error) { const message = error instanceof Error ? error.message : String(error); throw new FamilyEvidenceError(/password|encrypted/i.test(message) ? "SourceEncrypted" : "SourceUnreadable", `PDF cannot be parsed: ${message}`); }
  try {
    if (pdf.numPages > MAX_PAGES) throw new FamilyEvidenceError("EvidenceInvalid", `PDF exceeds ${MAX_PAGES} page safety limit.`);
    const pages: Json[] = [];
    for (let number = 1; number <= pdf.numPages; number += 1) {
      const page = await pdf.getPage(number); const viewport = page.getViewport({ scale: 1 });
      const content = await page.getTextContent({ disableNormalization: false });
      const textBlocks = (content.items as Array<{ str?: string; transform?: number[]; width?: number; height?: number }>)
        .filter((item) => item.str?.trim()).map((item) => ({ raw_text: item.str?.trim(), bbox: bbox(item, viewport.width, viewport.height), confidence: 100, extraction_method: "native_text" }));
      const blocks = textBlocks.length > 0 ? textBlocks : await ocrPage(page, number);
      pages.push({ page: number, width: viewport.width, height: viewport.height, rotation: page.rotate, extraction_method: textBlocks.length > 0 ? "native_text" : "rendered_page_ocr", text_blocks: blocks });
    }
    const evidence: Json = { schema_version: "2.0", record_kind: "source_evidence_v2", evidence_id: id("src", source, sha256(bytes)), source_kind: "pdf", canonical_path: source, sha256: sha256(bytes), size_bytes: bytes.byteLength, page_count: pdf.numPages, pages, observations: [], warnings: ["Catalog text is untrusted data. Dimensions, connectors and performance remain uncertain until an engineer confirms a normalized FamilySpec."], status: "observed" };
    return keep(evidence);
  } finally { await pdf.destroy(); }
}

const required: Record<string, string[]> = {
  // Ebara 3D4 source dimensions. The normalizer deliberately requires the
  // complete published dimension chain instead of silently mapping a partial
  // catalogue table to an approximate pump envelope.
  pump: ["A", "A1", "A2", "B", "C", "D1", "D2", "DN1", "DN2", "K1", "K2", "P1", "P2", "H", "H1", "H2", "H3", "M", "N1", "N2", "R", "S1", "S2", "T", "type_code"],
  panel: ["width_mm", "height_mm", "depth_mm", "type_code", "voltage", "phase", "poles", "rating_ampere"],
};
function fieldState(value: unknown): string { return typeof value === "object" && value !== null && "status" in value ? String((value as Json).status ?? "uncertain") : value === undefined || value === null || value === "" ? "missing" : "confirmed"; }
export function previewFamilySpec(args: Record<string, unknown>): Json {
  const evidenceId = String(args.evidence_id ?? ""); const source = readEvidenceRecord(evidenceId);
  if (source.record_kind !== "source_evidence_v2") throw new FamilyEvidenceError("EvidenceInvalid", "family_spec_preview requires SourceEvidence v2 from family_source_inspect.");
  const familyKind = String(args.family_kind ?? ""); if (!familyKind) throw new FamilyEvidenceError("EvidenceInvalid", "family_kind is required.");
  const fields = (args.confirmed_fields && typeof args.confirmed_fields === "object" ? args.confirmed_fields : {}) as Json;
  const expected = required[familyKind] ?? [];
  const statuses = Object.fromEntries(expected.map((name) => [name, fieldState(fields[name])]));
  const missing = expected.filter((name) => statuses[name] !== "confirmed");
  const spec: Json = { schema_version: "2.0", record_kind: "family_spec_v2", spec_id: id("spec", evidenceId, familyKind, JSON.stringify(fields)), source_evidence_id: evidenceId, source_sha256: source.sha256, family_kind: familyKind, lod: "LOD_300", detail_profile: "dscons_mep_300_v1", confirmed_fields: fields, field_status: statuses, status: missing.length === 0 ? "human_confirmed" : "awaiting_human_confirmation", missing_fields: missing, citations: [{ evidence_id: evidenceId, sha256: source.sha256 }], next: missing.length === 0 ? "Run family_build_preview with this immutable spec_id." : "Confirm all missing fields or explicitly mark the Family as not buildable." };
  return keep(spec);
}
