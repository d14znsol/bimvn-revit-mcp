# DSCons Revit MCP

Đây là một project MCP độc lập để kết nối AI client với Revit MEP 2019–2027.
Project không phải module của `DSCons-MEP-Tools` và không dùng chung source, DLL
hoặc Ribbon với bộ tool đó. Project cũng không thuộc một chương trình/khóa học;
một buổi trong chương trình phễu chỉ là buổi hướng dẫn cài đặt và kiểm tra kết
nối cho học viên.

Compiler baseline là .NET SDK 10 (`global.json`). TFM theo năm: 2019–2020
`net47`, 2021–2024 `net48`, 2025–2026 `net8.0-windows`, 2027
`net10.0-windows`. Không có Revit version mặc định; mọi lệnh build/install nhận
`-RevitVersion` rõ ràng. Revit 2027 là POC cho đến khi runtime-test đạt.
API Revit 2027 hiện được phát hiện tại phiên bản `27.0.10.13` trên máy build.

## Luồng runtime

```text
Codex / Claude / Google Antigravity
        │ MCP stdio
        ▼
MCP-Server/                  Node.js + TypeScript MCP server
        │ TCP loopback 127.0.0.1:43827 + session secret
        ▼
MCP/                         Revit Add-in bridge
        │ ExternalEvent
        ▼
MCP.CoreRuntime/             runtime Revit API có thể reload nội bộ
        ▼
Autodesk Revit API
```

`MCP-Server/` là MCP Server duy nhất. `MCP/` là plugin chạy bên trong Revit.
`MCP.CoreRuntime/` chỉ là implementation runtime bên trong plugin, không phải
MCP Server thứ hai và không được đăng ký trong Codex, Claude hoặc Antigravity.

## Bản đồ thư mục

| Thư mục | Vai trò |
| --- | --- |
| `MCP-Server/` | MCP protocol stdio, transport client và tool registry/schema |
| `MCP-Server/src/tools/` | Module catalog tool theo nhóm read/MEP/Family/Combine; không chứa Revit API |
| `MCP/` | Add-in entry point, manifest, bridge và Ribbon status/reload |
| `MCP/Commands/` | Các lệnh Revit API do bridge gọi |
| `MCP.CoreRuntime/` | Runtime reloadable nội bộ của add-in |
| `contracts/` | DTO, error code, preview và interface giữa loader/runtime |
| `domain/` | SOP và luật nghiệp vụ MEP dùng chung cho AI/người dùng |
| `docs/` | Kiến trúc, cài đặt, safety, migration và runtime test |
| `integrations/` | Template cấu hình Codex, Claude Code/Desktop và Antigravity |
| `scripts/` | Build, preflight, install, rollback, client configuration và hot reload |
| `installer/` | Layout artifact/manifest khi cài vào Revit |
| `tests/` | Contract, protocol smoke và runtime harness trên model copy |
| `.agents/` | Ledger riêng, chỉ có trong workspace maintainer và không thuộc public source |

## Source of truth

- MCP entry: `MCP-Server/src/index.ts`
- MCP transport: `MCP-Server/src/socket.ts`
- MCP tool registry: `MCP-Server/src/tools/index.ts`
- Tool modules: `MCP-Server/src/tools/read-tools.ts`, `mep-tools.ts`
- Revit add-in entry: `MCP/Application.cs`
- Revit bridge: `MCP/Core/BridgeServer.cs`, `ExternalEventBridge.cs`
- Revit command modules: `MCP/Commands/`
- Internal reload runtime: `MCP.CoreRuntime/`
- Canonical Revit manifest: `MCP/DSCons.RevitMcp.addin`

## Build và kiểm tra offline

```powershell
.\scripts\build-mcp.ps1 -RevitVersion All -Configuration Release
.\scripts\test-contracts.ps1
node .\tests\mcp_protocol_smoke.mjs
```

Build từng Revit target chạy tuần tự để tránh race trong
`obj/project.assets.json`; truyền `-RevitVersion` rõ ràng (hoặc `All` để dùng
các phiên bản đã cài). Không chạy install, uninstall hoặc configure client nếu
chưa có xác nhận rõ ràng. Runtime test phải dùng model copy theo
`docs/RUNTIME-TEST.md`.
