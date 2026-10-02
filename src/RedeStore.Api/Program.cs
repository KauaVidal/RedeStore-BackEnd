using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RedeStore.Api.Endpoints;
using RedeStore.Api.Middleware;
using RedeStore.Api.OpenApi;
using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Auth.Validators;
using RedeStore.Application.Common;
using RedeStore.Application.Eventos;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Eventos.Validators;
using RedeStore.Application.Inscricoes;
using RedeStore.Application.Pedidos;
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Application.Pedidos.Validators;
using RedeStore.Application.Produtos;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Application.Produtos.Validators;
using RedeStore.Infrastructure.Auth;
using RedeStore.Infrastructure.Email;
using RedeStore.Infrastructure.Persistence;
using RedeStore.Infrastructure.Persistence.Repositories;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "FrontendCorsPolicy";

// Plataformas como Vercel, Render e Railway informam a porta em que o container deve escutar via PORT.
var porta = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(porta))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{porta}");
}

builder.Services.AddDbContext<RedeStoreDbContext>(options =>
{
    // DATABASE_URL é o nome usado pelas integrações de Postgres (Neon/Vercel, Render, Railway).
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? builder.Configuration["DATABASE_URL"]
        ?? throw new InvalidOperationException("ConnectionStrings:Default (ou DATABASE_URL) ausente. Configure via dotnet user-secrets ou variável de ambiente.");
    options.UseNpgsql(PostgresConnectionString.Normalizar(connectionString));
});

builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
builder.Services.AddScoped<IProdutoRepository, ProdutoRepository>();
builder.Services.AddScoped<IEventoRepository, EventoRepository>();
builder.Services.AddScoped<IInscricaoRepository, InscricaoRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IPedidoRepository, PedidoRepository>();
builder.Services.AddScoped<IVariacaoRepository, VariacaoRepository>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IValidator<CadastroRequest>, CadastroRequestValidator>();
builder.Services.AddSingleton<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddSingleton<IValidator<AtualizarPerfilRequest>, AtualizarPerfilRequestValidator>();
builder.Services.AddSingleton<IValidator<RecuperarSenhaRequest>, RecuperarSenhaRequestValidator>();
builder.Services.AddSingleton<IValidator<RedefinirSenhaRequest>, RedefinirSenhaRequestValidator>();

builder.Services.AddScoped<IProdutoService, ProdutoService>();
builder.Services.AddSingleton<IValidator<CriarProdutoRequest>, CriarProdutoRequestValidator>();
builder.Services.AddSingleton<IValidator<AtualizarProdutoRequest>, AtualizarProdutoRequestValidator>();

builder.Services.AddScoped<IEventoService, EventoService>();
builder.Services.AddSingleton<IValidator<CriarEventoRequest>, CriarEventoRequestValidator>();
builder.Services.AddSingleton<IValidator<AtualizarEventoRequest>, AtualizarEventoRequestValidator>();

builder.Services.AddScoped<IInscricaoService, InscricaoService>();

builder.Services.AddScoped<IPedidoService, PedidoService>();
builder.Services.AddSingleton<IValidator<CriarPedidoRequest>, CriarPedidoRequestValidator>();

builder.Services.AddOptions<FrontendOptions>()
    .Bind(builder.Configuration.GetSection(FrontendOptions.SectionName))
    .Validate(o => Uri.TryCreate(o.ResetPasswordUrl, UriKind.Absolute, out _),
              "Frontend:ResetPasswordUrl ausente ou não é uma URL absoluta.")
    .ValidateOnStart();
builder.Services.AddOptions<ResendOptions>()
    .Bind(builder.Configuration.GetSection(ResendOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Resend:ApiKey ausente. Configure via dotnet user-secrets.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.FromEmail), "Resend:FromEmail ausente.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>((serviceProvider, client) =>
{
    var resendOptions = serviceProvider.GetRequiredService<IOptions<ResendOptions>>().Value;
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resendOptions.ApiKey);
});

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && System.Text.Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
              "Jwt:SigningKey ausente ou com menos de 32 bytes (necessário para HMAC-SHA256).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
              "Jwt:Issuer e Jwt:Audience são obrigatórios.")
    .ValidateOnStart();
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = JwtTokenValidationParametersFactory.Create(jwtOptions.Value);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Cors:AllowedOrigin aceita várias origens separadas por vírgula (ex.: domínio próprio + domínio *.vercel.app).
var origensPermitidas = (builder.Configuration["Cors:AllowedOrigin"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(origem => origem.TrimEnd('/'))
    .ToArray();

if (origensPermitidas.Length == 0)
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Cors:AllowedOrigin ausente. Em produção, informe a URL pública do frontend.");
    }
    origensPermitidas = ["http://localhost:4200"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins(origensPermitidas)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Limita tentativas por IP nas rotas públicas de autenticação (força bruta de senha, spam de cadastro
// e de e-mails de recuperação). Atrás de proxy, o IP real depende de ASPNETCORE_FORWARDEDHEADERS_ENABLED=true.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.RateLimitPolicy, httpContext =>
    {
        var limitePorMinuto = httpContext.RequestServices.GetRequiredService<IConfiguration>()
            .GetValue("RateLimiting:AuthPorMinuto", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limitePorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
});

builder.Services.AddOpenApi(OpenApiConfiguration.Configurar);

var app = builder.Build();

// A geração do openapi.json em build (GetDocument.Insider) executa este Program sem banco disponível.
var geracaoDeDocumentoOpenApi = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

// Em plataformas serverless (ex.: Vercel) cada cold start rodaria as migrations; nelas, desligue com
// Database__MigrarNaInicializacao=false e aplique as migrations uma vez por deploy (veja docs/deploy.md).
var migrarNaInicializacao = app.Configuration.GetValue("Database:MigrarNaInicializacao", true);

if (!geracaoDeDocumentoOpenApi && migrarNaInicializacao)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<RedeStoreDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseCors(FrontendCorsPolicy);
app.UseRateLimiter();
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
})
.WithTags("Health")
.WithSummary("Verifica se a API está no ar e conectada ao banco")
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapAuthEndpoints();
app.MapUsuariosEndpoints();
app.MapProdutosEndpoints();
app.MapEventosEndpoints();
app.MapInscricoesEndpoints();
app.MapPedidosEndpoints();

app.Run();

public partial class Program;
