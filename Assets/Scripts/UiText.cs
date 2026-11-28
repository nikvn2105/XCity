namespace XCity
{
    // Every player-facing string lives here, so an English table can be added later (roadmap phase 8).
    public static class UiText
    {
        public const string StrokeCost = "Chi phí: $";
        public const string StrokeOverBudget = "Thiếu ngân sách · Chi phí: $";

        // Tutorial quest card.
        public static string QuestStep(int step, int total) => $"NHIỆM VỤ {step}/{total}";
        public static string QuestDone(string title) => $"Hoàn thành: {title}";
        public const string TutorialDone = "Đã hoàn thành hướng dẫn cơ bản. Chúc bạn xây thành phố vui!";
        public static readonly string[] QuestTitles =
        {
            "Vẽ con đường đầu tiên",
            "Đặt nhà máy điện",
            "Quy hoạch khu dân cư",
            "Thêm thương mại hoặc công nghiệp",
            "Đặt trạm bơm nước"
        };
        public static readonly string[] QuestBodies =
        {
            "Chọn Đường, chạm điểm đầu rồi điểm cuối (hoặc kéo) để vẽ.",
            "Chọn Tiện ích, chạm một ô sát con đường. Điện truyền theo đường.",
            "Chọn Nhà ở, chạm hoặc kéo dọc hai bên đường.",
            "Chọn Thương mại (hoặc Công nghiệp) để người dân có việc làm.",
            "Mở mũi tên trên Tiện ích, chọn Trạm bơm, đặt sát bờ nước."
        };

        // Road tool.
        public const string RoadTapStart = "Chạm điểm đầu rồi điểm cuối, hoặc kéo để vẽ đường";
        public const string RoadTapEnd = "Chạm điểm cuối để xây · Chạm lại điểm đầu để dừng";

        // Top bar.
        public const string PerTick = "/lượt";
        public const string Debt = "Nợ ";
        public const string Population = "Dân ";
        public const string HappinessLabel = "Hài lòng ";
        public const string Day = "Ngày ";
        public const string Pause = "II";
        public const string Speed1 = "1×";
        public const string Speed3 = "3×";

        // Toasts.
        public const string FireStarted = "Có đám cháy! Chạm để xem";
        public const string DebtWarning = "Ngân sách âm: vay tiền hoặc cắt giảm chi phí trước khi phá sản";
        public const string DroppedOnLoad = "Đã gỡ {0} công trình không còn hợp lệ trên bản đồ";

        // Main menu and saves.
        public const string GameTitle = "XCity";
        public const string Wordmark = "XCITY";
        public const string ContinueCity = "Tiếp tục";
        public const string NewCity = "Thành phố mới";
        public const string LoadCity = "Tải";
        public const string Settings = "Cài đặt";
        public const string ReturnToMenu = "Về menu chính";
        public const string Back = "Quay lại";
        public const string Cancel = "Hủy";
        public const string DeleteCity = "Xóa";
        public const string CreateCity = "Tạo thành phố";
        public const string OverwriteCity = "Ghi đè";
        public const string CityName = "Tên thành phố";
        public const string EmptySlot = "Ô lưu trống";
        public const string SummaryUnavailable = "Chưa đọc được thông tin bản lưu";
        public const string SaveFailed = "Lưu thất bại. Thành phố vẫn đang mở.";
        public const string CreateFailed = "Không thể tạo bản lưu.";
        public const string LoadFailed = "Không thể tải bản lưu hoặc bản dự phòng.";
        public const string DeleteFailed = "Không thể xóa hết các tệp của ô lưu.";
        public const string MigrationFailed = "Chưa thể chuyển bản lưu cũ.";
        public static string DefaultCityName(int slot) => $"Thành phố {slot}";
        public static string SlotLabel(int slot) => $"Ô lưu {slot}";
        public static string ConfirmDeleteCity(string name) => $"Xóa bản lưu và bản dự phòng của {name}?";
        public static string ConfirmOverwriteCity(string name) => $"Ghi đè bản lưu của {name}?";
        public static string CitySummary(int population, int money, int day, System.DateTime lastSaved) =>
            $"Dân số: {population:N0}\nNgân sách: ${money:N0}\nNgày {day}\nLần lưu cuối: {lastSaved:dd/MM/yyyy HH:mm}";

        // Build bar.
        public const string View = "Xem";
        public const string Road = "Đường";
        public const string Bridge = "Cầu";
        public const string Residential = "Nhà ở";
        public const string Commercial = "Thương mại";
        public const string Industrial = "Công nghiệp";
        public const string Education = "Giáo dục";
        public const string Services = "Dịch vụ";
        public const string Parks = "Công viên";
        public const string SmallPark = "Công viên nhỏ";
        public const string Plaza = "Quảng trường";
        public const string LargePark = "Công viên lớn";
        public const string LandValue = "Giá đất";
        public const string Utilities = "Tiện ích";
        public const string OverlayOff = "Tắt lớp phủ";
        public const string Map = "Bản đồ";
        public const string Bulldoze = "Phá bỏ";
        public const string PowerPlant = "Nhà máy điện";
        public const string Power = "Điện";
        public const string Environment = "Môi trường";
        public const string Happiness = "Hạnh phúc";
        public const string WorldMap = "Bản đồ thế giới";
        public const string Close = "Đóng";
        public const string Locked = "Khóa";
        public const string InfoTitle = "Thông tin ô";
        public const string OverlayLegend =
            "Hạnh phúc\nĐỏ (10) · Vàng (55) · Xanh (100)\n\nPhủ dịch vụ\nLam: Giáo dục\nNgọc: Y tế\nChàm: Công an\nCam: Cứu hỏa\nXám: Khác";

        public static string WorldMapTitle(int size) => $"Thế giới {size} × {size} ô · Chạm để đến";
        public static string UnlockAt(int population) => $"Mở ở {population.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))} dân";
        public static string Unlocked(string names) => $"Đã mở khóa: {names}";
        public static string Price(int amount, bool perCell) => perCell ? $"Chi phí: ${amount:N0}/ô" : $"Chi phí: ${amount:N0}";
        public static string BridgePrice(int water, int bank) => $"Chi phí: ${water:N0}/ô nước\nĐầu cầu mới: ${bank:N0}/ô";
        public static string Upkeep(int amount) => $"Duy trì: ${amount:N0}/tháng";
        public static string ParkHappiness(int points) => $"Hạnh phúc: +{points}";
        public static string Radius(int cells) => $"Bán kính: {cells:N0} ô";
        public static string Seats(int seats) => $"Học sinh: {seats:N0} chỗ";

        public static string ShortModel(BuildingModel model, string name) => model switch
        {
            BuildingModel.OfficeTower => "Tháp VP",
            BuildingModel.ShoppingMall => "Trung tâm TM",
            BuildingModel.HighStreet => "Phố cửa hàng",
            BuildingModel.PolicePost => "Đồn nhỏ",
            BuildingModel.FireStationSmall => "Cứu hỏa nhỏ",
            BuildingModel.FireStation => "Cứu hỏa",
            BuildingModel.Harbor => "Cảng",
            BuildingModel.WaterPump => "Trạm bơm",
            BuildingModel.SewagePlant => "Xử lý nước",
            BuildingModel.Workshop => "Xưởng",
            _ => name
        };
    }
}
