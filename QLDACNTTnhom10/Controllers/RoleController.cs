using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace QLDACNTTnhom10.Controllers;
[Route("api/[controller]")]
[ApiController]
public class RoleController : ControllerBase
{
    // 1. Ai có Token hợp lệ (bất kể Role gì) cũng vào được
    [HttpGet("public-profile")]
    [Authorize]
    public IActionResult GetPublicProfile()
    {
        // Trích xuất UserID từ Token
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Ok(new { Message = $"Xin chào UserID: {userId}. Bạn đã đăng nhập thành công!" });
    }

    // 2. CHỈ DÀNH CHO ADMIN
    [HttpGet("admin-only")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetAdminDashboard()
    {
        return Ok(new { Message = "Dữ liệu mật: Chỉ Admin mới nhìn thấy luồng này!" });
    }

    // 3. DÀNH CHO NHIỀU ROLE (Staff và Admin đều được)
    [HttpGet("staff-area")]
    [Authorize(Roles = "Admin,Staff")]
    public IActionResult GetStaffData()
    {
        return Ok(new { Message = "Khu vực lập hợp đồng: Dành cho Lễ tân và Admin." });
    }
}
