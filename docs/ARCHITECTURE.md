# DSCons Revit MCP architecture

DSCons Revit MCP is an independent project. It shares development conventions with the Revit API Programming Kit, but it does not share source, DLLs or Ribbon panels with `DSCons-MEP-Tools`.

```text
Codex / Claude / Google Antigravity
             │ MCP stdio
             ▼
MCP-Server/  Node/TypeScript host-facing MCP protocol and tool schemas
             │ loopback TCP + session secret
             ▼
MCP/         stable Revit loader and manifest
             │ loads/reloads
             ▼
MCP.CoreRuntime/ replaceable Revit API runtime
             │ ExternalEvent
             ▼
Revit API
```

| Layer | Responsibility | Boundary |
| --- | --- | --- |
| `MCP-Server/` | MCP stdio, `initialize`, `tools/list`, `tools/call`, transport client, host-facing schemas and explicitly-approved local knowledge search | Never references Revit API or uploads local evidence |
| `MCP-Server/src/tools/` | Tool discovery fallback and JSON schemas grouped away from the protocol entry point | No model mutation |
| `contracts/` | Internal loader/runtime interface, error codes and preview/session DTOs | Not an MCP server; no Revit types |
| `MCP/` | Revit add-in, `.addin` manifest, Ribbon status/reload commands and bridge entry point | Revit API boundary |
| `MCP.CoreRuntime/` | Internal reloadable implementation used by the `MCP/` add-in | Not a server; never configured as an AI client |
| `domain/` | Shared MEP SOP and decision rules | No C# implementation |

The Revit add-in writes a random session secret to `%LocalAppData%\DSCons\RevitMcp\session.json`. The MCP server reads that file and sends the secret with every loopback request. Revit API calls are marshalled through `ExternalEvent`.

The loader is loaded once by Revit. The CoreRuntime is copied to a runtime directory and loaded from a byte copy on Revit 2019–2024 or a collectible load context on Revit 2025–2027. Changes to runtime command logic can therefore be built, published with `publish-hot-reload.ps1`, and activated with the `Reload Core` Ribbon command. The adjacent On/Off command stops or reloads only CoreRuntime and its loopback bridge/session; the Node stdio process remains owned by the AI client.

The v1 route planner accepts only axis-aligned segments. A diagonal segment is rejected before a Revit transaction starts. Fitting selection requires matching connector domain/profile and chooses the nearest compatible pair. Disconnect operations search only connectors that are actually connected to the other selected element.

The active host-facing MCP implementation follows the reference repositories:
`MCP-Server` is a Node/TypeScript stdio process and `MCP` is the C# Revit
add-in bridge. `MCP.CoreRuntime` is an internal hot-reload implementation
detail of that add-in; it is never registered directly with Codex, Claude or
Antigravity.

The tool registry is intentionally split by responsibility:

- `MCP-Server/src/tools/index.ts` composes the registry.
- `MCP-Server/src/tools/read-tools.ts` contains read-only schemas.
- `MCP-Server/src/tools/mep-tools.ts` contains MEP preview/write schemas.
- `MCP/Commands/` contains the corresponding Revit API implementation.

This separation is structural: adding a tool does not create a new MCP server,
Ribbon panel or Revit add-in. It adds one capability to the existing MCP
server/add-in pair.

`dscons_knowledge_search` is the one Node-local capability: it reads only
`TỔNG HỢP KHÓA HỌC.md` and `*_summary.md` below a path supplied by the user in
the current call. It returns bounded excerpts plus local citation metadata and
never crosses the Revit bridge, uploads a file, creates a persistent index or
packages private course content. The remaining Combine/Shop commands keep the
same bridge and Revit `ExternalEvent` boundary as every other Revit command.

The live `get_capabilities` response is the source of truth for tool availability, read/write classification, state scope, fresh-context requirements, prerequisites and declared limitations. When Revit is unavailable, the server uses the last capability cache and then the built-in v1 catalog.

## V2 BIM production pipeline

V2 keeps the same single MCP server and one Revit bridge. It adds no second server, Ribbon or client registration.

```text
bim_context_snapshot + bim_model_catalog
             │
             ▼
model_create_batch / bim_changeset_preview → bim_changeset_apply
             │                              (atomic + rollback preview)
             ▼
coordination_links / coordination_scan → quantity_takeoff
             │
             ▼
documentation_plan → documentation_apply
```

`bim_context_snapshot` prevents an agent from applying a long-lived plan to a different active document or view. `bim_model_catalog` exposes actual model IDs for types, systems, levels, MEP FamilySymbols, templates and title blocks; title blocks include measured sheet size/orientation from rollback-only temporary sheets. Change Set preview executes all supported operations in one Revit TransactionGroup and rolls it all back; apply commits every operation or rolls back every operation. Coordination is bounding-box triage with evidence, not automatic rerouting. BOQ is current-model evidence, not pricing or waste calculation. Documentation only uses explicit existing types/templates. Its Schedule flow first discovers which semantic BuiltInParameter fields are actually schedulable and whether each can sort/total, then requires learner-confirmed column order, sort/group priority and totals before apply. Its Sheet flow requires a learner-selected title block and layout proposal; auto-fit crops to explicit model elements, tests standard scales and verifies viewport plus label against a usable sheet region. Native schedules retain the exact hidden Comments filter; documentation never Save/Syncs, prints or publishes.
