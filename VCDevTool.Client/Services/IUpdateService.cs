using System;
using System.Threading.Tasks;

namespace VCDevTool.Client.Services
{
    public interface IUpdateService
    {
        /// <summary>
        /// Gets the current application version
        /// </summary>
        Version CurrentVersion { get; }

        /// <summary>
        /// Gets the latest available version from the server
        /// </summary>
        Version? LatestVersion { get; }

        /// <summary>
        /// Indicates if an update is currently available
        /// </summary>
        bool IsUpdateAvailable { get; }

        /// <summary>
        /// Indicates if an update is currently being downloaded or installed
        /// </summary>
        bool IsUpdating { get; }

        /// <summary>
        /// Current status of the update process
        /// </summary>
        UpdateStatus Status { get; }

        /// <summary>
        /// Progress of the current update operation (0.0 to 1.0)
        /// </summary>
        double Progress { get; }

        /// <summary>
        /// Event fired when update status changes
        /// </summary>
        event EventHandler<UpdateStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// Event fired when update progress changes
        /// </summary>
        event EventHandler<UpdateProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Checks for available updates
        /// </summary>
        /// <returns>True if updates are available</returns>
        Task<bool> CheckForUpdatesAsync();

        /// <summary>
        /// Downloads and applies available updates
        /// </summary>
        /// <param name="restartApplication">Whether to restart the application after update</param>
        /// <returns>True if update was successful</returns>
        Task<bool> UpdateAsync(bool restartApplication = true);

        /// <summary>
        /// Rolls back to the previous version if possible
        /// </summary>
        /// <returns>True if rollback was successful</returns>
        Task<bool> RollbackAsync();

        /// <summary>
        /// Enables or disables automatic updates
        /// </summary>
        /// <param name="enabled">Whether automatic updates should be enabled</param>
        void SetAutoUpdateEnabled(bool enabled);

        /// <summary>
        /// Gets whether automatic updates are enabled
        /// </summary>
        bool IsAutoUpdateEnabled { get; }

        /// <summary>
        /// Forces an immediate update check and download if available
        /// </summary>
        /// <returns>True if update was found and applied</returns>
        Task<bool> ForceUpdateAsync();

        /// <summary>
        /// Gets update history
        /// </summary>
        /// <returns>List of previous updates</returns>
        Task<UpdateHistory[]> GetUpdateHistoryAsync();
    }

    public enum UpdateStatus
    {
        Idle,
        CheckingForUpdates,
        UpdateAvailable,
        Downloading,
        Installing,
        RestartRequired,
        Failed,
        RollingBack,
        RollbackComplete
    }

    public class UpdateStatusChangedEventArgs : EventArgs
    {
        public UpdateStatus PreviousStatus { get; set; }
        public UpdateStatus CurrentStatus { get; set; }
        public string? Message { get; set; }
        public Exception? Error { get; set; }
    }

    public class UpdateProgressEventArgs : EventArgs
    {
        public double Progress { get; set; }
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }
        public string? CurrentOperation { get; set; }
    }

    public class UpdateHistory
    {
        public Version Version { get; set; } = new();
        public DateTime UpdatedAt { get; set; }
        public string? ReleaseNotes { get; set; }
        public bool WasRolledBack { get; set; }
        public string? UpdateSource { get; set; }
    }

    public class UpdateInfo
    {
        public Version Version { get; set; } = new();
        public string DownloadUrl { get; set; } = string.Empty;
        public string? ReleaseNotes { get; set; }
        public DateTime ReleasedAt { get; set; }
        public long FileSize { get; set; }
        public string? Checksum { get; set; }
        public Version MinimumRequiredVersion { get; set; } = new();
        public bool IsSecurityUpdate { get; set; }
        public bool IsMandatory { get; set; }
    }
} 