# Quick start thực hành cá nhân v1

Dùng model copy. Revit 2023/2025 chỉ có runtime evidence cho flow MEP/V2 cũ; Combine/Shop mới, Family và CAD vẫn cần runtime test riêng. `student-setup.ps1 -Mode Check` chỉ đọc, không cài hoặc đổi client.

## Prompt 1 — Combine / Shop review

```text
Đọc document_info, get_active_view và bim_context_snapshot trong lượt này. Liệt kê coordination_links,
sau đó chạy coordination_solid_scan chỉ trên element/link tôi chỉ định. Tách rõ solid_intersection
với clearance_triage; không reroute hay sửa link/model. Trả finding unreviewed và dữ liệu còn thiếu.
Sau khi tôi phân loại toàn bộ finding, chỉ preview Issue Report và section. Chỉ gọi
coordination_section_apply khi tôi nói đúng: XÁC NHẬN TẠO MẶT CẮT COMBINE.
```

## Prompt 2 — Tuyến / kết nối

```text
Đọc lại document, view/selection khi cần, element detail, connector network và connectivity QA.
Dùng catalog để chọn type/system/level. Chỉ preview route/kết nối khi tôi yêu cầu; trình bày thay đổi,
chờ xác nhận, apply rồi đọc lại verification. Nếu lỗi, báo rollback toàn bộ.
```

## Prompt 3 — Bóc tách

```text
Đọc document, tạo context snapshot, lấy model catalog rồi gọi quantity_takeoff. Trả bảng
count/length/area/volume có bộ lọc và nguồn dữ liệu. Không thêm wastage, giá, cost code hay procurement rule.
```

Lưu MCP log, preview ID, confirmation, read-back và lỗi. Không gọi Save/Sync. Khi cần gỡ gói, chỉ dùng `student-setup.ps1 -Mode Uninstall -ConfirmUninstall` sau khi xác nhận đúng version/client và Revit đã đóng.
