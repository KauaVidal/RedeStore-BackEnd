using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(IUsuarioRepository usuarioRepository, IPasswordHasher passwordHasher, IJwtTokenGenerator jwtTokenGenerator)
    {
        _usuarioRepository = usuarioRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResponse> CadastrarAsync(CadastroRequest request, CancellationToken ct)
    {
        var existente = await _usuarioRepository.BuscarPorEmailAsync(request.Email, ct);
        if (existente is not null)
        {
            throw new EmailEmUsoException($"O e-mail '{request.Email}' já está em uso.");
        }

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome,
            Email = request.Email,
            Papel = Papel.Jovem,
            SenhaHash = _passwordHasher.HashPassword(request.Senha),
        };

        await _usuarioRepository.AdicionarAsync(usuario, ct);

        var token = _jwtTokenGenerator.GenerateToken(usuario.Id, usuario.Email, usuario.Papel.ToString().ToLowerInvariant());
        return new AuthResponse(MapearParaDto(usuario), token);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorEmailAsync(request.Email, ct);
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

        if (request.Email is not null && request.Email != usuario.Email)
        {
            var existente = await _usuarioRepository.BuscarPorEmailAsync(request.Email, ct);
            if (existente is not null)
            {
                throw new EmailEmUsoException($"O e-mail '{request.Email}' já está em uso.");
            }
            usuario.Email = request.Email;
        }

        if (request.Nome is not null)
        {
            usuario.Nome = request.Nome;
        }

        if (request.Telefone is not null)
        {
            usuario.Telefone = request.Telefone;
        }

        await _usuarioRepository.AtualizarAsync(usuario, ct);
        return MapearParaDto(usuario);
    }

    private static UsuarioDto MapearParaDto(Usuario usuario) =>
        new(usuario.Id, usuario.Nome, usuario.Email, usuario.Telefone, usuario.Papel.ToString().ToLowerInvariant());
}
