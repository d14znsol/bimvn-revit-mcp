# Safety và audit standard v1

## Cổng trước thao tác

1. Làm việc trên model copy/local đã được phép; không dùng Central trực tiếp, model read-only hoặc file khách hàng chưa được duyệt.
2. Trong lượt có trạng thái Revit, luôn đọc `document_info`; nếu phụ thuộc view/selection thì đọc thêm `get_active_view`/`get_selection`.
3. Trước ghi MEP/Combine, lấy context mới, kiểm tra target/type/connector/link và preview. Không dùng ID hoặc preview từ lượt cũ.
4. Xác nhận riêng mọi apply. Mặt cắt Combine chỉ apply sau câu **XÁC NHẬN TẠO MẶT CẮT COMBINE**.
5. Path local phải là absolute, ở thư mục đã được người dùng duyệt; UNC/network, traversal, output conflict và overwrite bị chặn.

## Fingerprint, token và rollback

- Token preview có thời hạn, single-use, gắn document/target/resource fingerprint.
- Đổi document, view, link transform, template, output path hoặc file resource làm token/scan stale. Preview lại, không cố bypass.
- Preview Revit nằm trong `TransactionGroup` và rollback. Apply atomic: lỗi phải rollback model hoặc dọn staging/output vừa tạo.
- Không gọi Save, Sync, publish, print hoặc export trong các workflow này.

## Audit tối thiểu

Lưu ngoài source/release: thời điểm, client/user, tool, document/path, context/scan/preview ID, input đã duyệt, fingerprint, confirmation, kết quả, verification read-back, checksum report và lỗi/rollback. Không đưa secrets, course transcript hay dữ liệu khách hàng vào log dùng chung.

## Mã lỗi và phục hồi

| Mã / dấu hiệu | Phục hồi an toàn |
| --- | --- |
| `PreviewExpired`, `PreviewInvalid`, `ContextInvalid` | Re-anchor, kiểm lại dữ liệu, preview lại; không tái dùng token. |
| `PathBlocked`, `FileConflict` | Chọn local directory đã duyệt hoặc tên output mới; không xóa/ghi đè file có sẵn. |
| `ReadOnly`, `CentralBlocked`, `OwnershipBlocked` | Dừng; dùng copied/local model hoặc xin quyền phù hợp. |
| `TemplateInvalid`, `DocumentTypeInvalid` | Chọn đúng template/document đã kiểm tra. |
| `VerificationFailed`, `TransactionFailed` | Giữ evidence lỗi, xác minh rollback/cleanup rồi báo Fire Keeper. |
| `FingerprintMismatch` hoặc link transform đổi | Bỏ scan/preview cũ và chạy lại toàn bộ sequence liên quan. |

Build hay static test không thay thế Runtime Test Report. Rule từ kho kiến thức chỉ là candidate cho tới khi kỹ sư/Fire Keeper xác nhận giá trị, phạm vi và nguồn kỹ thuật.
