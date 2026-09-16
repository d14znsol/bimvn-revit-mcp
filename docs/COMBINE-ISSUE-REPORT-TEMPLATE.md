# DSCons Combine Issue Report v1 — template

Mỗi report là snapshot evidence của một `scan_id`, không phải kết luận pháp lý, clash matrix chứng nhận hay lệnh tự reroute.

## Metadata

- Report name / JSON SHA256 / HTML SHA256
- Scan ID, thời điểm, document/view fingerprint và link instance/transform evidence
- Người phân loại issue và ngày review

## Mỗi issue

- Finding ID, source/link element ID, category/system và vị trí host.
- Method: `solid_intersection` hoặc `clearance_triage`.
- Intersection volume chỉ áp dụng cho Solid intersection.
- Quyết định kỹ sư: `real_clash`, `false_positive`, `accepted`, `needs_information`.
- Priority, protected elements, movable elements, ghi chú, input còn thiếu.
- Citation riêng tư (nếu có): canonical path, SHA256, heading; không dán nội dung khóa học.

## Kiểm tra trước phát hành

- Có quyết định cho mọi finding của scan.
- JSON/HTML chưa tồn tại; output ở local directory đã duyệt.
- Không có dữ liệu khách hàng, transcript hay rule chưa được kỹ sư xác nhận.
- Nếu issue cần triển khai, tạo/review section trước; reroute là Change Set riêng ở Wave sau.
