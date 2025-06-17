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
using System.Diagnostics;

namespace VCDevTool.Client.Services
{
    public class ApiClient : IApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly int _maxRetries = 3;
        private readonly int _retryDelayMs = 1000;
        private readonly AuthenticationService _authService;

        public ApiClient(string baseUrl = "http://localhost:5289", AuthenticationService? authService = null)
        {
            _httpClient = new HttpClient();
            _baseUrl = baseUrl;
            _authService = authService ?? new AuthenticationService(baseUrl);
            
            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromSeconds(10); // Set a reasonable timeout
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Subscribe to authentication changes to update HTTP client headers
            _authService.AuthenticationChanged += OnAuthenticationChanged;
        }

        private void OnAuthenticationChanged(object? sender, AuthenticationEventArgs e)
        {
            // AUTHENTICATION DISABLED - No authorization headers needed
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        // Test connection to the API
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Try to get nodes endpoint which doesn't require authentication for the call itself
                // but will tell us if the server is responsive
                var response = await _httpClient.GetAsync("api/nodes");
                
                // We expect either success or 401 (unauthorized) - both indicate server is responsive
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return true;
                }

                return false;
            }
            catch (HttpRequestException)
            {
                // Network connectivity issues
                return false;
            }
            catch (TaskCanceledException)
            {
                // Timeout
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Connection test failed: {ex.Message}");
                return false;
            }
        }

        // Task Management
        public async Task<List<BatchTask>> GetTasksAsync()
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<List<BatchTask>>("api/tasks") ?? new List<BatchTask>());
        }

        public async Task<BatchTask?> GetTaskByIdAsync(int taskId)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<BatchTask>($"api/tasks/{taskId}"));
        }

        public async Task<BatchTask?> CreateTaskAsync(BatchTask task)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.PostAsJsonAsync("api/tasks", task);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<BatchTask>(_jsonOptions);
            });
        }

        public async Task<BatchTask?> UpdateTaskStatusAsync(int taskId, BatchTaskStatus status, string? resultMessage = null, byte[]? rowVersion = null)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var updateData = new
                {
                    Status = status,
                    ResultMessage = resultMessage,
                    RowVersion = rowVersion
                };

                var response = await _httpClient.PutAsJsonAsync($"api/tasks/{taskId}/status", updateData);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    if (string.IsNullOrWhiteSpace(responseContent))
                    {
                        System.Diagnostics.Debug.WriteLine("Warning: Empty response from server");
                        return null;
                    }

                    try
                    {
                        return JsonSerializer.Deserialize<BatchTask>(responseContent, _jsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"JSON deserialization error: {ex.Message}");
                        return null;
                    }
                }
                
                return null;
            });
        }

        private async Task<T?> HandleConcurrencyConflictAsync<T>(HttpResponseMessage response) where T : class
        {
            try
            {
                // Parse the conflict response
                var conflictContent = await response.Content.ReadAsStringAsync();
                var conflictData = JsonSerializer.Deserialize<ConflictResponse<T>>(conflictContent, _jsonOptions);
                
                // Log the conflict
                System.Diagnostics.Debug.WriteLine($"Concurrency conflict: {conflictData?.Message}");
                
                // You could implement retry logic here, or prompt the user, but for now just return the current entity
                return conflictData?.CurrentTask as T;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling concurrency conflict: {ex.Message}");
                return null;
            }
        }
        
        private class ConflictResponse<T>
        {
            public string? Message { get; set; }
            public T? CurrentTask { get; set; }
        }

        public async Task<bool> AssignTaskToNodeAsync(int taskId, string nodeId)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.PutAsync($"api/tasks/{taskId}/assign/{nodeId}", null);
                return response.IsSuccessStatusCode;
            });
        }

        // Node Management
        public async Task<List<ComputerNode>> GetAvailableNodesAsync()
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<List<ComputerNode>>("api/nodes") ?? new List<ComputerNode>());
        }

        // Retrieve all nodes, including offline ones
        public async Task<List<ComputerNode>> GetAllNodesAsync()
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<List<ComputerNode>>("api/nodes/all") ?? new List<ComputerNode>());
        }

        public async Task<ComputerNode> RegisterNodeAsync(ComputerNode node)
        {
            try
            {
                // Make sure the node has its hardware fingerprint set
                if (string.IsNullOrEmpty(node.HardwareFingerprint))
                {
                    System.Diagnostics.Debug.WriteLine("Warning: Node has no hardware fingerprint");
                }

                // First, try to authenticate with the auth service
                bool authenticated = await _authService.RegisterAsync(node);
                if (!authenticated)
                {
                    // If registration fails, try login (node might already exist)
                    authenticated = await _authService.LoginAsync(node.Id, node.HardwareFingerprint ?? "");
                }

                if (!authenticated)
                {
                    throw new UnauthorizedAccessException("Failed to authenticate with the API");
                }

                // Now that we're authenticated, we should be able to get node information
                // The registration happened through the auth service, so we just need to return the node info
                return node; // The auth service handles the actual registration
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error registering node: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> SendHeartbeatAsync(string nodeId)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.PostAsync($"api/nodes/{nodeId}/heartbeat", null);
                return response.IsSuccessStatusCode;
            });
        }

        public async Task<bool> UpdateNodeAsync(ComputerNode node)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.PutAsJsonAsync($"api/nodes/{node.Id}", node);
                return response.IsSuccessStatusCode;
            });
        }

        // File Locking
        public async Task<bool> TryAcquireFileLockAsync(string filePath, string nodeId)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var request = new
                {
                    FilePath = filePath,
                    NodeId = nodeId
                };

                var response = await _httpClient.PostAsJsonAsync("api/filelocks/acquire", request);
                return response.IsSuccessStatusCode;
            });
        }

        public async Task<bool> ReleaseFileLockAsync(string filePath, string nodeId)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var request = new
                {
                    FilePath = filePath,
                    NodeId = nodeId
                };

                var response = await _httpClient.PostAsJsonAsync("api/filelocks/release", request);
                return response.IsSuccessStatusCode;
            });
        }

        public async Task<List<FileLock>> GetActiveLocksAsync()
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<List<FileLock>>("api/filelocks") ?? new List<FileLock>());
        }

        // Reset all file locks
        public async Task<bool> ResetFileLocksAsync()
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.DeleteAsync("api/filelocks/reset");
                return response.IsSuccessStatusCode;
            });
        }

        // Task Folder Progress Management
        public async Task<List<TaskFolderProgress>> GetTaskFoldersAsync(int taskId)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<List<TaskFolderProgress>>($"api/tasks/{taskId}/folders") ?? new List<TaskFolderProgress>());
        }

        public async Task<TaskFolderProgress?> CreateTaskFolderAsync(int taskId, TaskFolderProgress folderProgress)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.PostAsJsonAsync($"api/tasks/{taskId}/folders", folderProgress);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TaskFolderProgress>(_jsonOptions);
            });
        }

        public async Task<TaskFolderProgress?> UpdateTaskFolderStatusAsync(int folderId, TaskFolderStatus status, string? nodeId = null, string? nodeName = null, double progress = 0.0, string? errorMessage = null, string? outputPath = null)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var updateData = new
                {
                    Status = status,
                    NodeId = nodeId,
                    NodeName = nodeName,
                    Progress = progress,
                    ErrorMessage = errorMessage,
                    OutputPath = outputPath
                };

                var response = await _httpClient.PutAsJsonAsync($"api/taskfolders/{folderId}/status", updateData);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    if (string.IsNullOrWhiteSpace(responseContent))
                    {
                        return null;
                    }

                    try
                    {
                        return JsonSerializer.Deserialize<TaskFolderProgress>(responseContent, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        return null;
                    }
                }
                
                return null;
            });
        }

        public async Task<bool> DeleteTaskFoldersAsync(int taskId)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.DeleteAsync($"api/tasks/{taskId}/folders");
                return response.IsSuccessStatusCode;
            });
        }

        // Debug Management
        public async Task<bool> SendDebugMessageAsync(string source, string message, string? nodeId = null)
        {
            await EnsureAuthenticatedAsync();
            return await ExecuteWithRetryAsync(async () => 
            {
                var debugData = new
                {
                    Source = source,
                    Message = message,
                    NodeId = nodeId,
                    Timestamp = DateTime.UtcNow
                };

                var response = await _httpClient.PostAsJsonAsync("api/debug", debugData);
                return response.IsSuccessStatusCode;
            });
        }

        private async Task EnsureAuthenticatedAsync()
        {
            // AUTHENTICATION DISABLED - No authentication required
            await Task.CompletedTask;
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    attempts++;
                    return await action();
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("401") && attempts == 1)
                {
                    // Try to refresh token on first 401 error
                    if (_authService.IsAuthenticated)
                    {
                        await _authService.RefreshTokenAsync();
                        continue; // Retry with new token
                    }
                    throw;
                }
                catch (Exception ex)
                {
                    if (attempts >= _maxRetries)
                    {
                        throw new Exception($"Failed after {_maxRetries} attempts: {ex.Message}", ex);
                    }
                    
                    await Task.Delay(_retryDelayMs * attempts); // Increasing delay between retries
                }
            }
        }

        // Add the GetBaseUrl method to expose the base URL to other classes
        public string GetBaseUrl() => _baseUrl;

        public async Task<ConnectionHealthStatus> GetConnectionHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var status = new ConnectionHealthStatus
            {
                LastChecked = DateTime.UtcNow
            };

            try
            {
                var isHealthy = await TestConnectionAsync();
                stopwatch.Stop();
                
                status.IsHealthy = isHealthy;
                status.ResponseTime = stopwatch.Elapsed;
                
                if (!isHealthy)
                {
                    status.LastError = "Connection test failed";
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                status.ResponseTime = stopwatch.Elapsed;
                status.IsHealthy = false;
                status.LastError = ex.Message;
            }

            return status;
        }

        public void Dispose()
        {
            _authService?.Dispose();
            _httpClient?.Dispose();
        }
    }
} 