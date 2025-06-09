using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using VCDevTool.Shared;

namespace VCDevTool.Client.Services
{
    public class TaskHubClient : IDisposable
    {
        private HubConnection? _hubConnection;
        private string _serverUrl;
        private bool _isDisposed;

        // Event for receiving task notifications
        public event EventHandler<TaskNotificationEventArgs>? TaskNotificationReceived;

        public TaskHubClient(string serverUrl)
        {
            _serverUrl = serverUrl;
        }

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public async Task StartAsync()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TaskHubClient));

            if (_hubConnection != null && IsConnected)
                return;

            try
            {
                // Build the hub URL
                string hubUrl = $"{_serverUrl.TrimEnd('/')}/taskHub";

                // Create the connection
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl)
                    .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                    .Build();

                // Register handlers for task notifications
                _hubConnection.On<object>("ReceiveTaskNotification", notification =>
                {
                    try
                    {
                        dynamic taskData = notification;
                        var taskNotification = new TaskNotification
                        {
                            TaskId = taskData.TaskId,
                            NodeId = taskData.NodeId,
                            TaskType = (TaskType)taskData.TaskType,
                            Status = (BatchTaskStatus)taskData.Status,
                            Message = taskData.Message ?? "",
                            Timestamp = taskData.Timestamp
                        };

                        // Raise the event
                        TaskNotificationReceived?.Invoke(this, new TaskNotificationEventArgs(taskNotification));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing task notification: {ex.Message}");
                    }
                });

                // Connection state events
                _hubConnection.Reconnected += connectionId =>
                {
                    Debug.WriteLine($"Task hub reconnected with ID: {connectionId}");
                    return Task.CompletedTask;
                };

                _hubConnection.Reconnecting += error =>
                {
                    Debug.WriteLine($"Task hub reconnecting: {error?.Message}");
                    return Task.CompletedTask;
                };

                _hubConnection.Closed += error =>
                {
                    Debug.WriteLine($"Task hub connection closed: {error?.Message}");
                    
                    // Try to reconnect if not disposed
                    if (!_isDisposed)
                    {
                        Task.Delay(TimeSpan.FromSeconds(5))
                            .ContinueWith(_ => StartAsync());
                    }
                    return Task.CompletedTask;
                };

                // Start the connection
                await _hubConnection.StartAsync();
                Debug.WriteLine("Task hub connection started");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting task hub connection: {ex.Message}");
                
                // Try to reconnect after a delay if not disposed
                if (!_isDisposed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    await StartAsync();
                }
            }
        }

        public async Task StopAsync()
        {
            if (_hubConnection == null)
                return;

            try
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping task hub connection: {ex.Message}");
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

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            
            // Stop and dispose the hub connection
            StopAsync().Wait();
            
            GC.SuppressFinalize(this);
        }
    }

    public class TaskNotification
    {
        public int TaskId { get; set; }
        public string? NodeId { get; set; }
        public TaskType TaskType { get; set; }
        public BatchTaskStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class TaskNotificationEventArgs : EventArgs
    {
        public TaskNotification Notification { get; }

        public TaskNotificationEventArgs(TaskNotification notification)
        {
            Notification = notification;
        }
    }
} 