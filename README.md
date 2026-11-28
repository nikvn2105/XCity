# XCity

[![Tải bản demo mới nhất](https://img.shields.io/github/v/release/nikvn2105/XCity?include_prereleases&label=T%E1%BA%A3i%20demo&style=for-the-badge&color=1565C0)](https://github.com/nikvn2105/XCity/releases)

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

## Sao lưu Google Drive (tùy chọn)
Mỗi bản demo, workflow chép mã nguồn (zip), APK và IPA vào `XCity-backup/<tag>/` trên Drive nếu repo có secret `RCLONE_CONFIG`:
1. Trên máy tính: cài rclone (rclone.org/downloads), chạy `rclone config` → `n` (new) → tên **`drive`** → loại `drive` → để trống client id/secret → scope `1` (full) hoặc `3` (file) → đăng nhập Google trong trình duyệt.
2. Chạy `rclone config show drive`, sao chép toàn bộ khối `[drive] ...`.
3. GitHub → Settings → Secrets and variables → Actions → New secret: tên `RCLONE_CONFIG`, dán khối đó. Không gửi khối này cho ai.
