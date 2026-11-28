# XCity
XCity — 100% Code-Driven Unity City-Builder for iOS (Cities: Skylines style)

## Bản demo cộng đồng

Đẩy một tag dạng `v0.1.0-demo.3` (từ `main`): workflow **Demo release (APK + IPA)**
build APK và IPA hoàn toàn trên GitHub (IPA trên máy Mac của GitHub), rồi tạo
pre-release có `XCity-v0.1.0-demo.3.apk`, `XCity-v0.1.0-demo.3-unsigned.ipa` và
hai nút tải. IPA cần được ký trước khi cài lên iPhone.
Tạo tag trên web: Releases → Draft a new release → tag mới từ `main` → tích pre-release → **Publish release** (workflow dùng lại release đó). Hai workflow build riêng (iOS, Android) chỉ chạy khi bấm **Run workflow**.

Dự phòng: nếu máy Mac của GitHub kẹt hàng chờ quá 20 phút hoặc lỗi, workflow tự
nhờ Codemagic biên dịch IPA từ cùng dự án Xcode. Đường dự phòng cần hai secret
của repo (Settings → Secrets and variables → Actions): `CODEMAGIC_API_TOKEN`
(Codemagic → Settings → Integrations → Codemagic API) và `CODEMAGIC_APP_ID`
(đoạn mã trong địa chỉ `codemagic.io/app/<APP_ID>`).

Build từ nhánh dùng phiên bản trong Unity và mã commit ngắn, ví dụ
`XCity-v0.1.0-f324ea5.apk`.
