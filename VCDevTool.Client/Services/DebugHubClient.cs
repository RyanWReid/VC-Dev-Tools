using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace VCDevTool.Client.Services
{
    public class DebugHubClient : IDisposable
    {
        private HubConnection? _hubConnection;
        private string _serverUrl;
        private bool _isConnected;
        private bool _isDisposed;

        // Event for receiving debug messages
        public event EventHandler<DebugMessageEventArgs>? DebugMessageReceived;

        public DebugHubClient(string serverUrl)
        {
            _serverUrl = serverUrl;
        }

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public async Task StartAsync()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(DebugHubClient));

            if (_hubConnection != null && IsConnected)
                return;

            try
            {
                // Build the hub URL
                string hubUrl = $"{_serverUrl.TrimEnd('/')}/debugHub";

                // Create the connection
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl)
                    .WithAutomaticReconnect()
                    .Build();

                // Register handlers
                _hubConnection.On<object>("ReceiveDebugMessage", message =>
                {
                    // Convert the anonymous object to our typed one
                    // This is needed because SignalR deserializes to dynamic objects
                    try
                    {
                        dynamic debugData = message;
                        var debugMessage = new DebugMessage
                        {
                            Source = debugData.Source ?? "Unknown",
                            Message = debugData.Message ?? "",
                            NodeId = debugData.NodeId,
                            Timestamp = debugData.Timestamp
                        };

                        // Raise the event
                        DebugMessageReceived?.Invoke(this, new DebugMessageEventArgs(debugMessage));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error processing debug message: {ex.Message}");
                    }
                });

                // Handle reconnection
                _hubConnection.Reconnected += async connectionId =>
                {
                    System.Diagnostics.Debug.WriteLine($"Debug hub reconnected with ID: {connectionId}");
                    _isConnected = true;
                };

                _hubConnection.Reconnecting += async error =>
                {
                    System.Diagnostics.Debug.WriteLine($"Debug hub reconnecting: {error?.Message}");
                    _isConnected = false;
                };

                _hubConnection.Closed += async error =>
                {
                    System.Diagnostics.Debug.WriteLine($"Debug hub connection closed: {error?.Message}");
                    _isConnected = false;
                };

                // Start the connection
                await _hubConnection.StartAsync();
                _isConnected = true;
                System.Diagnostics.Debug.WriteLine("Connected to debug hub");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error connecting to debug hub: {ex.Message}");
                _isConnected = false;
            }
        }

        public async Task StopAsync()
        {
            if (_hubConnection != null)
            {
                try
                {
                    await _hubConnection.StopAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping debug hub connection: {ex.Message}");
                }
                finally
                {
                    _isConnected = false;
                }
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (_hubConnection != null)
                {
                    // Fire and forget to avoid issues in finalizers
                    try
                    {
                        _hubConnection.StopAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore exceptions in dispose
                    }
                    _hubConnection.DisposeAsync().AsTask().ConfigureAwait(false);
                }
                _isDisposed = true;
            }
        }

        public void UpdateServerUrl(string serverUrl)
        {
            if (_serverUrl != serverUrl)
            {
                _serverUrl = serverUrl;
                if (IsConnected)
                {
                    // Restart the connection with the new URL
                    Task.Run(async () =>
                    {
                        await StopAsync();
                        await StartAsync();
                    });
                }
            }
        }
    }

    public class DebugMessage
    {
        public string Source { get; set; } = "Unknown";
        public string Message { get; set; } = string.Empty;
        public string? NodeId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class DebugMessageEventArgs : EventArgs
    {
        public DebugMessage Message { get; }

        public DebugMessageEventArgs(DebugMessage message)
        {
            Message = message;
        }
    }
} 