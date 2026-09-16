# Học liệu DSCons Revit MCP

DSCons Revit MCP là sản phẩm độc lập, không thuộc một chương trình hay khóa
học nào. Trong chương trình phễu, học viên chỉ được hướng dẫn bộ kit trong
một buổi: kiểm tra/cài đặt trước, sau đó có thể chạy pilot MCP 60 phút trên
Project copy đã được phép.

Học viên bắt đầu từ `START_HERE.md`. Agent đọc source-of-truth, hỏi phiên bản
Revit/client, giải thích bằng ngôn ngữ dễ hiểu và luôn kèm ví dụ khi đặt câu
hỏi.

- `LEARNER_GUIDE.md`: quy trình một buổi cài đặt, kiểm tra kết nối và gỡ/khôi phục.
- `60-MINUTE-MCP-PILOT.md`: dựng route MEP, quantity, Sheet và Family sau khi
  kết nối PASS; Family Template được MCP tự dò theo năm/category.
- `../MCP-RIBBON-CONTROLS.md`: hai nút Bật/Tắt MCP và Cập nhật Code.
- `templates/`: mẫu log buổi cài đặt và phiếu báo lỗi khi cần gửi hỗ trợ.

Các tài liệu thử nghiệm hoặc phát triển cũ trong thư mục source không thuộc
learner release hiện tại và không cần mở trong buổi cài đặt. `MCP.CoreRuntime`
là hot reload nội bộ của add-in, không phải MCP Server thứ hai.

Claim runtime vẫn phải dựa trên evidence đúng phiên bản; build thành công không
tự động có nghĩa là đã kiểm tra runtime.
