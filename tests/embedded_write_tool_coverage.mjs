import assert from "node:assert/strict";
import { builtInTools } from "../MCP-Server/build/tools/index.js";
import { isEmbeddedWriteTool } from "../MCP-Server/build/embedded-approval.js";

// These previews create only temporary documents/rollback transactions. Every
// other non-read-only tool must cross the one-use DSCons approval broker.
const previewOnlyWrites = new Set(["family_build_preview", "cad_to_revit_preview"]);
const writeAnnotated = builtInTools
  .filter((tool) => tool.annotations?.readOnlyHint === false)
  .map((tool) => tool.name)
  .sort();
const classified = writeAnnotated.filter((name) => isEmbeddedWriteTool(name) || previewOnlyWrites.has(name));

assert.deepEqual(classified.sort(), writeAnnotated, "each write-annotated tool must be broker-protected or explicitly preview-only");
for (const name of previewOnlyWrites) assert.ok(writeAnnotated.includes(name), `${name} must remain explicitly annotated as a write-like preview`);
console.log(`PASS embedded write coverage: ${writeAnnotated.length} write annotations are classified`);
