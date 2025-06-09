using System;
using System.ComponentModel;
using System.Windows.Media;
using VCDevTool.Shared;

namespace VCDevTool.Client.ViewModels
{
    public class TaskViewModel : INotifyPropertyChanged
    {
        private readonly BatchTask _task;
        private double _progress;
        private string? _assignedNodeName;
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public TaskViewModel(BatchTask task)
        {
            _task = task;
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
        
        public string? AssignedNodeName
        {
            get => _assignedNodeName;
            set
            {
                if (_assignedNodeName != value)
                {
                    _assignedNodeName = value;
                    OnPropertyChanged(nameof(AssignedNodeName));
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
        
        public bool IsRunning => _task.Status == BatchTaskStatus.Running;
        
        public bool IsPending => _task.Status == BatchTaskStatus.Pending;
        
        public bool IsCompleted => _task.Status == BatchTaskStatus.Completed;
        
        public bool IsFailed => _task.Status == BatchTaskStatus.Failed;
        
        public bool IsCancelled => _task.Status == BatchTaskStatus.Cancelled;
        
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
        }
        
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 