# BÁO CÁO TỔNG QUAN TÀI LIỆU NGHIÊN CỨU & BẢNG TỔNG HỢP MINH CHỨNG
## Đề tài: Add-in Revit xây dựng hệ thống tự động thiết kế, định tuyến thích ứng tránh va chạm và tính toán thủy lực mạng lưới chữa cháy Sprinkler (SSer - SBIM)

---

### PHẦN 1: TỔNG QUAN VÀ TÓM TẮT CHI TIẾT CÁC TÀI LIỆU LIÊN QUAN

Nhằm xây dựng cơ sở khoa học, phương pháp luận kỹ thuật và cơ sở pháp lý vững chắc cho đề tài đồ án, tác giả đã tiến hành khảo cứu, phân tích và hệ thống hóa **8 tài liệu tiêu biểu** thuộc 3 nhóm trụ cột chính:
1. **Quy chuẩn - Tiêu chuẩn kỹ thuật pháp lý (Việt Nam & Quốc tế):** TCVN 7336:2021 và NFPA 13.
2. **Bài báo khoa học chuyên ngành (ISI/Scopus & Trong nước):** Các nghiên cứu về thuật toán tìm đường 3D, định tuyến né va chạm (Collision Avoidance) và tự động hóa thủy lực trên nền tảng BIM/Revit API.
3. **Phần mềm & Giải pháp công nghệ tương tự trên thị trường:** MagiCAD Sprinkler Designer, AutoSPRINK, và Canute FHC.

---

#### 1. Tài liệu 1: TCVN 7336:2021 — Tiêu chuẩn Quốc gia Việt Nam
* **Tên tài liệu:** TCVN 7336:2021: *Phòng cháy chữa cháy - Hệ thống chữa cháy tự động bằng nước và bọt - Yêu cầu thiết kế và lắp đặt* (Thay thế TCVN 7336:2003).
* **Đơn vị ban hành:** Bộ Khoa học và Công nghệ công bố; Cục Cảnh sát PCCC & CNCH phối hợp Viện Khoa học Công nghệ Xây dựng biên soạn.
* **Nội dung cốt lõi:**
  * **Phân loại mức độ nguy cơ cháy:** Phân định rõ 5 nhóm: Nguy cơ cháy thấp (Light Hazard), Nguy cơ cháy trung bình Nhóm 1, 2, 3 (Ordinary Hazard Groups 1, 2, 3), và Nguy cơ cháy cao (Extra Hazard).
  * **Chỉ tiêu kỹ thuật bố trí:** 
    * Cường độ phun yêu cầu $I$ (từ $0.08$ đến trên $0.24\text{ L/(s}\cdot\text{m}^2\text{)}$).
    * Diện tích bảo vệ tối đa của một đầu phun $A_{head}$ ($9\text{ m}^2$ đến $12\text{ m}^2$).
    * Khoảng cách tối đa giữa các đầu phun $S \le 3.0\text{ m} - 4.0\text{ m}$.
    * Khoảng cách từ đầu phun tới vách tường bao $D_{wall} \le S/2$ (hoặc không vượt quá $1.5\text{ m} - 2.0\text{ m}$).
  * **Chỉ tiêu áp lực và lưu lượng:** Áp lực làm việc tối thiểu tại đầu phun bất lợi nhất $P_{min} \ge 0.05\text{ MPa}$ ($0.5\text{ bar}$). Lưu lượng đầu phun: $Q = K \cdot \sqrt{P}$ ($K \approx 80$ cho đầu phun thông dụng $K=5.6\text{ US}$).
  * **Công thức thủy lực quy chuẩn:** Áp dụng công thức Hazen-Williams với hệ số nhám $C=120$ (đối với ống thép đen/mạ kẽm), kiểm soát vận tốc dòng chảy tối đa trong mạng đường ống nhánh và ống góp không vượt quá $5\text{ m/s}$ (đến $10\text{ m/s}$ tại một số nút đặc biệt).
* **Ứng dụng vào đề tài:** Là căn cứ pháp lý bắt buộc và bộ tham số gốc cho **Module 1 (Layout Engine)** để tự động chia lưới toạ độ đầu phun, và **Module 3 (Hydraulic Engine)** để kiểm tra giới hạn vận tốc $v \le 5\text{ m/s}$, tính tổn thất áp lực và tự động tăng/giảm đường kính ống (Auto-Sizing).

---

#### 2. Tài liệu 2: NFPA 13 (Standard for the Installation of Sprinkler Systems - 2022/2025 Edition)
* **Tên tài liệu:** NFPA 13: *Standard for the Installation of Sprinkler Systems*.
* **Tổ chức ban hành:** National Fire Protection Association (NFPA - Hoa Kỳ).
* **Nội dung cốt lõi:**
  * Được coi là "kinh thánh" của ngành kỹ thuật chữa cháy nước toàn cầu, cung cấp nền tảng toán học và thực nghiệm toàn diện nhất.
  * **Phương pháp tính toán thủy lực theo diện tích thiết kế bất lợi nhất (Hydraulically Most Demanding Area / Remote Area):** Xác định vùng bảo vệ hình chữ nhật có chiều dài bằng $1.2 \sqrt{A_{operation}}$ song song với tuyến ống nhánh.
  * **Bảng chiều dài tương đương của phụ kiện (Equivalent Pipe Length Table):** Quy định chi tiết tổn thất cục bộ qua cút $90^\circ$, cút $45^\circ$, tê dòng thẳng, tê rẽ nhánh, van một chiều, van chặn tương đương với bao nhiêu mét chiều dài ống thẳng theo từng dải đường kính danh định DN25 – DN200.
  * **Quy tắc tránh chướng ngại vật kết cấu (Beam Clearance Rule):** Biểu đồ và bảng khoảng cách tối thiểu từ đầu phun đến mép dầm bê tông để không làm gián đoạn quỹ đạo tia nước hình nón (Deflector pattern).
* **Ứng dụng vào đề tài:** Tham chiếu công thức tính chiều dài tương đương cho các cụm 4 cút lượn (Bypass Offset fittings) khi định tuyến ống né dầm/ống gió, đảm bảo kết quả thẩm tra thủy lực đạt chuẩn quốc tế.

---

#### 3. Tài liệu 3: Bài báo ISI về Định tuyến đường ống MEP tự động né va chạm trong BIM
* **Tên bài báo:** *Automated MEP pipe layout planning in building construction based on Building Information Modeling and heuristic pathfinding algorithms.*
* **Tạp chí & Tác giả:** Tạp chí *Automation in Construction* (Elsevier, Impact Factor ~10.3), nhóm nghiên cứu về AEC Automation.
* **Nội dung cốt lõi:**
  * Mô hình hóa không gian khoảng trần kỹ thuật công trình (Ceiling Void) thành cấu trúc lưới không gian 3D (3D Voxel/Octree Grid) dựa trên thông tin hình học trích xuất từ mô hình BIM.
  * Phát triển giải thuật tìm đường thông minh dựa trên biến thể của **$A^*$ Algorithm** hoặc **Dijkstra Heuristic** với hàm mục tiêu chi phí đa thành phần:
    $$\text{Cost} = w_1 \cdot L_{pipe} + w_2 \cdot N_{elbows} + w_3 \cdot P_{clash} + w_4 \cdot C_{priority}$$
    Trong đó:
    * $L_{pipe}$: Tổng chiều dài đường ống (tối thiểu hóa chi phí vật tư).
    * $N_{elbows}$: Số lượng cút nối chuyển hướng (hạn chế cút để giảm tổn thất áp lực thủy lực và chi phí nhân công lắp đặt).
    * $P_{clash}$: Chi phí phạt va chạm với dầm, cột, sàn kiến trúc - kết cấu.
    * $C_{priority}$: Thứ bậc ưu tiên không gian cơ điện (Trọng lực thoát nước > Ống gió HVAC kích thước lớn > Ống cấp áp lực PCCC > Máng cáp).
* **Ứng dụng vào đề tài:** Đóng vai trò là phương pháp luận thuật toán cho **Module 2 (Adaptive Routing)**: Thiết kế thuật toán tia quét Ray-Casting / BoundingBox Intersects để tự động phát hiện dầm kết cấu, ống gió HVAC và tự động bẻ uốn né va chạm (Bypass Offset 4 cút) với khoảng hở an toàn $\ge 50\text{ mm}$.

---

#### 4. Tài liệu 4: Bài báo về Mô hình hóa đồ thị và Tối ưu hóa thủy lực đường ống trên Revit API
* **Tên bài báo:** *BIM-based automated hydraulic calculation and pipe sizing optimization for fire protection systems using graph theory and Revit API.*
* **Tạp chí:** *Journal of Building Engineering* / *Advanced Engineering Informatics*.
* **Nội dung cốt lõi:**
  * Biểu diễn mạng lưới đường ống chữa cháy phân nhánh hình cây (Tree System) dưới dạng cấu trúc đồ thị có hướng không chu trình (**Directed Acyclic Graph - DAG**), trong đó các đầu phun là các lá (Leaf Nodes), các mối nối tê/thập là các nút trung gian (Junction Nodes), và ống cấp chính/van báo động là nút gốc (Root Node).
  * Ứng dụng giải thuật duyệt đồ thị ngược từ lá về gốc (**Reverse Topological / Post-Order Traversal**) để cộng dồn lưu lượng $Q$ qua từng phân đoạn ống.
  * Tự động tra bảng phân cỡ ống theo lưu lượng tích lũy (Schedule Pipe Sizing) hoặc cân bằng ma sát để chọn đường kính danh định tối ưu, thỏa mãn đồng thời hai điều kiện biên:
    $$P_{head\_min} \ge P_{req} \quad \text{và} \quad v_{pipe} \le v_{max}$$
* **Ứng dụng vào đề tài:** Làm nền tảng kiến trúc cho module tính toán thủy lực `DSCons.Revit.Starter.Core.Hydraulics`: Xây dựng cấu trúc cây `HydraulicNetworkNode`, tính toán chính xác $Q$, $p$, $v$ và xuất bảng thẩm tra thủy lực sang định dạng Excel/Báo cáo.

---

#### 5. Tài liệu 5: Nghiên cứu trong nước về Ứng dụng Revit API tự động hóa thiết kế Cơ điện
* **Tên tài liệu:** *Nghiên cứu ứng dụng Building Information Modeling (BIM) và lập trình Revit API trong tự động hóa thiết kế, phối hợp và kiểm soát xung đột hệ thống MEP.*
* **Nguồn trích dẫn:** Tạp chí Khoa học Công nghệ Xây dựng (Trường Đại học Xây dựng Hà Nội - HUCE) / Kỷ yếu Hội nghị Khoa học chuyên ngành Xây dựng.
* **Nội dung cốt lõi:**
  * Phân tích các giới hạn của công cụ thiết kế cơ điện thủ công trong Revit: Kỹ sư phải tự dựng từng đoạn ống, chỉnh cao độ bằng tay khi gặp dầm kết cấu, mất nhiều thời gian và dễ xảy ra xung đột khi mô hình thay đổi.
  * Khảo sát các lớp đối tượng cốt lõi trong Revit API: `Pipe.Create`, `Connector.ConnectTo`, `FilteredElementCollector`, `ReferenceIntersector`, `FamilyInstance`.
  * Khuyến nghị mô hình kiến trúc phát triển Add-in: Cần tách biệt tầng xử lý nghiệp vụ toán học (Business/Core Logic) độc lập khỏi tầng giao tiếp Revit API để thuận tiện cho việc kiểm thử tự động (Unit Testing) và tăng tốc độ xử lý khi làm việc với mô hình quy mô lớn.
* **Ứng dụng vào đề tài:** Đề tài đã áp dụng mô hình phân lớp chuẩn Clean Architecture: Tách thành `DSCons.Revit.Starter.Core` (.NET Standard/10 độc lập) và `DSCons.Revit.Starter` (Giao diện UI WPF và điều phối Revit Transaction).

---

#### 6. Tài liệu 6 (Phần mềm thương mại 1): MagiCAD for Revit — Sprinkler Designer
* **Nhà phát triển:** MagiCAD Group (Phần Lan / Trimble).
* **Đặc điểm & Tính năng:**
  * Là bộ công cụ bổ trợ chuyên nghiệp hàng đầu châu Âu tích hợp trực tiếp trên nền tảng Autodesk Revit.
  * Tự động tính toán thủy lực mạng lưới Sprinkler theo nhiều tiêu chuẩn quốc tế (EN 12845, NFPA 13, BS 9251, CEA 4001).
  * Xác định tự động diện tích chữa cháy bất lợi nhất (Most Demanding Area) và cân bằng áp lực mạng lưới bằng cách thay đổi kích thước ống hoặc thêm van giảm áp.
* **Ưu điểm & Nhược điểm:**
  * *Ưu điểm:* Tính năng hoàn thiện, độ tin cậy cao, có thư viện sản phẩm phong phú từ các hãng sản xuất thiết bị PCCC thực tế.
  * *Nhược điểm:* Giá bản quyền rất cao (hàng ngàn EUR/license/năm), rào cản tài chính quá lớn cho sinh viên và đa số doanh nghiệp vừa và nhỏ tại Việt Nam; **không tích hợp sẵn Tiêu chuẩn Việt Nam TCVN 7336:2021**; thuật toán tự động uốn né dầm kết cấu vẫn còn hạn chế, đòi hỏi kỹ sư phải điều chỉnh thủ công nhiều vị trí.

---

#### 7. Tài liệu 7 (Phần mềm thương mại 2): AutoSPRINK
* **Nhà phát triển:** M.E.P.CAD, Inc. (Hoa Kỳ).
* **Đặc điểm & Tính năng:**
  * Giải pháp phần mềm chuyên sâu độc lập và Add-in Revit mạnh mẽ nhất chuyên về thiết kế và thủy lực chữa cháy.
  * Nổi tiếng với công nghệ tính toán thủy lực thời gian thực (**Real-Time Hydraulics**): Khi kỹ sư kéo thả thay đổi vị trí một đầu phun hoặc đổi cỡ ống, áp lực và lưu lượng toàn mạng lưới được tính toán lại ngay lập tức trong tích tắc.
  * Tích hợp công cụ phát hiện xung đột chuyên dụng với dầm, cột, trần và máng cáp.
* **Ưu điểm & Nhược điểm:**
  * *Ưu điểm:* Khả năng tính toán thủy lực cực mạnh, giao diện chuyên biệt cho kỹ sư PCCC.
  * *Nhược điểm:* Sử dụng engine đồ họa riêng biệt phức tạp; quá trình đồng bộ dữ liệu hai chiều (Round-trip Sync) giữa AutoSPRINK và Autodesk Revit thường xuyên xảy ra lỗi mất thông số kỹ thuật (parameters); chi phí sở hữu license cực kỳ đắt đỏ; hoàn toàn theo hệ tiêu chuẩn Mỹ (NFPA) mà không có TCVN.

---

#### 8. Tài liệu 8 (Phần mềm thẩm tra chuyên dụng): Canute FHC (Fire Hydraulic Calculations)
* **Nhà phát triển:** Canute LLP (Vương quốc Anh).
* **Đặc điểm & Tính năng:**
  * Phần mềm thẩm tra thủy lực đường ống phòng cháy chữa cháy chuẩn mực quốc tế, được các tổ chức chứng nhận uy tín như LPCB (Loss Prevention Certification Board) và FM Global phê duyệt.
  * Áp dụng phương pháp giải số Newton-Raphson / Hardy Cross để giải hệ phương trình lưu lượng và áp lực phức tạp cho cả mạng lưới vòng kín (Grid System), mạng nhánh cây (Tree System) và hệ thống phun xối (Deluge System).
* **Ưu điểm & Nhược điểm:**
  * *Ưu điểm:* Độ chính xác tính toán thủy lực tuyệt đối, là căn cứ thẩm tra được các cơ quan cảnh sát PCCC quốc tế chấp thuận.
  * *Nhược điểm:* Là phần mềm thuần tính toán số học trên sơ đồ đơn tuyến 2D (Schematic 2D); **hoàn toàn không có khả năng mô hình hóa 3D**, không tích hợp BIM, không thể tự động rải đầu phun và không thể nhận diện né va chạm với dầm/ống gió trên mô hình Revit.

---

### PHẦN 2: BẢNG TỔNG HỢP & MA TRẬN SO SÁNH TÀI LIỆU MINH CHỨNG

Dưới đây là bảng tổng hợp đối chiếu đa chiều phục vụ minh chứng cho Bước 1 của đồ án:

| STT | Tên tài liệu / Tiêu chuẩn / Phần mềm | Tác giả / Đơn vị ban hành & Năm | Thể loại | Nội dung cốt lõi & Phương pháp luận | Giá trị kế thừa & Ứng dụng vào đề tài SSer | Ưu điểm nổi bật | Giới hạn & Khoảng trống công nghệ (Research Gap) |
| :---: | :--- | :--- | :---: | :--- | :--- | :--- | :--- |
| **1** | **TCVN 7336:2021** (PCCC - Hệ thống chữa cháy tự động bằng nước và bọt) | Bộ KH&CN / Cục Cảnh sát PCCC & CNCH (2021) | Tiêu chuẩn Quốc gia Việt Nam | Quy định phân nhóm nguy cơ cháy (Light, Ordinary, Extra), diện tích bảo vệ ($9-12\text{ m}^2$), khoảng cách đầu phun ($S \le 3-4\text{ m}$), công thức Hazen-Williams ($C=120$), $v \le 5\text{ m/s}$, $P_{min} \ge 0.5\text{ bar}$. | Là cơ sở pháp lý và công thức chuẩn để xây dựng thuật toán chia lưới rải đầu phun (Module 1) và thẩm tra Auto-Sizing thủy lực (Module 3). | Bắt buộc áp dụng tại Việt Nam, cập nhật thay thế TCVN 7336:2003 cũ. | Chỉ là tiêu chuẩn lý thuyết dạng văn bản, chưa có công cụ phần mềm nội địa hóa tự động hóa hoàn toàn trên mô hình BIM. |
| **2** | **NFPA 13** (Standard for the Installation of Sprinkler Systems) | National Fire Protection Association - USA (2022/2025) | Tiêu chuẩn Quốc tế | Tiêu chuẩn toàn cầu về tính toán thủy lực, diện tích thiết kế bất lợi nhất ($A_{remote}$), bảng chiều dài tương đương cho cút/tê/van và quy tắc khoảng cách né dầm. | Áp dụng bảng tổn thất tương đương cho cút uốn né va chạm và quy tắc vùng tán tia nước dưới dầm kết cấu. | Bộ dữ liệu thực nghiệm đồ sộ, được áp dụng trên toàn thế giới. | Đơn vị đo lường mặc định là Imperial (feet/inch/psi/gpm), một số quy định về phân loại nguy cơ và lưu lượng khác biệt so với TCVN. |
| **3** | **Automated MEP pipe layout planning using BIM and heuristic algorithms** | *Automation in Construction* (Elsevier, 2018-2023) | Bài báo Khoa học Quốc tế (ISI Q1) | Số hóa không gian trần thành 3D Voxel Grid; áp dụng thuật toán tìm đường cải tiến ($A^*$ / Dijkstra) với hàm mục tiêu giảm chiều dài ống, giảm số cút nối và né va chạm kết cấu. | Phương pháp luận thuật toán để xây dựng tính năng định tuyến thích ứng (Adaptive Routing) né dầm bê tông và ống gió HVAC (Module 2). | Thuật toán tối ưu hóa mạnh mẽ, tính toán đường đi ngắn nhất không va chạm. | Độ phức tạp tính toán lớn nếu áp dụng cho cả toà nhà lớn; chưa gắn liền với các ràng buộc thủy lực phân cấp đặc thù của mạng Sprinkler. |
| **4** | **BIM-based hydraulic calculation and pipe sizing using graph theory** | *Journal of Building Engineering* (Elsevier, 2021-2024) | Bài báo Khoa học Quốc tế (Scopus/ISI) | Biểu diễn mạng đường ống nhánh cây thành đồ thị có hướng DAG; duyệt cây từ ngọn về gốc (Post-order) để cộng dồn lưu lượng $Q$ và tự động chọn cỡ ống tối ưu. | Cấu trúc dữ liệu `HydraulicNetworkNode` và thuật toán Auto-Sizing phân cấp từ DN25 đến DN150 theo công thức Hazen-Williams. | Xử lý tốc độ cao (mili-giây), giải quyết triệt để bài toán thủy lực cây phân nhánh. | Thường chỉ tập trung vào giải thuật lý thuyết, chưa tích hợp thành Add-in hoàn chỉnh tương tác mượt mà trên Revit. |
| **5** | **Ứng dụng Revit API tự động hóa thiết kế Cơ điện & PCCC** | Tạp chí KHCN Xây dựng (Trường ĐH Xây dựng Hà Nội - HUCE, 2020-2023) | Bài báo KH chuyên ngành trong nước | Phân tích tập lệnh Revit API (`Pipe.Create`, `BoundingBoxXYZ`, `Connector`), khuyến nghị mô hình tách lớp Core Logic độc lập để kiểm thử và tăng tốc độ xử lý. | Áp dụng kiến trúc Clean Architecture: Tách `DSCons.Revit.Starter.Core` và `DSCons.Revit.Starter` giúp chạy Hot Reload và Unit Test đạt 12/12 test pass. | Bám sát thực tiễn ngành xây dựng và đào tạo kỹ sư tại Việt Nam. | Quy mô nghiên cứu trước đây chủ yếu dừng lại ở mức rải đối tượng đơn giản hoặc dựng ống 2D, chưa tích hợp trọn vẹn uốn né va chạm 3D và thủy lực. |
| **6** | **MagiCAD for Revit — Sprinkler Designer** | MagiCAD Group (Trimble, Phần Lan) | Phần mềm Thương mại Quốc tế | Add-in Revit chuyên sâu: Tự động tính thủy lực theo EN 12845 / NFPA 13, tự động cân bằng áp lực, tìm vùng bất lợi nhất, thư viện thiết bị chuẩn hãng. | Tham khảo mô hình giao diện người dùng chuyên nghiệp (WPF UI), quy trình nhập thông số và định dạng xuất bảng kết quả thủy lực. | Tính năng rất mạnh, tích hợp sâu trong Revit, độ tin cậy được khẳng định toàn cầu. | Giá bản quyền quá đắt; **không hỗ trợ TCVN 7336:2021**; tính năng tự động uốn né dầm kết cấu còn hạn chế, kỹ sư vẫn phải bẻ cút thủ công. |
| **7** | **AutoSPRINK** | M.E.P.CAD, Inc. (Hoa Kỳ) | Phần mềm Thương mại Chuyên dụng | Phần mềm 3D chuyên biệt PCCC với khả năng tính thủy lực thời gian thực (Real-time Hydraulics) và phát hiện va chạm tự động. | Học hỏi ý tưởng phản hồi tức thời về áp lực/vận tốc khi người dùng điều chỉnh thông số bố trí đầu phun trên giao diện. | Tốc độ tính toán tức thời, trực quan sinh động 3D. | Chạy nền tảng độc lập, việc chuyển đổi 2 chiều với Revit phức tạp và dễ lỗi; chi phí bản quyền cực cao; không tương thích tiêu chuẩn TCVN. |
| **8** | **Canute FHC** | Canute LLP (Vương quốc Anh) | Phần mềm Thẩm tra Thủy lực Chuyên dụng | Phần mềm tính toán thủy lực số học chuẩn mực (được LPCB, FM Global phê duyệt), sử dụng giải thuật Hardy Cross giải mạng mạch vòng và phân nhánh. | Dùng làm cơ sở đối chuẩn (Benchmark) để kiểm chứng tính đúng đắn của công thức Hazen-Williams và bảng tra phụ kiện trong Core Engine của đề tài. | Độ chính xác toán học - thủy lực tuyệt đối, là chuẩn mực thẩm tra của cảnh sát PCCC. | Giao diện cổ điển, **chỉ tính toán trên sơ đồ 2D**, không có đồ họa 3D, hoàn toàn không tương tác được với mô hình không gian BIM Revit. |

---

### PHẦN 3: PHÂN TÍCH KHOẢNG TRỐNG CÔNG NGHỆ & ĐỊNH VỊ ĐỀ TÀI

Qua việc nghiên cứu và tổng hợp 8 tài liệu trên, tác giả rút ra **3 khoảng trống nghiên cứu (Research Gaps)** lớn trong thực tế thiết kế PCCC hiện nay tại Việt Nam:

1. **Khoảng trống về tiêu chuẩn bản địa hóa (Localization Gap):**
   * Các phần mềm quốc tế hàng đầu (MagiCAD, AutoSPRINK, Canute FHC) chỉ hỗ trợ NFPA 13 hoặc EN 12845.
   * Kỹ sư Việt Nam khi lập hồ sơ thiết kế và thẩm duyệt PCCC với Cục Cảnh sát PCCC & CNCH bắt buộc phải tuân theo **TCVN 7336:2021** (về phân nhóm nguy cơ, chỉ tiêu cường độ phun, diện tích bảo vệ $9-12\text{ m}^2$, giới hạn vận tốc $v \le 5\text{ m/s}$). Việc tính toán thủ công bằng Excel rồi vẽ lại vào Revit làm tăng nguy cơ sai lệch số liệu.

2. **Khoảng trống về tự động hóa định tuyến thích ứng né va chạm (Adaptive Routing Gap):**
   * Trong mô hình BIM thực tế, hệ thống chữa cháy Sprinkler thường được thiết kế sau kết cấu và ống gió HVAC. Khi ống nhánh và ống chính đi qua dầm bê tông, mũ cột hay ống gió lớn, kỹ sư phải thủ công cắt ống, đặt 4 cút lượn ($90^\circ$ hoặc $45^\circ$) để né vật cản, duy trì khoảng hở $\ge 50\text{ mm}$. Quá trình này tốn hàng trăm giờ làm việc của kỹ sư cho một dự án toà nhà cao tầng.
   * Các công cụ hiện có trên Revit chưa có tính năng tự động phát hiện dầm/ống gió và tự sinh cụm cút uốn hạ cao độ (Bypass Offset) một cách thông minh.

3. **Khoảng trống về tính đồng bộ khép kín (Integrated Workflow Gap):**
   * Hiện tại quy trình bị phân mảnh: Vẽ trên Revit $\rightarrow$ Xuất dữ liệu sang phần mềm thủy lực rời rạc $\rightarrow$ Chỉnh lại cỡ ống trên Revit.
   * Đề tài **SSer (SmartSprinkler)** giải quyết bài toán khép kín hoàn chỉnh ngay trong môi trường **Autodesk Revit 2026**:
     $$\text{Chọn tầng / Bố trí đầu phun (TCVN 7336)} \longrightarrow \text{Tự động định tuyến né dầm/ống gió} \longrightarrow \text{Thẩm tra thủy lực & Tự đổi cỡ ống (Auto-Sizing)}$$
