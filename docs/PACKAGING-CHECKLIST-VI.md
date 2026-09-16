# Checklist đóng gói DSCons Revit MCP cho buổi cài đặt và pilot 60 phút

DSCons Revit MCP là sản phẩm độc lập, không thuộc một chương trình hay khóa học
nào. Trong chương trình phễu, bộ kit được hướng dẫn trong **một buổi cài đặt,
kiểm tra kết nối và pilot MCP 60 phút**. Tài liệu này dành cho người phụ trách đóng gói/bàn giao;
đánh dấu từng ô, lưu bằng chứng ở mỗi cổng kiểm tra và xử lý nguyên nhân trước
khi kết luận chưa đạt.

## 0. Thông tin đợt phát hành

- [x] Ngày phát hành: `22/09/2026`
- [x] Người phụ trách: `Phạm Quang Huy`
- [x] Mã release: `personal-v1-20260922` *(đề xuất)*
- [x] Thư mục output: `artifacts/student-release-20260922-r7`
- [x] Người duyệt Fire Keeper: `Phạm Quang Huy`
- [x] Phạm vi bàn giao: `Cài đặt/kiểm tra và pilot MCP 60 phút trong chương trình phễu`
- [x] Tư cách sản phẩm: `MCP độc lập; không thuộc chương trình nào`
- [x] Client: `Codex + ClaudeCode + Antigravity`
- [x] Revit target: `2019–2027 (phạm vi kiểm kê; claim theo runtime evidence từng phiên bản)`
- [x] Đã ghi release ID, ngày và người phụ trách vào biên bản
  `runtime/release-preflight-20260915.md`.

## 1. Quyết định sản phẩm phải giữ nguyên

- [x] Kiến trúc chỉ có một MCP Server: `MCP-Server/` → `MCP/` →
  `MCP.CoreRuntime/`.
- [x] `MCP.CoreRuntime/` chỉ là hot-reload nội bộ của add-in, không đăng ký như
  MCP server thứ hai.
- [x] Baseline host-facing chính thức là **46 tool**.
- [x] Không coi build PASS là runtime PASS.
- [x] Việc dùng Revit Central, model khách hàng, file read-only hoặc project
  chưa được phép làm fixture dạy học tùy lựa chọn khách hàng, nhưng phải hỏi và
  xác nhận phạm vi an toàn trước khi thực hiện.
- [x] Không Save/Sync, publish, print hoặc export khi chưa hỏi và nhận xác nhận
  riêng trước khi thực hiện.
- [x] Fire Keeper đã chọn [license học tập phi thương mại](../LICENSE): cho phép
  cá nhân tải/chạy/sửa để học; cấm bán, dịch vụ trả phí và phân phối lại khi
  chưa có chấp thuận bằng văn bản.

## 2. Chuẩn bị source và máy đóng gói

- [x] Đọc [PROJECT.md](../PROJECT.md), [AGENTS.md](../AGENTS.md),
  `.agents/AGENTS.md`, `.agents/prd.md`, `.agents/implementation-plan.md`,
  `.agents/implementation-status.md`, `.agents/tasks.md` và
  `.agents/chat-history.md`.
- [x] Kiểm tra `node --version` và `npm --version`.
- [x] Kiểm tra `.NET SDK 10` bằng `dotnet --version`.
- [x] Kiểm tra PowerShell bằng `$PSVersionTable.PSVersion`.
- [x] Không yêu cầu học viên build C#; bộ kit phải có lệnh tự cài dependency cần
  thiết hoặc hướng dẫn cài đặt rõ ràng nếu không thể tự động.
- [x] Không đưa `.agents/`, `inputs/`, model, runtime/audit/session data,
  catalog PDF, RFA tham khảo, `node_modules` hoặc Revit DLL vào public export.
- [x] Kiểm tra [COMPATIBILITY.md](COMPATIBILITY.md) trước khi ghi claim phiên bản.

### Quét dữ liệu không được phát hành

- [x] Không có file tạm `.stage`, `.tmp`, `.bak` trong artifact — quét release
  `student-release-20260922-r7` PASS.
- [x] Không có secret/API key/private key/token thực tế trong artifact — quét
  PEM/secret và các file do DSCons sở hữu PASS; chuỗi mẫu trong mã dependency
  `jose` không phải private key.
- [x] Không có đường dẫn máy cá nhân trong artifact dành cho học viên — quét
  path tuyệt đối PASS.
- [x] Không có `RevitAPI.dll` hoặc `RevitAPIUI.dll` trong release/export — PASS.

## 3. Kiểm tra nội dung learner kit

- [x] [START_HERE.md](../START_HERE.md) là điểm bắt đầu duy nhất.
- [x] `START_HERE.md` có đúng một prompt mở đầu.
- [x] Prompt yêu cầu Agent đọc source-of-truth, hỏi Revit version/client và
  chạy Check read-only trước; mỗi câu hỏi có ví dụ trả lời.
- [x] Prompt yêu cầu hỏi trước khi dùng model khách hàng/Central hoặc thực hiện
  thao tác ảnh hưởng dữ liệu; không tự ý Save/Sync, build C# hoặc cài package.
- [x] [LEARNER_GUIDE.md](learning/LEARNER_GUIDE.md) tách rõ phần cài đặt/kiểm tra
  và phần pilot runtime trên project copy đã được phép.
- [x] [learning/README.md](learning/README.md) trỏ tới learner guide, pilot 60 phút,
  quy tắc tự chọn Family template và templates; không yêu cầu chọn giáo trình/track.
- [x] Prompt có các điểm dừng: Check chỉ-đọc → giải thích → xác nhận Install /
  Configure → người dùng tự mở Revit → smoke chỉ-đọc.
- [x] Hướng dẫn giải thích thuật ngữ kỹ thuật bằng ngôn ngữ tự nhiên và luôn
  đưa ví dụ cho câu hỏi cần học viên trả lời.
- [x] Templates có MCP log và Bug Report cho buổi cài đặt; các mẫu kỹ thuật
  khác chỉ là tài liệu phụ, không phải bài bắt buộc.
- [x] [personal-quick-start-vi.md](personal-quick-start-vi.md) có rollback.

## 4. Kiểm thử offline

Chạy từ thư mục gốc project và lưu output vào biên bản release:

```powershell
npm --prefix .\MCP-Server run build
node .\tests\mcp_protocol_smoke.mjs
python .\tests\family_platform_static_contract.py
node .\tests\family_evidence_test.mjs
python .\tests\source_format_validation.py
python .\tests\ribbon_toggle_static_contract.py
.\scripts\test-contracts.ps1
```

- [x] Chạy đủ bộ kiểm thử trước khi phát hành; nếu có lỗi thì tìm nguyên nhân,
  sửa và chạy lại. Chỉ ghi `BLOCKED` khi lỗi chưa sửa được hoặc thiếu điều kiện
  bên ngoài.
- [x] Node/TypeScript build PASS — 2026-09-15.
- [x] Protocol smoke báo đúng `46 tools` — 2026-09-15.
- [x] Family static contract PASS — 2026-09-15.
- [x] Source format validation PASS cho JSON/XML/add-in/Markdown links — 2026-09-15.
- [x] Contract tests PASS — 2026-09-15.
- [x] Negative test input thiếu/sai bị chặn — static/contract regression PASS.
- [x] Negative test token stale/expired/reuse bị chặn — static/contract regression PASS.
- [x] Negative test document/path mismatch, Central/read-only và file conflict PASS
  — static/contract regression PASS.
- [x] Preview rollback không để lại element/file tạm — static/contract regression
  PASS; từng Revit workflow vẫn cần runtime evidence riêng.
- [x] Config preservation giữ nguyên MCP entry không liên quan — regression PASS.
- [x] JSON/TOML là UTF-8 không BOM — static regression PASS.

### Kiểm tra số lượng tool

- [x] Host-facing count = `46`.
- [x] Revit-side command count = `45`.
- [x] `dscons_knowledge_search` là tool Node-local thứ 46.
- [x] 28 tool cũ giữ nguyên tên/schema/hành vi.
- [x] Family/PDF/CAD/Combine tools hiện có vẫn trong registry.

## 5. Build artifact theo Revit

Chỉ dùng API binary đúng năm; không dùng DLL năm khác để giả lập.

- [x] Mục tiêu phát hành là nghiên cứu, build và kiểm tra đủ Revit 2019–2027;
  không chốt gói chỉ cho 2023/2025 rồi bỏ các năm còn lại.

### Revit 2023

```powershell
.\scripts\build-mcp.ps1 -RevitVersion 2023 -Configuration Release
```

- [x] Build PASS; artifact hiện có đầy đủ.
- [x] Có `artifacts\Revit2023\DSCons.RevitMcp.dll`.
- [x] Có `artifacts\Revit2023\DSCons.RevitMcp.Contracts.dll`.
- [x] Có `artifacts\Revit2023\runtime\DSCons.RevitMcp.CoreRuntime.dll`.
- [x] Có `artifacts\Revit2023\DSCons.RevitMcp.addin`.
- [x] Đã lưu SHA256 từng file trong biên bản kiểm tra ngày `15/09/2026`.

### Revit 2025

```powershell
.\scripts\build-mcp.ps1 -RevitVersion 2025 -Configuration Release
```

- [x] Build PASS; warning (nếu có) đã ghi trong biên bản.
- [x] Có đủ loader, contracts, CoreRuntime và manifest 2025.
- [x] Đã lưu SHA256 từng file trong biên bản kiểm tra ngày `15/09/2026`.

### Các năm còn lại

- [x] Mỗi năm 2019–2027 có hồ sơ compile/package riêng: đủ ba dependency API,
  đúng assembly major, TFM đúng năm, build và artifact PASS; runtime evidence
  vẫn theo từng năm và không được suy ra từ compile.
- [x] Không ghi “runtime hỗ trợ 2019–2027” chỉ vì build thành công; chỉ nâng
  claim sau khi có bằng chứng thực tế của từng năm.
- [x] Nếu thiếu API, lưu log thiếu API và tiếp tục nghiên cứu bundle đúng năm;
  tuyệt đối không thay bằng DLL năm khác.

## 6. Tạo student release

Luôn dùng thư mục output mới, không ghi đè release cũ:

```powershell
$out = Join-Path $env:TEMP 'dscons-student-release-YYYYMMDD'
.\scripts\build-student-release.ps1 -OutputRoot $out `
  -RevitVersions 2019,2020,2021,2022,2023,2024,2025,2026,2027 `
  -ApiBundleRoot 'C:\Approved\RevitApiBundles'
```

- [x] Student release nhắm đủ 9 phiên bản 2019–2027; phiên bản thiếu API đúng
  năm được bỏ qua có ghi rõ trong manifest, không làm hỏng cả gói.
- [x] Có artifact cho đủ chín phiên bản — requested = built = `2019–2027`,
  skipped rỗng; `revitBuilds` ghi TFM, trạng thái và version của ba dependency.
- [x] Có `MCP-Server\build\index.js`.
- [x] Có `package.json`, `package-lock.json` và production dependencies để học
  viên cài repo rồi dùng, không phải tự chạy `npm install`.
- [x] OCR `eng` + `vie` và manifest checksum được đồng bộ offline.
- [x] Có `student-setup.ps1`, preflight, install, uninstall và ba script client.
- [x] Có `START_HERE.md`, learner docs và templates.
- [x] Có `release-manifest.json` với path, bytes và SHA256 cho từng payload file.
- [x] Có `THIRD-PARTY-NOTICES.md`.
- [x] Không có model, catalog private, RFA tham khảo hoặc Revit DLL.
- [x] Không yêu cầu học viên build C# hoặc tự cài package; nếu một bản phát hành
  không thể đóng gói dependency thì phải có hướng dẫn cài đi kèm.

### Xác minh manifest

```powershell
$manifest = Get-Content -Raw (Join-Path $out 'release-manifest.json') | ConvertFrom-Json
$manifest.files.Count
Get-ChildItem -LiteralPath $out -Recurse -File | Measure-Object
```

- [x] Số file thực tế khớp hoặc đã ghi rõ lý do chênh lệch — manifest có 4.194
  payload files; thư mục có thêm chính `release-manifest.json` (được tạo sau
  bước enumerate nên không tự chứa checksum của nó).
- [x] Hash loader, contracts, CoreRuntime, Node entrypoint và add-in manifest khớp.
- [x] Không đổi file sau khi manifest tạo; nếu đổi phải build lại manifest.

## 7. `student-setup.ps1 -Mode Check`

`Check` chỉ đọc, không sửa add-in, client config, process Revit hoặc model:

```powershell
.\scripts\student-setup.ps1 -Mode Check -RevitVersion 2023 -Client Codex
.\scripts\student-setup.ps1 -Mode Check -RevitVersion 2023 -Client ClaudeCode
.\scripts\student-setup.ps1 -Mode Check -RevitVersion 2023 -Client Antigravity
```

- [x] Node, npm, production dependencies, đủ API/APIUI/Newtonsoft, server artifact
  và bốn artifact Revit được báo — PASS 9 năm × 3 client ngày `15/09/2026`.
- [x] Process Revit được báo rõ đang mở hay đã đóng.
- [x] Entrypoint client được kiểm tra.
- [x] JSON/TOML hợp lệ, không BOM, không stale path.
- [x] Check không tạo ownership manifest hoặc backup.
- [x] Check không sửa client config; checksum trước/sau của cả ba client không đổi.
- [x] Nếu thiếu Node.js/npm, báo hướng dẫn cài Node.js LTS rõ ràng.
- [x] Sau xác nhận Install, nếu thiếu dependency hoặc Node build thì bộ cài tự
  chạy cài dependency, tạo Node build và chỉ giữ production dependencies; không
  build C#.
- [x] Output tiếng Việt rõ ràng để lưu transcript evidence.

## 8. Install có kiểm soát

Chỉ chạy khi đã có xác nhận riêng và đóng toàn bộ Revit:

- [x] Quy trình cài đặt có kiểm soát đã được người phụ trách duyệt: phải Check,
  đóng Revit, chọn đúng version/client, backup, giữ MCP khác và kiểm tra lại sau
  Install.

### Biên bản lần cài thực tế

- [ ] Đã lưu kết quả Check của lần cài thực tế.
- [ ] Revit đã đóng hoàn toàn tại thời điểm cài.
- [ ] Đã chọn đúng version và client cho lần cài.
- [ ] Không có ownership manifest cũ chưa xử lý.
- [ ] Có quyền ghi backup dưới `%LOCALAPPDATA%\DSCons\RevitMcp\student-setup\`.
- [x] Script bắt buộc dùng đồng thời hai cờ:

```powershell
.\scripts\student-setup.ps1 -Mode Install `
  -RevitVersion 2023 -Client Codex `
  -ConfirmInstall -ConfirmConfigure
```

- [x] Script backup manifest add-in, thư mục add-in và client config trước khi
  thay đổi; lần cài thực tế vẫn phải kiểm tra backup đọc được.
- [x] Script chỉ thêm/thay đổi MCP entry của DSCons.
- [x] MCP entry không liên quan được giữ nguyên theo regression guard.
- [x] Ownership manifest ghi version, client, file, checksum và backup path.
- [x] Install không mở Revit và không sửa model.
- [ ] Chạy lại Check sau lần Install thực tế và lưu transcript.

## 9. Kết nối và smoke read-only

Trên model được khách hàng/giảng viên cho phép, luôn re-anchor theo thứ tự:

1. `system_status`
2. `document_info`
3. `get_active_view`
4. `get_selection` nếu workflow phụ thuộc selection
5. `get_capabilities`

- [x] Quy trình smoke read-only đã được duyệt: đọc lại document, active view,
  selection liên quan và capabilities trong chính lượt đang làm.
- [x] Trước thao tác phải xác định model copy, model khách hàng hay Central; nếu
  là model khách hàng/Central thì giải thích rủi ro và hỏi xác nhận riêng.
- [x] Kết quả quantity chỉ là snapshot hiện trạng, không tự thêm hao hụt, chi
  phí hoặc đề xuất mua hàng.
- [x] Coordination chỉ là triage/evidence để kỹ sư xem xét, không tự kết luận
  thiết kế đúng hoặc sai.
- [x] Smoke kết nối chỉ đọc và không Save/Sync.

### Biên bản smoke thực tế

- [ ] Bridge loopback và session secret hợp lệ.
- [ ] Loại document và phạm vi cho phép đã được xác nhận.
- [ ] Path document khớp path đã khai báo.
- [ ] Active view được ghi lại.
- [ ] Capability = 45 lệnh Revit.
- [ ] Host-facing discovery = 46 tool.
- [ ] MEP filter/detail/connectivity read-only PASS.
- [ ] Không có Save/Sync trong audit log.

## 10. Kiểm tra workflow giảng dạy

- [x] Nội dung một buổi được người phụ trách mở rộng ngày `15/09/2026`: sau
  Check/kết nối là pilot dựng tuyến, tạo bảng thống kê, tạo sheet và tạo Family
  trên project copy/demo directory đã được phép.
- [x] Thời lượng dự kiến: `60 phút`.
- [x] Agenda pilot: 0–8 phút re-anchor/kết nối; 8–15 phút catalog/context;
  15–27 phút dựng tuyến; 27–34 phút bảng thống kê; 34–45 phút sheet;
  45–57 phút Family quạt hướng trục; 57–60 phút read-back và MCP log.
- [x] Nếu phát sinh lỗi ngoài phạm vi xử lý nhanh, ghi Phiếu lỗi và chuyển sang
  hỗ trợ sau buổi; không lặp thao tác cài đặt một cách mù quáng để ép vừa giờ.
- [x] Điều kiện học viên cần chuẩn bị: Windows; Revit đã cài; một client trong
  Codex/ClaudeCode/Antigravity; quyền cài phần mềm; biết đúng phiên bản Revit;
  project copy và demo directory đã được phép. Học viên không phải tìm Family template.
- [ ] Walkthrough thực tế đã chạy và có transcript evidence. *(Chỉ đánh dấu
  sau khi kiểm thử; việc duyệt nội dung không thay thế bước này.)*

### Luồng một buổi

- [ ] Học viên mở đúng workspace/release và `START_HERE.md`.
- [ ] Học viên dán đúng prompt duy nhất.
- [ ] Agent hỏi Revit version và client; mỗi câu hỏi có ví dụ trả lời.
- [ ] Agent chạy `Check` chỉ-đọc và giải thích Node, artifact, Revit API,
  client config bằng ngôn ngữ tự nhiên.
- [ ] Lỗi kỹ thuật có nguyên nhân và hướng sửa; không dùng `BLOCKED` thay cho
  việc chẩn đoán có thể làm an toàn.
- [ ] Trước Install/Configure, Agent nêu rõ thay đổi và chờ xác nhận cụ thể.
- [ ] Học viên tự đóng toàn bộ Revit; Install chỉ chạy sau khi đã xác nhận.
- [ ] Install tạo backup/ownership, không mở Revit và không sửa model.
- [ ] Học viên tự mở Revit; Agent chỉ chạy smoke kết nối chỉ-đọc gồm status,
  document, view/selection khi cần và capabilities.
- [ ] Trước mỗi nhóm thao tác ghi, Agent nêu project copy, đối tượng dự kiến và
  chờ xác nhận cụ thể; không dùng Central/model khách hàng chưa được phép.
- [ ] Agent dựng một tuyến MEP nhỏ, đọc lại kết quả, rồi tạo quantity snapshot/bảng
  thống kê từ dữ liệu model.
- [ ] Agent tạo view/sheet theo preview → xác nhận → apply → read-back.
- [ ] Agent tạo Family quạt hướng trục; MCP tự chọn đúng template Mechanical
  Equipment theo phiên bản Revit và trả `template_selection`. Học viên không duyệt `.rft`.
- [ ] Nếu thiếu template chuyên ngành, MCP dùng Metric Generic Model đúng năm,
  đổi/đọc lại Family Category; Agent giải thích vị trí **Create → Family Category
  and Parameters** cho học viên.
- [ ] Không có CAD/Combine, Save/Sync, publish, print hoặc export trong pilot;
  các thao tác đó vẫn cần một phase/xác nhận riêng.
- [ ] Học viên kiểm tra hai nút Ribbon: Bật/Tắt MCP và Cập nhật Code;
  nút Bật/Tắt chỉ điều khiển bridge Revit, không kill Node của client.
- [ ] Học viên nhận biên bản PASS/FAIL/CẦN BẠN THỰC HIỆN, bằng chứng và bước
  tiếp theo; có hướng dẫn Uninstall/rollback.

## 11. Runtime evidence và claim

- [x] Tách riêng compile/build, runtime 28 tool, Family, CAD, Combine và learner release.
- [x] Revit 2023 chỉ claim theo copied-model evidence đã có.
- [x] Revit 2025 chỉ claim theo copied-model evidence đã có.
- [x] Pump/DB/axial connector chỉ claim theo record cụ thể.
- [x] `round_hvac` không claim nếu connector probe chưa PASS.
- [x] Solid intersection và clearance triage được phân biệt rõ.
- [x] Revit 2027 ghi POC nếu chưa có runtime record.
- [x] 2019–2027 ghi compile PASS; runtime chỉ claim theo evidence từng năm và
  2027 vẫn là POC.
- [x] Mỗi record có model copy, version, context, token, confirmation, read-back và audit.
- [x] Không ghi claim chung “Revit 2020–2027” khi thiếu runtime evidence.

## 12. Public source export

```powershell
$publicOut = Join-Path $env:TEMP 'dscons-public-export-YYYYMMDD'
.\scripts\export-public.ps1 -OutputRoot $publicOut
```

- [x] Export chỉ lấy source theo allowlist — isolated export ngày `15/09/2026`
  tạo 102 files PASS.
- [x] Không có `.agents/`, `inputs/`, model, audit/session/runtime data.
- [x] Không có `node_modules`, build/bin/obj/cache hoặc Revit DLL.
- [x] Không có catalog PDF, RFA tham khảo hoặc nội dung khóa học private.
- [x] Không có secret, local path hoặc API key.
- [x] Có `export-manifest.json` và SHA256.
- [x] UTF-8 không BOM và Markdown links không hỏng.
- [x] Script không `git init`, không tạo remote, commit hoặc push.

## 13. Rollback và Uninstall

- [ ] Chỉ thử trên máy/VM test; Revit đã đóng.
- [ ] Ownership manifest đúng version/client tồn tại.
- [ ] Backup còn nguyên và đọc được.
- [ ] Config/add-in chưa bị sửa ngoài ownership manifest.
- [ ] Dùng cờ xác nhận:

```powershell
.\scripts\student-setup.ps1 -Mode Uninstall `
  -RevitVersion 2023 -Client Codex `
  -ConfirmUninstall
```

- [ ] Thiếu confirm bị chặn.
- [ ] Revit đang chạy bị chặn.
- [ ] Checksum/config thay đổi bị chặn.
- [ ] Chỉ file/entry trong ownership manifest bị gỡ.
- [ ] Backup được khôi phục nếu có.
- [ ] MCP entry không liên quan vẫn nguyên vẹn.
- [ ] Ownership manifest chỉ bị xóa sau khi uninstall hợp lệ.
- [ ] Chạy Check lại sau Uninstall và lưu bằng chứng.

## 14. Cổng dừng bắt buộc

Dừng phát hành và ghi `BLOCKED` nếu:

- [ ] Protocol không đúng 46 tool.
- [ ] Không xác minh được checksum.
- [ ] Revit đang mở khi Install/Uninstall.
- [ ] Không xác định được copied model hoặc Central state.
- [ ] Client entrypoint stale/missing.
- [ ] JSON/TOML/XML/Markdown link lỗi.
- [ ] Public export có secret, model, local path hoặc Revit DLL.
- [ ] Preview/apply thiếu confirmation hoặc read-back.
- [ ] Có Save/Sync ngoài phạm vi cho phép.
- [ ] Chưa ghi rõ “Chưa chắc hoặc chưa test”.

## 15. Biên bản bàn giao

- [x] Student release path: `artifacts/student-release-20260922-r7`
- [x] `release-manifest.json` SHA256:
  `7D6474788B9275286EAB0D511FD10F488A0B60755DBDB12BD9F20572BC710562`
- [x] Revit 2023 artifact SHA256 (loader):
  `674000DAFF52B81830E7337A90498738F41F0453D7C520CE82C058690FE03C9E`
- [x] Revit 2025 artifact SHA256 (loader):
  `A1646F731C666F1B275CC630685617C3DCCB6EDBA80AE6E6AA2455E7F973E5FE`
- [x] Node entrypoint SHA256:
  `2F9577D2FE5C1DCBCA86066999DE6D0BA13991181CA7416F2A2D82B4E09DAF65`
- [x] Public export path: `artifacts/public-export-20260922-r7`
  (không đưa đường dẫn máy cá nhân vào source/public export).
- [x] Hash export được ghi ở biên bản release ngoài public export để tránh
  manifest tự checksum chính tài liệu đang chứa hash của manifest.
- [x] Protocol: `PASS` (46 tools)
- [x] Contracts/static: `PASS`
- [ ] Learner walkthrough: `CHƯA CHẠY — cần môi trường an toàn và transcript`
- [ ] Runtime 2023: `NOT RUN trong phase checklist này`
- [ ] Runtime 2025: `NOT RUN trong phase checklist này`
- [x] Các năm: `compile/package: 2019–2027; runtime theo evidence riêng; 2027 POC`
- [ ] Fire Keeper duyệt evidence: `Chưa — cần duyệt walkthrough/artifact cuối`
- [ ] Ngày bàn giao: `Chưa bàn giao`
- [ ] Người xác nhận: `Chưa xác nhận`

## 16. Cập nhật ledger sau mỗi phase

Ghi vào ledger phù hợp, không viết lại lịch sử cũ:

- **Đã làm:** việc đã hoàn tất.
- **Bằng chứng:** lệnh, checksum, report, runtime ID hoặc transcript.
- **Chưa chắc hoặc chưa test:** giới hạn còn lại.
- **Cần Fire Keeper duyệt:** quyết định hoặc fixture cần duyệt.
- **Bước tiếp theo:** hành động kế tiếp, người phụ trách và điều kiện bắt đầu.

Sau checklist, cập nhật tối thiểu `.agents/chat-history.md`; nếu phạm vi hoặc
acceptance thay đổi, cập nhật thêm `.agents/prd.md`,
`.agents/implementation-plan.md`, `.agents/implementation-status.md` và
`.agents/tasks.md`.
