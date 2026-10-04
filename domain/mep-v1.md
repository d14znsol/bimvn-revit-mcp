---
name: mep-v1
audience: AI agent and MEP developer
---

# DSCons MEP v1 SOP

1. Không suy luận từ ảnh hoặc dữ liệu của turn trước khi model đang kết nối. Trước mọi claim hoặc thao tác phụ thuộc ngữ cảnh, re-anchor trong **turn hiện tại**: gọi `document_info`; gọi thêm `get_active_view` nếu liên quan view/level và `get_selection` nếu liên quan selection.
2. Với phần tử: gọi `mep_element_detail`, sau đó `mep_connector_network` khi có liên kết/routing/fitting.
3. Khi lọc, gọi `mep_filter_elements`: `categories`, `system_name`, `level_name` kết hợp theo AND. Dùng `mep_qa_connectivity` để rà connector hở hoặc Pipe/Duct thiếu System Name; finding là tín hiệu cần kỹ sư review, không phải lệnh tự sửa.
4. Trước khi tạo/chuyển/dịch/nối: đọc type, system, size, connector và routing preference cần thiết. Không tự tạo family/fitting thiếu.
5. Gọi write tool trực tiếp chỉ khi người dùng đã yêu cầu thao tác. Write tool tự Preview rồi Apply; nếu cần kiểm tra trước, gọi `mep_preview` rồi `mep_apply_preview` trong 90 giây. Preview mô phỏng operation trong Revit `TransactionGroup` rồi rollback, vì vậy lỗi type/fitting/routing có thể được phát hiện trước apply.
6. Sau write thành công, báo cáo trường `verification` từ post-commit read-back. Không chỉ nói “đã làm” dựa vào element ID; phải nêu trạng thái type/size/connector đọc lại từ Revit.
7. Nếu model là Central mở trực tiếp, Read-only, target owned-by-other, Pin hoặc Group: dừng; không cố lách.
8. Lỗi fitting/domain/size/connector phải báo rõ Revit đã rollback toàn bộ; không coi segment tạo dở là thành công.
9. Route v1 chỉ dùng các đoạn thẳng vuông góc theo một trục X/Y/Z; route chéo phải bị từ chối trước Transaction. `mep_disconnect` chỉ ngắt cặp connector đang thực sự nối với nhau.

Đơn vị các point/dịch chuyển/size là **mm**. Với Cable Tray phải lấy Type đã chọn để giữ Service Type/routing dữ liệu mà type Revit mang theo; không ghi đè bằng chuỗi suy đoán.
