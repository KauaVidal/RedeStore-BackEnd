using System.Net.Http.Headers;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

builder.Services.AddDbContext<RedeStoreDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default ausente. Configure via dotnet user-secrets.")));

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

builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection(FrontendOptions.SectionName));
builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection(ResendOptions.SectionName));

var resendApiKey = builder.Configuration["Resend:ApiKey"]
    ?? throw new InvalidOperationException("Resend:ApiKey ausente. Configure via dotnet user-secrets.");

builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resendApiKey);
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

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddOpenApi(OpenApiConfiguration.Configurar);

var app = builder.Build();

// A geração do openapi.json em build (GetDocument.Insider) executa este Program sem banco disponível.
var geracaoDeDocumentoOpenApi = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

if (!geracaoDeDocumentoOpenApi)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<RedeStoreDbContext>();
    await dbContext.Database.MigrateAsync();
}

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
