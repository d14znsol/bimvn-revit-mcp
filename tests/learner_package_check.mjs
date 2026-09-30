// Read-only packaged-layout regression; never installs or connects to Revit.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const root = path.resolve(process.argv[2] ?? '');
if (!process.argv[2]) throw new Error('Pass a learner release directory.');
const must = p => { if (!fs.existsSync(path.join(root,p))) throw new Error(`Missing release file: ${p}`); };
const manifest = JSON.parse(fs.readFileSync(path.join(root,'release-manifest.json'),'utf8'));
const expectedTfm = { '2019':'net47', '2020':'net47', '2021':'net48', '2022':'net48', '2023':'net48', '2024':'net48', '2025':'net8.0-windows', '2026':'net8.0-windows', '2027':'net10.0-windows' };
if (!Array.isArray(manifest.revitBuilds) || manifest.revitBuilds.length !== manifest.requestedRevitVersions.length) throw new Error('Manifest revitBuilds metadata is missing or incomplete');
const buildByYear = new Map(manifest.revitBuilds.map(item => [String(item.revitVersion), item]));
for (const year of manifest.requestedRevitVersions) {
  const record = buildByYear.get(String(year));
  if (!record || record.targetFramework !== expectedTfm[String(year)]) throw new Error(`Invalid build metadata for Revit ${year}`);
  const built = manifest.revitVersions.includes(year);
  if (built && record.status !== 'built') throw new Error(`Built list/status mismatch for Revit ${year}`);
  if (!built && record.status !== 'skipped') throw new Error(`Skipped list/status mismatch for Revit ${year}`);
  if (record.status === 'skipped' && (!Array.isArray(record.missingDependencies) || !record.reason)) throw new Error(`Skipped Revit ${year} lacks reason metadata`);
}
for (const year of manifest.revitVersions) for (const file of ['DSCons.RevitMcp.dll','DSCons.RevitMcp.Contracts.dll','DSCons.RevitMcp.addin','runtime/DSCons.RevitMcp.CoreRuntime.dll']) must(`artifacts/Revit${year}/${file}`);
for (const file of ['START_HERE.md','LICENSE','THIRD-PARTY-NOTICES.md','docs/learning/LEARNER_GUIDE.md','docs/learning/60-MINUTE-MCP-PILOT.md','docs/FAMILY-TEMPLATE-AUTO-RESOLUTION.md','docs/FAMILY-BLUEPRINT-V3.md','docs/MODEL-TRANSFER-MEPF.md','docs/SOURCE-TO-PROJECT-MEPF.md','docs/CODEX-REVIT-CHAT-POC.md','docs/MCP-RIBBON-CONTROLS.md','docs/COMPATIBILITY.md','docs/learning/README.md','MCP-Server/build/index.js','MCP-Server/node_modules/@modelcontextprotocol/sdk/package.json','scripts/student-setup.ps1','scripts/install-mcp.ps1']) must(file);
const start = fs.readFileSync(path.join(root,'START_HERE.md'),'utf8');
if (start.includes('.agents/')) throw new Error('Learner entrypoint depends on private ledger');
if (!start.includes('sản phẩm độc lập')) throw new Error('Learner entrypoint does not state the independent-product scope');
if (!start.includes('pilot 60 phút')) throw new Error('Learner entrypoint does not state the approved 60-minute MCP pilot');
if (!start.includes('node .\\MCP-Server\\scripts\\build-server.mjs')) throw new Error('Learner entrypoint does not use the deterministic MCP build entry point');
if (!start.includes('Không tự tìm, tải hoặc thay thế Family RFA')) throw new Error('Learner entrypoint permits automatic RFA search or look-alike substitution');
if (!start.includes('behavior thực sự tương đương') || !start.includes('không dùng cho fitting, 2D, Profile, Annotation, Tag')) throw new Error('Learner entrypoint does not bound Generic Model fallback by behavior and specialized-family exclusions');
if (!start.includes('Bật MCP') || !start.includes('MCP.CoreRuntime')) throw new Error('Learner entrypoint does not explain the Revit-side MCP boundary');
for (const phrase of ['Livestream','Track A','Track B','thử thách 7 ngày']) {
  if (start.includes(phrase)) throw new Error(`Learner entrypoint contains obsolete program flow: ${phrase}`);
}
for (const file of ['docs/learning/livestream.md','docs/learning/track-a.md','docs/learning/track-b.md']) {
  if (fs.existsSync(path.join(root,file))) throw new Error(`Obsolete program document shipped in learner release: ${file}`);
}
for (const item of manifest.files) {
  const resolved = path.resolve(root,item.path);
  if (!resolved.startsWith(root+path.sep)) throw new Error('Manifest traversal');
  const bytes = fs.readFileSync(resolved);
  if (crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase() !== item.sha256.toUpperCase()) throw new Error(`Checksum mismatch: ${item.path}`);
}
if (!manifest.files.some(x=>x.path==='THIRD-PARTY-NOTICES.md')) throw new Error('Notices not checksummed');
console.log(`PASS learner package: layout, standalone entrypoint, dependencies, ${manifest.files.length} hashes including notices. NOT an installation/runtime test.`);
