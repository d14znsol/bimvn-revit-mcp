# Alignment with the two reference repositories

This project was checked against:

- `shuotao/REVIT_MCP_study` — the primary learning/reference architecture:
  <https://github.com/shuotao/REVIT_MCP_study>
- `NCO-1986/Revit_mcp` — the larger product-oriented implementation:
  <https://github.com/NCO-1986/Revit_mcp>

## Shared architecture confirmed

Both references separate the MCP-facing process from the Revit-side plugin:

```text
AI client --stdio--> MCP Server --local socket--> Revit Add-in
                                                   └─ ExternalEvent → Revit API
```

The AI client launches the MCP server. The Revit add-in is installed in Revit
and exposes a local bridge. A tool is a capability registered in this pair;
it is not a Ribbon panel or an independent add-in project.

## Decisions for DSCons

| Reference concept | DSCons implementation |
| --- | --- |
| Node/TypeScript stdio MCP server | `MCP-Server/` |
| Revit C# add-in bridge | `MCP/` |
| Tool registry/schema modules | `MCP-Server/src/tools/` |
| Revit command implementation | `MCP/Commands/` |
| ExternalEvent/main-thread boundary | `MCP/Core/ExternalEventBridge.cs` |
| Local authenticated transport | `MCP/Core/BridgeServer.cs` + session secret |
| Project docs and setup | `docs/`, `scripts/`, `integrations/` |

DSCons additionally keeps `MCP.CoreRuntime/` for hot reload and `contracts/`
for a stable loader/runtime boundary. These are internal implementation
details, not additional MCP servers and not AI-client configuration targets.

## Deliberate scope difference

`NCO-1986/Revit_mcp` is a broader all-.NET product with Guardian, dashboard and
many Revit commands. DSCons v1 is intentionally MEP-first and targets Revit
2023/2025 while its runtime matrix is being validated. The difference is scope,
not a change to the MCP architecture.

## Client configuration rule

All supported clients launch the same entry point:

```text
MCP-Server/build/index.js
```

Only the client configuration location differs. No client should point to
`MCP.CoreRuntime`, a loader DLL, or an individual C# command file.
