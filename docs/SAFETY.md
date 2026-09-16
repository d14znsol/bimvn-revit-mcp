# Safety and Local/Central rules

| Document state | Read | MEP apply |
| --- | --- | --- |
| Model độc lập | Có | Có sau Preview hợp lệ |
| Local của Central | Có | Có nếu toàn bộ targets editable |
| Central mở trực tiếp | Có | Chặn |
| Read-only | Có | Chặn |
| Owned by another / Pin / Group | Có | Chặn |

Mọi MEP write tool tạo Preview token nội bộ với document + target/connector/type fingerprint. Direct call thực hiện `Preview → Auto Apply`; `mep_preview` mô phỏng operation bằng Revit transaction thật trong `TransactionGroup` rồi rollback, vì vậy model không đổi nhưng type/fitting/routing failures có thể bị phát hiện trước apply. `mep_apply_preview` chỉ nhận token chưa dùng, chưa hết 90 giây. Thay document, type, connector hoặc target sau preview làm apply bị chặn.

Mutation chạy trong `TransactionGroup`; exception rollback toàn bộ. Sau commit, MCP đọc lại element/type/size/connector và trả `verification.mode = post_commit_read_back`; nếu read-back không thực hiện được, group bị rollback. Audit append-only ở `%LocalAppData%\DSCons\RevitMcp\audit.jsonl` ghi timestamp, client, user, tool, document/worksharing, preview, result/error. TCP chỉ loopback và yêu cầu session secret ngẫu nhiên.

Mọi agent phải re-anchor `document_info` trong turn hiện tại trước write; nếu request phụ thuộc active view hoặc selection, phải gọi thêm `get_active_view` hoặc `get_selection`. Không dùng view, selection hay document state của turn trước làm căn cứ thao tác.

V1 bị cấm: code C# tự do, Save/Sync, Purge, xóa hàng loạt, chỉnh Project Settings.

## V2 production controls

- `model_create_batch`, `bim_changeset_preview` và `documentation_apply` bắt buộc có `context_id` từ `bim_context_snapshot` trong tối đa 15 phút. Đổi document hoặc active view làm context vô hiệu.
- Change Set không hỗ trợ lồng Change Set; giới hạn 50 operations và batch giới hạn 100 routes. Một lỗi bất kỳ rollback toàn bộ set.
- `coordination_scan` chỉ báo overlap/clearance bằng transformed bounding box. Nó không phải clash solid geometry và không tự route né clash.
- `quantity_takeoff` chỉ trả quantity đang có trong model. Không tự thêm wastage, giá, cost code hay quy tắc nghiệm thu.
- `documentation_apply` chỉ tạo Floor Plan, apply View Template đã có, tạo sheet từ Title Block đã có và đặt View ID được chỉ định. Cấm Save, Sync, publish, print, export hoặc sửa template/project settings.
