namespace QLDACNTTNhom10.DTOs
{
    // 1. DTO gom nhóm doanh thu theo từng Gói tập
    public class RevenueByPackageDto
    {
        public int PackageID { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string PackageType { get; set; } = string.Empty;
        public int TotalContracts { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    // 2. DTO chi tiết từng giao dịch thu tiền theo thời gian
    public class RevenueDetailDto
    {
        public int PaymentID { get; set; }
        public int ContractID { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public string PackageType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; }
    }

    // 3. DTO tổng hợp trả về cho Frontend
    public class RevenueReportResponseDto
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public decimal GrandTotalRevenue { get; set; }
        public int TotalTransactions { get; set; }
        public IEnumerable<RevenueByPackageDto> RevenueByPackages { get; set; } = new List<RevenueByPackageDto>();
        public IEnumerable<RevenueDetailDto> TransactionDetails { get; set; } = new List<RevenueDetailDto>();
    }
}