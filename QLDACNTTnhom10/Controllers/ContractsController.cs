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

    // ==========================================
    // TASK-355: GIA HẠN HỢP ĐỒNG (RENEWAL)
    // ==========================================
    [HttpPost("{id}/renew")]
    public async Task<IActionResult> RenewContract(int id, [FromBody] RenewContractRequest req)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // 1. Lấy thông tin Hợp đồng cũ
            var oldContractSql = "SELECT * FROM CONTRACTS WHERE ContractID = @Id";
            var oldContract = await connection.QuerySingleOrDefaultAsync(oldContractSql, new { Id = id }, transaction);

            if (oldContract == null)
                return NotFound("Không tìm thấy hợp đồng gốc.");

            // 2. Lấy thông tin Gói tập hiện tại
            var packageSql = "SELECT DurationDays, TotalSessions, Price FROM PACKAGES WHERE PackageID = @PackageID";
            var package = await connection.QuerySingleOrDefaultAsync(packageSql, new { PackageID = oldContract.PackageID }, transaction);

            // 3. Logic tính StartDate mới:
            // - Nếu hợp đồng cũ vẫn còn hạn -> Nối đuôi (StartDate = EndDate cũ)
            // - Nếu hợp đồng cũ đã hết hạn -> Bắt đầu từ hôm nay (StartDate = Today)
            DateTime newStartDate = oldContract.EndDate > DateTime.Today ? oldContract.EndDate : DateTime.Today;
            DateTime newEndDate = newStartDate.AddDays(package.DurationDays);

            decimal discount = req.DiscountAmount ?? 0;
            decimal totalAmount = package.Price - discount;

            // 4. Đổi trạng thái hợp đồng cũ thành 'Renewed' để dễ thống kê
            await connection.ExecuteAsync("UPDATE CONTRACTS SET Status = 'Renewed' WHERE ContractID = @Id", new { Id = id }, transaction);

            // 5. Insert Hợp đồng mới (Kế thừa Member, Package, Trainer, Class của hợp đồng cũ)
            var insertNewContractSql = @"
                INSERT INTO CONTRACTS (MemberID, PackageID, TrainerID, ClassID, StartDate, EndDate, RemainingSessions, TotalAmount, DiscountAmount, PromoCode, Status)
                OUTPUT INSERTED.ContractID
                VALUES (@MemberID, @PackageID, @TrainerID, @ClassID, @StartDate, @EndDate, @RemainingSessions, @TotalAmount, @DiscountAmount, @PromoCode, 'Active')";

            int newContractId = (int)await connection.ExecuteScalarAsync(insertNewContractSql, new
            {
                oldContract.MemberID,
                oldContract.PackageID,
                oldContract.TrainerID,
                oldContract.ClassID,
                StartDate = newStartDate,
                EndDate = newEndDate,
                RemainingSessions = package.TotalSessions,
                TotalAmount = totalAmount,
                req.DiscountAmount,
                req.PromoCode
            }, transaction);

            // 6. Ghi nhận thanh toán cho hợp đồng mới
            var insertPaymentSql = @"
                INSERT INTO PAYMENTS (ContractID, Amount, PaymentMethod, Status)
                VALUES (@ContractID, @Amount, @PaymentMethod, 'Completed')";

            await connection.ExecuteAsync(insertPaymentSql, new
            {
                ContractID = newContractId,
                Amount = totalAmount,
                req.PaymentMethod
            }, transaction);

            transaction.Commit();
            return Ok(new { Message = "Gia hạn hợp đồng thành công!", NewContractID = newContractId, NewEndDate = newEndDate.ToString("yyyy-MM-dd") });
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return BadRequest($"Lỗi hệ thống khi gia hạn: {ex.Message}");
        }
    }
}