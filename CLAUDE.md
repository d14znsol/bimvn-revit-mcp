# DSCons Revit MCP — canonical AI instructions

`AGENTS.md` routes all clients to this file and the project ledger. Human
installation guidance belongs in `docs/`; Revit API implementation belongs in
the add-in side.

## Runtime boundary

```text
AI client (Codex / Claude / Google Antigravity)
  └─ MCP stdio → MCP-Server/build/index.js
                    └─ loopback TCP + session secret
                         └─ MCP/ stable Revit add-in loader
                              └─ MCP.CoreRuntime/ internal reloadable runtime
                                   └─ ExternalEvent → Revit API
```

Only `MCP-Server/build/index.js` is configured in an AI client. `MCP/` is a
Revit add-in, not an MCP client. `MCP.CoreRuntime/` and `contracts/` are
internal implementation projects.

## Source of truth

- Protocol entry: `MCP-Server/src/index.ts`
- Loopback transport: `MCP-Server/src/socket.ts`
- Tool registry and schemas: `MCP-Server/src/tools/`
- Revit add-in entry: `MCP/Application.cs`
- Revit bridge and dispatcher: `MCP/Core/`
- Revit API command modules: `MCP/Commands/`
- Reloadable implementation: `MCP.CoreRuntime/`
- Shared internal DTOs and safety contracts: `contracts/`
- MEP operating rules: `domain/`

Do not add a second server entry point, a version-specific `.addin`, or a
direct AI API call. Use the scripts under `scripts/`. In the maintainer
workspace, update the private `.agents` ledger after meaningful changes when
that directory exists; public clones must not depend on it.

## Verification

```powershell
npm run build --prefix .\MCP-Server
node .\tests\mcp_protocol_smoke.mjs
.\scripts\test-contracts.ps1
```

Live Revit tests remain separate and must use copied models as described in
`docs/RUNTIME-TEST.md`.
