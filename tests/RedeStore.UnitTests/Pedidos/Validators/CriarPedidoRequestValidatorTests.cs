using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Application.Pedidos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Pedidos.Validators;

public class CriarPedidoRequestValidatorTests
{
    private readonly CriarPedidoRequestValidator _validator = new();

    private static CriarPedidoRequest RequestValido() => new(
        Itens: [new ItemPedidoRequest(Guid.NewGuid(), "M", "Preto", 1)],
        FormaEntrega: "retirada",
        Endereco: null);

    [Fact]
    public void Validate_ComRequestValidoRetirada_NaoRetornaErros()
    {
        var resultado = _validator.Validate(RequestValido());

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComItensVazios_RetornaErro()
    {
        var request = RequestValido() with { Itens = [] };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComQuantidadeZero_RetornaErro()
    {
        var request = RequestValido() with { Itens = [new ItemPedidoRequest(Guid.NewGuid(), "M", "Preto", 0)] };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComFormaEntregaInvalida_RetornaErro()
    {
        var request = RequestValido() with { FormaEntrega = "teleporte" };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComFormaEntregaEntregaSemEndereco_RetornaErro()
    {
        var request = RequestValido() with { FormaEntrega = "entrega", Endereco = null };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComFormaEntregaEntregaEEnderecoCompleto_NaoRetornaErros()
    {
        var endereco = new EnderecoDto("Rua A", "1", null, "Centro", "SP", "01000-000");
        var request = RequestValido() with { FormaEntrega = "entrega", Endereco = endereco };

        var resultado = _validator.Validate(request);

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComEnderecoComCepVazio_RetornaErro()
    {
        var endereco = new EnderecoDto("Rua A", "1", null, "Centro", "SP", "");
        var request = RequestValido() with { FormaEntrega = "entrega", Endereco = endereco };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
