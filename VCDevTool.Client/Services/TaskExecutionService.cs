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

namespace VCDevTool.Client.Services
{
    public class TaskExecutionService : IDisposable
    {
        private ApiClient _apiClient;
        private readonly NodeService _nodeService;
        private readonly SlackNotificationService? _slackService;
        private readonly HashSet<int> _processedTaskIds;
        private System.Threading.Timer? _taskPollingTimer;
        private const int PollingIntervalMs = 5000;
        private bool _isExecutingTask;
        private bool _isDisposed;
        private CancellationTokenSource? _cancellationTokenSource;

        public TaskExecutionService(ApiClient apiClient, NodeService nodeService)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _nodeService = nodeService ?? throw new ArgumentNullException(nameof(nodeService));
            _processedTaskIds = new HashSet<int>();
            _slackService = ((App)System.Windows.Application.Current).SlackService;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void UpdateApiClient(ApiClient apiClient)
        {
            if (apiClient == null)
                throw new ArgumentNullException(nameof(apiClient));
            
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TaskExecutionService));
                
            _apiClient = apiClient;
            
            RestartTaskPolling();
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
                t.AssignedNodeId == _nodeService.CurrentNode.Id &&
                !_processedTaskIds.Contains(t.Id)).ToList();
            
            var pendingTasks = tasksForThisNode.Where(t => t.Status == BatchTaskStatus.Pending).ToList();
            
            foreach (var task in pendingTasks)
            {
                _processedTaskIds.Add(task.Id);
                await ExecuteTaskAsync(task);
            }
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
            try
            {
                // Cancel the current operation
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = new CancellationTokenSource();
                }
                
                // Get tasks for this node
                var tasks = await _apiClient.GetTasksAsync();
                var runningTask = tasks.FirstOrDefault(t => 
                    t.AssignedNodeId == nodeId && 
                    (t.Status == BatchTaskStatus.Running || t.Status == BatchTaskStatus.Pending));
                
                if (runningTask != null)
                {
                    // Mark the task as cancelled in the database
                    await _apiClient.UpdateTaskStatusAsync(
                        runningTask.Id,
                        BatchTaskStatus.Cancelled,
                        "Task was manually aborted by user"
                    );
                    
                    // Update UI
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var mainWindow = System.Windows.Application.Current.MainWindow;
                        if (mainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                        {
                            var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == nodeId);
                            if (node != null)
                            {
                                node.ClearActiveTask();
                                UpdateDebugOutput($"Task aborted: {runningTask.Type} ({runningTask.Id})");
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error aborting task: {ex.Message}");
                UpdateDebugOutput($"Error aborting task: {ex.Message}");
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
                await Task.Delay(500); // Half-second delay to show 100% completion
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
                        
                        // Use the dispatcher to show message box on UI thread
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            System.Windows.MessageBox.Show(
                                $"Message from {senderName}:\n\n{messageText}", 
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
                    var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
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
                    var node = viewModel.Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
                    if (node != null)
                    {
                        node.UpdateTaskName(message);
                    }
                }
            });
        }

        private void UpdateDebugOutput(string output)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow.DataContext is ViewModels.MainViewModel viewModel)
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

        private void ClearTaskFromUI(BatchTask task)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                {
                    var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
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
                    var node = mainViewModel.Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
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

                // Process each directory
                foreach (string directory in directories)
                {
                    if (!Directory.Exists(directory))
                    {
                        continue;
                    }

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

                // Find all VDB files from the directories
                List<string> allVdbFiles = new List<string>();
                Dictionary<string, List<string>> vdbFilesByGroup = new Dictionary<string, List<string>>();
                
                foreach (string rootPath in rootDirectories)
                {
                    // Check for cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await _apiClient.UpdateTaskStatusAsync(
                            task.Id,
                            BatchTaskStatus.Cancelled,
                            "Task was cancelled during file scan"
                        );
                        ClearTaskFromUI(task);
                        return;
                    }
                    
                    if (!Directory.Exists(rootPath) && File.Exists(rootPath) && Path.GetExtension(rootPath).ToLower() == ".vdb")
                    {
                        // This is a direct VDB file
                        allVdbFiles.Add(rootPath);
                        
                        // Group by filename without extension and without underscores
                        string fileName = Path.GetFileNameWithoutExtension(rootPath);
                        string groupName = fileName.Replace("_", "");
                        
                        if (!vdbFilesByGroup.ContainsKey(groupName))
                        {
                            vdbFilesByGroup[groupName] = new List<string>();
                        }
                        vdbFilesByGroup[groupName].Add(rootPath);
                    }
                    else if (Directory.Exists(rootPath))
                    {
                        // This is a directory, search for VDB files
                        var vdbFiles = Directory.GetFiles(rootPath, "*.vdb", SearchOption.AllDirectories);
                        allVdbFiles.AddRange(vdbFiles);
                        
                        // Group VDBs by their parent directory
                        foreach (string vdbFile in vdbFiles)
                        {
                            string parentDir = Path.GetFileName(Path.GetDirectoryName(vdbFile) ?? "");
                            if (string.IsNullOrEmpty(parentDir))
                            {
                                parentDir = "Default";
                            }
                            
                            if (!vdbFilesByGroup.ContainsKey(parentDir))
                            {
                                vdbFilesByGroup[parentDir] = new List<string>();
                            }
                            vdbFilesByGroup[parentDir].Add(vdbFile);
                        }
                    }
                }
                
                if (allVdbFiles.Count == 0)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Completed,
                        "No VDB files were found in the selected directories"
                    );
                    ClearTaskFromUI(task, true);
                    return;
                }
                
                // Check for cancellation again after scanning
                if (cancellationToken.IsCancellationRequested)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Cancelled,
                        "Task was cancelled after file scan"
                    );
                    ClearTaskFromUI(task);
                    return;
                }
                
                // Update debug output only, keep UI task name consistent
                System.Diagnostics.Debug.WriteLine($"Found {allVdbFiles.Count} VDB files in {vdbFilesByGroup.Count} groups");
                UpdateDebugOutput($"Found {allVdbFiles.Count} VDB files in {vdbFilesByGroup.Count} groups");
                
                // Process each group of VDB files
                int processedGroups = 0;
                int successfulGroups = 0;
                int failedGroups = 0;
                
                foreach (var group in vdbFilesByGroup)
                {
                    // Check if the task has been cancelled
                    if (cancellationToken.IsCancellationRequested)
                    {
                        // Break the loop and go to the finally block
                        break;
                    }
                    
                    string groupName = group.Key;
                    var files = group.Value;
                    
                    if (files.Count == 0)
                    {
                        continue;
                    }
                    
                    // Keep the task name consistent
                    // Log group info to debug output only
                    UpdateDebugOutput($"Processing group: {groupName}");
                    
                    try
                    {
                        // Determine output folder and input folder
                        string outputFolder;
                        string inputFolder = Path.GetDirectoryName(files[0]) ?? "";
                        
                        if (useOverrideOutput && !string.IsNullOrEmpty(overrideOutputDirectory))
                        {
                            // Use the override output directory with a subfolder for the group
                            outputFolder = Path.Combine(overrideOutputDirectory, groupName);
                        }
                        else
                        {
                            // Use the same directory as the first file
                            outputFolder = inputFolder;
                        }
                        
                        // Create the output folder if it doesn't exist
                        if (!Directory.Exists(outputFolder))
                        {
                            Directory.CreateDirectory(outputFolder);
                        }
                        
                        try
                        {
                            // Get folder name for output file
                            string folderName = Path.GetFileName(inputFolder);
                            if (string.IsNullOrEmpty(folderName))
                            {
                                folderName = groupName;
                            }
                            
                            // Determine suffix based on density options
                            string densitySuffix = "_native";
                            if (useFixedDimension)
                            {
                                densitySuffix = "_HD";
                            }
                            else if (useSdDimension)
                            {
                                densitySuffix = "_SD";
                            }
                            
                            // Add the density suffix to folder name before any numbers
                            string outputBaseName;
                            
                            // Check if the name contains a number sequence
                            int numberedPartIndex = -1;
                            for (int i = 0; i < folderName.Length; i++)
                            {
                                if (char.IsDigit(folderName[i]))
                                {
                                    // Find the start of the numeric sequence
                                    int start = i;
                                    while (start > 0 && folderName[start - 1] == '_')
                                    {
                                        start--;
                                    }
                                    numberedPartIndex = start;
                                    break;
                                }
                            }
                            
                            if (numberedPartIndex > 0)
                            {
                                // Insert the density suffix before the underscore that precedes the numbers
                                outputBaseName = folderName.Substring(0, numberedPartIndex) + densitySuffix + folderName.Substring(numberedPartIndex);
                            }
                            else
                            {
                                // No numbers found, just append the suffix
                                outputBaseName = folderName + densitySuffix;
                            }
                            
                            // Create output file path in the output folder
                            string outputFile;
                            
                            if (createOutputFolder)
                            {
                                // Create a subfolder with the same name plus density suffix
                                string targetSubfolder = Path.Combine(outputFolder, outputBaseName);
                                
                                // Create the subfolder if it doesn't exist
                                if (!Directory.Exists(targetSubfolder))
                                {
                                    Directory.CreateDirectory(targetSubfolder);
                                }
                                
                                outputFile = Path.Combine(targetSubfolder, $"{outputBaseName}.vcvol");
                            }
                            else
                            {
                                // Put the file directly in the output folder without creating a subfolder
                                outputFile = Path.Combine(outputFolder, $"{outputBaseName}.vcvol");
                            }
                            
                            // Build the command line arguments for Volume Compressor
                            string arguments = "-w -o \"" + outputFile + "\"";
                            
                            // Add dimension flag based on which option is selected
                            if (useFixedDimension)
                            {
                                arguments += " -d 512";
                            }
                            else if (useSdDimension)
                            {
                                arguments += " -d 350";
                            }
                            
                            // Add compression flag based on selected compression level
                            switch (compressionLevel)
                            {
                                case "4x Compression":
                                    arguments += " -c 4";
                                    break;
                                case "8x Compression":
                                    arguments += " -c 8";
                                    break;
                                case "No Compression":
                                default:
                                    // No compression flag needed for "No Compression"
                                    break;
                            }
                            
                            // Add the input folder as input
                            arguments += " \"" + inputFolder + "\"";
                            
                            // Create process start info
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = executablePath,
                                Arguments = arguments,
                                UseShellExecute = false,
                                CreateNoWindow = true,  // Hide the command window
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                WorkingDirectory = inputFolder
                            };
                            
                            UpdateDebugOutput($"=== Processing folder: {inputFolder} ({processedGroups + 1}/{vdbFilesByGroup.Count}) ===");
                            UpdateDebugOutput($"Command: {executablePath} {arguments}");
                            
                            // Start the process
                            using (var process = new Process())
                            {
                                process.StartInfo = startInfo;
                                
                                // Set up event handlers for real-time output
                                process.OutputDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        UpdateDebugOutput($"STDOUT: {e.Data}");
                                    }
                                };
                                
                                process.ErrorDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        UpdateDebugOutput($"STDERR: {e.Data}");
                                    }
                                };
                                
                                // Start the process and begin reading output
                                process.Start();
                                process.BeginOutputReadLine();
                                process.BeginErrorReadLine();
                                
                                // Wait for process to exit
                                await process.WaitForExitAsync();
                                
                                // Check if process was successful
                                if (process.ExitCode == 0)
                                {
                                    successfulGroups++;
                                    System.Diagnostics.Debug.WriteLine($"Successfully compressed folder: {inputFolder}");
                                }
                                else
                                {
                                    failedGroups++;
                                    System.Diagnostics.Debug.WriteLine($"Failed to compress folder: {inputFolder}, exit code: {process.ExitCode}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failedGroups++;
                            System.Diagnostics.Debug.WriteLine($"Error processing folder {inputFolder}: {ex.Message}");
                            UpdateDebugOutput($"Error processing folder {inputFolder}: {ex.Message}");
                        }
                        
                        // Increment counter and update progress
                        processedGroups++;
                        double progress = (double)processedGroups / vdbFilesByGroup.Count;
                        
                        // Update task progress (use our helper method)
                        UpdateTaskProgress(task, progress);
                        
                        // Add small delay to ensure UI updates
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        UpdateDebugOutput($"Error processing group {groupName}: {ex.Message}");
                    }
                }

                // Check if the task was cancelled
                if (cancellationToken.IsCancellationRequested)
                {
                    await _apiClient.UpdateTaskStatusAsync(
                        task.Id,
                        BatchTaskStatus.Cancelled,
                        $"Task was cancelled after processing {processedGroups} of {vdbFilesByGroup.Count} folders"
                    );
                    ClearTaskFromUI(task);
                    return;
                }

                // Mark task as completed
                string resultMessage = $"Volume compression completed. Successfully processed {successfulGroups} of {vdbFilesByGroup.Count} folders";
                if (failedGroups > 0)
                {
                    resultMessage += $" ({failedGroups} failed)";
                }
                
                Debug.WriteLine($"[VolumeCompressionTask] Task {task.Id}: updating API to Completed with message: {resultMessage}");
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id,
                    BatchTaskStatus.Completed,
                    resultMessage
                );

                // Clear the task from UI after a short delay and trigger Slack
                await Task.Delay(500);
                Debug.WriteLine($"[VolumeCompressionTask] Task {task.Id}: about to clear UI and trigger Slack");
                ClearTaskFromUI(task, true);
                Debug.WriteLine($"[VolumeCompressionTask] Task {task.Id}: UI cleared");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VolumeCompressionTask] Task {task.Id}: error occurred: {ex.Message}");
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id,
                    BatchTaskStatus.Failed,
                    $"Error processing volume compression: {ex.Message}"
                );
                Debug.WriteLine($"[VolumeCompressionTask] Task {task.Id}: clearing UI without Slack");
                ClearTaskFromUI(task, false);
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
                
                // Process each folder
                foreach (var rootDir in rootDirectories)
                {
                    if (!Directory.Exists(rootDir))
                    {
                        UpdateDebugOutput($"Directory not found: {rootDir}");
                        continue;
                    }

                    try
                    {
                        // Get the folder name to use for project name
                        string folderName = new DirectoryInfo(rootDir).Name;
                        UpdateDebugOutput($"Processing folder: {folderName}");

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
                            UpdateDebugOutput($"No PNG or JPEG images found in {folderName}");
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
                        UpdateDebugOutput($"Running command: {realityCaptureExe} {arguments}");

                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                UpdateDebugOutput($"RC: {e.Data}");
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                UpdateDebugOutput($"RC Error: {e.Data}");
                            }
                        };

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
                                UpdateDebugOutput($"Reality Capture process for {folderName} timed out after 1 hour and was terminated.");
                                failedFolders++;
                            }
                            catch (Exception ex)
                            {
                                UpdateDebugOutput($"Failed to terminate Reality Capture process: {ex.Message}");
                            }
                        }
                        else if (process.ExitCode != 0)
                        {
                            UpdateDebugOutput($"Reality Capture process exited with code {process.ExitCode}");
                            failedFolders++;
                        }
                        else
                        {
                            UpdateDebugOutput($"Successfully processed {folderName}");
                            successfulFolders++;
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
                        UpdateDebugOutput($"Error processing folder {rootDir}: {ex.Message}");
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
                        UpdateDebugOutput($"Error cleaning up temp file: {ex.Message}");
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
                    $"Error during Reality Capture processing: {ex.Message}"
                );
            }
        }
    }
} 