# BỘ GIÁO DỤC VÀ ĐÀO TẠO — TRƯỜNG ĐẠI HỌC CHUYÊN NGÀNH XÂY DỰNG / BÁCH KHOA
## ĐỀ CƯƠNG CHI TIẾT ĐỒ ÁN TỐT NGHIỆP / ĐỀ TÀI NGHIÊN CỨU ỨNG DỤNG

---

### TÊN ĐỀ TÀI:
**NGHIÊN CỨU XÂY DỰNG ADD-IN TRÊN NỀN TẢNG AUTODESK REVIT PHỤC VỤ TỰ ĐỘNG HÓA THIẾT KẾ, ĐỊNH TUYẾN THÍCH ỨNG TRÁNH VA CHẠM VÀ THẨM TRA TÍNH TOÁN THỦY LỰC MẠNG LƯỚI CHỮA CHÁY SPRINKLER**

* **Tên tiếng Anh:** *Development of an Autodesk Revit Add-in for Automated Layout, Adaptive Collision-Avoidance Routing, and Hydraulic Verification of Fire Sprinkler Networks.*
* **Tên thương mại / Mã dự án:** **SSer (Smart Sprinkler)** — Bộ giải pháp **SBIM** (Nhận diện thương hiệu: Thái Sơn - BIMVN, Tông màu chủ đạo: `#004225`).
* **Lĩnh vực nghiên cứu:** Mô hình thông tin công trình (BIM), Lập trình tự động hóa thiết kế (Computational Design / Revit API), Kỹ thuật Cơ điện & Phòng cháy chữa cháy (MEP & Fire Protection Engineering).

---

### PHẦN I: TÍNH CẤP THIẾT & XÁC ĐỊNH BÀI TOÁN THỰC TẾ TRONG NGÀNH

#### 1. Bài toán thực tế tại các dự án xây dựng hiện nay
Hệ thống chữa cháy tự động bằng nước (Sprinkler) là hạng mục an toàn sinh mạng cốt thái bắt buộc trong hầu hết các công trình dân dụng (chung cư cao tầng, trung tâm thương mại, khách sạn, văn phòng) và công nghiệp (nhà xưởng, kho bãi) theo Luật PCCC và Quy chuẩn kỹ thuật quốc gia QCVN 06:2022/BXD. Tuy nhiên, quy trình triển khai thiết kế và điều phối mô hình BIM hệ thống Sprinkler hiện nay tại Việt Nam đang đối mặt với những nút thắt lớn:
* **Thiết kế rải đầu phun thủ công, lặp lại và dễ sai sót:** Kỹ sư phải chia lưới từng phòng, đo vẽ thủ công khoảng cách giữa các đầu phun ($S$) và khoảng cách tới vách tường ($D$). Với các dự án quy mô hàng chục tầng, hàng trăm phòng chức năng với hình học phức tạp, quá trình này tốn hàng trăm giờ làm việc và dễ vi phạm quy chuẩn về vùng diện tích bảo vệ tối đa ($9-12\text{ m}^2$/đầu).
* **Nút thắt không gian trần và va chạm đa bộ môn (Ceiling Void Congestion & Clashes):** Không gian khoảng trần kỹ thuật là nơi tập trung dày đặc của dầm bê tông, cột kết cấu, hệ thống ống gió điều hòa - thông gió (HVAC), máng cáp điện (Cable Tray) và đường ống cấp thoát nước. Tuyến ống chữa cháy thường được dựng hình sau cùng. Khi gặp dầm hoặc ống gió chắn ngang, kỹ sư phải thực hiện thao tác thủ công: cắt ống, tính toán cao độ hạ, tạo 4 cút lượn ($90^\circ$ hoặc $45^\circ$) để né vật cản. Thao tác uốn né thủ công (Bypass Offset) chiếm tới 40% – 50% tổng thời gian dựng hình MEP.
* **Sự phân mảnh giữa mô hình dựng hình 3D và tính toán thủy lực:** Kỹ sư thường vẽ ống trên Revit theo "kinh nghiệm" hoặc tra bảng sơ bộ, sau đó xuất số liệu sang các bảng tính Excel rời rạc hoặc phần mềm tính thủy lực độc lập để chạy thẩm tra lưu lượng ($Q$), tổn thất áp lực ($p$) và vận tốc ($v$). Khi kết quả thủy lực yêu cầu thay đổi đường kính ống (ví dụ tăng từ DN32 lên DN50 để giảm vận tốc về dưới $5\text{ m/s}$), kỹ sư lại phải quay lại Revit tìm từng đoạn ống để chỉnh sửa thủ công. Quy trình đứt gãy hai chiều này tiềm ẩn rủi ro sai lệch dữ liệu rất cao khi nộp hồ sơ thẩm duyệt PCCC.

#### 2. Lý do chọn đề tài
1. **Đáp ứng lộ trình chuyển đổi số và bắt buộc áp dụng BIM:** Theo Quyết định số 258/QĐ-TTg ngày 17/03/2023 của Thủ tướng Chính phủ, việc áp dụng BIM là yêu cầu bắt buộc đối với các dự án đầu tư xây dựng. Việc phát triển các công cụ tự động hóa chuyên sâu (Custom Add-in) là chìa khóa để nâng cao năng suất thiết kế và năng lực cạnh tranh của kỹ sư Việt Nam.
2. **Cập nhật và nội địa hóa Tiêu chuẩn mới TCVN 7336:2021:** Tiêu chuẩn mới ban hành quy định khắt khe hơn về phân loại nguy cơ cháy, áp lực tối thiểu tại đầu phun ($P_{min} \ge 0.5\text{ bar}$) và kiểm soát vận tốc dòng chảy tối đa ($v \le 5\text{ m/s}$).
3. **Khoảng trống phần mềm bản địa hóa "Make in Vietnam":** Các công cụ quốc tế (như MagiCAD, AutoSPRINK) có giá thành bản quyền rất đắt đỏ (hàng trăm triệu đồng/năm), không tích hợp sẵn tiêu chuẩn TCVN 7336:2021 và thuật toán uốn né dầm tự động còn hạn chế. Do đó, việc nghiên cứu phát triển một Add-in tích hợp trực tiếp trên nền tảng **Autodesk Revit 2026** phục vụ kỹ sư trong nước là hoàn toàn cấp thiết và có giá trị ứng dụng cao.

---

### PHẦN II: MỤC TIÊU NGHIÊN CỨU & NỘI DUNG CỤ THỂ

#### 1. Mục tiêu tổng quát
Nghiên cứu làm chủ công nghệ lập trình Revit API trên nền tảng .NET hiện đại, xây dựng thành công bộ công cụ Add-in **SSer (Smart Sprinkler)** chạy tích hợp trên Autodesk Revit 2026, cung cấp giải pháp khép kín từ: Tự động bố trí đầu phun $\rightarrow$ Định tuyến thích ứng né va chạm $\rightarrow$ Thẩm tra và tối ưu hóa cỡ ống thủy lực tự động theo tiêu chuẩn Việt Nam TCVN 7336:2021.

#### 2. Mục tiêu kỹ thuật cụ thể (3 Module cốt lõi)
* **Module 1 — Thiết kế & Bố trí tự động đầu phun Sprinkler (Automated Layout Engine):**
  * Tự động nhận diện hình học không gian phòng (Room/Space) hoặc vùng lựa chọn của người dùng trên bất kỳ Tầng (Level) nào được chỉ định.
  * Tự động tính toán số hàng, số cột, khoảng cách tối ưu giữa các đầu phun ($S_x, S_y$) và khoảng cách tới biên tường ($D_x, D_y$) tuân thủ tuyệt đối các ràng buộc của TCVN 7336:2021 theo từng cấp nguy cơ cháy (Light Hazard, Ordinary Hazard Group 1 & 2).
  * Cho phép người dùng linh hoạt: Tự động theo Room BoundingBox hoặc Click chọn điểm gốc trên màn hình; tự động gán cao độ treo theo trần/sàn tầng thực tế.
* **Module 2 — Định tuyến thích ứng tránh va chạm thông minh (Adaptive Collision-Avoidance Routing):**
  * Tự động sinh tuyến ống nhánh (Branch Lines) kết nối hàng loạt đầu phun và tuyến ống cấp chính (Cross Main).
  * Ứng dụng thuật toán quét va chạm không gian (Ray-Casting / BoundingBox Intersection) để phát hiện chướng ngại vật: Dầm bê tông, Cột, Mũ cột (kết cấu) và Ống gió, Máng cáp, Ống Chiller (cơ điện).
  * Tự động uốn né hạ luồn (Bypass Offset) bằng cụm 4 cút chuyển hướng ($90^\circ$ hoặc $45^\circ$) với khoảng hở tĩnh đảm bảo an toàn thi công $\ge 50\text{ mm}$.
* **Module 3 — Động cơ thẩm tra thủy lực & Tự đổi cỡ ống (Hydraulic Engine & Auto-Sizing):**
  * Xây dựng đồ thị mạng lưới phân nhánh (Directed Acyclic Graph - Tree System), tự động duyệt cây từ đầu phun xa nhất về nguồn cấp theo thuật toán Post-Order Traversal.
  * Tính toán thủy lực chính xác theo công thức Hazen-Williams ($C=120$): Lưu lượng từng nút $Q = K \cdot \sqrt{P}$, tổn thất áp lực ma sát dọc đường $p$ và cục bộ qua phụ kiện co/cút/tê (theo NFPA 13).
  * Kiểm soát vận tốc dòng chảy $v \le 5\text{ m/s}$; tự động nâng/hạ đường kính danh định (Auto-Sizing từ DN25 đến DN150 Schedule 40).
  * Xuất báo cáo thẩm tra trực quan trên giao diện WPF (bôi màu Pass/Warning) và trích xuất bảng tính ra file Excel phục vụ hồ sơ thẩm duyệt PCCC.

---

### PHẦN III: PHẠM VI NGHIÊN CỨU & ĐỐI TƯỢNG SỬ DỤNG

#### 1. Phạm vi nghiên cứu (Scope & Limitations)
* **Loại công trình áp dụng:** Công trình dân dụng (văn phòng, thương mại, chung cư) và công nghiệp tiêu chuẩn có trần phẳng hoặc lộ dầm/mũ cột.
* **Loại hệ thống PCCC:** Hệ thống chữa cháy tự động bằng nước Sprinkler đường ống ướt (Wet Pipe Sprinkler System) dạng mạng lưới phân nhánh hình cây (Tree System: Main Riser $\rightarrow$ Cross Main $\rightarrow$ Branch Lines $\rightarrow$ Sprinkler Heads).
* **Tiêu chuẩn áp dụng:** 
  * Tiêu chuẩn chính: **TCVN 7336:2021** (Phòng cháy chữa cháy - Hệ thống chữa cháy tự động bằng nước và bọt).
  * Tiêu chuẩn tham chiếu: **NFPA 13** (Standard for the Installation of Sprinkler Systems - bảng chiều dài tương đương của phụ kiện).
* **Đối tượng chướng ngại vật quét va chạm:** Dầm bê tông (Structural Framing), Cột (Structural Columns), Mũ cột (Drop Panels), Ống gió (Ducts), Máng cáp (Cable Trays), Ống Chiller/HVAC Pipes.
* **Môi trường kỹ thuật:** Autodesk Revit 2026, nền tảng ngôn ngữ C# / .NET 10 (`net10.0-windows`), giao diện đồ họa WPF / XAML, kiến trúc nạp module động (Hot Reload Host).

#### 2. Đối tượng sử dụng & Thụ hưởng đề tài
* **Kỹ sư thiết kế Cơ điện (MEP Design Engineers):** Rút ngắn 70% – 80% thời gian rải đầu phun và dựng hình đường ống né dầm; giảm thiểu sai sót chủ quan.
* **Kỹ sư điều phối BIM (BIM Coordinators / BIM Managers):** Giải quyết triệt để các bài toán xung đột va chạm không gian trần (Clash Detection), xuất mô hình sạch không còn lỗi giao cắt vật lý.
* **Cơ quan thẩm duyệt và Chuyên viên PCCC:** Nhận được hồ sơ thẩm tra thủy lực minh bạch, chuẩn hóa theo TCVN 7336:2021 kèm biểu đồ vận tốc và áp lực chi tiết cho từng đoạn ống.
* **Các doanh nghiệp tư vấn thiết kế và nhà thầu cơ điện:** Sở hữu công cụ Add-in bản địa hóa, tiết kiệm hàng ngàn USD chi phí mua bản quyền phần mềm nước ngoài hàng năm.
* **Sinh viên, học viên ngành Xây dựng / MEP:** Sử dụng làm tài liệu tham khảo mẫu mực về kỹ thuật lập trình Revit API nâng cao kết hợp cơ học chất lỏng và tiêu chuẩn ngành.

---

### PHẦN IV: PHƯƠNG PHÁP LUẬN & KIẾN TRÚC PHẦN MỀM

#### 1. Phương pháp nghiên cứu
* **Phương pháp phân tích lý thuyết & Tiêu chuẩn:** Nghiên cứu đối chiếu quy chuẩn TCVN 7336:2021 và NFPA 13 để trích xuất các bảng dữ liệu kỹ thuật (Hazard Tables, Schedule Sizing Tables, Friction Loss Coefficients).
* **Phương pháp mô hình hóa hình học tính toán (Computational Geometry):** Thuật toán tối ưu hóa lưới điểm 2D trên mặt phẳng sàn; thuật toán tia quét Ray-Casting và BoundingBox mở rộng để tính toán vector uốn né 3D.
* **Phương pháp lý thuyết đồ thị (Graph Theory):** Xây dựng cấu trúc cây phân cấp `HydraulicNetworkNode` để mô phỏng dòng chảy một chiều và tính toán thủy lực tích lũy.
* **Phương pháp thực nghiệm phát triển phần mềm (Software Engineering):**
  * Áp dụng kiến trúc Clean Architecture: Tách biệt hoàn toàn `Core Engine` (xử lý toán học/thủy lực độc lập, hỗ trợ Unit Test) và `Revit Adapter` (tầng giao tiếp với Revit API).
  * Phương pháp phát triển dựa trên kiểm thử (Test-Driven Development - TDD): Đảm bảo 100% các hàm tính thủy lực, Hazen-Williams và phân cỡ ống vượt qua các ca kiểm thử tự động.

#### 2. Sơ đồ luồng xử lý tổng thể của hệ thống SSer
```
[Khởi chạy SSer trên Revit 2026 (Tab SBIM)]
                   │
                   ▼
[1. Khảo sát mô hình: Quét Level, Family Sprinkler, Room BoundingBox]
                   │
                   ▼
[2. Giao diện WPF SSer: Chọn Tầng, Cấp nguy cơ (TCVN 7336), Nhập kích thước/Pick điểm]
                   │
                   ▼
[3. Module 1: Layout Engine tính toán tọa độ lưới đầu phun (Sx, Sy, Dx, Dy)]
                   │
                   ▼
[4. Module 2: Adaptive Routing vẽ tuyến ống & Ray-Casting phát hiện Dầm/Ống gió]
                   │
         ┌─────────┴─────────┐
    (Có va chạm)        (Không va chạm)
         │                   │
         ▼                   │
  [Tự động tạo 4 cút         │
  Bypass Offset hở ≥50mm]    │
         │                   │
         └─────────┬─────────┘
                   ▼
[5. Module 3: Hydraulic Engine duyệt cây mạng lưới, tính Hazen-Williams (Q, p, v)]
                   │
                   ▼
[6. Auto-Sizing: Tự động gán đường kính ống (DN25 - DN150) thỏa mãn v ≤ 5 m/s]
                   │
                   ▼
[7. Xuất Bảng Thẩm Tra Thủy Lực trực quan (Pass/Warning) & Xuất File Excel]
```

---

### PHẦN V: DỰ KIẾN CẤU TRÚC THUYẾT MINH ĐỒ ÁN (BÁO CÁO TỐT NGHIỆP)

* **LỜI CẢM ƠN — CAM ĐOAN — DANH MỤC CÁC KÝ HIỆU, CHỮ VIẾT TẮT**
* **MỞ ĐẦU:** Tính cấp thiết của đề tài; Mục tiêu; Đối tượng và phạm vi nghiên cứu; Ý nghĩa khoa học và thực tiễn.
* **CHƯƠNG 1: TỔNG QUAN VỀ HỆ THỐNG CHỮA CHÁY SPRINKLER VÀ CÔNG NGHỆ BIM/REVIT API**
  * 1.1. Cấu tạo, nguyên lý hoạt động của hệ thống chữa cháy Sprinkler mạng nhánh cây.
  * 1.2. Tổng quan tiêu chuẩn thiết kế TCVN 7336:2021 và các yêu cầu kỹ thuật bắt buộc.
  * 1.3. Tổng quan công nghệ BIM, Autodesk Revit và tiềm năng ứng dụng của Revit API.
  * 1.4. Đánh giá các giải pháp phần mềm hiện có và khoảng trống nghiên cứu tại Việt Nam.
* **CHƯƠNG 2: CƠ SỞ KHOA HỌC, THUẬT TOÁN ĐỊNH TUYẾN VÀ TÍNH TOÁN THỦY LỰC**
  * 2.1. Cơ sở toán học bố trí tối ưu lưới đầu phun trong không gian phòng.
  * 2.2. Thuật toán phát hiện xung đột không gian và định tuyến uốn né thích ứng (Bypass Offset).
  * 2.3. Cơ sở lý thuyết thủy lực đường ống theo công thức Hazen-Williams và bài toán Auto-Sizing.
  * 2.4. Thuật toán duyệt đồ thị mạng lưới phân nhánh (Directed Acyclic Graph Traversal).
* **CHƯƠNG 3: THIẾT KẾ KIẾN TRÚC VÀ XÂY DỰNG ADD-IN SSER TRÊN REVIT 2026**
  * 3.1. Thiết kế kiến trúc phần mềm tổng thể (Clean Architecture & Hot Reload Host).
  * 3.2. Xây dựng Module 1: Giao diện và thuật toán bố trí đầu phun tự động.
  * 3.3. Xây dựng Module 2: Cơ chế vẽ ống tự động và module quét né va chạm vật lý.
  * 3.4. Xây dựng Module 3: Module thẩm tra thủy lực và xuất báo cáo tự động.
  * 3.5. Kiểm thử đơn vị (Unit Testing) và kiểm tra tính hợp lệ của các mô-đun.
* **CHƯƠNG 4: THỬ NGHIỆM TRÊN MÔ HÌNH THỰC TẾ, ĐÁNH GIÁ KẾT QUẢ VÀ SO SÁNH ĐỐI CHUẨN**
  * 4.1. Giới thiệu mô hình công trình thử nghiệm (Mặt bằng tầng điển hình có dầm và ống gió).
  * 4.2. Quá trình triển khai tự động hóa bằng Add-in SSer.
  * 4.3. Đánh giá kết quả: Độ chính xác định vị, mức độ sạch va chạm (0 clashes), sai số thủy lực so với tính toán lý thuyết.
  * 4.4. So sánh hiệu quả về thời gian và năng suất lao động so với phương pháp thiết kế thủ công.
* **KẾT LUẬN VÀ KIẾN NGHỊ:** Các kết quả chính đạt được; Hạn chế của đề tài; Hướng phát triển mở rộng trong tương lai.
* **TÀI LIỆU THAM KHẢO & PHỤ LỤC (Bao gồm Mã nguồn chương trình và Báo cáo tính toán thủy lực mẫu).**

---

### PHẦN VI: KẾ HOẠCH THỰC HIỆN & SẢN PHẨM MINH CHỨNG ĐẦU RA

#### 1. Kế hoạch thực hiện (Tiến độ dự kiến 10 tuần)
| Giai đoạn | Tuần thực hiện | Nội dung công việc chính | Sản phẩm / Kết quả đạt được |
| :---: | :---: | :--- | :--- |
| **GĐ 1** | Tuần 1 – 2 | Thu thập tiêu chuẩn TCVN 7336:2021, NFPA 13, bài báo khoa học; xây dựng đề cương chi tiết. | Đề cương nghiên cứu, Báo cáo tổng quan tài liệu. |
| **GĐ 2** | Tuần 3 – 4 | Thiết lập môi trường Revit 2026 / .NET 10; phát triển Core Engine tính thủy lực; viết Unit Test. | 12/12 ca kiểm thử Unit Test thủy lực vượt qua (Pass). |
| **GĐ 3** | Tuần 5 – 6 | Xây dựng giao diện WPF SBIM; tích hợp Module 1 tự động chọn Tầng và rải đầu phun trên mô hình. | Module 1 hoàn thiện, rải thành công đầu phun có kiểm soát cao độ. |
| **GĐ 4** | Tuần 7 – 8 | Xây dựng Module 2 quét va chạm dầm/ống gió và tự tạo 4 cút né Bypass Offset; liên kết tuyến ống. | Tuyến ống nhánh và ống chính tự động sinh, né 100% dầm kết cấu. |
| **GĐ 5** | Tuần 9 | Xây dựng Module 3 thẩm tra thủy lực thời gian thực, Auto-Sizing đổi cỡ ống và xuất báo cáo Excel. | Giao diện thẩm tra trực quan, xuất báo cáo PCCC chuẩn hóa. |
| **GĐ 6** | Tuần 10 | Chạy thử nghiệm toàn diện trên mô hình thực tế; hoàn thiện Thuyết minh đồ án và Slide bảo vệ. | Thuyết minh hoàn chỉnh, Video demo sản phẩm, Bản cài đặt Add-in. |

#### 2. Sản phẩm minh chứng bàn giao của đồ án
1. **Bộ mã nguồn chương trình hoàn chỉnh (Source Code):** Dự án C# / .NET 10 gồm các project `DSCons.Revit.Starter.Core`, `DSCons.Revit.Starter.Core.Tests`, `DSCons.Revit.HotReloadHost`, `DSCons.Revit.Starter`.
2. **Gói cài đặt Add-in:** Tích hợp trực tiếp lên Autodesk Revit 2026 dưới dạng Tab Ribbon **`SBIM`**, màu sắc nhận diện `#004225`.
3. **Quyển thuyết minh đồ án tốt nghiệp:** Đạt chuẩn 80 – 120 trang trình bày khoa học, đầy đủ cơ sở lý thuyết, sơ đồ thuật toán, kết quả kiểm nghiệm.
4. **Báo cáo thẩm tra thủy lực mẫu:** File Excel / PDF xuất trực tiếp từ phần mềm phục vụ trình nộp cơ quan Cảnh sát PCCC.
5. **Video clip ghi hình hoạt nghiệm (Demo Video):** Minh chứng quá trình thao tác 1-click tự động rải đầu phun, tự động uốn né dầm và tự động đổi cỡ ống ngay trong Revit.
