using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using QueueManagement.Api;
using QueueManagement.Api.Data;
using QueueManagement.Api.Hubs;
using QueueManagement.Shared;

static string GenerateQueueCode(int queueNumber, string serviceCode)
{
    var prefix = string.IsNullOrWhiteSpace(serviceCode)
        ? "GE"
        : serviceCode.Trim().ToUpperInvariant().Replace("-", string.Empty);

    if (prefix.Length < 2)
    {
        prefix = prefix.PadRight(2, 'X');
    }

    return $"{prefix.Substring(0, 2)}-{queueNumber:D4}";
}

var builder = WebApplication.CreateBuilder(args);

var dbPath = DatabasePathProvider.ResolveDatabasePath(builder.Environment.ContentRootPath);

builder.Services.AddDbContext<QueueDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<QueueDbContext>()
    .AddDefaultTokenProviders();

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
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});
builder.Services.AddAuthorization();

builder.Services.AddSignalR();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowClient", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5267",
                "https://localhost:7042",
                "https://localhost:7041",
                "http://localhost:5187")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QueueDbContext>();
    db.Database.EnsureCreated();
    SeedData.Initialize(db);

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    var roles = new[] { "Admin", "Staff", "Customer" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    var adminEmail = "admin@rapidq.local";
    if (await userManager.FindByEmailAsync(adminEmail) == null)
    {
        var adminUser = new IdentityUser { UserName = adminEmail, Email = adminEmail };
        var result = await userManager.CreateAsync(adminUser, "Admin123!");
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowClient");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => new { Status = "ok", Timestamp = DateTime.UtcNow });

var clientApi = app.MapGroup("/client");
var staffApi = app.MapGroup("/staff").RequireAuthorization(policy => policy.RequireRole("Staff", "Admin"));
var adminApi = app.MapGroup("/admin").RequireAuthorization(policy => policy.RequireRole("Admin"));
var authApi = app.MapGroup("/auth");

authApi.MapPost("/register", async (RegisterRequest req, UserManager<IdentityUser> userManager) =>
{
    var user = new IdentityUser { UserName = req.Email, Email = req.Email };
    var result = await userManager.CreateAsync(user, req.Password);
    
    if (result.Succeeded)
    {
        await userManager.AddToRoleAsync(user, req.Role);
        return Results.Ok();
    }
    
    return Results.BadRequest(result.Errors);
});

authApi.MapPost("/login", async (LoginRequest req, UserManager<IdentityUser> userManager, IConfiguration config) =>
{
    var user = await userManager.FindByEmailAsync(req.Email);
    if (user != null && await userManager.CheckPasswordAsync(user, req.Password))
    {
        var roles = await userManager.GetRolesAsync(user);
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Email!),
            new Claim(ClaimTypes.Email, user.Email!)
        };
        
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddDays(1),
            signingCredentials: creds
        );
        
        return Results.Ok(new AuthResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            Email = user.Email!,
            Roles = roles
        });
    }
    
    return Results.Unauthorized();
});

clientApi.MapGet("/branches", async (QueueDbContext db) =>
    await db.Branches.OrderBy(b => b.Name).Take(1).ToListAsync());

clientApi.MapGet("/services", async (QueueDbContext db) =>
    await db.Services.OrderBy(s => s.Name).ToListAsync());

clientApi.MapPost("/tickets", async (CreateAppointmentRequest request, QueueDbContext db, IHubContext<QueueHub> hub) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerName))
    {
        return Results.BadRequest("Customer name is required.");
    }

    if (string.IsNullOrWhiteSpace(request.CustomerPhone))
    {
        return Results.BadRequest("Customer phone number is required.");
    }

    if (request.ServiceId <= 0)
    {
        return Results.BadRequest("A valid service is required.");
    }

    if (!AppointmentScheduling.IsBusinessDay(request.AppointmentDate))
    {
        return Results.BadRequest("Appointments are available Monday to Friday only.");
    }

    if (string.IsNullOrWhiteSpace(request.TimeSlot) || !AppointmentScheduling.IsWithinBusinessHours(request.TimeSlot))
    {
        return Results.BadRequest("Please select a preferred time between 09:00 and 17:00 during office hours.");
    }

    var service = await db.Services.FirstOrDefaultAsync(s => s.Id == request.ServiceId);
    if (service is null)
    {
        return Results.BadRequest("Selected service does not exist.");
    }

    var branch = await db.Branches.OrderBy(b => b.Id).FirstOrDefaultAsync();
    if (branch is null)
    {
        return Results.BadRequest("Branch configuration is missing.");
    }

    var lastQueue = await db.Appointments.MaxAsync(a => (int?)a.QueueNumber) ?? 0;
    var queueNumber = lastQueue + 1;
    var queueCode = GenerateQueueCode(queueNumber, service.ServiceCode);
    var issuedAt = DateTime.UtcNow;
    var expectedTime = AppointmentScheduling.GetExpectedAppointmentTime(request.AppointmentDate, request.TimeSlot) ?? request.AppointmentDate.Date.AddHours(9);

    var appointment = new Appointment
    {
        CustomerName = request.CustomerName,
        CustomerEmail = request.CustomerEmail,
        CustomerPhone = request.CustomerPhone,
        ServiceId = request.ServiceId,
        BranchId = branch.Id,
        AppointmentDate = request.AppointmentDate,
        TimeSlot = string.IsNullOrWhiteSpace(request.TimeSlot) ? "Walk-in" : request.TimeSlot,
        QueueNumber = queueNumber,
        QueueCode = queueCode,
        Status = AppointmentStatus.Waiting,
        CreatedAt = issuedAt
    };

    db.Appointments.Add(appointment);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("QueueUpdated");

    return Results.Ok(new
    {
        appointment.Id,
        appointment.QueueNumber,
        appointment.QueueCode,
        appointment.Status,
        BranchName = branch.Name,
        ServiceName = service.Name,
        ServiceCode = service.ServiceCode,
        TimeSlot = appointment.TimeSlot,
        ExpectedTime = expectedTime,
        IssuedAt = issuedAt
    });
});

clientApi.MapGet("/track/{queueCode}", async (string queueCode, QueueDbContext db) =>
{
    var appointment = await db.Appointments
        .Include(a => a.Service)
        .FirstOrDefaultAsync(a => a.QueueCode == queueCode);

    if (appointment is null) return Results.NotFound();

    var peopleAhead = await db.Appointments.CountAsync(a => 
        a.Status == AppointmentStatus.Waiting && 
        a.ServiceId == appointment.ServiceId &&
        a.QueueNumber < appointment.QueueNumber);

    return Results.Ok(new TrackResponse
    {
        QueueCode = appointment.QueueCode,
        Status = appointment.Status,
        ServiceName = appointment.Service?.Name ?? string.Empty,
        ExpectedTime = appointment.AppointmentDate,
        PeopleAhead = peopleAhead
    });
});

staffApi.MapGet("/queue", async (QueueDbContext db) =>
    await db.Appointments
        .Include(a => a.Service)
        .Include(a => a.Branch)
        .Where(a => a.Status != AppointmentStatus.Served && a.Status != AppointmentStatus.Missed)
        .OrderBy(a => a.QueueNumber)
        .Select(a => new QueueViewItem
        {
            Id = a.Id,
            QueueNumber = a.QueueNumber,
            QueueCode = a.QueueCode,
            CustomerName = a.CustomerName,
            CustomerEmail = a.CustomerEmail,
            CustomerPhone = a.CustomerPhone,
            ServiceName = a.Service!.Name,
            BranchName = a.Branch!.Name,
            TimeSlot = a.TimeSlot,
            AppointmentDate = a.AppointmentDate,
            Status = a.Status,
            CreatedAt = a.CreatedAt,
            CalledAt = a.CalledAt
        })
        .ToListAsync());

staffApi.MapGet("/dashboard", async (QueueDbContext db) =>
{
    var appointments = await db.Appointments.ToListAsync();
    var activeQueue = appointments.Count(a => a.Status != AppointmentStatus.Served && a.Status != AppointmentStatus.Missed);

    return new DashboardSummary
    {
        TotalAppointments = appointments.Count,
        ActiveQueue = activeQueue,
        StaffCallsToday = appointments.Count(a => a.Status == AppointmentStatus.Called || a.Status == AppointmentStatus.Serving),
        ServicesAvailable = await db.Services.CountAsync(),
        AverageWaitMinutes = activeQueue == 0 ? 0 : Math.Max(5, activeQueue * 7)
    };
});

staffApi.MapGet("/history", async (QueueDbContext db) =>
{
    var history = await db.Appointments
        .Include(a => a.Service)
        .Include(a => a.Branch)
        .Where(a => a.Status == AppointmentStatus.Served || a.Status == AppointmentStatus.Missed)
        .OrderByDescending(a => a.ServedAt ?? a.CreatedAt)
        .ToListAsync();

    return history.Select(a => new AppointmentHistoryItem
    {
        Id = a.Id,
        CustomerName = a.CustomerName,
        CustomerEmail = a.CustomerEmail,
        CustomerPhone = a.CustomerPhone,
        ServiceName = a.Service?.Name ?? "Unknown service",
        ServiceCode = a.Service?.ServiceCode ?? "—",
        BranchName = a.Branch?.Name ?? "Main Branch",
        QueueCode = a.QueueCode,
        AppointmentDate = a.AppointmentDate,
        TimeSlot = a.TimeSlot,
        CreatedAt = a.CreatedAt,
        ServedAt = a.ServedAt,
        ServiceDurationMinutes = a.CalledAt.HasValue && a.ServedAt.HasValue
            ? (int)Math.Max(0, (a.ServedAt.Value - a.CalledAt.Value).TotalMinutes)
            : null,
        Status = a.Status
    }).ToList();
});

staffApi.MapPost("/queue/{appointmentId:int}/call", async (int appointmentId, QueueDbContext db, IHubContext<QueueHub> hub) =>
{
    var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
    if (appointment is null)
    {
        return Results.NotFound();
    }

    appointment.Status = AppointmentStatus.Called;
    appointment.CalledAt ??= DateTime.UtcNow;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("QueueUpdated");
    return Results.Ok(appointment);
});

staffApi.MapPost("/queue/{appointmentId:int}/serve", async (int appointmentId, QueueDbContext db, IHubContext<QueueHub> hub) =>
{
    var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
    if (appointment is null)
    {
        return Results.NotFound();
    }

    appointment.Status = AppointmentStatus.Served;
    appointment.ServedAt ??= DateTime.UtcNow;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("QueueUpdated");
    return Results.Ok(appointment);
});

staffApi.MapPost("/queue/{appointmentId:int}/skip", async (int appointmentId, QueueDbContext db, IHubContext<QueueHub> hub) =>
{
    var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
    if (appointment is null)
    {
        return Results.NotFound();
    }

    appointment.Status = AppointmentStatus.Missed;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("QueueUpdated");
    return Results.Ok(appointment);
});

staffApi.MapPost("/queue/{appointmentId:int}/recall", async (int appointmentId, QueueDbContext db, IHubContext<QueueHub> hub) =>
{
    var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
    if (appointment is null)
    {
        return Results.NotFound();
    }

    appointment.Status = AppointmentStatus.Waiting;
    appointment.CalledAt = null;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("QueueUpdated");
    return Results.Ok(appointment);
});

app.MapHub<QueueHub>("/queueHub");

adminApi.MapGet("/analytics", async (QueueDbContext db) =>
{
    var appointments = await db.Appointments.Include(a => a.Service).ToListAsync();
    var activeQueue = appointments.Count(a => a.Status != AppointmentStatus.Served && a.Status != AppointmentStatus.Missed);

    var distribution = appointments
        .Where(a => a.Service != null)
        .GroupBy(a => a.Service!.Name)
        .Select(g => new ServiceDistributionItem { ServiceName = g.Key, Count = g.Count() })
        .ToList();

    return new AdminAnalyticsResponse
    {
        Summary = new DashboardSummary
        {
            TotalAppointments = appointments.Count,
            ActiveQueue = activeQueue,
            StaffCallsToday = appointments.Count(a => a.Status == AppointmentStatus.Called || a.Status == AppointmentStatus.Serving),
            ServicesAvailable = await db.Services.CountAsync(),
            AverageWaitMinutes = activeQueue == 0 ? 0 : Math.Max(5, activeQueue * 7)
        },
        ServiceDistribution = distribution
    };
});

adminApi.MapGet("/services", async (QueueDbContext db) => await db.Services.ToListAsync());
adminApi.MapPost("/services", async (ServiceItem service, QueueDbContext db) =>
{
    db.Services.Add(service);
    await db.SaveChangesAsync();
    return Results.Created($"/admin/services/{service.Id}", service);
});
adminApi.MapPut("/services/{id:int}", async (int id, ServiceItem service, QueueDbContext db) =>
{
    var existing = await db.Services.FindAsync(id);
    if (existing is null) return Results.NotFound();
    existing.Name = service.Name;
    existing.ServiceCode = service.ServiceCode;
    existing.Description = service.Description;
    existing.BranchId = service.BranchId;
    await db.SaveChangesAsync();
    return Results.NoContent();
});
adminApi.MapDelete("/services/{id:int}", async (int id, QueueDbContext db) =>
{
    var existing = await db.Services.FindAsync(id);
    if (existing is null) return Results.NotFound();
    db.Services.Remove(existing);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

adminApi.MapGet("/branches", async (QueueDbContext db) => await db.Branches.ToListAsync());
adminApi.MapPost("/branches", async (Branch branch, QueueDbContext db) =>
{
    db.Branches.Add(branch);
    await db.SaveChangesAsync();
    return Results.Created($"/admin/branches/{branch.Id}", branch);
});
adminApi.MapPut("/branches/{id:int}", async (int id, Branch branch, QueueDbContext db) =>
{
    var existing = await db.Branches.FindAsync(id);
    if (existing is null) return Results.NotFound();
    existing.Name = branch.Name;
    existing.Location = branch.Location;
    await db.SaveChangesAsync();
    return Results.NoContent();
});
adminApi.MapDelete("/branches/{id:int}", async (int id, QueueDbContext db) =>
{
    var existing = await db.Branches.FindAsync(id);
    if (existing is null) return Results.NotFound();
    db.Branches.Remove(existing);
    await db.SaveChangesAsync();
    return Results.NoContent();
});


app.Run();

public partial class Program { }
