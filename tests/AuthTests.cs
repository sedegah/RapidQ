using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using QueueManagement.Shared;

namespace QueueManagement.Tests;

public class AuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Register_ValidUser_ReturnsOk()
    {
        // Arrange
        var uniqueEmail = $"testuser_{Guid.NewGuid():N}@rapidq.local";
        var request = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = "Password123!",
            Role = "Customer"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidUser_ReturnsToken()
    {
        // Arrange
        var uniqueEmail = $"loginuser_{Guid.NewGuid():N}@rapidq.local";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = "Password123!",
            Role = "Customer"
        };
        await _client.PostAsJsonAsync("/auth/register", registerRequest);

        var loginRequest = new LoginRequest
        {
            Email = uniqueEmail,
            Password = "Password123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/auth/login", loginRequest);
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(authResponse);
        Assert.NotEmpty(authResponse.Token);
        Assert.Equal(uniqueEmail, authResponse.Email);
        Assert.Contains("Customer", authResponse.Roles);
    }

    [Fact]
    public async Task Login_InvalidUser_ReturnsUnauthorized()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Email = "nonexistent@rapidq.local",
            Password = "Password123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
