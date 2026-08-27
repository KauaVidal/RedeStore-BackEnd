using RedeStore.Infrastructure.Auth;
using Xunit;

namespace RedeStore.UnitTests.Auth;

public class PasswordHasherTests
{
    private readonly PasswordHasher _sut = new();

    [Fact]
    public void HashPassword_ThenVerifyPassword_WithCorrectPassword_ReturnsTrue()
    {
        var hash = _sut.HashPassword("senha12345");

        var result = _sut.VerifyPassword(hash, "senha12345");

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_ReturnsFalse()
    {
        var hash = _sut.HashPassword("senha12345");

        var result = _sut.VerifyPassword(hash, "senhaErrada");

        Assert.False(result);
    }

    [Fact]
    public void HashPassword_CalledTwiceWithSamePassword_ProducesDifferentHashes()
    {
        var hash1 = _sut.HashPassword("senha12345");
        var hash2 = _sut.HashPassword("senha12345");

        Assert.NotEqual(hash1, hash2);
    }
}
