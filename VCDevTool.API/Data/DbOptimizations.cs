using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace VCDevTool.API.Data
{
    /// <summary>
    /// Database optimization configurations and performance enhancements
    /// </summary>
    public static class DbOptimizations
    {
        /// <summary>
        /// Configure performance-optimized DbContext options
        /// </summary>
        public static void ConfigurePerformanceOptions(DbContextOptionsBuilder options, string connectionString, bool isProduction)
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                // Connection resilience
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);

                // Command timeout for long-running operations
                sqlOptions.CommandTimeout(120);

                if (isProduction)
                {
                    // Production optimizations
                    sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                }
            });

            // Performance configurations
            options.ConfigureWarnings(warnings =>
            {
                if (isProduction)
                {
                    warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.SensitiveDataLoggingEnabledWarning);
                }
            });

            // Connection pooling (default in ASP.NET Core, but explicitly configure)
            options.EnableThreadSafetyChecks(false); // Better performance in production
            options.EnableSensitiveDataLogging(!isProduction);
            options.EnableDetailedErrors(!isProduction);

            if (!isProduction)
            {
                // Development performance monitoring
                options.LogTo(
                    message => Debug.WriteLine(message),
                    Microsoft.Extensions.Logging.LogLevel.Information);
            }
        }

        /// <summary>
        /// Get optimized query for frequently accessed task data
        /// </summary>
        public static IQueryable<T> WithPerformanceOptimizations<T>(this IQueryable<T> query) where T : class
        {
            return query.AsNoTracking(); // Read-only queries for better performance
        }

        /// <summary>
        /// Configure batch operations for bulk inserts/updates
        /// </summary>
        public static async Task<int> SaveChangesOptimizedAsync(this DbContext context, CancellationToken cancellationToken = default)
        {
            // Optimize for bulk operations
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                var result = await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.DetectChanges();
                return result;
            }
            finally
            {
                context.ChangeTracker.AutoDetectChangesEnabled = true;
            }
        }

        /// <summary>
        /// Execute raw SQL for high-performance operations
        /// </summary>
        public static class RawSqlQueries
        {
            public const string CleanupOldFileLocks = @"
                DELETE FROM FileLocks 
                WHERE LastUpdatedAt < DATEADD(MINUTE, -@timeoutMinutes, GETUTCDATE())";

            public const string GetTaskStatistics = @"
                SELECT 
                    Status,
                    COUNT(*) as Count,
                    AVG(CASE 
                        WHEN CompletedAt IS NOT NULL AND StartedAt IS NOT NULL 
                        THEN DATEDIFF(SECOND, StartedAt, CompletedAt) 
                        ELSE NULL 
                    END) as AvgDurationSeconds
                FROM Tasks 
                WHERE CreatedAt >= @fromDate
                GROUP BY Status";

            public const string GetNodePerformanceMetrics = @"
                SELECT 
                    t.AssignedNodeId,
                    COUNT(*) as TasksProcessed,
                    AVG(DATEDIFF(SECOND, t.StartedAt, t.CompletedAt)) as AvgProcessingTimeSeconds,
                    SUM(CASE WHEN t.Status = 4 THEN 1 ELSE 0 END) as FailedTasks
                FROM Tasks t
                WHERE t.AssignedNodeId IS NOT NULL 
                    AND t.StartedAt IS NOT NULL 
                    AND t.CreatedAt >= @fromDate
                GROUP BY t.AssignedNodeId";
        }

        /// <summary>
        /// Index maintenance and optimization recommendations
        /// </summary>
        public static class IndexMaintenanceQueries
        {
            public const string CheckIndexFragmentation = @"
                SELECT 
                    OBJECT_NAME(i.object_id) AS TableName,
                    i.name AS IndexName,
                    ips.avg_fragmentation_in_percent,
                    ips.page_count
                FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
                INNER JOIN sys.indexes i ON i.object_id = ips.object_id AND i.index_id = ips.index_id
                WHERE ips.avg_fragmentation_in_percent > 10
                    AND ips.page_count > 1000
                ORDER BY ips.avg_fragmentation_in_percent DESC";

            public const string ReorganizeIndex = "ALTER INDEX {0} ON {1} REORGANIZE";
            public const string RebuildIndex = "ALTER INDEX {0} ON {1} REBUILD";
        }
    }
} 