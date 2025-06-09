using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using VCDevTool.API.Data;

namespace VCDevTool.API.Tests.Data
{
    /// <summary>
    /// Test-specific database optimization configurations
    /// </summary>
    public static class TestDbOptimizations
    {
        /// <summary>
        /// Configure test-optimized DbContext options for SQLite in-memory testing
        /// </summary>
        public static void ConfigureTestOptions(DbContextOptionsBuilder options, SqliteConnection connection)
        {
            options.UseSqlite(connection, sqliteOptions =>
            {
                // Basic SQLite configuration
                sqliteOptions.CommandTimeout(30);
            });

            // Test-specific configurations
            options.EnableSensitiveDataLogging(true);
            options.EnableDetailedErrors(true);
            options.LogTo(message => System.Diagnostics.Debug.WriteLine(message));
            
            // Configure warnings
            options.ConfigureWarnings(warnings =>
            {
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning);
            });
        }

        /// <summary>
        /// Configure test-optimized DbContext options for InMemory testing
        /// </summary>
        public static void ConfigureInMemoryTestOptions(DbContextOptionsBuilder options, string databaseName)
        {
            options.UseInMemoryDatabase(databaseName);
            
            // Test-specific configurations
            options.EnableSensitiveDataLogging(true);
            options.EnableDetailedErrors(true);
            options.LogTo(message => System.Diagnostics.Debug.WriteLine(message));
            
            // Configure warnings for InMemory
            options.ConfigureWarnings(warnings =>
            {
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning);
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning);
            });
        }

        /// <summary>
        /// Configure optimized SQLite options for integration tests
        /// </summary>
        public static void ConfigureSqliteTestOptions(DbContextOptionsBuilder options, string connectionString)
        {
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
            });

            // Test-specific configurations
            options.EnableSensitiveDataLogging(true);
            options.EnableDetailedErrors(true);
            options.LogTo(message => System.Diagnostics.Debug.WriteLine(message));
            
            // Configure warnings for SQLite
            options.ConfigureWarnings(warnings =>
            {
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning);
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
            });
        }

        /// <summary>
        /// Initialize test database with proper schema
        /// </summary>
        public static void InitializeTestDatabase(AppDbContext context)
        {
            context.Database.EnsureCreated();
            
            // Enable foreign keys for SQLite
            if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON");
                context.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL");
                context.Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL");
                context.Database.ExecuteSqlRaw("PRAGMA cache_size = 1000");
                context.Database.ExecuteSqlRaw("PRAGMA temp_store = memory");
            }
        }

        /// <summary>
        /// Create a SQLite in-memory connection for testing
        /// </summary>
        public static SqliteConnection CreateInMemoryConnection()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Create a test-specific AppDbContext with SQLite in-memory database
        /// </summary>
        public static AppDbContext CreateSqliteInMemoryContext()
        {
            var connection = CreateInMemoryConnection();
            var options = new DbContextOptionsBuilder<AppDbContext>();
            ConfigureTestOptions(options, connection);
            
            var context = new AppDbContext(options.Options);
            InitializeTestDatabase(context);
            
            return context;
        }

        /// <summary>
        /// Create a test-specific AppDbContext with Entity Framework In-Memory database
        /// </summary>
        public static AppDbContext CreateInMemoryContext(string databaseName = null)
        {
            databaseName ??= $"TestDb_{Guid.NewGuid()}";
            
            var options = new DbContextOptionsBuilder<AppDbContext>();
            ConfigureInMemoryTestOptions(options, databaseName);
            
            var context = new AppDbContext(options.Options);
            InitializeTestDatabase(context);
            
            return context;
        }

        /// <summary>
        /// Create a test database context with optimal settings for integration tests
        /// </summary>
        public static AppDbContext CreateIntegrationTestContext(string connectionString = null)
        {
            connectionString ??= $"DataSource=IntegrationTest_{Guid.NewGuid()}.db";
            
            var options = new DbContextOptionsBuilder<AppDbContext>();
            ConfigureSqliteTestOptions(options, connectionString);
            
            var context = new AppDbContext(options.Options);
            InitializeTestDatabase(context);
            
            return context;
        }

        /// <summary>
        /// Reset database for clean test state
        /// </summary>
        public static async Task ResetDatabaseAsync(AppDbContext context)
        {
            if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                await context.Database.EnsureDeletedAsync();
                await context.Database.EnsureCreatedAsync();
            }
            else if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // For SQLite, clear all data but keep schema
                await TestDataSeeder.ClearAllTestDataAsync(context);
            }
        }
    }
} 