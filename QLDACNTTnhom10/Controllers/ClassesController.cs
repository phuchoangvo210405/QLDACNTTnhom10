namespace QLDACNTTnhom10.Controllers;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QLDACNTTnhom10.DTOs;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin,Staff")]
public class ClassesController : ControllerBase
{
    private readonly string _connectionString;

    public ClassesController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection");
    }

    // TASK-360: API Tạo lớp học và phân công PT
    [HttpPost]
    public async Task<IActionResult> CreateClass([FromBody] CreateClassRequest req)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = @"
            INSERT INTO CLASSES (ClassName, TrainerID, Capacity) 
            OUTPUT INSERTED.ClassID 
            VALUES (@ClassName, @TrainerID, @Capacity)";

        int newClassId = (int)(await connection.ExecuteScalarAsync(sql, req));
        return Ok(new { Message = "Tạo lớp và phân công HLV thành công!", ClassID = newClassId });
    }

    // TASK-389: API Tạo ca học và gán phòng tập
    [HttpPost("schedules")]
    public async Task<IActionResult> CreateSchedule([FromBody] CreateScheduleRequest req)
    {
        // 1. Kiểm tra giờ hoạt động (7h - 23h)
        if (req.StartTime.TimeOfDay < TimeSpan.FromHours(7) || req.EndTime.TimeOfDay > TimeSpan.FromHours(23))
        {
            return BadRequest("Lịch học phải nằm trong khung giờ hoạt động từ 07:00 đến 23:00.");
        }

        if (req.StartTime >= req.EndTime)
        {
            return BadRequest("Giờ bắt đầu phải nhỏ hơn giờ kết thúc.");
        }

        using var connection = new SqlConnection(_connectionString);

        // 2. Thuật toán kiểm tra trùng lịch (Overlap Checking)
        // Lịch bị trùng khi: (Giờ BĐ mới < Giờ KT cũ) VÀ (Giờ KT mới > Giờ BĐ cũ)
        var checkOverlapSql = @"
            SELECT COUNT(1) FROM CLASS_SCHEDULES 
            WHERE Room = @Room 
              AND (CAST(StartTime AS DATE) = CAST(@StartTime AS DATE))
              AND (@StartTime < EndTime AND @EndTime > StartTime)";

        var overlapCount = (int)await connection.ExecuteScalarAsync(checkOverlapSql, new
        {
            req.Room,
            req.StartTime,
            req.EndTime
        });

        if (overlapCount > 0)
        {
            return BadRequest($"Phòng {req.Room} đã có lớp học khác đăng ký trong khoảng thời gian này.");
        }

        // 3. Thêm lịch học vào Database
        var insertSql = @"
            INSERT INTO CLASS_SCHEDULES (ClassID, Room, StartTime, EndTime)
            OUTPUT INSERTED.ScheduleID
            VALUES (@ClassID, @Room, @StartTime, @EndTime)";

        int scheduleId = (int)await connection.ExecuteScalarAsync(insertSql, req);
        return Ok(new { Message = "Xếp phòng và tạo ca học thành công!", ScheduleID = scheduleId });
    }
    // ==========================================
    // 1. NGHIỆP VỤ SỬA / XÓA LỚP HỌC (CLASSES)
    // ==========================================

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateClass(int id, [FromBody] CreateClassRequest req)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = @"
            UPDATE CLASSES 
            SET ClassName = @ClassName, TrainerID = @TrainerID, Capacity = @Capacity
            WHERE ClassID = @Id";

        var affected = await connection.ExecuteAsync(sql, new
        {
            req.ClassName,
            req.TrainerID,
            req.Capacity,
            Id = id
        });

        if (affected == 0) return NotFound("Không tìm thấy lớp học.");
        return Ok(new { Message = "Cập nhật lớp học thành công!" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteClass(int id)
    {
        using var connection = new SqlConnection(_connectionString);

        // Dùng Soft Delete: Chỉ đổi IsActive = 0 để không bẻ gãy Hợp đồng cũ
        var sql = "UPDATE CLASSES SET IsActive = 0 WHERE ClassID = @Id";
        var affected = await connection.ExecuteAsync(sql, new { Id = id });

        if (affected == 0) return NotFound("Không tìm thấy lớp học.");
        return Ok(new { Message = "Đã hủy/ngừng hoạt động lớp học này!" });
    }

    // ==========================================
    // 2. NGHIỆP VỤ SỬA / XÓA CA HỌC (SCHEDULES)
    // ==========================================

    [HttpPut("schedules/{id}")]
    public async Task<IActionResult> UpdateSchedule(int id, [FromBody] CreateScheduleRequest req)
    {
        if (req.StartTime.TimeOfDay < TimeSpan.FromHours(7) || req.EndTime.TimeOfDay > TimeSpan.FromHours(23))
            return BadRequest("Lịch học phải nằm trong khung giờ 07:00 - 23:00.");

        using var connection = new SqlConnection(_connectionString);

        // ĐIỂM QUAN TRỌNG: Check trùng lịch nhưng phải LOẠI TRỪ chính cái ScheduleID đang sửa
        var checkOverlapSql = @"
            SELECT COUNT(1) FROM CLASS_SCHEDULES 
            WHERE Room = @Room 
              AND ScheduleID != @Id 
              AND (CAST(StartTime AS DATE) = CAST(@StartTime AS DATE))
              AND (@StartTime < EndTime AND @EndTime > StartTime)";

        var overlapCount = (int)await connection.ExecuteScalarAsync(checkOverlapSql, new
        {
            req.Room,
            req.StartTime,
            req.EndTime,
            Id = id
        });

        if (overlapCount > 0)
            return BadRequest($"Phòng {req.Room} đã bị trùng lịch trong khung giờ này.");

        var sql = @"
            UPDATE CLASS_SCHEDULES 
            SET Room = @Room, StartTime = @StartTime, EndTime = @EndTime 
            WHERE ScheduleID = @Id";

        var affected = await connection.ExecuteAsync(sql, new
        {
            req.Room,
            req.StartTime,
            req.EndTime,
            Id = id
        });

        return Ok(new { Message = "Đổi giờ/phòng ca học thành công!" });
    }

    [HttpDelete("schedules/{id}")]
    public async Task<IActionResult> DeleteSchedule(int id)
    {
        using var connection = new SqlConnection(_connectionString);

        // Trước khi xóa ca học, phải check xem đã có ai điểm danh (Check-in) vào ca này chưa
        var checkInSql = "SELECT COUNT(1) FROM CHECK_INS WHERE ScheduleID = @Id";
        var hasCheckIns = (int)await connection.ExecuteScalarAsync(checkInSql, new { Id = id });

        if (hasCheckIns > 0)
            return BadRequest("Không thể xóa ca học này vì đã có hội viên điểm danh tham gia.");

        // Nếu chưa có ai học, cho phép xóa cứng (Hard Delete)
        var sql = "DELETE FROM CLASS_SCHEDULES WHERE ScheduleID = @Id";
        await connection.ExecuteAsync(sql, new { Id = id });

        return Ok(new { Message = "Đã xóa ca học thành công!" });
    }
}
