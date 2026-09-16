import net from "node:net";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";

export interface SessionInfo {
  port: number;
  secret: string;
  processId: number;
  startedAtUtc: string;
}

export interface BridgeResponse {
  jsonRpc?: string;
  id?: string;
  success: boolean;
  resultJson?: string;
  errorCode?: string;
  errorMessage?: string;
}

const DEFAULT_PORT = 43827;
// A preview runs a real Revit transaction and then rolls its TransactionGroup
// back. Large models can need substantially longer than a read request while
// Revit processes registered updaters and warnings.
const DEFAULT_TIMEOUT_MS = 120_000;

function sessionPath(): string {
  return process.env.DSCONS_MCP_SESSION_PATH ??
    path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), "AppData", "Local"), "DSCons", "RevitMcp", "session.json");
}

async function readSession(): Promise<SessionInfo> {
  const raw = await fs.readFile(sessionPath(), "utf8");
  const session = JSON.parse(raw) as Partial<SessionInfo>;
  if (!session.secret) throw new Error("DSCons MCP session secret is missing. Start the DSCons MCP service in Revit.");
  return {
    port: Number(process.env.DSCONS_MCP_PORT ?? session.port ?? DEFAULT_PORT),
    secret: session.secret,
    processId: Number(session.processId ?? 0),
    startedAtUtc: String(session.startedAtUtc ?? "")
  };
}

function readLine(socket: net.Socket, timeoutMs: number): Promise<string> {
  return new Promise((resolve, reject) => {
    let buffer = "";
    const timeout = setTimeout(() => {
      cleanup();
      reject(new Error(`Revit bridge timed out after ${timeoutMs} ms.`));
    }, timeoutMs);

    const onData = (chunk: Buffer): void => {
      buffer += chunk.toString("utf8");
      const newline = buffer.indexOf("\n");
      if (newline >= 0) {
        cleanup();
        resolve(buffer.slice(0, newline));
      }
    };
    const onError = (error: Error): void => { cleanup(); reject(error); };
    const onClose = (): void => {
      cleanup();
      reject(new Error("Revit bridge closed before returning a response."));
    };
    const cleanup = (): void => {
      clearTimeout(timeout);
      socket.off("data", onData);
      socket.off("error", onError);
      socket.off("close", onClose);
    };
    socket.on("data", onData);
    socket.once("error", onError);
    socket.once("close", onClose);
  });
}

export async function callRevit(method: string, params: Record<string, unknown> = {}, clientName = "dscons-revit-mcp"): Promise<BridgeResponse> {
  const session = await readSession();
  const request = {
    jsonRpc: "2.0",
    id: `${Date.now()}-${Math.random().toString(36).slice(2)}`,
    secret: session.secret,
    method,
    paramsJson: JSON.stringify(params),
    clientName
  };

  const socket = net.createConnection({ host: "127.0.0.1", port: session.port });
  try {
    await new Promise<void>((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error("Could not connect to the Revit bridge.")), DEFAULT_TIMEOUT_MS);
      socket.once("connect", () => { clearTimeout(timeout); resolve(); });
      socket.once("error", (error) => { clearTimeout(timeout); reject(error); });
    });
    socket.write(`${JSON.stringify(request)}\n`, "utf8");
    const raw = await readLine(socket, DEFAULT_TIMEOUT_MS);
    return JSON.parse(raw) as BridgeResponse;
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error);
    throw new Error(`Cannot reach DSCons Revit bridge at 127.0.0.1:${session.port}. ${detail}`);
  } finally {
    socket.destroy();
  }
}
