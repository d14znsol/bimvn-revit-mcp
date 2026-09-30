# Hướng dẫn học viên — một buổi cài đặt DSCons Revit MCP

## Mục tiêu của buổi này

DSCons Revit MCP là một bộ sản phẩm độc lập. Buổi hướng dẫn trong chương trình
phễu chỉ giúp học viên:

1. xác định đúng phiên bản Revit và client AI;
2. kiểm tra máy bằng lệnh chỉ-đọc;
3. cài add-in và cấu hình đúng một client sau khi học viên xác nhận;
4. tự mở Revit rồi kiểm tra kết nối chỉ-đọc;
5. biết cách gỡ hoặc khôi phục khi cần.

Phần cài đặt không ghi model. Sau khi kết nối PASS, người phụ trách có thể chạy
pilot 60 phút gồm route MEP, quantity, Sheet và Family trên Project copy được
phép. Pilot là phase riêng và mọi write vẫn cần xác nhận.

## Thời lượng dự kiến: 60 phút

- 5 phút: giới thiệu mục tiêu và phạm vi.
- 10 phút: hỏi phiên bản Revit/client và chạy `Check`.
- 10 phút: giải thích kết quả bằng ngôn ngữ dễ hiểu.
- 15 phút: Install/configure sau khi bạn xác nhận.
- 10 phút: bạn tự mở Revit và Agent kiểm tra kết nối chỉ-đọc.
- 5 phút: hướng dẫn uninstall/rollback.
- 5 phút: dự phòng và ghi nhận câu hỏi.

Nếu máy có lỗi cần nghiên cứu thêm, Agent sẽ ghi phiếu hỗ trợ và nói rõ phần
nào chưa hoàn tất; không lặp thao tác cài đặt nhiều lần chỉ để giữ đúng thời gian.

## Trước khi bắt đầu

Chuẩn bị Windows, một bản Revit đã cài và đăng nhập client AI mà bạn muốn dùng:
Codex, ClaudeCode hoặc Antigravity. Khi đi từ GitHub source, prompt tại
`START_HERE.md` cho phép Agent tự cài Git, Node.js LTS/npm và .NET SDK 10 chính
thức bằng `winget`, rồi tự clone/build; học viên không cần tự gõ các lệnh đó.
Với learner release đã đóng gói, Agent có thể bỏ qua Git/.NET/build nếu artifact
đúng năm đã có sẵn.

Bạn cũng cần có quyền cài phần mềm trên máy và biết chính xác phiên bản Revit
đang dùng. Ví dụ, bộ thông tin chuẩn bị là:

> Windows 11, Revit 2023, Codex, có quyền cài phần mềm.

Nếu tham gia pilot, bạn cần thêm một Project copy local và một thư mục demo
được phép ghi. Agent chỉ dò Autodesk `.rft` đã cài cục bộ, đúng năm, khi
Blueprint đã nêu rõ behavior/category/Part Type (ví dụ quạt/bơm thường là
Mechanical Equipment, cửa gió Air Terminal, van gió Duct Accessory). Agent
không tự tìm, tải hoặc thay thế Family RFA của hãng hay chọn Family gần giống.
Với Family nguồn, catalogue hoặc transfer, kỹ sư phải xác nhận có RFA tương
thích, catalogue hoặc template chuẩn nào; dữ liệu thiếu/mơ hồ được hỏi-gộp rồi
chặn đúng phạm vi phụ thuộc. Generic Model chỉ có thể là fallback khi behavior
tương đương, Blueprint cho phép và Revit read-back xác nhận category; fitting,
2D, Profile, Annotation và Tag chuyên biệt không được fallback. Agent sẽ chỉ
cho bạn vị trí **Create → Family Category and Parameters** để hiểu thao tác,
nhưng không yêu cầu bạn tự tìm `.rft` hoặc sửa tay.

Hãy ghi lại hai thông tin sau. Ví dụ câu trả lời:

> Revit 2023, Codex.

Nếu bạn muốn dùng Revit khác, thay `2023` trong các lệnh bên dưới bằng đúng năm
đang cài. Nếu learner release chưa có artifact đúng năm, Agent sẽ giải thích
nguyên nhân và hướng xử lý; không dùng DLL của năm khác để thay thế.

## Bước 1 — Kiểm tra trước khi cài

Mở PowerShell tại thư mục gốc của bộ kit và chạy:

```powershell
.\scripts\student-setup.ps1 -Mode Check -RevitVersion 2023 -Client Codex
```

`Check` chỉ đọc: nó không cài gì, không sửa cấu hình client, không mở/đóng
Revit và không sửa model. Nó kiểm tra Node/npm, phần phụ thuộc của MCP Server,
API Revit đúng năm, artifact, client và trạng thái Revit.

Agent cần giải thích kết quả như sau:

- `PASS`: điều kiện đó đã được tìm thấy.
- `CẦN KHẮC PHỤC`: có lỗi kỹ thuật; Agent phải nói nguyên nhân và đề xuất cách
  sửa an toàn, ví dụ cài Node.js LTS hoặc chọn đúng artifact.
- `CẦN BẠN THỰC HIỆN`: cần việc bên ngoài như đóng Revit hoặc xác nhận cài đặt.

Nếu thiếu Git, Node/npm hoặc .NET SDK 10 trong quy trình GitHub, Agent phải dùng
`winget` cài đúng các gói `Git.Git`, `OpenJS.NodeJS.LTS` và
`Microsoft.DotNet.SDK.10` còn thiếu, làm mới PATH rồi đọc lại version. `npm` đi
kèm Node.js LTS, không cài riêng từ nguồn khác. Nếu thiếu dependency Node nhưng
đã có `package-lock.json`, Agent tự chạy `npm ci` và build Node; học viên không
phải tự gõ `npm install`. Chỉ chuyển sang `CẦN BẠN THỰC HIỆN` khi thiếu winget,
UAC/quyền bị chặn hoặc installer chính thức thất bại.

## Bước 2 — Xác nhận cài đặt

Agent phải nói rõ cài đặt sẽ làm gì: chép add-in đúng năm Revit, có thể chuẩn bị
Node runtime trong thư mục kit và thêm/chỉnh đúng entry MCP của client đã chọn.
Agent phải hỏi lại một câu có ví dụ, chẳng hạn:

> Bạn có xác nhận cài DSCons Revit MCP cho Revit 2023 và cấu hình Codex không?
> Ví dụ trả lời: `Xác nhận cài Revit 2023 và cấu hình Codex.`

Chỉ sau câu xác nhận cụ thể đó, hãy đóng hoàn toàn mọi cửa sổ Revit. Không cần
Agent tự đóng Revit; học viên tự đóng để biết dữ liệu của mình đang ở trạng thái
nào.

## Bước 3 — Cài đặt có kiểm soát

Sau khi Revit đã đóng và Agent đã xác nhận lại đúng năm/client, chạy:

```powershell
.\scripts\student-setup.ps1 -Mode Install `
  -RevitVersion 2023 -Client Codex `
  -ConfirmInstall -ConfirmConfigure
```

Script sẽ tạo backup và ownership record để lần sau chỉ gỡ những gì bộ kit đã
tạo. Nó không mở Revit và không Save/Sync model. Nếu script báo lỗi, không chạy
lại Install liên tục; gửi nguyên văn lỗi cho Agent để được chẩn đoán.

## Bước 4 — Tự mở Revit và kiểm tra kết nối

Sau khi Install báo hoàn tất, học viên tự mở Revit. Có thể mở một project copy
an toàn nếu muốn nhìn thấy tên document/view; không cần ghi bất kỳ phần tử nào.

Trong panel **DSCons MCP**, nếu nút đang ghi **Bật MCP** thì bấm để kết nối; nút
sẽ đổi thành **Tắt MCP** khi bridge Revit hoạt động. Nút này không chạy hoặc kill
Node của AI client và không sửa model. Xem
[hướng dẫn Ribbon](../MCP-RIBBON-CONTROLS.md).

 Riêng Revit 2023 có nút **Chat AI · Thử nghiệm**. Panel này dùng tài khoản Codex
 của học viên và bắt buộc chat `đồng ý`/`thực hiện`/`tiếp tục`/`làm đi`/`ok`/`xác nhận`
 cho từng thao tác ghi sau Preview; `hủy`/`thôi` dừng yêu cầu. Thanh trạng thái hiển thị
 AI đang đọc Project hay gọi tool nào, không dùng đồng hồ đếm. `Enter` gửi và
 `Shift+Enter` xuống dòng. Lịch sử tách theo từng file Revit; học viên chỉ lưu Bộ nhớ
 khi chủ động nói `hãy nhớ ...` hoặc mở nút **Bộ nhớ** để quản lý.
 Panel tự thích ứng khi kéo hẹp/rộng; mở **Hoạt động công cụ** để xem AI đang đọc
 Project, Preview hay Apply mà không cần hiểu tên tool kỹ thuật.
Không dùng panel để Save/Sync. Xem [hướng dẫn POC](../CODEX-REVIT-CHAT-POC.md).

Agent chỉ thực hiện các đọc kiểm tra tương ứng với client đã cài:

1. trạng thái bridge;
2. `document_info`;
3. `get_active_view`;
4. `get_selection` nếu cần biết selection;
5. capabilities.

Kết quả đạt khi client gọi được MCP Server, bridge nhận kết nối và các thông tin
đọc lại khớp với Revit đang mở. Đây là kiểm tra kết nối, không phải chứng nhận
mọi workflow modeling hay mọi phiên bản Revit. Sau khi kết nối PASS, xem
[buổi thực hành 60 phút](60-MINUTE-MCP-PILOT.md).

## Bước 5 — Gỡ hoặc khôi phục khi cần

Chỉ gỡ trên máy đã có ownership record và sau khi tự đóng Revit:

```powershell
.\scripts\student-setup.ps1 -Mode Uninstall `
  -RevitVersion 2023 -Client Codex `
  -ConfirmUninstall
```

Nếu cấu hình client hoặc add-in đã bị sửa ngoài ownership record, script sẽ dừng
để bảo vệ dữ liệu. Không xóa thủ công thư mục backup. Hãy gửi báo cáo cho người
phụ trách để xem xét khôi phục từng mục.

## Những điều không làm trong buổi cài đặt

- Không tự mở/đóng Revit bằng Agent.
- Không ghi, xóa, kết nối, di chuyển hoặc sửa model.
- Không Save/Sync, publish, print hoặc export.
- Không cài MCP.CoreRuntime như một MCP Server thứ hai.
- Không dùng DLL Revit khác năm để vượt qua thiếu artifact.
- Không đưa secret, tên khách hàng, đường dẫn cá nhân hoặc file riêng tư vào log.

## Khi cần hỗ trợ

Ghi lại phiên bản Revit, client, lệnh đã chạy và thông báo lỗi. Không gửi secret
hay toàn bộ model. Dùng [MCP Log](templates/mcp-log.md) để ghi lại
Check/Install/kết nối và [Bug Report](templates/bug-report.md) nếu cần gửi một
lỗi cụ thể. Luôn ghi rõ phần nào đã kiểm tra và phần nào chưa kiểm tra.
