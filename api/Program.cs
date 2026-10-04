using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using QueueManagement.Api.Data;
using QueueManagement.Api.Hubs;
using QueueManagement.Shared;

const string AuthCookieName = "rapidq_auth";

var builder = WebApplication.CreateBuilder(args);
var portEnv = Environment.GetEnvironmentVariable("PORT");
var listenPort = !string.IsNullOrWhiteSpace(portEnv) ? portEnv : "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException("Set Jwt__Key to a secret of at least 32 characters in the API environment.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "RapidQApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "RapidQClient";

builder.Services.AddHttpClient<CloudflareD1Client>();
builder.Services.AddScoped<RapidQRepository>();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (string.IsNullOrWhiteSpace(context.Request.Headers.Authorization) &&
                context.Request.Cookies.TryGetValue(AuthCookieName, out var token))
            {
                context.Token = token;
            }

            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowClient", policy => policy.SetIsOriginAllowed(_ => true)
        .AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var d1 = scope.ServiceProvider.GetRequiredService<CloudflareD1Client>();
    await d1.QueryAsync<HealthRow>("SELECT 1 AS Value");
    var adminEmail = app.Configuration["BootstrapAdmin:Email"];
    var adminPassword = app.Configuration["BootstrapAdmin:Password"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        await scope.ServiceProvider.GetRequiredService<RapidQRepository>()
            .EnsureBootstrapAdminAsync(adminEmail, adminPassword);
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
app.UseCors("AllowClient");
app.UseAuthentication();
app.UseAuthorization();
app.UseBlazorFrameworkFiles();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html") || ctx.File.Name.EndsWith(".json"))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
        }
    }
});

app.MapGet("/api/health", async (CloudflareD1Client d1, CancellationToken ct) =>
{
    await d1.QueryAsync<HealthRow>("SELECT 1 AS Value", ct);
    return Results.Ok(new { Status = "ok", Timestamp = DateTime.UtcNow });
});

var clientApi = app.MapGroup("/client");
var staffApi = app.MapGroup("/staff").RequireAuthorization(policy => policy.RequireRole("Staff", "Admin"));
var adminApi = app.MapGroup("/admin").RequireAuthorization(policy => policy.RequireRole("Admin"));
var authApi = app.MapGroup("/auth");

authApi.MapPost("/register", async (RegisterRequest request, RapidQRepository repository, CancellationToken ct) =>
{
    var result = await repository.RegisterAsync(request, ct);
    return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Error);
});

authApi.MapPost("/login", async (LoginRequest request, RapidQRepository repository, IConfiguration config, HttpContext context, CancellationToken ct) =>
{
    var account = await repository.AuthenticateAsync(request.Email, request.Password, ct);
    if (account is null) return Results.Unauthorized();

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, account.Email),
        new(ClaimTypes.Email, account.Email)
    };
    claims.AddRange(account.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
    var token = new JwtSecurityToken(
        issuer: config["Jwt:Issuer"] ?? "RapidQApi",
        audience: config["Jwt:Audience"] ?? "RapidQClient",
        claims: claims,
        expires: DateTime.UtcNow.AddDays(1),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);

    context.Response.Cookies.Append(AuthCookieName, tokenValue, new CookieOptions
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddDays(1)
    });

    return Results.Ok(new AuthResponse { Token = string.Empty, Email = account.Email, Roles = account.Roles.ToList() });
});

authApi.MapGet("/me", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    return Results.Ok(new CurrentUserResponse
    {
        Email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity.Name ?? string.Empty,
        Roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList()
    });
});

authApi.MapPost("/logout", (HttpContext context) =>
{
    context.Response.Cookies.Delete(AuthCookieName, new CookieOptions
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/"
    });
    return Results.NoContent();
});

clientApi.MapGet("/branches", (RapidQRepository repository, CancellationToken ct) => repository.GetBranchesAsync(ct));
clientApi.MapGet("/services", (RapidQRepository repository, CancellationToken ct) => repository.GetServicesAsync(ct));

clientApi.MapPost("/tickets", async (CreateAppointmentRequest request, RapidQRepository repository, IHubContext<QueueHub> hub, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerName)) return Results.BadRequest("Customer name is required.");
    if (!PhoneNumberValidation.IsValid(request.CustomerPhone)) return Results.BadRequest("Customer phone number must contain exactly 10 digits.");
    if (request.ServiceId <= 0) return Results.BadRequest("A valid service is required.");
    if (!AppointmentScheduling.IsBusinessDay(request.AppointmentDate)) return Results.BadRequest("Appointments are available Monday to Friday only.");
    if (string.IsNullOrWhiteSpace(request.TimeSlot) || !AppointmentScheduling.IsWithinBusinessHours(request.TimeSlot))
        return Results.BadRequest("Please select a preferred time between 09:00 and 17:00 during office hours.");

    var (ticket, service, branch) = await repository.CreateTicketAsync(request, ct);
    if (service is null) return Results.BadRequest("Selected service does not exist.");
    if (branch is null) return Results.BadRequest("Branch configuration is missing.");
    if (ticket is null) return Results.Conflict("A ticket could not be allocated. Please try again.");

    await hub.Clients.All.SendAsync("QueueUpdated", cancellationToken: ct);
    var expectedTime = AppointmentScheduling.GetExpectedAppointmentTime(request.AppointmentDate, request.TimeSlot)
        ?? request.AppointmentDate.Date.AddHours(9);
    return Results.Ok(new
    {
        ticket.Id,
        ticket.QueueNumber,
        ticket.QueueCode,
        Status = (AppointmentStatus)ticket.Status,
        BranchName = branch.Name,
        ServiceName = service.Name,
        ServiceCode = service.ServiceCode,
        TimeSlot = request.TimeSlot,
        ExpectedTime = expectedTime,
        IssuedAt = ticket.CreatedAt
    });
});

clientApi.MapGet("/track/{queueCode}", async (string queueCode, RapidQRepository repository, CancellationToken ct) =>
{
    var ticket = await repository.TrackAsync(queueCode, ct);
    return ticket is null ? Results.NotFound() : Results.Ok(ticket);
});

staffApi.MapGet("/queue", (RapidQRepository repository, CancellationToken ct) => repository.GetQueueAsync(ct));
staffApi.MapGet("/dashboard", (RapidQRepository repository, CancellationToken ct) => repository.GetDashboardAsync(ct));
staffApi.MapGet("/analytics", (RapidQRepository repository, CancellationToken ct) => repository.GetStaffAnalyticsAsync(ct));
staffApi.MapGet("/dashboard-view", (RapidQRepository repository, CancellationToken ct) => repository.GetStaffDashboardAsync(ct));
staffApi.MapGet("/history", (RapidQRepository repository, CancellationToken ct) => repository.GetHistoryAsync(ct));

MapTicketAction("call");
MapTicketAction("serve");
MapTicketAction("skip");
MapTicketAction("recall");

void MapTicketAction(string action)
{
    staffApi.MapPost($"/queue/{{appointmentId:int}}/{action}", async (int appointmentId, RapidQRepository repository, IHubContext<QueueHub> hub, CancellationToken ct) =>
    {
        var ticket = await repository.AdvanceTicketAsync(appointmentId, action, ct);
        if (ticket is null)
        {
            return await repository.TicketExistsAsync(appointmentId, ct)
                ? Results.Conflict("The ticket status changed or this action is not valid for its current status.")
                : Results.NotFound();
        }

        await hub.Clients.All.SendAsync("QueueUpdated", cancellationToken: ct);
        return Results.Ok(ticket);
    });
}

app.MapHub<QueueHub>("/queueHub");

adminApi.MapGet("/analytics", (RapidQRepository repository, CancellationToken ct) => repository.GetAdminAnalyticsAsync(ct));
adminApi.MapGet("/services", (RapidQRepository repository, CancellationToken ct) => repository.AdminServicesAsync(ct));
adminApi.MapPost("/services", async (ServiceItem service, RapidQRepository repository, CancellationToken ct) =>
{
    var id = await repository.AddServiceAsync(service, ct);
    if (id is null) return Results.BadRequest();
    service.Id = id.Value;
    return Results.Created($"/admin/services/{id}", service);
});
adminApi.MapPut("/services/{id:int}", async (int id, ServiceItem service, RapidQRepository repository, CancellationToken ct) =>
{
    var result = await repository.UpdateServiceAsync(id, service, ct);
    return result.Changes == 0 ? Results.NotFound() : Results.NoContent();
});
adminApi.MapDelete("/services/{id:int}", async (int id, RapidQRepository repository, CancellationToken ct) =>
{
    var result = await repository.DeleteServiceAsync(id, ct);
    return result.Changes == 0 ? Results.NotFound() : Results.NoContent();
});
adminApi.MapGet("/branches", (RapidQRepository repository, CancellationToken ct) => repository.GetAllBranchesAsync(ct));
adminApi.MapPost("/branches", async (Branch branch, RapidQRepository repository, CancellationToken ct) =>
{
    var id = await repository.AddBranchAsync(branch, ct);
    if (id is null) return Results.BadRequest();
    branch.Id = id.Value;
    return Results.Created($"/admin/branches/{id}", branch);
});
adminApi.MapPut("/branches/{id:int}", async (int id, Branch branch, RapidQRepository repository, CancellationToken ct) =>
{
    var result = await repository.UpdateBranchAsync(id, branch, ct);
    return result.Changes == 0 ? Results.NotFound() : Results.NoContent();
});
adminApi.MapDelete("/branches/{id:int}", async (int id, RapidQRepository repository, CancellationToken ct) =>
{
    var result = await repository.DeleteBranchAsync(id, ct);
    return result.Changes == 0 ? Results.NotFound() : Results.NoContent();
});

app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        ctx.Context.Response.Headers.Pragma = "no-cache";
        ctx.Context.Response.Headers.Expires = "0";
    }
});

app.Run();

public sealed class HealthRow { public int Value { get; set; } }
public partial class Program { }
