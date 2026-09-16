# Runtime test — Combine & Shop Copilot v1

Chỉ chạy sau khi Fire Keeper cấp quyền riêng cho copied model. Không mở/đóng Revit, không dùng Central trực tiếp và không chạy apply trên model gốc.

## Input Fire Keeper cần cấp

- Revit 2023, copied Project local có một link Architecture/Structure và quyền ghi cho test copy.
- Source/link elements có va chạm thật, một false positive bbox, một link transform có thể kiểm tra; Section `ViewFamilyType`, View Template (nếu dùng) và thư mục report local trống.
- Tên kỹ sư review, quyết định issue và xác nhận rõ ràng cho từng apply.

## Matrix Revit 2023

1. `document_info`, `get_active_view`, `bim_context_snapshot`, `coordination_links`.
2. Bbox triage và `coordination_solid_scan`: exact intersection, empty solid, nested `GeometryInstance`, duplicate pair, unloaded link, changed transform; xác nhận `clearance_triage` không được gọi Solid certificate.
3. Scan fingerprint stale khi đổi document/view/link, token expired/reused và input ID sai.
4. Issue report preview/apply: thiếu decision, output ngoài thư mục/UNC, conflict, checksum, JSON/HTML read-back và cleanup khi lỗi.
5. Section preview rollback: Section `ViewFamilyType` sai, template sai, tên trùng, crop/depth/scale sai; không có view tồn tại sau preview.
6. Chỉ sau câu **XÁC NHẬN TẠO MẶT CẮT COMBINE** chạy section apply; kiểm tra name/scale/crop/depth/template/ID read-back và không Save/Sync.
7. Tùy chọn: `documentation_plan` + `documentation_apply` riêng để đặt view đã tạo lên sheet.
8. Lưu audit log, scan report, preview ID/confirmation, read-back và Runtime Test Report.

Chỉ khi toàn bộ 2023 PASS mới lặp lại matrix trên Revit 2025. Revit 2019–2022, 2024, 2026 và 2027 không suy runtime từ compile; 2027 vẫn POC.
