namespace RedeStore.Application.Pedidos.Dtos;

public sealed record EnderecoDto(string Rua, string Numero, string? Complemento, string Bairro, string Cidade, string Cep);
