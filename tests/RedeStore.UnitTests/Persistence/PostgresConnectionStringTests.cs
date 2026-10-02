using Npgsql;
using RedeStore.Infrastructure.Persistence;
using Xunit;

namespace RedeStore.UnitTests.Persistence;

public class PostgresConnectionStringTests
{
    [Fact]
    public void Normalizar_FormatoChaveValor_RetornaSemAlteracao()
    {
        const string original = "Host=localhost;Port=5432;Database=redestore;Username=postgres;Password=postgres";

        Assert.Equal(original, PostgresConnectionString.Normalizar(original));
    }

    [Fact]
    public void Normalizar_FormatoUri_ConverteParaChaveValor()
    {
        var resultado = PostgresConnectionString.Normalizar(
            "postgresql://usuario:s%40nha%3Aforte@ep-exemplo.sa-east-1.aws.neon.tech/redestore?sslmode=require&channel_binding=require");

        var builder = new NpgsqlConnectionStringBuilder(resultado);
        Assert.Equal("ep-exemplo.sa-east-1.aws.neon.tech", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("redestore", builder.Database);
        Assert.Equal("usuario", builder.Username);
        Assert.Equal("s@nha:forte", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
        Assert.Equal(ChannelBinding.Require, builder.ChannelBinding);
    }

    [Fact]
    public void Normalizar_UriComPortaEPrefixoPostgres_UsaPortaInformada()
    {
        var builder = new NpgsqlConnectionStringBuilder(
            PostgresConnectionString.Normalizar("postgres://app:segredo@db.exemplo.com:6543/loja?sslmode=verify-full"));

        Assert.Equal(6543, builder.Port);
        Assert.Equal("loja", builder.Database);
        Assert.Equal(SslMode.VerifyFull, builder.SslMode);
    }
}
