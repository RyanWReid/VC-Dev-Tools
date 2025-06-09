using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using VCDevTool.API.Data;
using VCDevTool.API.Hubs;
using VCDevTool.API.Services;
using VCDevTool.Shared;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel server to listen on all interfaces
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    // Use Kestrel configuration from appsettings.json
    // And additionally ensure we're listening on all interfaces
    serverOptions.ListenAnyIP(5289);
});

// Add services to the container.
builder.Services.AddControllers();

// Configure Entity Framework
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register services
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddSingleton<DebugBroadcastService>();

// Add SignalR
builder.Services.AddSignalR();

// Configure CORS for client applications
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalClients", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Disposition"); // For file downloads
    });
});

// Add API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "VC Dev Tool API", Version = "v1" });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    
    // Apply migrations in development environment
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();
    }
}

// Disable HTTPS redirection for simplicity
// app.UseHttpsRedirection();

// Apply CORS policy
app.UseCors("AllowLocalClients");
app.UseAuthorization();

app.MapControllers();
app.MapHub<DebugHub>("/debugHub");

// Print a message to confirm we're listening on all interfaces
Console.WriteLine("API Server listening on all interfaces at port 5289");
Console.WriteLine("Press Ctrl+C to stop the server");

app.Run();
