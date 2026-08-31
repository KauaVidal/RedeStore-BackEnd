namespace RedeStore.Application.Inscricoes.Dtos;

public sealed record InscricaoDto(
    Guid Id,
    Guid EventoId,
    Guid UsuarioId,
    string Status,
    decimal ValorPago,
    DateTime CriadoEm);
