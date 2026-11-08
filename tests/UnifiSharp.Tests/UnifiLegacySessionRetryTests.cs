using System.Net;
using UnifiSharp.Legacy;

namespace UnifiSharp.Tests;

#pragma warning disable CS0618 // UnifiLegacyClient is intentionally obsolete (ADR-0003)

// Session auth re-logs-in on a 401 and repeats the request. The repeat must carry the same
// body and headers, not throw because the first attempt disposed them.
public class UnifiLegacySessionRetryTests
{
    private static readonly Uri Site = new("https://gw.lan/proxy/network/api/s/default");
    private static readonly Uri V2 = new("https://gw.lan/proxy/network/v2/api/site/default/nat/abc");

    [Theory]
    [InlineData("PUT")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("GET")]
    public async Task SendAsync_repeats_the_request_with_its_body_after_the_relogin(string verb)
    {
        var h = new ExpiringSessionHandler();
        using var session = h.Session();
        var body = verb is "PUT" or "POST" ? UnifiLegacySession.JsonBody("""{"a":1}""") : null;

        using var resp = await session.SendAsync(new HttpMethod(verb), "rest/thing/1", body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        AssertLoginThenRepeat(h, verb, body is null ? null : """{"a":1}""");
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task SendAbsoluteAsync_repeats_the_request_with_its_body_after_the_relogin(string verb)
    {
        var h = new ExpiringSessionHandler();
        using var session = h.Session();
        var body = verb is "PUT" or "POST" ? UnifiLegacySession.JsonBody("""{"b":2}""") : null;

        using var resp = await session.SendAbsoluteAsync(new HttpMethod(verb), V2, body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        AssertLoginThenRepeat(h, verb, body is null ? null : """{"b":2}""");
    }

    [Fact]
    public async Task A_second_401_after_the_relogin_is_returned_not_retried_again()
    {
        var h = new ExpiringSessionHandler(alwaysExpired: true);
        using var session = h.Session();

        using var resp = await session.SendAsync(HttpMethod.Put, "rest/thing/1", UnifiLegacySession.JsonBody("{}"));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(2, h.Requests.Count(r => !IsLogin(r.Uri)));
    }

    private static bool IsLogin(Uri u) => u.AbsolutePath.EndsWith("/api/auth/login");

    private static void AssertLoginThenRepeat(ExpiringSessionHandler h, string verb, string? body)
    {
        // The session logs in before its first request; the 401 then forces a second login.
        Assert.Equal(["login", "op", "login", "op"], h.Requests.Select(r => IsLogin(r.Uri) ? "login" : "op"));
        var attempts = h.Requests.Where(r => !IsLogin(r.Uri)).ToList();
        Assert.All(attempts, a => Assert.Equal(new HttpMethod(verb), a.Method));
        Assert.All(attempts, a => Assert.Equal(body, a.Body));
        if (body is not null) Assert.All(attempts, a => Assert.Equal("application/json", a.ContentType));
    }

    // Answers 401 to the first non-login request (or to all of them), 200 to the login and after.
    private sealed class ExpiringSessionHandler(bool alwaysExpired = false) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body, string? ContentType)> Requests { get; } = [];
        private bool _expiredOnce;

        public UnifiLegacySession Session() => new(
            new UnifiLegacyOptions { BaseUrl = Site, Username = "u", Password = "p" }, this);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!, body, request.Content?.Headers.ContentType?.MediaType));
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/auth/login"))
                return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
            if (alwaysExpired || !_expiredOnce)
            {
                _expiredOnce = true;
                return new(HttpStatusCode.Unauthorized) { Content = new StringContent("") };
            }

            return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }
    }
}
