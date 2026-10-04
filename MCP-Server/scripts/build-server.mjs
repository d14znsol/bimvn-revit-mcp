import { existsSync, renameSync, rmSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const serverRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const buildRoot = path.join(serverRoot, "build");
const nonce = `${process.pid}-${Date.now()}`;
const stageRoot = path.join(serverRoot, `.build-stage-${nonce}`);
const backupRoot = path.join(serverRoot, `.build-backup-${nonce}`);
const tscEntrypoint = path.join(serverRoot, "node_modules", "typescript", "bin", "tsc");

try {
  const compile = spawnSync(process.execPath, [tscEntrypoint, "--pretty", "false", "--outDir", stageRoot], {
    cwd: serverRoot,
    encoding: "utf8",
    stdio: "inherit",
  });
  if (compile.error) throw compile.error;
  if (compile.status !== 0) process.exit(compile.status ?? 1);

  for (const relative of ["index.js", "computer-use-evaluation.js", "source-to-revit.js", "tools/index.js", "embedded-capabilities.js"]) {
    if (!existsSync(path.join(stageRoot, ...relative.split("/")))) throw new Error(`TypeScript build is incomplete: missing ${relative}.`);
  }

  const moduleNonce = `?build=${nonce}`;
  const { builtInTools } = await import(`${pathToFileURL(path.join(stageRoot, "tools", "index.js")).href}${moduleNonce}`);
  const { createEmbeddedCapabilityManifest } = await import(`${pathToFileURL(path.join(stageRoot, "embedded-capabilities.js")).href}${moduleNonce}`);
  const manifest = createEmbeddedCapabilityManifest(builtInTools);
  writeFileSync(path.join(stageRoot, "embedded-capabilities.json"), `${JSON.stringify(manifest, null, 2)}\n`, "utf8");

  if (existsSync(buildRoot)) renameSync(buildRoot, backupRoot);
  try { renameSync(stageRoot, buildRoot); }
  catch (error) { if (existsSync(backupRoot) && !existsSync(buildRoot)) renameSync(backupRoot, buildRoot); throw error; }
  rmSync(backupRoot, { recursive: true, force: true });
  process.stdout.write(`Built MCP Server: ${manifest.toolCount} tools, ${manifest.schemaFingerprintSha256}\n`);
} finally {
  rmSync(stageRoot, { recursive: true, force: true });
  if (existsSync(backupRoot) && !existsSync(buildRoot)) renameSync(backupRoot, buildRoot);
}
