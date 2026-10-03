using RedeStore.Application.Produtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.UnitTests.Produtos;

public class CategoriasProdutoTests
{
    [Theory]
    [InlineData("camisetas", CategoriaProduto.Camisetas)]
    [InlineData("calcas", CategoriaProduto.Calcas)]
    [InlineData("calcados", CategoriaProduto.Calcados)]
    [InlineData("vestidos", CategoriaProduto.Vestidos)]
    public void Converter_ComValorConhecido_RetornaCategoria(string valor, CategoriaProduto esperada)
    {
        Assert.Equal(esperada, CategoriasProduto.Converter(valor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Camisetas")]
    [InlineData("sapatos")]
    public void Converter_ComValorDesconhecido_RetornaNulo(string? valor)
    {
        Assert.Null(CategoriasProduto.Converter(valor));
    }

    [Fact]
    public void Valores_CobremTodoOEnumEmMinusculas()
    {
        foreach (var categoria in Enum.GetValues<CategoriaProduto>())
        {
            var valor = CategoriasProduto.ParaValor(categoria);
            Assert.Equal(valor.ToLowerInvariant(), valor);
            Assert.Contains(valor, CategoriasProduto.Valores);
            Assert.Equal(categoria, CategoriasProduto.Converter(valor));
        }
    }
}
