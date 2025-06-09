using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using VCDevTool.API.Data;
using VCDevTool.API.Hubs;
using VCDevTool.API.Middleware;
using VCDevTool.API.Services;
using VCDevTool.API.Authorization;
using VCDevTool.Shared;
using FluentValidation;
using VCDevTool.API.Validators;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for structured logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "VCDevTool.API")
    .WriteTo.Console()
    .WriteTo.File("logs/vcdevtool-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Configure Kestrel server
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    // Configure HTTPS endpoints for production
    if (builder.Environment.IsProduction())
    {
        serverOptions.ListenAnyIP(5289, listenOptions =>
        {
            listenOptions.UseHttps(); // Use HTTPS in production
        });
    }
    else
    {
        serverOptions.ListenAnyIP(5289); // HTTP for development
    }
});

// Add services to the container.
builder.Services.AddControllers();

// Configure Entity Framework
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    
    // Skip database configuration for testing environment as it will be overridden
    if (builder.Environment.EnvironmentName == "Testing")
    {
        return; // Tests will configure their own database provider
    }
    
    // In production, connection string should come from secure configuration
    if (builder.Environment.IsProduction() && string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("Database connection string must be configured for production");
    }
    
    // Use optimized database configuration
    DbOptimizations.ConfigurePerformanceOptions(options, connectionString!, builder.Environment.IsProduction());
});

// Configure Active Directory options
builder.Services.Configure<ActiveDirectoryOptions>(
    builder.Configuration.GetSection(ActiveDirectoryOptions.Section));

// Configure Windows Authentication options
builder.Services.Configure<WindowsAuthenticationOptions>(
    builder.Configuration.GetSection(WindowsAuthenticationOptions.Section));

// Get Windows Authentication settings
var windowsAuthConfig = builder.Configuration.GetSection("WindowsAuthentication");
var isWindowsAuthEnabled = windowsAuthConfig.GetValue<bool>("Enabled");

// Configure JWT Authentication settings
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["SecretKey"];

if (string.IsNullOrEmpty(secretKey) && builder.Environment.IsProduction())
{
    throw new InvalidOperationException("JWT SecretKey must be configured for production");
}

// Configure Authentication
if (isWindowsAuthEnabled)
{
    // Configure Windows Authentication with JWT fallback
    builder.Services.AddAuthentication(options =>
    {
        // Use JWT as the default scheme since Windows auth is handled via middleware
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // JWT configuration for API clients
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                secretKey ?? "VCDevTool-Default-Secret-Key-This-Should-Be-Changed-In-Production-123456789")),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"] ?? "VCDevTool",
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"] ?? "VCDevTool",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // Configure JWT for SignalR
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                
                if (!string.IsNullOrEmpty(accessToken) && 
                    (path.StartsWithSegments("/debugHub") || path.StartsWithSegments("/taskHub")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
}
else
{
    // Standard JWT Authentication only
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                secretKey ?? "VCDevTool-Default-Secret-Key-This-Should-Be-Changed-In-Production-123456789")),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"] ?? "VCDevTool",
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"] ?? "VCDevTool",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // Configure JWT for SignalR
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                
                if (!string.IsNullOrEmpty(accessToken) && 
                    (path.StartsWithSegments("/debugHub") || path.StartsWithSegments("/taskHub")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
}

// Configure Authorization with AD group support
builder.Services.AddAuthorization(options =>
{
    // Enhanced role-based policies that work with AD role mapping
    options.AddPolicy("NodePolicy", policy => 
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            // Allow users with "Node" role (for JWT authentication)
            var hasNodeRole = context.User.IsInRole("Node");
            
            // Allow users in Node groups (for Windows authentication)
            var isInNodeGroup = context.User.FindAll("Groups")
                .Any(claim => new[] { "VCDevTool_ComputerNodes", "VCDevTool_ProcessingNodes" }
                    .Contains(claim.Value, StringComparer.OrdinalIgnoreCase));
            
            // Allow Admin role as well (admins can do everything)
            var hasAdminRole = context.User.IsInRole("Admin");
            
            return hasNodeRole || isInNodeGroup || hasAdminRole;
        });
    });
    
    options.AddPolicy("AdminPolicy", policy => 
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            // Allow users with "Admin" role (for JWT authentication)
            var hasAdminRole = context.User.IsInRole("Admin");
            
            // Allow users in Admin groups (for Windows authentication)
            var isInAdminGroup = context.User.FindAll("Groups")
                .Any(claim => new[] { "VCDevTool_Administrators", "Domain Admins", "IT_Administrators" }
                    .Contains(claim.Value, StringComparer.OrdinalIgnoreCase));
            
            // For development/testing: allow Node role to create tasks if no Admin roles are configured
            // This can be removed in production if stricter control is needed
            var hasNodeRole = context.User.IsInRole("Node");
            
            return hasAdminRole || isInAdminGroup || hasNodeRole;
        });
    });
    
    options.AddPolicy("UserPolicy", policy => 
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            // Allow users with "User" or "Admin" role or in User/Admin groups
            var hasUserRole = context.User.IsInRole("User") || context.User.IsInRole("Admin");
            var isInUserGroup = context.User.FindAll("Groups")
                .Any(claim => new[] { "VCDevTool_Users", "VCDevTool_Administrators", "Domain Users", "Domain Admins" }
                    .Contains(claim.Value, StringComparer.OrdinalIgnoreCase));
            
            return hasUserRole || isInUserGroup;
        });
    });

    // Active Directory group-based policies (for more specific authorization)
    options.AddPolicy("ADAdminPolicy", policy =>
        policy.Requirements.Add(new ADGroupRequirement(new[] { "VCDevTool_Administrators", "Domain Admins", "IT_Administrators" })));
    
    options.AddPolicy("ADUserPolicy", policy =>
        policy.Requirements.Add(new ADGroupRequirement(new[] { "VCDevTool_Users", "VCDevTool_Administrators", "Domain Users" })));
    
    options.AddPolicy("ADNodePolicy", policy =>
        policy.Requirements.Add(new ADGroupRequirement(new[] { "VCDevTool_ComputerNodes", "VCDevTool_ProcessingNodes" })));

    // Computer account policies
    options.AddPolicy("ComputerAccountPolicy", policy =>
        policy.Requirements.Add(new ComputerAccountRequirement(new[] { "VCDevTool_ComputerNodes", "VCDevTool_ProcessingNodes" })));

    // Role-based policies with AD mapping
    options.AddPolicy("ADRoleAdminPolicy", policy =>
        policy.Requirements.Add(new ADRoleRequirement("Admin")));
    
    options.AddPolicy("ADRoleUserPolicy", policy =>
        policy.Requirements.Add(new ADRoleRequirement(new[] { "User", "Admin" })));

    // Fallback policy for authenticated users
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Register Active Directory and Authorization services
builder.Services.AddScoped<IActiveDirectoryService, ActiveDirectoryService>();
builder.Services.AddScoped<IAuthorizationHandler, ADGroupAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ADRoleAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ComputerAccountAuthorizationHandler>();

// Register existing services
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IPerformanceMonitoringService, PerformanceMonitoringService>();
builder.Services.AddSingleton<DebugBroadcastService>();
builder.Services.AddSingleton<TaskNotificationService>();

// Add FluentValidation validators
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.CreateTaskRequest>, CreateTaskRequestValidator>();
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.UpdateTaskRequest>, UpdateTaskRequestValidator>();
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.RegisterNodeRequest>, RegisterNodeRequestValidator>();
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.UpdateNodeRequest>, UpdateNodeRequestValidator>();
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.CreateFileLockRequest>, CreateFileLockRequestValidator>();
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.LoginRequest>, LoginRequestValidator>();
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.CreateTaskFolderProgressRequest>, CreateTaskFolderProgressRequestValidator>();
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.UpdateTaskFolderProgressRequest>, UpdateTaskFolderProgressRequestValidator>();
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.TaskQueryParameters>, TaskQueryParametersValidator>();
builder.Services.AddScoped<IValidator<VCDevTool.API.Models.NodeQueryParameters>, NodeQueryParametersValidator>();

// Add enhanced validators for improved security
builder.Services.AddScoped<EnhancedCreateTaskRequestValidator>();
builder.Services.AddScoped<EnhancedRegisterNodeRequestValidator>();
builder.Services.AddScoped<EnhancedCreateFileLockRequestValidator>();

// Add background services
builder.Services.AddHostedService<TaskCompletionService>();

// Add SignalR
builder.Services.AddSignalR();

// Configure CORS with restricted origins for production
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalClients", policy =>
    {
        if (builder.Environment.IsProduction())
        {
            // In production, specify exact allowed origins
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() 
                ?? new[] { "https://localhost:7289" }; // Default fallback
            
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials()
                  .WithExposedHeaders("Content-Disposition", "X-Correlation-ID");
        }
        else
        {
            // More permissive for development
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .WithExposedHeaders("Content-Disposition", "X-Correlation-ID");
        }
    });
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database")
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

// Add API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "VC Dev Tool API", 
        Version = "v1",
        Description = "Distributed file processing and task management API with Windows Authentication support"
    });
    
    // Add JWT authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // Add Windows authentication to Swagger
    if (isWindowsAuthEnabled)
    {
        c.AddSecurityDefinition("Windows", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "negotiate",
            Description = "Windows Authentication using Negotiate (Kerberos/NTLM)"
        });
    }
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = isWindowsAuthEnabled ? "Windows" : "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Add Razor Pages support
builder.Services.AddRazorPages().AddRazorRuntimeCompilation();

var app = builder.Build();

// Configure the HTTP request pipeline.

// Add correlation ID middleware first
app.UseCorrelationId();

// Add global exception handling
app.UseGlobalExceptionHandling();

// Configure Swagger for development and staging
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "VC Dev Tool API V1");
        c.RoutePrefix = "swagger";
    });
    
    // Apply migrations in non-production environments
    /*
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            // Only apply migrations for SQL Server databases, not InMemory databases
            if (dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
            {
                dbContext.Database.Migrate();
                Log.Information("Database migrations applied successfully");
            }
            else
            {
                Log.Information("Skipping migrations for InMemory database");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error applying database migrations");
            throw;
        }
    }
    */
    Log.Information("Skipping database migrations for now");
}

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    if (context.Request.IsHttps || app.Environment.IsDevelopment())
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
    
    await next();
});

// Enable HTTPS redirection in production
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

// Apply CORS policy
app.UseCors("AllowLocalClients");

// Add health checks endpoint
app.MapHealthChecks("/health");

app.UseRouting();

// Authentication & Authorization
app.UseAuthentication();

// Add Windows Authentication enrichment middleware after authentication
if (isWindowsAuthEnabled)
{
    app.UseWindowsAuthenticationEnrichment();
}

app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

// Secure SignalR hubs (require authentication)
app.MapHub<DebugHub>("/debugHub"); //.RequireAuthorization("NodePolicy");
app.MapHub<TaskHub>("/taskHub"); //.RequireAuthorization("NodePolicy");

// Log startup information
Log.Information("VCDevTool API starting up...");
Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);
Log.Information("API Server listening on port 5289");
Log.Information("Windows Authentication Enabled: {WindowsAuthEnabled}", isWindowsAuthEnabled);

if (app.Environment.IsProduction())
{
    Log.Information("HTTPS redirection enabled");
    Log.Information("Security headers configured");
}

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make Program class accessible for testing
public partial class Program { }
