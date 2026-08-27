using Xunit;

namespace RedeStore.IntegrationTests;

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Integration";
}
