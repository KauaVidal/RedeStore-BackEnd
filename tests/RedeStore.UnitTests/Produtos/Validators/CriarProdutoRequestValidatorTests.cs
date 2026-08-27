using RedeStore.Application.Produtos.Dtos;
using RedeStore.Application.Produtos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Produtos.Validators;

public class CriarProdutoRequestValidatorTests
{
    private readonly CriarProdutoRequestValidator _validator = new();

    private static CriarProdutoRequest RequestValido() => new(
        Nome: "Camiseta Rede",
        Categoria: "camisetas",
        Preco: 79.90m,
        Descricao: "Camiseta oficial",
        Fotos: ["https://exemplo.com/foto.jpg"],
        Destaque: false,
        Variacoes: [new VariacaoRequest("M", "Preto", 10)]);

    [Fact]
    public void Validate_ComRequestValido_NaoRetornaErros()
    {
        var resultado = _validator.Validate(RequestValido());

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComCategoriaInvalida_RetornaErro()
    {
        var request = RequestValido() with { Categoria = "sapatos" };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoZero_RetornaErro()
    {
        var request = RequestValido() with { Preco = 0 };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_SemVariacoes_RetornaErro()
    {
        var request = RequestValido() with { Variacoes = [] };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComVariacaoComEstoqueNegativo_RetornaErro()
    {
        var request = RequestValido() with { Variacoes = [new VariacaoRequest("M", "Preto", -1)] };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
