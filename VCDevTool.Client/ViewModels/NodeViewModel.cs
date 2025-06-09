using System;
using System.ComponentModel;
using System.Windows.Media;
using VCDevTool.Shared;

namespace VCDevTool.Client.ViewModels
{
    public enum NodeState
    {
        Offline,
        Available,
        Running
    }

    public class NodeViewModel : INotifyPropertyChanged
    {
        private ComputerNode _node;
        private BatchTask? _activeTask;
        private BatchTask? _lastCompletedTask;
        private double _taskProgress;
        private bool _isSelected;
        private DateTime? _taskStartTime;
        private TimeSpan _taskElapsedTime = TimeSpan.Zero;
        private TimeSpan _taskRemainingTime = TimeSpan.Zero;
        private NodeState _state;

        public event PropertyChangedEventHandler? PropertyChanged;

        public NodeViewModel(ComputerNode node)
        {
            _node = node;
            UpdateState();
        }

        public string Id => _node.Id;
        public string Name => _node.Name;
        public string IpAddress => _node.IpAddress;
        public bool IsAvailable => State == NodeState.Available;
        public bool IsOffline => State == NodeState.Offline;
        public bool HasActiveTask => State == NodeState.Running;
        public bool HasCompletedLastTask => _lastCompletedTask != null;
        
        public NodeState State
        {
            get => _state;
            private set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged(nameof(State));
                    OnPropertyChanged(nameof(IsAvailable));
                    OnPropertyChanged(nameof(IsOffline));
                    OnPropertyChanged(nameof(HasActiveTask));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public BatchTask? ActiveTask => _activeTask;
        public BatchTask? GetLastCompletedTask => _lastCompletedTask;
        public string? LastTaskName => _lastCompletedTask?.Type.ToString().ToUpper();
        public string? LastTaskStartedAt => _lastCompletedTask?.StartedAt?.ToString("MM/dd/yyyy @h:mmtt");
        public string? LastTaskCompletedAt => _lastCompletedTask?.CompletedAt?.ToString("MM/dd/yyyy @h:mmtt");
        public string? LastTaskStatus => _lastCompletedTask?.Status.ToString().ToUpper();
        
        public System.Windows.Media.Brush LastTaskStatusColor
        {
            get
            {
                if (_lastCompletedTask == null) return System.Windows.Media.Brushes.Gray;
                
                return _lastCompletedTask.Status switch
                {
                    Shared.BatchTaskStatus.Completed => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#27fc23")),  // Green
                    Shared.BatchTaskStatus.Failed => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#fc2332")),    // Red
                    _ => System.Windows.Media.Brushes.Gray
                };
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string? ActiveTaskName => _activeTask != null 
            ? (!string.IsNullOrEmpty(_activeTask.Name) ? _activeTask.Name : _activeTask.Type.ToString().ToUpper())
            : null;
        
        public string TaskStartTimeFormatted => _taskStartTime.HasValue 
            ? _taskStartTime.Value.ToString("MM/dd/yyyy @h:mmtt") 
            : "N/A";
            
        public string TaskElapsedTimeFormatted => HasActiveTask 
            ? $"{_taskElapsedTime.Hours:D2}:{_taskElapsedTime.Minutes:D2}:{_taskElapsedTime.Seconds:D2}" 
            : "N/A";
            
        public string TaskRemainingTimeFormatted => HasActiveTask 
            ? $"{_taskRemainingTime.Hours:D2}:{_taskRemainingTime.Minutes:D2}:{_taskRemainingTime.Seconds:D2}" 
            : "N/A";

        public double TaskProgress
        {
            get => _taskProgress;
            set
            {
                if (_taskProgress != value)
                {
                    _taskProgress = value;
                    
                    // Update estimated remaining time based on progress and elapsed time
                    if (_taskProgress > 0 && _taskElapsedTime > TimeSpan.Zero)
                    {
                        double remainingPercentage = 1.0 - _taskProgress;
                        double estimatedTotalSeconds = _taskElapsedTime.TotalSeconds / _taskProgress;
                        double remainingSeconds = estimatedTotalSeconds * remainingPercentage;
                        _taskRemainingTime = TimeSpan.FromSeconds(remainingSeconds);
                        OnPropertyChanged(nameof(TaskRemainingTimeFormatted));
                    }
                    
                    OnPropertyChanged(nameof(TaskProgress));
                }
            }
        }

        // Used internally for calculations, not displayed in UI
        internal string TaskProgressPercentage => $"{_taskProgress * 100:F0}%";

        public string StatusText
        {
            get
            {
                return State switch
                {
                    NodeState.Offline => "OFFLINE",
                    NodeState.Running => ActiveTaskName ?? "RUNNING",
                    NodeState.Available => "AVAILABLE",
                    _ => string.Empty
                };
            }
        }

        public System.Windows.Media.Brush StatusColor
        {
            get
            {
                return State switch
                {
                    NodeState.Offline => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#fc2332")),    // Dark Red
                    NodeState.Running =>  new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#fc9e23")),  // Orange
                    NodeState.Available => new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#27fc23")),  // Green
                    _ => System.Windows.Media.Brushes.Gray
                };
            }
        }

        public void UpdateFromNode(ComputerNode node)
        {
            _node = node;
            UpdateState();
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(IpAddress));
        }

        public void SetLastCompletedTask(BatchTask task)
        {
            _lastCompletedTask = task;
            
            OnPropertyChanged(nameof(HasCompletedLastTask));
            OnPropertyChanged(nameof(LastTaskName));
            OnPropertyChanged(nameof(LastTaskStartedAt));
            OnPropertyChanged(nameof(LastTaskCompletedAt));
            OnPropertyChanged(nameof(LastTaskStatus));
            OnPropertyChanged(nameof(LastTaskStatusColor));
        }

        public void SetActiveTask(BatchTask task, double progress)
        {
            _activeTask = task;
            TaskProgress = progress;
            
            if (_taskStartTime == null)
            {
                _taskStartTime = task.StartedAt ?? DateTime.Now;
                _taskElapsedTime = DateTime.Now - _taskStartTime.Value;
            }
            else
            {
                _taskElapsedTime = DateTime.Now - _taskStartTime.Value;
            }
            
            UpdateState();
            
            OnPropertyChanged(nameof(ActiveTaskName));
            OnPropertyChanged(nameof(TaskStartTimeFormatted));
            OnPropertyChanged(nameof(TaskElapsedTimeFormatted));
            OnPropertyChanged(nameof(TaskRemainingTimeFormatted));
        }
        
        public void ClearActiveTask(bool wasCompleted = false)
        {
            if (wasCompleted && _activeTask != null)
            {
                SetLastCompletedTask(_activeTask);
            }
            
            _activeTask = null;
            _taskProgress = 0;
            _taskStartTime = null;
            _taskElapsedTime = TimeSpan.Zero;
            _taskRemainingTime = TimeSpan.Zero;
            
            UpdateState();
            
            OnPropertyChanged(nameof(ActiveTaskName));
            OnPropertyChanged(nameof(TaskProgress));
            OnPropertyChanged(nameof(TaskProgressPercentage));
            OnPropertyChanged(nameof(TaskStartTimeFormatted));
            OnPropertyChanged(nameof(TaskElapsedTimeFormatted));
            OnPropertyChanged(nameof(TaskRemainingTimeFormatted));
        }

        private void UpdateState()
        {
            if (!_node.IsAvailable)
            {
                State = NodeState.Offline;
            }
            else if (_activeTask != null)
            {
                State = NodeState.Running;
            }
            else
            {
                State = NodeState.Available;
            }
        }

        public void UpdateTaskTimes()
        {
            if (HasActiveTask && _taskStartTime.HasValue)
            {
                _taskElapsedTime = DateTime.Now - _taskStartTime.Value;
                
                // Only update remaining time if we have some progress
                if (_taskProgress > 0)
                {
                    double remainingPercentage = 1.0 - _taskProgress;
                    double estimatedTotalSeconds = _taskElapsedTime.TotalSeconds / _taskProgress;
                    double remainingSeconds = estimatedTotalSeconds * remainingPercentage;
                    _taskRemainingTime = TimeSpan.FromSeconds(remainingSeconds);
                }
                
                OnPropertyChanged(nameof(TaskElapsedTimeFormatted));
                OnPropertyChanged(nameof(TaskRemainingTimeFormatted));
            }
        }
        
        public void UpdateTaskName(string newTaskName)
        {
            if (HasActiveTask && _activeTask != null)
            {
                _activeTask.Name = newTaskName;
                OnPropertyChanged(nameof(ActiveTaskName));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 