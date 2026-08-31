namespace RedeStore.Domain.Entities;

public class Endereco
{
    public required string Rua { get; set; }
    public required string Numero { get; set; }
    public string? Complemento { get; set; }
    public required string Bairro { get; set; }
    public required string Cidade { get; set; }
    public required string Cep { get; set; }
}
