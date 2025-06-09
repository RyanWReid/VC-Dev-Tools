using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using VCDevTool.Shared;

namespace VCDevTool.Client.Services
{
    /// <summary>
    /// Utility class for common task execution operations including folder locking and UI progress updates
    /// </summary>
    public class TaskExecutionUtility
    {
        private readonly ApiClient _apiClient;
        private readonly NodeService _nodeService;

        public TaskExecutionUtility(ApiClient apiClient, NodeService nodeService)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _nodeService = nodeService ?? throw new ArgumentNullException(nameof(nodeService));
        }

        #region Folder Locking Methods

        /// <summary>
        /// Attempts to acquire a lock on a folder to ensure exclusive access across nodes
        /// </summary>
        /// <param name="folderPath">Path to the folder to lock</param>
        /// <param name="nodeId">ID of the node requesting the lock</param>
        /// <returns>True if lock was acquired, false otherwise</returns>
        public async Task<bool> TryAcquireFolderLockAsync(string folderPath, string nodeId)
        {
            try
            {
                string normalizedFolderPath = PathUtils.NormalizePath(folderPath);
                string folderLockPath = PathUtils.GetFolderLockKey(normalizedFolderPath);
                bool lockAcquired = await _apiClient.TryAcquireFileLockAsync(folderLockPath, nodeId);
                
                if (lockAcquired)
                {
                    LogDebugMessage($"Acquired lock for folder: {folderPath}");
                }
                else
                {
                    LogDebugMessage($"Could not acquire lock for folder: {folderPath} - already being processed by another node");
                }
                
                return lockAcquired;
            }
            catch (Exception ex)
            {
                LogError($"Error acquiring folder lock: {folderPath}", ex);
                return false;
            }
        }

        /// <summary>
        /// Releases a previously acquired folder lock
        /// </summary>
        /// <param name="folderPath">Path to the folder to unlock</param>
        /// <param name="nodeId">ID of the node that acquired the lock</param>
        public async Task ReleaseFolderLockAsync(string folderPath, string nodeId)
        {
            try
            {
                string normalizedFolderPath = PathUtils.NormalizePath(folderPath);
                string folderLockPath = PathUtils.GetFolderLockKey(normalizedFolderPath);
                await _apiClient.ReleaseFileLockAsync(folderLockPath, nodeId);
                LogDebugMessage($"Released lock for folder: {folderPath}");
            }
            catch (Exception ex)
            {
                LogError($"Error releasing folder lock: {folderPath}", ex);
            }
        }

        /// <summary>
        /// Executes an action with automatic folder locking and release
        /// </summary>
        /// <param name="folderPath">Path to the folder to lock</param>
        /// <param name="action">The action to execute while the folder is locked</param>
        /// <returns>True if the operation was successful, false if the lock couldn't be acquired</returns>
        public async Task<bool> ExecuteWithFolderLockAsync(string folderPath, Func<Task> action)
        {
            string nodeId = _nodeService.CurrentNode.Id;
            bool lockAcquired = await TryAcquireFolderLockAsync(folderPath, nodeId);
            
            if (!lockAcquired)
            {
                return false;
            }
            
            try
            {
                await action();
                return true;
            }
            finally
            {
                await ReleaseFolderLockAsync(folderPath, nodeId);
            }
        }

        /// <summary>
        /// Executes an action with automatic folder locking and release, returning a result
        /// </summary>
        /// <typeparam name="T">The type of result returned by the action</typeparam>
        /// <param name="folderPath">Path to the folder to lock</param>
        /// <param name="action">The action to execute while the folder is locked</param>
        /// <param name="defaultValue">Default value to return if lock can't be acquired</param>
        /// <returns>The result of the action, or defaultValue if lock couldn't be acquired</returns>
        public async Task<T> ExecuteWithFolderLockAsync<T>(string folderPath, Func<Task<T>> action, T defaultValue)
        {
            string nodeId = _nodeService.CurrentNode.Id;
            bool lockAcquired = await TryAcquireFolderLockAsync(folderPath, nodeId);
            
            if (!lockAcquired)
            {
                return defaultValue;
            }
            
            try
            {
                return await action();
            }
            finally
            {
                await ReleaseFolderLockAsync(folderPath, nodeId);
            }
        }

        #endregion

        #region Task Progress UI Methods

        /// <summary>
        /// Updates the progress of a task in the UI
        /// </summary>
        /// <param name="task">The task being executed</param>
        /// <param name="progress">Progress value between 0 and 1</param>
        public void UpdateTaskProgress(BatchTask task, double progress)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating task progress: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error invoking dispatcher for task progress update: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the task name or status message displayed in the UI
        /// </summary>
        /// <param name="task">The task being executed</param>
        /// <param name="message">Status message to display</param>
        public void UpdateTaskUI(BatchTask task, string message)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (System.Windows.Application.Current.MainWindow.DataContext is ViewModels.MainViewModel viewModel)
                        {
                            var node = viewModel.Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
                            if (node != null)
                            {
                                node.UpdateTaskName(message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating task UI: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error invoking dispatcher for task UI update: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the active task from the node UI
        /// </summary>
        /// <param name="task">The task to clear</param>
        /// <param name="wasCompleted">Whether the task was completed successfully</param>
        public void ClearTaskFromUI(BatchTask task, bool wasCompleted = false)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
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
                                    Debug.WriteLine($"Task {task.Id} completed on node {node.Name}");
                                    node.SetLastCompletedTask(task);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing completed task in UI: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error invoking dispatcher for completed task: {ex.Message}");
            }
        }

        #endregion

        #region Notification and Debug Methods

        /// <summary>
        /// Logs a message to the debug output
        /// </summary>
        /// <param name="message">Message to log</param>
        public async Task LogDebugMessageAsync(string message)
        {
            try
            {
                // Log to debug output
                Debug.WriteLine(message);
                
                // Send to API for broadcasting to other clients
                string nodeName = _nodeService.CurrentNode.Name;
                string nodeId = _nodeService.CurrentNode.Id;
                await _apiClient.SendDebugMessageAsync(nodeName, message, nodeId);
                
                // Update UI
                LogDebugMessage(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error logging debug message: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Logs a message to the debug output UI
        /// </summary>
        /// <param name="message">Message to log</param>
        public void LogDebugMessage(string message)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (System.Windows.Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel viewModel)
                        {
                            viewModel.DebugOutput += message + Environment.NewLine;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating debug output UI: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error invoking dispatcher for debug output: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Logs an error message to debug output
        /// </summary>
        /// <param name="message">Error message prefix</param>
        /// <param name="ex">Exception that occurred</param>
        public void LogError(string message, Exception ex)
        {
            string errorMessage = $"{message}: {ex.Message}";
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {errorMessage}");
            LogDebugMessage(errorMessage);
        }

        #endregion
    }
} 