using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> CadastrarAsync(CadastroRequest request, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UsuarioDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<UsuarioDto> ObterComAutorizacaoAsync(Guid idSolicitado, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct);
    Task<UsuarioDto> AtualizarPerfilAsync(Guid usuarioId, AtualizarPerfilRequest request, CancellationToken ct);
    Task RecuperarSenhaAsync(string email, CancellationToken ct);
    Task RedefinirSenhaAsync(string token, string novaSenha, CancellationToken ct);
}
