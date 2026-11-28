# Báo cáo cân bằng kinh tế XCity

Đo trên mô phỏng tại `bbedfae` bằng .NET 8 Release. Mỗi kịch bản bắt đầu từ bản đồ trống, seed 1 và 50.000 tiền. Mọi công trình đều đặt bằng `TrySetCell`, trả đủ chi phí và đáp ứng mốc `PeakPopulation` tại thời điểm xây. Bốn thành phố dùng cùng vị trí gốc (314, 385), đường nối chung và nhà máy điện bổ sung khi số ô tiêu thụ vượt công suất. Không vay, phá dỡ hay sửa save.

| Kịch bản | Kế hoạch xây |
|---|---|
| Nhỏ cân bằng | Xây 15 nhà phố, 6 chợ, 6 xưởng và 3 kho trước lượt 1, rồi giữ nguyên. |
| Nhiều dân, ít việc | Xây 36 nhà phố và 1 chợ trước lượt 1, rồi giữ nguyên. |
| Nhiều công nghiệp | Xây 12 nhà phố, 6 chợ, 12 xưởng và 6 kho trước lượt 1, rồi giữ nguyên. |
| Xây dịch vụ sớm | Khởi đầu với 90 ô dân cư, thương mại và công nghiệp theo cùng tỷ lệ với thành phố nhỏ, thêm một trạm bơm và một trạm xử lý nước thải. Sau mốc 500 dân, nếu quỹ có ít nhất 25.000, thêm 50 ô theo tỷ lệ 5 chung cư, 2 văn phòng, 3 nhà máy mỗi 25 lượt, tối đa 9 đợt. Xây một công trình cho mỗi loại dịch vụ ngay lượt kế tiếp sau khi mở khóa nếu đủ quỹ theo kế hoạch. Mốc 5.000 cũng thử đặt một tháp văn phòng và một trung tâm thương mại. |

Mỗi kịch bản gọi `Tick()` 2.000 lần. Bảng ghi trạng thái sau lượt thứ 100, 200, …, 2.000. Cột **Tick thực tế** sẽ dừng tăng nếu thành phố phá sản. R/C/I là nhu cầu nhà ở, thương mại và công nghiệp trên thang −100 đến +100. **Ròng/tick** đã trừ chi vận hành. Chi phí xây dựng được trừ khỏi tiền mặt khi đặt công trình. Đây là bốn kế hoạch với một seed. Kịch bản dịch vụ cũng có quy mô và nhịp mở rộng khác, nên mức chênh lệch tiền và hạnh phúc phản ánh toàn bộ kế hoạch xây.

## Kết quả và mốc mở khóa

Dấu **—** nghĩa là chưa xảy ra trong 2.000 lượt. Các mốc được ghi khi dân số đỉnh chạm ngưỡng lần đầu.

| Thành phố | 500 dân | 2.000 dân | 5.000 dân | Lượt đạt 100.000 | Lượt đạt 1.000.000 | Quỹ thấp nhất | Phá sản |
|---|---:|---:|---:|---:|---:|---:|---:|
| Nhỏ cân bằng | — | — | — | 240 | — | 44.530 | — |
| Nhiều dân, ít việc | — | — | — | — | — | 20.040 | — |
| Nhiều công nghiệp | — | — | — | 385 | — | 43.720 | — |
| Xây dịch vụ sớm | 23 | 80 | 225 | 83 | 261 | 18.489 | — |

- **Nhiều dân, ít việc** là thành phố duy nhất lỗ đều, 12/tick từ lần đo đầu. Chỉ 9/36 nhà phát triển, với 108 dân, 6 việc và R = −80. Thành phố chưa từng âm quỹ trong khoảng đo. Nếu giữ nguyên mức lỗ và không can thiệp, quỹ sẽ âm ở lượt 3.671 và chạm 40 lượt nợ liên tục ở lượt 3.710.
- **Nhỏ cân bằng** kết thúc với 515.503 tiền, gấp 10,3 lần vốn đầu, dù giữ nguyên ở 180 dân và hạnh phúc 20. **Nhiều công nghiệp** kết thúc với 339.149 tiền, 144 dân và 144 việc. Lực lượng lao động là 86, bằng khoảng 60% số việc. Nhu cầu công nghiệp −44 và thuế doanh nghiệp chỉ 128/tick. Xây thêm xưởng trong kế hoạch này cho lợi ích kém hơn việc đưa thêm dân vào mạng đường có việc làm.
- **Xây dịch vụ sớm** đạt một triệu ở lượt 261 và chốt 14.247.204 tiền với 5.484 dân. Đến lượt 100, thành phố đã có 143.184 tiền và hạnh phúc 70. Đến lượt 2.000, hạnh phúc còn 57. Một cặp trạm nước và nước thải phục vụ cả hai tiện ích cho 21/270 nhà đã phát triển trong bố cục này, và có 21 nhà lên cấp. Sau khi hết các đợt xây, quỹ vẫn tăng 7.617/tick.
- **Mốc 500** tới ở lượt 23 trong kế hoạch chủ động mở rộng. Ở nhịp 1,5 giây/tick, đó là khoảng 35 giây mô phỏng, chưa tính thời gian người chơi bố trí công trình. Ba kịch bản cố định dừng ở 108–180 dân, nên thời gian chờ thêm không tự mở khóa mốc 500. Kết quả cho thấy mốc này nhạy với quy mô xây ban đầu và tỷ lệ việc làm.
- **Mốc 2.000 và 5.000** tới ở lượt 80 và 225, khoảng 2 phút và 5 phút 38 giây mô phỏng trong kế hoạch mở rộng. Gói trường trung học, bệnh viện, cứu hỏa và cảng được đặt ở lượt 81. Đại học, tháp văn phòng và trung tâm thương mại được đặt ở lượt 226. Mốc 5.000 đến khá sớm nếu mục tiêu thiết kế là để nhóm công trình lớn xuất hiện sau một giai đoạn quản lý ngân sách dài hơn. Quỹ đã vượt 100.000 từ lượt 83 và dịch vụ gần như không tạo áp lực chi phí trong kịch bản này.

## Cơ cấu chi phí

Chi xây dựng là tổng tiền thực trả trong 2.000 lượt. Chi vận hành và thuế là số của lượt 2.000.

| Thành phố | Xây đường | Xây điện | Nhà và khu việc làm | Xây dịch vụ | Xây nước/nước thải | Tổng chi xây |
|---|---:|---:|---:|---:|---:|---:|
| Nhỏ cân bằng | 1.700 | 2.000 | 1.770 | 0 | 0 | 5.470 |
| Nhiều dân, ít việc | 2.100 | 2.000 | 1.860 | 0 | 0 | 5.960 |
| Nhiều công nghiệp | 2.000 | 2.000 | 2.280 | 0 | 0 | 6.280 |
| Xây dịch vụ sớm | 34.800 | 23.000 | 64.700 | 9.750 | 1.900 | 134.150 |

| Thành phố | Đường/tick | Điện/tick | Dịch vụ/tick | Nước/nước thải/tick | Thuế nhà ở/tick | Thuế doanh nghiệp/tick | Ròng/tick |
|---|---:|---:|---:|---:|---:|---:|---:|
| Nhỏ cân bằng | 34 | 120 | 0 | 0 | 225 | 165 | +236 |
| Nhiều dân, ít việc | 42 | 120 | 0 | 0 | 135 | 15 | −12 |
| Nhiều công nghiệp | 40 | 120 | 0 | 0 | 180 | 128 | +148 |
| Xây dịch vụ sớm | 696 | 1.380 | 185 | 45 | 6.855 | 3.068 | +7.617 |

**Điện áp đảo chi thường xuyên** trong cả bốn thành phố, chiếm từ 60% đến 78% tổng chi vận hành ở lượt 2.000. Ở thành phố thiếu việc, riêng điện đã dùng 120 trong 150 thuế mỗi lượt. Trong thành phố dịch vụ, đường và điện cộng lại chiếm 2.076/2.306 chi vận hành, khoảng 90%. Dịch vụ và cấp/thoát nước cộng lại chỉ tốn 230/tick. Thuế nhà ở chiếm khoảng 69% tổng thu 9.923/tick ở thành phố này.

## Số liệu mỗi 100 lượt

### Nhỏ cân bằng

| Lượt | Tick thực tế | Tiền | Ròng/tick | Dân | Việc | Hạnh phúc | R | C | I |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 100 | 67103 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 200 | 200 | 90703 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 300 | 300 | 114303 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 400 | 400 | 137903 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 500 | 500 | 161503 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 600 | 600 | 185103 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 700 | 700 | 208703 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 800 | 800 | 232303 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 900 | 900 | 255903 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1000 | 1000 | 279503 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1100 | 1100 | 303103 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1200 | 1200 | 326703 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1300 | 1300 | 350303 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1400 | 1400 | 373903 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1500 | 1500 | 397503 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1600 | 1600 | 421103 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1700 | 1700 | 444703 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1800 | 1800 | 468303 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 1900 | 1900 | 491903 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |
| 2000 | 2000 | 515503 | 236 | 180 | 132 | 20 | 32 | 25 | -6 |

### Nhiều dân, ít việc

| Lượt | Tick thực tế | Tiền | Ròng/tick | Dân | Việc | Hạnh phúc | R | C | I |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 100 | 42840 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 200 | 200 | 41640 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 300 | 300 | 40440 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 400 | 400 | 39240 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 500 | 500 | 38040 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 600 | 600 | 36840 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 700 | 700 | 35640 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 800 | 800 | 34440 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 900 | 900 | 33240 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1000 | 1000 | 32040 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1100 | 1100 | 30840 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1200 | 1200 | 29640 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1300 | 1300 | 28440 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1400 | 1400 | 27240 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1500 | 1500 | 26040 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1600 | 1600 | 24840 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1700 | 1700 | 23640 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1800 | 1800 | 22440 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 1900 | 1900 | 21240 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |
| 2000 | 2000 | 20040 | -12 | 108 | 6 | 20 | -80 | 80 | 100 |

### Nhiều công nghiệp

| Lượt | Tick thực tế | Tiền | Ròng/tick | Dân | Việc | Hạnh phúc | R | C | I |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 100 | 57949 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 200 | 200 | 72749 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 300 | 300 | 87549 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 400 | 400 | 102349 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 500 | 500 | 117149 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 600 | 600 | 131949 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 700 | 700 | 146749 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 800 | 800 | 161549 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 900 | 900 | 176349 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1000 | 1000 | 191149 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1100 | 1100 | 205949 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1200 | 1200 | 220749 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1300 | 1300 | 235549 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1400 | 1400 | 250349 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1500 | 1500 | 265149 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1600 | 1600 | 279949 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1700 | 1700 | 294749 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1800 | 1800 | 309549 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 1900 | 1900 | 324349 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |
| 2000 | 2000 | 339149 | 148 | 144 | 144 | 20 | 77 | 7 | -44 |

### Xây dịch vụ sớm

| Lượt | Tick thực tế | Tiền | Ròng/tick | Dân | Việc | Hạnh phúc | R | C | I |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 100 | 143184 | 3239 | 2524 | 1526 | 70 | 10 | 20 | 3 |
| 200 | 200 | 578690 | 6132 | 4504 | 2806 | 58 | 13 | 15 | 1 |
| 300 | 300 | 1298304 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 400 | 400 | 2060004 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 500 | 500 | 2821704 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 600 | 600 | 3583404 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 700 | 700 | 4345104 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 800 | 800 | 5106804 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 900 | 900 | 5868504 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1000 | 1000 | 6630204 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1100 | 1100 | 7391904 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1200 | 1200 | 8153604 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1300 | 1300 | 8915304 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1400 | 1400 | 9677004 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1500 | 1500 | 10438704 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1600 | 1600 | 11200404 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1700 | 1700 | 11962104 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1800 | 1800 | 12723804 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 1900 | 1900 | 13485504 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |
| 2000 | 2000 | 14247204 | 7617 | 5484 | 3610 | 57 | 19 | 2 | 0 |

## Chạy lại

Chạy lệnh sau từ thư mục gốc của worktree.

```powershell
dotnet test Tests/XCity.Simulation.Tests -c Release --filter FullyQualifiedName~EconomyReportTests --logger "console;verbosity=detailed"
```

Test in lại các bảng và mốc, kiểm tra công trình đặt hợp lệ, và đối chiếu tiền cuối kỳ với chi xây dựng và thu nhập ròng tích lũy.

## Đề xuất thử nghiệm

1. Thử giảm thuế nhà ở từ **1,25 xuống 1,00 mỗi dân/tick** để hãm tích lũy ở thành phố đã ổn định. Với trạng thái cuối của thành phố dịch vụ, chênh lệch tĩnh khoảng **1.371/tick**. Theo dõi lại thời điểm âm quỹ của thành phố thiếu việc vì thuế nhà ở chiếm phần lớn thu nhập của nó.
2. Thử tăng công suất mỗi trạm nước và mỗi trạm nước thải từ **40 lên 60 ô**. Đo lại tỷ lệ nhà có đủ hai tiện ích và số nhà lên cấp trong cùng bố cục. Đồng thời theo dõi thuế, vì nhiều nhà lên cấp hơn sẽ đẩy doanh thu lên.
3. Giữ mốc **500 dân** cho chung cư và trường đầu tiên. Thử mốc **6.500 thay cho 5.000** cho đại học, tháp văn phòng và trung tâm thương mại để kéo dài tiến độ công trình lớn. Cần cho kịch bản mở rộng vượt 6.500 dân khi đo biến thể này.
4. Thử tăng vừa phải chi vận hành nhóm dịch vụ mở ở 2.000 dân, chẳng hạn bệnh viện **35 → 50** và cứu hỏa **24 → 35** mỗi tick. Đây là phép thử sức nặng của quyết định xây dịch vụ. Với cùng trạng thái cuối, hai thay đổi chỉ làm giảm 26/tick, nên đánh giá riêng với đề xuất giảm thuế.

## Đo lại sau giai đoạn 2a–2c (thuế, giá trị đất, mật độ) — chuẩn bị bước 2e

Đo tại nhánh `claude/trusting-einstein-1p5whu` sau commit `5f4323d`, cùng khung báo cáo (`EconomyReportTests`). Thêm kịch bản **Thị trưởng tự động** (`Scenario.Growth`): khởi đầu 20 ô hỗn hợp + trạm bơm + xử lý nước thải; cứ 10 lượt, khu nào có nhu cầu > 0 và quỹ > 4.000 thì quy hoạch thêm (4 nhà, 2 thương mại, 2 công nghiệp; chung cư/văn phòng sau mốc 500), dịch vụ xây ngay khi mở khóa, mỗi ~30 nhà một công viên nhỏ. Thuế ba khu đặt bằng nhau.

| Thuế | 500 dân | 2.000 dân | Dân lượt 1.000 | Ròng/tick lượt 1.000 | Lượt đạt 1 triệu | Hài lòng |
|---:|---:|---:|---:|---:|---:|---:|
| 8% | lượt 90 | lượt 583 | 3.164 | +2.579 | 868 | 58 |
| 10% | lượt 92 | lượt 578 | 3.792 | +5.233 | 675 | 59 |
| 13% | lượt 110 | lượt 643 | 3.572 | +6.862 | 569 | 56 |

Chi lượt cuối (thuế 10%): đường 696, điện 1.500, giáo dục 40, y tế 35, cảnh sát 18, cứu hỏa 24, khác 117 — tổng ≈ 2.430 so với thu ≈ 7.660.

### Phát hiện

1. **Giữa game quá dễ giàu.** Từ ~2.000 dân, thu gấp ~3 lần chi; 1 triệu tiền trước lượt 700. Không có áp lực ngân sách nào sau giai đoạn đầu.
2. **Thuế cao gần như không bị phạt.** 13% lớn gần bằng 10% (chậm hơn ~10%) nhưng lãi hơn ~30%; hài lòng chỉ −3. Người chơi sẽ luôn để thuế cao. 8% không giúp lớn nhanh hơn.
3. **Đầu game ổn:** quỹ thấp nhất 20–32 nghìn, không phá sản khi mở rộng theo nhu cầu.
4. **Mở rộng ồ ạt vẫn phá sản được** (kịch bản Xây dịch vụ sớm, ~900 dân): 17 nhà máy điện (1.020/lượt) + đường (578) ăn hết thuế. Điện là khoản chi nặng nhất ở mọi kịch bản.
5. **Nước là nút thắt lên cấp:** chỉ ~23/250 nhà có đủ nước và thoát nước (mỗi trạm 40 ô), nên chỉ ~14 nhà lên cấp 2–3. Một phần do khung đo chỉ có 3 ô bờ biển.
6. **Giới hạn của khung đo:** ~600 lô đất nên dừng ở ~3.800 dân; muốn đo tới 10.000 dân cần khung bản đồ lớn hơn và nhiều bờ nước.

### Đề xuất cho 2e (chưa làm)

- Thuế: mỗi điểm trên 10% trừ nhu cầu 5–6 (thay 3) và hài lòng 2 (thay 1); có thể cho thu nhập tăng chậm dần khi thuế > 12%.
- Kéo chi giữa game lên: rác (2d) sẽ thêm một khoản; cân nhắc phí đường tăng theo quy mô, phí dịch vụ lớn hơn, hoặc giảm thuế gốc mỗi dân.
- Nước: tăng sức chứa trạm (40 → 80 ô) hoặc giảm giá, để lên cấp nhà không bị nghẽn.
- Mở rộng khung đo lên ~2.500 lô với nhiều bờ nước để kiểm chứng mốc 10.000 dân.

### Đã làm trong 2e (đợt 1, trước khi có rác)

- Thuế trên 10%: mỗi điểm trừ nhu cầu **4** và hài lòng dân **2**; dưới 10% giữ +3 nhu cầu / +1 hài lòng. Phần thuế trên 12% chỉ thu được một nửa (20% thu như 16%).
- Trạm bơm và xử lý nước thải phục vụ **80** ô (trước 40).

| Thuế | 500 dân | 2.000 dân | Dân lượt 1.000 | Ròng/tick lượt 1.000 | Ghi chú |
|---:|---:|---:|---:|---:|---|
| 8% | lượt 90 | lượt 503 | 3.416 | +3.044 | |
| 10% | lượt 92 | lượt 536 | 3.660 | +5.004 | nhà lên cấp 2–3: 14 → 29 |
| 13% | lượt 145 | — | 636 | +921 | chững lại nhưng vẫn lãi |
| 16% | — | — | 156 | +9 | đứng yên, không phá sản |

Còn lại cho đợt 2 (sau khi gộp rác 2d): chi giữa game vẫn thấp so với thu (lãi ~5.000/tick ở ~3.700 dân); đo lại khi rác thêm chi phí rồi mới chỉnh phí đường/dịch vụ hoặc thuế gốc.

### Sau khi gộp rác (2d)

Thị trưởng tự động thêm bãi rác (lò đốt sau 2.000 dân) khi thu gom thiếu > 60 đơn vị, tối đa một cơ sở mỗi 50 lượt.

| Thuế | 2.000 dân | Dân lượt 1.000 | Ròng/tick lượt 1.000 | Chi rác |
|---:|---:|---:|---:|---:|
| 8% | lượt 470 | 3.332 | +2.176 | 552 |
| 10% | lượt 494 | 3.760 | +4.391 | 552 |
| 13% | — | 480 | +481 | 36 |
| 16% | — | 156 | −11 | 18 |

Rác chiếm ~19% chi ở ~3.700 dân nhưng giữa game vẫn dư (thu 7.368 so với chi 2.977).

**Thử và đã hoàn lại:** giảm thuế gốc 20% (1 mỗi dân, 12 mỗi cấp doanh nghiệp) + phí đường 3. Đầu game sụp: thuế 8% phá sản ở lượt 143, kịch bản dịch vụ sớm phá sản. Kết luận: không giảm thu đều tay; cần một khoản chi **tăng theo quy mô** (chỉ nặng ở giữa và cuối game), ví dụ chi phí hành chính ≈ dân² / 10.000 mỗi lượt (300 dân ≈ 9, 1.000 ≈ 100, 3.700 ≈ 1.370).

### Chi phí hành chính (2e đợt 2)

Chi mỗi lượt = dân² / 20.000 dưới 10.000 dân; từ 10.000 trở lên = 0,5 mỗi dân (liền mạch tại 10.000 = 5.000; không tràn số ở thành phố khổng lồ). Dòng "Hành chính" trong bảng ngân sách.

Bản đầu (dân² / 10.000, rồi 1 mỗi dân) làm **Đô thị lớn phá sản ở ~9.500–10.900 dân** (hành chính 10.904 ≈ nửa thu): thu ~1,85/dân, chi khác đã ~1,0/dân, nên mọi thành phố 10.000 dân đều lỗ. Đã giảm một nửa.

**Kịch bản Đô thị lớn** (`Scenario.Metropolis`): lưới 40 × 30 phố (2.400 lô, 9 ô bờ), mở rộng gấp đôi nhịp thường, thêm cặp trạm nước mỗi 150 lượt khi còn bờ, 4.000 lượt, thuế 10%.

| Lượt | Dân | Ròng/tick | Hài lòng |
|---:|---:|---:|---:|
| 250 | 2.640 | +2.685 | 64 |
| 750 | 6.416 | +4.749 | 57 |
| 1.250 | 8.792 | +4.142 | 48 |
| 1.750 | 10.904 | +3.748 | 43 |

Mốc 500 / 2.000 / 5.000 dân: lượt 54 / 218 / 470. Không nợ; quỹ thấp nhất 36.656. Chi lượt cuối: điện 5.760, hành chính 5.452, đường 2.624, rác 1.957, khác 427, dịch vụ ~160; thu 20.131.

Ở các kịch bản nhỏ (thuế 10%, ~3.700 dân): hành chính 688, lãi +3.624 (trước khi có hành chính +4.391). Thuế 8%: lãi +1.593.

### Còn mở sau 2e

- Lãi cuối game vẫn cao (~+3.700/tick ở 10.900 dân, 14 triệu sau 4.000 lượt), nhưng một phần do "thị trưởng tự động" chỉ xây một công trình mỗi loại dịch vụ: hài lòng tụt 64 → 43 và chi dịch vụ chỉ ~160. Người chơi thật sẽ phủ dịch vụ rộng hơn và tốn hơn nhiều. Cần đo lại khi bot phủ dịch vụ theo bán kính.
- Nước: chỉ 125/744 nhà có đủ nước và thoát nước (9 ô bờ). Thành phố lớn xa bờ sẽ nghẽn lên cấp — xem xét ở giai đoạn 3 (sông, ao) hoặc thêm tháp nước không cần bờ.
- Điện là khoản chi lớn nhất (96 nhà máy cho ~2.000 ô); giai đoạn sau nên có nhà máy công suất lớn hơn.

### Điện (28/11)

Nhà máy điện cơ bản 24 → 60 ô, phí 60 → 90/lượt; thêm **Nhà máy điện khí** (mở ở 2.000 dân): 250 ô, giá 6.000, phí 300/lượt (save 15). Đô thị lớn (bot chỉ xây nhà máy cơ bản): 40 nhà máy thay vì 96, chi điện 3.600 thay vì 5.760; lên 11.780 dân, lãi +7.194/tick. Lãi cuối game tăng thêm — cần đo lại khi bot phủ dịch vụ đầy đủ (xem "Còn mở sau 2e").

### Cân bằng cuối game (28/11)

Bot Đô thị lớn nay xây thêm một bộ trường tiểu học, đồn công an, bệnh viện và trạm cứu hỏa mỗi 100 lượt (từ 2.000 dân) để phủ dịch vụ như người chơi thật. Trước khi chỉnh: 19.000 dân, hài lòng 76, lãi **+20.600/tick** (dịch vụ chỉ tốn ~1.200).

Đã đổi (Anh NiK chọn "không trần dân"):
- Phí dịch vụ lớn ×5: đồn công an 90, bệnh viện 175, trạm cứu hỏa 120. Bản nhỏ ×2: trạm công an 16, phòng khám 28, cứu hỏa nhỏ 20. Trường học giữ nguyên.
- Hành chính trên 10.000 dân: 1,2 mỗi người (trước 0,5), liền mạch tại 10.000.
- Đồn công an mở ở 2.000 dân (trước 500); đầu game dùng trạm công an.

Kết quả Đô thị lớn: 20.200 dân, hài lòng 77, lãi **+11.800/tick** (chi hành chính 17.200, dịch vụ ~5.700). Mỗi người dân thêm vẫn lãi ~0,2 nên không có trần. Các kịch bản Tăng trưởng (8–10%) gần như không đổi.

Còn mở: kịch bản "Xây dịch vụ sớm" (thị trấn 540 dân không lớn thêm, trước lãi +12) nay lỗ −16 và phá sản ở lượt 1.214; thành phố giả 1,4 triệu dân trong test hiệu năng (không dịch vụ) lỗ ~430.000/tick.
