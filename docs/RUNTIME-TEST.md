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
