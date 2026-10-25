using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace QLDACNTTNhom10.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TrainersController : ControllerBase
    {
        private readonly string _connectionString;
        public TrainersController(IConfiguration config) => _connectionString = config.GetConnectionString("DefaultConnection")!;

        // GET /api/trainers
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllTrainers()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT t.TrainerID, t.UserID, u.Username, u.Email, t.Specialization, t.Phone, u.IsActive, t.CreatedAt
                FROM TRAINERS t
                JOIN USERS u ON t.UserID = u.UserID
                WHERE u.IsActive = 1
                ORDER BY t.TrainerID ASC";
            return Ok(await connection.QueryAsync(sql));
        }
    }
}