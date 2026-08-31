using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Eventos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Eventos.Validators;

public class CriarEventoRequestValidatorTests
{
    private readonly CriarEventoRequestValidator _validator = new();

    private static CriarEventoRequest RequestValido() => new(
        Titulo: "Culto Jovem",
        Descricao: "Encontro semanal",
        DataHora: DateTime.UtcNow.AddDays(7),
        Local: "Templo Sede",
        Preco: 0m,
        VagasTotais: 50,
        Foto: "https://exemplo.com/evento.jpg");

    [Fact]
    public void Validate_ComRequestValido_NaoRetornaErros()
    {
        var resultado = _validator.Validate(RequestValido());

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComTituloVazio_RetornaErro()
    {
        var request = RequestValido() with { Titulo = "" };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoNegativo_RetornaErro()
    {
        var request = RequestValido() with { Preco = -1m };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoZero_NaoRetornaErro()
    {
        var request = RequestValido() with { Preco = 0m };

        var resultado = _validator.Validate(request);

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComVagasTotaisZero_RetornaErro()
    {
        var request = RequestValido() with { VagasTotais = 0 };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComFotoVazia_RetornaErro()
    {
        var request = RequestValido() with { Foto = "" };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
