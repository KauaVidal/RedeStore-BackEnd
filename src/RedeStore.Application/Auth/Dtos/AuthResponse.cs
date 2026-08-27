namespace RedeStore.Application.Auth.Dtos;

public sealed record AuthResponse(UsuarioDto Usuario, string Token);
