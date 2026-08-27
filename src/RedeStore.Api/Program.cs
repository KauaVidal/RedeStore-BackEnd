using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using RedeStore.Api.Middleware;
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Auth;
using RedeStore.Infrastructure.Persistence;
using RedeStore.Infrastructure.Persistence.Repositories;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "FrontendCorsPolicy";

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default ausente. Configure via dotnet user-secrets.");

builder.Services.AddDbContext<RedeStoreDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && System.Text.Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
              "Jwt:SigningKey ausente ou com menos de 32 bytes (necessário para HMAC-SHA256).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
              "Jwt:Issuer e Jwt:Audience são obrigatórios.")
    .ValidateOnStart();
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configuração 'Jwt' ausente. Configure via dotnet user-secrets.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = JwtTokenValidationParametersFactory.Create(jwtOptions);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health", async (RedeStoreDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? Results.Ok(new { status = "healthy", database = "connected" })
        : Results.Problem("Não foi possível conectar ao banco de dados.", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

public partial class Program;
