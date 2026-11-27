namespace XCity
{
    // Every player-facing string lives here, so an English table can be added later (roadmap phase 8).
    public static class UiText
    {
        public const string StrokeCost = "Chi phí: $";
        public const string StrokeOverBudget = "Thiếu ngân sách · Chi phí: $";

        public const string View = "Xem";
        public const string Road = "Đường";
        public const string Bridge = "Cầu";
        public const string Residential = "Nhà ở";
        public const string Commercial = "Thương mại";
        public const string Industrial = "Công nghiệp";
        public const string Education = "Giáo dục";
        public const string Services = "Dịch vụ";
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
