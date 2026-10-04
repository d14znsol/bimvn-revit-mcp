# PDF/DWG/ảnh công trình → Revit MEPF

Luồng này ưu tiên Cơ điện MEPF. Kiến trúc và kết cấu chỉ cung cấp
Level, Grid, host, không gian và bằng chứng va chạm; hệ thống không tự dựng
toàn bộ mô hình kiến trúc/kết cấu từ hình ảnh giống nhau.

## Hợp đồng

```text
SourceEvidence v3
  → source_to_revit_proposal(mapping=mep_project_reconstruction)
  → ProjectReconstructionPlan
  → bim_context_snapshot + bim_model_catalog
  → bim_changeset_preview (TransactionGroup rollback)
  → xác nhận một lần
  → bim_changeset_apply
  → post-commit element/connector read-back
```

Mọi route phải có discipline, service, Revit Type, System Type nếu là
Pipe/Duct, Level, kích thước, tọa độ tim tuyến và locator nguồn. Mọi thiết
bị phải có MEPF category, FamilySymbol, Level, vị trí, góc quay và locator
nguồn. Tất cả mapping tọa độ/Level/Type/System/hình học/kích thước
phải được kỹ sư xác nhận.

## Gate đủ thông tin trước khi dựng

Quy tắc này áp dụng cho mọi dựng mới Project/Family từ PDF, DWG/DXF và ảnh,
không chỉ cho workflow chuyển phiên bản. Trước Preview, tool lập danh sách
trường bắt buộc theo đối tượng: đơn vị/scale, nguồn và revision, discipline và
service, nominal size/shape, Level/elevation, material/standard, Revit Type và
System Type, Family/Type nếu có, connector/routing intent và locator nguồn.
Với callout, text, dimension hoặc leader, bằng chứng phải xác định duy nhất
đối tượng hình học được nhắc tới. Trường thiếu, mâu thuẫn hoặc leader mơ hồ tạo
câu hỏi cho kỹ sư và chặn riêng Change Set/FamilySpec phụ thuộc; AI không được
điền giá trị theo nét vẽ, layer hoặc hình dạng tương tự.

### Cổng evidence-governance thực thi

`mepf_evidence_readiness` ghi một record cục bộ có checksum cho đúng
`target_kind + mapping + SourceEvidence`. Nó trả một ma trận đầy đủ và hỏi-gộp
các trường thiếu, không hỏi lần lượt từng field. `source_to_revit_proposal` chỉ
có thể thành `ready_for_fresh_revit_preview` khi record này khớp. Với Project,
sau khi có plan hash còn bắt buộc `mepf_engineering_review`; nếu có từ hai nguồn
thì bắt buộc `source_conflict_assess` đã giải quyết revision/datum/fact conflict.

```text
SourceEvidence → readiness (câu hỏi-gộp) → proposal draft
  → conflict alignment (nhiều nguồn) → engineering review (đúng plan hash)
  → fresh Revit Preview
```

Review kỹ thuật chỉ kiểm các fact xác định: service/System/kind, profile size,
Level/điểm cao độ, đường tim, slope/invert boundary và Family/hosting boundary.
Nó không tính hoặc chứng nhận thủy lực, áp suất, airflow, tải/voltage-drop,
short-circuit hay fire hydraulic.

## PDF bản vẽ

`family_source_inspect` nhận `sheet_metadata` theo trang: discipline M/P/F/E,
loại plan/riser/schematic/section/detail/schedule, drawing number, Level, tỷ lệ
và hướng Bắc. PDF vector và PDF scan được giữ tách biệt. Bộ đọc vector lấy
bounded `constructPath` của PDF.js, áp ma trận transform và rotation trang,
rồi lưu line/polyline/rectangle/curve theo hệ tọa độ điểm trang
`x sang phải, y xuống dưới`. Mỗi path có bounds, paint operator, trạng thái
linear/curve và SHA-256 riêng; giới hạn 5.000 path và 50.000 command mỗi trang.

Muốn đưa hình học vào Project plan phải có units và scale anchor do người dùng
xác nhận. Một route có thể trỏ chính xác `page + path_index + path_sha256`, hoặc
trỏ text-block SHA-256 khi bằng chứng nằm ở ghi chú/bảng. Path PDF không có
layer CAD và không tự mang nghĩa MEPF: màu/nét/line không tự biến thành ống,
ống gió, máng cáp, DN, cao độ hay System. Kỹ sư phải xác nhận mapping đó.

PDF scan không có vector path. Nó chỉ đi tiếp khi có anchor tỷ lệ, Level/view,
điểm khống chế và hình học normalized được người dùng xác nhận; OCR chỉ là gợi
ý để đối chiếu, không phải dimension hoặc engineering intent.

## DWG/DXF

DXF ASCII được parse bounded. DWG binary không được đọc như text.
`trusted_adapter_manifest` phải do Autodesk/Revit Link inspection hoặc ODA
Drawings-compatible adapter tạo và phải khớp SHA-256 của DWG. Manifest có
adapter/version, units, coordinate policy/origin/rotation, layouts, xrefs, layers,
blocks và entity inventory. Layer/handle dùng trong plan phải tồn tại trong
evidence bất biến. Không explode hoặc blind-import vào model đích.

Khi Revit đang khả dụng, `cad_geometry_inspect` có thể nhận
`use_revit_trusted_adapter=true` cùng units do kỹ sư xác nhận. Adapter tạo một
Family document tạm từ template đúng năm, import DWG tại origin, đọc tối đa
50.000 Line/Arc/Circle/Polyline và GraphicsStyle layer, rollback transaction rồi
đóng document với `Save=false`. Node kiểm đủ bốn bằng chứng trước khi chấp nhận
manifest: có manifest SHA-256-bound, `active_project_touched=false`,
`temporary_document_rolled_back=true` và `temporary_document_saved=false`.

Đường này hỗ trợ native DWG mà không đưa CAD vào Project đích, nhưng Revit API
không bảo toàn đầy đủ cấu trúc authoring DWG. Block/attribute/dimension/xref,
dynamic block, proxy object, hatch/text và entity bị tessellate/skip được báo là
giới hạn; khi cần metadata đó phải dùng ODA/Autodesk adapter chuyên dụng. Không
được suy MEP service/System/Type/Level từ tên layer mà chưa có kỹ sư xác nhận.
Render layout/model space thành ảnh độ phân giải cao chỉ dùng cho đối chiếu
trực quan, review và OCR trợ giúp; source of truth vẫn là vector/metadata CAD
và mapping được xác nhận. Raster không thay thế layer, handle, tọa độ, text,
dimension hoặc quan hệ leader-to-entity.

DXF ASCII hiện giữ riêng `TEXT`, `MTEXT`, `ATTRIB`, `DIMENSION` và `LEADER`
cùng handle/source provenance. `cad_annotation_assess` chỉ nhận mapping khi kỹ
sư chỉ rõ `annotation_handle`, `leader_handle` (nếu có) và
`target_entity_handle`, rồi xác nhận nghĩa service/DN/elevation/material. Nó
không chọn line gần leader nhất. `MLEADER`, CAD table và xref metadata không
được giả vờ đã parse: cần trusted Autodesk/ODA manifest hash-bound; nếu thiếu
thì workflow bị chặn ở `requires_trusted_adapter`.

## Ảnh công trình

Mỗi ảnh hiện trạng có `site_capture`: capture-set, camera, khu vực, Level
và world control points. Project proposal cần tối thiểu hai ảnh trong cùng
capture-set và tối thiểu ba world control points. Locator chỉ ra image region
cụ thể. Ảnh không chứng minh dịch vụ bị che, cao độ sau trần, connector,
host, fire rating hoặc vật thể phía sau bề mặt.

## MEPF buildable hiện tại

- Pipe và Conduit: route trực X/Y/Z, diameter, Type/Level; Pipe có System Type.
- Duct: round diameter hoặc rectangular width + height, Type/System/Level.
- Cable Tray: width + height, Type/Level.
- Fitting góc dùng Routing Preference hiện có; thất bại thì rollback cả batch.
- Thiết bị: Mechanical Equipment, Duct Terminal/Accessory, Pipe Accessory,
  Plumbing Fixture, Sprinkler, Electrical Equipment/Fixture và Lighting Fixture
  chỉ khi Family là non-hosted `OneLevelBased`.
- Apply trả element detail và connector network bằng Revit read-back.

## ProjectReconstructionPlan 1.1 theo giai đoạn

| Giai đoạn | Nội dung MEPF | Trạng thái hiện tại |
| --- | --- | --- |
| 0 | Hệ tọa độ, Level, Type, System và mapping nguồn | Bắt buộc kỹ sư xác nhận |
| 1 | Tạo route và thiết bị non-hosted | Có Change Set rollback Preview/Apply/read-back |
| 2 | Nối connector thiết bị ↔ route, elbow/tee/transition/union/tap | Direct/elbow có thể resolve logical key sau khi tạo trong cùng atomic Change Set; tee/transition/union/tap cần workflow riêng |
| 3 | Slope/invert, insulation Pipe/Duct, lining Duct | Insulation/lining resolve route vừa tạo trong cùng Change Set; slope chặn placeholder nằm ngang và chưa executable |
| 4 | Circuit, panel, pole, voltage, phase/load propagation | Lưu request; cần electrical connector read-back và runtime riêng |
| 5 | Opening/sleeve/firestop qua wall/floor/roof/structure | Coordination request, không tự cắt host |
| 6 | Kiểm tra độc lập | Bắt buộc sau mỗi Apply: geometry, Type, Level, System, size, connector network |

`connections` tham chiếu logical element key và vai trò/index connector, không
tham chiếu ElementId tạm đoán. Với direct/elbow, Change Set tạo route/equipment
trước, lập index logical-key → ElementId/connector thật ngay trong transaction,
nối và read-back mạng rồi mới commit; equipment bắt buộc connector index ổn định,
route dùng role `start`/`end`. Không tự di chuyển hai đầu để ép nối. Insulation
Pipe/Duct và lining Duct cũng resolve từng curve vừa tạo, tạo native Revit
Insulation/Lining, rồi kiểm host/type/thickness trước commit. `slope_plans` và
`electrical_circuits` được băm cùng plan nhưng loại khỏi executable operation
cho tới khi workflow tương ứng được runtime-certify. Vì vậy hệ thống có thể tạo
an toàn phase 1 mà không giả vờ đã hoàn thiện network/circuit; riêng route có
slope/invert sẽ fail closed và không tạo route ngang thay thế.

## Cách dựng sau khi đủ dữ liệu

1. Neo bộ nguồn bất biến bằng SHA-256 và revision; tách plan/riser/schematic/
   schedule, PDF vector/scan, DWG model/layout/xref và bộ ảnh đăng ký.
2. Đối chiếu context Revit thật: coordinate, Level, Type, System, FamilySymbol,
   category và connector. Kiến trúc/kết cấu chỉ cấp datum, host và clearance.
3. Kỹ sư duyệt bảng mapping: source locator → logical MEPF object → kích thước,
   cao độ/invert, System/Type/Level và quan hệ kết nối.
4. Sinh plan dependency-ordered. Phase 1 chỉ gồm route/equipment API đã hỗ trợ;
   các yêu cầu network, slope, insulation, circuit và penetration ở phase riêng.
5. Chạy rollback Preview trên bản sao Project, trình bày đúng operations và các
   phần bị giữ lại. Token xác nhận chỉ dùng một lần cho đúng plan hash/context.
6. Apply atomic phase đã duyệt, đọc lại element/connector từ Revit và so với
   plan. Nếu mismatch thì báo lỗi/rollback theo transaction; không Save/Sync.
7. Direct/elbow và insulation/lining có thể dùng ID vừa tạo trong cùng atomic
   Change Set; các phase khác chỉ đi tiếp sau khi phase trước có ID và connector
   read-back. Không dùng ID hoặc vị trí từ phiên trước làm bằng chứng.

### Độ sâu theo chuyên ngành

- **Mechanical/HVAC:** Duct supply/return/exhaust/fresh air, hydronic Pipe,
  equipment/terminal/accessory, fitting/network, insulation/lining, service và
  system classification. Airflow/pressure/calculation không suy từ nét vẽ.
- **Plumbing:** cold/hot water, drainage/vent/rainwater, fixture/accessory,
  diameter, invert/slope, insulation và sleeve. Drainage slope không được thay
  bằng route ngang.
- **Fire Protection:** sprinkler Pipe/equipment, diameter/system, head placement,
  sleeve/firestop coordination. Hydraulic design, hazard class và coverage phải
  do kỹ sư xác nhận và workflow riêng kiểm chứng.
- **Electrical/ELV:** Conduit/Cable Tray, electrical equipment/fixture/lighting,
  logical circuit/panel requests, poles/voltage. Cable sizing, breaker rating,
  phase balance, load propagation và panel schedule không suy từ PDF/ảnh.
- **Architecture/Structure:** Level/Grid, room/space/shaft, host, opening target,
  fire rating và clearance reference; không phải mục tiêu tái dựng chính.

## Fail closed

Tuyến dốc, đường chéo, fitting/routing chưa có evidence, Family hosted/
face/work-plane, sleeve/opening/fire stopping, mạch điện, panel assignment,
thiết bị bị che và tự reroute khi clash không được đưa vào Change Set.
Chúng trở thành finding/coordination request và cần workflow riêng. Không
Save/Sync, không coi hình giống nhau là chứng nhận LOD hoặc as-built.

Phần source/offline phải PASS trước. Fixture runtime phải PASS trên Revit
2023 trước khi lặp y hệt trên 2025.
