# PDF → Family và CAD → Change Set — Quick Start Wave 1

Chỉ dùng file local đã được phép trong thư mục demo; không upload catalog/CAD
khách hàng sang dịch vụ khác. Bắt đầu bằng `document_info` và, nếu workflow phụ
thuộc view/selection, đọc lại `get_active_view`/`get_selection` trong đúng lượt.

## PDF catalog → Family

1. `family_source_inspect` với PDF local và trang nguồn chuẩn.
2. `family_spec_preview`; trình bày trường đã thấy, suy luận, còn thiếu.
3. Kỹ sư xác nhận thông số và category. MCP tự chọn Family Template đúng
   phiên bản/category; học viên không nhập `template_path`.
4. `family_build_preview`; kiểm tra geometry, parameter, formula, connector,
   rollback và báo cáo chưa-certified.
5. Chỉ sau câu xác nhận riêng và runtime gate đạt mới `family_build_apply`.
6. Load/place là workflow riêng với xác nhận riêng.

Mapping mặc định: quạt/bơm/FCU/AHU → Mechanical Equipment; cửa gió → Air
Terminal; van gió → Duct Accessory; phụ kiện ống nước → Pipe Accessory. Nếu
content pack thiếu template chuyên ngành, MCP dùng Metric Generic Model đúng năm,
đổi Family Category sang category đích trước khi tạo hình và đọc lại kết quả.
Không dùng template năm khác. Xem
[tự động chọn Family Template](FAMILY-TEMPLATE-AUTO-RESOLUTION.md).

## CAD → Change Set

1. Xác nhận DWG/DXF local, units, layer, origin và view.
2. `cad_geometry_inspect` để tạo evidence, không import mù.
3. `cad_to_revit_preview`; kiểm Change Set: type, system, level, points,
   segment và geometry không map được.
4. Kỹ sư xác nhận Change Set. Chỉ khi adapter runtime-approved mới apply.
5. Lưu read-back/audit vào CAD Change Set Report.

CAD bounding-box/linework chỉ là evidence cho review, không phải solid clash
certificate. Không tự reroute và không chuyển polyline tùy ý thành MEP.
