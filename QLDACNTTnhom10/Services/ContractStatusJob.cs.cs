namespace QLDACNTTnhom10.Services;
using Microsoft.Data.SqlClient;
using Dapper;

public class ContractStatusJob : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ContractStatusJob> _logger;

    public ContractStatusJob(IConfiguration config, ILogger<ContractStatusJob> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background Job quét hợp đồng bắt đầu khởi động.");

        // Vòng lặp chạy liên tục cho đến khi server bị tắt
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAndExpireContractsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi chạy Job quét hợp đồng.");
            }

            // Đợi 24 giờ (86400000 milliseconds) rồi mới quét lại
            // Để test nhanh trong quá trình code, bạn có thể đổi thành TimeSpan.FromMinutes(1)
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task ScanAndExpireContractsAsync()
    {
        string connectionString = _config.GetConnectionString("DefaultConnection");
        using var connection = new SqlConnection(connectionString);

        // Quét các hợp đồng đang 'Active' nhưng có ngày kết thúc (EndDate) nhỏ hơn ngày hôm nay
        var sql = @"
            UPDATE CONTRACTS 
            SET Status = 'Expired', UpdatedAt = GETDATE()
            WHERE Status = 'Active' AND EndDate < CAST(GETDATE() AS DATE)";

        int updatedCount = await connection.ExecuteAsync(sql);

        if (updatedCount > 0)
        {
            _logger.LogInformation($"[Tự động] Đã quét và cập nhật {updatedCount} hợp đồng sang trạng thái Hết Hạn.");
        }
    }
}