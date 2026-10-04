# Nút điều khiển MCP trong Revit

Panel **DSCons MCP** có ba nút trên Revit 2023; các năm khác vẫn có hai nút MCP:

| Nút | Tác dụng |
| --- | --- |
| **Chat AI · Thử nghiệm** | Mở panel chat dock bên phải, chọn Codex/Claude/Antigravity bằng tài khoản CLI của học viên. Claude cần >= 2.1.259; Antigravity chỉ bật khi kiểm isolation fail-closed. Chỉ xuất hiện trên Revit 2023 trong POC. |
| **Bật/Tắt MCP** | Bật hoặc dừng CoreRuntime và cầu nối loopback bên trong Revit. Nhãn/icon đổi theo trạng thái. |
| **Cập nhật Code** | Reload CoreRuntime sau khi build/publish; chỉ hoạt động khi MCP đang bật. |

## Bật/Tắt thực sự điều khiển gì?

Nút này điều khiển **phía Revit** của kết nối:

- Khi **Tắt**, bridge ngừng nhận request mới, giải phóng listener/ExternalEvent,
  hủy preview token trong runtime và xóa `session.json` do đúng tiến trình Revit
  đó sở hữu.
- Khi **Bật**, add-in nạp CoreRuntime, mở lại bridge loopback và tạo session/secret
  mới để AI client kết nối.
- Không thao tác model, không Save/Sync và không cần đóng Revit.

Node MCP Server dùng stdio vẫn do Codex, Claude Code hoặc Antigravity quản lý.
Nút Ribbon không chạy, dừng hoặc `kill` tiến trình Node của client. Vì vậy sau khi
bật lại, nếu client chưa tự reconnect, dùng Refresh MCP Servers trong client.

## Cách dùng trong buổi học

1. Mở Revit và nhìn panel **DSCons MCP**.
2. Nếu nút đang ghi **Bật MCP**, bấm nút đó để kết nối; sau đó Refresh MCP Servers ở AI client nếu cần.
3. Nếu muốn tạm ngắt Revit khỏi AI client, bấm **Tắt MCP**. Model không bị thay đổi.
4. Không dùng **Cập nhật Code** khi MCP đang tắt; bật MCP trước.

Việc bấm nút là thao tác do học viên thực hiện trong giao diện Revit. Agent không
tự mở/đóng Revit hoặc điều khiển Ribbon nếu chưa được cho phép rõ ràng.

Xem [hướng dẫn Chat AI POC](CODEX-REVIT-CHAT-POC.md) để biết phiên bản Codex,
Claude, trạng thái Antigravity, cơ chế chat `đồng ý`/`hủy` sau Preview và
checklist runtime.
