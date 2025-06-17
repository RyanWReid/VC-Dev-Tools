using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCDevTool.API.Services;

namespace VCDevTool.API.Controllers
{
    /// <summary>
    /// Controller for performance monitoring and benchmarking
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    // // [Authorize(Policy = "AdminPolicy")] // AUTHENTICATION DISABLED
    public class PerformanceController : ControllerBase
    {
        private readonly IPerformanceMonitoringService _performanceService;
        private readonly ILogger<PerformanceController> _logger;

        public PerformanceController(
            IPerformanceMonitoringService performanceService,
            ILogger<PerformanceController> logger)
        {
            _performanceService = performanceService;
            _logger = logger;
        }

        /// <summary>
        /// Get performance metrics for a specific time period
        /// </summary>
        /// <param name="fromDate">Start date for metrics (optional, defaults to 24 hours ago)</param>
        /// <param name="toDate">End date for metrics (optional, defaults to now)</param>
        /// <returns>Performance metrics</returns>
        [HttpGet("metrics")]
        public async Task<ActionResult<PerformanceMetrics>> GetPerformanceMetrics(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                var from = fromDate ?? DateTime.UtcNow.AddDays(-1);
                var to = toDate ?? DateTime.UtcNow;

                if (from >= to)
                {
                    return BadRequest("FromDate must be earlier than ToDate");
                }

                if ((to - from).TotalDays > 30)
                {
                    return BadRequest("Date range cannot exceed 30 days");
                }

                var metrics = await _performanceService.GetPerformanceMetricsAsync(from, to);
                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving performance metrics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get database performance metrics
        /// </summary>
        /// <returns>Database performance metrics</returns>
        [HttpGet("database")]
        public async Task<ActionResult<DatabasePerformanceMetrics>> GetDatabasePerformance()
        {
            try
            {
                var metrics = await _performanceService.GetDatabasePerformanceAsync();
                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving database performance metrics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get node performance metrics
        /// </summary>
        /// <param name="fromDate">Start date for metrics (optional, defaults to 24 hours ago)</param>
        /// <returns>Node performance metrics</returns>
        [HttpGet("nodes")]
        public async Task<ActionResult<NodePerformanceMetrics[]>> GetNodePerformanceMetrics(
            [FromQuery] DateTime? fromDate = null)
        {
            try
            {
                var from = fromDate ?? DateTime.UtcNow.AddDays(-1);
                var metrics = await _performanceService.GetNodePerformanceMetricsAsync(from);
                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving node performance metrics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get system health status
        /// </summary>
        /// <returns>System health status</returns>
        [HttpGet("health")]
        public async Task<ActionResult<SystemHealthStatus>> GetSystemHealth()
        {
            try
            {
                var health = await _performanceService.GetSystemHealthAsync();
                
                // Return appropriate HTTP status based on health
                if (!health.IsHealthy)
                {
                    return StatusCode(503, health); // Service Unavailable
                }

                return Ok(health);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving system health");
                return StatusCode(500, new SystemHealthStatus
                {
                    Timestamp = DateTime.UtcNow,
                    IsHealthy = false,
                    Issues = new List<string> { $"Health check failed: {ex.Message}" }
                });
            }
        }

        /// <summary>
        /// Run performance benchmark tests
        /// </summary>
        /// <returns>Benchmark results</returns>
        [HttpPost("benchmark")]
        public async Task<ActionResult<BenchmarkResults>> RunBenchmark()
        {
            try
            {
                var results = new BenchmarkResults
                {
                    StartTime = DateTime.UtcNow,
                    Tests = new List<BenchmarkTest>()
                };

                // Database performance benchmark
                var dbBenchmark = await RunDatabaseBenchmark();
                results.Tests.Add(dbBenchmark);

                // API response time benchmark
                var apiBenchmark = await RunApiBenchmark();
                results.Tests.Add(apiBenchmark);

                results.EndTime = DateTime.UtcNow;
                results.TotalDurationMs = (results.EndTime - results.StartTime).TotalMilliseconds;

                _logger.LogInformation("Performance benchmark completed. Total duration: {Duration}ms", 
                    results.TotalDurationMs);

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running performance benchmark");
                return StatusCode(500, "Benchmark failed");
            }
        }

        /// <summary>
        /// Get performance recommendations based on current metrics
        /// </summary>
        /// <returns>Performance recommendations</returns>
        [HttpGet("recommendations")]
        public async Task<ActionResult<PerformanceRecommendations>> GetPerformanceRecommendations()
        {
            try
            {
                var recommendations = new PerformanceRecommendations
                {
                    GeneratedAt = DateTime.UtcNow,
                    Recommendations = new List<string>()
                };

                // Get current metrics to base recommendations on
                var dbMetrics = await _performanceService.GetDatabasePerformanceAsync();
                var health = await _performanceService.GetSystemHealthAsync();

                // Database performance recommendations
                if (dbMetrics.IndexedQueryPerformanceMs > 100)
                {
                    recommendations.Recommendations.Add(
                        $"Indexed queries are slow ({dbMetrics.IndexedQueryPerformanceMs}ms). Consider optimizing indexes or query patterns.");
                }

                if (dbMetrics.ComplexQueryPerformanceMs > 500)
                {
                    recommendations.Recommendations.Add(
                        $"Complex queries are slow ({dbMetrics.ComplexQueryPerformanceMs}ms). Consider query optimization or adding indexes.");
                }

                if (dbMetrics.ExpiredFileLocksCount > 50)
                {
                    recommendations.Recommendations.Add(
                        $"High number of expired file locks ({dbMetrics.ExpiredFileLocksCount}). Implement cleanup job or adjust timeout settings.");
                }

                // System health recommendations
                if (health.DatabaseResponseTimeMs > 1000)
                {
                    recommendations.Recommendations.Add(
                        $"Database response time is high ({health.DatabaseResponseTimeMs}ms). Check database server performance and network connectivity.");
                }

                if (health.StuckTasksCount > 0)
                {
                    recommendations.Recommendations.Add(
                        $"Found {health.StuckTasksCount} stuck tasks. Implement task timeout and cleanup mechanisms.");
                }

                if (health.TotalNodes > 0 && (double)health.AvailableNodes / health.TotalNodes < 0.8)
                {
                    recommendations.Recommendations.Add(
                        $"Node availability is low ({health.AvailableNodes}/{health.TotalNodes}). Check node health and connectivity.");
                }

                // Table size recommendations
                if (dbMetrics.TasksCount > 100000)
                {
                    recommendations.Recommendations.Add(
                        $"Large number of tasks ({dbMetrics.TasksCount:N0}). Consider implementing data archival for completed tasks.");
                }

                if (recommendations.Recommendations.Count == 0)
                {
                    recommendations.Recommendations.Add("System performance looks good! No immediate recommendations.");
                }

                return Ok(recommendations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating performance recommendations");
                return StatusCode(500, "Failed to generate recommendations");
            }
        }

        private async Task<BenchmarkTest> RunDatabaseBenchmark()
        {
            var test = new BenchmarkTest
            {
                Name = "Database Performance",
                StartTime = DateTime.UtcNow
            };

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                await _performanceService.GetDatabasePerformanceAsync();
                stopwatch.Stop();

                test.DurationMs = stopwatch.ElapsedMilliseconds;
                test.Success = true;
                test.Details = $"Database operations completed in {test.DurationMs}ms";
                
                if (test.DurationMs < 100)
                    test.Rating = "Excellent";
                else if (test.DurationMs < 500)
                    test.Rating = "Good";
                else if (test.DurationMs < 1000)
                    test.Rating = "Fair";
                else
                    test.Rating = "Poor";
            }
            catch (Exception ex)
            {
                test.Success = false;
                test.Details = $"Database benchmark failed: {ex.Message}";
                test.Rating = "Failed";
            }
            finally
            {
                test.EndTime = DateTime.UtcNow;
            }

            return test;
        }

        private async Task<BenchmarkTest> RunApiBenchmark()
        {
            var test = new BenchmarkTest
            {
                Name = "API Response Time",
                StartTime = DateTime.UtcNow
            };

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                // Simulate API calls
                await _performanceService.GetSystemHealthAsync();
                
                stopwatch.Stop();

                test.DurationMs = stopwatch.ElapsedMilliseconds;
                test.Success = true;
                test.Details = $"API calls completed in {test.DurationMs}ms";
                
                if (test.DurationMs < 50)
                    test.Rating = "Excellent";
                else if (test.DurationMs < 200)
                    test.Rating = "Good";
                else if (test.DurationMs < 500)
                    test.Rating = "Fair";
                else
                    test.Rating = "Poor";
            }
            catch (Exception ex)
            {
                test.Success = false;
                test.Details = $"API benchmark failed: {ex.Message}";
                test.Rating = "Failed";
            }
            finally
            {
                test.EndTime = DateTime.UtcNow;
            }

            return test;
        }
    }

    /// <summary>
    /// Benchmark results models
    /// </summary>
    public class BenchmarkResults
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double TotalDurationMs { get; set; }
        public List<BenchmarkTest> Tests { get; set; } = new();
    }

    public class BenchmarkTest
    {
        public string Name { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public long DurationMs { get; set; }
        public bool Success { get; set; }
        public string Details { get; set; } = string.Empty;
        public string Rating { get; set; } = string.Empty;
    }

    public class PerformanceRecommendations
    {
        public DateTime GeneratedAt { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }
} 