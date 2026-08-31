namespace RedeStore.Application.Eventos.Dtos;

public sealed record CriarEventoRequest(
    string Titulo,
    string Descricao,
    DateTime DataHora,
    string Local,
    decimal Preco,
    int VagasTotais,
    string Foto);
