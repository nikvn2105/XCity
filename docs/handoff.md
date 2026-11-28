# Bàn giao: dự án XCity (cập nhật 28/11/2026)

## Quy tắc làm việc với Anh NiK (bắt buộc)
1. Gọi là "Anh NiK". Trả lời ngắn gọn, đủ ý, bằng tiếng Việt.
2. Không tự ý làm khi chưa có lệnh rõ ràng. Hỏi trước (dùng hộp câu hỏi có lựa chọn khi có nhiều phương án).
3. Lệnh sai, không hợp lý hoặc quá sớm so với tiến độ: mạnh dạn nói lại.
4. Ưu tiên "móng chắc" (ổn định, sửa lỗi) trước tính năng nâng cao. Thay đổi nhỏ nhất mà trọn vấn đề.
5. Cuối mỗi câu trả lời ghi rõ những gì đã bỏ qua hoặc chưa kiểm tra.
6. Chỉ test trên iPhone khi cần. Không để Anh NiK dán token/API key vào khung chat.
7. Phân vai: Claude soạn prompt, xem lại, gộp, làm việc vừa và nhỏ; Codex làm việc nặng trong worktree riêng, mỗi việc một nhánh. Anh NiK không còn máy có Unity: mọi kiểm tra là `dotnet test`, CompileCheck và bản build trên iPhone.

## Dự án
- Game xây thành phố tinh thần Cities: Skylines 2 cho iPhone (máy test iPhone 14 Pro Max; máy thấp nhất cần chạy tốt: iPhone 11). Không lên App Store; IPA chưa ký, cài bằng xsign.
- Unity 2022.3.47f1, built-in pipeline, C# 9. Dựng hoàn toàn bằng code (`XCityBootstrap` tự dựng mọi thứ, cảnh `Main.unity` trống).
- Repo github.com/nikvn2105/XCity. Nhánh làm việc của Claude: `claude/trusting-einstein-1p5whu`. Lộ trình: `docs/roadmap.md`. Cân bằng kinh tế: `docs/economy-report.md`.

## Kiểm tra bắt buộc trước khi đẩy
- `dotnet test Tests/XCity.Simulation.Tests/XCity.Simulation.Tests.csproj` (hiện 94 test). File mô phỏng mới phải thêm tay vào `.csproj` của test.
- `dotnet build Tests/XCity.CompileCheck/XCity.CompileCheck.csproj` (biên dịch mọi script game với thư viện Unity tham chiếu). Không bao gồm `Assets/Editor/` (script đặt icon app).
- Hai test "dấu vân tay" trong `UtilityPerfTests` ghi cứng tiền và trạng thái của một thành phố lớn: mỗi lần đổi kinh tế có chủ ý phải ghi lại giá trị mẫu (in ra rồi chép vào), kèm chú thích lý do; số ngẫu nhiên (`randomState`) phải giữ nguyên nếu tăng trưởng không đổi.

## Build IPA
1. Đẩy lên `main` → GitHub Actions `build-ios-ipa.yml`: bước "Build Unity Xcode Project" (~3 phút) tạo artifact `XCity-XcodeProject`. Bước đóng gói trên Mac luôn xếp hàng (GitHub không cấp máy Mac) — bỏ qua.
2. Anh NiK chạy Codemagic workflow `ios-unsigned-ipa`: lấy artifact Xcode mới nhất, build, xuất `XCity-unsigned-ipa.zip`.
3. Codex đang làm workflow build APK (Android) — chưa có trong lộ trình.
- Workflow "Cleanup Old Workflow Runs" giữ 2 lần chạy mới nhất mỗi workflow.

## Trạng thái theo lộ trình
- **Giai đoạn 0–1 (nền, giao diện): xong**, chờ Anh NiK chơi thử xác nhận.
  - Giao diện tối trong mờ: `UiTheme` (màu, cỡ chữ, khung bo góc 9-slice, bóng), `UiText` (mọi chữ tiếng Việt), `UiIcon` + `Assets/Resources/XCityIcons.ttf` (Material Icons, subset — thêm biểu tượng phải cắt lại font).
  - Thanh trên 3 khối nổi (tiền → chạm mở ngân sách, dân, R/C/I, ngày giờ, tốc độ, nút biểu tượng); thanh công cụ dưới nhớ công trình theo mục, hộp thẻ; bảng thông tin bên phải (Đến gần, Phá bỏ); thông báo nổi; nút xoay 45°.
  - Menu chính + 3 ô lưu (save cũ tự chuyển sang ô 1); hướng dẫn thẻ nhiệm vụ 5 bước; logo chữ X kính + icon app (`Assets/Editor/XCityAppIcon.cs`); hoạt ảnh mở game (camera xoay quanh thành phố gần nhất).
  - Đường chạm hai điểm (chữ L), xem trước khi kéo, 3 kiểu ngoại hình mỗi công trình.
- **Giai đoạn 2 (chiều sâu lối chơi): xong phần lớn.**
  - 2a thuế 0–20% từng khu, kinh phí 50–150% từng dịch vụ, bảng ngân sách (save 11).
  - 2b giá trị đất + 3 loại công viên + lớp phủ (save 12); thuế nhà ở/thương mại nhân theo giá trị đất.
  - 2c mật độ theo loại nhà: vừa cần đất ≥ 30, cao ≥ 60; công nghiệp không xét; không xuống cấp.
  - 2d rác: bãi rác, lò đốt, kinh phí rác, lớp phủ; ân hạn dưới 300 dân (save 13).
  - 2e cân bằng: phạt thuế cao (−4 nhu cầu, −2 hài lòng mỗi điểm trên 10%), thu giảm dần trên 12%, trạm nước 80 ô, chi phí hành chính (dân²/20.000, từ 10.000 dân là 0,5/dân).
- `codex/post-processing` (hiệu ứng hình ảnh) để dành giai đoạn 7, chưa gộp.

## Còn mở / rủi ro
1. Nhiều tính năng mới chưa chạy trên máy thật (ngân sách, công viên, rác, mật độ, hoạt ảnh mở game, icon app). Script đặt icon chưa từng chạy trong build.
2. Cân bằng: lãi cuối game vẫn cao, một phần do khung đo chỉ xây 1 công trình mỗi loại dịch vụ; nước nghẽn ở thành phố lớn xa bờ; điện là khoản chi lớn nhất. Chi tiết: `docs/economy-report.md`.
3. 19 nhánh cũ đã gộp (codex/*, gemini/*, claude/phase-a-polish) chưa xóa được: phiên Claude không có quyền xóa nhánh, Anh NiK xóa tay.

## Quy tắc giao việc cho Codex
- Mỗi việc một nhánh tách từ nhánh/commit được chỉ định, worktree riêng. Ghi rõ file được sửa và file không được đụng (khi Claude làm song song).
- Bắt buộc `dotnet test` và CompileCheck đạt. Đổi schema save phải nêu số phiên bản và giữ save cũ tải được.
- Claude xem lại diff, gộp, gỡ xung đột, chạy kiểm tra, rồi mới đẩy `main` khi Anh NiK cho phép.
