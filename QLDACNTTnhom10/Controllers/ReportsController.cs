using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QLDACNTTnhom10.DTOs;
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

// ... (Bên trong class ReportsController)

        // ==========================================
        // 1. API THỐNG KÊ TỔNG QUAN DASHBOARD (TASK-365)
        // ==========================================
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboardSummary()
        {
            using var connection = new SqlConnection(_connectionString);

            // 1.1 Tổng hội viên đang hoạt động
            var activeMembers = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM MEMBERS m 
                JOIN USERS u ON m.UserID = u.UserID 
                WHERE u.IsActive = 1");

            // 1.2 Hợp đồng đang Active & Hợp đồng sắp hết hạn (trong 7 ngày tới)
            var activeContracts = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM CONTRACTS WHERE Status = N'Active'");

            var expiringSoon = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM CONTRACTS 
                WHERE Status = N'Active' 
                  AND EndDate BETWEEN CAST(GETDATE() AS DATE) AND DATEADD(DAY, 7, CAST(GETDATE() AS DATE))");

            // 1.3 Lượt check-in trong ngày hôm nay
            var todayCheckIns = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM CHECK_INS 
                WHERE CAST(CheckInTime AS DATE) = CAST(GETDATE() AS DATE)");

            // 1.4 Doanh thu tháng hiện tại & Toàn thời gian
            var monthlyRevenue = await connection.ExecuteScalarAsync<decimal>(@"
                SELECT ISNULL(SUM(Amount), 0) FROM PAYMENTS 
                WHERE Status = N'Completed' 
                  AND MONTH(PaymentDate) = MONTH(GETDATE()) 
                  AND YEAR(PaymentDate) = YEAR(GETDATE())");

            var totalRevenue = await connection.ExecuteScalarAsync<decimal>(@"
                SELECT ISNULL(SUM(Amount), 0) FROM PAYMENTS WHERE Status = N'Completed'");

            // 1.5 Tỷ lệ tái ký (Số hợp đồng Renewed / Tổng số hợp đồng)
            var totalContractsCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM CONTRACTS");
            var renewedContractsCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM CONTRACTS WHERE Status = N'Renewed'");

            double renewalRate = totalContractsCount > 0
                ? Math.Round((double)renewedContractsCount / totalContractsCount * 100, 2)
                : 0;

            // 1.6 Thống kê hiệu suất HLV (Số lớp phụ trách, số hợp đồng PT & doanh thu PT)
            var trainerSql = @"
                SELECT 
                    t.TrainerID,
                    t.Specialization,
                    t.Phone,
                    (SELECT COUNT(1) FROM CLASSES cls WHERE cls.TrainerID = t.TrainerID) AS TotalClassesAssigned,
                    COUNT(c.ContractID) AS TotalPTContracts,
                    ISNULL(SUM(c.TotalAmount), 0) AS TotalPTRevenue
                FROM TRAINERS t
                LEFT JOIN CONTRACTS c ON t.TrainerID = c.TrainerID
                GROUP BY t.TrainerID, t.Specialization, t.Phone
                ORDER BY TotalPTRevenue DESC";

            var trainers = await connection.QueryAsync<TrainerPerformanceDto>(trainerSql);

            return Ok(new DashboardSummaryDto
            {
                TotalActiveMembers = activeMembers,
                TotalActiveContracts = activeContracts,
                ExpiringSoonContracts = expiringSoon,
                TodayCheckIns = todayCheckIns,
                CurrentMonthRevenue = monthlyRevenue,
                TotalRevenueAllTime = totalRevenue,
                RenewalRatePercent = renewalRate,
                TrainerPerformances = trainers
            });
        }

        // ==========================================
        // 2. API EXPORT EXCEL: BÁO CÁO DOANH THU
        // ==========================================
        [HttpGet("export-revenue")]
        public async Task<IActionResult> ExportRevenueToExcel(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"
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
                ORDER BY p.PaymentDate DESC";

            var data = (await connection.QueryAsync<RevenueDetailDto>(sql, new
            {
                FromDate = fromDate?.Date,
                ToDate = toDate?.Date
            })).ToList();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("BaoCaoDoanhThu");

            // Tạo dòng tiêu đề (Header)
            string[] headers = { "Mã GD", "Mã HĐ", "Tên Hội Viên", "Gói Tập", "Loại Gói", "Số Tiền (VNĐ)", "Phương Thức", "Ngày Thanh Toán" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            }

            // Đổ dữ liệu vào từng dòng
            int row = 2;
            foreach (var item in data)
            {
                worksheet.Cell(row, 1).Value = item.PaymentID;
                worksheet.Cell(row, 2).Value = item.ContractID;
                worksheet.Cell(row, 3).Value = item.MemberName;
                worksheet.Cell(row, 4).Value = item.PackageName;
                worksheet.Cell(row, 5).Value = item.PackageType;
                worksheet.Cell(row, 6).Value = item.Amount;
                worksheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(row, 7).Value = item.PaymentMethod;
                worksheet.Cell(row, 8).Value = item.PaymentDate.ToString("yyyy-MM-dd HH:mm");
                row++;
            }

            // Dòng tổng cộng cuối bảng
            worksheet.Cell(row, 5).Value = "TỔNG CỘNG:";
            worksheet.Cell(row, 5).Style.Font.Bold = true;
            worksheet.Cell(row, 6).Value = data.Sum(x => x.Amount);
            worksheet.Cell(row, 6).Style.Font.Bold = true;
            worksheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            string fileName = $"BaoCaoDoanhThu_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // ==========================================
        // 3. API EXPORT EXCEL: DANH SÁCH HỘI VIÊN
        // ==========================================
        [HttpGet("export-members")]
        public async Task<IActionResult> ExportMembersToExcel()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT m.MemberID, u.Username, u.Email, m.FullName, m.Phone, m.DateOfBirth, m.Gender, u.IsActive
                FROM MEMBERS m
                JOIN USERS u ON m.UserID = u.UserID
                ORDER BY m.MemberID ASC";

            var members = (await connection.QueryAsync<MemberResponseDto>(sql)).ToList();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("DanhSachHoiVien");

            string[] headers = { "Mã HV", "Username", "Email", "Họ và Tên", "Số Điện Thoại", "Ngày Sinh", "Giới Tính", "Trạng Thái" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightCornflowerBlue;
            }

            int row = 2;
            foreach (var m in members)
            {
                worksheet.Cell(row, 1).Value = m.MemberID;
                worksheet.Cell(row, 2).Value = m.Username;
                worksheet.Cell(row, 3).Value = m.Email;
                worksheet.Cell(row, 4).Value = m.FullName;
                worksheet.Cell(row, 5).Value = m.Phone;
                worksheet.Cell(row, 6).Value = m.DateOfBirth?.ToString("yyyy-MM-dd") ?? "";
                worksheet.Cell(row, 7).Value = m.Gender ?? "";
                worksheet.Cell(row, 8).Value = m.IsActive ? "Hoạt động" : "Đã khóa";
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DanhSachHoiVien_{DateTime.Now:yyyyMMdd}.xlsx");
        }

        // ==========================================
        // 4. API IMPORT EXCEL: NHẬP HỘI VIÊN HÀNG LOẠT
        // ==========================================
        [HttpPost("import-members")]
        public async Task<IActionResult> ImportMembersFromExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Vui lòng chọn file Excel (.xlsx) để tải lên.");

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Hệ thống chỉ hỗ trợ định dạng file .xlsx");

            int successCount = 0;
            var errors = new List<string>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RangeUsed()?.RowsUsed().Skip(1); // Bỏ qua dòng tiêu đề (dòng 1)

            if (rows == null)
                return BadRequest("File Excel không có dữ liệu.");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Cấu trúc cột đọc vào (khớp với thứ tự hoặc tạo file mẫu):
            // Cột 1: Username | Cột 2: Email | Cột 3: Họ Tên | Cột 4: SĐT | Cột 5: Ngày sinh (yyyy-MM-dd) | Cột 6: Giới tính
            foreach (var row in rows)
            {
                int rowNum = row.RowNumber();
                string username = row.Cell(1).GetValue<string>().Trim();
                string email = row.Cell(2).GetValue<string>().Trim();
                string fullName = row.Cell(3).GetValue<string>().Trim();
                string phone = row.Cell(4).GetValue<string>().Trim();
                string dobStr = row.Cell(5).GetValue<string>().Trim();
                string gender = row.Cell(6).GetValue<string>().Trim();

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName))
                {
                    errors.Add($"Dòng {rowNum}: Thiếu Username, Email hoặc Họ tên.");
                    continue;
                }

                // Kiểm tra trùng Username hoặc Email
                var exists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM USERS WHERE Username = @Username OR Email = @Email",
                    new { Username = username, Email = email });

                if (exists > 0)
                {
                    errors.Add($"Dòng {rowNum}: Username '{username}' hoặc Email '{email}' đã tồn tại.");
                    continue;
                }

                using var transaction = connection.BeginTransaction();
                try
                {
                    // Mật khẩu mặc định khi import là "Gym@123"
                    string defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("Gym@123");

                    var sqlUser = @"
                        INSERT INTO USERS (Username, PasswordHash, Email, RoleID, IsActive)
                        OUTPUT INSERTED.UserID
                        VALUES (@Username, @PasswordHash, @Email, 4, 1)";

                    int newUserId = await connection.ExecuteScalarAsync<int>(
                        sqlUser, new { Username = username, PasswordHash = defaultPasswordHash, Email = email }, transaction);

                    DateTime? dob = DateTime.TryParse(dobStr, out DateTime parsedDob) ? parsedDob : null;

                    var sqlMember = @"
                        INSERT INTO MEMBERS (UserID, FullName, Phone, DateOfBirth, Gender)
                        VALUES (@UserID, @FullName, @Phone, @DateOfBirth, @Gender)";

                    await connection.ExecuteAsync(sqlMember, new
                    {
                        UserID = newUserId,
                        FullName = fullName,
                        Phone = phone,
                        DateOfBirth = dob,
                        Gender = string.IsNullOrWhiteSpace(gender) ? null : gender
                    }, transaction);

                    transaction.Commit();
                    successCount++;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    errors.Add($"Dòng {rowNum}: Lỗi hệ thống ({ex.Message})");
                }
            }

            return Ok(new
            {
                Message = $"Hoàn tất Import! Thành công: {successCount} hội viên. Lỗi/Bỏ qua: {errors.Count} dòng.",
                SuccessCount = successCount,
                ErrorDetails = errors
            });
        }
    }
}