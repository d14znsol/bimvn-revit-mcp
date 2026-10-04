# PDF/ảnh/CAD → Family hoặc Change Set — SourceEvidence v3

Với bộ bản vẽ PDF, DWG/DXF hoặc ảnh công trình dùng để dựng Project
MEPF thay vì một Family, xem [SOURCE-TO-PROJECT-MEPF.md](SOURCE-TO-PROJECT-MEPF.md).

Chỉ dùng file local đã được phép trong thư mục demo; không upload catalog/CAD
khách hàng sang dịch vụ khác. Tạo/preview RFA không cần Project đang mở. Chỉ khi
load/place hoặc kiểm tra Project mới phải bắt đầu bằng `document_info` và đọc
lại view/selection liên quan trong đúng lượt.

## PDF catalog → Family

1. `family_source_inspect` với PDF local. Evidence v3 lưu SHA-256/revision,
   page rotation, phân loại catalog/table, vector/mixed hoặc raster scan, text
   block fingerprint, units và scale anchors nếu có.
2. Dùng `source_to_revit_proposal` với `mapping=pdf_catalog_family`, khai
   category/behavior, box hoặc cylinder, và 2–100 Type. Mỗi width/height/depth
   hoặc diameter/height phải trỏ tới đúng `page`, `block_index` và
   `block_sha256`; tool sinh Blueprint v3 và `type_confirmed_fields` trực tiếp.
3. `family_spec_preview` bằng `family_spec_input` trả về; trình bày trường đã thấy, còn thiếu và capability
   compiler. Kỹ sư xác nhận dữ liệu; MCP chọn template đúng behavior/category.
4. `family_build_preview`; kiểm tra geometry, parameter, formula, connector,
   rollback và báo cáo chưa-certified.
5. Chỉ sau câu xác nhận riêng và runtime gate đạt mới `family_build_apply`.
6. Load/place là workflow riêng với xác nhận riêng.

Mapping legacy vẫn được giữ. Với Blueprint v3, template được chọn theo kiểu đặt
trước category; chỉ fallback sang Generic Model cùng hành vi, và không fallback
cho fitting/2D/Tag cần template chuyên biệt. Không dùng template năm khác. Xem
[tự động chọn Family Template](FAMILY-TEMPLATE-AUTO-RESOLUTION.md).

PDF nhiều Type bắt buộc `type_confirmed_fields` và
`type_catalog_revisions` cho từng exact Type name; không dùng một revision hoặc
field fallback chung. Provenance page/block chỉ chứng minh locator thuộc đúng
file bất biến, không tự xác nhận nội dung catalog hay revision của hãng.

## CAD → Change Set

1. Xác nhận DWG/DXF local, units, layer, origin và view.
2. `cad_geometry_inspect` để tạo SourceEvidence v3. DXF ASCII được parse theo
   whitelist `LINE/LWPOLYLINE/CIRCLE/ARC/INSERT`; `SPLINE/POLYLINE` giữ ở mức
   engineering review. DWG không bao giờ bị đọc binary như text. Có thể truyền
   manifest ODA/Autodesk đã bind SHA-256, hoặc dùng
   `use_revit_trusted_adapter=true` với units đã xác nhận để Revit import vào
   Family document tạm, đọc Line/Arc/Circle/Polyline + layer, rollback và đóng
   không lưu. Cách này không chạm Project và không chứng nhận block/attribute/
   dimension/xref; không blind-import.
3. `source_to_revit_proposal` để map DXF đã chọn sang `family_profile`,
   `symbolic_lines`, `model_lines`, `detail_lines` hoặc `route_centerline`.
   Bốn mapping Family trả Blueprint v3 đã validate và `family_spec_input`, không
   còn chỉ là fragment. Profile chỉ nhận closed LWPOLYLINE. Route bắt buộc
   type/system khi phù hợp, level, elevation và tolerance; fitting/slope/reroute
   không được suy ra.
4. Với Family linework, gọi `family_spec_preview` rồi Family Preview/Apply
   chuẩn. Với route, gọi `cad_to_revit_preview`; kiểm Change Set: type, system, level, points,
   segment và geometry không map được.
5. Kỹ sư xác nhận Change Set. Chỉ khi adapter runtime-approved mới apply.
6. Lưu read-back/audit vào CAD Change Set Report.

CAD bounding-box/linework chỉ là evidence cho review, không phải solid clash
certificate. Không tự reroute và không chuyển polyline tùy ý thành MEP.

## Ảnh nhiều góc → Family proposal

Một ảnh phối cảnh chỉ là proposal nguồn và không đủ để dựng Family tham số.
Để dùng `source_to_revit_proposal` với `image_family`, cần tối thiểu hai góc
front/side/top/orthographic khác nhau; từng ảnh phải có unit và ít nhất một
scale anchor do người dùng xác nhận. Người dùng vẫn phải nhập rõ kích thước
box/cylinder và đặt `dimensions_confirmed_by_user=true`.

Output có Blueprint v3 đã validate và `family_spec_input`; ảnh đầu là evidence
chính, các ảnh còn lại đi vào `supporting_evidence_ids`, connector luôn rỗng.
Hình học mặt khuất, bề dày, host, connector, material và performance không được
suy từ ảnh. Sau review gọi `family_spec_preview`, rollback preview và Apply bằng
confirmation token riêng.

## Build MCP Server tin cậy

`node MCP-Server/scripts/build-server.mjs` emit TypeScript vào staging directory duy nhất, kiểm tra các
module bắt buộc và sinh capability manifest ngay trên staging. Chỉ sau khi mọi
bước PASS mới atomically swap sang `MCP-Server/build`; bản cũ được phục hồi nếu
emit/manifest lỗi. Cơ chế này tránh build thành công giả hoặc thiếu module trên
Windows khi dọn output ngay trước `tsc`.
