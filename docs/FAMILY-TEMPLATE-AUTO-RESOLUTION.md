# Tự động chọn Family Template

Học viên không phải tự duyệt thư mục Autodesk hoặc nhập `template_path` trong
workflow thông thường. Khi preview Family, MCP đọc phiên bản Revit đang chạy,
suy ra category từ `family_kind` và tìm template category-specific trong
`%PROGRAMDATA%\Autodesk\RVT <năm>\Family Templates\`.

| Loại Family | `family_kind` | Template/category cần chọn |
| --- | --- | --- |
| Quạt hướng trục/inline/ly tâm, FCU, AHU, bơm | `axial_fan`, `inline_fan`, `centrifugal_fan`, `fcu`, `ahu`, `pump` | Mechanical Equipment |
| Cửa gió | `air_terminal` | Air Terminal |
| Van gió/phụ kiện ống gió | `duct_accessory` | Duct Accessory |
| Van/phụ kiện ống nước | `pipe_accessory` | Pipe Accessory |
| Thiết bị vệ sinh | `plumbing_fixture` | Plumbing Fixture |
| Sprinkler | `sprinkler` | Sprinkler |
| Tủ điện/hộp nối | `panel`, `conduit_junction_box` | Electrical Equipment |
| Đèn | `lighting_fixture` | Lighting Fixture |
| Phụ kiện máng cáp | `cable_tray_fitting` | Cable Tray Fitting |

MCP ưu tiên template chuyên ngành Metric, đúng năm và không thuộc thư mục Annotations.
`template_path` chỉ là tùy chọn nâng cao cho người phụ trách. Nếu được truyền
cho `family_build_preview`, file override phải nằm trong
`approved_demo_directory`. Nếu bỏ trống, MCP dùng template tin cậy từ đúng bộ
cài Revit và trả `template_selection` để kiểm tra path, category, nguồn và năm.

Nếu thiếu template category-specific nhưng có `Metric Generic Model.rft` đúng
năm, MCP dùng file Generic đó, đổi `OwnerFamily.FamilyCategory` sang category
đích trong Family document trước khi tạo hình học và đọc lại category. Preview
trả `template_category`, `target_category`, `category_change_required`,
`category_change_mode` và `category_assignment` để học viên kiểm tra.

Trong bài học, Agent giải thích thao tác tương đương trong Revit là mở
**Create → Family Category and Parameters**, chọn đúng category rồi xác nhận.
Workflow bình thường đã làm và kiểm tra bước này tự động; học viên không cần tự
tìm `.rft` hoặc sửa tay. MCP chỉ trả `TemplateInvalid` khi cả template chuyên
ngành lẫn Generic Model đúng năm đều thiếu, và tuyệt đối không dùng template
của phiên bản Revit khác.

Mapping/template fallback không đồng nghĩa adapter hình học đã runtime-certify;
adapter chưa có geometry vẫn không được apply.
