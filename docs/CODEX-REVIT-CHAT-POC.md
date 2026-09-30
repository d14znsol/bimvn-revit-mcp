# Chat AI trong Revit — Codex, Claude và Antigravity (thử nghiệm Revit 2023/2025)

Nút **Chat AI · Thử nghiệm** mở một panel WPF duy nhất ở cạnh phải Revit 2023 hoặc 2025.
Người dùng chọn **Codex**, **Claude** hoặc **Antigravity** ngay trong panel.
Add-in không nhận, không đọc và không lưu API key, token hay credential path; mỗi
provider chỉ dùng trạng thái đăng nhập sẵn có của CLI tương ứng.

Đây là POC source/offline. Không coi việc compile, render WPF hoặc kiểm tra
protocol là chứng nhận runtime Revit hay LOD/Family.

## Trạng thái provider

| Provider | Cách tích hợp | Trạng thái an toàn hiện tại |
| --- | --- | --- |
| Codex | Codex CLI `app-server`, streaming, conversation/resume | Có đường tích hợp hiện hữu; dùng capability manifest DSCons và approval broker chung. |
| Claude | Claude Code CLI `stream-json` với `--bare`, `--strict-mcp-config`, `--tools ""`, restricted mode và không hiện permission prompt | Cần Claude Code **>= 2.1.259**. Máy đã khảo sát là 2.1.177 nên panel báo cần nâng cấp; không tự cập nhật và không dùng chế độ yếu hơn. |
| Antigravity | CLI headless NDJSON trong workspace cô lập, `--sandbox` | Chỉ bật sau khi `init.tools` chứng minh chính xác inventory DSCons, không có shell, ghi file, web, subagent hay MCP ngoài DSCons. Nếu không chứng minh được, panel báo “chưa hỗ trợ an toàn”. |

Codex vẫn dùng luồng `app-server`; Claude/Antigravity không phải lựa chọn API
key. Khi cần đăng nhập, panel chỉ hướng dẫn chạy lệnh chính thức của CLI ngoài
Revit; không tự động đăng nhập hoặc nâng cấp.

## Usage/quota

Panel dùng contract `UsageSnapshot` độc lập với chat stream. Mỗi snapshot có
provider/plan (khi provider công bố), thời điểm quan sát, confidence và các
window `session_5h`, `weekly`, `requests`, `tokens` hoặc provider-specific;
credit/overage là trường riêng. UI hiển thị `Phiên hiện tại`, `Tuần` và
`Credits/overage` với một trong ba trạng thái:

- `Trực tiếp`: dữ liệu machine-readable của provider.
- `Một phần`: chỉ biết một phần như remaining/reset/limit-hit.
- `Không khả dụng`: không có số giả; panel hướng dẫn mở Usage của provider.

Codex đọc `account/rateLimits/read` và nhận
`account/rateLimits/updated` từ app-server đã kiểm. Cửa sổ 300 phút được gắn
nhãn 5 giờ và 10080 phút được gắn nhãn tuần; API request/token headers không
được coi là quota sản phẩm. Claude/Antigravity hiện trả `Không khả dụng` cho
đến khi CLI đã pin cung cấp quota headless có cấu trúc; không scrape TUI.
Snapshot cache ngắn, refresh khi kết nối/kết thúc turn/người dùng yêu cầu hoặc
provider báo limit. Lỗi quota không làm hỏng chat và raw response/credential
không được ghi vào history.

Ngày 26/09/2026, probe chỉ đọc trên runtime thật đã xác nhận cùng contract ở cả
Revit 2023 và Revit 2025 với Codex CLI `0.154.0-alpha.6.2`: đúng 56 DSCons
tools, không có MCP ngoại lai dùng được, tài khoản ChatGPT đã xác thực, tám model
và endpoint quota có cấu trúc. Hai window live có duration `300` phút và
`10080` phút, tương ứng `Phiên hiện tại · 5 giờ` và `Tuần`; probe không gửi turn
và không thực hiện model write. Đây là bằng chứng capability/runtime của đúng
CLI đã kiểm, không phải cam kết rằng mọi plan luôn có cùng hai window.

## Bảo vệ MCP và thay đổi model

Mọi provider cùng đi qua một `EmbeddedApprovalServer`:

`Preview rollback → người dùng chat xác nhận → Apply → verification read-back`.

Chỉ broker này được phép mở ranh giới thao tác ghi. Các câu chấp thuận hợp lệ:
`đồng ý`, `thực hiện`, `tiếp tục`, `làm đi`, `ok`, `xác nhận`; các câu hủy gồm
`hủy`, `không thực hiện`, `dừng lại`, `thôi`. Token xác nhận chỉ dùng một lần,
gắn với đúng Preview/PID/document hiện tại. Provider switch bị khóa trong khi
có turn hoặc approval đang chờ.

Panel tạo một config MCP tạm chỉ có `dscons_embedded`. Capability manifest được
sinh từ registry Node, chứa tool names đã sắp xếp, write metadata và SHA-256 của
schema canonical. Số tool chỉ là thông tin hiển thị; thiếu/thừa tool, fingerprint
không khớp hoặc MCP ngoài DSCons đều fail-closed.

## Giao diện, lịch sử và Bộ nhớ

Selector gồm Trợ lý AI, Model và Cuộc trò chuyện; bố cục được thiết kế cho chiều
rộng 320, 440 và 680 DIP (DPI 100/125/150), không có thanh cuộn ngang tin nhắn.
Model và lịch sử đổi theo provider. History schema v2 lưu `ProviderId` và
`ProviderConversationId`; record v1 với `ThreadId` được migrate riêng thành
conversation Codex, không thể bị Claude/Antigravity resume.

Bộ nhớ chỉ lưu khi người học chủ động yêu cầu và chỉ nhận sở thích bền vững.
Nó không lưu token, API key, credential path, PID, preview/element ID, raw tool
arguments hay trạng thái model tạm thời. File history nằm cục bộ trong
`%LOCALAPPDATA%\DSCons\RevitMcp\Chat\`, không nằm trong RVT.

## Gate runtime còn mở

Source/offline đã có đủ provider selector, quota contract, DSCons inventory
fingerprint, approval broker và fake-process boundary. Codex app-server runtime
read-only cùng structured quota đã PASS đúng fixture trên Revit 2023 rồi Revit
2025. Claude `2.1.177` trên máy hiện tại thấp hơn minimum đã pin `2.1.259`;
Antigravity `1.0.6` chưa có headless JSON/NDJSON quota/tool-inventory contract
đã kiểm chứng. Hai provider sau hoàn tất theo nhánh fail-closed: panel không mở
session yếu hơn, không hiển thị quota giả và không scrape TUI. Chỉ khi CLI tương
lai cung cấp protocol phù hợp mới chạy lại stream/cancel/resume/isolation trên
Revit 2023 trước, rồi lặp fixture trên 2025. Không tự nâng cấp, đăng nhập hoặc
cấu hình tài khoản; không Save/Sync và chỉ dùng Project/Family test copy.

## Computer Use: chỉ là UI fallback có chứng cứ

`revit_computer_use_assess` là tool Node chỉ đọc, không điều khiển Revit. Nó
đánh giá evidence của bốn workflow whitelist: Tag Label, Sweep/ProfileSketch
label, một số Family parameter association và đổi Shaded/Realistic để chụp
visual evidence. Mỗi run phải có fresh anchor, screenshot nguyên độ phân giải
trước/sau, dialog/security observation, cancel và Revit read-back độc lập.

Một workflow chỉ có thể chuyển từ `operator_assisted_only` sang
`eligible_for_product_review` khi đạt ít nhất 20 run an toàn liên tiếp và phủ
đủ Revit 2023/2025, DPI 100/125/150%, 1920×1080, single/multi-monitor,
docked/floating pane, dialog che khuất, security boundary và cancel probe.
Harness tự động loại run có thao tác sai model, Save/Sync, click security,
read-back không khớp hoặc screenshot không phải nguyên độ phân giải. Kết luận
đủ điều kiện review vẫn không phải chứng nhận tự động hay quyền Apply.

Assessment trả kèm `harness_contract`: re-anchor document/view/selection →
screenshot nguyên độ phân giải → detect dialog/security boundary → đúng một
whitelisted action → screenshot → read-back Revit độc lập. Budget là một action,
20 giây mỗi bước/120 giây toàn run, luôn có cancel; mọi trạng thái không rõ phải
dừng và phân loại blocked/cancelled, không tiếp tục qua security dialog.
