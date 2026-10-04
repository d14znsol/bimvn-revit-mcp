# AI client setup

Ba client cùng chạy một local MCP-Server Node/TypeScript qua stdio; account subscription/permission vẫn do chính client quản lý. DSCons MCP không dùng hay yêu cầu API key.

MCP Server entrypoint là `MCP-Server/build/index.js`. Revit chỉ host plugin
bridge ở phía bên kia loopback; không chạy Ribbon tool, CoreRuntime DLL hoặc
từng command C# như một MCP server riêng.

Các template cấu hình MCP được đặt tại:

- `integrations/codex/mcp-config.toml.example`
- `integrations/claude-code/.mcp.json.example`
- `integrations/claude-desktop/claude_desktop_config.json.example`
- `integrations/antigravity/mcp_config.json.example`

Sau `scripts/build-mcp.ps1`, chỉ chạy script đúng client khi đã xác nhận:

```powershell
./scripts/configure-codex.ps1 -ConfirmConfigure
./scripts/configure-claude.ps1 -ConfirmConfigure
./scripts/configure-claude-desktop.ps1 -ConfirmConfigure
./scripts/configure-antigravity.ps1 -ConfirmConfigure
# Read-only validation: config path, JSON, UTF-8 BOM and entrypoint.
./scripts/configure-antigravity.ps1 -Check
```

Các script trên chỉ đăng ký cùng một entry point là
`MCP-Server/build/index.js`. Chúng không đăng ký `MCP.CoreRuntime`, DLL loader
hoặc từng C# command.

MCP-Server được chạy bằng `node`, còn plugin Revit `MCP/` và runtime nội bộ
chạy bên trong Revit. Antigravity dùng
`%USERPROFILE%\.gemini\antigravity\mcp_config.json`; Claude Desktop dùng
`%APPDATA%\Claude\claude_desktop_config.json`; Claude Code dùng `.mcp.json`;
Codex dùng cấu hình MCP của Codex. Script thay entry cũ
`mcp-server-for-revit` bằng `dscons-revit-mcp` trong đúng cấu hình client được
chọn. Không sửa client khác và không cài hay gỡ add-in Revit.
