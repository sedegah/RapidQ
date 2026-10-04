using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using QueueManagement.Api.Data;

namespace QueueManagement.Tests;

public class CloudflareD1ClientTests
{
    [Fact]
    public async Task QueryAsync_UsesServerCredentialAndReturnsD1Rows()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return JsonResponse("{\"success\":true,\"result\":[{\"success\":true,\"results\":[{\"Value\":1}],\"meta\":{\"changes\":0}}]}");
        });
        var client = CreateClient(handler);
        var d1 = new CloudflareD1Client(client, Configuration());

        var rows = await d1.QueryAsync<HealthRow>("SELECT ? AS Value", default, 1);

        Assert.Single(rows);
        Assert.Equal(1, rows[0].Value);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Contains("/accounts/test-account/d1/database/test-database/query", captured.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(await captured.Content!.ReadAsStringAsync());
        Assert.Equal("SELECT ? AS Value", body.RootElement.GetProperty("sql").GetString());
        Assert.Equal("1", body.RootElement.GetProperty("params")[0].GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsChangeCount()
    {
        var handler = new StubHandler(_ => JsonResponse("{\"success\":true,\"result\":[{\"success\":true,\"results\":[],\"meta\":{\"changes\":1,\"last_row_id\":9}}]}"));
        var d1 = new CloudflareD1Client(CreateClient(handler), Configuration());

        var result = await d1.ExecuteAsync("DELETE FROM branches WHERE id = ?", default, 9);

        Assert.Equal(1, result.Changes);
        Assert.Equal(9, result.LastRowId);
    }

    [Fact]
    public async Task QueryAsync_ThrowsForCloudflareErrors()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"success\":false,\"errors\":[{\"message\":\"permission denied\"}]}", Encoding.UTF8, "application/json")
        });
        var d1 = new CloudflareD1Client(CreateClient(handler), Configuration());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => d1.QueryAsync<HealthRow>("SELECT 1 AS Value"));

        Assert.Contains("permission denied", error.Message);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler);

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cloudflare:AccountId"] = "test-account",
            ["Cloudflare:D1DatabaseId"] = "test-database",
            ["Cloudflare:ApiToken"] = "test-only-token"
        })
        .Build();

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class HealthRow
    {
        public int Value { get; set; }
    }
}
