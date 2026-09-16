# DSCons Revit MCP troubleshooting

Use this guide in order: validate the Node client entrypoint, then the Revit
bridge, then the active document. `MCP-Server/build/index.js` is the only MCP
entrypoint; do not configure `MCP.CoreRuntime` in an AI client.

## Antigravity cannot load MCP servers

| Symptom | Cause | Safe check / recovery |
| --- | --- | --- |
| `MODULE_NOT_FOUND` for an old project path | Project was moved or Node entrypoint was not rebuilt. | Run `scripts/build-mcp.ps1`. With approval, run `scripts/configure-antigravity.ps1 -ConfirmConfigure`, then Refresh. |
| `Failed to load MCP servers` immediately after Refresh | A config is missing, invalid JSON, stale, or starts with a UTF-8 BOM. Windows PowerShell's legacy UTF-8 writer emits BOM, while Node `JSON.parse` rejects it. | Run `scripts/configure-antigravity.ps1 -Check` (read-only). With approval rerun `-ConfirmConfigure`, which writes UTF-8 without BOM. |
| Config is correct but the previous error remains | Antigravity retained cached server state. | Refresh MCP Servers; restart Antigravity only if Refresh does not clear it. |

The Antigravity script validates and configures these locations while preserving
unrelated MCP entries:

- `%USERPROFILE%\.gemini\config\mcp_config.json`
- `%USERPROFILE%\.gemini\antigravity-ide\mcp_config.json`
- `%USERPROFILE%\.gemini\antigravity\mcp_config.json`

## Revit bridge unavailable

Open a supported Revit version with DSCons Revit MCP loaded and an active
document. The bridge session is at
`%LOCALAPPDATA%\DSCons\RevitMcp\session.json`; it is specific to the running
Revit process and must not be reused. Inspect `system_status` and the latest
Revit journal before changing configuration.

Nếu panel DSCons MCP đang hiện nút **Bật MCP**, hãy bấm nút đó rồi Refresh MCP
Servers ở client nếu client chưa tự reconnect. Nút này chỉ bật/tắt bridge phía
Revit; không kill Node của Codex/Claude/Antigravity.
Xem [Nút điều khiển MCP trong Revit](MCP-RIBBON-CONTROLS.md).

## Network query reaches a logical connector

Logical Revit connectors do not always expose origin, coordinate system,
dimensions, connection state or references. Those physical-only properties can
throw `Autodesk.Revit.Exceptions.InvalidOperationException`. DSCons returns
null or empty physical evidence while retaining available topology; one logical
connector must never fail the entire network query.

## Revit 2023 startup has a Newtonsoft binding conflict

Revit 2023 owns `Newtonsoft.Json` 12.0.0.0 and Revit 2025 owns 13.0.0.0. The
CoreRuntime references Revit's assembly and must not ship another Newtonsoft
DLL. If a legacy runtime folder still contains `Newtonsoft.Json.dll`, publish
the current CoreRuntime after Revit is closed, or use Reload Core as appropriate.

## Revit 2027 build fails on ElementId

Revit 2027 no longer exposes `ElementId.IntegerValue`. The compatibility layer
in `MCP/Commands/RevitIdCompatibility.cs` is the only place allowed to call the
legacy member. Rebuild with `scripts/build-mcp.ps1 -RevitVersion 2027`; do not
copy a 2023/2025 CoreRuntime into the 2027 artifact.

## V2 batch preview fails before apply

`bim_changeset_preview` intentionally executes a real Revit operation inside a
rolled-back TransactionGroup. A preview failure is useful evidence and leaves
no persistent model change. On blank/default templates, use a straight route:
a bend may only fail because Routing Preferences lack a compatible elbow family.
