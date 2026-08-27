namespace RedeStore.Infrastructure.Email;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    public required string ApiKey { get; init; }
    public required string FromEmail { get; init; }
}
