using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Net;
using System.Threading;
using VCDevTool.Shared;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using VCDevTool.Client.Models;
using Polly;
using Polly.Extensions.Http;
using Polly.CircuitBreaker;
using System.Diagnostics;

namespace VCDevTool.Client.Services
{
    public class EnhancedApiClient : IApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ConnectionOptions _connectionOptions;
        private readonly ILogger<EnhancedApiClient>? _logger;
        private readonly ResiliencePipeline _retryPipeline;
        private readonly ResiliencePipeline _circuitBreakerPipeline;
        private readonly ResiliencePipeline _combinedPipeline;
        private readonly Random _jitterRandom;
        private bool _disposed = false;

        public EnhancedApiClient(
            string baseUrl = "http://localhost:5289",
            ConnectionOptions? connectionOptions = null,
            ILogger<EnhancedApiClient>? logger = null)
        {
            _baseUrl = baseUrl;
            _connectionOptions = connectionOptions ?? new ConnectionOptions();
            _logger = logger;
            _jitterRandom = new Random();

            // Configure HttpClient with connection pooling
            var handler = new SocketsHttpHandler()
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = _connectionOptions.ConnectionPoolSize,
                ConnectTimeout = TimeSpan.FromMilliseconds(_connectionOptions.ConnectionPoolTimeoutMs),
                ResponseDrainTimeout = TimeSpan.FromSeconds(30)
            };

            _httpClient = new HttpClient(handler);
            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromSeconds(_connectionOptions.DefaultTimeoutSeconds);

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Configure resilience pipelines
            _retryPipeline = CreateRetryPipeline();
            _circuitBreakerPipeline = CreateCircuitBreakerPipeline();
            _combinedPipeline = CreateCombinedPipeline();

            _logger?.LogInformation("Enhanced API Client initialized with base URL: {BaseUrl}", _baseUrl);
        }

        #region Resilience Pipeline Configuration

        private ResiliencePipeline CreateRetryPipeline()
        {
            return new ResiliencePipelineBuilder()
                .AddRetry(new Polly.Retry.RetryStrategyOptions
                {
                    ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception switch
                    {
                        HttpRequestException => true,
                        TaskCanceledException => true,
                        _ => args.Outcome.Result is HttpResponseMessage response && 
                            (!response.IsSuccessStatusCode && 
                             (response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                              response.StatusCode == HttpStatusCode.RequestTimeout ||
                              response.StatusCode == HttpStatusCode.TooManyRequests ||
                              (int)response.StatusCode >= 500))
                    }),
                    
                    MaxRetryAttempts = _connectionOptions.MaxRetryAttempts,
                    
                    Delay = TimeSpan.FromMilliseconds(_connectionOptions.BaseRetryDelayMs),
                    
                    BackoffType = DelayBackoffType.Exponential,
                    
                    UseJitter = _connectionOptions.EnableJitter,
                    
                    MaxDelay = TimeSpan.FromMilliseconds(_connectionOptions.MaxRetryDelayMs),
                    
                    OnRetry = args =>
                    {
                        _logger?.LogWarning("Retry attempt {Attempt} for {Operation}. Exception: {Exception}", 
                            args.AttemptNumber, 
                            args.Outcome.Exception?.Message ?? "HTTP error",
                            args.Outcome.Exception?.GetType().Name);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
        }

        private ResiliencePipeline CreateCircuitBreakerPipeline()
        {
            if (!_connectionOptions.EnableCircuitBreaker)
            {
                return ResiliencePipeline.Empty;
            }

            return new ResiliencePipelineBuilder()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception switch
                    {
                        HttpRequestException => true,
                        TaskCanceledException => true,
                        _ => args.Outcome.Result is HttpResponseMessage response && 
                            (!response.IsSuccessStatusCode && (int)response.StatusCode >= 500)
                    }),
                    
                    FailureRatio = 0.7, // 70% failure rate
                    MinimumThroughput = _connectionOptions.CircuitBreakerMinimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(_connectionOptions.CircuitBreakerSamplingDurationSeconds),
                    BreakDuration = TimeSpan.FromSeconds(_connectionOptions.CircuitBreakerBreakDurationSeconds),
                    
                    OnOpened = args =>
                    {
                        _logger?.LogError("Circuit breaker opened. Outcome: {Outcome}", args.Outcome.Exception?.Message);
                        return ValueTask.CompletedTask;
                    },
                    
                    OnClosed = args =>
                    {
                        _logger?.LogInformation("Circuit breaker closed");
                        return ValueTask.CompletedTask;
                    },
                    
                    OnHalfOpened = args =>
                    {
                        _logger?.LogInformation("Circuit breaker half-opened");
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
        }

        private ResiliencePipeline CreateCombinedPipeline()
        {
            var builder = new ResiliencePipelineBuilder();
            
            if (_connectionOptions.EnableCircuitBreaker)
            {
                builder.AddPipeline(_circuitBreakerPipeline);
            }
            
            return builder.AddPipeline(_retryPipeline).Build();
        }

        #endregion

        #region Connection Management

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(
                    TimeSpan.FromSeconds(_connectionOptions.HealthCheckTimeoutSeconds));

                // Simple ping test to the API host
                var uri = new Uri(_baseUrl);
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(uri.Host, 3000);
                    if (reply.Status != IPStatus.Success)
                    {
                        _logger?.LogWarning("Ping to {Host} failed with status: {Status}", uri.Host, reply.Status);
                        return false;
                    }
                }

                // Try to connect to the API with resilience
                var result = await _combinedPipeline.ExecuteAsync(async cancellationToken =>
                {
                    var response = await _httpClient.GetAsync("api/health", cancellationToken);
                    return response.IsSuccessStatusCode;
                }, cancellationTokenSource.Token);

                _logger?.LogDebug("Connection test result: {Result}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Connection test failed");
                return false;
            }
        }

        public async Task<ConnectionHealthStatus> GetConnectionHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var status = new ConnectionHealthStatus
            {
                LastChecked = DateTime.UtcNow
            };

            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(
                    TimeSpan.FromSeconds(_connectionOptions.HealthCheckTimeoutSeconds));

                var response = await _combinedPipeline.ExecuteAsync(async cancellationToken =>
                {
                    return await _httpClient.GetAsync("api/health", cancellationToken);
                }, cancellationTokenSource.Token);

                stopwatch.Stop();
                status.ResponseTime = stopwatch.Elapsed;
                status.IsHealthy = response.IsSuccessStatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var healthData = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(_jsonOptions, cancellationTokenSource.Token);
                    if (healthData?.ContainsKey("version") == true)
                    {
                        status.ServerVersion = healthData["version"]?.ToString() ?? "";
                    }
                }
                else
                {
                    status.LastError = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                }
            }
            catch (BrokenCircuitException ex)
            {
                stopwatch.Stop();
                status.ResponseTime = stopwatch.Elapsed;
                status.IsHealthy = false;
                status.CircuitBreakerOpen = true;
                status.LastError = "Circuit breaker is open";
                _logger?.LogWarning("Circuit breaker prevented health check: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                status.ResponseTime = stopwatch.Elapsed;
                status.IsHealthy = false;
                status.LastError = ex.Message;
                _logger?.LogError(ex, "Health check failed");
            }

            return status;
        }

        public string GetBaseUrl() => _baseUrl;

        #endregion

        #region Task Management

        public async Task<List<BatchTask>> GetTasksAsync()
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                return await _httpClient.GetFromJsonAsync<List<BatchTask>>("api/tasks", _jsonOptions, cancellationToken) 
                    ?? new List<BatchTask>();
            }, TimeSpan.FromSeconds(_connectionOptions.TaskOperationTimeoutSeconds));
        }

        public async Task<BatchTask?> GetTaskByIdAsync(int taskId)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                return await _httpClient.GetFromJsonAsync<BatchTask>($"api/tasks/{taskId}", _jsonOptions, cancellationToken);
            }, TimeSpan.FromSeconds(_connectionOptions.TaskOperationTimeoutSeconds));
        }

        public async Task<BatchTask?> CreateTaskAsync(BatchTask task)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var response = await _httpClient.PostAsJsonAsync("api/tasks", task, _jsonOptions, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<BatchTask>(_jsonOptions, cancellationToken);
            }, TimeSpan.FromSeconds(_connectionOptions.TaskOperationTimeoutSeconds));
        }

        public async Task<BatchTask?> UpdateTaskStatusAsync(int taskId, BatchTaskStatus status, string? resultMessage = null, byte[]? rowVersion = null)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var request = new
                {
                    Status = status,
                    ResultMessage = resultMessage,
                    RowVersion = rowVersion
                };

                var response = await _httpClient.PutAsJsonAsync($"api/tasks/{taskId}/status", request, _jsonOptions, cancellationToken);
                
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    // Handle concurrency conflict
                    var conflictResult = await HandleConcurrencyConflictAsync<BatchTask>(response, cancellationToken);
                    return conflictResult;
                }
                
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<BatchTask>(_jsonOptions, cancellationToken);
            }, TimeSpan.FromSeconds(_connectionOptions.TaskOperationTimeoutSeconds));
        }

        public async Task<bool> AssignTaskToNodeAsync(int taskId, string nodeId)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var response = await _httpClient.PutAsync($"api/tasks/{taskId}/assign/{nodeId}", null, cancellationToken);
                return response.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(_connectionOptions.TaskOperationTimeoutSeconds));
        }

        #endregion

        #region Node Management

        public async Task<List<ComputerNode>> GetAvailableNodesAsync()
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                return await _httpClient.GetFromJsonAsync<List<ComputerNode>>("api/nodes", _jsonOptions, cancellationToken) 
                    ?? new List<ComputerNode>();
            }, TimeSpan.FromSeconds(_connectionOptions.DefaultTimeoutSeconds));
        }

        public async Task<List<ComputerNode>> GetAllNodesAsync()
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                return await _httpClient.GetFromJsonAsync<List<ComputerNode>>("api/nodes/all", _jsonOptions, cancellationToken) 
                    ?? new List<ComputerNode>();
            }, TimeSpan.FromSeconds(_connectionOptions.DefaultTimeoutSeconds));
        }

        public async Task<ComputerNode> RegisterNodeAsync(ComputerNode node)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                // Make sure the node has its hardware fingerprint set
                if (string.IsNullOrEmpty(node.HardwareFingerprint))
                {
                    _logger?.LogWarning("Node has no hardware fingerprint");
                }

                // Send to the API
                var response = await _httpClient.PostAsJsonAsync("api/nodes/register", node, _jsonOptions, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ComputerNode>(_jsonOptions, cancellationToken) 
                    ?? throw new InvalidOperationException("Failed to deserialize registered node");
            }, TimeSpan.FromSeconds(_connectionOptions.DefaultTimeoutSeconds));
        }

        public async Task<bool> SendHeartbeatAsync(string nodeId)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var response = await _httpClient.PostAsync($"api/nodes/{nodeId}/heartbeat", null, cancellationToken);
                return response.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(_connectionOptions.HealthCheckTimeoutSeconds));
        }

        public async Task<bool> UpdateNodeAsync(ComputerNode node)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var response = await _httpClient.PutAsJsonAsync($"api/nodes/{node.Id}", node, _jsonOptions, cancellationToken);
                return response.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(_connectionOptions.DefaultTimeoutSeconds));
        }

        #endregion

        #region File Locking

        public async Task<bool> TryAcquireFileLockAsync(string filePath, string nodeId)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var request = new
                {
                    FilePath = filePath,
                    NodeId = nodeId
                };

                var response = await _httpClient.PostAsJsonAsync("api/filelocks/acquire", request, _jsonOptions, cancellationToken);
                return response.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(_connectionOptions.FileOperationTimeoutSeconds));
        }

        public async Task<bool> ReleaseFileLockAsync(string filePath, string nodeId)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var request = new
                {
                    FilePath = filePath,
                    NodeId = nodeId
                };

                var response = await _httpClient.PostAsJsonAsync("api/filelocks/release", request, _jsonOptions, cancellationToken);
                return response.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(_connectionOptions.FileOperationTimeoutSeconds));
        }

        public async Task<List<FileLock>> GetActiveLocksAsync()
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                return await _httpClient.GetFromJsonAsync<List<FileLock>>("api/filelocks", _jsonOptions, cancellationToken) 
                    ?? new List<FileLock>();
            }, TimeSpan.FromSeconds(_connectionOptions.FileOperationTimeoutSeconds));
        }

        public async Task<bool> ResetFileLocksAsync()
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var response = await _httpClient.PostAsync("api/filelocks/reset", null, cancellationToken);
                return response.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(_connectionOptions.FileOperationTimeoutSeconds));
        }

        #endregion

        #region Task Folder Progress Management

        public async Task<List<TaskFolderProgress>> GetTaskFoldersAsync(int taskId)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                return await _httpClient.GetFromJsonAsync<List<TaskFolderProgress>>($"api/tasks/{taskId}/folders", _jsonOptions, cancellationToken) 
                    ?? new List<TaskFolderProgress>();
            }, TimeSpan.FromSeconds(_connectionOptions.TaskOperationTimeoutSeconds));
        }

        public async Task<TaskFolderProgress?> CreateTaskFolderAsync(int taskId, TaskFolderProgress folderProgress)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var response = await _httpClient.PostAsJsonAsync($"api/tasks/{taskId}/folders", folderProgress, _jsonOptions, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TaskFolderProgress>(_jsonOptions, cancellationToken);
            }, TimeSpan.FromSeconds(_connectionOptions.TaskOperationTimeoutSeconds));
        }

        public async Task<TaskFolderProgress?> UpdateTaskFolderStatusAsync(int folderId, TaskFolderStatus status, string? nodeId = null, string? nodeName = null, double progress = 0.0, string? errorMessage = null, string? outputPath = null)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var request = new
                {
                    Status = status,
                    NodeId = nodeId,
                    NodeName = nodeName,
                    Progress = progress,
                    ErrorMessage = errorMessage,
                    OutputPath = outputPath
                };

                var response = await _httpClient.PutAsJsonAsync($"api/taskfolders/{folderId}/status", request, _jsonOptions, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TaskFolderProgress>(_jsonOptions, cancellationToken);
            }, TimeSpan.FromSeconds(_connectionOptions.TaskOperationTimeoutSeconds));
        }

        public async Task<bool> DeleteTaskFoldersAsync(int taskId)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var response = await _httpClient.DeleteAsync($"api/tasks/{taskId}/folders", cancellationToken);
                return response.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(_connectionOptions.TaskOperationTimeoutSeconds));
        }

        #endregion

        #region Debug Management

        public async Task<bool> SendDebugMessageAsync(string source, string message, string? nodeId = null)
        {
            return await ExecuteWithPolicyAsync(async cancellationToken =>
            {
                var request = new
                {
                    Source = source,
                    Message = message,
                    NodeId = nodeId,
                    Timestamp = DateTime.UtcNow
                };

                var response = await _httpClient.PostAsJsonAsync("api/debug", request, _jsonOptions, cancellationToken);
                return response.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(_connectionOptions.DefaultTimeoutSeconds));
        }

        #endregion

        #region Helper Methods

        private async Task<T> ExecuteWithPolicyAsync<T>(Func<CancellationToken, Task<T>> action, TimeSpan timeout)
        {
            using var cancellationTokenSource = new CancellationTokenSource(timeout);
            
            return await _combinedPipeline.ExecuteAsync(async cancellationToken =>
            {
                return await action(cancellationToken);
            }, cancellationTokenSource.Token);
        }

        private async Task<T?> HandleConcurrencyConflictAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
        {
            try
            {
                // Parse the conflict response
                var conflictContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var conflictData = JsonSerializer.Deserialize<ConflictResponse<T>>(conflictContent, _jsonOptions);
                
                // Log the conflict
                _logger?.LogWarning("Concurrency conflict: {Message}", conflictData?.Message);
                
                // Return the current entity
                return conflictData?.CurrentTask as T;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling concurrency conflict");
                return null;
            }
        }
        
        private class ConflictResponse<T>
        {
            public string? Message { get; set; }
            public T? CurrentTask { get; set; }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
                _logger?.LogDebug("Enhanced API Client disposed");
            }
        }

        #endregion
    }
} 