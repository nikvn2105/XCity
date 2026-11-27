namespace XCity
{
    // Every player-facing string lives here, so an English table can be added later (roadmap phase 8).
    public static class UiText
    {
        public const string StrokeCost = "Chi phí: $";
        public const string StrokeOverBudget = "Thiếu ngân sách · Chi phí: $";

        // Top bar.
        public const string PerTick = "/lượt";
        public const string Debt = "Nợ ";
        public const string Population = "Dân ";
        public const string Happiness = "Hài lòng ";
        public const string Day = "Ngày ";
        public const string Pause = "II";
        public const string Speed1 = "1×";
        public const string Speed3 = "3×";

        // Toasts.
        public const string Unlocked = "Đã mở khóa: ";
        public const string FireStarted = "Có đám cháy! Chạm để xem";
        public const string DebtWarning = "Ngân sách âm: vay tiền hoặc cắt giảm chi phí trước khi phá sản";
        public const string DroppedOnLoad = "Đã gỡ {0} công trình không còn hợp lệ trên bản đồ";
    }
}
