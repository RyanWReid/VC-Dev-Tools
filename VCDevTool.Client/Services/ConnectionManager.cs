using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VCDevTool.Client.Models;
using System.Diagnostics;

namespace VCDevTool.Client.Services
{
    public class ConnectionManager : IDisposable
    {
        private IApiClient _currentClient;
        private readonly string _baseUrl;
        private readonly ConnectionOptions _connectionOptions;
        private readonly ILogger<ConnectionManager>? _logger;
        private readonly System.Threading.Timer _healthCheckTimer;
        private bool _useEnhancedClient;
        private bool _disposed = false;
        private ConnectionHealthStatus _lastHealthStatus;

        public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
        public event EventHandler<string>? ConnectionMessage;

        public ConnectionManager(
            string baseUrl = "http://localhost:5289",
            ConnectionOptions? connectionOptions = null,
            ILogger<ConnectionManager>? logger = null,
            bool useEnhancedClient = true)
        {
            _baseUrl = baseUrl;
            _connectionOptions = connectionOptions ?? new ConnectionOptions();
            _logger = logger;
            _useEnhancedClient = useEnhancedClient;
            _lastHealthStatus = new ConnectionHealthStatus();

            // Initialize the appropriate client
            _currentClient = CreateClient();

            // Start health monitoring
            _healthCheckTimer = new System.Threading.Timer(PerformHealthCheck, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            _logger?.LogInformation("Connection Manager initialized with {ClientType} client", 
                _useEnhancedClient ? "Enhanced" : "Standard");
        }

        public IApiClient CurrentClient => _currentClient;

        public ConnectionHealthStatus LastHealthStatus => _lastHealthStatus;

        public bool IsUsingEnhancedClient => _useEnhancedClient;

        private IApiClient CreateClient()
        {
            if (_useEnhancedClient)
            {
                return new EnhancedApiClient(_baseUrl, _connectionOptions, 
                    _logger as ILogger<EnhancedApiClient>);
            }
            else
            {
                return new ApiClient(_baseUrl);
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                return await _currentClient.TestConnectionAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Connection test failed");
                return false;
            }
        }

        public async Task SwitchToEnhancedClientAsync()
        {
            if (_useEnhancedClient) return;

            _logger?.LogInformation("Switching to Enhanced API Client");

            var oldClient = _currentClient;
            _useEnhancedClient = true;
            _currentClient = CreateClient();

            // Test the new connection
            var isHealthy = await TestConnectionAsync();
            
            if (isHealthy)
            {
                // Dispose old client
                oldClient.Dispose();
                OnConnectionMessage("Switched to Enhanced API Client successfully");
                _logger?.LogInformation("Successfully switched to Enhanced API Client");
            }
            else
            {
                // Revert to old client
                _currentClient.Dispose();
                _currentClient = oldClient;
                _useEnhancedClient = false;
                OnConnectionMessage("Failed to switch to Enhanced API Client, reverted to Standard client");
                _logger?.LogWarning("Failed to switch to Enhanced API Client, reverted to Standard client");
            }
        }

        public async Task SwitchToStandardClientAsync()
        {
            if (!_useEnhancedClient) return;

            _logger?.LogInformation("Switching to Standard API Client");

            var oldClient = _currentClient;
            _useEnhancedClient = false;
            _currentClient = CreateClient();

            // Test the new connection
            var isHealthy = await TestConnectionAsync();
            
            if (isHealthy)
            {
                // Dispose old client
                oldClient.Dispose();
                OnConnectionMessage("Switched to Standard API Client successfully");
                _logger?.LogInformation("Successfully switched to Standard API Client");
            }
            else
            {
                // Revert to old client
                _currentClient.Dispose();
                _currentClient = oldClient;
                _useEnhancedClient = true;
                OnConnectionMessage("Failed to switch to Standard API Client, reverted to Enhanced client");
                _logger?.LogWarning("Failed to switch to Standard API Client, reverted to Enhanced client");
            }
        }

        public async Task RecreateConnectionAsync()
        {
            _logger?.LogInformation("Recreating connection");
            
            var oldClient = _currentClient;
            _currentClient = CreateClient();

            // Test the new connection
            var isHealthy = await TestConnectionAsync();
            
            if (isHealthy)
            {
                // Dispose old client
                oldClient.Dispose();
                OnConnectionMessage("Connection recreated successfully");
                _logger?.LogInformation("Connection recreated successfully");
            }
            else
            {
                // Revert to old client
                _currentClient.Dispose();
                _currentClient = oldClient;
                OnConnectionMessage("Failed to recreate connection, using previous connection");
                _logger?.LogWarning("Failed to recreate connection, using previous connection");
            }
        }

        public async Task<bool> TryAutoRecoverConnectionAsync()
        {
            _logger?.LogInformation("Attempting auto-recovery of connection");

            // First try recreating with same client type
            await RecreateConnectionAsync();
            var isHealthy = await TestConnectionAsync();
            
            if (isHealthy)
            {
                OnConnectionMessage("Auto-recovery successful with same client type");
                return true;
            }

            // If enhanced client failed, try switching to standard client
            if (_useEnhancedClient)
            {
                _logger?.LogInformation("Enhanced client failed, trying Standard client");
                await SwitchToStandardClientAsync();
                isHealthy = await TestConnectionAsync();
                
                if (isHealthy)
                {
                    OnConnectionMessage("Auto-recovery successful by switching to Standard client");
                    return true;
                }
            }
            else
            {
                // If standard client failed, try switching to enhanced client
                _logger?.LogInformation("Standard client failed, trying Enhanced client");
                await SwitchToEnhancedClientAsync();
                isHealthy = await TestConnectionAsync();
                
                if (isHealthy)
                {
                    OnConnectionMessage("Auto-recovery successful by switching to Enhanced client");
                    return true;
                }
            }

            OnConnectionMessage("Auto-recovery failed with both client types");
            _logger?.LogError("Auto-recovery failed with both client types");
            return false;
        }

        private async void PerformHealthCheck(object? state)
        {
            if (_disposed) return;

            try
            {
                var previousStatus = _lastHealthStatus;
                _lastHealthStatus = await _currentClient.GetConnectionHealthAsync();

                // Check if status changed
                if (previousStatus.IsHealthy != _lastHealthStatus.IsHealthy)
                {
                    var eventArgs = new ConnectionStatusChangedEventArgs
                    {
                        PreviousStatus = previousStatus,
                        CurrentStatus = _lastHealthStatus,
                        Timestamp = DateTime.UtcNow
                    };

                    OnConnectionStatusChanged(eventArgs);

                    if (_lastHealthStatus.IsHealthy)
                    {
                        OnConnectionMessage($"Connection restored. Response time: {_lastHealthStatus.ResponseTime.TotalMilliseconds:F0}ms");
                        _logger?.LogInformation("Connection restored. Response time: {ResponseTime}ms", 
                            _lastHealthStatus.ResponseTime.TotalMilliseconds);
                    }
                    else
                    {
                        OnConnectionMessage($"Connection lost: {_lastHealthStatus.LastError}");
                        _logger?.LogWarning("Connection lost: {Error}", _lastHealthStatus.LastError);

                        // Attempt auto-recovery if connection is lost
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(5000); // Wait 5 seconds before attempting recovery
                            await TryAutoRecoverConnectionAsync();
                        });
                    }
                }

                // Log circuit breaker status changes
                if (_lastHealthStatus.CircuitBreakerOpen && !previousStatus.CircuitBreakerOpen)
                {
                    OnConnectionMessage("Circuit breaker opened - requests will be blocked temporarily");
                    _logger?.LogWarning("Circuit breaker opened");
                }
                else if (!_lastHealthStatus.CircuitBreakerOpen && previousStatus.CircuitBreakerOpen)
                {
                    OnConnectionMessage("Circuit breaker closed - normal operation resumed");
                    _logger?.LogInformation("Circuit breaker closed");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during health check");
            }
        }

        protected virtual void OnConnectionStatusChanged(ConnectionStatusChangedEventArgs e)
        {
            ConnectionStatusChanged?.Invoke(this, e);
        }

        protected virtual void OnConnectionMessage(string message)
        {
            ConnectionMessage?.Invoke(this, message);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _healthCheckTimer?.Dispose();
                _currentClient?.Dispose();
                _disposed = true;
                _logger?.LogDebug("Connection Manager disposed");
            }
        }
    }

    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public ConnectionHealthStatus PreviousStatus { get; set; } = new();
        public ConnectionHealthStatus CurrentStatus { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
} 