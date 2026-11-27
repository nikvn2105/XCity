# Bảng đo FPS Trước / Sau — Đồ họa Batch 1 (`fb5fd12`)

Báo cáo hiệu năng cho gói nâng cấp đồ họa [fb5fd12](https://github.com/nikvn2105/XCity/commit/fb5fd126428047300ac601889674cb75513f01d4) (bầu trời gradient `XCitySky.shader`, tiếp xúc nền/góc công trình `XCityVertexColor.shader`, bờ biển + `_XCityShoreTex` 512×512 trong `CityChunkRenderer.cs`, mặt nước fragment wave + Fresnel `XCityWater.shader`).

## 1. Điều kiện & môi trường đo

- **Mốc so sánh:**
  - **Trước:** `29a9912` (trước khi gộp `gemini/graphics-polish`)
  - **Sau:** `915c8fd` (đã gộp `fb5fd12` vào `claude/trusting-einstein-1p5whu`)
- **Thành phố thử nghiệm:** `XCityPreviewCity` (cùng bản đồ, cùng dữ liệu ô, `LightingMode.Day`, `_XCityTime = 12.5`).
- **Góc camera cố định:**
  - `ZoomedIn` (cận cảnh ven biển / công trình): `target = (1802, 0.2, 1471)`, `distance = 40`, `pitch = 28°`, `yaw = 210°`
  - `ZoomedOut` (toàn cảnh thành phố 2048 m): `target = (1024, 0, 1024)`, `distance = 2800`, `pitch = 48°`, `yaw = 45°`
- **Phương pháp đo:** Trong cùng quy trình dựng/cập nhật từng khung (`ChunkRenderScheduler.Refresh(_cam, 2)`, `RebuildDirtyChunks(1)` cho địa hình và công trình, `DayNightSystem.Update(0.016f)`, `_cam.Render()`), đồng bộ GPU bằng `ReadPixels(1×1)`, `targetFrameRate = 120`, `vSyncCount = 0`, 1 khung làm nóng + 5 khung đo liên tiếp cho mỗi tổ hợp góc camera × mức đồ họa.
- **Độ phân giải theo cấu hình iPhone 14 Pro Max (`2796×1290` × RenderScale):**
  - Mức 0 (`Thấp`): `1678×774` (`0.60×`, MSAA `1×`)
  - Mức 1 (`Trung bình`): `2097×968` (`0.75×`, MSAA `1×`)
  - Mức 2 (`Cao`): `2516×1161` (`0.90×`, MSAA `2×`)
  - Mức 3 (`Cực cao`): `2796×1290` (`1.00×`, MSAA `4×`)
- **Thiết bị chạy benchmark:** `Unity 2022.3.47f1` (`StandaloneLinux64`) trên GitHub Actions `ubuntu-latest` (OpenGL 4.5 software rasterizer **Mesa `llvmpipe`**). Do chạy rasterizer bằng CPU thay vì GPU phần cứng iPhone, trị số tuyệt đối thấp hơn máy thật — giá trị chính là độ chênh lệch tương đối giữa `29a9912` và `915c8fd`.

## 2. Kết quả đo FPS

### Góc camera 1 — Cận cảnh (`ZoomedIn`)

| Mức đồ họa | Độ phân giải (MSAA) | Trước (`29a9912`) FPS TB | Trước frame TB (ms) | Trước frame chậm nhất (ms) | Sau (`915c8fd`) FPS TB | Sau frame TB (ms) | Sau frame chậm nhất (ms) | Thay đổi FPS |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| **Thấp** (Level 0) | `1678×774` (1×) | 52.8 | 18.95 | 19.6 | 46.9 | 21.33 | 22.4 | -11.2% |
| **Trung bình** (Level 1) | `2097×968` (1×) | 17.6 | 56.68 | 59.3 | 15.7 | 63.58 | 68.5 | -10.8% |
| **Cao** (Level 2) | `2516×1161` (2×) | 7.3 | 137.60 | 138.5 | 6.3 | 159.84 | 161.8 | -13.7% |
| **Cực cao** (Level 3) | `2796×1290` (4×) | 3.3 | 298.95 | 303.3 | 2.8 | 358.43 | 361.3 | -15.2% |

### Góc camera 2 — Toàn cảnh (`ZoomedOut`)

| Mức đồ họa | Độ phân giải (MSAA) | Trước (`29a9912`) FPS TB | Trước frame TB (ms) | Trước frame chậm nhất (ms) | Sau (`915c8fd`) FPS TB | Sau frame TB (ms) | Sau frame chậm nhất (ms) | Thay đổi FPS |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| **Thấp** (Level 0) | `1678×774` (1×) | 54.7 | 18.28 | 18.7 | 47.7 | 20.95 | 21.5 | -12.8% |
| **Trung bình** (Level 1) | `2097×968` (1×) | 32.5 | 30.80 | 31.4 | 23.0 | 43.49 | 44.7 | -29.2% |
| **Cao** (Level 2) | `2516×1161` (2×) | 24.0 | 41.72 | 48.0 | 12.3 | 81.24 | 85.3 | -48.8% |
| **Cực cao** (Level 3) | `2796×1290` (4×) | 5.6 | 179.62 | 184.2 | 3.9 | 256.20 | 261.1 | -30.4% |

## 3. Nhận xét kỹ thuật

1. **Ở mức `Thấp` (`_XCityQuality == 0`):** `XCityWater.shader` bỏ qua tính toán sóng sin nhiều lớp và texture `_XCityShoreTex` phức tạp, giữ mức giảm thời gian khung hình nhỏ (`+2.38 ms` khi cận cảnh, `+2.67 ms` khi toàn cảnh trên `llvmpipe`).
2. **Chi phí tăng ở mức `Trung bình` / `Cao` / `Cực cao` khi nhìn toàn cảnh (`ZoomedOut`):** Trong `ZoomedOut`, phần đại dương (`Ocean`) chiếm diện tích màn hình lớn và vòm trời (`XCitySky.shader`) vẽ toàn màn hình thay cho lệnh `Clear` màu trơn, cộng thêm `XCityWater.shader` sample `_XCityShoreTex` và tính 3 sóng + Fresnel theo từng pixel. Trên software rasterizer CPU (`llvmpipe`) bị nghẽn pixel fill-rate và MSAA 2×/4× rõ rệt nhất ở `Cao` / `Cực cao`.
3. **Frame chậm nhất bám sát frame trung bình:** Khoảng cách giữa `worstMs` và `avgMs` ở cả 8 cấu hình chỉ chênh khoảng `0.5–4.9 ms`, không có hiện tượng giật khung hình (`frame spike`) do rebuild mesh hay cấp phát bộ nhớ trên CPU.
