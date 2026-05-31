using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace UnifiSharp;

/// <summary>
/// Kiota authentication provider that adds UniFi's <c>X-API-KEY</c> header to
/// every request.
/// </summary>
public sealed class UnifiApiKeyAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _apiKey;

    public UnifiApiKeyAuthenticationProvider(UnifiClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _apiKey = options.ApiKey;
    }

    public Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAdd("X-API-KEY", _apiKey);
        return Task.CompletedTask;
    }
}
