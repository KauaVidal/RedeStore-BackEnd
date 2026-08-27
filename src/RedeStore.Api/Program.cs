using Microsoft.EntityFrameworkCore;
using RedeStore.Api.Middleware;
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Auth;
using RedeStore.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RedeStoreDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => "RedeStore API");

app.Run();
