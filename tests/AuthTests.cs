using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QueueManagement.Api.Data;
using QueueManagement.Shared;

namespace QueueManagement.Tests;

public sealed class AuthTests : IDisposable
{
    private readonly Dictionary<string, string?> _previousEnvironment = new();
    private readonly TestD1Store _store = new();
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthTests()
    {
        SetEnvironment("Jwt__Key", "TestOnlySigningKeyForAuthTestsAtLeast32Chars!");
        SetEnvironment("Cloudflare__AccountId", "test-account");
        SetEnvironment("Cloudflare__D1DatabaseId", "test-database");
        SetEnvironment("Cloudflare__ApiToken", "test-only-token");
        _factory = new TestApplicationFactory(_store);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task RegisterAndLogin_UsesD1AndIssuesHttpOnlyCookie()
    {
        var email = $"testuser_{Guid.NewGuid():N}@rapidq.local";
        var registration = await _client.PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Email = email,
            Password = "Password123!",
            Role = "Customer"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        var login = await _client.PostAsJsonAsync("/auth/login", new LoginRequest { Email = email, Password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authResponse = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResponse);
        Assert.Equal(email, authResponse.Email);
        Assert.Empty(authResponse.Token);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.Contains("rapidq_auth=") && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        var user = await _client.GetFromJsonAsync<CurrentUserResponse>("/auth/me");
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.Contains("Customer", user.Roles);
    }

    [Fact]
    public async Task Login_InvalidUser_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Email = "missing@rapidq.local",
            Password = "Password123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_CannotSelfAssignAdminRole()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Email = $"admin_{Guid.NewGuid():N}@rapidq.local",
            Password = "Password123!",
            Role = "Admin"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        foreach (var entry in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }
    }

    private void SetEnvironment(string key, string value)
    {
        _previousEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    private sealed class TestApplicationFactory(TestD1Store store) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<CloudflareD1Client>()
                    .ConfigurePrimaryHttpMessageHandler(() => new TestD1Handler(store)));
        }
    }

    private sealed class TestD1Store
    {
        public Dictionary<string, TestUser> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record TestUser(string Id, string Email, string NormalizedEmail, string PasswordHash, string Role);

    private sealed class TestD1Handler(TestD1Store store) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.TryGetProperty("batch", out var batch))
            {
                var firstParams = batch[0].GetProperty("params");
                var user = new TestUser(
                    firstParams[0].GetString()!,
                    firstParams[1].GetString()!,
                    firstParams[2].GetString()!,
                    firstParams[3].GetString()!,
                    batch[1].GetProperty("params")[1].GetString()!);
                store.Users[user.NormalizedEmail] = user;
                return Json("{\"success\":true,\"result\":[{\"success\":true,\"results\":[],\"meta\":{\"changes\":1}},{\"success\":true,\"results\":[],\"meta\":{\"changes\":1}}]}");
            }

            var sql = root.GetProperty("sql").GetString() ?? string.Empty;
            var parameters = root.GetProperty("params");
            if (sql.Contains("FROM users WHERE normalized_email", StringComparison.OrdinalIgnoreCase))
            {
                var key = parameters[0].GetString()!;
                if (store.Users.TryGetValue(key, out var user))
                {
                    return Json($"{{\"success\":true,\"result\":[{{\"success\":true,\"results\":[{{\"Id\":{Quote(user.Id)},\"Email\":{Quote(user.Email)},\"PasswordHash\":{Quote(user.PasswordHash)}}}],\"meta\":{{}}}}]}}");
                }

                return Json("{\"success\":true,\"result\":[{\"success\":true,\"results\":[],\"meta\":{}}]}");
            }

            if (sql.Contains("FROM roles r JOIN user_roles", StringComparison.OrdinalIgnoreCase))
            {
                var userId = parameters[0].GetString()!;
                var user = store.Users.Values.FirstOrDefault(candidate => candidate.Id == userId);
                var rolesJson = user is null ? "[]" : $"[{{\"Name\":{Quote(user.Role)}}}]";
                return Json($"{{\"success\":true,\"result\":[{{\"success\":true,\"results\":{rolesJson},\"meta\":{{}}}}]}}");
            }

            return Json("{\"success\":true,\"result\":[{\"success\":true,\"results\":[],\"meta\":{\"changes\":0}}]}");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        private static string Quote(string value) => JsonSerializer.Serialize(value);
    }
}
