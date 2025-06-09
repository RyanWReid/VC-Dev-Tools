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

namespace VCDevTool.Client.Services
{
    public class ApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly int _maxRetries = 3;
        private readonly int _retryDelayMs = 1000;

        public ApiClient(string baseUrl = "http://localhost:5289")
        {
            _httpClient = new HttpClient();
            _baseUrl = baseUrl;
            
            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromSeconds(10); // Set a reasonable timeout
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        // Test connection to the API
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Simple ping test to the API host
                var uri = new Uri(_baseUrl);
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(uri.Host, 3000);
                    if (reply.Status != IPStatus.Success)
                    {
                        return false;
                    }
                }

                // Try to connect to the API
                var response = await _httpClient.GetAsync("api/health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Task Management
        public async Task<List<BatchTask>> GetTasksAsync()
        {
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<List<BatchTask>>("api/tasks") ?? new List<BatchTask>());
        }

        public async Task<BatchTask?> GetTaskByIdAsync(int taskId)
        {
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<BatchTask>($"api/tasks/{taskId}"));
        }

        public async Task<BatchTask?> CreateTaskAsync(BatchTask task)
        {
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.PostAsJsonAsync("api/tasks", task);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<BatchTask>(_jsonOptions);
            });
        }

        public async Task<BatchTask?> UpdateTaskStatusAsync(int taskId, BatchTaskStatus status, string? resultMessage = null)
        {
            return await ExecuteWithRetryAsync(async () => 
            {
                var request = new
                {
                    Status = status,
                    ResultMessage = resultMessage
                };

                var response = await _httpClient.PutAsJsonAsync($"api/tasks/{taskId}/status", request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<BatchTask>(_jsonOptions);
            });
        }

        public async Task<bool> AssignTaskToNodeAsync(int taskId, string nodeId)
        {
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.PutAsync($"api/tasks/{taskId}/assign/{nodeId}", null);
                return response.IsSuccessStatusCode;
            });
        }

        // Node Management
        public async Task<List<ComputerNode>> GetAvailableNodesAsync()
        {
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<List<ComputerNode>>("api/nodes") ?? new List<ComputerNode>());
        }

        // Retrieve all nodes, including offline ones
        public async Task<List<ComputerNode>> GetAllNodesAsync()
        {
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

                // Send to the API
                var response = await ExecuteWithRetryAsync(async () => await _httpClient.PostAsJsonAsync("api/nodes/register", node));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ComputerNode>(_jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error registering node: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> SendHeartbeatAsync(string nodeId)
        {
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.PostAsync($"api/nodes/{nodeId}/heartbeat", null);
                return response.IsSuccessStatusCode;
            });
        }

        public async Task<bool> UpdateNodeAsync(ComputerNode node)
        {
            return await ExecuteWithRetryAsync(async () => 
            {
                var response = await _httpClient.PutAsJsonAsync($"api/nodes/{node.Id}", node);
                return response.IsSuccessStatusCode;
            });
        }

        // File Locking
        public async Task<bool> TryAcquireFileLockAsync(string filePath, string nodeId)
        {
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
            return await ExecuteWithRetryAsync(async () => 
                await _httpClient.GetFromJsonAsync<List<FileLock>>("api/filelocks") ?? new List<FileLock>());
        }

        // Debug functionality
        public async Task<bool> SendDebugMessageAsync(string source, string message, string? nodeId = null)
        {
            try
            {
                var request = new
                {
                    Source = source,
                    Message = message,
                    NodeId = nodeId
                };

                var response = await _httpClient.PostAsJsonAsync("api/debug/send", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending debug message: {ex.Message}");
                return false;
            }
        }

        // Generic retry method
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
    }
} 