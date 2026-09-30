# Runtime verification — copied models only

Runtime support được ghi riêng theo từng target Revit 2019–2027. Revit 2023 và
2025 đã pass copied-model matrix; Revit 2027 chỉ là POC cho đến khi hoàn tất
kiểm tra trên model copy. Compile thành công không thay thế runtime verification.

## V2 runtime follow-up — 24/08/2026

Sau lần chạy V2 trên project thử riêng, Change Set đã preview bằng
`TransactionGroup` rollback và apply Pipe thành công với
`post_commit_read_back`. Hai lỗi runtime tiếp theo đã được sửa và build lại:

- Template trống có thể thiếu elbow trong Routing Preferences, vì vậy harness
  V2 dùng một Pipe thẳng để kiểm chứng Change Set thay vì vô tình kiểm tra
  library fitting của template.
- `mep_network_explore` giữ topology của logical connector nhưng trả geometry,
  connection state và refs không khả dụng là `null`/rỗng. Các property đó được
  bảo vệ bằng đúng `Autodesk.Revit.Exceptions.InvalidOperationException`.
- CoreRuntime không còn đóng gói `Newtonsoft.Json.dll`. Nó tham chiếu assembly
  Revit đã nạp sẵn (v12 ở Revit 2023, v13 ở Revit 2025); hot publish cũng xóa
  DLL Newtonsoft cũ. Điều này loại bỏ lỗi startup binding `13.0.0.0` vs
  `12.0.0.0` trong journal Revit 2023.

Build 2023/2025, V2 contract check, contracts và protocol smoke đều PASS sau
thay đổi này. Xác minh live cuối cùng cho package mới phải do người dùng mở
Revit/project test để tránh automation UI can thiệp vào phiên Revit.

### Manual hand-off (one-time)

1. Mở **Revit 2023** bình thường và mở một file `.rvt` copy chuyên cho test
   (ví dụ `FILE TEST - DSCons V2 Test 20260824.rvt`), không mở `FILE TEST.rvt`.
2. Nếu Revit hỏi trust add-in, chọn **Load Once** hoặc **Always Load**. Nếu
   Revit đã mở trước lúc publish, vào **Add-Ins → DSCons MCP → Reload Core**.
3. Khi view của project đã sẵn sàng, báo lại `đã mở Revit 2023 + file test`.
   Agent sẽ chỉ chạy `python tests/runtime_verify_v2.py --apply-writes` qua
   bridge; không thao tác chuột/phím trong Revit.

Lặp lại cùng thao tác cho Revit 2025 sau khi Revit 2023 matrix PASS.

### Revit 2023 final result

PASS on the dedicated copy `FILE TEST - DSCons Reliability Test 20260824.rvt`
(the original `FILE TEST.rvt` was not opened or modified):

- 28-tool discovery and the full V2 read-only pass: catalog, existing-network
  topology including logical connectors, coordinate triage, link inventory,
  quantity takeoff and documentation-plan validation.
- Atomic `bim_changeset_preview` returned
  `validation_level = revit_transactiongroup_rollback` and passed.
- `bim_changeset_apply` created Pipe `2076337` and returned post-commit
  read-back; subsequent `mep_network_explore` and quantity takeoff included
  the committed route (MEP count moved from 87 to 88).
- `documentation_apply` created Floor Plan `2076353`, then Sheet `2076379`
  and Viewport `2076385`, using explicit existing types. No Save, Sync,
  publish, print or export operation was issued.

### Revit 2025 final result

PASS on the dedicated copy `FILE TEST - DSCons Reliability Test R2025
20260824.rvt` (the original `FILE TEST.rvt` was not opened or modified):

- The full V2 read-only matrix passed, including existing-network exploration,
  coordination evidence, quantity takeoff (87 relevant elements) and
  documentation-plan validation.
- Atomic preview passed at `revit_transactiongroup_rollback`; apply created
  Pipe `2081376` and returned post-commit read-back. Network and takeoff
  included the committed route (count moved from 87 to 88).
- Controlled documentation created Floor Plan `2081390`, Sheet `2081416` and
  Viewport `2081422`; no Save, Sync, publish, print or export was issued.

The V2 copied-model runtime matrix is complete for Revit 2023 and 2025.

## Kết quả kiểm tra runtime ngày 14/08/2026

- PASS Revit 2023 đang chạy với DSCons session hợp lệ, bridge loopback mở ở cổng 43827.
- PASS `system_status`, `get_capabilities`, `document_info`, `get_active_view` và `get_selection`.
- PASS MCP stdio thực tế (trước đợt reliability enhancement): `initialize`, `tools/list` nhận 16 tool và `tools/call`.
- PASS `mep_filter_elements` và `mep_element_detail` trên model `FILE TEST`.
- PASS `mep_connector_network` với connector Round, Rectangular và Invalid sau khi đọc kích thước theo đúng hình học.
- PASS trên bản sao `FILE TEST - DSCons Runtime Test`: tạo Pipe, Duct và Cable Tray hai đoạn vuông góc với fitting; Conduit type không hợp lệ bị từ chối/rollback không treo Revit.
- PASS tạo route Pipe/Duct/Cable Tray sau khi thêm `IFailuresPreprocessor`; warning/failure fitting không còn mở modal làm MCP timeout.
- PASS `mep_move_route` sau khi sửa: fitting đang nối được tự đưa vào danh sách di chuyển; connector sau dịch chuyển vẫn khớp.
- PASS Preview chặn route chéo, token giả, token hết hạn và Preview bị stale sau khi target thay đổi.
- PASS route Type không hợp lệ bị Revit từ chối và TransactionGroup rollback.
- PASS audit JSONL sau khi bổ sung element IDs, document path, readOnly, centralDirect và rolledBack.
- NOT VERIFIED Read-only thật của Revit: đặt thuộc tính Read-only cho file Windows không làm `Document.IsReadOnly=true`; cần mở bằng tùy chọn Read-only hoặc một phiên đang khóa file.
- NOT VERIFIED Local/Central, Central mở trực tiếp, ownership, Pin và Group vì `FILE TEST` là model độc lập không workshared và không có target Pin/Group phù hợp trong ma trận hiện tại.
- NOT VERIFIED nút `Reload Core` bằng UI ở lượt này vì Computer Use không kết nối; các bản runtime đã được build/publish và xác nhận bằng restart sạch Revit.
- Revit 2025 runtime smoke bị BLOCKED bởi TaskDialog của add-in ngoài phạm vi: `DSCons MEP Tools` trỏ tới DLL không tồn tại trong workspace 2025; không phải lỗi DSCons Revit MCP.
- PENDING after reliability enhancement: validate 17-tool discovery, `mep_qa_connectivity`, transaction-rollback preview validation, post-commit read-back and capability scope/context metadata on a copied model.

- [x] Offline MCP `initialize`, notification handling, `ping`, `tools/list`.
- [x] Contracts test, solution build và packaging build.
- [x] Offline route planner rejects diagonal/redundant segments; disconnect selects an actually connected connector pair.
- [x] Mở Revit với add-in DSCons, kiểm tra `session.json` và `system_status` trên Revit 2023.
- [x] Đọc document, active view, selection, MEP element, system, level và connector trên model copy.
- [x] Tạo Pipe, Duct, Cable Tray với route hợp lệ và fitting hợp lệ; Conduit đã kiểm tra nhánh type không hợp lệ và rollback. Conduit type hợp lệ chưa có trong model test.
- [x] Route chéo và Type không hợp lệ bị từ chối/rollback; fitting thiếu/incompatible riêng chưa có mẫu ổn định.
- [x] Connect/disconnect, move route có fitting liên quan, đổi Type/size trên model copy.
- [x] Preview expiry, preview reuse, document/type/connector fingerprint mismatch.
- [ ] Model độc lập đã đạt; Local của Central, Central trực tiếp và Read-only thật chưa được kiểm tra.
- [x] Audit JSONL; Ownership conflict, Pin và Group chưa được kiểm tra runtime.
- [ ] Build CoreRuntime, copy runtime DLL và bấm `Reload Core` không restart Revit.

Chạy smoke test read-only sau khi add-in đã được cài vào Revit:

```powershell
.\scripts\preflight-mcp.ps1 -RevitVersion 2023
python tests/runtime_verify_mep.py
```

## Reliability enhancement matrix (copied model only)

Run these checks only after the updated artifact has been installed and Revit
has been restarted or the Core Runtime has been reloaded. The read-only harness
now checks `mep_qa_connectivity`; the remaining items are manual write checks
on a copied model.

Preview simulation may legitimately take up to 120 seconds on models with many
registered updaters or posted warnings because Revit commits a temporary
transaction and then rolls the enclosing TransactionGroup back. Do not treat a
35-second client timeout as a model-validation result.

1. Call `get_capabilities`; verify every write tool reports `scope`,
   `requiresFreshContext`, prerequisites and safety limitations.
2. Call `mep_qa_connectivity` with a small `limit`; verify it returns
   read-only review findings and no document modification.
3. Call `mep_preview` for one valid route. Verify
   `validation_level = revit_transaction_rollback`, simulated validation is
   passed and no persistent element is created before apply.
4. Call the same write tool directly on a copied model. Verify its response
   includes both `preview_validation` and
   `verification.mode = post_commit_read_back`.
5. Force an incompatible fitting/type case. Verify preview rejects it and the
   copied model remains unchanged.

Nếu `session.json` chưa xuất hiện, kiểm tra journal của phiên Revit hiện tại. Manifest mới chỉ được Revit đọc khi khởi động; hãy đóng toàn bộ Revit sau khi lưu công việc, mở lại đúng version đã cài, rồi chạy smoke test. Không chạy migration/install khi Revit còn mở.

Lưu ý: manifest canonical đã được chuẩn hóa theo thứ tự field mà Revit 2023/2025 đang dùng ở các add-in hoạt động. Nếu bạn đang dùng phiên Revit đã mở trước khi build mới, cần đóng/mở lại sau khi cài artifact mới.

The harness also verifies that a deliberately invalid session secret is rejected. It performs no model mutation.

## Computer Use evaluation matrix

Computer Use không thay Revit API/MCP. Chỉ chạy trên Family/test Project copy
đã được người dùng cho phép và theo vòng `document_info` + view/selection
anchor → screenshot nguyên độ phân giải → một hành động whitelist → screenshot
→ Revit read-back độc lập. Không click security dialog, không Save/Sync và
không dùng trên project sản xuất.

Ghi mỗi run theo schema của `revit_computer_use_assess`, bao gồm Revit version,
DPI, resolution, monitor/pane mode, fingerprint trước/sau, dialog/security,
cancel và verification. Ma trận tối thiểu phủ 2023/2025, DPI 100/125/150%,
1920×1080, single/multi-monitor, docked/floating, dialog che khuất, security
boundary và cancel. Workflow chỉ đủ điều kiện product review sau 20 run an toàn
liên tiếp và matrix complete; còn lại giữ `operator_assisted_only`.

## Family native Identity Data, Schedule và Tag (copied Family/Project only)

Mục tiêu là kiểm chứng field chuẩn Revit theo Type, không phải chỉ đọc JSON
evidence từ RFA. Dùng một Family copy có ít nhất hai Type, với các giá trị khác
nhau cho `manufacturer`, `model`, `description`, `url`, `type_comments`,
`classification_number` và `classification_title`.

1. Trong Family copy, chạy `family_inspect` và đối chiếu từng Type/field với
   Blueprint đã được xác nhận; mở lại Family và lặp lại read-back. Một field
   native thiếu, read-only hay sai Type là FAIL, không thay bằng Shared/Family
   Parameter cùng tên.
2. Chỉ trong Project copy được phép test, load Family và đặt tối thiểu hai Type
   tại vùng trống. Lập Schedule đúng category, sử dụng các field Identity Data
   mà Revit thực sự liệt kê; thay đổi Type để kiểm giá trị dòng Schedule đổi
   theo Type. Không đoán tên/cột field và không Save/Sync.
3. Nếu category có Tag hợp lệ, dùng Tag đã được author qua controlled Family
   Editor UI và kiểm nội dung Tag đổi theo chính field native/Type. Không coi
   TextElement hard-code là Tag động; nếu Label API còn thiếu thì ghi kết quả là
   `UI fallback required`, không tự khẳng định PASS.
4. Ghi Revit version, template/category, tên Type, field hiển thị thực, ID
   element/Schedule/Tag, screenshot/record read-back và trạng thái rollback hoặc
   cleanup trên **Project copy**. Lặp fixture PASS ở Revit 2025 sau Revit 2023.

Pass ở đây chỉ chứng minh exposure trong fixture đã kiểm. Nó không xác nhận
catalogue của hãng, mapping của mọi category/template, tiêu chuẩn schedule dự án
hay LOD300/350.

## Family Routing Preference probe (copied Project only)

This probe is the acceptance gate for one newly built **Pipe Fitting** or
**Duct Fitting** type. It is not the normal load/place workflow and never
persists a loaded RFA or a changed Routing Preference.

1. Open a dedicated local Project copy, call `document_info` and
   `get_active_view` in the same turn, then obtain existing Pipe/Duct Type,
   system type and Level IDs from the current Project.
2. Select an empty 2 m x 2 m area and call
   `family_routing_probe_preview` with the exact active project path/view,
   `copied_project_confirmed: true`, an RFA that is not already loaded, the
   intended Family Type and matching `curve_kind`.
3. PASS only if the response says `validation_level =
   project_transactiongroup_rollback`, `routing_preference.verified = true`
   and `network.verified = true`; confirm its selected symbol is the requested
   Family Type and `rollback_state_read_back.verified = true`. The test must
   not leave any new Family/rule/curve/fitting in the copied Project.
4. Record the Revit version, source RFA SHA-256, selected MEP type/system,
   profile/size, Part Type and full read-back. Run 2023 first and repeat on
   2025. A failed transaction, an unexpected symbol, incomplete connector
   network or a same-name loaded Family is a failure, not a warning to ignore.

The probe covers only routing selection and the physically connected temporary
network for elbow, tee/wye/lateral tee, cross/lateral cross, transition and
union. `Breaks Into`/`Valve Breaks Into` placement, system calculation,
pressure-loss result, type change, rotate/mirror and LOD350 coordination each
remain separate runtime gates.

## Family Break Into / Valve Breaks Into controlled-UI acceptance (copied Project only)

This is deliberately not an MCP API placement test. Revit's native inline
placement is performed by an approved UI operator; MCP provides a bounded
preflight and a read-only topology check so a decorative two-port RFA cannot be
reported as a connected inline component.

1. In a dedicated local Project copy, call `document_info` and
   `get_active_view` in the same turn. Keep the returned view active. Record
   the Project path, RFA SHA-256, intended Family Type and whether the test is
   Pipe or Duct.
2. Call `family_break_into_ui_preflight` with that context,
   `copied_project_confirmed: true`, the exact RFA/Type and `curve_kind`. PASS
   requires a fresh `preflight_id`, `read_only = true`, an accepted
   `breaks_into` or `valve_breaks_into` Part Type, and two aligned opposing
   connectors with matching profile/size.
3. Before the record expires, use only Revit's native Break Into workflow to
   place that exact Type on one straight matching segment. Do not edit the RFA,
   change its Type, change curve/system type, Save or Sync.
4. Obtain the created FamilyInstance ID and the IDs of the two resulting
   Pipe/Duct segments. Call `family_break_into_ui_verify` with the
   `preflight_id`, `accessory_instance_id` and exactly two `segment_ids`.
   PASS requires `post_controlled_ui_read_back`, matching Family/Type/Part Type,
   one-to-one physical connector references to both segments, profile/size
   compatibility and one shared system type.
5. Record Revit version, Family/RFA hash, profile/size, system type, all IDs
   and full response. Repeat a passed 2023 fixture in Revit 2025. A failed
   preflight or verification is a failed fixture; do not bypass it by manually
   connecting extra components.

The API cannot observe which UI command produced the result. The procedure
therefore proves resulting connector topology only, not native-command identity,
pressure loss/system calculation, route healing after removal, type change,
rotate/mirror, hosting or LOD350 coordination.

## Family advanced Duct/Pipe connector acceptance (copied Project only)

This is a separate acceptance gate for connector values and parameter
associations. It must not be inferred from a successful RFA build, a connector
count, or the fitting Routing Preference probe.

1. In a dedicated local Project copy, re-anchor with `document_info` and
   `get_active_view`. Record the exact Revit version, Project copy path, RFA
   SHA-256, Family/Type, system type, Level, profile and nominal size.
2. Open each newly built Family in the Family Editor and call `family_inspect`.
   Revit 2023 Duct `flow_parameter` is currently API fail-closed: a controlled
   Family Editor UI operator must make the association, then reopen/inspect it;
   a direct API build must not be reported as PASS. For a Pipe connector with
   `flow_configuration=preset`, PASS requires
   the `flow` association and declared L/s. Exercise each approved min, nominal
   and max Type value and record the individual connector read-back.
3. For a Duct/Pipe connector with `flow_configuration=system`, PASS requires
   the expected `flow_factor_parameter` association and a value in 0–1 for
   every approved Type. A Pipe `allow_slope_adjustments` case must have system
   classification `Global` and read back the declared slope flag. It is a
   failure if the compiler silently accepts a non-Global or non-Pipe case.
4. In the copied Project, load/place the exact RFA only through the approved
   test workflow, create matching 1 m stubs and read connector topology with
   `mep_connector_network`. Where the fixture has matching Routing Preference,
   run the routing probe separately. No Save or Sync is permitted.
5. Record the complete Family and Project read-back, including any Revit error
   or warning. Repeat a passed Revit 2023 fixture on Revit 2025; do not extend
   its result to other versions or types.

This procedure verifies the Family-side association and bounded Project
topology only. Routing Preference selection, system-flow calculation,
pressure-loss results, slope propagation, network propagation and engineering
calculation remain separate gates until each has direct copied-Project evidence.

## Family hosted / two-level controlled-UI acceptance (copied Project only)

This is a native Revit placement test. `family_hosting_ui_preflight` and
`family_hosting_ui_verify` never place, rehost or cut by API; they bind the
manual action to one RFA/Type/Project/view/host record and read the result back.

1. In a dedicated local Project copy, call `document_info` and `get_active_view`
   in the same turn. Keep that view active and identify exactly one RFA/Type.
   For wall, ceiling, floor or roof, record exactly one physical host element;
   for two-level placement record two ordered Level IDs.
2. Call `family_hosting_ui_preflight` with `copied_project_confirmed: true`,
   approved RFA path, Type, mode and target. It must return `read_only = true`,
   a new `preflight_id` and `OneLevelBasedHosted` or `WorkPlaneBased`
   (face-based physical host), or `TwoLevelsBased` (two levels). If void cutting is in scope, set
   `require_void_cut: true`; it must additionally report a cut-eligible target,
   `Cut with Voids When Loaded`, and at least one native void form.
3. Before expiry, use Revit's native UI to place exactly that Type on the
   selected host or between the selected levels. Do not edit the RFA, change
   Type/host/level, Save or Sync. For the void case, create the intended cut in
   the same controlled placement.
4. Call `family_hosting_ui_verify` with the one-use `preflight_id` and resulting
   `instance_id`. PASS requires exact Family/Type/placement type, the exact
   host element and category (plus a `HostFace` on that same host for a
   `WorkPlaneBased` Family), or the exact `FAMILY_BASE_LEVEL_PARAM` and
   `FAMILY_TOP_LEVEL_PARAM`. A void-cut test additionally requires the host in
   `InstanceVoidCutUtils.GetElementsBeingCut`.
5. Record Revit version, RFA SHA-256, Family/Type, Project/view/host or Level
   IDs, whether void cut was required, and the full read-back. Run a passed
   Revit 2023 fixture again in Revit 2025 without claiming it covers other
   years.

The result verifies current host/level and optional cut relation only. It does
not prove native-command identity, rehost history, type change, rotate/mirror,
cut persistence after editing, or LOD350/BEP coordination.

## Nested versus monolithic Family-library benchmark (copied Project only)

`family_library_benchmark_preview` is a generic measurement harness, not a
Family optimiser. It is available only for two **different**, declared-equivalent
unhosted `OneLevelBased` RFAs. It fails closed when their Family Category or
`FamilyPlacementType` differs, a Family is already loaded, the exact Type is
absent, its declared flex parameter is not one writable non-formula Type Length
parameter, a min/nominal/max transition does not move `GenericForm` bounds, or
the two case-value sets differ. Matching these minimum checks does not prove
equal geometry, connector behavior, source data, LOD, or manufacturer scope.

1. Prepare two staged/no-overwrite RFAs in the approved demo directory: one
   monolithic and one nested implementation of the same declared device scope.
   Keep their internal Family names different. Do not compare a parent-only
   fixture to an unrelated monolithic Family.
2. In a dedicated local Project copy, call `document_info`, `get_active_view`
   and `get_selection` in the same turn. Record exact Project path, view ID,
   Level and an origin far from model content. The benchmark does not Save or
   Sync, but temporary load/place still requires that copied Project scope.
3. Call `family_library_benchmark_preview` with the exact two RFA/type entries,
   matching `min`, `nominal`, `max` values for each declared Type Length
   parameter, `equivalent_function_confirmed: true`,
   `isolated_origin_confirmed: true`, bounded `instance_count` (1–100) and
   spacing (100–1,000,000 mm).
4. PASS requires two no-save Family-document flex sequences with parameter and
   geometry read-back, then temporary `LoadFamily`, exact-Type placement of the
   requested count and one `Document.Regenerate` in a copied-Project
   `TransactionGroup` that is always rolled back. Record RFA byte/hash and all
   timings, plus the comparability boundary, for this exact Revit version.
5. Read `document_info` again after the call. It must remain `modified=false`.
   Repeat only an accepted Revit 2023 pair in Revit 2025; do not claim any result
   for a different pair, year, instance count or host behavior.

The harness reports observed timing and file-size values only. It must not be
used to claim that nested Families are universally smaller/faster, or to replace
Dynamic Tag, Schedule, routing, hosting, material-visual or LOD acceptance.

## Source-to-Revit certification order

Chỉ chạy sau offline regression và build loader tương ứng:

1. Revit 2023: CSV/TSV catalogue hãng numeric → `manufacturer_catalog_inspect`
   → `mepf_evidence_readiness(mapping=family_blueprint)` → FamilySpec có
   `catalogue_provenance` → rollback Family preview. Kiểm checksum/revision,
   từng row min/nominal/max qua `size_lookup`, lookup-table inventory và sau
   Apply thì staging/no-overwrite RFA + reopen. Fixture này có thể dùng Generic
   Model để chứng minh dữ liệu; nó **không** chứng nhận Pipe Fitting, connector
   hoặc Routing Preference.
2. Revit 2023: PDF catalog nhiều Type → `family_spec_preview` → rollback Family
   preview → staging/no-overwrite Apply → reopen/hash/flex.
3. Revit 2023: DXF Profile/Symbolic/Model/Detail Blueprint với cùng chuỗi
   Preview/Apply/reopen; geometry không map được phải còn trong findings.
4. Revit 2023 copied Project: CAD centerline route rollback Preview. Persistent
   Apply chỉ dùng fixture test đã xác nhận; bắt buộc connector/network
   post-commit read-back, không fitting/slope/reroute ngầm và không Save/Sync.
5. Revit 2023: multi-view image Blueprint; connector phải rỗng, citation đủ
   mọi ảnh, flex min/nominal/max rồi reopen/hash.
6. Chỉ sau khi từng fixture 2023 PASS mới lặp nguyên input và assertion trên
   Revit 2025. Compile success không phải runtime PASS.

Computer Use dùng đúng harness contract của `revit_computer_use_assess`. Một
workflow chưa có 20 run liên tiếp và đủ ma trận Revit/DPI/monitor/pane/dialog/
cancel phải giữ `operator_assisted_only`; không tạo evidence giả để đủ matrix.

## V2 BIM production matrix (copied model only)

```powershell
python tests/runtime_verify_v2.py
# Only after confirming that the active file is a dedicated copy:
python tests/runtime_verify_v2.py --apply-writes
```

The read-only pass verifies model catalog, network topology, loaded-link coordination triage, quantity takeoff and documentation-plan validation. The write pass creates a Pipe batch through an atomically previewed Change Set, then creates a Floor Plan and, when the test model contains a title block, a sheet plus viewport. Every write must return `post_commit_read_back`.

The coordination scan uses transformed bounding-box evidence and deliberately reports review findings; it is not a solid-geometry certificate and never reroutes automatically. Quantity is current-model evidence in mm/m2/m3; project measurement, waste and procurement rules remain engineer-controlled.

## Reliability enhancement runtime result — 24/08/2026

The reliability-enhanced artifact passed the full copied-model matrix on both
Revit 2023 and Revit 2025. The models used were dedicated copies under
`C:\\Users\\Admin\\Desktop`; the original `FILE TEST.rvt` was not used for
write validation.

- PASS (2023 and 2025): 17-tool discovery; `system_status`, capability
  metadata, document/read operations, `mep_qa_connectivity`, and invalid
  session-secret rejection.
- PASS (2023 and 2025): valid `mep_preview` executed at Revit level and
  returned `validation_level = revit_transaction_rollback`; no model change
  persisted from preview.
- PASS (2023 and 2025): `mep_apply_preview` created the planned elements and
  returned verified `post_commit_read_back` data.
- PASS (2023 and 2025): direct `mep_create_route` first completed the rollback
  preview, then applied the route and returned verified read-back data.
- PASS (2023 and 2025): diagonal route request was rejected as `InvalidParam`;
  an invalid type was also rejected in 2023 without persisting a change.

The first rollback simulation in a warning-heavy Revit 2023 test copy showed
that the previous 35-second transport timeout was too short. The transport and
contract timeout are now 120 seconds. This is intentional: Revit may need to
commit the inner trial transaction before rolling back its enclosing group.

Revit 2025 also displayed an unrelated missing-DLL dialog from the separate
`DSCons MEP Tools` add-in during startup. It was dismissed; DSCons Revit MCP
loaded, created its session and completed this matrix. The dialog is not a
failure of this independent MCP project.

## Revit 2027 POC hand-off

Build and stage the 2027 artifact with:

```powershell
.\scripts\build-mcp.ps1 -RevitVersion 2027 -Configuration Release
```

After explicit approval, close every Revit process and install only the matching
artifact with `scripts/install-mcp.ps1 -RevitVersion 2027 -ConfirmInstall`.
Open a dedicated copied model in Revit 2027 and verify `system_status`,
`document_info`, `get_active_view`, `get_selection`, `get_capabilities`,
read-only MEP inspection, then one preview/apply write with rollback and
`post_commit_read_back`. Until that hand-off is complete, 2027 remains POC and
must not be described as runtime-certified.
