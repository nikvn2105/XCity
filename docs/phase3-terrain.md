# Giai đoạn 3: địa hình và bản đồ (bản thiết kế, chờ duyệt)

## Quyết định của Anh NiK (28/11)
- Đồi núi kiểu **bậc thềm**: 3–4 tầng phẳng; nhà xây trên mặt phẳng từng tầng; sườn giữa hai tầng chỉ làm đường dốc.
- Sông và ao: **nước ngọt cho trạm bơm**, **phải xây cầu để vượt sông**, **tăng giá trị đất ven sông**, **ô nhiễm sông**.
- Bản đồ **sinh ngẫu nhiên theo hạt giống** (seed).
- Save cũ **giữ nguyên trên bản đồ Quần đảo** hiện tại.

## Hiện trạng
`WorldTerrain` là một hàm cố định: quần đảo, mặt đất phẳng. `LandHeight` chỉ dùng để phân biệt đất/nước và tô màu cỏ. Mọi thứ khác (đặt nhà, cảng, trạm bơm, vẽ đất, xe, công dân, bản đồ thế giới) gọi thẳng `WorldTerrain.IsLand` / `LandHeight`.

## Hướng kỹ thuật
- **Bản đồ là dữ liệu tính trước:** mỗi bản đồ sinh ra một lưới 512 × 512 gồm loại ô (biển / sông / hồ / đất) và tầng cao (0–3). Khoảng 0,5 MB, tính một lần khi mở thành phố. `WorldTerrain` đọc từ lưới này thay vì công thức.
- **Seed 0 = Quần đảo hiện tại**, sinh lại đúng từng ô như cũ (có test so sánh toàn bản đồ). Save cũ không có seed → seed 0. Save mới lưu `mapSeed` và `mapStyle` (schema 14).
- **Bộ sinh ngẫu nhiên có kiểm tra chất lượng:** sinh theo seed + kiểu (Cân bằng / Nhiều nước / Nhiều đồi), rồi kiểm tra: ≥ 50% đất, có ít nhất một con sông, có một khu đất phẳng ≥ 40 × 40 ô sát nước ngọt để khởi đầu. Không đạt thì tự thử seed kế tiếp. Nhờ vậy tránh được bản đồ xấu — rủi ro lớn nhất của cách ngẫu nhiên.
- **Bậc thềm:** độ cao bề mặt = tầng × một bước cố định. Một hàm chung `SurfaceY(x, z)` thay cho mọi chỗ đang giả định mặt đất ở độ cao 0 (đất, nhà, đường, xe, công dân, công trường, khói, cháy, lớp phủ, ô xem trước). Ô giáp tầng khác là sườn: chỉ đặt được đường (đường dốc). Vẽ vách đá thẳng đứng giữa các tầng.
- **Sông:** một đường uốn lượn từ vùng cao ra biển, khắc thành ô nước có hướng chảy; ao/hồ là lòng chảo trên tầng cao. Trạm bơm và xử lý nước thải đặt được cạnh sông/hồ; cảng chỉ cạnh biển. Cầu dùng công cụ có sẵn.
- **Ô nhiễm sông:** xử lý nước thải và công nghiệp cạnh sông làm bẩn một đoạn hạ lưu; trạm bơm lấy nước bẩn → khu được cấp nước giảm hài lòng (như thiếu nước một nửa). Tính lại khi có thay đổi, không lưu vào save.

## Các bước (mỗi bước build được, chơi được)
| Bước | Nội dung | Cỡ | Ai |
|---|---|---|---|
| 3a | Hệ thống bản đồ: lưới tính trước, seed 0 = Quần đảo, save `mapSeed`/`mapStyle`, hộp "Thành phố mới" chọn kiểu + seed (nút ngẫu nhiên) + bản đồ thu nhỏ xem trước, bộ sinh bản đồ **phẳng** (bờ biển, đảo đa dạng) + kiểm tra chất lượng | M | Claude |
| 3b | Sông và hồ: khắc sông/hồ, trạm bơm nước ngọt, giá trị đất ven nước, kiểm tra luôn có sông | M | Codex |
| 3c | Bậc thềm: tầng cao, sườn chỉ cho đường, `SurfaceY` ở mọi hệ thống, vách đá, đường dốc | L (nặng nhất) | Codex |
| 3d | Ô nhiễm sông: hướng chảy, nguồn bẩn, nước bẩn ở trạm bơm, lớp phủ — **xong** (bẩn 60 ô hạ lưu, hồ tối đa 150 ô; nước bẩn −5 hài lòng) | S–M | Claude |

Thứ tự 3a → 3b → 3c → 3d: mỗi bước dựa trên bước trước; 3c chạm nhiều file nhất nên làm khi bản đồ và sông đã ổn.

## Rủi ro
- 3c chạm gần như mọi hệ thống vẽ; dễ có vật thể lơ lửng hoặc chìm. Cần test trên máy sau bước này.
- Hiệu năng iPhone 11: lưới tính trước nhẹ; vách đá thêm tam giác cho mỗi chunk — đo lại FPS.
- Bản đồ ngẫu nhiên có thể khó chơi dù qua kiểm tra; cần chơi thử vài seed.
- Giai đoạn 4 (đường cong) sẽ phải làm đường đi theo độ cao — bậc thềm giữ việc này đơn giản (đường chỉ dốc ở ô sườn).
