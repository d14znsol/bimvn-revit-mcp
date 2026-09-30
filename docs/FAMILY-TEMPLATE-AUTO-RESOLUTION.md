# Chọn Family Template có kiểm chứng

> Blueprint v3 chọn **kiểu đặt/hành vi trước, category sau**. Bảng `family_kind`
> bên dưới chỉ là lớp tương thích cho workflow cũ. Xem
> [FAMILY-BLUEPRINT-V3.md](FAMILY-BLUEPRINT-V3.md).

Học viên không phải tự duyệt thư mục Autodesk hoặc nhập `template_path` trong
workflow thông thường. Khi preview Family, MCP đọc phiên bản Revit đang chạy,
đối chiếu behavior/category/Part Type đã được Blueprint xác nhận và chỉ dò
template category-specific đã cài trong
`%PROGRAMDATA%\Autodesk\RVT <năm>\Family Templates\`. Nếu thiếu, trùng hoặc
không đủ chữ ký tương thích thì chặn đúng phạm vi; không tải, không chọn RFA
gần giống và không dùng template của năm khác.

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

## Fitting phải phân biệt theo Part Type

`duct_fitting` và `pipe_fitting` không được coi là một template duy nhất. Khi
Blueprint khai `family.part_type`, resolver kiểm tra riêng primitive và chỉ
chấp nhận đúng nguồn đã được đọc lại:

| Category | Part Type | Autodesk `.rft` candidates / nguồn thay thế |
| --- | --- | --- |
| Duct Fitting | `elbow`, `cross`, `tee`, `transition` | `Metric Duct Elbow/Cross/Tee/Transition.rft` đúng năm nếu có |
| Pipe Fitting | `elbow`, `cross`, `tee`, `transition` | kiểm tra tên native tương ứng; nếu bộ cài không cung cấp thì dùng RFA native đã được xác minh |
| Pipe Fitting | `union`, `pipe_mechanical_coupling`, `pipe_flange` | không suy từ Generic Model; dùng RFA native tương thích hoặc chặn |

Tên Family/RFA như “coupling” hoặc “elbow” chỉ là gợi ý tìm kiếm. Trước khi
dùng làm nguồn, gọi `family_artifact_inspect` trên bản RFA staging để Revit
đọc lại version lưu, category, `FamilyPlacementType`, Part Type, connector
count/discipline/profile/size và Type. Inspector mở RFA tạm rồi đóng không lưu;
không load/place vào Project. RFA đời cao bị chặn từ `BasicFileInfo` và không
được đưa thẳng vào Revit đời thấp; phải qua workflow chuyển native có đối
chiếu. Nếu không có `.rft` và cũng không có RFA tương thích, Preview phải trả
blocked cho đúng primitive đó, không chặn hoặc suy diễn cho các primitive khác.

MCP ưu tiên template chuyên ngành Metric, đúng năm và không thuộc thư mục Annotations.
`template_path` chỉ là tùy chọn nâng cao cho người phụ trách. Nếu được truyền
cho `family_build_preview`, file override phải nằm trong
`approved_demo_directory`. Nếu bỏ trống, MCP dùng template tin cậy từ đúng bộ
cài Revit và trả `template_selection` để kiểm tra path, category, nguồn và năm.

Với workflow legacy level-based, `Metric Generic Model.rft` đúng năm chỉ có thể
được dùng khi Blueprint cho phép và read-back chứng minh **behavior thực sự
tương đương** trước khi đổi `OwnerFamily.FamilyCategory` sang category đích.
Fitting, Detail Item, Profile, Annotation và Tag chuyên biệt không được
fallback. Preview
trả `template_category`, `target_category`, `category_change_required`,
`category_change_mode` và `category_assignment` để học viên kiểm tra.

Trong bài học, Agent giải thích thao tác tương đương trong Revit là mở
**Create → Family Category and Parameters**, chọn đúng category rồi xác nhận.
Workflow có thể đọc/kiểm tra bước này tự động, nhưng không tự vượt qua thiếu
nguồn hoặc mơ hồ. MCP trả `TemplateInvalid`/`SourceRequired` khi template,
Family source hoặc behavior chưa đủ; học viên không cần tự tìm `.rft` nhưng
người phụ trách phải cung cấp nguồn đã xác minh khi workflow yêu cầu. Tuyệt đối
không dùng template của phiên bản Revit khác.

Mapping/template fallback không đồng nghĩa compiler hình học đã runtime-certify;
primitive chưa hỗ trợ vẫn không được preview/apply.
