# DSCons Revit MCP

MCP độc lập kết nối Codex, Claude Code hoặc Google Antigravity với Autodesk
Revit MEP. Project này không phải module của `DSCons-MEP-Tools`.

Repository công khai chứa mã nguồn, tài liệu, script và kiểm thử offline. Nó
không chứa Revit API DLL, model/RFA, artifact đã build, dữ liệu khách hàng,
runtime evidence hay ledger nội bộ `.agents`.

## Tương thích

| Revit | Target framework |
| --- | --- |
| 2019–2020 | `net47` |
| 2021–2024 | `net48` |
| 2025–2026 | `net8.0-windows` |
| 2027 | `net10.0-windows` |

`.NET SDK 10` là compiler baseline. Compile thành công không đồng nghĩa đã
runtime-certified; Revit 2027 vẫn là POC cho đến khi có copied-model runtime
evidence tương ứng. Xem [COMPATIBILITY.md](docs/COMPATIBILITY.md).

## Bắt đầu từ source

Khuyến nghị mở [START_HERE.md](START_HERE.md), sao chép prompt duy nhất và dán
vào AI client. Prompt cho phép AI tự cài Git, Node.js LTS/npm và .NET SDK 10 từ
`winget`, clone repository, build và chạy Check; học viên không phải tự cài từng
prerequisite. AI vẫn phải dừng tại checkpoint trước khi cài add-in, sửa cấu hình
client hoặc tác động tới Revit/model.

Nếu tự thao tác source, yêu cầu là Windows, Git, Node.js/npm, .NET SDK theo
`global.json`, và Revit/API bundle đúng năm nếu muốn build add-in.

```powershell
npm ci --prefix .\MCP-Server
npm run build --prefix .\MCP-Server
node .\tests\mcp_protocol_smoke.mjs
.\scripts\test-contracts.ps1
```

Build add-in cho một năm hoặc mọi năm có đủ dependency:

```powershell
.\scripts\build-mcp.ps1 -RevitVersion 2023 -Configuration Release
.\scripts\build-mcp.ps1 -RevitVersion All -Configuration Release
```

Không chạy install, uninstall hoặc cấu hình AI client khi chưa có xác nhận
riêng. Runtime test chỉ dùng model copy theo
[RUNTIME-TEST.md](docs/RUNTIME-TEST.md).

## Kiến trúc

```text
AI client
  └─ MCP stdio → MCP-Server/build/index.js
                    └─ TCP loopback + session secret
                         └─ MCP/ Revit add-in bridge
                              └─ MCP.CoreRuntime/ (hot reload nội bộ)
                                   └─ ExternalEvent → Revit API
```

`MCP-Server/` là MCP Server duy nhất. `MCP/` là Revit add-in bridge.
`MCP.CoreRuntime/` không phải server và không được đăng ký vào AI client.

## Tài liệu

- [PROJECT.md](PROJECT.md) — ranh giới và source of truth.
- [ARCHITECTURE.md](docs/ARCHITECTURE.md) — kiến trúc runtime.
- [CLIENT-SETUP.md](docs/CLIENT-SETUP.md) — cấu hình các AI client.
- [SAFETY.md](docs/SAFETY.md) — giới hạn Local/Central và Preview → Apply.
- [MIGRATION.md](docs/MIGRATION.md) — thay thế hoặc khôi phục MCP cũ.
- [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) — chẩn đoán lỗi thường gặp.
- [MCP-Server/README.md](MCP-Server/README.md) — Node MCP server.
- [MCP/README.md](MCP/README.md) — Revit add-in bridge.

## License

Copyright © 2026 Phạm Quang Huy. Source được phép sử dụng và sửa đổi cho học
tập cá nhân, phi thương mại; không được bán, dùng cho dịch vụ trả phí hoặc phân
phối lại nếu chưa có chấp thuận bằng văn bản. Xem [LICENSE](LICENSE) và
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
