using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QLDACNTTnhom10.DTOs;
using QLDACNTTNhom10.DTOs;
using System.Text.RegularExpressions;
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
        // 4. API IMPORT EXCEL KÈM VALIDATE CHI TIẾT (TASK-387)
        // ==========================================
        [HttpPost("import-members")]
        public async Task<IActionResult> ImportMembersFromExcel(IFormFile file)
        {
            // --- TẦNG 1: KIỂM TRA FILE ĐẦU VÀO ---
            if (file == null || file.Length == 0)
                return BadRequest(new { Message = "Vui lòng chọn file Excel (.xlsx) không rỗng để tải lên." });

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { Message = "Sai định dạng! Hệ thống chỉ hỗ trợ file có đuôi .xlsx" });

            const long maxFileSize = 5 * 1024 * 1024; // 5MB
            if (file.Length > maxFileSize)
                return BadRequest(new { Message = "Dung lượng file vượt quá giới hạn cho phép (Tối đa 5MB)." });

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            XLWorkbook workbook;
            try
            {
                workbook = new XLWorkbook(stream);
            }
            catch
            {
                return BadRequest(new { Message = "File Excel bị hỏng hoặc không đúng cấu trúc OpenXML." });
            }

            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null || worksheet.RangeUsed() == null)
                return BadRequest(new { Message = "File Excel không có trang tính hoặc không có dữ liệu." });

            // Tự động kiểm tra xem Cột 1 có phải là cột "STT" hoặc "Mã HV" không
            string firstHeader = worksheet.Cell(1, 1).GetString().Trim();
            int offset = (firstHeader.Equals("STT", StringComparison.OrdinalIgnoreCase) ||
                          firstHeader.Contains("Mã", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

            // Kiểm tra các cột bắt buộc (Username, Email, Họ tên, SĐT) đã dịch theo offset
            string colUsername = worksheet.Cell(1, 1 + offset).GetString().Trim();
            string colEmail = worksheet.Cell(1, 2 + offset).GetString().Trim();
            string colFullName = worksheet.Cell(1, 3 + offset).GetString().Trim();
            string colPhone = worksheet.Cell(1, 4 + offset).GetString().Trim();

            if (string.IsNullOrEmpty(colUsername) || string.IsNullOrEmpty(colEmail) ||
                string.IsNullOrEmpty(colFullName) || string.IsNullOrEmpty(colPhone))
            {
                return BadRequest(new
                {
                    Message = "Cấu trúc cột tiêu đề (Dòng 1) không hợp lệ. Yêu cầu: STT | Username | Email | FullName | Phone | DateOfBirth | Gender"
                });
            }

            var rows = worksheet.RangeUsed()!.RowsUsed().Skip(1).ToList(); // Bỏ qua dòng tiêu đề
            if (!rows.Any())
                return BadRequest(new { Message = "File Excel chỉ có tiêu đề, không có dòng dữ liệu hội viên nào." });

            int successCount = 0;
            var errorReport = new List<object>();

            // HashSet dùng để bắt trùng lặp ngay trong nội bộ file Excel
            var seenUsernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            foreach (var row in rows)
            {
                int rowNum = row.RowNumber();
                var rowErrors = new List<string>();

                // Nếu có cột STT ở Cột 1 (offset = 1), code sẽ tự động lấy Username từ Cột 2 trở đi
                string username = row.Cell(1 + offset).GetString().Trim(); // Cột 2
                string email = row.Cell(2 + offset).GetString().Trim(); // Cột 3
                string fullName = row.Cell(3 + offset).GetString().Trim(); // Cột 4
                string phone = row.Cell(4 + offset).GetString().Trim(); // Cột 5
                var dobCell = row.Cell(5 + offset);                    // Cột 6 (Ô Ngày sinh)
                string dobStr = dobCell.GetString().Trim();
                string gender = row.Cell(6 + offset).GetString().Trim(); // Cột 7

                // Bỏ qua dòng trắng hoàn toàn
                if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(email) && string.IsNullOrEmpty(fullName) && string.IsNullOrEmpty(phone))
                    continue;

                // --- TẦNG 2: VALIDATE ĐỊNH DẠNG & ĐỘ DÀI TỪNG CỘT ---
                // 1. Validate Username
                if (string.IsNullOrWhiteSpace(username))
                    rowErrors.Add("Cột 1 (Username): Không được để trống.");
                else if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]{3,50}$"))
                    rowErrors.Add("Cột 1 (Username): Phải từ 3-50 ký tự, chỉ gồm chữ cái không dấu, số hoặc dấu gạch dưới.");

                // 2. Validate Email
                if (string.IsNullOrWhiteSpace(email))
                    rowErrors.Add("Cột 2 (Email): Không được để trống.");
                else if (email.Length > 100 || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                    rowErrors.Add("Cột 2 (Email): Định dạng Email không hợp lệ hoặc vượt quá 100 ký tự.");

                // 3. Validate Họ tên
                if (string.IsNullOrWhiteSpace(fullName))
                    rowErrors.Add("Cột 3 (FullName): Họ và tên không được để trống.");
                else if (fullName.Length > 100)
                    rowErrors.Add("Cột 3 (FullName): Họ và tên vượt quá 100 ký tự.");

                // 4. Validate Số điện thoại
                if (string.IsNullOrWhiteSpace(phone))
                    rowErrors.Add("Cột 4 (Phone): Số điện thoại không được để trống.");
                else if (!Regex.IsMatch(phone, @"^0\d{9}$"))
                    rowErrors.Add("Cột 4 (Phone): Số điện thoại phải gồm đúng 10 chữ số và bắt đầu bằng số 0.");

                // 5. Validate Ngày sinh (Không bắt buộc, nhưng nếu nhập phải đúng chuẩn)
                DateTime? dob = null;
                if (!string.IsNullOrWhiteSpace(dobStr))
                {
                    if (row.Cell(5).TryGetValue<DateTime>(out DateTime excelDob))
                    {
                        dob = excelDob;
                    }
                    else if (DateTime.TryParse(dobStr, out DateTime parsedDob))
                    {
                        dob = parsedDob;
                    }
                    else
                    {
                        rowErrors.Add("Cột 5 (DateOfBirth): Định dạng ngày sinh không hợp lệ (Chuẩn: yyyy-MM-dd).");
                    }

                    if (dob.HasValue)
                    {
                        int age = DateTime.Today.Year - dob.Value.Year;
                        if (dob.Value.Date > DateTime.Today || age < 10 || age > 100)
                            rowErrors.Add("Cột 5 (DateOfBirth): Ngày sinh không hợp lệ (Độ tuổi hội viên phải từ 10 đến 100 tuổi).");
                    }
                }

                // 6. Validate Giới tính
                var validGenders = new[] { "Nam", "Nữ", "Khác" };
                if (!string.IsNullOrWhiteSpace(gender) && !validGenders.Contains(gender, StringComparer.OrdinalIgnoreCase))
                {
                    rowErrors.Add("Cột 6 (Gender): Giới tính chỉ chấp nhận 'Nam', 'Nữ', 'Khác' hoặc để trống.");
                }

                // --- TẦNG 3: KIỂM TRA TRÙNG LẶP TRONG FILE EXCEL & TRONG DATABASE ---
                if (!string.IsNullOrWhiteSpace(username) && !seenUsernames.Add(username))
                    rowErrors.Add($"Username '{username}' bị lặp lại nhiều lần trong file Excel.");

                if (!string.IsNullOrWhiteSpace(email) && !seenEmails.Add(email))
                    rowErrors.Add($"Email '{email}' bị lặp lại nhiều lần trong file Excel.");

                if (!string.IsNullOrWhiteSpace(phone) && !seenPhones.Add(phone))
                    rowErrors.Add($"Số điện thoại '{phone}' bị lặp lại nhiều lần trong file Excel.");

                // Nếu dữ liệu cơ bản hợp lệ thì kiểm tra tiếp trong Database
                if (!rowErrors.Any())
                {
                    var dbConflict = await connection.QueryFirstOrDefaultAsync<string>(@"
                        SELECT TOP 1 
                            CASE 
                                WHEN u.Username = @Username THEN N'Username đã tồn tại trong CSDL'
                                WHEN u.Email = @Email THEN N'Email đã tồn tại trong CSDL'
                                WHEN m.Phone = @Phone THEN N'Số điện thoại đã tồn tại trong CSDL'
                            END
                        FROM USERS u
                        LEFT JOIN MEMBERS m ON u.UserID = m.UserID
                        WHERE u.Username = @Username OR u.Email = @Email OR m.Phone = @Phone",
                        new { Username = username, Email = email, Phone = phone });

                    if (!string.IsNullOrEmpty(dbConflict))
                    {
                        rowErrors.Add(dbConflict);
                    }
                }

                // Nếu dòng này có bất kỳ lỗi nào -> Ghi vào danh sách lỗi và bỏ qua không Insert
                if (rowErrors.Any())
                {
                    errorReport.Add(new
                    {
                        RowNumber = rowNum,
                        Username = username,
                        Email = email,
                        Errors = rowErrors
                    });
                    continue;
                }

                // --- TẦNG 4: LƯU VÀO CSDL BẰNG TRANSACTION ---
                using var transaction = connection.BeginTransaction();
                try
                {
                    string defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("Gym@123");

                    var sqlUser = @"
                        INSERT INTO USERS (Username, PasswordHash, Email, RoleID, IsActive)
                        OUTPUT INSERTED.UserID
                        VALUES (@Username, @PasswordHash, @Email, 4, 1)";

                    int newUserId = await connection.ExecuteScalarAsync<int>(
                        sqlUser, new { Username = username, PasswordHash = defaultPasswordHash, Email = email }, transaction);

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
                    errorReport.Add(new
                    {
                        RowNumber = rowNum,
                        Username = username,
                        Email = email,
                        Errors = new List<string> { $"Lỗi CSDL: {ex.Message}" }
                    });
                }
            }

            return Ok(new
            {
                Message = $"Kiểm tra và xử lý hoàn tất! Thành công: {successCount} dòng. Lỗi: {errorReport.Count} dòng.",
                TotalRowsProcessed = successCount + errorReport.Count,
                SuccessCount = successCount,
                FailedCount = errorReport.Count,
                ErrorDetails = errorReport
            });
        }
    }
}