using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VCDevTool.Shared;
using System.Text.Json;
using System.Windows;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace VCDevTool.Client.Services
{
    public class TaskExecutionService : IDisposable
    {
        private IApiClient _apiClient;
        private readonly NodeService _nodeService;
        private readonly SlackNotificationService? _slackService;
        private readonly HashSet<int> _processedTaskIds;
        private System.Threading.Timer? _taskPollingTimer;
        private const int PollingIntervalMs = 5000;
        private bool _isExecutingTask;
        private bool _isDisposed;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly ConcurrentDictionary<string, Process> _runningProcesses = new();
        private readonly HashSet<string> _heldLocks = new();
        private readonly object _lockSetSync = new();

        public TaskExecutionService(IApiClient apiClient, NodeService nodeService)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _nodeService = nodeService ?? throw new ArgumentNullException(nameof(nodeService));
            _processedTaskIds = new HashSet<int>();
            _slackService = ((App)System.Windows.Application.Current).SlackService;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        // Overload for backward compatibility
        public TaskExecutionService(ApiClient apiClient, NodeService nodeService) 
            : this((IApiClient)apiClient, nodeService)
        {
        }

        public void UpdateApiClient(IApiClient apiClient)
        {
            if (apiClient == null)
                throw new ArgumentNullException(nameof(apiClient));
            
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TaskExecutionService));
                
            _apiClient = apiClient;
            
            RestartTaskPolling();
        }

        // Overload for backward compatibility
        public void UpdateApiClient(ApiClient apiClient)
        {
            UpdateApiClient((IApiClient)apiClient);
        }

        public void ClearProcessedTaskIds()
        {
            _processedTaskIds.Clear();
            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Manually cleared processed task cache");
        }

        public void StartTaskPolling()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TaskExecutionService));
                
            RestartTaskPolling();
        }

        public void StopTaskPolling()
        {
            _taskPollingTimer?.Dispose();
            _taskPollingTimer = null;
        }

        private void RestartTaskPolling()
        {
            StopTaskPolling();
            _processedTaskIds.Clear();
            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Task polling restarted - cleared processed task cache");
            _taskPollingTimer = new System.Threading.Timer(PollForTasks, null, 0, PollingIntervalMs);
        }

        private async void PollForTasks(object? state)
        {
            if (_isExecutingTask || _isDisposed)
                return;

            try
            {
                _isExecutingTask = true;
                await ProcessPendingTasksAsync();
                await HandleStuckTasksAsync();
                await CleanupCompletedTasksAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error polling tasks: {ex.Message}");
            }
            finally
            {
                _isExecutingTask = false;
            }
        }

        private async Task ProcessPendingTasksAsync()
        {
            var tasks = await _apiClient.GetTasksAsync();
            var tasksForThisNode = tasks.Where(t => 
                    IsTaskAssignedToNode(t, _nodeService.CurrentNode.Id) &&
                    !_processedTaskIds.Contains(t.Id)).ToList();
            
            // Debug: Show all running tasks and their assignments
            var runningTasks = tasks.Where(t => t.Status == BatchTaskStatus.Running).ToList();
            if (runningTasks.Any())
            {
                foreach (var runningTask in runningTasks)
                {
                    var assignedNodes = GetAssignedNodeIds(runningTask);
                    var isAssignedToThisNode = IsTaskAssignedToNode(runningTask, _nodeService.CurrentNode.Id);
                    UpdateDebugOutput($"[DEBUG] Task {runningTask.Id} ({runningTask.Type}) - Assigned to: [{string.Join(", ", assignedNodes)}] - This node ({_nodeService.CurrentNode.Name}) assigned: {isAssignedToThisNode}");
                }
            }
            
            // Modified filtering logic: Allow VolumeCompression tasks when Running, maintain existing behavior for other types
            var pendingTasks = tasksForThisNode.Where(t => 
                t.Status == BatchTaskStatus.Pending || 
                (t.Type == TaskType.VolumeCompression && t.Status == BatchTaskStatus.Running)
            ).ToList();
            
            if (pendingTasks.Any())
            {
                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Found {pendingTasks.Count} tasks to process (pending + running VolumeCompression tasks)");
            }
            
            foreach (var task in pendingTasks)
            {
                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Starting execution of task {task.Id} ({task.Type}) with status {task.Status}");
                _processedTaskIds.Add(task.Id); // Add to processed set only when we start executing
                await ExecuteTaskAsync(task);
            }
        }

        /// <summary>
        /// Gets the list of assigned node IDs for a task
        /// </summary>
        private List<string> GetAssignedNodeIds(BatchTask task)
        {
            try
            {
                if (!string.IsNullOrEmpty(task.AssignedNodeIds))
                {
                    return JsonSerializer.Deserialize<List<string>>(task.AssignedNodeIds) ?? new List<string>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing AssignedNodeIds for task {task.Id}: {ex.Message}");
            }
            
            // Fallback to single assigned node for backward compatibility
            if (!string.IsNullOrEmpty(task.AssignedNodeId))
            {
                return new List<string> { task.AssignedNodeId };
            }
            
            return new List<string>();
        }

        /// <summary>
        /// Checks if a task is assigned to the specified node by looking in both AssignedNodeId and AssignedNodeIds
        /// </summary>
        private bool IsTaskAssignedToNode(BatchTask task, string nodeId)
        {
            // Check single node assignment (backward compatibility)
            if (task.AssignedNodeId == nodeId)
            {
                return true;
            }
            
            // Check multiple node assignment
            try
            {
                if (!string.IsNullOrEmpty(task.AssignedNodeIds))
                {
                    var assignedNodeIds = JsonSerializer.Deserialize<List<string>>(task.AssignedNodeIds);
                    return assignedNodeIds?.Contains(nodeId) == true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing AssignedNodeIds for task {task.Id}: {ex.Message}");
            }
            
            return false;
        }

        private async Task HandleStuckTasksAsync()
        {
            var tasks = await _apiClient.GetTasksAsync();
            var stuckTasks = tasks.Where(t => 
                    t.Status == BatchTaskStatus.Running && 
                    _processedTaskIds.Contains(t.Id) &&
                    t.StartedAt.HasValue && 
                    DateTime.UtcNow - t.StartedAt.Value > TimeSpan.FromSeconds(30)).ToList();
            
            foreach (var task in stuckTasks)
            {
                if (task.Type == TaskType.TestMessage)
                {
                    await CompleteStuckTaskAsync(task);
                }
            }
        }

        private async Task CleanupCompletedTasksAsync()
        {
            var tasks = await _apiClient.GetTasksAsync();
            var completedTaskIds = tasks
                    .Where(t => (t.Status == BatchTaskStatus.Completed || t.Status == BatchTaskStatus.Failed) && 
                           _processedTaskIds.Contains(t.Id))
                    .Select(t => t.Id)
                    .ToList();
            
            foreach (var id in completedTaskIds)
                {
                    _processedTaskIds.Remove(id);
            }
        }

        private async Task ExecuteTaskAsync(BatchTask task)
        {
            try
            {
                task.StartedAt = DateTime.UtcNow;
                await _apiClient.UpdateTaskStatusAsync(task.Id, BatchTaskStatus.Running);

                    switch (task.Type)
                    {
                        case TaskType.TestMessage:
                        await ExecuteTestMessageTask(task);
                            break;
                        case TaskType.RenderThumbnails:
                        await ExecuteRenderThumbnailTask(task);
                            break;
                        case TaskType.PackageTask:
                        await ExecutePackageTask(task);
                            break;
                        case TaskType.VolumeCompression:
                        await ExecuteVolumeCompressionTask(task);
                            break;
                        case TaskType.RealityCapture:
                        await ExecuteRealityCaptureTask(task);
                            break;
                        default:
                        await _apiClient.UpdateTaskStatusAsync(task.Id, BatchTaskStatus.Failed, "Unsupported task type");
                            break;
                }
            }
            catch (Exception ex)
            {
                await _apiClient.UpdateTaskStatusAsync(task.Id, BatchTaskStatus.Failed, $"Error: {ex.Message}");
            }
        }

        private async Task CompleteStuckTaskAsync(BatchTask task)
        {
            try
            {
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id, 
                    BatchTaskStatus.Completed, 
                    $"Task auto-completed after timeout on {_nodeService.CurrentNode.Name} at {DateTime.Now}"
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error auto-completing stuck task: {ex.Message}");
            }
        }

        private void SendSlackNotificationAsync(string nodeName, BatchTask task)
        {
            try
            {
                Debug.WriteLine($"[Slack] Sending completion notification for task {task.Id} on node {nodeName}");
                _slackService?.SendTaskCompletionNotificationAsync(nodeName, task);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending Slack notification: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            StopTaskPolling();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task AbortCurrentTask(string nodeId)
        {
            UpdateDebugOutput($"[ABORT] Task abort requested for node {nodeId}");
            bool taskFoundAndCancelled = false;
            
            try
            {
                // 1. Cancel the current operation first
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = new CancellationTokenSource();
                    UpdateDebugOutput($"[ABORT] Cancellation token cancelled for node {nodeId}");
                }

                // 2. Kill any running external process for this node
                if (_runningProcesses.TryRemove(nodeId, out var process))
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                            UpdateDebugOutput($"[ABORT] Process {process.Id} killed for node {nodeId}");
                        }
                        else
                        {
                            UpdateDebugOutput($"[ABORT] Process for node {nodeId} had already exited");
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateDebugOutput($"[ABORT] Error killing process for node {nodeId}: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
                else
                {
                    UpdateDebugOutput($"[ABORT] No running process found for node {nodeId}");
                }

                // 3. Get and cancel tasks for this node - try multiple approaches
                UpdateDebugOutput($"[ABORT] Looking for tasks to cancel for node {nodeId}");
                
                try
                {
                    var tasks = await _apiClient.GetTasksAsync();
                    UpdateDebugOutput($"[ABORT] Retrieved {tasks.Count} total tasks from API");
                    
                    // Find tasks assigned to this node that are running or pending
                    var runningTasks = tasks.Where(t => 
                        t.AssignedNodeId == nodeId && 
                        (t.Status == BatchTaskStatus.Running || t.Status == BatchTaskStatus.Pending)).ToList();
                    
                    UpdateDebugOutput($"[ABORT] Found {runningTasks.Count} running/pending tasks for node {nodeId}");
                    
                    foreach (var task in runningTasks)
                    {
                        UpdateDebugOutput($"[ABORT] Cancelling task {task.Id} ({task.Type}) - Status: {task.Status}");
                        
                        try
                        {
                            var result = await _apiClient.UpdateTaskStatusAsync(
                                task.Id,
                                BatchTaskStatus.Cancelled,
                                "Task was manually aborted by user"
                            );
                            
                            if (result != null)
                            {
                                UpdateDebugOutput($"[ABORT] Successfully cancelled task {task.Id}");
                                taskFoundAndCancelled = true;
                            }
                            else
                            {
                                UpdateDebugOutput($"[ABORT] Warning: Null result when cancelling task {task.Id}");
                            }
                        }
                        catch (Exception ex)
                        {
                            UpdateDebugOutput($"[ABORT] Error cancelling task {task.Id}: {ex.Message}");
                            // Continue with other tasks even if one fails
                        }
                    }
                    
                    // Also check if there are tasks with multiple assigned nodes that include this node
                    var multiNodeTasks = tasks.Where(t => 
                        !string.IsNullOrEmpty(t.AssignedNodeIds) && 
                        t.AssignedNodeIds.Contains(nodeId) &&
                        (t.Status == BatchTaskStatus.Running || t.Status == BatchTaskStatus.Pending)).ToList();
                    
                    if (multiNodeTasks.Any())
                    {
                        UpdateDebugOutput($"[ABORT] Found {multiNodeTasks.Count} multi-node tasks involving node {nodeId}");
                        foreach (var task in multiNodeTasks)
                        {
                            if (!runningTasks.Any(rt => rt.Id == task.Id)) // Don't duplicate cancellation
                            {
                                UpdateDebugOutput($"[ABORT] Cancelling multi-node task {task.Id}");
                                try
                                {
                                    var result = await _apiClient.UpdateTaskStatusAsync(
                                        task.Id,
                                        BatchTaskStatus.Cancelled,
                                        $"Task was manually aborted by user on node {nodeId}"
                                    );
                                    
                                    if (result != null)
                                    {
                                        UpdateDebugOutput($"[ABORT] Successfully cancelled multi-node task {task.Id}");
                                        taskFoundAndCancelled = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    UpdateDebugOutput($"[ABORT] Error cancelling multi-node task {task.Id}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UpdateDebugOutput($"[ABORT] Error getting tasks from API: {ex.Message}");
                    throw; // Re-throw to handle in outer catch
                }

                // 4. Release all held locks for this node
                List<string> locksToRelease;
                lock (_lockSetSync)
                {
                    locksToRelease = _heldLocks.ToList();
                    _heldLocks.Clear();
                }
                
                UpdateDebugOutput($"[ABORT] Releasing {locksToRelease.Count} held locks for node {nodeId}");
                
                foreach (var normalizedFolderPath in locksToRelease)
                {
                    try
                    {
                        string folderLockPath = PathUtils.GetFolderLockKey(normalizedFolderPath);
                        bool released = await _apiClient.ReleaseFileLockAsync(folderLockPath, nodeId);
                        UpdateDebugOutput($"[ABORT] Released lock for folder: {normalizedFolderPath} - Success: {released}");
                    }
                    catch (Exception ex)
                    {
                        UpdateDebugOutput($"[ABORT] Error releasing lock for folder {normalizedFolderPath}: {ex.Message}");
                    }
                }
                
                // 5. Clean up any lingering locks in the database for this node
                try
                {
                    var locks = await _apiClient.GetActiveLocksAsync();
                    var nodeLocks = locks.Where(l => l.LockingNodeId == nodeId).ToList();
                    
                    if (nodeLocks.Any())
                    {
                        UpdateDebugOutput($"[ABORT] Found {nodeLocks.Count} additional locks to clean up for node {nodeId}");
                        
                        foreach (var lockItem in nodeLocks)
                        {
                            try
                            {
                                bool released = await _apiClient.ReleaseFileLockAsync(lockItem.FilePath, nodeId);
                                UpdateDebugOutput($"[ABORT] Released leftover lock: {lockItem.FilePath} - Success: {released}");
                            }
                            catch (Exception ex)
                            {
                                UpdateDebugOutput($"[ABORT] Error releasing leftover lock {lockItem.FilePath}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        UpdateDebugOutput($"[ABORT] No additional locks found for node {nodeId}");
                    }
                }
                catch (Exception ex)
                {
                    UpdateDebugOutput($"[ABORT] Error cleaning up leftover locks: {ex.Message}");
                }
                
                // 6. Update UI
                try
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var mainWindow = System.Windows.Application.Current.MainWindow;
                        if (mainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                        {
                            var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == nodeId);
                            if (node != null)
                            {
                                node.ClearActiveTask();
                                UpdateDebugOutput($"[ABORT] UI updated for node {nodeId}");
                            }
                            else
                            {
                                UpdateDebugOutput($"[ABORT] Warning: Node {nodeId} not found in UI");
                            }
                        }
                        else
                        {
                            UpdateDebugOutput($"[ABORT] Warning: MainViewModel not found for UI update");
                        }
                    });
                }
                catch (Exception ex)
                {
                    UpdateDebugOutput($"[ABORT] Error updating UI: {ex.Message}");
                }

                if (taskFoundAndCancelled)
                {
                    UpdateDebugOutput($"[ABORT] Task abort completed successfully for node {nodeId}");
                }
                else
                {
                    UpdateDebugOutput($"[ABORT] Warning: No active tasks found to cancel for node {nodeId}");
                }
            }
            catch (Exception ex)
            {
                UpdateDebugOutput($"[ABORT] Critical error aborting task for node {nodeId}: {ex.Message}");
                UpdateDebugOutput($"[ABORT] Stack trace: {ex.StackTrace}");
                
                // Re-throw the exception so the calling code knows there was an error
                throw new Exception($"Failed to abort task for node {nodeId}: {ex.Message}", ex);
            }
        }

        private async Task ExecuteTestMessageTask(BatchTask task)
        {
            try
            {
                // Check if task is already marked as completed
                if (task.Status == BatchTaskStatus.Completed)
                {
                    // Task was already handled by sender, just display the message
                    await DisplayMessageFromTask(task);
                    return;
                }
                
                // Mark task as running
                task.StartedAt = DateTime.UtcNow;
                await _apiClient.UpdateTaskStatusAsync(task.Id, BatchTaskStatus.Running);
                
                // Get the main view model to update the UI
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                            var mainWindow = System.Windows.Application.Current.MainWindow;
                            if (mainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                            {
                                var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
                                if (node != null)
                                {
                                    // Set initial progress at 0%
                                    node.SetActiveTask(task, 0);
                                }
                        }
                    });
                
                // Simulate progress - delay for a total of 3 seconds with incremental progress
                for (int i = 1; i <= 10; i++)
                {
                    await Task.Delay(300); // 300ms delay per step (10 steps = 3 seconds)
                    
                    double progress = i / 10.0; // Progress from 0.1 to 1.0
                    
                    // Update the progress on the UI
                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                        {
                                var mainWindow = System.Windows.Application.Current.MainWindow;
                                if (mainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                                {
                                    var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
                                    if (node != null)
                                    {
                                        node.SetActiveTask(task, progress);
                                    }
                        }
                    });
                }
                
                // Display the message
                bool messageDisplayed = await DisplayMessageFromTask(task);
                
                // Update task status to completed
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id, 
                    BatchTaskStatus.Completed, 
                    messageDisplayed
                        ? $"Message displayed on {_nodeService.CurrentNode.Name} at {DateTime.Now}"
                        : $"Message processed on {_nodeService.CurrentNode.Name} at {DateTime.Now}"
                );
                
                // After a small delay, clear the task from the node UI
                await Task.Delay(500);
                ClearTaskFromUI(task);
            }
            catch (Exception ex)
            {
                // Update task status to failed
                System.Diagnostics.Debug.WriteLine($"Error executing test message: {ex.Message}");
                try
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id, 
                        BatchTaskStatus.Failed, 
                        $"Error displaying message: {ex.Message}"
                    );
                }
                catch (Exception updateEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to update task status: {updateEx.Message}");
                }
            }
        }

        private async Task<bool> DisplayMessageFromTask(BatchTask task)
        {
            // Parse parameters to get the message
            if (!string.IsNullOrEmpty(task.Parameters))
            {
                try
                {
                    var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(task.Parameters);
                    if (parameters != null && parameters.TryGetValue("MessageText", out string messageText))
                    {
                        string senderName = "Another computer";
                        if (parameters.TryGetValue("SenderName", out string sender))
                        {
                            senderName = sender;
                        }
                        
                        // Add connectivity test information
                        string connectionStatus = "Connected";
                        string responseTime = "N/A";
                        
                        try
                        {
                            // Test API connection with timing
                            var stopwatch = new Stopwatch();
                            stopwatch.Start();
                            bool isConnected = await _apiClient.TestConnectionAsync();
                            stopwatch.Stop();
                            
                            connectionStatus = isConnected ? "Connected" : "Disconnected";
                            responseTime = $"{stopwatch.ElapsedMilliseconds}ms";
                        }
                        catch (Exception ex)
                        {
                            connectionStatus = $"Error: {ex.Message}";
                        }
                        
                        // Add connection info to the message
                        string fullMessage = $"Message from {senderName}:\n\n{messageText}\n\n" +
                                            $"Connection Status: {connectionStatus}\n" +
                                            $"Response Time: {responseTime}\n" +
                                            $"Node: {_nodeService.CurrentNode.Name} ({_nodeService.CurrentNode.IpAddress})";
                        
                        // Use the dispatcher to show message box on UI thread
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                    System.Windows.MessageBox.Show(
                                        fullMessage, 
                                        "New Message Received", 
                                        System.Windows.MessageBoxButton.OK, 
                                        System.Windows.MessageBoxImage.Information
                                    );
                            });
                        
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error displaying message: {ex.Message}");
                }
            }
            
            return false;
        }

        private string? FindCinema4DPath()
        {
            string path = @"C:\Program Files\Maxon Cinema 4D 2025";
            
            if (Directory.Exists(path))
            {
                string exePath = Path.Combine(path, "Cinema 4D.exe");
                if (File.Exists(exePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Found Cinema 4D at: {exePath}");
                    return exePath;
                }
            }

            System.Diagnostics.Debug.WriteLine("Cinema 4D 2025 not found at the specified location");
            return null;
        }

        private async Task ExecuteRenderThumbnailTask(BatchTask task)
        {
            try
            {
                // Mark task as running
                task.StartedAt = DateTime.UtcNow;
                await _apiClient.UpdateTaskStatusAsync(task.Id, BatchTaskStatus.Running);

                // Find Cinema 4D executable
                string? cinema4DPath = FindCinema4DPath();
                if (cinema4DPath == null)
                {
                    // Show message box to user
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                                System.Windows.MessageBox.Show(
                                    "Could not find Cinema 4D 2025. Please install it or try again.",
                                    "Cinema 4D Not Found",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Warning
                                );
                        });

                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        "Cinema 4D 2025 installation not found"
                    );
                    return;
                }

                // Parse parameters
                var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(task.Parameters ?? "{}");
                if (parameters == null || !parameters.ContainsKey("Directories"))
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        "No directories specified in task parameters"
                    );
                    return;
                }

                var directories = JsonSerializer.Deserialize<List<string>>(parameters["Directories"]);
                if (directories == null || directories.Count == 0)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        "No valid directories found in parameters"
                    );
                    return;
                }

                // Update UI to show task started
                UpdateTaskProgress(task, 0);

                // Process each directory
                for (int i = 0; i < directories.Count; i++)
                {
                    string directory = directories[i];
                    if (!Directory.Exists(directory))
                    {
                        continue;
                    }

                    // Find all .c4d files in the directory
                    var c4dFiles = Directory.GetFiles(directory, "*.c4d", SearchOption.AllDirectories);
                    
                    foreach (var file in c4dFiles)
                    {
                        // Start Cinema 4D with command line arguments for rendering
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = cinema4DPath,
                            Arguments = $"\"{file}\" -render preview",
                            UseShellExecute = true,
                            CreateNoWindow = false
                        };

                        using (var process = Process.Start(startInfo))
                        {
                            if (process != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"Started Cinema 4D process for file: {file}");
                                await process.WaitForExitAsync();
                            }
                        }
                    }

                    // Update progress
                    double progress = (i + 1.0) / directories.Count;
                    UpdateTaskProgress(task, progress);
                }

                // Mark task as completed
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id,
                    BatchTaskStatus.Completed,
                    $"Thumbnail rendering completed on {_nodeService.CurrentNode.Name} at {DateTime.Now}"
                );

                // Clear the task from UI after a short delay
                await Task.Delay(500);
                ClearTaskFromUI(task, true);
            }
            catch (Exception ex)
            {
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id,
                    BatchTaskStatus.Failed,
                    $"Error rendering thumbnails: {ex.Message}"
                );
            }
        }

        private void UpdateTaskProgress(BatchTask task, double progress)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                {
                    // For VolumeCompression tasks that support concurrent processing, use the current node
                    // For other tasks, use the task's assigned node ID
                    string targetNodeId = (task.Type == TaskType.VolumeCompression) 
                        ? _nodeService.CurrentNode.Id 
                        : task.AssignedNodeId;
                        
                    var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == targetNodeId);
                    if (node != null)
                    {
                        node.SetActiveTask(task, progress);
                        
                        // Force UI to update progress bar properties
                        node.OnPropertyChanged(nameof(node.TaskProgress));
                    }
                }
            });
        }

        private void UpdateTaskUI(BatchTask task, string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow.DataContext is ViewModels.MainViewModel viewModel)
                {
                    // For VolumeCompression tasks that support concurrent processing, use the current node
                    // For other tasks, use the task's assigned node ID
                    string targetNodeId = (task.Type == TaskType.VolumeCompression) 
                        ? _nodeService.CurrentNode.Id 
                        : task.AssignedNodeId;
                        
                    var node = viewModel.Nodes.FirstOrDefault(n => n.Id == targetNodeId);
                    if (node != null)
                    {
                        node.UpdateTaskName(message);
                    }
                }
            });
        }

        private void UpdateDebugOutput(string output)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (System.Windows.Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel viewModel)
                    {
                        viewModel.DebugOutput += output + Environment.NewLine;
                        
                        // Send to API for broadcasting to other clients
                        Task.Run(async () =>
                        {
                            try
                            {
                                string nodeName = _nodeService.CurrentNode.Name;
                                string nodeId = _nodeService.CurrentNode.Id;
                                await _apiClient.SendDebugMessageAsync(nodeName, output, nodeId);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error broadcasting debug message: {ex.Message}");
                            }
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating debug output: {ex.Message}");
            }
        }

        private async Task<bool> TryAcquireFolderLockAsync(string folderPath, string nodeId)
        {
            try
            {
                string normalizedFolderPath = PathUtils.NormalizePath(folderPath);
                string folderLockPath = PathUtils.GetFolderLockKey(normalizedFolderPath);
                bool lockAcquired = await _apiClient.TryAcquireFileLockAsync(folderLockPath, nodeId);
                if (lockAcquired)
                {
                    lock (_lockSetSync)
                    {
                        _heldLocks.Add(normalizedFolderPath);
                    }
                    UpdateDebugOutput($"Acquired lock for folder: {folderPath}");
                }
                else
                {
                    UpdateDebugOutput($"Could not acquire lock for folder: {folderPath} - already being processed by another node");
                    
                    // For additional diagnostics, try to find out who holds the lock
                    try
                    {
                        var locks = await _apiClient.GetActiveLocksAsync();
                        var matchingLock = locks.FirstOrDefault(l => l.FilePath.Equals(folderLockPath, StringComparison.OrdinalIgnoreCase));
                        
                        if (matchingLock != null)
                        {
                            UpdateDebugOutput($"Lock for {folderPath} is held by node: {matchingLock.LockingNodeId} since {matchingLock.AcquiredAt:u}");
                        }
                        else
                        {
                            UpdateDebugOutput($"WARNING: Lock acquisition failed but no matching lock found in database for {folderPath}");
                            
                            // Check for non-normalized versions of the lock
                            var anyMatchingLocks = locks
                                .Where(l => l.FilePath.StartsWith("folder_lock:") && 
                                       l.FilePath.Substring(12).Equals(folderPath, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            
                            if (anyMatchingLocks.Any())
                            {
                                foreach (var lock1 in anyMatchingLocks)
                                {
                                    UpdateDebugOutput($"Found possible non-normalized lock: {lock1.FilePath} held by: {lock1.LockingNodeId}");
                                }
                            }
                            else
                            {
                                // Last resort: try to view all locks to help diagnose the issue
                                var folderLocks = locks.Where(l => l.FilePath.StartsWith("folder_lock:")).ToList();
                                UpdateDebugOutput($"Currently active folder locks in database: {folderLocks.Count}");
                                
                                if (folderLocks.Count < 10) // Only show if there's a reasonable number
                                {
                                    foreach (var lock1 in folderLocks)
                                    {
                                        UpdateDebugOutput($"  → {lock1.FilePath} (held by: {lock1.LockingNodeId})");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateDebugOutput($"Error checking lock details: {ex.Message}");
                    }
                }
                return lockAcquired;
            }
            catch (Exception ex)
            {
                UpdateDebugOutput($"Error acquiring folder lock: {ex.Message}");
                return false;
            }
        }

        private async Task ReleaseFolderLockAsync(string folderPath, string nodeId)
        {
            try
            {
                string normalizedFolderPath = PathUtils.NormalizePath(folderPath);
                string folderLockPath = PathUtils.GetFolderLockKey(normalizedFolderPath);
                await _apiClient.ReleaseFileLockAsync(folderLockPath, nodeId);
                lock (_lockSetSync)
                {
                    _heldLocks.Remove(normalizedFolderPath);
                }
                UpdateDebugOutput($"Released lock for folder: {folderPath}");
            }
            catch (Exception ex)
            {
                UpdateDebugOutput($"Error releasing folder lock: {ex.Message}");
            }
        }

        private void ClearTaskFromUI(BatchTask task)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                {
                    // For VolumeCompression tasks that support concurrent processing, use the current node
                    // For other tasks, use the task's assigned node ID
                    string targetNodeId = (task.Type == TaskType.VolumeCompression) 
                        ? _nodeService.CurrentNode.Id 
                        : task.AssignedNodeId;
                        
                    var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == targetNodeId);
                    if (node != null)
                    {
                        node.ClearActiveTask();
                    }
                }
            });
        }

        private void ClearTaskFromUI(BatchTask task, bool wasCompleted = false)
        {
            Debug.WriteLine($"[TaskExecutionService] ClearTaskFromUI invoked for task {task.Id}, wasCompleted={wasCompleted}");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                {
                    // For VolumeCompression tasks that support concurrent processing, use the current node
                    // For other tasks, use the task's assigned node ID
                    string targetNodeId = (task.Type == TaskType.VolumeCompression) 
                        ? _nodeService.CurrentNode.Id 
                        : task.AssignedNodeId;
                        
                    var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == targetNodeId);
                    if (node != null)
                    {
                        // Ensure the task has the right status and CompletedAt time before passing to UI
                        if (wasCompleted && task.Status != BatchTaskStatus.Completed)
                        {
                            task.Status = BatchTaskStatus.Completed;
                            if (!task.CompletedAt.HasValue)
                            {
                                task.CompletedAt = DateTime.Now;
                            }
                        }
                        
                        node.ClearActiveTask(wasCompleted);
                        
                        if (wasCompleted)
                        {
                            Debug.WriteLine($"[TaskExecutionService] Task {task.Id} completed on node {node.Name}, sending Slack notification");
                            node.SetLastCompletedTask(task);
                            
                            // Send Slack notification if enabled
                            SendSlackNotificationAsync(node.Name, task);
                        }
                    }
                }
            });
        }

        private async Task ExecutePackageTask(BatchTask task)
        {
            try
            {
                // Mark task as running
                task.StartedAt = DateTime.UtcNow;
                await _apiClient.UpdateTaskStatusAsync(task.Id, BatchTaskStatus.Running);

                // Parse parameters
                var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(task.Parameters ?? "{}");
                if (parameters == null || !parameters.ContainsKey("Directories"))
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        "No directories specified in task parameters"
                    );
                    return;
                }

                var directories = JsonSerializer.Deserialize<List<string>>(parameters["Directories"]);
                if (directories == null || directories.Count == 0)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        "No valid directories found in parameters"
                    );
                    return;
                }

                // Update UI to show task started
                UpdateTaskProgress(task, 0);

                int totalFilesPaired = 0;
                int processedPairs = 0;
                List<string> fbxFilesWithoutE3d = new List<string>();
                List<string> e3dFilesWithoutFbx = new List<string>();
                bool continueWithUnpairedFiles = false;
                int skippedDirectories = 0;

                // Process each directory
                foreach (string directory in directories)
                {
                    if (!Directory.Exists(directory))
                    {
                        continue;
                    }

                    // Use the new helper method for acquiring the lock
                    bool lockAcquired = await TryAcquireFolderLockAsync(directory, _nodeService.CurrentNode.Id);
                    
                    if (!lockAcquired)
                    {
                        UpdateDebugOutput($"Skipping directory: {directory} - already being processed by another node");
                        skippedDirectories++;
                        continue;
                    }
                    
                    try
                    {
                        // Get all .e3d and .fbx files
                        var e3dFiles = Directory.GetFiles(directory, "*.e3d", SearchOption.TopDirectoryOnly);
                        var fbxFiles = Directory.GetFiles(directory, "*.fbx", SearchOption.TopDirectoryOnly);

                        // Group files by base name (without extension)
                        var e3dDict = e3dFiles.ToDictionary(
                            path => Path.GetFileNameWithoutExtension(path).ToLowerInvariant(),
                            path => path
                        );

                        var fbxDict = fbxFiles.ToDictionary(
                            path => Path.GetFileNameWithoutExtension(path).ToLowerInvariant(),
                            path => path
                        );

                        // Find unpaired files
                        foreach (var fbxFile in fbxFiles)
                        {
                            string baseName = Path.GetFileNameWithoutExtension(fbxFile).ToLowerInvariant();
                            if (!e3dDict.ContainsKey(baseName))
                            {
                                fbxFilesWithoutE3d.Add(fbxFile);
                            }
                            else
                            {
                                totalFilesPaired++;
                            }
                        }

                        foreach (var e3dFile in e3dFiles)
                        {
                            string baseName = Path.GetFileNameWithoutExtension(e3dFile).ToLowerInvariant();
                            if (!fbxDict.ContainsKey(baseName))
                            {
                                e3dFilesWithoutFbx.Add(e3dFile);
                            }
                        }

                        // Show alert for unpaired files if this is the first time we've found them
                        if ((fbxFilesWithoutE3d.Count > 0 || e3dFilesWithoutFbx.Count > 0) && !continueWithUnpairedFiles)
                        {
                            // Format the list of unpaired files for display
                            List<string> missingFilesList = new List<string>();
                            
                            foreach (var fbxFile in fbxFilesWithoutE3d)
                            {
                                missingFilesList.Add($"{Path.GetFileName(fbxFile)} (missing E3D counterpart)");
                            }
                            
                            foreach (var e3dFile in e3dFilesWithoutFbx)
                            {
                                missingFilesList.Add($"{Path.GetFileName(e3dFile)} (missing FBX counterpart)");
                            }
                            
                            // Limit the list for display purposes
                            string filesList = string.Join("\n", missingFilesList.Take(10));
                            if (missingFilesList.Count > 10)
                            {
                                filesList += $"\n... and {missingFilesList.Count - 10} more files";
                            }

                            // Show the message box to user with counts
                            bool shouldContinue = false;
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                string message = $"{fbxFilesWithoutE3d.Count + e3dFilesWithoutFbx.Count} files are missing their counterparts:\n\n{filesList}\n\nWould you like to continue processing paired files only?";
                                
                                var result = System.Windows.MessageBox.Show(
                                    message,
                                    "Missing File Counterparts",
                                    System.Windows.MessageBoxButton.YesNo,
                                    System.Windows.MessageBoxImage.Warning
                                );
                                
                                shouldContinue = (result == System.Windows.MessageBoxResult.Yes);
                            });

                            if (!shouldContinue)
                            {
                                // User chose not to continue
                                await _apiClient.UpdateTaskStatusAsync(
                                    task.Id,
                                    BatchTaskStatus.Cancelled,
                                    "Task cancelled due to missing file counterparts"
                                );
                                ClearTaskFromUI(task);
                                return;
                            }
                            
                            // Set flag to avoid showing the message box again
                            continueWithUnpairedFiles = true;
                        }

                        // Process matching pairs
                        foreach (var fbxFile in fbxFiles)
                        {
                            string baseName = Path.GetFileNameWithoutExtension(fbxFile).ToLowerInvariant();
                            
                            // Check if there's a matching .e3d file
                            if (e3dDict.TryGetValue(baseName, out string e3dFile))
                            {
                                // Original base name (preserving case)
                                string originalBaseName = Path.GetFileNameWithoutExtension(fbxFile);
                                
                                // Create target folder
                                string targetFolder = Path.Combine(directory, originalBaseName);
                                
                                try
                                {
                                    // Create the directory if it doesn't exist
                                    if (!Directory.Exists(targetFolder))
                                    {
                                        Directory.CreateDirectory(targetFolder);
                                    }

                                    // Move the files to the new folder
                                    string fbxDestination = Path.Combine(targetFolder, Path.GetFileName(fbxFile));
                                    string e3dDestination = Path.Combine(targetFolder, Path.GetFileName(e3dFile));

                                    File.Move(fbxFile, fbxDestination, false);
                                    File.Move(e3dFile, e3dDestination, false);

                                    processedPairs++;
                                    
                                    // Update progress
                                    double progress = totalFilesPaired > 0 ? (double)processedPairs / totalFilesPaired : 1.0;
                                    UpdateTaskProgress(task, progress);
                                    
                                    System.Diagnostics.Debug.WriteLine($"Packaged: {originalBaseName}");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error packaging {originalBaseName}: {ex.Message}");
                                }
                            }
                        }
                    }
                    finally
                    {
                        // Use the new helper method for releasing the lock
                        await ReleaseFolderLockAsync(directory, _nodeService.CurrentNode.Id);
                    }
                }

                // Mark task as completed
                string resultMessage;
                
                if (fbxFilesWithoutE3d.Count > 0 || e3dFilesWithoutFbx.Count > 0)
                {
                    resultMessage = $"Packaged {processedPairs} of {totalFilesPaired} file pairs. Skipped {fbxFilesWithoutE3d.Count} FBX files and {e3dFilesWithoutFbx.Count} E3D files without counterparts.";
                }
                else
                {
                    resultMessage = totalFilesPaired > 0 
                        ? $"Packaged {processedPairs} of {totalFilesPaired} file pairs into folders"
                        : "No matching .e3d and .fbx pairs found to package";
                }
                
                if (skippedDirectories > 0)
                {
                    resultMessage += $" ({skippedDirectories} directories skipped - already being processed by other nodes)";
                }
                
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id,
                    BatchTaskStatus.Completed,
                    resultMessage
                );

                // Clear the task from UI after a short delay
                await Task.Delay(500);
                ClearTaskFromUI(task, true);
            }
            catch (Exception ex)
            {
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id,
                    BatchTaskStatus.Failed,
                    $"Error packaging files: {ex.Message}"
                );
            }
        }

        private async Task ExecuteVolumeCompressionTask(BatchTask task)
        {
            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] *** STARTING VOLUME COMPRESSION TASK {task.Id} ***");
            
            // Create a new cancellation token source for this task
            if (_cancellationTokenSource != null && _cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
            }
            
            CancellationToken cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;
            string nodeId = _nodeService.CurrentNode.Id;
            List<string> acquiredLocks = new List<string>();
            
            try
            {
                // Mark task as running first
                task.StartedAt = DateTime.UtcNow;
                await _apiClient.UpdateTaskStatusAsync(task.Id, BatchTaskStatus.Running);
                
                // Parse parameters
                var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(task.Parameters ?? "{}");
                if (parameters == null || !parameters.ContainsKey("Directories"))
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        "No directories specified in task parameters"
                    );
                    return;
                }
                
                var rootDirectories = JsonSerializer.Deserialize<List<string>>(parameters["Directories"]);
                if (rootDirectories == null || rootDirectories.Count == 0)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        "No valid directories found in parameters"
                    );
                    return;
                }

                // Check for Volume Compressor executable
                string volumeCompressorPath = Path.Combine(AppContext.BaseDirectory, "volume_compressor.exe");
                string volumeCompressorBatchPath = Path.Combine(AppContext.BaseDirectory, "volume_compressor.bat");
                
                if (!File.Exists(volumeCompressorPath) && !File.Exists(volumeCompressorBatchPath))
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        $"Volume Compressor executable not found at: {volumeCompressorPath} or {volumeCompressorBatchPath}"
                    );
                    return;
                }
                
                // Use the batch file if the executable doesn't exist
                string executablePath = File.Exists(volumeCompressorPath) ? volumeCompressorPath : volumeCompressorBatchPath;

                // Check for override output directory
                string? overrideOutputDirectory = null;
                bool useOverrideOutput = false;
                
                if (parameters.TryGetValue("OverrideOutputDirectory", out string overrideValue) && 
                    bool.TryParse(overrideValue, out useOverrideOutput) && 
                    useOverrideOutput)
                {
                    if (parameters.TryGetValue("OutputDirectory", out string outputDirValue) && 
                        !string.IsNullOrEmpty(outputDirValue))
                    {
                        overrideOutputDirectory = outputDirValue;
                        
                        // Create the output directory if it doesn't exist
                        if (!Directory.Exists(overrideOutputDirectory))
                        {
                            Directory.CreateDirectory(overrideOutputDirectory);
                        }
                    }
                }

                // Check fixed dimension parameter
                bool useFixedDimension = false;
                if (parameters.TryGetValue("UseFixedDimension", out string dimensionValue) && 
                    bool.TryParse(dimensionValue, out useFixedDimension) && 
                    useFixedDimension)
                {
                    // Flag is set in parameters
                }
                
                // Check SD dimension parameter
                bool useSdDimension = false;
                if (parameters.TryGetValue("UseSdDimension", out string sdDimensionValue) && 
                    bool.TryParse(sdDimensionValue, out useSdDimension) && 
                    useSdDimension)
                {
                    // Flag is set in parameters
                }
                
                // Get compression level parameter
                string compressionLevel = "No Compression";
                if (parameters.TryGetValue("CompressionLevel", out string compressionValue) && 
                    !string.IsNullOrWhiteSpace(compressionValue))
                {
                    compressionLevel = compressionValue;
                }
                
                // Check create output folder parameter
                bool createOutputFolder = true; // Default to true for backward compatibility
                if (parameters.TryGetValue("CreateOutputFolder", out string createFolderValue) && 
                    bool.TryParse(createFolderValue, out bool parseResult))
                {
                    createOutputFolder = parseResult;
                }

                // Update UI to show task started
                UpdateTaskProgress(task, 0);
                UpdateTaskUI(task, "Processing VDB Files");
                
                // Check if the task was cancelled before starting
                if (cancellationToken.IsCancellationRequested)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Cancelled,
                        "Task was cancelled before starting file scan"
                    );
                    ClearTaskFromUI(task);
                    return;
                }

                // Load existing folder progress records created during pre-scan
                var existingFolderProgress = await _apiClient.GetTaskFoldersAsync(task.Id);
                var folderProgressLookup = new Dictionary<string, TaskFolderProgress>();
                
                // Build lookup dictionary from existing folder progress records
                foreach (var folderProgress in existingFolderProgress)
                {
                    folderProgressLookup[folderProgress.FolderPath] = folderProgress;
                }
                
                // If no pre-scanned folders exist, create them now (fallback)
                if (!folderProgressLookup.Any())
                {
                    UpdateDebugOutput("No pre-scanned folder progress found, scanning directories now...");
                    
                    foreach (string rootPath in rootDirectories)
                    {
                        if (!Directory.Exists(rootPath))
                        {
                            UpdateDebugOutput($"Directory not found: {rootPath}");
                            continue;
                        }
                        
                        // Check if this directory contains VDB files directly
                        var vdbFiles = Directory.GetFiles(rootPath, "*.vdb", SearchOption.TopDirectoryOnly);
                        if (vdbFiles.Length > 0)
                        {
                            string folderPath = rootPath;
                            if (!folderProgressLookup.ContainsKey(folderPath))
                            {
                                var folderProgress = new TaskFolderProgress
                                {
                                    TaskId = task.Id,
                                    FolderPath = folderPath,
                                    FolderName = Path.GetFileName(folderPath),
                                    Status = TaskFolderStatus.Pending,
                                    AssignedNodeId = null,
                                    AssignedNodeName = null,
                                    Progress = 0.0
                                };

                                try
                                {
                                    var createdProgress = await _apiClient.CreateTaskFolderAsync(task.Id, folderProgress);
                                    if (createdProgress != null)
                                    {
                                        folderProgressLookup[folderPath] = createdProgress;
                                        UpdateDebugOutput($"Created fallback folder progress record for: {folderProgress.FolderName}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    UpdateDebugOutput($"Error creating fallback folder progress record for {folderProgress.FolderName}: {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            // Check subdirectories for VDB files
                            try
                            {
                                var subdirectories = Directory.GetDirectories(rootPath);
                                foreach (var subdir in subdirectories)
                                {
                                    var subdirVdbFiles = Directory.GetFiles(subdir, "*.vdb", SearchOption.TopDirectoryOnly);
                                    if (subdirVdbFiles.Length > 0)
                                    {
                                        string folderPath = subdir;
                                        if (!folderProgressLookup.ContainsKey(folderPath))
                                        {
                                            var folderProgress = new TaskFolderProgress
                                            {
                                                TaskId = task.Id,
                                                FolderPath = folderPath,
                                                FolderName = Path.GetFileName(folderPath),
                                                Status = TaskFolderStatus.Pending,
                                                AssignedNodeId = null,
                                                AssignedNodeName = null,
                                                Progress = 0.0
                                            };

                                            try
                                            {
                                                var createdProgress = await _apiClient.CreateTaskFolderAsync(task.Id, folderProgress);
                                                if (createdProgress != null)
                                                {
                                                    folderProgressLookup[folderPath] = createdProgress;
                                                    UpdateDebugOutput($"Created fallback folder progress record for: {folderProgress.FolderName}");
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                UpdateDebugOutput($"Error creating fallback folder progress record for {folderProgress.FolderName}: {ex.Message}");
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                UpdateDebugOutput($"Error scanning subdirectories in {rootPath}: {ex.Message}");
                            }
                        }
                    }
                }
                
                // If no folders with VDBs were found, abort the task
                if (!folderProgressLookup.Any())
                {
                    UpdateDebugOutput("No directories containing VDB files found to process");
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Completed,
                        "No directories containing VDB files found to process"
                    );
                    ClearTaskFromUI(task, true);
                    return;
                }
                
                UpdateDebugOutput($"Found {folderProgressLookup.Count} directories containing VDB files");

                // Calculate total files for progress tracking
                int totalFiles = 0;
                foreach (var folderEntry in folderProgressLookup)
                {
                    string folderPath = folderEntry.Key;
                    if (Directory.Exists(folderPath))
                    {
                        var vdbFiles = Directory.GetFiles(folderPath, "*.vdb", SearchOption.TopDirectoryOnly);
                        totalFiles += vdbFiles.Length;
                    }
                }
                
                UpdateDebugOutput($"Total files to process: {totalFiles} across {folderProgressLookup.Count} folders");
                UpdateDebugOutput($"Node {_nodeService.CurrentNode.Name} starting concurrent folder processing...");
                UpdateDebugOutput($"=== CONCURRENT PROCESSING MODE: Each node will claim available folders independently ===");

                // **NEW CONCURRENT PROCESSING LOGIC**
                // Instead of processing folders sequentially, continuously look for available folders
                int processedFolders = 0;
                int processedFiles = 0;
                int maxRetries = 3;
                int retryCount = 0;
                
                while (processedFolders < folderProgressLookup.Count && retryCount < maxRetries)
                {
                    // Check for cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        UpdateDebugOutput("Volume compression task was cancelled by user");
                        break;
                    }
                    
                    UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Looking for available folders to process... (attempt {retryCount + 1}/{maxRetries})");
                    
                    // Get current folder statuses from the database to see what's available
                    var currentTaskFolders = await _apiClient.GetTaskFoldersAsync(task.Id);
                    var availableFolders = currentTaskFolders
                        .Where(f => f.Status == TaskFolderStatus.Pending)
                        .OrderBy(f => f.FolderName) // Process in consistent order for better load balancing
                        .ToList();
                    
                    UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Found {availableFolders.Count} pending folders, {currentTaskFolders.Where(f => f.Status == TaskFolderStatus.InProgress).Count()} in progress, {currentTaskFolders.Where(f => f.Status == TaskFolderStatus.Completed).Count()} completed");
                    
                    if (!availableFolders.Any())
                    {
                        // No more pending folders, check if any are still in progress
                        var inProgressFolders = currentTaskFolders
                            .Where(f => f.Status == TaskFolderStatus.InProgress)
                            .ToList();
                        
                        if (inProgressFolders.Any())
                        {
                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] No pending folders available. {inProgressFolders.Count} folders still in progress by other nodes. Waiting...");
                            foreach (var inProgressFolder in inProgressFolders)
                            {
                                UpdateDebugOutput($"  → {inProgressFolder.FolderName} being processed by {inProgressFolder.AssignedNodeName ?? inProgressFolder.AssignedNodeId ?? "unknown"}");
                            }
                            await Task.Delay(2000, cancellationToken); // Wait 2 seconds before checking again
                            retryCount++;
                            continue;
                        }
                        else
                        {
                            // All folders are completed or failed
                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] All folders have been processed by all nodes");
                            break;
                        }
                    }
                    
                    // Reset retry count since we found available folders
                    retryCount = 0;
                    
                    // Try to claim and process the first available folder
                    bool foundWork = false;
                    foreach (var availableFolder in availableFolders)
                    {
                        // Check for cancellation
                        if (cancellationToken.IsCancellationRequested)
                        {
                            UpdateDebugOutput("Volume compression task was cancelled by user");
                            break;
                        }
                        
                        string folderPath = availableFolder.FolderPath;
                        
                        // Try to acquire a lock for this specific folder
                        bool lockAcquired = await TryAcquireFolderLockAsync(folderPath, nodeId);
                        if (!lockAcquired)
                        {
                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Could not acquire lock for folder: {Path.GetFileName(folderPath)} - trying next folder");
                            continue; // Try next folder
                        }
                        
                        // Successfully acquired lock - process this folder
                        foundWork = true;
                        acquiredLocks.Add(folderPath);
                        
                        try
                        {
                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Processing folder: {Path.GetFileName(folderPath)} (lock acquired)");
                            
                            // Mark folder as in progress
                            try
                            {
                                await _apiClient.UpdateTaskFolderStatusAsync(
                                    availableFolder.Id, 
                                    TaskFolderStatus.InProgress, 
                                    nodeId, 
                                    _nodeService.CurrentNode.Name,
                                    0.0);
                                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Started processing folder: {availableFolder.FolderName}");
                            }
                            catch (Exception ex)
                            {
                                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error updating folder progress to InProgress: {ex.Message}");
                            }
                            
                            // Get VDB files in this folder
                            var vdbFiles = Directory.GetFiles(folderPath, "*.vdb", SearchOption.TopDirectoryOnly);
                            if (vdbFiles.Length == 0)
                            {
                                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] No VDB files found in folder: {availableFolder.FolderName}");
                                
                                // Mark folder as completed (no files to process)
                                await _apiClient.UpdateTaskFolderStatusAsync(
                                    availableFolder.Id, 
                                    TaskFolderStatus.Completed, 
                                    nodeId, 
                                    _nodeService.CurrentNode.Name,
                                    1.0);
                                processedFolders++;
                                continue;
                            }
                            
                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Found {vdbFiles.Length} VDB files in folder: {availableFolder.FolderName}");
                            
                            // Process each VDB file in this folder
                            for (int i = 0; i < vdbFiles.Length; i++)
                            {
                                // Check for cancellation before processing each file
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    UpdateDebugOutput("Volume compression task was cancelled by user");
                                    break;
                                }
                                
                                string inputFile = vdbFiles[i];
                                string inputFileName = Path.GetFileName(inputFile);
                                string inputBaseName = Path.GetFileNameWithoutExtension(inputFile);
                                string outputFileName = $"{inputBaseName}_compressed.vdb";
                                
                                string outputDir;
                                if (useOverrideOutput && !string.IsNullOrEmpty(overrideOutputDirectory))
                                {
                                    outputDir = overrideOutputDirectory;
                                }
                                else 
                                {
                                    // Default: create a "Compressed" subfolder in the same directory as input files
                                    outputDir = Path.Combine(folderPath, "Compressed");
                                }
                                
                                // Create the output directory if it doesn't exist
                                if (!Directory.Exists(outputDir))
                                {
                                    Directory.CreateDirectory(outputDir);
                                }
                                
                                string outputPath = Path.Combine(outputDir, outputFileName);
                                
                                // Set compression encoding based on the compression level
                                string encodingArg = "";
                                if (compressionLevel == "No Compression")
                                {
                                    encodingArg = "--encoding none";
                                }
                                else if (compressionLevel == "Medium Compression")
                                {
                                    encodingArg = "--encoding quant8";
                                }
                                else if (compressionLevel == "High Compression")
                                {
                                    encodingArg = "--encoding quant4";
                                }
                                
                                // Build the full command with correct argument order for a single file:
                                // volume_compressor.exe [OPTIONS] --output <o> <INPUT>
                                var outputArg = $"--output \"{outputPath}\"";
                                var arguments = $"{encodingArg} --overwrite {outputArg} \"{inputFile}\"";
                                
                                // Start the volume compressor process with output redirection for this file
                                using var process = new Process
                                {
                                    StartInfo = new ProcessStartInfo
                                    {
                                        FileName = executablePath,
                                        Arguments = arguments,
                                        UseShellExecute = false,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true,
                                        CreateNoWindow = true
                                    }
                                };
                                
                                // Log the command
                                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Running command: {executablePath} {arguments}");
                                
                                process.OutputDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] VC: {e.Data}");
                                    }
                                };
                                
                                process.ErrorDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] VC Error: {e.Data}");
                                    }
                                };
                                
                                // Start the process and begin reading output
                                _runningProcesses[_nodeService.CurrentNode.Id] = process;
                                process.Start();
                                process.BeginOutputReadLine();
                                process.BeginErrorReadLine();
                                
                                // Wait for the process to complete
                                await Task.Run(() => process.WaitForExit(), cancellationToken);
                                
                                if (process.ExitCode != 0)
                                {
                                    UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Volume Compressor exited with code {process.ExitCode} for file {inputFileName}");
                                }
                                else
                                {
                                    UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Successfully processed file {inputFileName}");
                                }
                                
                                // Update progress for this folder
                                double currentFolderProgress = (double)(i + 1) / vdbFiles.Length;
                                try
                                {
                                    await _apiClient.UpdateTaskFolderStatusAsync(
                                        availableFolder.Id, 
                                        TaskFolderStatus.InProgress, 
                                        nodeId, 
                                        _nodeService.CurrentNode.Name,
                                        currentFolderProgress);
                                }
                                catch (Exception ex)
                                {
                                    UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error updating folder progress: {ex.Message}");
                                }
                                
                                // Update overall task progress
                                processedFiles++;
                                double overallProgress = totalFiles > 0 ? (double)processedFiles / totalFiles : 0;
                                UpdateTaskProgress(task, overallProgress);
                                
                                // Clean up process
                                _runningProcesses.TryRemove(_nodeService.CurrentNode.Id, out _);
                            }
                            
                            // Mark folder as completed if all files were processed successfully
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                try
                                {
                                    await _apiClient.UpdateTaskFolderStatusAsync(
                                        availableFolder.Id, 
                                        TaskFolderStatus.Completed, 
                                        nodeId, 
                                        _nodeService.CurrentNode.Name,
                                        1.0);
                                    UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Completed processing folder: {availableFolder.FolderName}");
                                    processedFolders++;
                                }
                                catch (Exception ex)
                                {
                                    UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error updating folder progress to Completed: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error processing folder {folderPath}: {ex.Message}");
                            
                            // Mark folder as failed
                            try
                            {
                                await _apiClient.UpdateTaskFolderStatusAsync(
                                    availableFolder.Id, 
                                    TaskFolderStatus.Failed, 
                                    nodeId, 
                                    _nodeService.CurrentNode.Name,
                                    0.0);
                            }
                            catch (Exception updateEx)
                            {
                                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error updating folder progress to Failed: {updateEx.Message}");
                            }
                        }
                        finally
                        {
                            // Release the lock for this folder
                            await ReleaseFolderLockAsync(folderPath, nodeId);
                            acquiredLocks.Remove(folderPath);
                        }
                        
                        // Break out of the folder loop since we found and processed work
                        break;
                    }
                    
                    if (!foundWork)
                    {
                        // No folders could be locked, wait a bit before trying again
                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] No folders could be locked, waiting before retry...");
                        await Task.Delay(1000, cancellationToken);
                        retryCount++;
                    }
                }
                
                // Check if the task was cancelled
                if (cancellationToken.IsCancellationRequested)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Cancelled,
                        $"Task was cancelled after processing {processedFiles} files in {processedFolders} folders by node {_nodeService.CurrentNode.Name}"
                    );
                    ClearTaskFromUI(task);
                    return;
                }
                
                // Check final status of all folders to determine if task is complete
                var finalFolderProgress = await _apiClient.GetTaskFoldersAsync(task.Id);
                var completedFolders = finalFolderProgress.Where(f => f.Status == TaskFolderStatus.Completed).Count();
                var failedFolders = finalFolderProgress.Where(f => f.Status == TaskFolderStatus.Failed).Count();
                var totalFolders = finalFolderProgress.Count();
                
                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Final status: {completedFolders} completed, {failedFolders} failed, {totalFolders} total folders");
                
                // Only mark task as completed if this is the last node working on it
                // (We'll let the task completion be handled by a separate process or the last node)
                string resultMessage = $"Node {_nodeService.CurrentNode.Name} finished processing. Processed {processedFiles} files in {processedFolders} folders";
                
                // Don't change the overall task status here - let the system determine when all nodes are done
                UpdateDebugOutput(resultMessage);
                
                // Clear the task from UI after a short delay
                await Task.Delay(500);
                ClearTaskFromUI(task, true);
            }
            catch (Exception ex)
            {
                _runningProcesses.TryRemove(_nodeService.CurrentNode.Id, out _); // Cleanup on error
                Debug.WriteLine($"[VolumeCompressionTask] Task {task.Id}: error occurred: {ex.Message}");
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id,
                    BatchTaskStatus.Failed,
                    $"Error processing volume compression on node {_nodeService.CurrentNode.Name}: {ex.Message}"
                );
                Debug.WriteLine($"[VolumeCompressionTask] Task {task.Id}: clearing UI without Slack");
                ClearTaskFromUI(task, false);
            }
            finally
            {
                // Clean up all acquired locks
                foreach (var lockPath in acquiredLocks)
                {
                    try
                    {
                        await ReleaseFolderLockAsync(lockPath, nodeId);
                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Released lock for folder: {lockPath} on task completion");
                    }
                    catch (Exception ex)
                    {
                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error releasing lock for {lockPath}: {ex.Message}");
                    }
                }
                
                // Also clean up any orphaned locks for this node
                try
                {
                    var locks = await _apiClient.GetActiveLocksAsync();
                    var nodeLocks = locks.Where(l => l.LockingNodeId == nodeId && l.FilePath.StartsWith("folder_lock:")).ToList();
                    
                    if (nodeLocks.Any())
                    {
                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Found {nodeLocks.Count} additional folder locks to clean up");
                        
                        foreach (var lockItem in nodeLocks)
                        {
                            try
                            {
                                string folderPath = lockItem.FilePath.Substring(12); // Remove "folder_lock:" prefix
                                string normalizedFolderPath = PathUtils.NormalizePath(folderPath);
                                string folderLockPath = PathUtils.GetFolderLockKey(normalizedFolderPath);
                                await _apiClient.ReleaseFileLockAsync(folderLockPath, nodeId);
                                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Released orphaned lock for folder: {folderPath}");
                            }
                            catch (Exception ex)
                            {
                                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error releasing orphaned lock: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error cleaning up orphaned locks: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Pre-scans directories for volume compression tasks and creates folder progress records
        /// This should be called when the task is created, before execution starts
        /// </summary>
        public async Task PreScanVolumeCompressionTaskAsync(BatchTask task)
        {
            try
            {
                if (task.Type != TaskType.VolumeCompression)
                {
                    return; // Only process volume compression tasks
                }

                // Parse parameters
                var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(task.Parameters ?? "{}");
                if (parameters == null || !parameters.ContainsKey("Directories"))
                {
                    return;
                }

                var rootDirectories = JsonSerializer.Deserialize<List<string>>(parameters["Directories"]);
                if (rootDirectories == null || rootDirectories.Count == 0)
                {
                    return;
                }

                // Clear any existing folder progress records for this task
                await _apiClient.DeleteTaskFoldersAsync(task.Id);

                // Scan directories and create folder progress records
                var foldersWithVdbs = new List<string>();

                foreach (string rootPath in rootDirectories)
                {
                    if (Directory.Exists(rootPath))
                    {
                        // Check if this directory contains VDB files directly
                        var vdbFiles = Directory.GetFiles(rootPath, "*.vdb", SearchOption.TopDirectoryOnly);
                        if (vdbFiles.Length > 0)
                        {
                            foldersWithVdbs.Add(rootPath);
                        }
                        else
                        {
                            // Check subdirectories for VDB files
                            try
                            {
                                var subdirectories = Directory.GetDirectories(rootPath);
                                foreach (var subdir in subdirectories)
                                {
                                    var subdirVdbFiles = Directory.GetFiles(subdir, "*.vdb", SearchOption.TopDirectoryOnly);
                                    if (subdirVdbFiles.Length > 0)
                                    {
                                        foldersWithVdbs.Add(subdir);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                UpdateDebugOutput($"Error scanning subdirectories in {rootPath}: {ex.Message}");
                            }
                        }
                    }
                }

                // Create folder progress records for all directories containing VDB files
                foreach (string folderPath in foldersWithVdbs)
                {
                    var folderProgress = new TaskFolderProgress
                    {
                        TaskId = task.Id,
                        FolderPath = folderPath,
                        FolderName = Path.GetFileName(folderPath),
                        Status = TaskFolderStatus.Pending,
                        AssignedNodeId = null,
                        AssignedNodeName = null,
                        Progress = 0.0
                    };

                    try
                    {
                        var createdProgress = await _apiClient.CreateTaskFolderAsync(task.Id, folderProgress);
                        if (createdProgress != null)
                        {
                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Pre-created folder progress record for: {folderProgress.FolderName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error creating folder progress record for {folderProgress.FolderName}: {ex.Message}");
                    }
                }

                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Pre-scan completed: Found {foldersWithVdbs.Count} directories containing VDB files");
            }
            catch (Exception ex)
            {
                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error during pre-scan: {ex.Message}");
            }
        }

        private async Task ExecuteRealityCaptureTask(BatchTask task)
        {
            // Create a new cancellation token source for this task
            if (_cancellationTokenSource != null && _cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
            }
            
            CancellationToken cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;
            
            try
            {
                // Mark task as running first
                task.StartedAt = DateTime.UtcNow;
                await _apiClient.UpdateTaskStatusAsync(task.Id, BatchTaskStatus.Running);
                
                // Parse parameters
                var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(task.Parameters ?? "{}");
                if (parameters == null || !parameters.ContainsKey("Directories"))
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        "No directories specified in task parameters"
                    );
                    return;
                }

                var rootDirectories = JsonSerializer.Deserialize<List<string>>(parameters["Directories"]);
                if (rootDirectories == null || rootDirectories.Count == 0)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        "No valid directories found in parameters"
                    );
                    return;
                }

                // Path to Reality Capture executable
                string realityCaptureExe = "C:\\Program Files\\Capturing Reality\\RealityCapture\\RealityCapture.exe";
                
                // Check if Reality Capture executable exists
                if (!File.Exists(realityCaptureExe))
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Failed,
                        $"Reality Capture executable not found at: {realityCaptureExe}"
                    );
                    return;
                }

                // Path to save parameter file for this run
                string tempParamFilePath = Path.Combine(Path.GetTempPath(), $"rc_params_{Guid.NewGuid()}.xml");
                
                int processedFolders = 0;
                int successfulFolders = 0;
                int failedFolders = 0;
                int skippedFolders = 0;
                
                // Process each folder
                foreach (var rootDir in rootDirectories)
                {
                    if (!Directory.Exists(rootDir))
                    {
                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Directory not found: {rootDir}");
                        continue;
                    }

                    try
                    {
                        // Use the new helper method for acquiring the lock
                        bool lockAcquired = await TryAcquireFolderLockAsync(rootDir, _nodeService.CurrentNode.Id);
                        
                        if (!lockAcquired)
                        {
                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Skipping folder: {rootDir} - already being processed by another node");
                            skippedFolders++;
                            continue;
                        }
                        
                        try
                        {
                            // Get the folder name to use for project name
                            string folderName = new DirectoryInfo(rootDir).Name;
                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Processing folder: {folderName} (lock acquired)");

                            // Define paths for the project
                            string jpegsFolder = Path.Combine(rootDir, "Jpegs");
                            string tiffsFolder = Path.Combine(rootDir, "Tiffs");
                            string pngsFolder = Path.Combine(rootDir, "PNGs");

                            // Create the working directory structure if it doesn't exist
                            string workingFolder = Path.Combine(rootDir, "00_Working", "01_Reality_Capture");
                            if (!Directory.Exists(workingFolder))
                            {
                                Directory.CreateDirectory(workingFolder);
                            }

                            string projectFile = Path.Combine(workingFolder, $"{folderName}.rcproj");

                            // Build command line arguments for Reality Capture
                            string arguments = "-clearCache";
                            
                            // Add folders containing the images
                            // First check PNG folder as primary
                            if (Directory.Exists(pngsFolder) && Directory.EnumerateFiles(pngsFolder, "*.png").Any())
                            {
                                arguments += $" -addFolder \"{pngsFolder}\"";
                            }
                            // Then JPEGs as fallback
                            else if (Directory.Exists(jpegsFolder) && Directory.EnumerateFiles(jpegsFolder, "*.jpg").Any())
                            {
                                arguments += $" -addFolder \"{jpegsFolder}\"";
                            }
                            else
                            {
                                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] No PNG or JPEG images found in {folderName}");
                                failedFolders++;
                                continue;
                            }
                            
                            // Main algorithm options
                            arguments += " -selectAllImages"
                             + $" -save \"{projectFile}\""
                             + " -align"
                             + $" -save \"{projectFile}\""
                             + " -selectMaximalComponent"
                             + " -setReconstructionRegionAuto"
                             + " -calculateHighModel"
                             + " -selectMarginalTriangles"
                             + " -removeSelectedTriangles"
                             + " -selectLargestModelComponent"
                             + " -invertTrianglesSelection"
                             + " -removeSelectedTriangles"
                             + " -renameSelectedModel HP"
                             + " -selectModel HP"
                             + $" -save \"{projectFile}\""
                             + " -quit";

                            // Start the Reality Capture process
                            using var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = realityCaptureExe,
                                    Arguments = arguments,
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    CreateNoWindow = true
                                }
                            };

                                // Log the command
                                UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Running command: {realityCaptureExe} {arguments}");

                                process.OutputDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] RC: {e.Data}");
                                    }
                                };

                                process.ErrorDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] RC Error: {e.Data}");
                                    }
                                };

                                _runningProcesses[_nodeService.CurrentNode.Id] = process;
                                process.Start();
                                process.BeginOutputReadLine();
                                process.BeginErrorReadLine();

                                    // Wait for the process to complete with timeout
                                    bool completed = await Task.Run(() => process.WaitForExit(60 * 60 * 1000), cancellationToken); // 1 hour timeout
                                    
                                    if (!completed)
                                    {
                                        // Process timed out, kill it
                                        try
                                        {
                                            process.Kill(true);
                                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Reality Capture process for {folderName} timed out after 1 hour and was terminated.");
                                            failedFolders++;
                                        }
                                        catch (Exception ex)
                                        {
                                            UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Failed to terminate Reality Capture process: {ex.Message}");
                                        }
                                    }
                                    else if (process.ExitCode != 0)
                                    {
                                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Reality Capture process exited with code {process.ExitCode}");
                                        failedFolders++;
                                    }
                                    else
                                    {
                                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Successfully processed {folderName}");
                                        successfulFolders++;
                            }
                        }
                        finally
                        {
                            // Use the new helper method for releasing the lock
                            await ReleaseFolderLockAsync(rootDir, _nodeService.CurrentNode.Id);
                            _runningProcesses.TryRemove(_nodeService.CurrentNode.Id, out _); // Cleanup after exit
                        }

                        // Check for cancellation
                        if (cancellationToken.IsCancellationRequested)
                        {
                            UpdateDebugOutput("Task was cancelled by user");
                            break;
                        }

                        // Update progress
                        processedFolders++;
                        double progress = (double)processedFolders / rootDirectories.Count;
                        UpdateTaskProgress(task, progress);
                    }
                    catch (Exception ex)
                    {
                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error processing folder {rootDir}: {ex.Message}");
                        failedFolders++;
                    }
                }

                // Clean up any temp files
                if (File.Exists(tempParamFilePath))
                {
                    try
                    {
                        File.Delete(tempParamFilePath);
                    }
                    catch (Exception ex)
                    {
                        UpdateDebugOutput($"[{_nodeService.CurrentNode.Name}] Error cleaning up temp file: {ex.Message}");
                    }
                }

                // Check if the task was cancelled
                if (cancellationToken.IsCancellationRequested)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Cancelled,
                        $"Task was cancelled after processing {processedFolders} of {rootDirectories.Count} folders"
                    );
                    ClearTaskFromUI(task);
                    return;
                }

                // Mark task as completed
                string resultMessage = $"Reality Capture processing completed. Successfully processed {successfulFolders} of {processedFolders} folders";
                if (failedFolders > 0)
                {
                    resultMessage += $" ({failedFolders} failed)";
                }
                if (skippedFolders > 0)
                {
                    resultMessage += $", {skippedFolders} skipped (already being processed by other nodes)";
                }
                
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id,
                    BatchTaskStatus.Completed,
                    resultMessage
                );
                
                // Clear the task from UI after a short delay
                await Task.Delay(500);
                ClearTaskFromUI(task, true);
            }
            catch (Exception ex)
            {
                _runningProcesses.TryRemove(_nodeService.CurrentNode.Id, out _); // Cleanup on error
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id,
                    BatchTaskStatus.Failed,
                    $"Error during Reality Capture processing: {ex.Message}"
                );
            }
        }
    }
} 