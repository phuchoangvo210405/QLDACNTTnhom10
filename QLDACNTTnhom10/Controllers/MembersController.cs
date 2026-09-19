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
               m.FullName, m.Phone, m.DateOfBirth, m.Gender, m.AvatarUrl, u.IsActive
        FROM MEMBERS m
        JOIN USERS u ON m.UserID = u.UserID
        WHERE m.FullName LIKE @Search 
           OR m.Phone LIKE @Search 
           OR CAST(m.MemberID AS NVARCHAR) = @ExactSearch -- Tìm chính xác theo Mã HV
        ORDER BY m.MemberID DESC";

        var members = await connection.QueryAsync<MemberResponseDto>(sql, new
        {
            Search = $"%{search}%",
            ExactSearch = search // Truyền chuỗi gốc vào để check ID
        });

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
    [HttpPost("{id}/avatar")]
    public async Task<IActionResult> UploadAvatar(int id, IFormFile file)
    {
        // 1. Kiểm tra file hợp lệ
        if (file == null || file.Length == 0)
            return BadRequest("Vui lòng chọn file ảnh hợp lệ.");

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var extension = Path.GetExtension(file.FileName).ToLower();
        if (!allowedExtensions.Contains(extension))
            return BadRequest("Chỉ chấp nhận định dạng ảnh hợp lệ (jpg, png, webp).");

        // 2. Tạo thư mục wwwroot/uploads/avatars nếu chưa có
        var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        // 3. Đặt tên file chống trùng lặp (ví dụ: member_1_63830001234.jpg)
        var fileName = $"member_{id}_{DateTime.Now.Ticks}{extension}";
        var filePath = Path.Combine(folderPath, fileName);

        // 4. Copy file vào hệ thống
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // 5. Cập nhật đường dẫn vào Database
        var avatarUrl = $"/uploads/avatars/{fileName}";
        var sql = "UPDATE MEMBERS SET AvatarUrl = @AvatarUrl WHERE MemberID = @Id";

        using var connection = new SqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(sql, new { AvatarUrl = avatarUrl, Id = id });

        if (affected == 0) return NotFound("Không tìm thấy hội viên để cập nhật ảnh.");

        return Ok(new
        {
            Message = "Tải ảnh đại diện thành công!",
            AvatarUrl = avatarUrl
        });
    }
}
