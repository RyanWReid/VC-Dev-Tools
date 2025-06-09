# Enhanced Connection Management - Implementation Guide

## Overview

The Enhanced Connection Management system implements Phase 1.1 of the VCDevTool Task Handling Enhancement Plan. It provides enterprise-grade network resilience, automatic failover, and comprehensive connection monitoring for on-premises corporate environments.

## Key Features

### 🔄 Resilience Patterns
- **Circuit Breaker**: Automatically opens/closes based on failure rates
- **Exponential Backoff with Jitter**: Prevents thundering herd problems  
- **Retry Policies**: Configurable retry attempts with intelligent failure detection
- **Connection Pooling**: Optimized HTTP connection management

### 🌐 Network Fault Tolerance
- **Automatic Failover**: Switches between Enhanced and Standard clients
- **Health Monitoring**: Continuous connection health assessment
- **Auto Recovery**: Attempts to restore connections automatically
- **Timeout Configuration**: Per-operation timeout settings

### 📊 Monitoring & Observability
- **Connection Health Status**: Real-time connection monitoring
- **Performance Metrics**: Response time tracking
- **Circuit Breaker Status**: Visual feedback on circuit state
- **Comprehensive Logging**: Detailed logging with configurable levels

## Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│ TaskExecution   │────│ ConnectionManager │────│ EnhancedApiClient│
│ Service         │    │                  │    │                 │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                              │                          │
                              │                          │
                       ┌──────▼──────┐           ┌──────▼──────┐
                       │ ApiClient   │           │ Polly       │
                       │ (Standard)  │           │ Resilience  │
                       └─────────────┘           └─────────────┘
```

## Configuration

### appsettings.json
```json
{
  "Connection": {
    "DefaultTimeoutSeconds": 30,
    "HealthCheckTimeoutSeconds": 5,
    "TaskOperationTimeoutSeconds": 60,
    "FileOperationTimeoutSeconds": 120,
    "MaxRetryAttempts": 3,
    "BaseRetryDelayMs": 1000,
    "MaxRetryDelayMs": 30000,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerSamplingDurationSeconds": 60,
    "CircuitBreakerMinimumThroughput": 3,
    "CircuitBreakerBreakDurationSeconds": 30,
    "ConnectionPoolSize": 10,
    "ConnectionPoolTimeoutMs": 5000,
    "EnableConnectionPooling": true,
    "EnableCircuitBreaker": true,
    "EnableJitter": true
  },
  "ApiSettings": {
    "BaseUrl": "http://localhost:5289",
    "UseEnhancedClient": true,
    "EnableAutoRecovery": true,
    "HealthCheckIntervalMinutes": 1
  }
}
```

### Configuration Options

| Setting | Description | Default | Recommended Range |
|---------|-------------|---------|-------------------|
| `DefaultTimeoutSeconds` | Base timeout for HTTP requests | 30 | 15-60 |
| `HealthCheckTimeoutSeconds` | Health check timeout | 5 | 3-10 |
| `TaskOperationTimeoutSeconds` | Task operation timeout | 60 | 30-300 |
| `FileOperationTimeoutSeconds` | File operation timeout | 120 | 60-600 |
| `MaxRetryAttempts` | Maximum retry attempts | 3 | 1-5 |
| `BaseRetryDelayMs` | Initial retry delay | 1000 | 500-2000 |
| `MaxRetryDelayMs` | Maximum retry delay | 30000 | 10000-60000 |
| `CircuitBreakerFailureThreshold` | Failures before opening | 5 | 3-10 |
| `CircuitBreakerBreakDurationSeconds` | How long circuit stays open | 30 | 15-120 |
| `ConnectionPoolSize` | HTTP connection pool size | 10 | 5-20 |

## Usage Examples

### Basic Usage with Enhanced Client

```csharp
// Create connection options
var connectionOptions = new ConnectionOptions
{
    EnableCircuitBreaker = true,
    MaxRetryAttempts = 3,
    EnableJitter = true
};

// Create enhanced API client
using var apiClient = new EnhancedApiClient(
    "http://localhost:5289", 
    connectionOptions, 
    logger);

// Use with TaskExecutionService
var taskService = new TaskExecutionService(apiClient, nodeService);
```

### Using ConnectionManager for Automatic Failover

```csharp
// Create connection manager
using var connectionManager = new ConnectionManager(
    "http://localhost:5289",
    connectionOptions,
    logger,
    useEnhancedClient: true);

// Subscribe to connection events
connectionManager.ConnectionStatusChanged += (sender, e) =>
{
    Console.WriteLine($"Connection status changed: {e.CurrentStatus.IsHealthy}");
};

connectionManager.ConnectionMessage += (sender, message) =>
{
    Console.WriteLine($"Connection: {message}");
};

// Use the current client
var taskService = new TaskExecutionService(
    connectionManager.CurrentClient, 
    nodeService);
```

### Health Monitoring

```csharp
// Get detailed health status
var healthStatus = await apiClient.GetConnectionHealthAsync();

Console.WriteLine($"Healthy: {healthStatus.IsHealthy}");
Console.WriteLine($"Response Time: {healthStatus.ResponseTime.TotalMilliseconds}ms");
Console.WriteLine($"Circuit Breaker Open: {healthStatus.CircuitBreakerOpen}");
Console.WriteLine($"Server Version: {healthStatus.ServerVersion}");

if (!healthStatus.IsHealthy)
{
    Console.WriteLine($"Error: {healthStatus.LastError}");
}
```

### Manual Connection Switching

```csharp
// Switch to Enhanced Client
await connectionManager.SwitchToEnhancedClientAsync();

// Switch to Standard Client
await connectionManager.SwitchToStandardClientAsync();

// Recreate current connection
await connectionManager.RecreateConnectionAsync();

// Attempt auto-recovery
var recovered = await connectionManager.TryAutoRecoverConnectionAsync();
```

## Migration Guide

### From Existing ApiClient

1. **Update Dependencies**: Add required NuGet packages to your project
2. **Update Constructors**: Change from `ApiClient` to `IApiClient`
3. **Add Configuration**: Create `appsettings.json` with connection settings
4. **Initialize Enhanced Client**: Replace `ApiClient` instantiation

#### Before (Existing Code)
```csharp
var apiClient = new ApiClient("http://localhost:5289");
var taskService = new TaskExecutionService(apiClient, nodeService);
```

#### After (Enhanced Connection Management)
```csharp
var connectionOptions = configuration.GetSection("Connection")
    .Get<ConnectionOptions>() ?? new ConnectionOptions();

var connectionManager = new ConnectionManager(
    configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5289",
    connectionOptions,
    logger,
    configuration.GetValue<bool>("ApiSettings:UseEnhancedClient", true));

var taskService = new TaskExecutionService(
    connectionManager.CurrentClient, 
    nodeService);
```

### Backward Compatibility

The existing `ApiClient` class remains unchanged and continues to work. The `TaskExecutionService` supports both:

- `TaskExecutionService(ApiClient apiClient, NodeService nodeService)` - Original
- `TaskExecutionService(IApiClient apiClient, NodeService nodeService)` - Enhanced

## Environment-Specific Configurations

### Development Environment
```json
{
  "Connection": {
    "EnableCircuitBreaker": false,
    "MaxRetryAttempts": 1,
    "DefaultTimeoutSeconds": 10
  }
}
```

### Production Environment
```json
{
  "Connection": {
    "EnableCircuitBreaker": true,
    "MaxRetryAttempts": 5,
    "DefaultTimeoutSeconds": 60,
    "CircuitBreakerBreakDurationSeconds": 60
  }
}
```

### High-Latency Networks
```json
{
  "Connection": {
    "DefaultTimeoutSeconds": 120,
    "TaskOperationTimeoutSeconds": 300,
    "MaxRetryDelayMs": 60000,
    "CircuitBreakerBreakDurationSeconds": 120
  }
}
```

## Troubleshooting

### Common Issues

#### Circuit Breaker Always Open
- **Symptom**: Requests immediately fail with "Circuit breaker is open"
- **Solution**: Check `CircuitBreakerFailureThreshold` and `CircuitBreakerMinimumThroughput`
- **Action**: Lower failure threshold or increase minimum throughput

#### Excessive Retries
- **Symptom**: Requests take too long due to many retries
- **Solution**: Reduce `MaxRetryAttempts` or increase `BaseRetryDelayMs`
- **Action**: Tune retry policy for your network conditions

#### Connection Pool Exhaustion
- **Symptom**: Requests hang waiting for available connections
- **Solution**: Increase `ConnectionPoolSize` or reduce `ConnectionPoolTimeoutMs`
- **Action**: Monitor connection usage patterns

### Monitoring and Diagnostics

#### Enable Detailed Logging
```json
{
  "Logging": {
    "LogLevel": {
      "VCDevTool.Client.Services": "Debug"
    }
  }
}
```

#### Connection Health Dashboard
```csharp
// Periodic health check display
var timer = new Timer(async _ =>
{
    var health = await connectionManager.CurrentClient.GetConnectionHealthAsync();
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] " +
        $"Healthy: {health.IsHealthy}, " +
        $"Response: {health.ResponseTime.TotalMilliseconds:F0}ms, " +
        $"Circuit: {(health.CircuitBreakerOpen ? "OPEN" : "CLOSED")}");
}, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
```

## Performance Considerations

### Network Optimization
- Use connection pooling for high-throughput scenarios
- Tune timeout values based on network latency
- Enable jitter to prevent synchronized retry storms

### Memory Management
- Dispose `ConnectionManager` and API clients properly
- Monitor connection pool usage
- Use appropriate timeout values to prevent resource leaks

### Scalability
- Connection pool size should match expected concurrent requests
- Circuit breaker thresholds should reflect acceptable failure rates
- Health check intervals should balance monitoring with resource usage

## Best Practices

1. **Configuration Management**
   - Store sensitive settings in secure configuration
   - Use different configurations for different environments
   - Validate configuration values on startup

2. **Error Handling**
   - Always handle `BrokenCircuitException`
   - Log connection events for troubleshooting
   - Implement graceful degradation strategies

3. **Monitoring**
   - Monitor connection health metrics
   - Set up alerts for circuit breaker events
   - Track response times and failure rates

4. **Testing**
   - Test failover scenarios in staging
   - Validate retry behavior under load
   - Verify circuit breaker thresholds

## Future Enhancements

The Enhanced Connection Management system is designed to support future enhancements including:

- **Network Discovery Service** (Phase 1.1)
- **Offline Capability** (Phase 1.1)  
- **Load Balancing** (Phase 2)
- **Metrics Export** (Phase 3)
- **Distributed Tracing** (Phase 3)

## Support

For issues related to Enhanced Connection Management:

1. Check the configuration settings
2. Review the logs for error patterns
3. Test with different timeout values
4. Verify network connectivity
5. Consult the troubleshooting section above

## References

- [VCDevTool Task Handling Enhancement Plan](../TASK_HANDLING_ENHANCEMENT_PLAN.md)
- [Polly Resilience Library Documentation](https://www.pollydocs.org/)
- [Circuit Breaker Pattern](https://docs.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- [Exponential Backoff](https://docs.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/implement-exponential-backoff) 