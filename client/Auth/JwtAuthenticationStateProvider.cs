using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace QueueManagement.Client.Auth;

public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;

    public JwtAuthenticationStateProvider(HttpClient httpClient, ILocalStorageService localStorage)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var savedToken = await _localStorage.GetItemAsync<string>("authToken");

            if (string.IsNullOrWhiteSpace(savedToken))
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var claims = ParseClaimsFromJwt(savedToken).ToList();
            if (!claims.Any())
            {
                await _localStorage.RemoveItemAsync("authToken");
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", savedToken);

            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt")));
        }
        catch
        {
            try { await _localStorage.RemoveItemAsync("authToken"); } catch { }
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }

    public void MarkUserAsAuthenticated(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
        var claims = ParseClaimsFromJwt(token).ToList();
        var authenticatedUser = new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
        var authState = Task.FromResult(new AuthenticationState(authenticatedUser));
        NotifyAuthenticationStateChanged(authState);
    }

    public void MarkUserAsLoggedOut()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var authState = Task.FromResult(new AuthenticationState(anonymousUser));
        NotifyAuthenticationStateChanged(authState);
    }

    private IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        try
        {
            var claims = new List<Claim>();
            var parts = jwt.Split('.');
            if (parts.Length < 2) return claims;

            var payload = parts[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            using var doc = JsonDocument.Parse(jsonBytes);
            var root = doc.RootElement;

            foreach (var prop in root.EnumerateObject())
            {
                var key = prop.Name;

                if (key.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(ClaimTypes.Role, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in prop.Value.EnumerateArray())
                        {
                            var r = elem.GetString();
                            if (!string.IsNullOrWhiteSpace(r))
                            {
                                claims.Add(new Claim(ClaimTypes.Role, r));
                            }
                        }
                    }
                    else
                    {
                        var r = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(r))
                        {
                            claims.Add(new Claim(ClaimTypes.Role, r));
                        }
                    }
                    continue;
                }

                if (key.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("unique_name", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(ClaimTypes.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var n = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(n))
                    {
                        claims.Add(new Claim(ClaimTypes.Name, n));
                    }
                    continue;
                }

                if (key.Equals("email", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(ClaimTypes.Email, StringComparison.OrdinalIgnoreCase))
                {
                    var em = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(em))
                    {
                        claims.Add(new Claim(ClaimTypes.Email, em));
                    }
                    continue;
                }

                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    claims.Add(new Claim(key, prop.Value.GetString() ?? string.Empty));
                }
                else
                {
                    claims.Add(new Claim(key, prop.Value.ToString() ?? string.Empty));
                }
            }

            return claims;
        }
        catch
        {
            return Enumerable.Empty<Claim>();
        }
    }

    private byte[] ParseBase64WithoutPadding(string base64)
    {
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }
}
