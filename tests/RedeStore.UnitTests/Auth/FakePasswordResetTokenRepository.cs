using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Auth;

public sealed class FakePasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly Dictionary<string, PasswordResetToken> _tokensPorHash = new();

    public Task AdicionarAsync(PasswordResetToken token, CancellationToken ct)
    {
        _tokensPorHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<PasswordResetToken?> BuscarPorTokenHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(_tokensPorHash.GetValueOrDefault(tokenHash));

    public Task AtualizarAsync(PasswordResetToken token, CancellationToken ct)
    {
        _tokensPorHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }
}
