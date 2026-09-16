# DSCons MCP-Server

This folder is the Node/TypeScript MCP stdio server launched by Codex, Claude Code/Desktop, Google Antigravity, or another MCP client. It follows the same responsibility boundary as `REVIT_MCP_study`: MCP protocol handling and host-facing tool schemas stay here; Revit API implementation stays in the add-in runtime.

It does not reference the Revit API. `src/tools/index.ts` is the registry
composer; schemas are grouped into `src/tools/read-tools.ts` and
`src/tools/mep-tools.ts`. The server discovers the live tool catalog from the
Revit add-in through the local loopback bridge and forwards `tools/call`
requests.

Build with:

```powershell
npm install
npm run build
```

The client entry point is `MCP-Server/build/index.js`.

The Revit-side folder intentionally remains separate:

```text
MCP-Server/
  src/index.ts       # MCP stdio entry point
  src/socket.ts      # loopback bridge client
  src/tools/         # MCP tool registry and schema modules
MCP/
  Core/              # Revit bridge, ExternalEvent and loader
  Commands/          # Revit API command implementations
```

`MCP-Server` is the only MCP server. `MCP` is the Revit add-in; neither is a
panel of `DSCons-MEP-Tools`. `MCP.CoreRuntime` is an internal reloadable
implementation used by the add-in and is never configured as an AI client.

The `bin/`, `obj/`, `build/`, and `artifacts/` folders are generated locally and are not source. A stale binary from the earlier C# scaffold must never be referenced by an AI client; all active client templates point to `MCP-Server/build/index.js`.
