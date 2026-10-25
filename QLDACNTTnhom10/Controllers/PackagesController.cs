namespace QLDACNTTnhom10.Controllers;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QLDACNTTnhom10.DTOs;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")] // Chỉ Admin được quyền thay đổi gói tập
public class PackagesController : ControllerBase
{
    private readonly string _connectionString;

    public PackagesController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection");
    }

    // GET: Lấy danh sách toàn bộ gói tập
    [HttpGet]
    [AllowAnonymous] // Cho phép khách vãng lai hoặc hội viên xem danh sách gói
    public async Task<IActionResult> GetAllPackages([FromQuery] bool onlyActive = true)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = onlyActive
            ? "SELECT * FROM PACKAGES WHERE IsActive = 1 ORDER BY Price ASC"
            : "SELECT * FROM PACKAGES ORDER BY PackageID DESC";

        var packages = await connection.QueryAsync(sql);
        return Ok(packages);
    }

    // POST: Giải quyết TASK-414 (Viết Api tạo Gói tập)
    [HttpPost]
    public async Task<IActionResult> CreatePackage([FromBody] PackageRequest req)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = @"
            INSERT INTO PACKAGES (PackageName, PackageType, DurationDays, TotalSessions, Price, IsFlexibleTime, IsActive)
            OUTPUT INSERTED.PackageID
            VALUES (@PackageName, @PackageType, @DurationDays, @TotalSessions, @Price, @IsFlexibleTime, 1)";

        int newPackageId = (int)await connection.ExecuteScalarAsync(sql, req);

        return Ok(new { Message = "Tạo gói tập thành công!", PackageID = newPackageId });
    }

    // PUT: Cập nhật thông tin và giá bán gói tập
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePackage(int id, [FromBody] PackageRequest req)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = @"
            UPDATE PACKAGES 
            SET PackageName = @PackageName, 
                PackageType = @PackageType, 
                DurationDays = @DurationDays, 
                TotalSessions = @TotalSessions, 
                Price = @Price, 
                IsFlexibleTime = @IsFlexibleTime
            WHERE PackageID = @Id";

        var affected = await connection.ExecuteAsync(sql, new
        {
            req.PackageName,
            req.PackageType,
            req.DurationDays,
            req.TotalSessions,
            req.Price,
            req.IsFlexibleTime,
            Id = id
        });

        if (affected == 0) return NotFound("Không tìm thấy gói tập.");
        return Ok(new { Message = "Cập nhật gói tập thành công!" });
    }

    // DELETE: Xóa mềm (Ngừng bán gói tập)
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePackage(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        // Soft delete để giữ nguyên vẹn các hợp đồng cũ đã ký theo gói này
        var sql = "UPDATE PACKAGES SET IsActive = 0 WHERE PackageID = @Id";

        var affected = await connection.ExecuteAsync(sql, new { Id = id });

        if (affected == 0) return NotFound("Không tìm thấy gói tập.");
        return Ok(new { Message = "Đã ngừng bán gói tập thành công!" });
    }
}
