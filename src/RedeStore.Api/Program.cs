using RedeStore.Application.Common;
using RedeStore.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

var app = builder.Build();

app.MapGet("/", () => "RedeStore API");

app.Run();
