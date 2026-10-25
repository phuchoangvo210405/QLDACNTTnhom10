using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace QLDACNTTNhom10.Controllers
{
    [Route("api/retention")]
    [ApiController]
    [Authorize(Roles = "Admin,Staff")]
    public class AiRetentionController : ControllerBase
    {
        private readonly string _connectionString;
        public AiRetentionController(IConfiguration config) => _connectionString = config.GetConnectionString("DefaultConnection")!;

        // 1. GET /api/retention/predictions (Lọc theo riskLevel = High/Medium/Low)
        [HttpGet("predictions")]
        public async Task<IActionResult> GetPredictions([FromQuery] string? riskLevel = null)
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT 
                    ai.PredictionID, ai.MemberID, m.FullName AS MemberName, m.Phone,
                    ai.ContractID, c.EndDate, p.PackageName,
                    ai.RenewalProbability, ai.RiskLevel, ai.RetentionStrategy, ai.IsHandled, ai.PredictionDate
                FROM AI_PREDICTIONS ai
                JOIN MEMBERS m ON ai.MemberID = m.MemberID
                JOIN CONTRACTS c ON ai.ContractID = c.ContractID
                JOIN PACKAGES p ON c.PackageID = p.PackageID
                WHERE (@RiskLevel IS NULL OR ai.RiskLevel = @RiskLevel)
                ORDER BY ai.RenewalProbability ASC";

            return Ok(await connection.QueryAsync(sql, new { RiskLevel = riskLevel }));
        }

        // 2. PUT /api/retention/predictions/{id}/handle (Đánh dấu IsHandled = 1)
        [HttpPut("predictions/{id}/handle")]
        public async Task<IActionResult> MarkAsHandled(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            var affected = await connection.ExecuteAsync(@"
                UPDATE AI_PREDICTIONS SET IsHandled = 1 WHERE PredictionID = @Id;
                UPDATE CONTRACTS SET LastRemindedDate = GETDATE() 
                WHERE ContractID = (SELECT ContractID FROM AI_PREDICTIONS WHERE PredictionID = @Id);",
                new { Id = id });

            if (affected == 0) return NotFound("Không tìm thấy bản ghi dự báo AI.");
            return Ok(new { Message = "Đã đánh dấu hoàn tất chăm sóc hội viên!" });
        }

        // 3. GET /api/retention/stats (Thống kê tỷ lệ dự báo gia hạn trung bình)
        [HttpGet("stats")]
        public async Task<IActionResult> GetRetentionStats()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT 
                    COUNT(1) AS TotalPredictions,
                    ISNULL(ROUND(AVG(RenewalProbability), 2), 0) AS AverageRenewalProbability,
                    SUM(CASE WHEN RiskLevel = N'High' THEN 1 ELSE 0 END) AS HighRiskCount,
                    SUM(CASE WHEN RiskLevel = N'Medium' THEN 1 ELSE 0 END) AS MediumRiskCount,
                    SUM(CASE WHEN RiskLevel = N'Low' THEN 1 ELSE 0 END) AS LowRiskCount,
                    SUM(CASE WHEN IsHandled = 1 THEN 1 ELSE 0 END) AS HandledCount
                FROM AI_PREDICTIONS";

            return Ok(await connection.QuerySingleAsync(sql));
        }
    }
}