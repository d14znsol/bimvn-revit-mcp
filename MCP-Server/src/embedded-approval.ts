import crypto from "node:crypto";
import net from "node:net";

const WRITE_TOOLS = new Set([
  "mep_apply_preview",
  "mep_create_route",
  "mep_connect",
  "mep_disconnect",
  "mep_move_route",
  "mep_change_type",
  "mep_change_size",
  "model_create_batch",
  "bim_changeset_apply",
  "documentation_apply",
  "family_axial_fan_apply",
  "family_load_place_apply",
  "family_build_apply",
  "cad_to_revit_apply",
  "coordination_issue_report_apply",
  "coordination_section_apply",
  "model_transfer_apply",
]);

const DIRECT_MEP_WRITES = new Set([
  "mep_create_route", "mep_connect", "mep_disconnect", "mep_move_route", "mep_change_type", "mep_change_size",
]);

export class EmbeddedApprovalError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.code = code;
  }
}

export function isEmbeddedWriteTool(toolName: string): boolean {
  return WRITE_TOOLS.has(toolName);
}

export function approvalHash(toolName: string, argumentsJson: string): string {
  return crypto.createHash("sha256").update(`${toolName}\n${argumentsJson}`, "utf8").digest("hex");
}

function readLine(socket: net.Socket, timeoutMs: number): Promise<string> {
  return new Promise((resolve, reject) => {
    let buffer = "";
    const timeout = setTimeout(() => finish(new EmbeddedApprovalError("ApprovalTimeout", "Đã hết thời gian chờ học viên xác nhận.")), timeoutMs);
    const finish = (error?: Error, line?: string): void => {
      clearTimeout(timeout);
      socket.off("data", onData);
      socket.off("error", onError);
      socket.off("close", onClose);
      if (error) reject(error); else resolve(line ?? "");
    };
    const onData = (chunk: Buffer): void => {
      buffer += chunk.toString("utf8");
      const newline = buffer.indexOf("\n");
      if (newline >= 0) finish(undefined, buffer.slice(0, newline));
    };
    const onError = (error: Error): void => finish(error);
    const onClose = (): void => finish(new EmbeddedApprovalError("ApprovalDisconnected", "Khung Chat AI đã ngắt trước khi xác nhận."));
    socket.on("data", onData);
    socket.once("error", onError);
    socket.once("close", onClose);
  });
}

export async function requireEmbeddedApproval(toolName: string, args: Record<string, unknown>): Promise<void> {
  if (process.env.DSCONS_EMBEDDED_CHAT !== "1" || !isEmbeddedWriteTool(toolName)) return;
  if (DIRECT_MEP_WRITES.has(toolName)) {
    throw new EmbeddedApprovalError("PreviewRequired", "Chat AI không được gọi lệnh MEP ghi trực tiếp. Hãy dùng mep_preview rồi mep_apply_preview để học viên xem và xác nhận đúng preview.");
  }

  const pipeName = process.env.DSCONS_EMBEDDED_APPROVAL_PIPE?.trim();
  const secret = process.env.DSCONS_EMBEDDED_APPROVAL_SECRET?.trim();
  const expectedPid = Number(process.env.DSCONS_MCP_EXPECTED_REVIT_PID ?? 0);
  if (!pipeName || !secret || !Number.isInteger(expectedPid) || expectedPid <= 0) {
    throw new EmbeddedApprovalError("ApprovalUnavailable", "Phiên Chat AI thiếu kênh xác nhận an toàn; thao tác ghi đã bị chặn.");
  }

  const argumentsJson = JSON.stringify(args);
  const hash = approvalHash(toolName, argumentsJson);
  const requestId = crypto.randomUUID();
  const request = { requestId, toolName, argumentsJson, hash, expectedPid, secret };
  const timeoutMs = Math.max(1_000, Number(process.env.DSCONS_EMBEDDED_APPROVAL_TIMEOUT_MS ?? 600_000));
  const socket = net.createConnection(`\\\\.\\pipe\\${pipeName}`);
  try {
    await new Promise<void>((resolve, reject) => {
      const timeout = setTimeout(() => reject(new EmbeddedApprovalError("ApprovalUnavailable", "Không kết nối được với khung xác nhận Chat AI.")), 10_000);
      socket.once("connect", () => { clearTimeout(timeout); resolve(); });
      socket.once("error", (error) => { clearTimeout(timeout); reject(error); });
    });
    socket.write(`${JSON.stringify(request)}\n`, "utf8");
    const response = JSON.parse(await readLine(socket, timeoutMs)) as {
      requestId?: string; hash?: string; approved?: boolean; reason?: string;
    };
    if (response.requestId !== requestId || response.hash !== hash) {
      throw new EmbeddedApprovalError("ApprovalMismatch", "Phản hồi xác nhận không khớp yêu cầu hiện tại.");
    }
    if (!response.approved) {
      throw new EmbeddedApprovalError("ApprovalDenied", response.reason || "Học viên đã hủy thao tác ghi.");
    }
  } catch (error) {
    if (error instanceof EmbeddedApprovalError) throw error;
    throw new EmbeddedApprovalError("ApprovalUnavailable", `Không thể xác nhận thao tác ghi: ${error instanceof Error ? error.message : String(error)}`);
  } finally {
    socket.destroy();
  }
}
