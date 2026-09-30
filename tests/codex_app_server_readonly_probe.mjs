import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const manifest = JSON.parse(await readFile(path.join(root, "MCP-Server", "build", "embedded-capabilities.json"), "utf8"));
const expectedToolCount = manifest.tools.length;
const codex = process.env.CODEX_CLI_PATH;
if (!codex) throw new Error("Set CODEX_CLI_PATH to the verified codex.exe before running this read-only probe.");
const expectedVersion = "0.154.0-alpha.6.2";
const versionText = await new Promise((resolve, reject) => {
  const child = spawn(codex, ["--version"], { windowsHide: true });
  let output = "";
  child.stdout.on("data", (chunk) => output += chunk);
  child.once("error", reject);
  child.once("exit", (code) => code === 0 ? resolve(output.trim()) : reject(new Error(`codex --version exited ${code}`)));
});
assert.match(versionText, new RegExp(expectedVersion.replaceAll(".", "\\.")));

const child = spawn(codex, ["app-server"], {
  cwd: process.env.ProgramData || "C:\\ProgramData",
  windowsHide: true,
  stdio: ["pipe", "pipe", "pipe"],
});
const pending = new Map();
let nextId = 0;
let stdout = "";
child.stdout.on("data", (chunk) => {
  stdout += chunk.toString("utf8");
  while (stdout.includes("\n")) {
    const index = stdout.indexOf("\n");
    const line = stdout.slice(0, index); stdout = stdout.slice(index + 1);
    if (!line.trim()) continue;
    const message = JSON.parse(line);
    if (message.id && pending.has(String(message.id))) {
      const record = pending.get(String(message.id)); pending.delete(String(message.id));
      message.error ? record.reject(new Error(JSON.stringify(message.error))) : record.resolve(message.result);
    } else if (message.id && message.method) {
      child.stdin.write(`${JSON.stringify({ id: message.id, error: { code: -32601, message: "Probe denies server requests." } })}\n`);
    }
  }
});

function request(method, params = {}) {
  const id = String(++nextId);
  child.stdin.write(`${JSON.stringify({ id, method, params })}\n`);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`timeout: ${method}`)); }, 30_000);
    pending.set(id, { resolve: (value) => { clearTimeout(timer); resolve(value); }, reject: (error) => { clearTimeout(timer); reject(error); } });
  });
}
function notify(method, params = {}) { child.stdin.write(`${JSON.stringify({ method, params })}\n`); }

try {
  await request("initialize", { clientInfo: { name: "dscons-readonly-probe", title: "DSCons read-only probe", version: "0.1.0" } });
  notify("initialized", {});
  const diskConfig = await request("config/read", {});
  const configured = diskConfig?.config?.mcp_servers || diskConfig?.mcp_servers || {};
  const servers = Object.fromEntries(Object.keys(configured).map((name) => [name, { enabled: false }]));
  const expectedRevitPid = process.env.DSCONS_PROBE_REVIT_PID || "12345";
  servers.dscons_embedded = {
    command: process.execPath,
    args: [path.join(root, "MCP-Server", "build", "index.js")],
    env: {
      DSCONS_EMBEDDED_CHAT: "1",
      DSCONS_EMBEDDED_APPROVAL_PIPE: "readonly-probe-no-writes",
      DSCONS_EMBEDDED_APPROVAL_SECRET: "readonly-probe",
      DSCONS_MCP_EXPECTED_REVIT_PID: expectedRevitPid,
    },
    startup_timeout_sec: 20,
    tool_timeout_sec: 600,
    default_tools_approval_mode: "approve",
    enabled: true,
    required: true,
  };
  const threadResult = await request("thread/start", {
    cwd: process.env.ProgramData || "C:\\ProgramData",
    approvalPolicy: "never",
    sandbox: "read-only",
    developerInstructions: "Read-only DSCons MCP protocol probe. Do not start a turn.",
    ephemeral: false,
    config: {
      mcp_servers: servers,
      web_search: "disabled",
      memories: { generate_memories: false, use_memories: false, disable_on_external_context: true },
      features: { shell_tool: false, apps: false, plugins: false, browser_use: false, in_app_browser: false, computer_use: false, image_generation: false, multi_agent: false, skill_search: false, goals: false },
      agents: { enabled: false },
    },
  });
  const threadId = threadResult?.thread?.id;
  assert.ok(threadId, "thread/start must return thread.id");
  const status = await request("mcpServerStatus/list", { threadId, detail: "toolsAndAuthOnly", limit: 100 });
  const embedded = status.data.find((server) => server.name === "dscons_embedded");
  assert.ok(embedded, "dscons_embedded must be loaded");
  assert.equal(Object.keys(embedded.tools || {}).length, expectedToolCount, `embedded MCP must expose exactly ${expectedToolCount} manifest tools`);
  const foreign = status.data.filter((server) => server.name !== "dscons_embedded" && Object.keys(server.tools || {}).length > 0);
  assert.deepEqual(foreign.map((server) => server.name), [], "no foreign MCP may expose tools");
  const account = await request("account/read", { refreshToken: false });
  const models = await request("model/list", { limit: 100, includeHidden: false });
  assert.ok(Array.isArray(models.data) && models.data.length > 0, "model/list must return at least one picker-visible model");
  assert.ok(models.data.some((model) => model.isDefault), "model/list must identify a default model");
  assert.ok(models.data.every((model) => model.model && model.displayName), "each picker model needs model and displayName");
  let usageState = "unavailable";
  try {
    const usage = await request("account/rateLimits/read", { excludeResetCreditDetails: true, supportsLunaReserve: false });
    const limits = usage?.rateLimitsByLimitId?.codex ?? usage?.rateLimits ?? usage?.rate_limits;
    if (limits && typeof limits === "object") {
      const primary = limits.primary && typeof limits.primary === "object" ? limits.primary : {};
      const secondary = limits.secondary && typeof limits.secondary === "object" ? limits.secondary : {};
      usageState = `structured:primary=${String(primary.windowDurationMins ?? "unknown")}m,secondary=${String(secondary.windowDurationMins ?? "unknown")}m`;
    }
  } catch {
    usageState = "unavailable";
  }
  await new Promise((resolve) => setTimeout(resolve, Number(process.env.DSCONS_PROBE_STABILITY_MS || 250)));
  assert.equal(child.exitCode, null, "codex app-server must remain alive after initialization/account/model/MCP status");
  const accountState = account?.account?.type
    ? `authenticated:${account.account.type}`
    : `not-authenticated:requiresOpenaiAuth=${String(account?.requiresOpenaiAuth)}`;
  console.log(`PASS Codex app-server ${versionText}: persisted thread, isolated DSCons MCP (${expectedToolCount} tools), server approval mode delegated to DSCons, account/read (${accountState}), model/list (${models.data.length}), quota (${usageState}), process stable; no turn/model write`);
} finally {
  child.stdin.end();
  if (!child.killed) child.kill();
}
