using System;
using System.ComponentModel;
using System.Windows.Media;
using VCDevTool.Shared;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

namespace VCDevTool.Client.ViewModels
{
    public class TaskViewModel : INotifyPropertyChanged
    {
        private readonly BatchTask _task;
        private double _progress;
        private string? _assignedNodeName;
        private ObservableCollection<TaskFolderProgressViewModel> _folderProgress;
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public TaskViewModel(BatchTask task)
        {
            _task = task;
            _folderProgress = new ObservableCollection<TaskFolderProgressViewModel>();
        }

        public int Id => _task.Id;
        public string Name => !string.IsNullOrEmpty(_task.Name) ? _task.Name : _task.Type.ToString().ToUpper();
        public TaskType Type => _task.Type;
        public BatchTaskStatus Status => _task.Status;
        public string? AssignedNodeId => _task.AssignedNodeId;
        public DateTime CreatedAt => _task.CreatedAt;
        public DateTime? StartedAt => _task.StartedAt;
        public DateTime? CompletedAt => _task.CompletedAt;
        
        public string TaskTypeDisplay => _task.Type.ToString().ToUpper();
        
        public string StatusDisplay => _task.Status.ToString().ToUpper();
        
        public string CreatedAtFormatted => _task.CreatedAt.ToString("MM/dd/yyyy @h:mmtt");
        
        public string StartedAtFormatted => _task.StartedAt?.ToString("MM/dd/yyyy @h:mmtt") ?? "N/A";
        
        public string CompletedAtFormatted => _task.CompletedAt?.ToString("MM/dd/yyyy @h:mmtt") ?? "N/A";
        
        public string TaskProgressPercentage => $"{_progress * 100:0}%";
        
        // Multiple assigned nodes support
        public List<string> AssignedNodeIds
        {
            get
            {
                try
                {
                    if (!string.IsNullOrEmpty(_task.AssignedNodeIds))
                    {
                        return JsonSerializer.Deserialize<List<string>>(_task.AssignedNodeIds) ?? new List<string>();
                    }
                }
                catch
                {
                    // Fall back to single node if JSON parsing fails
                }
                
                // Fallback to single assigned node for backward compatibility
                if (!string.IsNullOrEmpty(_task.AssignedNodeId))
                {
                    return new List<string> { _task.AssignedNodeId };
                }
                
                return new List<string>();
            }
        }
        
        public string AssignedNodesDisplay
        {
            get
            {
                var nodeIds = AssignedNodeIds;
                if (!nodeIds.Any())
                {
                    return "Unassigned";
                }
                
                if (nodeIds.Count == 1)
                {
                    return !string.IsNullOrEmpty(_assignedNodeName) ? _assignedNodeName : nodeIds[0];
                }
                
                return $"{nodeIds.Count} nodes assigned";
            }
        }
        
        public string? AssignedNodeName
        {
            get => _assignedNodeName;
            set
            {
                if (_assignedNodeName != value)
                {
                    _assignedNodeName = value;
                    OnPropertyChanged(nameof(AssignedNodeName));
                    OnPropertyChanged(nameof(AssignedNodesDisplay));
                }
            }
        }
        
        public double Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    OnPropertyChanged(nameof(Progress));
                    OnPropertyChanged(nameof(TaskProgressPercentage));
                }
            }
        }

        public ObservableCollection<TaskFolderProgressViewModel> FolderProgress
        {
            get => _folderProgress;
            set
            {
                if (_folderProgress != value)
                {
                    _folderProgress = value;
                    OnPropertyChanged(nameof(FolderProgress));
                    OnPropertyChanged(nameof(HasFolderProgress));
                    OnPropertyChanged(nameof(FolderProgressSummary));
                }
            }
        }

        public bool HasFolderProgress => _folderProgress.Any();

        public string FolderProgressSummary
        {
            get
            {
                if (!HasFolderProgress) return "No folders tracked";
                
                var completed = _folderProgress.Count(f => f.Status == TaskFolderStatus.Completed);
                var failed = _folderProgress.Count(f => f.Status == TaskFolderStatus.Failed);
                var inProgress = _folderProgress.Count(f => f.Status == TaskFolderStatus.InProgress);
                var pending = _folderProgress.Count(f => f.Status == TaskFolderStatus.Pending);
                var total = _folderProgress.Count;
                
                return $"{completed} completed, {failed} failed, {inProgress} in progress, {pending} pending ({total} total)";
            }
        }
        
        public bool IsRunning => _task.Status == BatchTaskStatus.Running;
        
        public bool IsPending => _task.Status == BatchTaskStatus.Pending;
        
        public bool IsCompleted => _task.Status == BatchTaskStatus.Completed;
        
        public bool IsFailed => _task.Status == BatchTaskStatus.Failed;
        
        public bool IsCancelled => _task.Status == BatchTaskStatus.Cancelled;
        
        public bool CanAbort => IsRunning || IsPending;
        
        public System.Windows.Media.Brush StatusColor
        {
            get
            {
                return _task.Status switch
                {
                    BatchTaskStatus.Running => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D7")),  // Blue
                    BatchTaskStatus.Completed => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#27fc23")),  // Green
                    BatchTaskStatus.Failed => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#fc2332")),    // Red
                    BatchTaskStatus.Cancelled => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#fc9923")), // Orange
                    _ => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#AAAAAA"))                         // Gray
                };
            }
        }
        
        public void UpdateProgress(double progress)
        {
            Progress = progress;
        }
        
        public void UpdateStatus(BatchTaskStatus status)
        {
            _task.Status = status;
            
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsCancelled));
            OnPropertyChanged(nameof(CanAbort));
        }

        public void UpdateFolderProgress(ObservableCollection<TaskFolderProgressViewModel> folderProgress)
        {
            FolderProgress = folderProgress;
        }

        public void RefreshFolderProgressSummary()
        {
            OnPropertyChanged(nameof(FolderProgressSummary));
        }
        
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class TaskFolderProgressViewModel : INotifyPropertyChanged
    {
        private readonly TaskFolderProgress _folderProgress;
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public TaskFolderProgressViewModel(TaskFolderProgress folderProgress)
        {
            _folderProgress = folderProgress;
        }

        public int Id => _folderProgress.Id;
        public int TaskId => _folderProgress.TaskId;
        public string FolderPath => _folderProgress.FolderPath;
        public string FolderName => _folderProgress.FolderName;
        public TaskFolderStatus Status => _folderProgress.Status;
        public string? AssignedNodeId => _folderProgress.AssignedNodeId;
        public string? AssignedNodeName => _folderProgress.AssignedNodeName;
        public DateTime CreatedAt => _folderProgress.CreatedAt;
        public DateTime? StartedAt => _folderProgress.StartedAt;
        public DateTime? CompletedAt => _folderProgress.CompletedAt;
        public string? ErrorMessage => _folderProgress.ErrorMessage;
        public double Progress => _folderProgress.Progress;
        public string? OutputPath => _folderProgress.OutputPath;

        public string StatusDisplay => _folderProgress.Status.ToString().ToUpper();
        public string ProgressPercentage => $"{_folderProgress.Progress * 100:0}%";
        public string NodeDisplayName => !string.IsNullOrEmpty(_folderProgress.AssignedNodeName) ? _folderProgress.AssignedNodeName : (_folderProgress.AssignedNodeId ?? "Unassigned");
        
        public string StartedAtFormatted => _folderProgress.StartedAt?.ToString("MM/dd/yyyy @h:mmtt") ?? "N/A";
        public string CompletedAtFormatted => _folderProgress.CompletedAt?.ToString("MM/dd/yyyy @h:mmtt") ?? "N/A";

        public System.Windows.Media.Brush StatusColor
        {
            get
            {
                return _folderProgress.Status switch
                {
                    TaskFolderStatus.InProgress => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D7")),  // Blue
                    TaskFolderStatus.Completed => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#27fc23")),  // Green
                    TaskFolderStatus.Failed => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#fc2332")),    // Red
                    TaskFolderStatus.Skipped => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#fc9923")), // Orange
                    _ => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#AAAAAA"))                         // Gray
                };
            }
        }

        public void UpdateStatus(TaskFolderStatus status)
        {
            _folderProgress.Status = status;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(StatusColor));
        }

        public void UpdateProgress(double progress)
        {
            _folderProgress.Progress = progress;
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(ProgressPercentage));
        }

        public void UpdateNodeAssignment(string? nodeId, string? nodeName)
        {
            _folderProgress.AssignedNodeId = nodeId;
            _folderProgress.AssignedNodeName = nodeName;
            OnPropertyChanged(nameof(AssignedNodeId));
            OnPropertyChanged(nameof(AssignedNodeName));
            OnPropertyChanged(nameof(NodeDisplayName));
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 