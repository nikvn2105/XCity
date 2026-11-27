# Bàn giao: dự án XCity (cập nhật 27/11/2026)

## Quy tắc làm việc với anh NiK (bắt buộc)
1. Gọi là "Anh NiK". Trả lời ngắn gọn, đủ ý, bằng tiếng Việt.
2. Không tự ý làm khi chưa có lệnh rõ ràng. Hỏi trước.
3. Lệnh sai, không hợp lý hoặc quá sớm so với tiến độ: mạnh dạn nói lại.
4. Ưu tiên "móng chắc" (ổn định, sửa lỗi) trước tính năng nâng cao.
5. Thay đổi nhỏ nhất mà giải quyết trọn vấn đề. Cuối mỗi câu trả lời ghi rõ những gì đã bỏ qua hoặc chưa kiểm tra.
6. **Claude làm quân sư**: soạn prompt, xem lại nhánh, gộp. Việc nặng giao Codex và Gemini.

## Dự án
- Game xây thành phố kiểu Cities: Skylines 2 cho iPhone (máy test: iPhone 14 Pro Max).
- Unity 2022.3.47f1, built-in render pipeline. Toàn bộ dựng bằng code: không prefab, không texture, cảnh `Main.unity` trống, `XCityBootstrap` tự dựng mọi thứ.
- Repo: github.com/nikvn2105/XCity, nhánh chính `main` (`104200c`).

## Máy làm việc (Windows)
- Thư mục chính: `C:\Users\ezycloudx-admin\Desktop\XCity`. Codex dùng worktree riêng `..\XCity-codex`.
- Đã cài Unity 2022.3.47f1 (Hub) và .NET 8 SDK (`C:\Program Files\dotnet\dotnet.exe`; PATH có thể chưa nhận).
- Lệnh kiểm tra:
  - `dotnet test Tests/XCity.Simulation.Tests/XCity.Simulation.Tests.csproj` (hiện 53 test đạt)
  - `dotnet build Tests/XCity.CompileCheck/XCity.CompileCheck.csproj`
  - Unity thật (phải đóng Editor trước): `"C:\Program Files\Unity\Hub\Editor\2022.3.47f1\Editor\Unity.exe" -batchmode -quit -projectPath . -logFile unity.log`, rồi tìm "error CS" trong log.
- Save game: `%USERPROFILE%\AppData\LocalLow\nikstudiovn\XCity\xcity-save.json` (kèm `.bak`).

## Build IPA
- GitHub Actions `build-ios-ipa.yml`: Unity build trên Linux (~3 phút), đóng gói trên `macos-15`. Hàng chờ Mac thường rất lâu.
- **Codemagic** (thay cho bước Mac): `codemagic.yaml`, workflow `ios-unsigned-ipa`.
  - Codemagic tải artifact `XCity-XcodeProject` mới nhất từ GitHub, chạy `xcodebuild`, rồi xuất `XCity-unsigned.ipa`.
  - Cần biến `GITHUB_TOKEN` trong nhóm `github` (token fine-grained, chỉ repo XCity, Actions: Read). Đã cài.
  - Artifact Xcode chỉ còn khi job Mac trên GitHub chưa chạy xong (sau đó bị xóa) và giữ tối đa 3 ngày.
  - Build #1 đã khởi chạy, **chưa biết kết quả**.
- IPA chưa ký: ký và cài bằng Sideloadly hoặc AltStore.

## Đã có trên main
- **Mô phỏng:**
  - Hoàn tác, save schema 9 (`peakPopulation`).
  - Nhu cầu R/C/I, việc làm, dịch vụ, hạnh phúc, nước và nước thải, vay vốn, phá sản.
  - Mở khóa theo dân số cao nhất từng đạt: mốc 500, 2.000, 5.000.
- **Hiệu năng (Codex):**
  - Tính lại điện, nước, nước thải: 11 ms → 0,84 ms.
  - Một lượt mô phỏng: 10,6 ms → 3,1 ms (thành phố ~90.000 ô, đo trên PC).
- **Đồ họa (Gemini):** bầu trời chuyển màu, bóng chân tường, bờ biển có cát và bọt, nước động. Zoom gần tối đa = 7.
- **Giao diện:**
  - Cài đặt: 4 mức đồ họa, 30/60/120 FPS, hiện FPS.
  - Lớp phủ.
  - Gợi ý và cảnh báo khi đặt công trình không sát mép nước.
  - Thông báo khi save bị gỡ công trình.
  - Hướng dẫn lần đầu 5 bước, có nút "Bỏ qua", lưu trạng thái bằng PlayerPrefs.
- **Tài liệu:** `docs/fps-graphics-polish.md` (FPS trước/sau, đo trên CPU), `docs/economy-report.md` (báo cáo kinh tế, 4 đề xuất).

## Đã kiểm tra
- Unity biên dịch sạch các bản trước đó, và đã chơi thử trong Editor. Riêng hướng dẫn lần đầu chưa thử trong Unity.
- "Xây thành phố mới" sau phá sản: chạy đúng, không lỗi, không nhân đôi bootstrap. Đã sửa rò vật liệu bầu trời.
- Thành phố chỉ dùng công trình mốc 0 vượt được 500 dân.

## Còn mở / rủi ro
1. **Chưa ai chạy trên iPhone.** Đây là rủi ro lớn nhất. Cần đo:
   - FPS ở 4 mức đồ họa, mỗi mức đo khi zoom gần và zoom xa.
   - Zoom xa có giật không.
   - Ở mức Thấp, chữ có mờ không.
   - Máy có nóng sau khoảng 5 phút không.
2. Bảng FPS của Gemini: phần đồ họa mới làm giảm 11–49%, nặng nhất khi zoom xa ở mức Cao (do nước và bầu trời). Phải xác nhận trên iPhone.
3. Hướng dẫn lần đầu (Gemini) mới qua bước biên dịch, chưa chơi thử.
4. File `.bak` bị tự lưu ghi đè, nên thành phố đã phá sản bị mất. Lỗi nhỏ, chưa sửa.
5. Đề xuất cân bằng kinh tế chưa áp dụng:
   - Giảm thuế nhà ở.
   - Tăng công suất trạm nước từ 40 lên 60.
   - Đẩy mốc 5.000 lên 6.500.
   - Tăng chi phí vận hành bệnh viện và cứu hỏa.
   - Chờ anh chơi thử rồi quyết.

## Kế hoạch
- **A (gần xong):** cài IPA, đo trên iPhone, sửa theo kết quả. Nếu FPS thấp khi zoom xa: giảm sóng nước hoặc bầu trời ở mức Trung bình và Cao.
- **B:** cân bằng kinh tế (theo báo cáo và trải nghiệm chơi thử), chỉnh hướng dẫn lần đầu.
  - Bản đồ ngẫu nhiên có sông và ao: đã có prompt, giao Gemini **sau khi** đo FPS trên iPhone.
  - Prompt yêu cầu: save lưu `mapSeed`, schema 10; seed 0 giữ đúng bản đồ cũ; cảng chỉ đặt cạnh biển.
- **C (khi A và B ổn):** góc nhìn thứ nhất (hoặc camera thấp theo xe), xe buýt, đường cong, tiếng Anh.

## Quy tắc giao việc cho Codex và Gemini
- Mỗi việc một nhánh riêng tách từ `main`, làm trong **worktree riêng**, không làm trong thư mục chính. Lần trước Codex chuyển nhánh ngay trong thư mục chính và làm lạc một commit.
- Ghi rõ danh sách file được sửa. Bắt buộc `dotnet test` và CompileCheck đều đạt. Không đổi schema save nếu không được duyệt.
- Claude xem lại: chỉ sửa đúng file được giao, test đạt, gộp được mà không xung đột. Sau đó mới gộp vào `main`.
