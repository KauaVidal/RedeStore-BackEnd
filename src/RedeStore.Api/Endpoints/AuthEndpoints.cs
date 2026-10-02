using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Api.Filters;
using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/auth").WithTags("Auth");

        grupo.MapPost("/cadastro", async (CadastroRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var resposta = await authService.CadastrarAsync(request, ct);
            return Results.Ok(resposta);
        })
        .AddEndpointFilter<ValidationFilter<CadastroRequest>>()
        .WithSummary("Cadastra um novo usuário (papel 'jovem') e já devolve o token")
        .WithDescription("Regras: nome com 2+ caracteres, e-mail válido e senha com 8+ caracteres. E-mail duplicado retorna 409 EMAIL_EM_USO.")
        .Produces<AuthResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/login", async (LoginRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var resposta = await authService.LoginAsync(request, ct);
            return Results.Ok(resposta);
        })
        .AddEndpointFilter<ValidationFilter<LoginRequest>>()
        .WithSummary("Autentica com e-mail e senha e devolve o token JWT")
        .WithDescription("E-mail inexistente ou senha incorreta retornam o mesmo 401 CREDENCIAIS_INVALIDAS.")
        .Produces<AuthResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        grupo.MapGet("/me", async (ClaimsPrincipal user, IAuthService authService, CancellationToken ct) =>
        {
            var id = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var usuario = await authService.ObterPorIdAsync(id, ct);
            return Results.Ok(usuario);
        })
        .RequireAuthorization()
        .WithSummary("Retorna os dados do usuário autenticado")
        .Produces<UsuarioDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        grupo.MapPatch("/perfil", async (AtualizarPerfilRequest request, ClaimsPrincipal user, IAuthService authService, CancellationToken ct) =>
        {
            var id = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var usuario = await authService.AtualizarPerfilAsync(id, request, ct);
            return Results.Ok(usuario);
        })
        .RequireAuthorization()
        .AddEndpointFilter<ValidationFilter<AtualizarPerfilRequest>>()
        .WithSummary("Atualiza parcialmente o perfil do usuário autenticado")
        .WithDescription("Todos os campos são opcionais; só os enviados (não nulos) são alterados. Trocar para um e-mail já usado retorna 409 EMAIL_EM_USO.")
        .Produces<UsuarioDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/recuperar-senha", async (RecuperarSenhaRequest request, IAuthService authService, CancellationToken ct) =>
        {
            await authService.RecuperarSenhaAsync(request.Email, ct);
            return Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<RecuperarSenhaRequest>>()
        .WithSummary("Envia por e-mail o link de redefinição de senha")
        .WithDescription("Sempre retorna 204, exista ou não o e-mail (evita enumeração de usuários). O link aponta para Frontend:ResetPasswordUrl?token=... e expira em 1 hora.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem();

        grupo.MapPost("/redefinir-senha", async (RedefinirSenhaRequest request, IAuthService authService, CancellationToken ct) =>
        {
            await authService.RedefinirSenhaAsync(request.Token, request.NovaSenha, ct);
            return Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<RedefinirSenhaRequest>>()
        .WithSummary("Redefine a senha usando o token recebido por e-mail")
        .WithDescription("O token é de uso único e expira em 1 hora. Token inválido, expirado ou já usado retorna 400 TOKEN_INVALIDO.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
