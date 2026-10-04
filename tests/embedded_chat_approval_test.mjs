import assert from "node:assert/strict";
import fs from "node:fs/promises";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { approvalHash, isEmbeddedWriteTool, requireEmbeddedApproval } from "../MCP-Server/build/embedded-approval.js";
import { callRevit } from "../MCP-Server/build/socket.js";

async function withBroker(responseFactory, action) {
  const pipeName = `dscons-test-${process.pid}-${Date.now()}-${Math.random().toString(16).slice(2)}`;
  const pipePath = `\\\\.\\pipe\\${pipeName}`;
  const server = net.createServer((socket) => {
    let buffer = "";
    socket.on("data", (chunk) => {
      buffer += chunk.toString("utf8");
      const newline = buffer.indexOf("\n");
      if (newline < 0) return;
      const request = JSON.parse(buffer.slice(0, newline));
      const response = responseFactory(request);
      if (response) socket.end(`${JSON.stringify(response)}\n`);
    });
  });
  await new Promise((resolve, reject) => server.listen(pipePath, resolve).once("error", reject));
  process.env.DSCONS_EMBEDDED_CHAT = "1";
  process.env.DSCONS_EMBEDDED_APPROVAL_PIPE = pipeName;
  process.env.DSCONS_EMBEDDED_APPROVAL_SECRET = "unit-test-secret";
  process.env.DSCONS_MCP_EXPECTED_REVIT_PID = "24680";
  try { await action(); } finally { await new Promise((resolve) => server.close(resolve)); }
}

assert.equal(isEmbeddedWriteTool("mep_apply_preview"), true);
assert.equal(isEmbeddedWriteTool("document_info"), false);
assert.equal(approvalHash("x", "{}"), approvalHash("x", "{}"));

process.env.DSCONS_EMBEDDED_CHAT = "1";
await assert.rejects(() => requireEmbeddedApproval("mep_create_route", { kind: "pipe" }), (error) => error.code === "PreviewRequired");

await withBroker((request) => {
  assert.equal(request.secret, "unit-test-secret");
  assert.equal(request.expectedPid, 24680);
  assert.equal(request.hash, approvalHash(request.toolName, request.argumentsJson));
  return { requestId: request.requestId, hash: request.hash, approved: true };
}, async () => requireEmbeddedApproval("mep_apply_preview", { preview_id: "preview-1" }));

await withBroker((request) => ({ requestId: request.requestId, hash: request.hash, approved: false, reason: "Học viên hủy." }), async () => {
  await assert.rejects(() => requireEmbeddedApproval("documentation_apply", { preview_id: "preview-2" }), (error) => error.code === "ApprovalDenied");
});

process.env.DSCONS_EMBEDDED_APPROVAL_TIMEOUT_MS = "1000";
await withBroker(() => null, async () => {
  await assert.rejects(() => requireEmbeddedApproval("family_build_apply", { preview_id: "preview-3" }), (error) => error.code === "ApprovalTimeout");
});
delete process.env.DSCONS_EMBEDDED_APPROVAL_TIMEOUT_MS;

const temp = await fs.mkdtemp(path.join(os.tmpdir(), "dscons-pid-guard-"));
const sessionPath = path.join(temp, "session.json");
await fs.writeFile(sessionPath, JSON.stringify({ port: 1, secret: "secret", processId: 999, startedAtUtc: "now" }));
process.env.DSCONS_MCP_SESSION_PATH = sessionPath;
process.env.DSCONS_MCP_EXPECTED_REVIT_PID = "1000";
await assert.rejects(() => callRevit("document_info"), /PID mismatch/);
await fs.rm(temp, { recursive: true, force: true });

console.log("PASS embedded approval: allow/deny/timeout, hash binding, write allowlist, Revit PID guard");
