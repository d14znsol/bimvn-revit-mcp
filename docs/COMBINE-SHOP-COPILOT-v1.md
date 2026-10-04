# DSCons Combine & Shop Copilot v1

## Mục đích và trạng thái

Wave này hỗ trợ một kỹ sư Cơ Điện xử lý khu vực bất lợi bằng bằng chứng Revit-first: kiểm kê link, triage bounding-box, xác nhận Solid intersection, phân loại issue và tạo mặt cắt kiểm tra. Thứ tự quyết định là kỹ thuật → chi phí → thẩm mỹ; hệ tự chảy/độ dốc, thiết bị lớn hoặc khó thay đổi được ghi nhận là protected trước khi bàn phương án.

Trạng thái hiện tại là **offline implementation đã kiểm tra contract/build; chưa có runtime evidence Combine/Shop**. Không được nói là hỗ trợ runtime cho Revit 2023/2025 cho tới khi Runtime Test Report của copied model được Fire Keeper duyệt.

Không thuộc Wave này: tự reroute, sửa Central, Save/Sync, tag/dimension/BOD/BOE/COB, bố trí support, clash certificate cho khoảng hở, hoặc coi nội dung khóa học là quy chuẩn/kết luận kỹ thuật.

## Sáu tool bổ sung

| Tool | Tác dụng | Ghi/chỉnh model |
| --- | --- | --- |
| `dscons_knowledge_search` | Tìm cục bộ trong file summary được người dùng duyệt, trả excerpt/heading/path/SHA256. | Không |
| `coordination_solid_scan` | Bbox prefilter rồi Revit Solid intersection, hoặc `clearance_triage`. | Không |
| `coordination_issue_report_preview` | Ghép finding và quyết định kỹ sư thành preview báo cáo. | Không |
| `coordination_issue_report_apply` | Publish JSON/HTML qua staging GUID, no-overwrite và checksum. | Chỉ file báo cáo |
| `coordination_section_preview` | Tạo section tạm trong `TransactionGroup`, sau đó rollback. | Không |
| `coordination_section_apply` | Tạo section thật, read-back trước assimilate. | Chỉ copied/local model được phép |

## Quy trình cá nhân

1. Re-anchor trong lượt hiện tại: `document_info`, `get_active_view`, rồi `bim_context_snapshot`.
2. Gọi `coordination_links`; chọn link đang load và chỉ rõ source element/category khi có thể.
3. Dùng `coordination_scan` cho bbox triage nếu cần. Đây chỉ là shortlist, không phải clash certificate.
4. Dùng `coordination_solid_scan` với `scan_mode: solid_intersection`. `scan_id` bị vô hiệu nếu document, view, link hoặc transform đổi.
5. Kỹ sư phân loại mọi finding: `real_clash`, `false_positive`, `accepted`, hoặc `needs_information`; ghi priority, phần tử protected/movable và ghi chú.
6. Nếu cần evidence riêng, chạy `coordination_issue_report_preview`. Chỉ dùng thư mục output local mà kỹ sư đã duyệt. Sau khi kỹ sư xác nhận output, mới gọi apply.
7. Chọn các `finding_id` cần nhìn trong mặt cắt, cung cấp đúng Section `ViewFamilyType`, template (nếu có), scale/crop/depth rồi gọi `coordination_section_preview`.
8. Chỉ sau đúng câu **XÁC NHẬN TẠO MẶT CẮT COMBINE** mới gọi `coordination_section_apply`; đọc lại tên, scale, crop/depth, template và view ID.
9. Nếu cần đặt mặt cắt lên sheet, đó là bước độc lập qua `documentation_plan` rồi `documentation_apply` với context mới.

## Bằng chứng và guard

- Solid finding có source/link element ID, category/system, host location, intersection volume, confidence và trạng thái `unreviewed`.
- `clearance_triage` chỉ nói rằng bbox đã mở rộng giao nhau; không phải Solid certificate hay rule khoảng hở tự động.
- Preview token là single-use, hết hạn sau 90 giây, gắn document/view/resource fingerprint. Scan giữ tối đa 15 phút và kiểm lại transform/load state của link.
- Report không ghi đè: JSON/HTML được ghi file staging GUID, move rồi checksum/read-back; lỗi sẽ dọn staging và output vừa tạo.
- Section preview và apply đều chặn read-only/Central trực tiếp. Apply không gọi Save hoặc Sync.

## Kho kiến thức riêng tư

`dscons_knowledge_search` chỉ đọc `TỔNG HỢP KHÓA HỌC.md` và `*_summary.md` trong `approved_knowledge_directory` do người dùng cung cấp ở chính lần gọi đó. UNC/network path, traversal, symlink và transcript thô bị loại. Kết quả tối đa 20 hit, excerpt bị giới hạn, và citation chỉ chứa canonical path, SHA256, heading.

Kho học liệu là evidence/rule candidate riêng tư. Không đưa file, transcript, path cục bộ hoặc text khóa học vào source, learner release, public export hay Issue Report; kỹ sư vẫn phải xác nhận giá trị clearance, slope, maintenance và support theo nguồn kỹ thuật dự án.

## Prompt an toàn mẫu

```text
Đọc document_info, get_active_view và bim_context_snapshot trước. Liệt kê coordination_links,
sau đó chạy coordination_solid_scan chỉ trên các element/link tôi chỉ định. Không reroute hay tạo view.
Trả finding unreviewed, tách riêng solid_intersection với clearance_triage và nêu dữ liệu còn thiếu.
```

## Backlog sau runtime evidence

Option scoring/reroute preview; support layout; tag, dimension và cao độ; shop-sheet QA; Navisworks issue round-trip; rule pack HVAC/Điện/Nước/PCCC. Các hạng mục này không được suy là đã có từ Issue Report hoặc section view.
