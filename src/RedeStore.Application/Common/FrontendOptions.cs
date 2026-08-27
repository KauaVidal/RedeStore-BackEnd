namespace RedeStore.Application.Common;

public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    public required string ResetPasswordUrl { get; init; }
}
