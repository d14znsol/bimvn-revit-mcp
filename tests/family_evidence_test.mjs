import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { mkdtemp, mkdir, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { FamilyEvidenceError, inspectFamilySource, previewFamilySpec } from "../MCP-Server/build/family-evidence.js";

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

const evidence = await inspectFamilySource({ source_path: nativePdf, source_kind: "pdf", approved_demo_directory: approved });
assert.equal(evidence.schema_version, "2.0");
assert.equal(evidence.page_count, 1);
assert.equal(evidence.pages[0].text_blocks[0].raw_text, "Pump A=100 mm");
assert.equal(evidence.pages[0].text_blocks[0].extraction_method, "native_text");
assert.match(evidence.sha256, /^[a-f0-9]{64}$/);

const pumpFields = Object.fromEntries(["A", "A1", "A2", "B", "C", "D1", "D2", "DN1", "DN2", "K1", "K2", "P1", "P2", "H", "H1", "H2", "H3", "M", "N1", "N2", "R", "S1", "S2", "T"].map((key, index) => [key, index + 1]));
const incomplete = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pump", confirmed_fields: { A: 100 } });
assert.equal(incomplete.status, "awaiting_human_confirmation");
assert.ok(incomplete.missing_fields.includes("A1"));
const complete = previewFamilySpec({ evidence_id: evidence.evidence_id, family_kind: "pump", confirmed_fields: { ...pumpFields, type_code: "P-1" } });
assert.equal(complete.status, "human_confirmed");
assert.equal(complete.lod, "LOD_300");

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

console.log("PASS Family Evidence v2: native PDF text/bbox/checksum/spec, local path guards, and local OCR/offline-asset guard");
