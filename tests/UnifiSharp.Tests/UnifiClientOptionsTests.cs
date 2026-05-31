using UnifiSharp;

namespace UnifiSharp.Tests;

public class UnifiClientOptionsTests
{
    [Fact]
    public void VerifyTls_defaults_to_true()
    {
        var options = new UnifiClientOptions
        {
            BaseUrl = new Uri("https://192.168.1.1/proxy/network/integration/v1"),
            ApiKey = "x",
        };

        Assert.True(options.VerifyTls);
    }

    [Fact]
    public void Create_builds_a_client()
    {
        var options = new UnifiClientOptions
        {
            BaseUrl = new Uri("https://192.168.1.1/proxy/network/integration/v1"),
            ApiKey = "x",
            VerifyTls = false,
        };

        var client = UnifiApi.Create(options);

        Assert.NotNull(client);
    }
}
