# Giai đoạn 4: đường thế hệ 2 (bản thiết kế)

## Quyết định của Anh NiK (28/11)
- Vẽ đường cong bằng **ba chạm**: điểm đầu, điểm uốn, điểm cuối.
- Nhà dọc đường cong **theo ô, xoay mặt ra đường** (giữ lưới cho mô phỏng).
- 4a có đủ 4 loại: **đường nhỏ 2 làn**, **đại lộ 4 làn**, **đường một chiều**, **đường có hàng cây** (đắt hơn, tăng giá trị đất ven đường).
- Chưa làm code: chờ Anh NiK thử demo.5 trên máy trước.

## Hiện trạng
- Đường là **ô lưới**: `CityCell.Type = Road`, `RoadGraph` giữ hướng nối 4 phía mỗi ô (`RoadConnections`). Cầu là ô đường trên nước (`BridgeAxis`), dốc là ô đường trên ô sườn (`RoadGrade`).
- Mọi hệ thống đọc đường qua lưới: lối vào nhà (`GetAccess`), mạng điện/nước (thành phần liên thông của ô đường), kẹt xe (theo thành phần), tìm đường BFS (`TryFindPath`), xe và người đi theo đường cong bậc 2 trong từng ô, vẽ đường theo ô (`CityChunkRenderer`).
- Save lưu ô; đường không có dữ liệu riêng.

## Hướng kỹ thuật: "đoạn cong nằm trên lưới"
Giữ lưới làm nguồn sự thật cho mô phỏng, thêm một lớp **đoạn đường** (segment) chỉ để vẽ và cho xe chạy:
- **Đoạn** = nút đầu, nút cuối, điểm điều khiển (cong bậc 2), loại đường. **Nút** = giao lộ hoặc đầu cụt.
- Mỗi đoạn được **rải xuống lưới**: các ô nó đi qua thành ô đường như hôm nay (đủ ô liền 4 hướng). Nhờ vậy lối vào nhà, điện nước, kẹt xe, tìm đường, ngân sách **không phải viết lại**.
- Vẽ: đường vẽ theo đoạn (dải lưới uốn theo đường cong), giao lộ vẽ theo nút; ô đường do đoạn rải xuống không vẽ theo ô nữa. Đường cũ (không có đoạn) vẫn vẽ kiểu ô.
- Xe/người: đi theo tuyến ô như cũ (BFS), nhưng vị trí lấy trên đường cong của đoạn chứa ô đó.
- Nhà dọc đường cong: vẫn đặt theo ô, xoay mặt về phía đoạn gần nhất (CS2 cũng chia lô theo lưới cục bộ; lưới toàn cục là đánh đổi để giữ mô phỏng).

Cách này rủi ro thấp: nếu lớp đoạn hỏng, game vẫn chạy bằng lưới.

## Các bước (mỗi bước build được, chơi được)
| Bước | Nội dung | Cỡ | Ai (đề xuất) |
|---|---|---|---|
| 4a | **Loại đường trên lưới**: nhỏ (2 làn), đại lộ (4 làn, sức chứa ×2, đắt hơn, giá trị đất nhà ở kém hơn), một chiều (tìm đường xét chiều đi), có hàng cây (đắt hơn, +giá trị đất). Lưu loại mỗi ô (save 16). Kẹt xe tính theo sức chứa từng loại. Vẽ khác nhau. | M | Codex |
| 4b | **Lớp đoạn + rải xuống lưới**: dữ liệu đoạn/nút, rải ô, xóa đoạn → xóa ô, hoàn tác, save (đoạn lưu kèm ô, save 17). Chưa có công cụ vẽ cong: đường kéo hôm nay tạo đoạn thẳng. Test: rải đúng ô, liền mạch, save/tải. | M | Claude |
| 4c | **Công cụ vẽ cong**: chạm điểm đầu, kéo/chạm điểm uốn, chạm điểm cuối (giống 2 chạm hiện có), bám nút gần, bán kính tối thiểu, tự cắt thành giao lộ khi cắt nhau, xem trước giá. | M | Claude |
| 4d | **Vẽ đường cong + giao lộ**, xe/người chạy theo đoạn, nhà xoay theo đoạn. | L (nặng nhất) | Codex |
| 4e | Chuyển save cũ: gộp các dãy ô đường thẳng giữa hai giao lộ thành đoạn (để đường cũ cũng vẽ đẹp). Không bắt buộc: thiếu bước này đường cũ vẫn vẽ kiểu ô. | S | Claude |

Đường cao tốc và nút giao khác mức (cầu vượt) **để sau giai đoạn 5** (cần giao thông thật mới có ý nghĩa).

## Rủi ro
- 4d chạm vẽ đường, xe, người, nhà: dễ lệch giữa hình vẽ và ô thật (xe chạy lệch mép, nhà cách đường). Cần test máy sau 4d.
- Đường cong rải xuống lưới thành "bậc thang" ô: nhà dọc đường cong sẽ thưa hơn đường thẳng. Chấp nhận được, ghi lại.
- Hiệu năng iPhone 11: lưới đường cong nhiều tam giác hơn ô; giới hạn số đoạn chia mỗi đường cong theo độ cong.
- Save 16/17: save cũ phải tải được (đường thành loại nhỏ, không có đoạn).
