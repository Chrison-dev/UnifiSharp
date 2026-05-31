using Microsoft.Kiota.Http.HttpClientLibrary;
using UnifiSharp.Api;

namespace UnifiSharp;

/// <summary>
/// Builds a wired, Kiota-generated <see cref="UnifiApiClient"/> — X-API-KEY auth,
/// the controller base URL, and self-signed-cert handling. Entry point for the
/// generated read surface (sites, networks, WLANs, firewall, devices, clients).
/// </summary>
public static class UnifiApi
{
    public static UnifiApiClient Create(UnifiClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var authProvider = new UnifiApiKeyAuthenticationProvider(options);

        var handler = new HttpClientHandler();
        if (!options.VerifyTls)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: new HttpClient(handler))
        {
            BaseUrl = options.BaseUrl.AbsoluteUri.TrimEnd('/'),
        };

        return new UnifiApiClient(adapter);
    }
}
