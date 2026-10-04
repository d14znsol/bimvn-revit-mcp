# Mở rộng Revit 2019–2027

Source có mapping từng năm; chưa có runtime evidence toàn dải.
Không dùng DLL năm khác để build thay. Không coi cùng .NET là cùng Revit API.

## Build không cần cài mọi Revit trên máy giảng viên

Chủ sở hữu Revit cung cấp bộ API tham chiếu hợp lệ từ đúng bản đã cài:
RevitAPI.dll, RevitAPIUI.dll, Newtonsoft.Json.dll và dependency bổ sung nếu
compiler yêu cầu. Chỉ dùng nội bộ theo quyền sử dụng; không đưa vào gói học viên
hay public export. Website Revit API Docs chỉ để tra chữ ký, không cung cấp DLL.

```powershell
.\scripts\build-mcp.ps1 -RevitVersion 2024 -ApiDirectory D:\RevitApiBundles\2024
.\scripts\build-student-release.ps1 -OutputRoot D:\DSConsPilot2024 -RevitVersions 2024 -ApiBundleRoot D:\RevitApiBundles
```

Script kiểm tra assembly identity và major version trước build; thiếu hoặc sai
năm thì dừng. Một thư mục API không được dùng cho `-RevitVersion All`.
Các target cần reference/developer pack .NET tương ứng trên máy build.
Học viên dùng binary đúng năm, không cần build hoặc cài các năm Revit khác.

## Nghiệm thu phân tán

Mỗi năm một máy có Revit được cấp phép, model copy và người kiểm thử được duyệt.
Không yêu cầu một máy cài cả chín năm. Test 2023 trước, 2025 sau, rồi các năm
còn lại độc lập. Lưu version/build Revit, hash DLL DSCons, client, model fixture,
tool/case, preview, xác nhận, read-back, rollback và lỗi vào Runtime Test Report.
Chỉ sau PASS mới đổi trạng thái runtime của năm đó. Family/CAD/Combine cần
record riêng; generic apply đang Unsupported không được ghi thành PASS tạo hình.

Ngày 2026-09-15: đủ bộ `RevitAPI.dll`, `RevitAPIUI.dll` và
`NewtonSoft.Json.dll` đúng năm cho toàn bộ 2019–2027. Build tuần tự đúng năm
PASS cả chín bản. 2019–2024 không có cảnh báo C#; 2025–2027 chỉ còn cảnh báo
dependency `MSB3277 Microsoft.VisualBasic` đã biết. Kết quả compile/package
không phải bằng chứng runtime.
Runtime MEP/V2 có evidence lịch sử 2023/2025. Các năm khác chưa nghiệm thu;
2027 vẫn POC. Xem COMPATIBILITY.md. Bộ cài học viên vẫn có các gate rollback và
walkthrough chưa hoàn tất; mở rộng build không tự giải quyết những gate đó.
