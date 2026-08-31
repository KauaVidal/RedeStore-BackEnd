using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Eventos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Eventos.Validators;

public class AtualizarEventoRequestValidatorTests
{
    private readonly AtualizarEventoRequestValidator _validator = new();

    [Fact]
    public void Validate_ComTodosOsCamposNulos_NaoRetornaErros()
    {
        var request = new AtualizarEventoRequest(null, null, null, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComTituloVazio_RetornaErro()
    {
        var request = new AtualizarEventoRequest("", null, null, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoNegativo_RetornaErro()
    {
        var request = new AtualizarEventoRequest(null, null, null, null, -1m, null, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComVagasTotaisZero_RetornaErro()
    {
        var request = new AtualizarEventoRequest(null, null, null, null, null, 0, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
