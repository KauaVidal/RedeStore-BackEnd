using Npgsql;

namespace RedeStore.Infrastructure.Persistence;

/// <summary>
/// Aceita a connection string tanto no formato chave=valor do Npgsql quanto no formato URI
/// (<c>postgres://usuario:senha@host:porta/banco?sslmode=require</c>), que é o formato entregue
/// por provedores gerenciados como Neon, Supabase, Render e Railway na variável <c>DATABASE_URL</c>.
/// </summary>
public static class PostgresConnectionString
{
    public static string Normalizar(string connectionString)
    {
        var valor = connectionString.Trim();
        if (!valor.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !valor.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return valor;
        }

        var uri = new Uri(valor);
        var credenciais = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credenciais[0]),
        };

        if (credenciais.Length > 1)
        {
            builder.Password = Uri.UnescapeDataString(credenciais[1]);
        }

        foreach (var parametro in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = parametro.Split('=', 2);
            var chave = Uri.UnescapeDataString(partes[0]).ToLowerInvariant();
            var conteudo = partes.Length > 1 ? Uri.UnescapeDataString(partes[1]) : string.Empty;

            switch (chave)
            {
                case "sslmode":
                    builder.SslMode = Enum.Parse<SslMode>(conteudo.Replace("-", string.Empty), ignoreCase: true);
                    break;
                case "channel_binding":
                    builder.ChannelBinding = Enum.Parse<ChannelBinding>(conteudo, ignoreCase: true);
                    break;
                // Demais parâmetros de URI (ex.: options, connect_timeout) não têm equivalente direto; são ignorados.
            }
        }

        return builder.ConnectionString;
    }
}
