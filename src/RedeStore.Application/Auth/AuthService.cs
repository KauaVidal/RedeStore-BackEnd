using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly FrontendOptions _frontendOptions;

    public AuthService(
        IUsuarioRepository usuarioRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailSender emailSender,
        IOptions<FrontendOptions> frontendOptions)
    {
        _usuarioRepository = usuarioRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailSender = emailSender;
        _frontendOptions = frontendOptions.Value;
    }

    public async Task<AuthResponse> CadastrarAsync(CadastroRequest request, CancellationToken ct)
    {
        var email = EmailUsuario.Normalizar(request.Email);
        var existente = await _usuarioRepository.BuscarPorEmailAsync(email, ct);
        if (existente is not null)
        {
            throw new EmailEmUsoException($"O e-mail '{email}' já está em uso.");
        }

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome.Trim(),
            Email = email,
            Papel = Papel.Jovem,
            SenhaHash = _passwordHasher.HashPassword(request.Senha),
        };

        await _usuarioRepository.AdicionarAsync(usuario, ct);

        var token = _jwtTokenGenerator.GenerateToken(usuario.Id, usuario.Email, usuario.Papel.ToString().ToLowerInvariant());
        return new AuthResponse(MapearParaDto(usuario), token);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorEmailAsync(EmailUsuario.Normalizar(request.Email), ct);
        if (usuario is null || !_passwordHasher.VerifyPassword(usuario.SenhaHash, request.Senha))
        {
            throw new CredenciaisInvalidasException("E-mail ou senha inválidos.");
        }

        var token = _jwtTokenGenerator.GenerateToken(usuario.Id, usuario.Email, usuario.Papel.ToString().ToLowerInvariant());
        return new AuthResponse(MapearParaDto(usuario), token);
    }

    public async Task<UsuarioDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("Usuário não encontrado.");
        return MapearParaDto(usuario);
    }

    public async Task<UsuarioDto> ObterComAutorizacaoAsync(Guid idSolicitado, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct)
    {
        if (idSolicitado != idUsuarioLogado && !ehAdmin)
        {
            throw new AcessoNegadoException("Você não tem permissão para ver este usuário.");
        }
        return await ObterPorIdAsync(idSolicitado, ct);
    }

    public async Task<UsuarioDto> AtualizarPerfilAsync(Guid usuarioId, AtualizarPerfilRequest request, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorIdAsync(usuarioId, ct)
            ?? throw new InvalidOperationException("Usuário autenticado não encontrado.");

        var novoEmail = request.Email is null ? null : EmailUsuario.Normalizar(request.Email);
        if (novoEmail is not null && novoEmail != usuario.Email)
        {
            var existente = await _usuarioRepository.BuscarPorEmailAsync(novoEmail, ct);
            if (existente is not null && existente.Id != usuario.Id)
            {
                throw new EmailEmUsoException($"O e-mail '{novoEmail}' já está em uso.");
            }
            usuario.Email = novoEmail;
        }

        if (request.Nome is not null)
        {
            usuario.Nome = request.Nome.Trim();
        }

        if (request.Telefone is not null)
        {
            usuario.Telefone = request.Telefone;
        }

        await _usuarioRepository.AtualizarAsync(usuario, ct);
        return MapearParaDto(usuario);
    }

    public async Task RecuperarSenhaAsync(string email, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorEmailAsync(EmailUsuario.Normalizar(email), ct);
        if (usuario is null)
        {
            return;
        }

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var tokenBruto = Convert.ToBase64String(tokenBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenBruto)));

        await _passwordResetTokenRepository.AdicionarAsync(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = tokenHash,
            ExpiraEm = DateTime.UtcNow.AddHours(1),
        }, ct);

        var link = $"{_frontendOptions.ResetPasswordUrl}?token={tokenBruto}";
        await _emailSender.EnviarAsync(
            usuario.Email,
            "Recuperação de senha — REDE",
            $"<p>Clique no link para redefinir sua senha: <a href=\"{link}\">{link}</a></p><p>Este link expira em 1 hora.</p>",
            ct);
    }

    public async Task RedefinirSenhaAsync(string token, string novaSenha, CancellationToken ct)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var resetToken = await _passwordResetTokenRepository.BuscarPorTokenHashAsync(tokenHash, ct);

        if (resetToken is null || resetToken.UsadoEm is not null || resetToken.ExpiraEm < DateTime.UtcNow)
        {
            throw new TokenInvalidoException("Token de redefinição de senha inválido ou expirado.");
        }

        var usuario = await _usuarioRepository.BuscarPorIdAsync(resetToken.UsuarioId, ct)
            ?? throw new TokenInvalidoException("Token de redefinição de senha inválido ou expirado.");

        usuario.SenhaHash = _passwordHasher.HashPassword(novaSenha);
        await _usuarioRepository.AtualizarAsync(usuario, ct);

        resetToken.UsadoEm = DateTime.UtcNow;
        await _passwordResetTokenRepository.AtualizarAsync(resetToken, ct);
    }

    private static UsuarioDto MapearParaDto(Usuario usuario) =>
        new(usuario.Id, usuario.Nome, usuario.Email, usuario.Telefone, usuario.Papel.ToString().ToLowerInvariant());
}
