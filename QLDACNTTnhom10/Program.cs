using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QLDACNTTNhom10.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Đọc Key từ appsettings
var jwtKey = builder.Configuration["Jwt:Key"];
var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ClockSkew = TimeSpan.Zero // Hết hạn token là chặn ngay lập tức
        };

        // Bổ sung xử lý phản hồi JSON cho Middleware Phân quyền (TASK-361)
        options.Events = new JwtBearerEvents
        {
            // 1. Khi chưa gửi Token hoặc Token sai/hết hạn (Lỗi 401)
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var result = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Status = 401,
                    Message = "Bạn chưa đăng nhập hoặc phiên đăng nhập (Token) đã hết hạn!"
                });
                return context.Response.WriteAsync(result);
            },
            // 2. Khi đã đăng nhập nhưng Role không được phép truy cập API này (Lỗi 403)
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                var result = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Status = 403,
                    Message = "Truy cập bị từ chối! Vai trò (Role) của bạn không có quyền thực hiện chức năng này."
                });
                return context.Response.WriteAsync(result);
            }
        };
    });

// 1. Thêm thư viện Swagger vào phần builder (Bên trên dòng builder.Build())
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Cấu hình nút Authorize nhập Token trên Swagger
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Nhập 'Bearer' [khoảng trắng] và dán Token của bạn vào.\r\n\r\nVí dụ: 'Bearer eyJhbGci...'"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<ContractStatusJob>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ContractStatusJob>());
builder.Services.AddControllers();
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseAuthentication(); // Bắt buộc đứng trước UseAuthorization
app.UseAuthorization();
app.MapControllers();
app.Run();