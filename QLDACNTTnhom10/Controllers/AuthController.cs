using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using QLDACNTTnhom10.DTOs;
namespace QLDACNTTnhom10.Controllers;
[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly string _connectionString;

    public AuthController(IConfiguration config)
    {
        _config = config;
        _connectionString = _config.GetConnectionString("DefaultConnection");
    }

    // --- 1. API ĐĂNG KÝ (REGISTER) ---
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto req)
    {
        using var connection = new SqlConnection(_connectionString);

        // Kiểm tra Username hoặc Email đã tồn tại chưa
        var checkSql = "SELECT COUNT(1) FROM USERS WHERE Username = @Username OR Email = @Email";
        var exists = await connection.ExecuteScalarAsync<int>(checkSql, new { req.Username, req.Email });
        if (exists > 0) return BadRequest("Username hoặc Email đã tồn tại!");

        // Băm mật khẩu bằng BCrypt
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);

        // Lưu vào DB
        var insertSql = @"INSERT INTO USERS (Username, PasswordHash, Email, RoleID, IsActive) 
                          VALUES (@Username, @PasswordHash, @Email, @RoleID, 1)";
        await connection.ExecuteAsync(insertSql, new
        {
            req.Username,
            PasswordHash = passwordHash,
            req.Email,
            req.RoleID
        });

        return Ok(new { Message = "Đăng ký thành công!" });
    }

    // --- 2. API ĐĂNG NHẬP (LOGIN) ---
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto req)
    {
        using var connection = new SqlConnection(_connectionString);

        // Lấy User và RoleName tương ứng
        var sql = @"SELECT u.UserID, u.Username, u.PasswordHash, u.RoleID, r.RoleName, u.IsActive 
                    FROM USERS u 
                    JOIN ROLES r ON u.RoleID = r.RoleID 
                    WHERE u.Username = @Username";

        var user = await connection.QuerySingleOrDefaultAsync<UserDto>(sql, new { req.Username });

        // Validate tài khoản
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized("Sai tài khoản hoặc mật khẩu!");

        if (!user.IsActive)
            return Forbid("Tài khoản đã bị khóa!");

        // Tạo JWT Token
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]);

        // Gắn payload (thông tin mang theo token)
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.RoleName) // Quan trọng để phân quyền sau này
            }),
            Expires = DateTime.UtcNow.AddHours(8), // Token sống 8 tiếng
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);

        return Ok(new
        {
            Token = tokenHandler.WriteToken(token),
            Role = user.RoleName
        });
    }
}
