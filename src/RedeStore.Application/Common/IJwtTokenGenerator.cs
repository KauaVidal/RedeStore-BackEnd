namespace RedeStore.Application.Common;

public interface IJwtTokenGenerator
{
    string GenerateToken(Guid usuarioId, string email, string papel);
}
