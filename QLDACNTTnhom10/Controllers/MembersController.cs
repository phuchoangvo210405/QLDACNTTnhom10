namespace QLDACNTTnhom10.Controllers;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QLDACNTTnhom10.DTOs;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin,Staff")] // Chặn chặt quyền truy cập
public class MembersController : ControllerBase
{
    private readonly string _connectionString;

    public MembersController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection");
    }

    // --- 1. GET: Lấy danh sách hội viên (Có tìm kiếm) ---
    [HttpGet]
    public async Task<IActionResult> GetAllMembers([FromQuery] string search = "")
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = @"
            SELECT m.MemberID, m.UserID, u.Username, u.Email, 
                   m.FullName, m.Phone, m.DateOfBirth, m.Gender, u.IsActive
            FROM MEMBERS m
            JOIN USERS u ON m.UserID = u.UserID
            WHERE m.FullName LIKE @Search OR m.Phone LIKE @Search
            ORDER BY m.MemberID DESC";

        var members = await connection.QueryAsync<MemberResponseDto>(sql, new { Search = $"%{search}%" });
        return Ok(members);
    }

    // --- 2. POST: Thêm mới hội viên (Transaction 2 bảng) ---
    [HttpPost]
    public async Task<IActionResult> CreateMember([FromBody] CreateMemberRequest req)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // 2.1 Insert vào bảng USERS trước (RoleID = 4 là Member)
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            var sqlUser = @"
                INSERT INTO USERS (Username, PasswordHash, Email, RoleID, IsActive) 
                OUTPUT INSERTED.UserID
                VALUES (@Username, @PasswordHash, @Email, 4, 1)";

            int newUserId = await connection.ExecuteScalarAsync<int>(sqlUser,
                new { req.Username, PasswordHash = passwordHash, req.Email }, transaction);

            // 2.2 Insert vào bảng MEMBERS với UserID vừa tạo
            var sqlMember = @"
                INSERT INTO MEMBERS (UserID, FullName, Phone, DateOfBirth, Gender) 
                VALUES (@UserID, @FullName, @Phone, @DateOfBirth, @Gender)";

            await connection.ExecuteAsync(sqlMember,
                new { UserID = newUserId, req.FullName, req.Phone, req.DateOfBirth, req.Gender }, transaction);

            transaction.Commit();
            return Ok(new { Message = "Thêm hội viên thành công!" });
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return BadRequest(new { Message = "Lỗi tạo hội viên: " + ex.Message });
        }
    }

    // --- 3. PUT: Cập nhật thông tin hội viên ---
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateMember(int id, [FromBody] UpdateMemberRequest req)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = @"
            UPDATE MEMBERS 
            SET FullName = @FullName, Phone = @Phone, DateOfBirth = @DateOfBirth, Gender = @Gender 
            WHERE MemberID = @Id";

        var affected = await connection.ExecuteAsync(sql,
            new { req.FullName, req.Phone, req.DateOfBirth, req.Gender, Id = id });

        if (affected == 0) return NotFound("Không tìm thấy hội viên!");
        return Ok(new { Message = "Cập nhật thành công!" });
    }

    // --- 4. DELETE: Xóa mềm hội viên (Khóa tài khoản) ---
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMember(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        // Cập nhật IsActive = 0 ở bảng USERS thay vì xóa hẳn Data
        var sql = @"
            UPDATE u SET u.IsActive = 0 
            FROM USERS u
            JOIN MEMBERS m ON u.UserID = m.UserID
            WHERE m.MemberID = @Id";

        var affected = await connection.ExecuteAsync(sql, new { Id = id });

        if (affected == 0) return NotFound("Không tìm thấy hội viên!");
        return Ok(new { Message = "Đã khóa hồ sơ hội viên thành công!" });
    }
}
