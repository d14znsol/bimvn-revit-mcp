# Nền tảng Family Blueprint v3

Family Blueprint v3 là hợp đồng khai báo dùng chung để dựng mới Loadable Family
MEP từ Autodesk `.rft`. Tên thiết bị không quyết định khả năng dựng. MCP đánh
giá các primitive, kiểu đặt, category, tham số và connector trong Blueprint rồi
chỉ chuyển dữ liệu đã kiểm tra sang Revit. Blueprint không nhận C#, tên Revit
API hoặc script tùy ý.

Quy trình chuẩn:

`catalog/yêu cầu → SourceEvidence → Blueprint v3 → FamilySpec v3 → rollback preview → xác nhận → staging RFA → mở lại/read-back → load/place riêng`

`family_source_inspect`, `family_spec_preview`, `family_build_preview` và
`family_build_apply` giữ nguyên tên. Evidence/Spec được lưu cục bộ tối đa 30
ngày tại `%LOCALAPPDATA%\DSCons\RevitMcp\family-records`; Apply token vẫn ngắn
hạn, ràng buộc tài nguyên và dùng một lần.

## Catalogue và lookup table cho Family

Đây là workflow dựng Family thông thường, không phụ thuộc workflow hạ phiên
bản. Khi dùng catalogue hãng cho Pipe Fitting/Accessory hay thiết bị MEP, tool
phải yêu cầu catalogue/CSV/Type Catalog do kỹ sư cung cấp hoặc phê duyệt. Nó
staging nguồn không ghi đè, ghi SHA-256/revision/provenance, kiểm schema,
lookup key, đơn vị, dải giá trị, record thiếu/trùng và map được duyệt từ từng
cột catalogue sang Family parameter/Type/connector. Chỉ sau đó mới tạo Revit
lookup table/Type data. Mỗi thay đổi lookup như DN, vật liệu, pressure class
hoặc center-to-end phải qua flex, reopen read-back và routing/connector Project
gate riêng. Không tự tìm catalog, không chép bảng không rõ revision và không
tạo giá trị catalogue còn thiếu bằng nội suy/đoán.

Fixture runtime thử nghiệm `synthetic_mechanical_coupling_runtime_fixture` là
dữ liệu DSCons tự tạo để kiểm **hợp đồng**, không phải catalogue nhà sản xuất
và không được dùng trong hồ sơ. Nó kiểm Pipe Fitting
`pipe_mechanical_coupling`: category/Part Type native, hai connector Pipe,
lookup DN → chiều dài/pressure class, flex và RFA staged/reopen. Kể cả khi
fixture PASS, Routing Preference, tự chèn vào route, hệ thống tính toán và mạng
Project vẫn là các gate độc lập.

`manufacturer_catalog_inspect` chỉ nhận CSV/TSV trong thư mục cục bộ đã được
kỹ sư chỉ định. Nó xử lý BOM/quoted fields, giới hạn kích thước, hash đúng bytes
và kiểm header/schema/key/đơn vị/range/duplicate trước khi trả
`lookup_table_draft`. Draft mang `catalogue_provenance` gồm record ID,
SHA-256, manufacturer, revision, column map và key order. `FamilySpec` kiểm
lại từng row của lookup table với record này; thay đổi revision tạo impact report
cho Type/parameter cần review, không âm thầm đổi RFA đã có. Cột text không được
ép vào Revit numeric lookup table.

Không có đường tắt ở `family_spec_preview`: mọi SourceEvidence v3 đi vào
Family Blueprint trực tiếp phải kèm một `mepf_evidence_readiness` đã PASS,
`target_kind=family` và khớp **đúng tập** evidence chính/phụ. Với Blueprint
thông thường không đi qua PDF/DXF/ảnh proposal, dùng
`mapping=family_blueprint`; với proposal thì giữ mapping gốc
(`pdf_catalog_family`, `image_family`, `family_profile`, linework). Thiếu record
trả trạng thái `requires_information_readiness` cùng câu hỏi-gộp; nó không thể
đi tới `family_build_preview`. Bridge Node kiểm lại trạng thái này trước khi
gửi payload vào Revit, nên không dùng spec cũ để lách gate.

`family_compatibility_assess` là cổng riêng cho RFA/Family đích có sẵn: so sánh
category, placement/hosting, parameter name/data type/scope/Shared GUID,
connector signature, Part Type/routing intent và nested dependency từ read-back
thực. Thiếu Revit/artifact evidence là `needs_information`; trùng tên hoặc cùng
category không bao giờ là bằng chứng tương thích.

`family_acceptance_matrix` là tool chỉ đọc của Node MCP, công khai ma trận 26
fixture đại diện và kiểm evidence theo từng gate. Evidence phải ghi rõ fixture,
gate, RFA hash khi reopen RFA, Revit version, record ID và thời điểm hoàn tất.
Tool chỉ kết luận `phase_complete=true` khi mọi gate đều PASS ở Revit 2023,
rồi toàn bộ ma trận tương ứng PASS ở Revit 2025 theo đúng thứ tự thời gian. Nó
không tạo RFA, không ghi Project, không lưu path/element ID và không thay thế
read-back Revit hoặc ledger riêng.

SourceEvidence v3 mở rộng nguồn thành PDF, ảnh, DXF và DWG. Mọi record gắn
SHA-256/revision, units, scale anchors, orientation, provenance và confidence.
`source_to_revit_proposal` là bước Node chỉ đọc ở trước Blueprint/Change Set.
PDF catalog tạo Blueprint nhiều Type khi mọi kích thước có locator page/block
SHA-256 bất biến. DXF tạo Blueprint Profile/Symbolic/Model/Detail hoàn chỉnh từ
entity whitelist, hoặc route-centerline proposal khi đủ
type/system/level/elevation/tolerance. Ảnh tạo Blueprint box/cylinder khi có ít
nhất hai view đã scale và kích thước người dùng xác nhận; FamilySpec giữ citation
của toàn bộ view qua `supporting_evidence_ids`. Output chưa phải Apply token hay
runtime evidence.

## Biên giới compiler hiện tại

| Khả năng | Trạng thái v3 | Bằng chứng/giới hạn |
| --- | --- | --- |
| Family mới từ `.rft` đúng năm | Đã có | Không clone/reference RFA; không ghi đè output |
| Chọn template theo behavior rồi category | Đã có, compile-only | Level/work-plane/face/wall/ceiling/floor/roof/line/two-level; template đặc thù không bị thay bằng Generic Model sai hành vi. Với model Family, compiler bắt buộc kiểm `FamilyPlacementType` khi vừa mở `.rft`, sau đổi category hợp lệ và sau reopen RFA; lệch behavior thì fail-closed |
| Extrusion trục X/Y/Z hoặc hướng tùy ý | Đã có, compile-only | Rectangle, circle, ring và flat-oval thật; profile và chiều sâu tham số; oval dùng hai cung + hai đoạn tiếp tuyến, khai báo trục lớn và chặn dải flex làm đảo hướng; `axis_direction` được chuẩn hóa thành vector đơn vị, profile plane/connector `start/end` đi theo đúng hướng; hướng tùy ý vẫn phải runtime-test trên template thật |
| Parameter/type/formula | Đã có, compile-only | Length/area/volume/number/integer/text/URL/yes-no/angle/material/currency và data type MEP airflow, piping flow, HVAC/Piping pressure, power/apparent power, voltage/current/frequency, number of poles, load classification, HVAC/Piping temperature; Type/Instance, nhóm hiển thị, description, metadata Shared Definition và thứ tự trong từng parameter group; numeric Type Parameter được hình học/connector tham chiếu bắt buộc có ca flex min/nominal/max đã duyệt |
| Nguồn kích thước/dữ liệu | Đã có, compile-only | Mỗi `confirmed_field` được chuẩn hóa thành xác nhận người dùng hoặc locator trang/block SHA-256 đối chiếu với SourceEvidence PDF bất biến. Scalar cũ được ghi rõ là user confirmation, không được ngầm hiểu là OCR/catalog extraction. Family có từ hai Type và `required_source_fields` bắt buộc có evidence riêng cho mọi Type, đúng giá trị từng source-bound parameter; Preview/Apply chỉ trả locator băm đã che nội dung nguồn |
| Type Catalog | Có điều kiện, compile-only | Xuất sidecar UTF-8 BOM `.txt` cùng basename RFA, chỉ nhận Type Parameter không formula/lookup và bắt buộc đủ giá trị cho mọi type; không ghi đè. Chỉ phát hành header/unit đã được Autodesk công bố cho dữ liệu chung; token/unit MEP chưa có bằng chứng load thực bị chặn fail-closed. Load/chọn type vẫn cần runtime Revit 2023 |
| Family behavior và Part Type | Đã có, compile-only | Áp dụng/read-back Part Type, Shared, Work Plane-Based, Always Vertical, Cut with Voids When Loaded, Maintain Annotation Orientation và Round Connector Dimension; fail-closed nếu template/category không expose hoặc read-only |
| Connector MEP | Có điều kiện, compile-only | Duct, pipe, electrical, conduit, cable tray; host face, hướng, role, size association, primary/link. Duct/Pipe có flow/loss/joint/engagement; `flow_factor` 0–1 chỉ với mode system, Pipe Global có thể khai allow-slope. Revit 2023 hiện **fail-closed** với Duct `flow_parameter`: rollback trên Duct Fitting xác nhận `RBS_DUCT_FLOW_PARAM` đúng data type AirFlow, writable và Flow Configuration=Preset nhưng `CanElementParameterBeAssociated=false`; phải dùng Family Editor UI có checkpoint rồi reopen/inspect, không công bố preset-airflow API/flex đã đạt. Pipe preset và các connector còn lại vẫn cần runtime matrix độc lập. Electrical power có voltage, apparent load, poles, power factor/state, balanced load và load classification qua Family Parameter association. `Breaks Into`/`Valve Breaks Into` Duct/Pipe Accessory bị buộc two-port inline body, start/end, primary, link và size/profile qua reopen RFA; route split/network Project vẫn là gate riêng |
| Staging và kiểm chứng file | Đã có, compile-only | GUID staging, no-overwrite cả RFA/TXT, reopen, checksum, form/parameter/connector read-back; `SaveAsOptions.Compact` và preview view hợp lệ được áp dụng theo `publication` |
| Không có Project đang mở | Đã có ở contract/code | Build Family dùng session Revit; load/place vẫn là workflow Project riêng |
| Revolution/sweep/blend/swept blend | Có một runtime gate hẹp; còn lại compile-only/UI fallback | Profile literal rectangle/circle/ring/oval cho phạm vi primitive hợp lệ. Profile tham số dùng direct Length Type Parameter đã có cho Revolution rectangle/circle/ring/oval; Blend thẳng X và Swept Blend một đoạn thẳng +X cho rectangle/circle/oval. Revit 2023 đã PASS rollback preview cho **Revolution profile chữ nhật + hai Length Type Parameter**, đủ min/nominal/max và bounds từng part thay đổi; chưa có RFA reopen. Sweep line/polyline/cung và elbow 45°/90° vẫn dựng được qua API khi profile là literal; nhưng mọi kích thước trong `Sweep.ProfileSketch` là **Family Editor UI fallback**, vì Revit 2023 từ chối geometric dimension reference qua Family API. Cable Tray Offset chỉ có path điều khiển API; profile chữ nhật tham số cũng là UI fallback. Không được công bố elbow/fitting có profile tham số là API-complete hoặc flex đã đạt trước khi UI checkpoint, flex từng min/nominal/max và reopen/inspect PASS. Mọi tổ hợp khác, đặc biệt profile tròn Extrusion/Pants, vẫn cần runtime constraint/flex/reopen trên template thật |
| Void extrusion/revolution/sweep/blend/swept blend và join solid | Đã có, compile-only | Cả năm primitive dùng cờ Solid/Void native của Revit; Void bắt buộc `cut_targets`, solid join bắt buộc `join_with`; operation và `IsSolid` được đọc lại |
| Reference Plane/Line và constraint graph | Một phần; semantic role, linear Dimension, Model/Detail Line endpoint và plane-to-solid R23 PASS | Có named reference ổn định, `Defines Origin`, subcategory/màu/line weight/pattern. Linear Dimension/Label trên hai Strong Reference Plane đã flex 300/600/900 mm và reopen PASS trên R23. `cut_vector` được normalize rồi chuyển thành điểm thứ ba đúng contract của `NewReferencePlane2`; Type value được khôi phục trước regenerate sau khi gắn label. Sau cùng sửa này, exact R23 fixtures cho Model Line endpoint binding, Detail Line endpoint binding và plane-to-solid alignment đều PASS min/nominal/max + staged reopen; các kết quả âm trước sửa đã bị thay thế. EQ/radial/angular và Project swap vẫn giữ boundary riêng; R25 exact-repeat còn mở |
| Shared parameter/lookup table | RFA reopen + Project Schedule field Revit 2023/2025 | Shared GUID cố định; group/description/visible/user-modifiable/hide-when-no-value; file tạm và khôi phục cấu hình; lookup inline + `size_lookup`. Nested `sharing:shared` có `parameter_map` được chuẩn hoá thành `shared_parameter_map_mode: "identity_only"`: child/parent cùng Instance Shared GUID, data type và name. Đây chỉ là danh tính definition và khả năng Schedule field; không giả lập `AssociateElementParameterToFamilyParameter` hay công bố truyền giá trị parent→child. Project rollback trên Revit 2023/2025 đã chứng minh cả hai category có Schedule field và cũng chứng minh thay giá trị parent không truyền sang child; Tag động/placed Tag vẫn là gate UI/RFA |
| Material | Revit 2023 và exact-year Revit 2025 RFA/reopen PASS; visual runtime còn mở | Fixture Autodesk Mechanical Equipment đã xác minh gán Material cố định vào form, Appearance RGB/transparency/glossiness/metal, Physical/Structural isotropic và Thermal solid sau reopen RFA trên Revit 2023/2025. Exact-year R25 artifact cũng xác minh texture/bump không khai báo được xóa. Khi `UseRenderAppearanceForShading=true`, màu kiểm chứng là `appearance.color_rgb` (Revit dẫn xuất `Material.Color`); `appearance` là bắt buộc. Stock bitmap nội bộ không bị coi là file; texture/bump được khai báo vẫn phải khớp filename/hash. Shaded/Realistic vẫn cần visual evidence độc lập và không được suy từ metadata/reopen |
| Light Source/photometric | Đã có, compile-only | Lighting Fixture bắt buộc khai báo point/line/rectangle/circle, spherical/hemispherical/spot/photometric web, dữ liệu từng Family Type, intensity/color/loss/filter/dimming và IES checksum-bound trong approved demo directory; template Generic Model fallback bị chặn, inspect/reopen đọc lại toàn bộ. Hành vi ánh sáng và phép tính photometric/IES trong Project vẫn cần runtime |
| Nested Family/array/mirror | Một phần; Shared child/parent RFA/reopen và Schedule-field Project rollback Revit 2023 PASS; exact benchmark fixture PASS trên Revit 2023/2025 | Nested chỉ nhận artifact đã build từ child Blueprint, khớp checksum/hash/category/`FamilyPlacementType`, interface parameter đã reopen-verified và cùng demo directory. Fixture Shared Generic Model child + Mechanical Equipment parent đã xác minh `OneLevelBased`, vị trí child, một Instance Shared GUID chung sau reopen và Schedule field tạm ở cả hai category trong Project copy. Shared mapping là `identity_only`, không giả lập association thứ hai; rollback thực tế đã cho kết quả parent thay 100→125 mm nhưng child vẫn 100 mm. `family_library_benchmark_preview` chỉ nhận một cặp monolithic/nested do người vận hành xác nhận cùng công năng, cùng category/`OneLevelBased`, Type Length parameter và ba giá trị min/nominal/max trùng nhau; exact fixture đã PASS rollback trên Revit 2023/2025 cho byte/hash, flex, `LoadFamily`, nhiều instance và `Regenerate`. Kết quả chỉ là phép đo của cặp đã khai báo, không chứng minh hai cấu trúc tương đương tuyệt đối hay Family lồng tối ưu hơn. Có `level_point`, `host_face_point` cho child WorkPlaneBased và `host_face_line` cho child CurveBased trên planar face trong parent. Linear array, Family Type swap và mirror giữ phạm vi bounded; các tổ hợp hosted + swap/array/mirror bị chặn; Dynamic Tag/placed Tag và kiến trúc truyền giá trị thật nếu cần vẫn là gate riêng |
| Symbolic/Model/Detail line và Filled Region | Một phần; endpoint binding R23 PASS | Symbolic Line, Model Line literal và Detail Line literal nhận polyline theo template phù hợp; Model Line còn có subcategory, visibility và liên kết Yes/No. `endpoint_bindings` tới named Reference Plane đã PASS exact R23 runtime cho cả Model Family và Detail Item với flex 300/600/900 mm, no-overwrite staging và reopen. Filled Region vẫn chỉ dùng template 2D phù hợp; exact R25 repeat còn mở |
| Hướng nhìn, hiển thị tham số và flip control | Đã có, compile-only | Solid form, Symbolic/Model Line và nested instance có thể liên kết `visibility_parameter` tới Family Yes/No Parameter; form/line hỗ trợ hướng nhìn. Model Family hỗ trợ bốn dạng flip control; control trên Detail Item/Annotation/Profile bị chặn |
| Profile Family | Một phần, compile-only | Tạo closed ModelCurve loop từ polyline đơn; chưa có profile tham số hoặc nhiều kiểu biên dạng phức tạp |
| Annotation/Tag label động | UI fallback | Phải dùng Family Editor qua Computer Use ngoài Revit, có checkpoint; panel nhúng không giả lập khả năng này |
| Coordination zone LOD350 | Đã có, compile-only | Box/cylinder cho maintenance, access, service, installation, removal path, operation swing, connection/support interface; material trong suốt, subcategory chuẩn, visibility/Yes-No, flex và reopen read-back. Đây là hình học phối hợp có chủ đích, không phải bằng chứng loại khỏi quantity hoặc chứng nhận LOD350 Project/BEP |
| LOD300/LOD350 | Chưa chứng nhận chung | Build thành công chỉ chứng minh Blueprint/file; hosting, kết nối và phối hợp cần runtime matrix riêng |

Autodesk yêu cầu chọn template theo kiểu host/hành vi trước category và lưu ý
một số Family cần template chuyên biệt. Resolver v3 thực hiện đúng thứ tự đó,
đồng thời chặn Generic Model fallback cho fitting/2D/Tag chuyên biệt. Xem
[About Family Templates](https://help.autodesk.com/cloudhelp/2023/ENU/Revit-Customize/files/GUID-E36987A9-A68F-4121-A391-907306BAA60A.htm).

Với các model behavior đã có public API evidence, Blueprint v3 không còn chỉ
tin vào tên `.rft`: `level_based → OneLevelBased`, `face_based`/
`work_plane_based → WorkPlaneBased`, `wall_based`/`ceiling_based`/
`floor_based`/`roof_based → OneLevelBasedHosted`, `line_based → CurveBased` và
`two_level_based → TwoLevelsBased`. Check chạy ba lần: vừa tạo Family từ
Autodesk template, sau khi recategorize từ Generic Model nếu được phép, và sau
khi reopen RFA staged. Đây chỉ là chứng minh **Family document behavior**;
đặt vào Project copy, host cut, đổi host, rotate/mirror và phối hợp vẫn là gate
runtime độc lập.

## Ma trận bao phủ MEP

`Có điều kiện` nghĩa là Family thuộc nhóm đó dựng được khi hành vi của nó chỉ
dùng tập compiler hiện tại và dữ liệu kỹ thuật đã được xác nhận. Category có
mapping không đồng nghĩa mọi cấu trúc Family trong category đã được chứng nhận.

| Nhóm | Ví dụ | Category đã mô tả | Trạng thái |
| --- | --- | --- | --- |
| HVAC | quạt, FCU/AHU, hộp gió, tiêu âm | Mechanical Equipment | Có điều kiện; axial legacy có evidence riêng |
| HVAC đầu cuối/phụ kiện | cửa gió, damper, fitting | Air Terminal, Duct Accessory/Fitting | Part Type, connector/tổn thất, transition, tee/cross, Tap Perpendicular/Tap Adjustable, wye/lateral và Pants đối xứng X–XY bounded đã có compile-only. Elbow 45°/90° có Sweep profile literal và connector tiếp tuyến; nếu cần kích thước profile tham số, đó là UI fallback chưa có evidence flex/reopen. Oval giữ orientation qua toàn dải flex; pants bất kỳ/multi-branch tự do, routing/Break Into và mạng Project vẫn chờ capability/runtime |
| Cấp thoát nước | bơm, bình/bồn, thiết bị vệ sinh | Mechanical Equipment, Plumbing Fixtures | Có điều kiện; pump legacy vẫn là pilot, chưa chứng nhận connector |
| Phụ kiện ống | van, lọc, nối mềm, nhiều nhánh | Pipe Accessory/Fitting | Shared Parameter, lookup, Part Type, connector kỹ thuật, reducer/transition, tee/cross, Tap Perpendicular/Tap Adjustable, wye/lateral và Pants đối xứng X–XY bounded đã có compile-only. Elbow 45°/90° chỉ API khi Sweep profile literal; profile đường kính tham số phải qua UI fallback và không có evidence flex/reopen. Pants bất kỳ/multi-branch tự do, routing/Break Into và mạng Project vẫn chờ capability/runtime |
| PCCC | sprinkler, bộ van, tủ/hộp | Sprinklers, Pipe Accessory, Mechanical/Electrical Equipment | Có điều kiện; chưa runtime certify |
| Điện | tủ, transformer, nguồn, ổ cắm | Electrical Equipment/Fixtures | Power connector load/circuit parameter association đã có compile-only; Project circuit, panel assignment, phase/load propagation và panel schedule còn thiếu |
| Điện nhẹ | báo cháy, data, an ninh, điều khiển | Fire Alarm/Data/Communication/Security/Nurse Call/Telephone | Có mapping category/connector; hosted/2D/runtime còn thiếu |
| Đường dẫn | conduit/cable tray fitting | Conduit/Cable Tray Fitting | Conduit round elbow/tee/cross/transition/union và Channel/Ladder Cable Tray rectangular horizontal/vertical elbow, tee/cross/transition/union/offset có geometry contract bounded, connector origin/normal/size/primary/link và reopen verification. Cable Tray Offset có path constraint-driven theo bốn Type Parameter, flex/read-back và kiểm lại dimension label sau reopen. Conduit không khai `offset` vì Autodesk không có Part Type này; routing/network vẫn chờ runtime |
| Hệ đỡ | ty treo, giá, bệ phối hợp | Generic Model/Mechanical Equipment | Có điều kiện cho extrusion, bounded reference graph, linear part array, bounded mirror và nested Blueprint theo điểm hoặc planar face của solid parent; Project hosting/runtime còn thiếu |
| Phụ trợ 2D | Detail Item, Profile, ký hiệu | Detail Item/Profile/Annotation | Có điều kiện, compile-only cho symbolic/detail line, Filled Region và Profile loop đơn; chưa có runtime Family Editor record |
| Tag MEP | Tag theo dữ liệu đối tượng | Tag template chuyên biệt | UI fallback có kiểm soát; hard-coded text không được nghiệm thu như Tag |

`breaks_into` và `valve_breaks_into` không được coi là chỉ một lựa chọn Part Type.
Trong Blueprint bounded, chúng chỉ nhận Duct/Pipe Accessory có đúng một Solid
Extrusion trục X làm thân routing, hai connector cùng thân ở `start`/`end`, đúng
một primary ở đầu `start`, liên kết hai chiều, classification chung `Fitting`
hoặc `Global`, và profile/kích thước connector khớp profile thân (Pipe chỉ
round). Sau reopen, compiler đọc lại body, origin/hướng, primary, size binding,
flow/classification và linkage. Điều này chứng minh contract file-level chứ
**không** chứng minh Revit đã split một duct/pipe, chọn Routing Preference hay
truyền network; các hành vi đó bắt buộc thử trên copied Project.

## Cửa chất lượng và LOD

- Mỗi kích thước, vị trí cổng và dữ liệu kỹ thuật quan trọng phải dẫn về
  SourceEvidence hoặc xác nhận người dùng. Không có dữ liệu thì Spec ở trạng
  thái `awaiting_human_confirmation`. Khi nhà cung cấp/revision đã được biết,
  Spec nhận `catalog_revision` gồm manufacturer, catalog ID, revision và tùy
  chọn ngày phát hành/dòng sản phẩm. Revision bắt buộc có
  `revision_provenance`: locator trang/block của SourceEvidence hoặc xác nhận
  người dùng tường minh; danh tính và provenance được hash cùng SourceEvidence.
  Với Family nhiều Type có `required_source_fields`, phải dùng
  `type_catalog_revisions` cho từng exact Type name: không được rơi về revision
  chung, bỏ sót một Type hoặc dùng provenance revision chung ngầm định.
- Khi `required_source_fields` không rỗng, mỗi field phải liên kết trực tiếp
  với ít nhất một `parameter.source_field`; parameter đó không được formula hay
  lookup. Validator dẫn xuất `source_traceability`, ghi rõ parameter và các
  đường dùng trong phần hình học/connector. Một Length/Area/Volume/Angle
  parameter được dùng trực tiếp mà không có source trace sẽ bị chặn; formula và
  lookup được ghi riêng là dẫn xuất, không được hiểu là bằng chứng catalog.
  `confirmed_fields` scalar cũ được giữ tương thích nhưng luôn chuẩn hóa thành
  `user_confirmed` (không phải extraction). Dạng có cấu trúc nhận một trong hai
  provenance: `{ kind: "user_confirmed" }`, hoặc
  `{ kind: "source_document", page, block_index, block_sha256 }`. Locator thứ
  hai phải khớp block đã fingerprint trong SourceEvidence có cùng SHA-256; Spec
  lưu `field_provenance` không chứa text/bbox nguồn. Preview/Apply kiểm lại
  binding lẫn provenance và chỉ trả locator băm đã che nội dung nguồn. Cơ chế
  này không chứng minh độ chính xác extraction, trang/bảng catalog áp dụng cho
  type nào, revision nhà sản xuất, sự đầy đủ theo từng Type hay hành vi Revit
  runtime. `catalog_revision_identity_v2` bind metadata người khai báo **và**
  locator/xác nhận revision với hash SourceEvidence bất biến; nó không xác thực
  nội dung PDF, revision có áp dụng thật, sự chấp thuận của hãng, đầy đủ catalog
  hoặc Type Catalog runtime.
- Với Blueprint có từ hai `types` và `required_source_fields`, phải dùng
  `type_confirmed_fields`: map exact Type name → các field có provenance. Mỗi
  direct `parameter.source_field` bắt buộc có value tường minh trong Type đó và
  phải bằng field đã xác nhận của chính Type; không được rơi về default hay
  xác nhận dùng chung. Đây là bảo vệ consistency của FamilySpec/Type Catalog,
  chưa là bằng chứng Type Catalog đã load/chọn được trong Revit hoặc catalog
  revision đó hoàn chỉnh.
- Mỗi numeric Type Parameter được hình học/connector tham chiếu phải khai báo
  `verification.parameter_flex_cases`. Compiler đối chiếu nominal với type hiện
  tại, flex đúng thứ tự min → nominal → max, đọc lại từng part/connector liên
  quan và khôi phục giá trị gốc trong `finally`. Bounding box tổng không đủ để PASS.
- Connector phải đọc lại role/domain/profile/size/origin/normal/classification;
  kết nối mạng thật vẫn phải thử trong Project copy.
- LOD300 tách thành hình học, tham số và connector. LOD350 còn cần bằng chứng
  giao tiếp/lắp đặt trong Project. Coarse/Medium/Fine không phải LOD.
- [BIMForum LOD Specification 2025](https://bimforum.org/wp-content/uploads/2026/01/LOD-Spec-2025-Part-I-Official.pdf)
  là khung tham chiếu; MCP không tự cấp chứng nhận LOD chỉ từ RFA.

### Coordination zone cho LOD350

`coordination_zones` là danh sách riêng, không trộn vào `parts`. Mỗi zone bắt
buộc `target_lod=LOD_350`, có mục đích rõ ràng, hình box/cylinder, trục X/Y/Z,
gốc đặt, kích thước literal hoặc direct Length Type Parameter, Material có
transparency tối thiểu 70, role `non_physical_coordination_zone` và subcategory
chuẩn theo mục đích. Kích thước dùng parameter phải có min/nominal/max flex.

Compiler dựng form, liên kết visibility Yes/No khi có, đọc lại bounds,
subcategory, Material, visibility và parameter binding trước/sau reopen. Zone
không được làm connector host, join/cut target, array/mirror member hoặc giả làm
physical part. Read-back ghi rõ `quantity_exclusion_certified=false` và
`requires_project_bep_validation=true`: solid ẩn bằng visibility vẫn tồn tại
trong Family, vì vậy RFA không được dùng để tự tuyên bố loại khỏi quantity,
không va chạm, đủ khoảng thao tác hoặc đạt toàn bộ LOD350.

## Mức áp dụng Material, Shared Parameter và Nested Family

### Material

Blueprint có thể tạo Material, đặt màu Shading và transparency, tìm chính xác
Surface/Cut Foreground Pattern theo tên và loại Drafting/Model, tạo Generic
Appearance Asset với diffuse color, transparency, glossiness và cờ metal, rồi
bật `UseRenderAppearanceForShading`. Khi bật cờ này, Revit dẫn xuất màu Shading
từ `appearance.color_rgb` sau khi mở lại RFA; vì vậy `appearance` là bắt buộc và
là giá trị màu được kiểm chứng, không dùng `Material.Color` cơ bản làm chứng cứ.
Form có thể nhận Material cố định hoặc liên
kết `MATERIAL_ID_PARAM` với Family Material Parameter. `family_inspect` và bước
reopen đọc lại đồ họa, pattern và Appearance Asset.

`physical_asset` ánh xạ tab Physical của Revit qua `StructuralAsset`: hỗ trợ
class `generic|concrete|metal|wood`, behavior isotropic, density kg/m³ và tùy
chọn Young modulus/Shear modulus MPa, Poisson ratio. `thermal_asset` hiện bounded
cho vật liệu `solid`: density kg/m³, thermal conductivity W/(m·K), specific heat
J/(kg·K), emissivity và tùy chọn porosity/reflectivity/transmits-light. Compiler
đổi đơn vị SI sang internal units, tạo hoặc cập nhật `PropertySetElement`, gán
`StructuralAssetId`/`ThermalAssetId`, rồi đọc ngược lại các giá trị trước và sau
khi mở RFA. Cách dùng PropertySetElement và hai material aspect này bám theo
[Autodesk Material API](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Revit_Geometric_Elements/Material/Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_Material_General_Material_Information_html.html).

`appearance.texture` và `appearance.bump` hiện nhận ảnh `.png|.jpg|.jpeg|.bmp|.tif|.tiff` theo
`file_name` basename, SHA-256 và (với bump) `amount` −1000…1000. Khi có texture/bump,
`use_render_appearance_for_shading=true` là bắt buộc. Node Preview và compiler kiểm độc lập:
file là direct child của `approved_demo_directory` cục bộ, 16 byte…20 MB, đúng magic
format/extension và đúng checksum. Compiler chỉ dùng `AppearanceAssetEditScope`, map
`GenericDiffuse`/`GenericBumpMap` sang connected `UnifiedBitmap`, rồi kiểm lại khi mở RFA.
`family_inspect` và reopen chỉ trả basename, SHA-256, byte count, schema map và cờ
`external_path_redacted`; không trả đường dẫn tuyệt đối. Output RFA khai báo external
dependency để giữ ảnh cạnh RFA hoặc relink từ nguồn đã duyệt.

Texture/bump này là bằng chứng source/compile/reopen cho liên kết Appearance Asset, không
phải bằng chứng màu/độ nhám đã render đúng. Appearance reflectivity nâng cao, Physical Asset
anisotropic/transverse-isotropic và Thermal Asset liquid/gas vẫn chưa có.
Tên Fill Pattern phụ thuộc content/ngôn ngữ của template và Generic Appearance
Asset phụ thuộc cài đặt Revit, nên mọi trường hợp đều fail-closed nếu tài nguyên
thật không tồn tại. Shaded/Realistic và giá trị Physical/Thermal vẫn cần runtime
visual/data check trên template đúng năm trước khi dùng cho tính toán dự án.

### Identity Data native theo Family Type

`identity_data.type_values` khai báo dữ liệu thư viện theo **từng Family Type**,
không tạo Family Parameter tự đặt tên trùng với trường chuẩn của Revit. Mỗi Type
được khai báo đúng một lần và phải có ít nhất một giá trị. Các field hiện được
hỗ trợ là `manufacturer`, `model`, `description`, `url`, `type_comments`,
`classification_number` và `classification_title`; chúng lần lượt ghi vào
`ALL_MODEL_MANUFACTURER`, `ALL_MODEL_MODEL`, `ALL_MODEL_DESCRIPTION`,
`ALL_MODEL_URL`, `ALL_MODEL_TYPE_COMMENTS`, `OMNICLASS_CODE` và
`OMNICLASS_DESCRIPTION`.

Compiler chỉ ghi khi template/category thực sự expose đúng native text field,
field không read-only, và giá trị đọc lại ngay sau khi ghi khớp từng Type. Sau
khi staging và mở lại RFA, `family_inspect.identity_data` đối chiếu lại từng
field đã khai báo. Thiếu field, field sai kiểu hoặc read-back sai sẽ dừng build;
không tự thay bằng custom parameter nhìn giống native field.

Đây là bằng chứng source/compile/reopen về dữ liệu Type trong RFA, **chưa** là
bằng chứng hãng đã phê duyệt catalogue, revision áp dụng, Schedule/Tag có expose
field, hay dữ liệu procurement/asset trong Project. Các việc đó cần fixture
Family/Project copy theo `RUNTIME-TEST.md`.

### Shared Parameter và phụ kiện ống gió/nước

Parameter có thể khai báo GUID cố định, Type/Instance, group, description,
`visible`, `user_modifiable` và `hide_when_no_value`. Compiler tạo definition
file tạm, luôn khôi phục `Application.SharedParametersFilename`, xóa file tạm và
đọc lại GUID, scope và group sau khi mở lại RFA. Revit 2023 có thể chỉ trả về
`InternalDefinition` cho Family Parameter đã thêm, nên API không luôn đọc lại
được `description`/`visible`/`user_modifiable`/`hide_when_no_value`; kết quả
phải ghi `unavailable_revit_api` và cần kiểm tra Family Editor UI, không được
gắn nhãn metadata là đã nghiệm thu. Lookup table, nhiều Family Type và
`size_lookup` tiếp tục dùng chung với Shared Parameter khi contract hợp lệ.

Khi child nested được khai báo `sharing: "shared"` và có `parameter_map`, mỗi
cặp map bắt buộc là **Instance Shared Parameter** ở cả child và parent, cùng một
`shared_guid`. Child artifact chỉ được phép dùng sau khi lưu interface không chứa
giá trị (tên, data type, scope, trạng thái Shared và GUID) đã đối chiếu với
read-back sau reopen. Compiler kiểm lại interface trước association, rồi
`family_inspect`/reopen kiểm child parameter, parent association và GUID trùng
nhau. Đây là kiểm chứng identity của RFA, chưa chứng minh cột Schedule/Tag của
Project; tham số số ở nhánh shared-interface này không được coi là flex hình học
theo Type.

Đối với Duct/Pipe Accessory và Fitting, `part_type` là bắt buộc và được kiểm theo
category. Validator kiểm discipline và số connector tối thiểu/chính xác cho cap,
elbow, tee, cross, transition, valve, damper và multi-port. Connector hỗ trợ
flow direction/configuration, loss method, coefficient/pressure drop, joint,
gender và engagement length, rồi đọc lại metadata. Với Duct/Pipe,
`flow_parameter` chỉ được khai báo khi mode là `preset`: Duct dùng data type
`airflow`, Pipe dùng `flow`. Với target **Revit 2023**, Blueprint có Duct
`flow_parameter` bị `duct_connector_flow_parameter_revit2023_fail_closed` trước
khi dựng: test rollback đã xác nhận parameter Flow có đúng data type, writable,
Flow Configuration=Preset và đã Regenerate nhưng API vẫn trả
`CanElementParameterBeAssociated=false`. Giữ parameter Airflow trong Blueprint
để bàn giao UI Family Editor có checkpoint; không chuyển sang geometry tĩnh,
không giả chứng nhận flex/connector association. Pipe preset chưa bị suy rộng từ
kết quả này và vẫn phải qua runtime matrix riêng. `flow_factor_parameter` chỉ
được liên kết khi mode là `system`, dùng number Type Parameter trong 0–1.
`allow_slope_adjustments` chỉ dùng Pipe connector có system classification
`Global`, đúng phạm vi Revit expose control này. Inspection trả lưu lượng L/s, flow factor, slope flag và
association; nó nêu rõ calculation/routing/network Project chưa được chứng
nhận. Đây là bằng chứng file-level; Routing Preference, thao tác Break Into,
nối mạng, schedule/tag theo GUID và đổi type/rotate/mirror trong Project vẫn
phải nghiệm thu riêng.

Riêng hành vi elbow, kể cả Conduit và Channel/Ladder Cable Tray, compiler bounded
hiện yêu cầu đúng một solid Sweep cung, bán kính path do Length Type Parameter trực
tiếp điều khiển, hai connector đối ứng tại `path_start`/`path_end`, connector chính
ở `path_start` và kích thước connector khớp profile round/rectangle/oval. Cable Tray
ngang dùng path XY, loại vertical elbow dùng path XZ. Hai tiếp tuyến phải giao nhau
tại origin của Family. **Chỉ Sweep có profile literal** được dựng hoàn toàn qua API.
Nếu width/height/diameter của profile là Family Parameter, `Sweep.ProfileSketch`
phải được hoàn thiện bằng Family Editor UI có checkpoint; API Revit 2023 từ chối
geometric dimension reference và `SketchEditScope` không dùng được cho Family
document. Vì vậy path/bán kính tham số không chứng minh profile/kích thước elbow
đã flex; phải có flex từng min/nominal/max và reopen/inspect qua UI trước RFA,
load/place hoặc claim fitting. Đây mới là contract/file-level evidence; hành vi
fitting trong mạng vẫn là cửa mở.

Riêng `part_type: tee`, compiler bounded dùng hai Extrusion giao tại origin: run
đối xứng theo trục X và branch theo trục Y hoặc Z. Ba connector phải nằm ở hai
đầu ngoài của run và đầu ngoài của branch; connector chính bắt buộc ở mặt `start`
trục X. Profile round/rectangle/oval và tham số kích thước connector phải khớp từng
nhánh; hai solid phải `join_with`. Flex đọc riêng bounds của run/branch và
origin/normal/size của ba connector; reopen kiểm tra lại origin, hướng, primary và
Family Parameter binding. `linked_to` nhiều cổng bị chặn cho đến khi topology
connector của tee có runtime evidence, vì API chỉ đọc một linked connector cho
mỗi cổng. Validator fail-closed theo discipline: duct nhận round/rectangular/oval,
pipe và conduit chỉ round, cable tray chỉ rectangular, electrical chỉ logical.
Wye/lateral/cross và Tap bounded đã có compile-only. Pants chỉ nhận topology ba
solid đối xứng X–XY: một inlet X âm kết thúc tại origin và hai outlet forward
15–75° đối xứng qua trục X; connector/profile/Type Length/join được kiểm trước
và sau reopen. Pants bất kỳ, multi-branch tự do và network propagation vẫn là
capability tiếp theo.

Riêng pathway fitting thẳng hàng, `transition` bắt buộc là một Blend trục X với
hai profile đầu/cuối dùng direct Length Type Parameter khác nhau; `union` là một
Extrusion trục X có chiều dài điều khiển bởi direct Length Type Parameter và hai
đầu cùng profile/size binding. Conduit chỉ nhận circle/round, Cable Tray chỉ nhận
rectangle/rectangular. Cả hai loại yêu cầu hai connector ở đúng đầu, primary tại
đầu `start`, liên kết đối ứng và được kiểm lại origin, normal, size binding,
primary/link sau khi mở lại RFA.

Cable Tray `offset` là một ngoại lệ bounded: một Sweep chữ nhật trên polyline
bốn điểm nằm trong XY hoặc XZ, đoạn đầu/cuối song song +X, đoạn chéo tạo góc
15–75° và có dịch chuyển ngang thực. Path mới dùng bốn direct Type Parameter
`lead_in_parameter`, `lead_out_parameter`, `lateral_offset_parameter` và
`offset_angle_parameter`; mỗi tham số phải có flex min/nominal/max và type đầu
tiên phải khớp nominal. Compiler tạo Reference Plane/Reference Line,
alignment và bốn Dimension Family Label, sau đó kiểm bốn điểm cùng tiếp tuyến
+X ở từng ca flex. Reopen verification dựng lại hình học nominal từ flex contract,
đối chiếu path XY/XZ và xác nhận cả bốn label còn tồn tại. Contract polyline bốn
điểm literal cũ vẫn được đọc để tương thích, nhưng capability mới không nhận
tọa độ path do caller gửi. Channel/Ladder đều dùng contract này.
`conduit_fitting: offset` bị chặn vì danh sách Part Type chính thức của Autodesk
chỉ có Transition và Union cho Conduit, không có Offset. Mọi kết quả trên vẫn là
source/compile/file-level; constraint solver trên template thật, Routing
Preference và kết nối mạng trong Project là các cửa nghiệm thu riêng.

### Probe Routing Preference trong Project copy

`family_routing_probe_preview` là cửa kiểm tra hành vi mới cho **Pipe Fitting**
và **Duct Fitting** chưa được load trong Project copy. Tool buộc re-anchor đúng
Project/View, `copied_project_confirmed: true`, RFA/type/MEP curve type/system
type/Level và vị trí test xác định. Trong một `TransactionGroup`, nó chỉ tạm
load RFA, đưa chính xác `FamilySymbol` được yêu cầu lên rule index 0 của nhóm
Routing Preference, tạo tình huống tương ứng Part Type (elbow, tee/wye/lateral
tee, cross/lateral cross, transition hoặc union), rồi bắt buộc Revit chọn đúng
symbol và connector của fitting phải nối đến mọi đoạn test. Toàn bộ Project,
rule và load RFA được rollback; ID trong kết quả chỉ là ID mô phỏng.

Probe này là bằng chứng **routing selection + physical connector network** cho
một type/profile/size của Project đã chọn, không phải chứng nhận chung. RFA
trùng tên đã load bị chặn để tránh nhầm Family. Nó chưa kiểm `Breaks Into`/
`Valve Breaks Into` qua thao tác UI, tính toán system/pressure loss, đổi type,
rotate/mirror, hosting hay LOD350/BEP. Các phần đó vẫn phải có fixture Project
copy và read-back riêng.

### Controlled UI gate cho Break Into / Valve Breaks Into

`Breaks Into` và `Valve Breaks Into` là hành vi inline của **Duct/Pipe
Accessory**, không phải Routing Preference của Duct/Pipe Fitting. Public API
không có factory thay thế thao tác UI native đó, vì vậy MCP không được giả vờ
đã đặt được component bằng API. Cặp tool `family_break_into_ui_preflight` và
`family_break_into_ui_verify` tách rõ hai việc:

1. Preflight chỉ đọc RFA trong copied Project đã re-anchor và chỉ PASS khi đúng
   category, exact Type, Part Type, đúng hai connector cùng domain/profile/kích
   thước, có trục thẳng hàng và hai normal đối nghịch; Pipe bắt buộc Round,
   classification phải cùng `Fitting` hoặc `Global`. Nó phát record một lần,
   tồn tại tối đa năm phút, không tạo transaction Project hay load/đặt Family.
2. Một operator thực hiện Revit UI gốc trên đúng Project copy/active view với
   Family Type đã preflight. Sau đó verify đọc **instance đã tạo** và đúng hai
   Pipe/Duct segment sau split. PASS đòi Part Type/type chính xác, hai End port
   vật lý đã connect theo quan hệ một-một với hai segment, profile/kích thước
   khớp và hai segment có cùng system type.

API không quan sát được nút UI nào đã được bấm; vì vậy verify chứng minh topology
hiện tại sau UI đã kiểm soát, không chứng minh danh tính lệnh UI. Nó cũng không
chứng nhận pressure-loss/system calculation, tự liền tuyến sau removal, thay
Type, rotate/mirror, hosting hoặc LOD350/BEP. Runtime fixture cần chạy Revit
2023 trước rồi mới lặp Revit 2025; không Save/Sync Project copy.

### Controlled UI gate cho Family hosted và Two Levels

`FamilyPlacementType` trong RFA chỉ chứng minh cấu hình template; nó không đủ
để nói một instance đã bám đúng wall/ceiling/floor/roof, đã cut host, hoặc đã
nhận đúng Base/Top Level trong Project. Vì các thao tác này cần workflow native
và có thể làm thay đổi Project copy, MCP dùng hai tool tách biệt:
`family_hosting_ui_preflight` và `family_hosting_ui_verify`.

1. Preflight re-anchor Project copy/current view, khóa RFA, exact Type và host
   hoặc cặp Level. Family wall/ceiling/floor/roof nhận cả
   `OneLevelBasedHosted` lẫn `WorkPlaneBased` (face-based); Family hai level
   phải là `TwoLevelsBased`. Nếu yêu cầu cut, RFA phải bật `Cut with Voids When
   Loaded`, có void native và host được Revit cho phép cắt. Preflight chỉ đọc,
   phát record một lần trong tối đa năm phút và tuyệt đối không place/rehost/cut
   bằng API.
2. Operator đặt Family bằng UI native trên đúng host hoặc giữa đúng hai Level
   trong lúc record còn hiệu lực; không đổi RFA/type/host, không Save/Sync.
3. Verify đọc instance hiện hữu: Family/Type/placement type, host và, riêng
   Family face-based, `HostFace` phải trỏ đúng element đã preflight (hoặc
   `FAMILY_BASE_LEVEL_PARAM`/`FAMILY_TOP_LEVEL_PARAM` cho two-level). Khi yêu
   cầu cut, `InstanceVoidCutUtils` phải trả host nằm trong danh sách phần tử bị
   cắt. Các cờ flip/rotate chỉ là capability hiện tại, không phải bằng chứng
   thao tác.

Nó không chứng minh danh tính lệnh UI, lịch sử rehost, type change,
rotate/mirror, host cut còn bền sau chỉnh sửa, hoặc LOD350/BEP. Runtime fixture
Project copy Revit 2023 rồi 2025 vẫn là gate mở; không Save/Sync.

### Nested Family

Nested child phải là RFA do một child Blueprint v3 đã Apply, có checksum và
Blueprint hash còn khớp. Artifact chỉ được đăng ký khi RFA đã mở lại và Revit
trả `FamilyPlacementType`; Blueprint declaration và placement type thật phải
khớp trước khi child được dùng. Child khai báo `shared: true|false`; parent khai
báo `sharing: shared|embedded`, và build bị chặn nếu hai phía không khớp.

Với child Shared, `parameter_map` không còn associate chỉ bằng tên: artifact phải
có interface parameter được Revit reopen xác thực. Parent và child phải cùng
data type, cùng scope Instance, cùng Shared GUID; compiler chuẩn hoá
`shared_parameter_map_mode: "identity_only"` và kiểm lại danh tính này sau khi
parent RFA được mở lại. Nó không gọi một association thứ hai và không coi GUID
chung là liên kết giá trị. Thiếu interface, GUID khác, parameter Type,
formula/lookup parent hoặc mode khác `identity_only` đều fail-closed.

Revit 2023 Project-copy rollback đã kiểm tra fixture thực: parent Instance
Shared Parameter đổi từ `100` sang `125` mm, còn direct Shared child proxy vẫn
`100` mm. Hai Schedule tạm đều thêm được field theo GUID (Mechanical Equipment
và Generic Model), nhưng đây chỉ là chứng cứ khả năng Schedule field. Vì vậy
Blueprint/RFA không được tuyên bố parent→child propagation. Nếu một catalog cần
đồng bộ giá trị thật thì phải có kiến trúc Family khác và fixture Project chứng
minh riêng; không được suy ra từ `sharing: shared`. Dynamic Label và placed Tag
vẫn yêu cầu Tag RFA + controlled Family Editor UI, sau đó kiểm lại trong Project
copy.

`placement_mode` mặc định là `level_point` cho child `level_based`. Hai phép
hosted mới là `host_face_point` cho child `face_based`/`work_plane_based` có
`FamilyPlacementType.WorkPlaneBased`, và `host_face_line` cho child `line_based`
có `FamilyPlacementType.CurveBased`. Host phải là solid độc lập trong parent;
điểm hoặc toàn bộ đường phải nằm trên planar face đã khai báo. Point placement
cần reference direction khác zero và không song song pháp tuyến. Compiler đọc
lại `Host`, `HostFace`, `LocationPoint`/`LocationCurve` và sau khi mở lại RFA
đối chiếu lại instance theo ElementId. Đây là face-hosted **bên trong parent
Family**, không phải chứng nhận wall/ceiling/floor/roof hosting trong Project.

Parent có thể khai báo `family_type` Type Parameter và `type_options` gồm ít
nhất hai child artifact cùng category. Mỗi parent Family Type chọn một option
bằng key; compiler kiểm danh sách giá trị Family Type hợp lệ, gán symbol ID,
associate selector của nested instance và đọc lại lựa chọn theo từng type. Linear
array có thể nhận nested component thường, tạo child trước array và đọc lại
`NumMembers`; nested hoán đổi bằng Family Type Parameter bị chặn khỏi array cho
đến khi tổ hợp swap/array được runtime-test.

Hosted nested không được kết hợp với Family Type swap, array, mirror hoặc free
rotation cho đến khi từng tổ hợp có runtime evidence. `OneLevelBasedHosted`,
`TwoLevelsBased` và host category-specific trong Project vẫn là capability riêng.

Nested Family là công cụ cấu trúc/tái sử dụng, không tự động chứng minh file nhỏ
hơn. `family_library_benchmark_preview` là harness rollback-only để đo RFA
byte/hash, min/nominal/max flex, `LoadFamily`, nhiều instance và `Regenerate`.
Nó bắt buộc hai RFA/Type riêng, RFA chưa được load trong Project copy, một origin
cô lập, Level, spacing/count hữu hạn, xác nhận cùng công năng, cùng category và
`FamilyPlacementType.OneLevelBased`, cùng ba giá trị flex; nếu thiếu thì
fail-closed. Mỗi RFA mở trong Family document riêng, set đúng Type Length
non-formula qua min/nominal/max và kiểm bounding box `GenericForm` thay đổi ở cả
hai transition, rồi đóng không lưu. Project chỉ load/place/regenerate trong
`TransactionGroup.RollBack`. Do API không thể chứng minh hai hình học, connector,
catalog hay LOD thực sự bằng nhau, output phải luôn ghi boundary; không được kết
luận giảm dung lượng/tăng tốc chỉ từ hai file không tương đương. Chưa có cặp
fixture monolithic/nested tương đương đã chạy thành công, vì vậy chưa có claim tối
ưu. Compact save đã có ở publication contract. `publication.purge_unused` đã có
đường source/compile giới hạn cho Revit 2024–2027, nhưng chưa có runtime Family
Editor và không thay thế benchmark chứng minh mức giảm dung lượng. Revit 2019–2023 không có public
`Document.GetUnusedElements`; output chỉ được fail rõ hoặc giữ nguyên kèm bằng
chứng không purge. Hosted/array/Family Type hiện vẫn chỉ có bằng chứng contract/
compile, chưa có Family Editor runtime cho wave này.

### Electrical connector và dữ liệu tải

Blueprint có thể khai báo load classification mới bằng key/name/abbreviation và
hai data type chuyên biệt `number_of_poles`, `load_classification`. Power
connector bắt buộc liên kết Family Parameter cho voltage, apparent load, number
of poles, power factor, balanced load và load classification; power-factor state
nhận `leading|lagging`. Validator chặn điện áp không dương, tải âm, pole ngoài
1–3, power factor ngoài `(0,1]`, load classification không tồn tại và trạng thái
balanced mâu thuẫn với `PowerBalanced|PowerUnBalanced`.

Compiler tạo/read-back `ElectricalLoadClassification`, associate các Family
Parameter vào built-in connector parameter và kiểm lại association cùng primary
status sau khi mở lại RFA. Điều này đóng khoảng trống dữ liệu ở file-level nhưng
chưa chứng minh Family tạo được `ElectricalSystem`, gán panel, phân pha hoặc đưa
đúng tải lên panel schedule trong Project. Autodesk lưu ý dữ liệu điện dùng trong
schedule được lấy từ primary connector, vì vậy primary status là một phần của
gate này: [Connector Properties](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-Model/files/GUID-3DE410FC-7BB7-44FD-B75E-A02C4F42C1AD.htm),
[Electrical Family Parameters](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-Customize/files/GUID-4E5DD909-DD4C-41B9-83DB-37E9F7CA9639.htm).

Ranh giới phiên bản: Revit 2019–2020 chưa có public
`ElectricalLoadClassification.Abbreviation` API. Compiler vẫn build cho hai năm
này nhưng fail-closed bằng `Unsupported` nếu Blueprint yêu cầu tạo load
classification có abbreviation; không âm thầm bỏ dữ liệu. Nhánh authoring và
read-back đầy đủ của capability này bắt đầu từ Revit 2021.

Lighting Fixture là nhánh độc lập, không suy Light Source từ electrical
connector. `light_source` bắt buộc cho category này và cấu trúc theo đúng API:
shape/distribution là thuộc tính Family, còn kích thước phát sáng và photometric
data được khai báo riêng cho từng Family Type. Shape hỗ trợ point, line,
rectangle và circle; distribution hỗ trợ spherical, hemispherical, spot và
photometric web. Spot kiểm beam/field/tilt; intensity hỗ trợ luminous flux,
luminous intensity, illuminance tại khoảng cách xác định hoặc wattage + efficacy.
Initial color nhận nhiệt độ 1800–20000 K hoặc preset; loss factor nhận basic hoặc
bảy thành phần advanced; color filter và dimming color cũng được read-back.

Photometric web chỉ nhận basename `.ies` và SHA-256. `family_spec_preview` yêu
cầu file là direct child của `approved_demo_directory`, tối đa 5 MB, có record
`TILT`, checksum đúng; compiler kiểm lại ngay trước Preview/Apply. RFA output trả
manifest `external_dependencies` để cảnh báo phải giữ IES bên cạnh RFA hoặc
relink từ nguồn hãng. Đường dẫn tuyệt đối bị redacted khỏi `family_inspect`.
Lighting Fixture không được recategorize từ Generic Model vì template đó không
bảo đảm có Light Source thật. Autodesk cũng phân biệt Light Source/IES với tải
điện và cung cấp API `LightFamily`/`PhotometricWebLightDistribution` cho dữ liệu
này: [About the Light Source for a Lighting Family](https://help.autodesk.com/cloudhelp/2023/ENU/Revit-Customize/files/GUID-52EF01D6-F429-4596-8F13-5FAB97DA1A10.htm),
[LightFamily API](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/53ebee14-8d6f-28ac-f44e-1e7bd906c7d8.htm).

Đây vẫn là source/compile evidence. Chưa có Revit 2023 runtime record chứng minh
template thật chấp nhận mọi combination, hướng/độ sáng phát ra, hệ số sử dụng
dựa trên IES, room/work-plane calculation hoặc Project electrical circuit/panel
propagation. Theo Autodesk, IES cung cấp dữ liệu kỹ thuật cho hệ số sử dụng và
không được dùng trực tiếp để render; hai cửa này không được gộp thành một claim.

### Ngân sách độ phức tạp và hiệu năng

Mỗi Blueprint nhận `performance_budget`; nếu không khai báo thì validator điền
bộ giới hạn bảo thủ. Ngân sách gồm dung lượng RFA, số form/void, parameter,
formula/lookup-driven parameter, type, nested instance, light source, độ sâu nested, array
member ở giá trị flex lớn nhất, mirror copy, connector, reference datum,
dimension, đường 2D, material, lookup row và điểm phức tạp tổng.

`complexity_assessment` là dữ liệu dẫn xuất, không phải dữ liệu người gọi được
phép tự chứng nhận. Validator xóa hash/assessment do client gửi, tính lại metric
và chặn Blueprint vượt ngân sách. Khi resolve child Blueprint, hệ thống lấy
complexity evidence từ artifact đã build, tính độ sâu và tổng điểm của cây
nested; artifact cũ hoặc bị sửa mà thiếu evidence sẽ không được dùng làm child.

Preview kiểm ngân sách cấu trúc nhưng chưa ghi file. Apply lưu compact vào staging,
mở lại RFA rồi đếm nội dung thực: form/void, parameter/formula, type, nested,
connector, reference, dimension, đường 2D, material và tổng element. Compiler
chặn imported CAD/geometry và chặn ngay nếu byte RFA hoặc metric sau reopen vượt
budget; output chưa được finalize trong trường hợp đó. Build result và artifact
lưu cả metric, budget và ranh giới kiểm chứng.

Điểm phức tạp chỉ là chỉ số triage xác định được từ cấu trúc, không phải thời gian
regeneration đo trong Revit. Nó cũng không chứng minh Nested làm file nhẹ hơn.
Muốn kết luận tối ưu phải benchmark cùng một fixture ở phương án nguyên khối và
nested: byte RFA, thời gian load, flex/regeneration, nhiều instance trong Project
copy và bộ nhớ. Purge phần dư template chỉ loại một lớp rác có phạm vi; nó không
đo hoặc chứng minh các chỉ số hiệu năng này.

### Quản trị type, hiển thị và phát hành

Blueprint tự điền `parameter_order` theo thứ tự khai báo hoặc nhận một danh sách
chứa đúng mỗi parameter key một lần. Revit chỉ cho đổi thứ tự tương đối trong
cùng group; compiler vì vậy kiểm theo từng group cả trước và sau khi mở lại RFA.

`publication.type_catalog` tạo file `.txt` cùng basename với RFA. Mỗi cột phải
là Type Parameter, không do formula/lookup điều khiển và có giá trị cho từng
Family Type. Sidecar dùng UTF-8 BOM, escape CSV, 1/0 cho Yes/No, tên Material
thật và đơn vị SI đã khai báo. Trước khi staging/finalize, compiler parse lại
CSV, kiểm delimiter/header/đủ số cột/dòng/tên Type duy nhất và round-trip byte
UTF-8 BOM. Preview trả thêm evidence này, không ghi file.

Để không đoán cú pháp, catalog hiện chỉ nhận các Type Parameter có header/unit
đã được Autodesk công bố: Length, Area, Volume, Angle, Currency, Number,
Integer, Text, URL, Yes/No và Material. Tên Parameter không được chứa `##` hay
line break; type name và giá trị text/material cũng không được tách dòng. Các
parameter MEP như flow, pressure, power, voltage, current, frequency,
temperature, load classification, number of poles và Family Type vẫn dùng được
trong RFA/connector, nhưng bị chặn khỏi Type Catalog cho tới khi có fixture
Revit 2023 load/select-type xác nhận chính token/unit tương ứng. Điều này không
làm mất dữ liệu MEP; nó ngăn xuất TXT có vẻ hợp lệ nhưng có thể không được Revit
nhận đúng.

`publication.compact_rfa` mặc định `true`. `preview_view` nhận `auto`,
`three_dimensional` hoặc `none`; lựa chọn 3D fail-closed nếu template không có
view hợp lệ.

`publication.purge_unused` chỉ nhận `scope: template_residue`, tối đa 1–10 pass
và `unsupported_behavior: fail|skip_with_evidence`. Compiler chụp ElementId ngay
sau khi mở Autodesk `.rft`; chỉ các phần tử đã tồn tại ở thời điểm đó mới có thể
là ứng viên purge, còn mọi nội dung Blueprint tạo sau đó được loại khỏi phạm vi.
Preview chạy purge trong `TransactionGroup` rồi rollback. Apply kiểm purge hội
tụ, staging/no-overwrite, mở lại RFA và xác nhận không còn template residue có
thể purge trước khi finalize output.

Nhánh này dùng `Document.GetUnusedElements`, chỉ có public API từ Revit 2024.
Trên Revit 2019–2023, `fail` dừng build; `skip_with_evidence` xuất RFA chưa purge
và ghi rõ `skipped_api_unavailable`, không được tạo claim tối ưu. Cả hai nhánh
hiện mới có bằng chứng source/compile; chưa có runtime load Type Catalog, purge
trên template thật, kiểm thumbnail trực quan, so sánh dung lượng trước/sau hoặc
benchmark regeneration.

## Capability còn thiếu theo nhóm

| Nhóm | Khoảng trống chưa được phép suy diễn |
| --- | --- |
| Quản trị type/dữ liệu | Runtime load/chọn type từ Type Catalog; xác minh token/unit MEP trên template thực và kiểm thứ tự hiển thị Family Types theo từng group. Catalog hiện fail-closed với token MEP chưa được chứng minh, thay vì suy đoán header |
| Constraint/hình học | Named reference ổn định, insertion origin, đồ họa Reference Plane và Angular/linear/radial/EQ đã có phạm vi bounded compile-only; Extrusion X/Y/Z/hướng tùy ý, Revolution, Blend thẳng X và Swept Blend thẳng +X đã nhận profile tham số trong phạm vi nêu ở bảng trên. Sweep thẳng/polyline/cung chỉ API-complete khi profile literal; `Sweep.ProfileSketch` có width/height/diameter parameter là Family Editor UI fallback, cần checkpoint + flex min/nominal/max + reopen/inspect. Pants đối xứng X–XY ba cổng được kiểm body/connector/join và reopen. Còn pants/multi-branch tự do, shape-handle/runtime snap, alignment Reference Line → hình học xoay, radial ngoài circle/ring extrusion, graph vòng/phụ thuộc tổng quát, Sweep có đoạn đầu không theo +X, Swept Blend nhiều đoạn/hướng tùy ý và runtime các tổ hợp cut phức tạp |
| Nested | Hosted point/line trên solid face trong parent và `FamilyPlacementType` child đã reopen-check. Shared nested Revit 2023 Project rollback đã xác minh Schedule field cho parent/child category và phủ định propagation tự động qua GUID; Family cha nay cũng fail-closed nếu template model không đúng `OneLevelBasedHosted`/`TwoLevelsBased` đã khai báo | Project wall/ceiling/floor/roof, host cut, runtime array, Family Type interchangeable child, Dynamic Tag/placed Tag, Revit 2025 và một kiến trúc truyền giá trị shared nested được fixture chứng minh |
| Material | Texture/bump Generic Appearance có checksum/path policy/reopen evidence đã compile-only; còn reflectivity nâng cao, Physical Asset anisotropic/transverse-isotropic, Thermal Asset liquid/gas, runtime Shaded/Realistic và xác minh dữ liệu phân tích trên template thật |
| Điện/chiếu sáng | Power connector và Light Source/IES/photometric từng type đã compile-only; load-classification abbreviation authoring cần Revit 2021+; còn runtime hành vi ánh sáng, phép tính photometric/IES và Project circuit/panel/phase/load propagation |
| 2D/Tag | Symbolic/Model Line, visibility theo hướng/Yes-No và flip control đã compile-only. Dynamic Tag Label có contract preflight field/format/background và UI handoff có checkpoint; cặp biểu diễn 2D Coarse/Medium và form 3D Fine được kiểm ngay khi tạo và sau reopen. Còn ký hiệu phụ thuộc host/cut phức tạp, Preview Visibility trực quan và Label động thật trong Revit |
| Project/LOD350 | Routing Preference, Break Into, kết nối thật, rotate/mirror/type-change; coordination zone đã compile-only nhưng còn kiểm clash/access/support và BEP trong Project |
| Tối ưu thư viện | Compact/preview selection, ngân sách complexity, độ sâu cây nested, byte RFA sau reopen và purge giới hạn `template_residue` cho Revit 2024–2027 đã compile-only; `family_library_benchmark_preview` đã PASS một exact declared-equivalent `OneLevelBased` fixture trên Revit 2023/2025 cho byte/hash, flex, load, placement và regeneration. Đây không phải kết luận Nested tối ưu phổ quát. Còn runtime purge trên template thật, thumbnail trực quan và quản trị thư viện |

Ngoài ba chủ đề Material, Shared Parameter và Nested Family, việc gọi một Family
MEP là dùng được còn phụ thuộc các lớp độc lập sau. Một lớp PASS không được dùng
để suy diễn lớp khác đã đạt:

| Lớp kiểm soát | Đã có | Cửa còn mở |
| --- | --- | --- |
| Nguồn/catalog | `SourceEvidence`, field bắt buộc, checkpoint người dùng, catalog identity manufacturer/catalog/revision có locator/xác nhận revision hash-bound với SourceEvidence và trace `required_source_field → direct Family Parameter → geometry/connector path`; đa Type chặn revision/provenance chung/fallback | Xác thực metadata với PDF/hãng, revision applicability, bảng/trang và completeness theo Type; formula/lookup chỉ là dẫn xuất được công khai |
| Template/hosting | Resolver theo behavior trước category; native `FamilyPlacementType` của Family cha được check khi tạo, sau category assignment và reopen; nested point/face/line trong parent; controlled UI preflight/read-back cho host hoặc two-level | Runtime fixture wall/ceiling/floor/roof/one/two-level, host cut và đổi host thật trong Project copy |
| Skeleton/constraint | Named Reference, origin, EQ/linear/radial/angular, alignment bounded | Shape handle, graph phụ thuộc/vòng, solid xoay theo Reference Line và flex tổ hợp phức tạp |
| Hình học fitting | Transition/reducer, elbow 45°/90°, tee/cross vuông góc, wye/lateral và Tap bounded 15–75° trong XY/XZ; Pants ba port đối xứng X–XY 15–75°; pathway transition/union và Cable Tray offset constraint-driven theo lead-in/lead-out/lateral/angle Type Parameter | Runtime constraint/flex/reopen trên template thật; pants/tap nhiều cấu hình, nhiều branch direction độc lập, tolerance join và topology nhiều nhánh |
| Connector/system | Năm discipline, size/origin/normal, primary, flow/loss/joint/engagement | Link topology nhiều cổng, Routing Preference, Break Into và network propagation trong Project |
| Dữ liệu/type | Formula Type Parameter có graph dependency/topological apply, evaluated min/nominal/max read-back và reopen exact read-back trên Revit 2023/2025; Instance `size_lookup` cùng lookup-table inventory cũng đã qua đúng fixture hai năm; nhiều type, Type Catalog và thứ tự parameter | Công thức sai đơn vị trên thêm template/locale, công thức trực tiếp điều khiển hình học, load/chọn Type Catalog và quy ước GUID doanh nghiệp |
| Biểu diễn | Subcategory, material, Coarse/Medium/Fine, directional visibility, 2D line/region và cặp biểu diễn 2D Coarse/Medium ↔ 3D Fine | Ma trận mọi view/detail level, masking/annotation phức tạp, Preview Visibility và thumbnail trực quan |
| Điện/chiếu sáng | Category, electrical connector/load classification/power association và Light Source shape/distribution/intensity/color/loss/IES compile-only | Runtime hướng/độ sáng phát ra, phép tính IES/room/work-plane và Project circuit/panel/phase/load propagation |
| Tag/annotation | Template 2D; Tag preflight cho semantic field, prefix/suffix, format/rounding, background và UI fallback có checkpoint; Shared nested Revit 2023 có Schedule field rollback ở parent/child category | Dynamic Label/placed Tag thực, hiển thị shared-nested trên Tag và locale/font |
| LOD350/phối hợp | Tách riêng geometry/parameter/connector evidence; box/cylinder coordination zone có purpose/material/subcategory/visibility/flex/reopen read-back | Hanger/support behavior thật, quantity policy, Project/BEP clash/access/removal/installation validation |
| Hiệu năng/phát hành | No-overwrite, reopen/checksum, Compact, preview, complexity/RFA budget, nested-depth/import guard; purge giới hạn phần dư template trên Revit 2024–2027 và skip/fail có evidence trên 2019–2023; benchmark rollback pairwise có minimum comparability gate | Cặp fixture monolithic↔nested công năng tương đương trên Project copy, runtime purge, library versioning/migration |

## Ngoại lệ giao diện

UI fallback chỉ chạy trong Family mới dành riêng cho tác vụ, không đồng thời
với executor API. Client ngoài Revit phải xác định đúng cửa sổ/document, quan
sát trước thao tác và kiểm tra lại. Mất trạng thái, đổi document hoặc xuất hiện
hộp thoại bất ngờ thì dừng. Không tự bấm hộp thoại bảo mật unsigned add-in.

Kho bài giảng riêng chỉ dùng để đối chiếu tiêu chí như Reference Plane trước
geometry, flex từng khối, nested/array/lookup/2D và tối ưu dung lượng. Nội dung
riêng không được sao chép vào repository công khai.

Nested/array/void/formula phải được dùng có chủ đích chứ không được coi là tối
ưu mặc định. Autodesk khuyến nghị hạn chế các cấu trúc này, ưu tiên ký hiệu 2D
thay hình học phức tạp ở view tài liệu và benchmark trước khi công bố nhẹ hơn:
[Family Optimization](https://help.autodesk.com/cloudhelp/2022/ENU/Revit-Customize/files/GUID-3FE01E79-E0F8-4870-A1D4-0CEC6748F4B2.htm).

## Hợp đồng hình học bổ sung

- `revolution` khai báo `profile_plane`, `profile_origin_mm`, hai điểm trục và góc
  đầu/cuối. Validator chặn trục rỗng, sai mặt phẳng, góc ngoài 0–360° và
  profile chạm/cắt trục. Rectangle/circle/ring/oval có thể dùng direct Length
  Type Parameter; toàn dải flex phải giữ profile cách trục, type đầu tiên phải
  khớp nominal và ring phải giữ outer lớn hơn inner ở mọi type/toàn dải flex.
- `sweep` khai báo path line/polyline đồng phẳng hoặc path cung bounded, cùng vị
  trí profile `start/midpoint/end`. `swept_blend` hiện chỉ nhận một đoạn path
  thẳng.
- Path cung dùng `kind:arc`, `plane:xy|xz`, giao điểm hai tiếp tuyến,
  `start_tangent:{x_mm:1,y_mm:0,z_mm:0}`, chiều rẽ, đúng một bán kính literal
  hoặc parameter và góc lớn hơn 0°, nhỏ hơn 180°. Với API, profile phải literal;
  profile tham số rectangle/circle/oval phải đặt tại `start` và chuyển sang
  `parameterized_sweep_profile_family_editor_ui`; ring tham số bị chặn. Oval là
  flat-oval hai cung + hai đoạn thẳng, bắt buộc `major_axis=width|height`, nhận
  đúng một cặp literal hoặc một cặp direct Length Type Parameter. Mọi type phải
  giữ major > minor và `major.flex.min > minor.flex.max` để không đảo orientation.
  Bán kính nhỏ nhất trong flex case phải lớn hơn envelope profile lớn nhất để
  tránh tự giao.
- Sweep line/polyline API nhận rectangle/circle/ring/oval profile literal. Nếu
  profile dùng direct Length Type Parameter thì `profile_location=start` và đoạn
  path đầu tiên theo +X chỉ là điều kiện của **UI handoff**, không phải API
  buildability: Family Editor phải dimension/label `Sweep.ProfileSketch`, flex
  từng min/nominal/max rồi reopen/inspect. Instance/formula/lookup vẫn bị chặn;
  ring phải giữ outer > inner trên toàn dải. Hướng path tùy ý ngoài quy tắc +X
  vẫn là capability mở.
- `blend` và `swept_blend` cần `profile` + `end_profile`, mỗi profile một loop.
  Blend thẳng trục X nhận rectangle/circle/oval profile tham số. Swept Blend
  tham số nhận đúng một đoạn path thẳng +X và profile đầu/cuối rectangle/circle/
  oval. Mỗi kích thước dùng direct Length Type Parameter, không Instance/formula/
  lookup và có flex min/nominal/max. Mapping/orientation của Revit phụ thuộc thứ
  tự cạnh nên runtime read-back vẫn là cửa nghiệm thu bắt buộc.
- `operation:void` áp dụng cho extrusion, revolution, sweep, blend và swept
  blend, kể cả các profile tham số bounded nêu trên. Mọi Void
  bắt buộc có `cut_targets`, không nhận material hoặc `visibility_parameter`;
  target phải tồn tại và là solid. Solid có thể khai báo `join_with`.
- Primitive/path/profile ngoài phạm vi bounded nêu trên chuyển assessment sang
  `requires_capability_or_ui_fallback`; compiler không tạo tham số nhìn có vẻ hợp
  lệ nhưng không có constraint/flex/read-back tương ứng.

Ví dụ lõi của elbow tròn 90°:

```json
{
  "family": {
    "category": "pipe_fitting",
    "template_behavior": "level_based",
    "part_type": "elbow",
    "primary_axis": "x"
  },
  "parts": [{
    "key": "elbow_body",
    "primitive": "sweep",
    "operation": "solid",
    "profile": {
      "shape": "circle",
      "diameter_parameter": "nominal_diameter"
    },
    "path": {
      "kind": "arc",
      "plane": "xy",
      "tangent_intersection_mm": { "x_mm": 0, "y_mm": 0, "z_mm": 0 },
      "start_tangent": { "x_mm": 1, "y_mm": 0, "z_mm": 0 },
      "turn_direction": "counterclockwise",
      "radius_parameter": "bend_radius",
      "sweep_angle_degrees": 90
    },
    "profile_location": "start"
  }],
  "connectors": [
    { "key": "inlet", "host_part": "elbow_body", "host_face": "path_start", "primary": true, "linked_to": "outlet" },
    { "key": "outlet", "host_part": "elbow_body", "host_face": "path_end", "linked_to": "inlet" }
  ]
}
```

Schema đầy đủ còn bắt buộc discipline, role, classification, profile/size
parameter, parameter/type và ca flex. Đoạn rút gọn này minh họa hợp đồng path
và **phải** được assessment là UI fallback vì `diameter_parameter` nằm trong
`Sweep.ProfileSketch`; để minh họa API-only, thay nó bằng `diameter_mm` literal.

Extrusion có thể dùng `axis: x|y|z` hoặc `axis_direction:{x,y,z}`, nhưng không
được khai cả hai. Validator chuẩn hóa `axis_direction` thành vector đơn vị;
compiler dựng profile plane trực giao từ vector đó, giữ width/height orientation,
đặt connector `start/end` theo pháp tuyến thật và đọc lại origin/normal/size
binding sau reopen. Với Wye/Lateral bounded, nhánh phải nằm trong XY hoặc XZ,
tạo góc xiên 15–75° với trục X, bắt đầu/kết thúc ở Family origin và join với run.
Cross dùng nhánh đối xứng qua origin; `lateral_cross` dùng cùng quy tắc nhưng nhánh
xiên. Nếu template không có view phù hợp, compiler chỉ được tạo Section View nội
bộ khi template/API cho phép, nếu không phải fail-closed. `linked_to` cho ba/bốn
cổng vẫn bị chặn cho tới khi topology thật được runtime chứng nhận; geometry và
connector placement không được dùng thay cho bằng chứng network.

`visibility_parameter` chỉ được tham chiếu Family Parameter kiểu Yes/No. Compiler
liên kết tham số `Visible` native của solid form, Symbolic Line, Model Line hoặc
nested instance, rồi đọc lại association. `family_inspect` trả riêng inventory
Model/Symbolic Curve, line style, sketch plane, directional visibility và binding.
Model Line bị fail-closed trên Detail Item, Generic Annotation, Profile và Tag.

### Presentation subcategory cho hình 3D và 2D

`presentation_subcategories` là contract Object Styles riêng cho nội dung Family:
mỗi mục có `key`, tên bắt đầu bằng `DSCons `, RGB, projection line weight và tùy
chọn cut line weight/line pattern có sẵn trong Autodesk template. `parts`,
`symbolic_lines`, `model_lines`, `detail_lines` và `profile_loops` có thể dùng
`subcategory_key` để cùng một chuẩn đồ họa. Đây khác với
`reference_plane_subcategories`: Reference Plane là skeleton/datum; presentation
subcategory là hình physical hoặc 2D xuất bản.

Compiler tạo hoặc cập nhật đúng subcategory khai báo, gán nó cho form/line và
đọc lại màu, line weight, pattern, form subcategory và line style sau reopen.
`family_inspect` trả inventory subcategory và subcategory của từng form/curve.
Mọi key không tồn tại, tên trùng, tên không có prefix `DSCons `, RGB/line weight
không hợp lệ hoặc line pattern không tồn tại đều fail-closed. Ngân sách
`max_presentation_subcategories` chỉ tính những subcategory do Blueprint/compiler
quản lý; không tính các subcategory mặc định có sẵn trong `.rft` Autodesk.
Điều này kiểm chứng cấu hình graphics trong RFA, chưa chứng minh hiển thị Shaded,
Hidden Line, in PDF/CAD hoặc Object Styles override trong Project.

### Dynamic Tag Label: preflight và bàn giao UI có kiểm soát

`template_behavior: "tag"` chỉ hợp lệ với `family.category: "tag"`. Blueprint
phải khai báo ít nhất một `tag_labels` và đúng một `ui_fallbacks` action
`tag_label_editor` có human checkpoint. Mỗi label có `semantic_field` trong tập
System Abbreviation, Size, Elevation/BOD/COD/TOD, Mark, Type Mark, Family and
Type, Comments hoặc Shared Parameter. Shared Parameter phải có cả tên và GUID;
Size/cao độ mới được dùng format custom, rounding và dấu `+`. `tag_background`
chỉ nhận `transparent|opaque`.

Preview tạo `tag_ui_plan` — danh sách field/format/background cần thực hiện và
kiểm lại — nhưng đánh dấu `buildable_by_api=false` với lý do
`tag_label_editor_requires_controlled_ui`. API không được tuyên bố đã tạo Label
động, không tạo RFA Tag có Label và không điều khiển giao diện trong Panel Chat.
Family Editor/Computer Use phải mở đúng Family, quan sát từng thao tác Label,
thực hiện checkpoint người dùng rồi reopen/inspect trước khi load. Đây mới là
preflight source/offline, chưa phải bằng chứng runtime Dynamic Tag, Schedule/Tag
Shared GUID, locale/font hoặc LOD.

### Cặp biểu diễn 2D/3D theo Detail Level

detail_level_representations là contract tùy chọn dành cho Model Family cần ra
hồ sơ nhẹ mà vẫn giữ hình học phối hợp ở mức chi tiết. Mỗi set nêu rõ các
physical_part_keys và symbolic_line_keys/model_line_keys, với policy duy nhất
hiện có là coarse_medium_2d__fine_3d. Validator chỉ chấp nhận:

- Form vật lý là solid, chỉ hiện Fine; Coarse và Medium phải tắt.
- Line 2D hiện Coarse/Medium và phải tắt ở Fine.
- Mỗi part/line chỉ ở một set và role phải duy nhất để đối chiếu lại sau reopen.

Compiler kiểm visibility và số segment ngay sau khi tạo; sau khi lưu/mở lại RFA,
nó đối chiếu form theo subcategory, line theo line style/visibility và tọa độ
từng đoạn line với dung sai 0.01 mm. Điều này chỉ chứng minh setting và hình học
khai báo trong RFA, chưa thay thế kiểm tra trực quan Preview Visibility, view
Section/Elevation, in PDF hay CAD export trong Family Editor/Project.

Shared parameter được khai báo bằng `shared_guid`; compiler tạo file định
nghĩa tạm, khôi phục `SharedParametersFilename` trong `finally` và xóa file tạm.
Blueprint có thể đặt group, description và metadata Shared Definition
`visible`/`user_modifiable`/`hide_when_no_value`. GUID, scope và group được đọc
lại bằng `family_inspect`; nếu Revit chỉ expose `InternalDefinition`, metadata
được trả `unavailable_revit_api` và phải kiểm tra trong Family Editor UI.
Lookup table khai báo cột, cột tra, dòng và liên kết parameter; dữ liệu được
nhúng vào Family qua `FamilySizeTableManager`, không phụ thuộc file CSV sau khi
xuất RFA. Theo hướng dẫn Autodesk, `size_lookup` trong Blueprint chỉ dùng cho
parameter Instance; Type data nên dùng Family Type/Type Catalog.

### Formula và lookup: thứ tự vòng đời Family Type

Formula Family Parameter là một rủi ro riêng, không chỉ là chuỗi công thức.
Revit có thể từ chối `FamilyManager.SetFormula` khi Family chưa có Type, hoặc
khi các công thức tạo vòng phụ thuộc. Vì vậy Blueprint chỉ nhận formula ở
Type Parameter có data type phù hợp, cấm tên Family Parameter trùng nhau hoặc
trùng từ khóa công thức, và lập `formula_dependency_graph` theo đúng **tên**
Family Parameter. `formula_dependencies`, nếu được khai báo, phải khớp hoàn
toàn với dependency suy ra từ công thức; self-reference và cycle bị chặn.

Compiler tạo Family Type và gán các giá trị trực tiếp trước, sau đó mới áp
dụng formula theo `application_order` topo, rồi mới áp dụng `size_lookup`.
Khi reopen RFA, nó đối chiếu chính xác formula/`size_lookup` với Blueprint và
inventory lookup table; không chỉ kiểm chuỗi ở Preview. Flex không set trực tiếp
formula/lookup parameter, mà thay direct dependency rồi đọc giá trị do Revit
đánh giá cho toàn bộ dependency closure.

Fixture ngày 26/09/2026 đã PASS trên Revit 2023 rồi 2025: `Base Width`
400/600/900 mm làm Type formula `Overall Width = Base Width + 2 * Clearance`
đánh giá 500/700/1000 mm và Instance `size_lookup` đánh giá 40/60/90 mm. Cả hai
RFA giữ lookup table `DSCons Formula Lookup Sizes` sau reopen; R23 là 434176 B,
SHA-256 `7CCE61654D3BBD9038DC92EDD3A382EF4774F6BB59C83D7EB7B8A6316082E11E`,
R25 là 454656 B, SHA-256
`D248B24B3E139BFFF46C94FB0CE784402D34F7FFDD7AB78AB90553A09A768EC8`.

Ranh giới quan trọng: fixture chứng nhận **giá trị formula/lookup được Revit đánh
giá**, không chứng nhận hình học tham số đi theo một parameter dẫn xuất. Một thử
nghiệm phụ đã cho thấy formula đổi đúng 500/700/1000 nhưng bounds của form gắn
gián tiếp không đổi; vì vậy formula-driven geometry vẫn fail-closed thành gate
riêng. Cycle/dependency mismatch tiếp tục bị chặn offline; lỗi đơn vị vẫn phải
được kiểm trên từng template/locale cần phát hành.

Reference graph khai báo riêng `reference_planes`, `reference_lines`,
`dimensions` và `alignments`. `kind:linear` nhận Length Type Parameter hoặc
`equality:true`; EQ cần ít nhất ba reference và được lan truyền trong graph để
flex tìm tới mọi part alignment liên quan. `kind:radial` hiện chỉ nhận circle/ring
extrusion trục X, chỉ rõ profile loop và gắn Length Type Parameter trực tiếp lên
  cung profile. `kind:angular` nhận đúng hai Reference Line đồng phẳng, cung kích
  thước literal và Angle Type Parameter. Sweep `path.kind:reference_line` dùng
  Reference Line đó làm path native, không dùng transform danh nghĩa: baseline phải
  đứng trước, driven line đứng sau, cùng pivot, baseline theo trục dương của plane,
  `start_angle_degrees=0`, nominal/flex nằm trong 0–180°. Bounded wave chỉ nhận
  solid Sweep có profile literal, không connector/cut/join/array/mirror. Flex đọc
  lại endpoint/pivot/plane/độ dài và đúng góc ở min/nominal/max; reopen kiểm lại
  path nominal và Family Label. `family_inspect` trả shape, segment count, EQ và
  Family Label cho từng Dimension.

Mỗi Reference Plane nhận `reference_type` gồm các named reference ổn định
`left/center_left_right/right`, `front/center_front_back/back` và
`bottom/center_elevation/top`, ngoài `strong/weak/not_reference`. Trường
`strength` cũ vẫn được chuẩn hóa để tương thích; nếu khai cả hai mà mâu thuẫn thì
validator chặn. Named reference chỉ được xuất hiện một lần cho mỗi vai trò, phù
hợp giới hạn của Revit và giúp dimension có cơ sở ổn định hơn khi đổi type/family.

Insertion origin tùy chỉnh phải có đúng hai Reference Plane giao nhau cùng
`defines_origin:true`; một plane, hơn hai plane hoặc hai plane song song đều bị
chặn trước Revit. `reference_plane_subcategories` khai báo tên, RGB, projection
line weight 1–16 và tùy chọn line pattern theo tên chính xác. Compiler tạo/gán
subcategory dưới category Reference Planes, đọc lại `ELEM_REFERENCE_NAME`,
`DATUM_PLANE_DEFINES_ORIGIN` và `CLINE_SUBCATEGORY`, rồi đối chiếu lại sau khi
mở RFA. Trước khi tạo skeleton khai báo, compiler giải phóng đúng named role và
cờ origin trùng đang có sẵn trong Autodesk template; sau khi tạo xong, toàn
Family phải còn đúng một owner cho mỗi named role và đúng hai origin planes.
`family_inspect` trả `reference_type`, `defines_origin`, subcategory và
graphics cùng các count named/origin/styled. Line pattern không tồn tại trong
template bị fail-closed. Phần này mới có bằng chứng source/compile; shape handle,
snap/dimension ổn định khi swap trong Project vẫn cần runtime Revit.

Runtime Revit 2023 ngày 26/09/2026 đã sửa và chứng nhận riêng **semantic role**:
`Is Reference` phải đọc/ghi qua `ELEM_REFERENCE_NAME`, không phải tham số khác
`ELEM_IS_REFERENCE`. Native integer của ba role tổng quát là 12/13/14 cho
Not/Strong/Weak, không được cast trực tiếp từ enum public. Hai plane Strong đã
staged, mở lại và đọc đúng role trong RFA 434176 B, SHA-256
`7505B1EFA00BD497A7561ABCEE289C99F7A5030C156B3313EDCA6AA39BC04EF4`.
Kết quả này chỉ chứng nhận role/name/geometry plane; không tự chứng nhận
Dimension, Alignment, shape handle hay Project swap.

Fixture Linear Dimension tách riêng sau đó phát hiện hai lỗi lifecycle: trường
`cut_vector` là hướng nhưng `NewReferencePlane2` yêu cầu một `thirdPnt`, và
Revit 2023 có thể thay Type value bằng sentinel −1 ft ngay khi gắn
`FamilyLabel`. Compiler hiện normalize hướng, dựng điểm thứ ba từ trung điểm
bubble/free, rồi khôi phục đúng Type value đã xác nhận trước lần regenerate đầu
tiên; không dùng số đoán và vẫn fail nếu read-back sai. R23 đã PASS
min/nominal/max 300/600/900 mm và reopen giữ label `DSCons Control Span` cùng
hai references. RFA `DSConsDimensionRuntimeFixture_R2023_20260926A.rfa` có
434176 B, SHA-256
`588C7521C170C6DDA6D3CCBEFE3CB3B9EBE5AB279848B64922102D11F940301C`.
Project copy vẫn `modified=false`. Kết quả này chứng nhận linear plane-to-plane
Dimension/Label trên Strong Reference; EQ, radial/angular, named-role dimension,
shape handle và Project swap vẫn là gate độc lập.

`model_lines` của Model Family nhận `endpoint_bindings` cho đầu `start` và/hoặc
`end`, mỗi đầu trỏ tới đúng một `reference_plane_key`. Validator kiểm cùng
`view_plane`, đầu mút nominal nằm trên Plane và không khai trùng một đầu. Sau
khi sửa cách dựng `NewReferencePlane2.thirdPnt`, exact R23 fixture đã PASS
Preview, flex 300/600/900 mm, Apply no-overwrite và reopen: hai endpoint vẫn nằm
trên named Left/Right planes và Width Dimension còn nguyên. Artifact
`DSConsModelLineRuntimeFixture_R2023_20260926A.rfa` có 434176 B, SHA-256
`127469D56671F2B842E728544F8748E3DB1CD27D4A42181C9ACA56E8BB25AE27`.
Kết quả `NewAlignment` âm trước sửa xuất phát từ Reference Plane bị dựng sai,
không phải giới hạn Model Line endpoint; assessment vì vậy giữ nhánh API.

Với `template_behavior:detail_item`, `detail_lines` có cùng
`endpoint_bindings` để dựng Detail Item/Flexible 2D theo skeleton (ví dụ hai
Plane đầu-cuối điều khiển Length). Nhánh này vẫn bị chặn cho Generic Annotation
và các template khác. Compiler tạo `NewDetailCurve`, dùng endpoint reference +
`NewAlignment`, kiểm Plane 0,01 mm, flex theo Linear/EQ Dimension và mở lại RFA
kiểm Detail Curve geometry. Exact R23 fixture đã PASS 300/600/900 mm và reopen,
giữ Left/Right planes, `Flexible Length` label và Detail Line. Artifact
`DSConsDetailLineRuntimeFixture_R2023_20260926A.rfa` có 385024 B, SHA-256
`6B96361B01CA4429DB2E530521536357C656B5DBB003257763CCFEDE9C25F69A`.

Plane-to-solid alignment cũng đã PASS exact R23 Preview, flex, geometry-bounds
read-back, Apply no-overwrite và reopen sau cùng sửa `thirdPnt`. Artifact
`DSConsPlaneSolidAlignmentRuntimeFixture_R2023_20260926A.rfa` có 434176 B,
SHA-256 `3F2DE2F60A24C08F96F7DEBD31562247498493BADB8B016662A3952C350AB866`.
Riêng Sweep bounded
theo `reference_line` tạo path trực tiếp từ driven Reference Line nên min/nominal/
max đã kiểm chính đường Sweep xoay thật, không chỉ Dimension/Label. Constraint tới
nested instance, graph vòng hoặc phụ thuộc tổng quát, profile tham số hay connector
xoay theo Reference Line vẫn là gate mở; mọi kết quả của wave này còn cần runtime
Family Editor trên template thật.

Với Sweep cung, compiler tạo radial dimension cho bán kính và hai ràng buộc nội
bộ tới tâm cung để giữ giao điểm hai tiếp tuyến tại origin khi flex. Ca
min/nominal/max đọc riêng bounds của Sweep, origin connector và giao điểm tiếp
tuyến với dung sai 0,5 mm; bước reopen kiểm lại path, nhãn bán kính, connector
origin/normal. Các kiểm tra này vẫn cần chạy thật trong Revit trước khi được xem
là runtime evidence, vì constraint có thể over-constrained trên template thực.

Linear `arrays` nhận đúng một nhóm member: part độc lập hoặc nested component
thường. Part không được thuộc join/cut, làm host cho connector hoặc đồng thời bị
alignment. Nested array member phải là child Blueprint level-based đã xác thực và không được
dùng Family Type Parameter hoán đổi. Số lượng có thể cố định hoặc liên kết
Integer Type Parameter; trường hợp tham số được flex và đọc lại `NumMembers`
riêng. Một part/nested component không được thuộc nhiều array và nested member
trong array không được mirror ở compiler bounded hiện tại.

`nested_components` không nhận đường dẫn RFA do người dùng cung cấp. Child phải
được tạo và Apply bằng Family Blueprint v3 trước; Node lưu
`family_build_artifact_v3` gồm spec, checksum và blueprint hash. Parent chỉ load
artifact còn nguyên vẹn trong cùng approved demo directory, kiểm trạng thái
Shared/Embedded khớp contract. Level child hỗ trợ điểm đặt/xoay X/Y/Z; WorkPlane-
Based child hỗ trợ point + reference direction trên planar face; CurveBased child
hỗ trợ line trên planar face. Cả ba đường có thể map Instance Parameter của child
sang parameter cùng kiểu dữ liệu của parent. `mirrors` nhận plane origin/
normal tường minh và chỉ thao tác part độc lập hoặc nested component đã khai
báo; target liên quan join/cut, connector, alignment hoặc array bị chặn. Hosted
nested bị chặn khỏi array/mirror/type swap/free rotation; mirror phụ thuộc
constraint vẫn cần capability riêng. Family Type
interchangeable child và linear nested array đã có source/compile nhưng vẫn cần
runtime Revit; hai khả năng này chưa được phép kết hợp với nhau.

Ca flex dùng đơn vị khai báo của Blueprint: length là mm, angle là độ, number
giữ nguyên và integer phải là số nguyên. Ví dụ tối thiểu:

```json
{
  "verification": {
    "parameter_flex_cases": [
      { "parameter_key": "width", "min": 300, "nominal": 600, "max": 900 }
    ]
  }
}
```

Formula/lookup parameter không được set trực tiếp trong ca flex. Profile tham số
được nhận cho Extrusion; Revolution rectangle/circle/ring/oval; Sweep line/
polyline rectangle/circle/ring/oval với đoạn đầu +X; Sweep cung rectangle/circle/
oval; Blend thẳng X và Swept Blend một đoạn +X rectangle/circle/oval. Mọi tổ hợp
ngoài phạm vi này vẫn fail-closed cho tới khi có constraint graph và read-back.

Toàn bộ khả năng bổ sung ở trên đã compile trên API 2019–2027 nhưng chưa
có runtime Family Editor record. Vì vậy tài liệu không gắn nhãn LOD300/350,
connector network hay placement certification cho các phép dựng này.
