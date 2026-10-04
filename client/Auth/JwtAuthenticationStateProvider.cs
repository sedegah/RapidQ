using System.Security.Claims;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using QueueManagement.Shared;

namespace QueueManagement.Client.Auth;

public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _httpClient;
    private AuthenticationState _currentState = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public JwtAuthenticationStateProvider(HttpClient httpClient) => _httpClient = httpClient;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync("auth/me");
            if (!response.IsSuccessStatusCode)
            {
                _currentState = AnonymousState();
                return _currentState;
            }

            var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
            if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.Email))
            {
                _currentState = AnonymousState();
                return _currentState;
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, currentUser.Email),
                new(ClaimTypes.Email, currentUser.Email)
            };
            claims.AddRange(currentUser.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            _currentState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "cookie")));
        }
        catch
        {
            _currentState = AnonymousState();
        }

        return _currentState;
    }

    public async Task RefreshAuthenticationStateAsync()
    {
        _currentState = await GetAuthenticationStateAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(_currentState));
    }

    public void MarkUserAsLoggedOut()
    {
        _currentState = AnonymousState();
        NotifyAuthenticationStateChanged(Task.FromResult(_currentState));
    }

    private static AuthenticationState AnonymousState() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));
}
