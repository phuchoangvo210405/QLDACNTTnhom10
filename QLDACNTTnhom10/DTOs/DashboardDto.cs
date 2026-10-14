namespace QLDACNTTNhom10.DTOs
{
    public class TrainerPerformanceDto
    {
        public int TrainerID { get; set; }
        public string Specialization { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public int TotalClassesAssigned { get; set; }
        public int TotalPTContracts { get; set; }
        public decimal TotalPTRevenue { get; set; }
    }

    public class DashboardSummaryDto
    {
        public int TotalActiveMembers { get; set; }
        public int TotalActiveContracts { get; set; }
        public int ExpiringSoonContracts { get; set; } // Sắp hết hạn trong 7 ngày tới
        public int TodayCheckIns { get; set; }
        public decimal CurrentMonthRevenue { get; set; }
        public decimal TotalRevenueAllTime { get; set; }
        public double RenewalRatePercent { get; set; } // Tỷ lệ hội viên tái ký (%)
        public IEnumerable<TrainerPerformanceDto> TrainerPerformances { get; set; } = new List<TrainerPerformanceDto>();
    }
}