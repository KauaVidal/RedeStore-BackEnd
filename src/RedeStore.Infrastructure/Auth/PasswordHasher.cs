using Microsoft.AspNetCore.Identity;
using RedeStore.Application.Common;

namespace RedeStore.Infrastructure.Auth;

public sealed class PasswordHasher : IPasswordHasher
{
    private static readonly object HasherUser = new();
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<object> _hasher = new();

    public string HashPassword(string password) =>
        _hasher.HashPassword(HasherUser, password);

    public bool VerifyPassword(string hash, string password) =>
        _hasher.VerifyHashedPassword(HasherUser, hash, password)
            != PasswordVerificationResult.Failed;
}
