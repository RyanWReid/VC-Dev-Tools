using System;

namespace VCDevTool.Client.Models
{
    public class ConnectionOptions
    {
        public const string SectionName = "Connection";

        /// <summary>
        /// Base timeout for all HTTP requests in seconds
        /// </summary>
        public int DefaultTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Timeout for health check requests in seconds
        /// </summary>
        public int HealthCheckTimeoutSeconds { get; set; } = 5;

        /// <summary>
        /// Timeout for task operations in seconds
        /// </summary>
        public int TaskOperationTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Timeout for file operations in seconds
        /// </summary>
        public int FileOperationTimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Base delay between retries in milliseconds
        /// </summary>
        public int BaseRetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Maximum delay between retries in milliseconds
        /// </summary>
        public int MaxRetryDelayMs { get; set; } = 30000;

        /// <summary>
        /// Circuit breaker failure threshold
        /// </summary>
        public int CircuitBreakerFailureThreshold { get; set; } = 5;

        /// <summary>
        /// Circuit breaker sampling duration in seconds
        /// </summary>
        public int CircuitBreakerSamplingDurationSeconds { get; set; } = 60;

        /// <summary>
        /// Circuit breaker minimum throughput threshold
        /// </summary>
        public int CircuitBreakerMinimumThroughput { get; set; } = 3;

        /// <summary>
        /// Circuit breaker break duration in seconds
        /// </summary>
        public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;

        /// <summary>
        /// Connection pool size
        /// </summary>
        public int ConnectionPoolSize { get; set; } = 10;

        /// <summary>
        /// Connection pool timeout in milliseconds
        /// </summary>
        public int ConnectionPoolTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Enable connection pooling optimization
        /// </summary>
        public bool EnableConnectionPooling { get; set; } = true;

        /// <summary>
        /// Enable circuit breaker pattern
        /// </summary>
        public bool EnableCircuitBreaker { get; set; } = true;

        /// <summary>
        /// Enable exponential backoff with jitter
        /// </summary>
        public bool EnableJitter { get; set; } = true;
    }
} 