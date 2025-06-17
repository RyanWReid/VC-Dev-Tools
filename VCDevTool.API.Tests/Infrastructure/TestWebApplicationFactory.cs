using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VCDevTool.API.Data;
using VCDevTool.API.Tests.Data;
using FluentValidation;
using VCDevTool.API.Validators;
using VCDevTool.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using VCDevTool.API.Tests.Controllers;
using VCDevTool.API.Services;
using VCDevTool.API.Tests.Services;
using VCDevTool.Shared;

namespace VCDevTool.API.Tests.Infrastructure
{
    /// <summary>
    /// Test factory that properly configures the test environment with SQLite database and proper service registration
    /// </summary>
    public class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName;
        private readonly bool _useSqlite;

        // Parameterless constructor for xUnit compatibility
        public TestWebApplicationFactory() : this(false)
        {
        }

        internal TestWebApplicationFactory(bool useSqlite = false)
        {
            _databaseName = $"TestDb_{Guid.NewGuid()}";
            _useSqlite = useSqlite;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            
            builder.ConfigureServices(services =>
            {
                // Remove all existing DbContext related services to avoid dual provider registration
                var descriptorsToRemove = services.Where(d => 
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ImplementationType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(ITaskService) ||
                    d.ImplementationType == typeof(TaskService))
                    .ToList();

                foreach (var descriptor in descriptorsToRemove)
                {
                    services.Remove(descriptor);
                }

                // Configure database based on test requirements
                if (_useSqlite)
                {
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        TestDbOptimizations.ConfigureSqliteTestOptions(options, $"DataSource=TestDb_{_databaseName}.db");
                    });
                    
                    // Use SQLite-compatible task service
                    services.AddScoped<ITaskService, SqliteCompatibleTaskService>();
                }
                else
                {
                    // Use in-memory database for faster tests
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        TestDbOptimizations.ConfigureInMemoryTestOptions(options, _databaseName);
                    });
                    
                    // Use SQLite-compatible task service (works with InMemory too)
                    services.AddScoped<ITaskService, SqliteCompatibleTaskService>();
                }

                // Register all FluentValidation validators for tests
                services.AddScoped<IValidator<CreateTaskRequest>, CreateTaskRequestValidator>();
                services.AddScoped<IValidator<UpdateTaskRequest>, UpdateTaskRequestValidator>();
                services.AddScoped<IValidator<RegisterNodeRequest>, RegisterNodeRequestValidator>();
                services.AddScoped<IValidator<UpdateNodeRequest>, UpdateNodeRequestValidator>();
                services.AddScoped<IValidator<CreateFileLockRequest>, CreateFileLockRequestValidator>();
                services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
                services.AddScoped<IValidator<CreateTaskFolderProgressRequest>, CreateTaskFolderProgressRequestValidator>();
                services.AddScoped<IValidator<UpdateTaskFolderProgressRequest>, UpdateTaskFolderProgressRequestValidator>();
                services.AddScoped<IValidator<TaskQueryParameters>, TaskQueryParametersValidator>();
                services.AddScoped<IValidator<NodeQueryParameters>, NodeQueryParametersValidator>();

                // Register other test services
                services.AddScoped<MockFileLockingService>();

                // Disable authentication for tests
                services.AddSingleton<IPolicyEvaluator, FakePolicyEvaluator>();

                // Add logging for tests
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

                // Build service provider and ensure database is created with test data
                var serviceProvider = services.BuildServiceProvider();
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Initialize database and seed test data
                TestDbOptimizations.InitializeTestDatabase(context);
                
                // Seed basic test data to ensure foreign key relationships work
                try
                {
                    TestDataSeeder.SeedBasicTestDataAsync(context).Wait();
                }
                catch (Exception ex)
                {
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<TestWebApplicationFactory>>();
                    logger.LogError(ex, "Failed to seed test data during factory initialization");
                    // Continue without test data - individual tests can seed as needed
                }
            });
        }

        /// <summary>
        /// Create a factory configured for SQLite testing
        /// </summary>
        public static TestWebApplicationFactory CreateSqliteFactory()
        {
            return new TestWebApplicationFactory(useSqlite: true);
        }

        /// <summary>
        /// Create a factory configured for in-memory testing (default)
        /// </summary>
        public static TestWebApplicationFactory CreateInMemoryFactory()
        {
            return new TestWebApplicationFactory(useSqlite: false);
        }

        /// <summary>
        /// Get a scoped service from the test container
        /// </summary>
        public T GetService<T>() where T : class
        {
            var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// Execute an action with a database context
        /// </summary>
        public async Task WithDatabaseAsync(Func<AppDbContext, Task> action)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await action(context);
        }

        /// <summary>
        /// Execute a function with a database context and return a result
        /// </summary>
        public async Task<TResult> WithDatabaseAsync<TResult>(Func<AppDbContext, Task<TResult>> action)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await action(context);
        }
    }

    /// <summary>
    /// Fake policy evaluator that allows all requests for testing
    /// </summary>
    public class FakePolicyEvaluator : IPolicyEvaluator
    {
        public virtual async Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
        {
            var testScheme = "Test";
            var principal = new System.Security.Claims.ClaimsPrincipal();
            principal.AddIdentity(new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", "test-user"),
                new System.Security.Claims.Claim("name", "Test User")
            }, testScheme));

            return AuthenticateResult.Success(new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, testScheme));
        }

        public virtual async Task<PolicyAuthorizationResult> AuthorizeAsync(AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource)
        {
            return PolicyAuthorizationResult.Success();
        }
    }
} 