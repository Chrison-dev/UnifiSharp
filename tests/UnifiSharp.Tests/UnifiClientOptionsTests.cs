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

    [Fact]
    public void ToString_does_not_leak_the_api_key()
    {
        var options = new UnifiClientOptions
        {
            BaseUrl = new Uri("https://192.168.1.1/proxy/network/integration/v1"),
            ApiKey = "super-secret-api-key",
        };

        var text = options.ToString();

        Assert.DoesNotContain("super-secret-api-key", text);
        Assert.Contains("***", text);
    }
}
