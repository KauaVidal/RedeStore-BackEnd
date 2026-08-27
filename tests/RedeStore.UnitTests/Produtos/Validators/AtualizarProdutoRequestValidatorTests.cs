using RedeStore.Application.Produtos.Dtos;
using RedeStore.Application.Produtos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Produtos.Validators;

public class AtualizarProdutoRequestValidatorTests
{
    private readonly AtualizarProdutoRequestValidator _validator = new();

    [Fact]
    public void Validate_ComTodosOsCamposNulos_NaoRetornaErros()
    {
        var request = new AtualizarProdutoRequest(null, null, null, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComCategoriaInvalida_RetornaErro()
    {
        var request = new AtualizarProdutoRequest(null, "sapatos", null, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoZero_RetornaErro()
    {
        var request = new AtualizarProdutoRequest(null, null, 0m, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComListaDeVariacoesVazia_RetornaErro()
    {
        var request = new AtualizarProdutoRequest(null, null, null, null, null, null, []);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
