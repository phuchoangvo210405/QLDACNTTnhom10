using Dapper;
using Microsoft.Data.SqlClient;

namespace QLDACNTTNhom10.Services
{
    public class ContractStatusJob : BackgroundService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ContractStatusJob> _logger;
        private readonly EmailService _emailService;

        public ContractStatusJob(IConfiguration config, ILogger<ContractStatusJob> logger, EmailService emailService)
        {
            _config = config;
            _logger = logger;
            _emailService = emailService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Cronjob Quản lý hạn hợp đồng & Gửi Email tự động đã khởi động.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessDailyContractTasksAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi chạy Cronjob hợp đồng và gửi email.");
                }

                // Chạy định kỳ mỗi 24 giờ
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        // Hàm public để có thể gọi test trực tiếp từ Swagger
        public async Task<object> ProcessDailyContractTasksAsync()
        {
            string connectionString = _config.GetConnectionString("DefaultConnection")!;
            using var connection = new SqlConnection(connectionString);

            // 1. Quét và cập nhật các hợp đồng đã hết hạn
            var expireSql = @"
                UPDATE CONTRACTS 
                SET Status = N'Expired', UpdatedAt = GETDATE()
                WHERE Status = N'Active' AND EndDate < CAST(GETDATE() AS DATE)";
            int expiredCount = await connection.ExecuteAsync(expireSql);

            // 2. TASK-356: Tìm hội viên còn đúng 7 ngày hoặc 3 ngày hết hạn (chưa nhắc trong hôm nay)
            var memberRemindSql = @"
                SELECT 
                    c.ContractID, c.EndDate, 
                    DATEDIFF(DAY, CAST(GETDATE() AS DATE), c.EndDate) AS DaysLeft,
                    m.FullName, m.Phone, u.Email, p.PackageName
                FROM CONTRACTS c
                JOIN MEMBERS m ON c.MemberID = m.MemberID
                JOIN USERS u ON m.UserID = u.UserID
                JOIN PACKAGES p ON c.PackageID = p.PackageID
                WHERE c.Status = N'Active'
                  AND DATEDIFF(DAY, CAST(GETDATE() AS DATE), c.EndDate) IN (3, 7)
                  AND (c.LastRemindedDate IS NULL OR CAST(c.LastRemindedDate AS DATE) < CAST(GETDATE() AS DATE))";

            var reminders = (await connection.QueryAsync(memberRemindSql)).ToList();
            int memberEmailsSent = 0;

            foreach (var item in reminders)
            {
                string subject = $"[Nhắc nhở] Gói tập {item.PackageName} của bạn sẽ hết hạn sau {item.DaysLeft} ngày";
                string body = $@"
                    <h3>Xin chào {item.FullName},</h3>
                    <p>Hệ thống phòng Gym & Yoga xin thông báo hợp đồng gói tập <b>{item.PackageName}</b> của bạn sẽ hết hạn vào ngày <b>{((DateTime)item.EndDate):dd/MM/yyyy}</b> (còn <b>{item.DaysLeft} ngày</b>).</p>
                    <p>Hãy liên hệ quầy Lễ tân để gia hạn sớm và nhận ngay ưu đãi tái ký nhé!</p>";

                await _emailService.SendEmailAsync((string)item.Email, subject, body);

                // Cập nhật LastRemindedDate để chống gửi trùng lặp trong ngày
                await connection.ExecuteAsync(
                    "UPDATE CONTRACTS SET LastRemindedDate = GETDATE() WHERE ContractID = @ContractID",
                    new { item.ContractID });

                memberEmailsSent++;
            }

            
            return new
            {
                ExpiredUpdated = expiredCount,
                MemberReminderEmailsSent = memberEmailsSent,
                
            };
        }
    }
}