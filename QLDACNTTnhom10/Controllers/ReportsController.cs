using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using QLDACNTTNhom10.DTOs;

namespace QLDACNTTNhom10.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin,Staff")]
    public class ReportsController : ControllerBase
    {
        private readonly string _connectionString;

        public ReportsController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")!;
        }

        // TASK-382: API báo cáo doanh thu theo gói tập và thời gian
        [HttpGet("revenue")]
        public async Task<IActionResult> GetRevenueReport(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? packageId = null,
            [FromQuery] string? packageType = null)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            {
                return BadRequest("Ngày bắt đầu (fromDate) không được lớn hơn ngày kết thúc (toDate).");
            }

            using var connection = new SqlConnection(_connectionString);

            var parameters = new
            {
                FromDate = fromDate?.Date,
                ToDate = toDate?.Date,
                PackageId = packageId,
                PackageType = string.IsNullOrWhiteSpace(packageType) ? null : packageType.Trim()
            };

            // 1. Truy vấn thống kê doanh thu gom nhóm theo Gói tập (GROUP BY)
            var summaryByPackageSql = @"
                SELECT 
                    pkg.PackageID,
                    pkg.PackageName,
                    pkg.PackageType,
                    COUNT(DISTINCT c.ContractID) AS TotalContracts,
                    ISNULL(SUM(c.DiscountAmount), 0) AS TotalDiscount,
                    ISNULL(SUM(p.Amount), 0) AS TotalRevenue
                FROM PACKAGES pkg
                JOIN CONTRACTS c ON pkg.PackageID = c.PackageID
                JOIN PAYMENTS p ON c.ContractID = p.ContractID
                WHERE p.Status = N'Completed'
                  AND (@FromDate IS NULL OR CAST(p.PaymentDate AS DATE) >= @FromDate)
                  AND (@ToDate IS NULL OR CAST(p.PaymentDate AS DATE) <= @ToDate)
                  AND (@PackageId IS NULL OR pkg.PackageID = @PackageId)
                  AND (@PackageType IS NULL OR pkg.PackageType = @PackageType)
                GROUP BY pkg.PackageID, pkg.PackageName, pkg.PackageType
                ORDER BY TotalRevenue DESC";

            var revenueByPackages = (await connection.QueryAsync<RevenueByPackageDto>(summaryByPackageSql, parameters)).ToList();

            // 2. Truy vấn danh sách giao dịch chi tiết theo thời gian
            var detailSql = @"
                SELECT 
                    p.PaymentID,
                    c.ContractID,
                    m.FullName AS MemberName,
                    pkg.PackageName,
                    pkg.PackageType,
                    p.Amount,
                    p.PaymentMethod,
                    p.PaymentDate
                FROM PAYMENTS p
                JOIN CONTRACTS c ON p.ContractID = c.ContractID
                JOIN PACKAGES pkg ON c.PackageID = pkg.PackageID
                JOIN MEMBERS m ON c.MemberID = m.MemberID
                WHERE p.Status = N'Completed'
                  AND (@FromDate IS NULL OR CAST(p.PaymentDate AS DATE) >= @FromDate)
                  AND (@ToDate IS NULL OR CAST(p.PaymentDate AS DATE) <= @ToDate)
                  AND (@PackageId IS NULL OR pkg.PackageID = @PackageId)
                  AND (@PackageType IS NULL OR pkg.PackageType = @PackageType)
                ORDER BY p.PaymentDate DESC";

            var transactionDetails = (await connection.QueryAsync<RevenueDetailDto>(detailSql, parameters)).ToList();

            // 3. Đóng gói kết quả trả về
            var response = new RevenueReportResponseDto
            {
                FromDate = fromDate,
                ToDate = toDate,
                GrandTotalRevenue = revenueByPackages.Sum(x => x.TotalRevenue),
                TotalTransactions = transactionDetails.Count,
                RevenueByPackages = revenueByPackages,
                TransactionDetails = transactionDetails
            };

            return Ok(response);
        }
    }
}