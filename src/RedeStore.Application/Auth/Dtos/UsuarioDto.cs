namespace RedeStore.Application.Auth.Dtos;

public sealed record UsuarioDto(Guid Id, string Nome, string Email, string? Telefone, string Papel);
