namespace RedeStore.Application.Eventos.Dtos;

public sealed record EventoDto(
    Guid Id,
    string Titulo,
    string Descricao,
    DateTime DataHora,
    string Local,
    decimal Preco,
    int VagasTotais,
    int VagasRestantes,
    string Foto);
