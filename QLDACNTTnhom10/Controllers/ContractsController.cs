namespace QLDACNTTnhom10.Controllers;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QLDACNTTnhom10.DTOs;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin,Staff")]
public class ContractsController : ControllerBase
{
    private readonly string _connectionString;

    public ContractsController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection");
    }

    // ==========================================
    // TASK-350: LẬP HỢP ĐỒNG MỚI & THANH TOÁN
    // ==========================================
    [HttpPost]
    public async Task<IActionResult> CreateContract([FromBody] CreateContractRequest req)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // 1. Truy vấn thông tin Gói tập (PACKAGES) để lấy Giá tiền và Thời hạn
            var packageSql = "SELECT DurationDays, TotalSessions, Price FROM PACKAGES WHERE PackageID = @PackageID AND IsActive = 1";
            var package = await connection.QuerySingleOrDefaultAsync(packageSql, new { req.PackageID }, transaction);

            if (package == null)
                return BadRequest("Gói tập không tồn tại hoặc đã ngừng bán.");

            // 2. Tính toán ngày giờ & Số tiền
            DateTime startDate = DateTime.Today;
            DateTime endDate = startDate.AddDays(package.DurationDays);

            decimal discount = req.DiscountAmount ?? 0;
            decimal totalAmount = package.Price - discount;
            if (totalAmount < 0) totalAmount = 0;

            // 3. Insert vào bảng CONTRACTS
            var insertContractSql = @"
                INSERT INTO CONTRACTS (MemberID, PackageID, TrainerID, ClassID, StartDate, EndDate, RemainingSessions, TotalAmount, DiscountAmount, PromoCode, Status)
                OUTPUT INSERTED.ContractID
                VALUES (@MemberID, @PackageID, @TrainerID, @ClassID, @StartDate, @EndDate, @RemainingSessions, @TotalAmount, @DiscountAmount, @PromoCode, 'Active')";

            int newContractId = (int)await connection.ExecuteScalarAsync(insertContractSql, new
            {
                req.MemberID,
                req.PackageID,
                req.TrainerID,
                req.ClassID,
                StartDate = startDate,
                EndDate = endDate,
                RemainingSessions = package.TotalSessions,
                TotalAmount = totalAmount,
                req.DiscountAmount,
                req.PromoCode
            }, transaction);

            // 4. Insert vào bảng PAYMENTS (Ghi nhận doanh thu ngay lập tức)
            var insertPaymentSql = @"
                INSERT INTO PAYMENTS (ContractID, Amount, PaymentMethod, Status)
                VALUES (@ContractID, @Amount, @PaymentMethod, 'Completed')";

            await connection.ExecuteAsync(insertPaymentSql, new
            {
                ContractID = newContractId,
                Amount = totalAmount,
                req.PaymentMethod
            }, transaction);

            // 5. Xác nhận thành công
            transaction.Commit();
            return Ok(new { Message = "Lập hợp đồng và thanh toán thành công!", ContractID = newContractId });
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return BadRequest($"Lỗi hệ thống khi tạo hợp đồng: {ex.Message}");
        }
    }
}