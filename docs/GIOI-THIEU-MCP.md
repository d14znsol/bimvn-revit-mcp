# DSCons Revit MCP — Trợ lý AI cho Revit MEP

## Đọc trang này để biết gì?

DSCons Revit MCP là cầu nối cục bộ cho phép AI client như **Codex, Claude**
hoặc **Google Antigravity** làm việc với dữ liệu và các thao tác MEP trong
Autodesk Revit. Tài liệu này giải thích theo ngôn ngữ nghiệp vụ: MCP đọc được
gì, làm được gì, lúc nào nó sẽ từ chối thao tác, và người dùng cần kỳ vọng gì
khi giao việc cho AI.

MCP này là một dự án độc lập. Nó **không phải** module, DLL hay Ribbon panel
của `DSCons-MEP-Tools`; cũng không dùng chung source hoặc cấu hình MCP với bộ
tool đó.

> Mục tiêu của v1: để AI hỗ trợ kỹ sư MEP đọc đúng model, kiểm tra kết nối và
> thực hiện một tập thao tác route có kiểm soát. AI không tự đưa ra quyết định
> thiết kế thay cho kỹ sư và không được phép vượt qua các điều kiện an toàn của
> Revit.

---

## 1. MCP này hoạt động như thế nào?

Khi người dùng chat với AI, AI không “nhìn thấy” model Revit một cách mặc định.
Nó dùng các tool của MCP để hỏi Revit lấy dữ liệu thật hoặc yêu cầu Revit thực
hiện một thao tác. Toàn bộ luồng chạy trên máy đang mở Revit:

```text
Người dùng
   │ yêu cầu bằng ngôn ngữ tự nhiên
   ▼
AI client: Codex / Claude / Antigravity
   │ giao thức MCP qua stdio
   ▼
MCP-Server (Node.js + TypeScript)
   │ TCP loopback 127.0.0.1 + session secret
   ▼
MCP (DSCons Revit add-in bridge)
   │ ExternalEvent — chuyển yêu cầu về Revit UI thread
   ▼
Autodesk Revit API và document đang mở
```

Điều này có vài hệ quả quan trọng:

- AI chỉ có dữ liệu từ model sau khi nó gọi tool; không nên suy đoán kích thước,
  type hoặc kết nối chỉ từ ảnh màn hình.
- Add-in chỉ lắng nghe trên `127.0.0.1`, tức chỉ các tiến trình trên chính máy
  đó mới kết nối được.
- Mỗi phiên Revit tạo một session secret ngẫu nhiên. MCP server phải đọc secret
  đó và gửi kèm mọi request; request không có secret hợp lệ bị từ chối.
- Lệnh Revit API được chuyển qua `ExternalEvent`, vì vậy chúng chạy đúng trong
  UI context mà Revit yêu cầu; MCP không gọi Revit API trực tiếp từ tiến trình
  Node.js.

`MCP-Server` là MCP server duy nhất cần đăng ký trong AI client. `MCP/` là
add-in chạy bên trong Revit; `MCP.CoreRuntime/` là runtime nội bộ có thể hot
reload của add-in. Hai thành phần sau **không phải MCP server** và không được
đăng ký trực tiếp vào Codex, Claude hoặc Antigravity.

---

## 2. Ai nên dùng và dùng khi nào?

MCP phù hợp khi kỹ sư hoặc BIM coordinator cần AI hỗ trợ những việc như:

- Kiểm tra nhanh trạng thái model, view hiện hành và selection trước khi phân
  tích một vấn đề MEP.
- Đọc chính xác type, system, level, size, parameter, connector và quan hệ kết
  nối của pipe, duct, conduit, cable tray hoặc fitting.
- Lọc nhiều phần tử theo hệ thống, category và level để rà soát.
- Tạo một đoạn route MEP có điểm đầu/cuối và các góc vuông đã xác định rõ.
- Nối, ngắt, dịch chuyển, đổi type hoặc đổi kích thước một nhóm phần tử MEP đã
  được kỹ sư chỉ định.
- Buộc một thao tác phải qua preview, có rollback và có dấu vết audit thay vì
  để AI sửa model “mù”.

MCP không phù hợp để yêu cầu mơ hồ như “hãy tự thiết kế toàn bộ hệ thống cấp
gió” hoặc “tự tối ưu mọi route trong dự án”. AI có thể hỗ trợ phân tích và đề
xuất, nhưng người dùng vẫn cần chỉ rõ phạm vi, hệ thống, vị trí và quyết định
kỹ thuật cần áp dụng.

---

## 3. Khả năng đọc và chẩn đoán model

Các tool dưới đây không thay đổi model. Đây là điểm bắt đầu được khuyến nghị
trước mọi yêu cầu MEP khác.

| Tool | MCP đọc gì? | Khi nên dùng |
| --- | --- | --- |
| `system_status` | Trạng thái bridge/runtime, khả năng nhận request của Revit. | Khi vừa bắt đầu phiên làm việc hoặc nghi ngờ MCP mất kết nối. |
| `document_info` | Tên, đường dẫn, modified, read-only, workshared và trạng thái Central mở trực tiếp. | Trước khi đề xuất hoặc áp dụng thay đổi. |
| `get_active_view` | ID, tên, loại view và scale của active view. | Khi yêu cầu liên quan tới view hiện hành. |
| `get_selection` | Danh sách element ID mà người dùng đang chọn trong Revit. | Khi người dùng nói “các phần tử tôi đang chọn”. |
| `get_capabilities` | Danh sách tool, read/write, scope, yêu cầu fresh context, điều kiện và giới hạn. | Khi cần biết chính xác tool nào hiện đang khả dụng và có thể dùng an toàn. |
| `mep_element_detail` | Category/class, family/type, `type_id`, system, level, size, parameter và bounding box của element MEP. | Khi cần hiểu một hoặc nhiều phần tử cụ thể, hoặc lấy ID Type để tạo/đổi route. |
| `mep_connector_network` | Connector, domain, profile, size, hướng và element đang liên kết. | Trước khi nối/ngắt route hoặc chẩn đoán fitting. |
| `mep_filter_elements` | Danh sách MEP được lọc đồng thời theo category, system name, level name. | Khi cần khoanh vùng đối tượng để kiểm tra. |
| `mep_qa_connectivity` | Tín hiệu QA chỉ đọc: connector hở và Pipe/Duct chưa có System Name. | Khi rà model trước bàn giao; mọi finding cần kỹ sư review, không tự sửa. |

### `mep_element_detail` cho biết những gì?

Với một danh sách `element_ids`, MCP trả về dữ liệu để AI có thể mô tả đúng
phần tử thay vì đoán:

- Revit category và class của phần tử.
- Family/type đang dùng.
- System name và level.
- Kích thước hiện có, cùng các parameter và bounding box liên quan.

Ví dụ yêu cầu tốt:

> “Đọc type, system, level và kích thước của các phần tử đang chọn; cho tôi
> biết phần tử nào không thuộc hệ thống cấp gió.”

### `mep_connector_network` dùng để tránh lỗi gì?

Một yêu cầu “nối hai ống này” chưa đủ để Revit luôn tạo fitting. Trước đó cần
biết hai phần tử có connector không, connector nào còn trống, domain/profile
có tương thích hay không, kích thước có phù hợp hay không và chúng hiện đang
nối với ai. Tool này cung cấp dữ liệu đó.

Khi tạo fitting, MCP chọn cặp connector tương thích gần nhất. Nếu không có cặp
phù hợp, nó dừng và báo lý do; không tự tạo family fitting thiếu hoặc suy đoán
loại fitting.

### `mep_filter_elements` lọc như thế nào?

`categories`, `system_name` và `level_name` được kết hợp theo logic **AND**.
Nghĩa là kết quả phải đồng thời thỏa mọi điều kiện đã cung cấp. Có thể đặt
`limit` từ 1 đến 1000 để tránh trả quá nhiều element trong một lần.

Ví dụ yêu cầu tốt:

> “Tìm tối đa 100 duct trên Level 02 thuộc hệ thống `SA-01`, rồi đọc type và
> kích thước của chúng.”

---

## 4. Các thao tác MEP có thể thực hiện

Các tool sau có thể thay đổi model. AI chỉ nên gọi chúng khi người dùng đã yêu
cầu thao tác rõ ràng, sau khi đã kiểm tra dữ liệu cần thiết.

| Tool | Thao tác | Dữ liệu cần có |
| --- | --- | --- |
| `mep_preview` | Kiểm tra trước một thao tác write, không sửa model. | Tên operation và argument của operation đó. |
| `mep_apply_preview` | Áp dụng một preview token còn hiệu lực. | `preview_id`. |
| `mep_create_route` | Tạo Pipe, Duct, Conduit hoặc Cable Tray theo polyline vuông góc; Pipe có thể nhận đường kính, demo tag và một inline accessory. | Kind, type, level, các điểm; Pipe/Duct cần thêm system type khi Revit yêu cầu. |
| `mep_connect` | Nối hai element và tạo fitting hợp lệ. | Hai element ID. |
| `mep_disconnect` | Ngắt cặp connector đang nối trực tiếp giữa hai element. | Hai element ID. |
| `mep_move_route` | Dịch chuyển một hoặc nhiều phần tử MEP theo X/Y/Z. | Element ID và độ dịch chuyển mm. |
| `mep_change_type` | Đổi Type cho một hoặc nhiều element MEP. | Element ID và type ID. |
| `mep_change_size` | Đổi đường kính hoặc width/height. | Element ID và kích thước mm phù hợp. |

### Tạo route: phạm vi và quy tắc

`mep_create_route` tạo route cho bốn loại:

- `pipe`
- `duct`
- `conduit`
- `cable_tray`

Route là danh sách ít nhất hai điểm `{ x_mm, y_mm, z_mm }`. Mỗi segment phải
di chuyển theo **chính xác một trục X, Y hoặc Z**. Segment chéo, hai điểm trùng
nhau hoặc một góc trung gian thừa theo cùng một trục đều bị từ chối trước khi
mở transaction.

Ví dụ hợp lệ:

```text
(0, 0, 0) → (3000, 0, 0) → (3000, 1200, 0) → (3000, 1200, 800)
```

Ví dụ không hợp lệ vì là segment chéo:

```text
(0, 0, 0) → (3000, 1200, 0)
```

MCP tạo các đoạn và cố tạo elbow fitting giữa chúng. Nếu Revit không có routing
preference, fitting family, connector domain hoặc size phù hợp, toàn bộ thao
tác bị rollback và AI sẽ nhận lỗi thay vì một route dang dở.

Với Pipe, schema additive còn nhận `diameter_mm`, `demo_tag` và
`inline_accessory`. Inline accessory v1 phải là Pipe Accessory level-based đã
load, có đúng hai connector Piping tròn; `segment_index` và `offset_mm` phải nằm
bên trong đoạn ngang được chọn. Kết quả trả riêng Pipe, fitting và accessory ID.
`demo_tag` được ghi vào Comments để native Schedule có thể lọc đúng cụm demo.

Với Cable Tray, MCP dùng Type được chọn để giữ dữ liệu Service Type/routing mà
Revit quản lý; không tự ghi đè bằng chuỗi text suy đoán.

### Nối và ngắt phần tử

`mep_connect` nhận hai element ID, tìm cặp connector chưa nối và tương thích
nhất, sau đó yêu cầu Revit tạo fitting. Phù hợp không chỉ là “ở gần nhau”: cặp
connector phải phù hợp về domain/profile và có thể được Revit xử lý.

`mep_disconnect` không ngắt một connector bất kỳ. Nó chỉ tìm cặp connector
đang **thực sự nối trực tiếp với element còn lại**. Điều này tránh làm hỏng
một kết nối khác trên cùng phần tử.

### Dịch chuyển, đổi type và đổi size

- `mep_move_route` nhận `dx_mm`, `dy_mm`, `dz_mm`. Nếu một route đang nối với
  fitting, MCP tính cả fitting liên quan trong thao tác di chuyển để tránh bỏ
  chúng lại phía sau một cách vô ý.
- `mep_change_type` áp Type mà người dùng/AI đã xác định qua `type_id`; nó không
  tự chọn Type dựa trên tên gần giống.
- `mep_change_size` dùng `diameter_mm` cho phần tử tròn, hoặc `width_mm` và
  `height_mm` cho phần tử chữ nhật khi phù hợp với phần tử đó.

Sau mỗi thao tác, Revit vẫn là nơi quyết định cuối cùng. Một Type không phù
hợp, một size không hợp lệ hoặc một routing condition không thỏa sẽ làm
transaction rollback.

### Tạo bảng thống kê: hỏi trước, không đoán cột

Trước khi tạo Schedule, AI gọi `documentation_plan` với
`schedule_discovery_categories` để Revit trả các field thực sự khả dụng cùng
khả năng sort và total. AI phải hỏi người dùng ba quyết định: cột nào và thứ tự
cột; sort/group theo thứ tự ưu tiên nào; cần tính tổng cột nào. Ví dụ “tên viết
tắt hệ thống” ánh xạ tới `system_abbreviation`, còn “diện tích” ánh xạ tới
`area` — nhưng chỉ được dùng nếu catalog của category hiện tại có field đó.

AI nên đề xuất một phương án sort/group dễ đọc dựa trên các cột đã chọn, nhưng
không apply trước khi người dùng xác nhận. Field tổng phải có `can_total=true`.
Nếu Revit không có field hoặc không cho total/sort, AI báo rõ và đưa ra lựa chọn
khả dụng thay vì tự đổi yêu cầu. Schema apply bắt buộc có `sort_by`,
`total_fields` và `learner_requirements_confirmed=true`.

### Đưa view lên Sheet: chọn khung rồi mới fit

AI phải liệt kê Title Block hiện có kèm kích thước/hướng giấy và hỏi người dùng
chọn khung. Sau đó AI đề xuất crop scope, vùng lề/chừa ô tên và các tỷ lệ chuẩn.
Chế độ `fit_to_title_block` crop theo `fit_element_ids`, thử lần lượt
`allowed_scales`, đặt viewport vào tâm vùng hữu dụng và đo lại cả viewport lẫn
view label. Chỉ kết quả có `fits_usable_region=true` mới được apply.

Nếu view chưa vừa, AI không tự chọn khung hoặc tỷ lệ khác: nó báo kích thước
vùng vẽ và viewport, rồi hỏi người dùng chọn khung lớn hơn, tỷ lệ nhỏ hơn hoặc
thu hẹp phạm vi crop. `learner_title_block_confirmed` và
`learner_layout_confirmed` là hai cổng bắt buộc.

---

## 5. Preview → Apply: AI sửa model có kiểm soát

Mọi thao tác ghi được thiết kế theo quy trình hai bước:

```text
1. AI đọc document / target / connector / type cần thiết
2. AI gọi mep_preview
3. Revit mô phỏng operation trong `TransactionGroup`, commit nội bộ để kiểm tra fitting/routing rồi rollback toàn bộ; model không đổi
4. Bridge trả về preview_id, mức xác thực `revit_transaction_rollback` và thời hạn 90 giây
5. AI hoặc người dùng gọi mep_apply_preview với preview_id
6. Revit kiểm tra lại, thực hiện TransactionGroup rồi báo kết quả
```

Preview chứa fingerprint của:

- Document hiện hành.
- Target được thao tác.
- Type, trạng thái pinned và connector của target.

Khi apply, MCP so sánh fingerprint mới với fingerprint lúc preview. Nếu đổi
document, target, type, connector hoặc trạng thái liên quan sau preview,
preview trở thành stale và bị từ chối. Token cũng chỉ được dùng một lần và hết
hạn sau 90 giây.

Người dùng có hai cách làm việc:

1. **Xem trước trước khi sửa** — thích hợp với route mới, kết nối có rủi ro
   hoặc các thao tác cần kỹ sư xác nhận.
2. **Yêu cầu thao tác trực tiếp** — các tool write vẫn mô phỏng Revit rollback
   nội bộ trước, rồi apply ngay nếu tất cả guard đạt yêu cầu. Kết quả trả về cho
   biết thao tác đã `auto_applied` hay chưa, kết quả `preview_validation` và
   `verification` sau commit.

Mọi write chạy trong `TransactionGroup`. Nếu bất kỳ đoạn nào thất bại — chẳng
hạn không tạo được elbow, Type không hợp lệ hoặc Revit phát sinh failure không
thể xử lý — MCP rollback toàn bộ group. Sau commit thành công, MCP đọc lại
element/type/size/connector và trả kết quả `verification`; vì vậy “thành công”
nghĩa là vừa commit vừa đọc lại được trạng thái thật. Kết quả lỗi không được xem
là thay đổi thành công một phần.

---

## 6. Hàng rào an toàn và trường hợp MCP sẽ từ chối

MCP ưu tiên an toàn model hơn việc “cố làm cho xong”. Các tool đọc vẫn có thể
hoạt động trong nhiều điều kiện, nhưng thao tác ghi bị giới hạn như sau:

| Tình trạng document/target | Đọc model | Thao tác MEP | Lý do |
| --- | --- | --- | --- |
| Model độc lập | Có | Có, sau preview/guard hợp lệ | Phạm vi v1 an toàn nhất. |
| Local của Central | Có | Có nếu mọi target editable | Tôn trọng worksharing ownership. |
| Central mở trực tiếp | Có | Không | Tránh ghi trực tiếp vào central. |
| Document read-only | Có | Không | Revit không cho phép ghi an toàn. |
| Target do người khác sở hữu | Có | Không | Không tranh chấp worksharing. |
| Target pinned | Có | Không | Tránh dịch/chỉnh nhầm đối tượng đã khóa. |
| Target ở trong group | Có | Không | Tránh thay đổi gây lỗi group. |
| Connector/domain/profile/type không hợp lệ | Có | Không | Tránh route/fitting không xác định. |

Ngoài các guard nói trên, v1 cố ý **không hỗ trợ**:

- Chạy C# hoặc Revit API tùy ý từ prompt.
- Save, Save As, Synchronize with Central hoặc các hành động quản lý file.
- Purge, xóa hàng loạt, thay đổi Project Settings.
- Bỏ qua quyền sở hữu, trạng thái read-only, pin, group hoặc quy tắc Central.
- Tự tạo family/fitting còn thiếu chỉ để ép một kết nối thành công.

Khi một request bị chặn, phản hồi có error code rõ ràng như `ReadOnly`,
`CentralBlocked`, `OwnershipBlocked`, `ProtectedElement`, `PreviewExpired`,
`PreviewInvalid` hoặc `TransactionFailed`. AI nên giải thích nguyên nhân và
đề xuất bước kiểm tra tiếp theo, không thử lách guard.

---

## 7. Audit, bảo mật và khả năng truy vết

Mỗi request qua bridge được ghi append-only vào:

```text
%LocalAppData%\DSCons\RevitMcp\audit.jsonl
```

Audit gồm thời điểm, client, user Windows, tool, kết quả, error code, preview
ID, document/path, trạng thái read-only/worksharing/Central, các element liên
quan và dấu hiệu rollback. Log này hữu ích khi cần rà một thao tác AI đã làm gì
hoặc vì sao Revit từ chối nó.

Kết nối không mở ra mạng LAN/Internet: bridge chỉ bind `127.0.0.1`. Khi Revit
khởi động add-in, nó tạo file session tạm ở:

```text
%LocalAppData%\DSCons\RevitMcp\session.json
```

File chứa port, process ID, thời điểm bắt đầu và session secret ngẫu nhiên.
MCP server đọc file này để xác thực với đúng phiên Revit. Nếu secret sai,
bridge trả lỗi `Unauthorized`.

---

## 8. Cách giao việc hiệu quả cho AI

Yêu cầu càng cụ thể, AI càng dễ chọn đúng tool và ít phải hỏi lại. Một yêu cầu
tốt thường bao gồm: phạm vi, phần tử/hệ thống, mục tiêu, vị trí/kích thước và
mức độ cho phép sửa model.

### Ví dụ: kiểm tra trước khi sửa

> “Kiểm tra document có phải Central mở trực tiếp không. Sau đó đọc connector
> network của hai phần tử tôi đang chọn, cho tôi biết chúng có thể nối được
> không. Chưa sửa model.”

AI nên gọi theo thứ tự `document_info` → `get_selection` →
`mep_connector_network`, rồi giải thích kết quả. Không có thao tác write.

### Ví dụ: tạo route có xác nhận

> “Tạo preview một route Pipe Type 123 tại Level 456: từ (0,0,0) đến
> (3000,0,0), rồi lên (3000,0,800). Chỉ áp dụng sau khi tôi xác nhận.”

AI nên tạo preview, trả `preview_id`, thời hạn và các điều kiện. Chỉ khi người
dùng xác nhận trong thời hạn, AI mới gọi `mep_apply_preview`.

### Ví dụ: thay đổi trực tiếp có phạm vi rõ

> “Dịch chuyển các ống ID 101, 102 và 103 sang phải 250 mm; kiểm tra an toàn
> rồi thực hiện.”

AI có thể đọc chi tiết/kết nối cần thiết, rồi gọi `mep_move_route`. Tool sẽ tự
Preview → Apply, đồng thời rollback nếu Revit không thể thực hiện an toàn.

### Ví dụ: yêu cầu AI nên từ chối

> “Bỏ pin toàn bộ ống trong model, xóa hết fitting lỗi, rồi sync lên Central.”

Đây là ngoài giới hạn v1: có thay đổi hàng loạt, bỏ qua protection và Sync.
AI cần nêu rõ không thực hiện được bằng MCP này; có thể hỗ trợ đọc/lập danh
sách để kỹ sư xử lý có kiểm soát.

---

## 9. Trạng thái kiểm chứng và giới hạn thực tế

Kiến trúc, contract test, Node MCP protocol smoke và build artifact cho Revit
2023/2025 đã được kiểm tra offline. Runtime Revit 2023 đã có smoke/read-write
matrix trên model copy cho các luồng chính: đọc document/selection/MEP,
tạo route Pipe/Duct/Cable Tray, rollback lỗi, preview expiry/stale và audit.

Một số điều kiện runtime cần tiếp tục được kiểm chứng đầy đủ trước khi xem là
coverage hoàn chỉnh trên mọi môi trường:

- Revit 2025 runtime matrix đầy đủ.
- Local của Central, Central mở trực tiếp và document read-only thực sự.
- Ownership conflict, target pinned và target nằm trong group.
- Reload Core qua Ribbon UI không cần restart Revit.

Do đó, MCP không nên được hiểu là công cụ tự động hóa không giới hạn. Nó là một
bridge MEP v1 có guard rõ ràng; mọi áp dụng vào dự án thật vẫn cần theo quy
trình BIM, model copy khi thử nghiệm và sự giám sát của người chịu trách nhiệm.

---

## 10. Tóm tắt một câu

**DSCons Revit MCP biến AI thành trợ lý Revit MEP có thể đọc dữ liệu thật,
phân tích connector/routing và thực hiện một số thay đổi có preview, guard,
rollback và audit — nhưng không bao giờ được phép vượt qua an toàn model hoặc
quyền kiểm soát của kỹ sư.**

## Tài liệu liên quan

- [Kiến trúc chi tiết](ARCHITECTURE.md)
- [Quy tắc an toàn Local/Central](SAFETY.md)
- [Quy trình runtime test trên model copy](RUNTIME-TEST.md)
- [Hướng dẫn cấu hình AI client](CLIENT-SETUP.md)
- [SOP nghiệp vụ MEP v1](../domain/mep-v1.md)
