import { writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { builtInTools } from "../build/tools/index.js";
import { createEmbeddedCapabilityManifest } from "../build/embedded-capabilities.js";

const output = resolve("build", "embedded-capabilities.json");
const manifest = createEmbeddedCapabilityManifest(builtInTools);
writeFileSync(output, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
process.stdout.write(`Generated embedded capabilities: ${manifest.toolCount} tools, ${manifest.schemaFingerprintSha256}\n`);
