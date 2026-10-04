# Buổi thực hành DSCons Revit MCP — 60 phút

Buổi này diễn ra sau khi `student-setup -Mode Check` và cài/kết nối đã PASS.
Dùng Revit 2023 trước, một Project copy local được phép thử, không phải Central
hay model khách hàng. Không Save/Sync, publish, print hoặc export.

## Kết quả đầu ra

1. Một route MEP nhỏ có post-commit read-back.
2. Ba Revit Schedule thật cho Pipe, Pipe Fitting và Pipe Accessory, được lọc
   bằng mã demo; quantity snapshot dùng làm bước kiểm chứng bổ sung.
3. Một Floor Plan/Sheet dùng type, template và Title Block đã có; chỉ Floor Plan
   được đặt lên Sheet, ba Schedule giữ riêng trong Project Browser.
4. Một Family quạt hướng trục `.rfa` trong thư mục demo đã duyệt, sau đó load và
   đặt một instance vào Project.

Trước Family step, kỹ sư xác nhận Blueprint, nguồn thông số và quyết định nguồn
Family. Agent chỉ dò Autodesk `.rft` đúng năm đã cài cục bộ theo behavior,
category và Part Type đã khai; một axial fan thường cần Mechanical Equipment.
Agent không tự tìm, tải hoặc thay thế Family RFA của hãng, không chọn Family
gần giống và không đoán dữ liệu catalogue thiếu. Nếu không có template/source
phù hợp, giữ Family step ở `CẦN BẠN THỰC HIỆN`; không thay bằng Generic Model.
Generic Model chỉ là ngoại lệ khi behavior tương đương, Blueprint cho phép và
read-back xác nhận; fitting, 2D, Profile, Annotation và Tag chuyên biệt luôn bị
chặn. Xem [quy tắc chọn template](../FAMILY-TEMPLATE-AUTO-RESOLUTION.md).

## Timeline

| Phút | Hoạt động | Gate |
| ---: | --- | --- |
| 0–8 | `system_status` → `document_info` → active view/selection → capabilities | Chỉ đọc; xác nhận model copy |
| 8–15 | `bim_context_snapshot` và `bim_model_catalog` | Chỉ dùng ID thực của document |
| 15–29 | Preview rồi tạo Pipe route DN25, elbow và inline Gate Valve | Đọc lại ID/type/level/diameter/connector |
| 29–39 | Tạo ba native Schedule và chạy `quantity_takeoff` | Hidden Comments filter cô lập đúng mã demo |
| 39–47 | `documentation_plan` → preview/apply | Floor Plan lên Sheet A3; Schedule giữ riêng |
| 47–58 | Tạo quạt → reopen/checksum → load/place preview/apply | Blueprint/source/template đã xác nhận; đặt instance trên Level 1 |
| 58–60 | Đọc lại connector/IDs và ghi MCP log | Xác nhận không Save/Sync |

### Hội thoại bắt buộc trước khi tạo Schedule

Agent phải gọi `documentation_plan` với `schedule_discovery_categories` trước
để lấy danh sách field thật sự dùng được trong Revit hiện tại. Sau đó hỏi học
viên theo đúng ba bước, không tự chọn thay:

1. “Anh/chị muốn bảng có những thông tin nào, theo thứ tự cột nào?” Ví dụ nếu
   cần tên viết tắt hệ thống thì chọn **System Abbreviation**; nếu cần diện tích
   thì chỉ đề xuất **Area** khi field này có trong catalog của category.
2. Đưa ra một phương án sort/group dựa trên các cột đã chọn, rồi hỏi học viên
   xác nhận thứ tự ưu tiên và tăng/giảm. Ví dụ: System Abbreviation → Family and
   Type → Size, đều tăng dần.
3. “Cần tính tổng cột nào?” Chỉ đề xuất các field có `can_total=true`, chẳng hạn
   Length, Area hoặc Count tùy category/model; học viên có thể chọn không tính
   tổng.

Nếu field được yêu cầu không có, agent phải nói rõ field đó không khả dụng cho
category hiện tại và đưa ra các field gần nhất trong catalog. Không thay thế
ngầm. Sau khi học viên xác nhận, request tạo Schedule phải chứa đúng thứ tự
`fields`, `sort_by`, `total_fields` và `learner_requirements_confirmed=true`.

### Hội thoại bắt buộc trước khi tạo Sheet

1. Agent đọc `title_blocks`/`title_block_catalog`, trình bày tên Family/Type,
   kích thước và hướng giấy đang có, rồi hỏi: “Anh/chị muốn dùng khung nào?”
2. Sau khi học viên chọn, agent hỏi phần nội dung nào cần xuất hiện trên bản vẽ
   và dùng các element ID đó làm crop scope. Grid, annotation hoặc đối tượng
   ngoài phạm vi không được tự kéo rộng viewport.
3. Agent đề xuất vùng vẽ hữu dụng: lề mép giấy và phần chừa ô tên; đề xuất danh
   sách tỷ lệ chuẩn theo thứ tự ưu tiên, ví dụ 1:50 → 1:75 → 1:100 → 1:150.
   Học viên xác nhận trước khi preview.
4. Preview phải trả tỷ lệ được chọn, kích thước/vị trí viewport, vùng hữu dụng và
   `fits_usable_region=true`. Phép đo bao gồm cả nhãn view. Nếu chưa vừa, agent
   nêu ba lựa chọn: khung lớn hơn, tỷ lệ nhỏ hơn hoặc crop scope hẹp hơn.

Ảnh phản hồi ngày 2026-09-16 cho thấy viewport cũ bị grid/annotation kéo ra
ngoài khung A3 vì dùng tọa độ đặt cố định và không crop/scale theo vùng vẽ.
Workflow mới không coi Sheet đó là mẫu bố trí đạt chuẩn.

Nếu model thiếu Title Block hoặc type MEP phù hợp, ghi `CẦN BẠN THỰC HIỆN`;
không đoán ID và không tự thay đổi Project Template. Nếu Autodesk content thiếu
template đúng hoặc không có source Family tương thích, Agent nêu chính xác
prerequisite/source cần kỹ sư cung cấp hoặc repair; không tự download hay thay
bằng Generic Model.

## Điều kiện PASS

- Host discovery khớp capability manifest của artifact vừa build và Revit
  capability đúng command inventory của phiên runtime đó.
- Mỗi write dùng current document/view context và trả post-commit verification.
- Ba Schedule là `ViewSchedule` thật, có đúng các cột/sort/group/total học viên
  đã xác nhận, một hidden Comments field và một exact-tag filter; quantity phản
  ánh model sau thay đổi.
- View/Sheet/Viewport, RFA và Family instance được đọc lại bằng ID/path thực.
- Sheet chỉ PASS khi đúng Title Block học viên chọn và viewport/label nằm trọn
  trong vùng vẽ hữu dụng ở tỷ lệ đã preview.
- Connector quạt có thể được đọc lại nhưng không được gọi là network-certified
  nếu chưa có probe với Duct type/profile/kích thước phù hợp.
- Không có Save/Sync; transcript ghi rõ phần chưa test.
