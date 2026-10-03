namespace RedeStore.Application.Auth;

/// <summary>
/// Forma canônica do e-mail de login: sem espaços nas pontas e em minúsculas.
/// Todo e-mail é gravado e buscado assim, então "Joao@Email.com" e "joao@email.com " são a mesma conta.
/// </summary>
public static class EmailUsuario
{
    public static string Normalizar(string email) => email.Trim().ToLowerInvariant();
}
