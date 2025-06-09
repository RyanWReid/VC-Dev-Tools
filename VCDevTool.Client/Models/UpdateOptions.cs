using System;

namespace VCDevTool.Client.Models
{
    public class UpdateOptions
    {
        public const string SectionName = "Update";

        /// <summary>
        /// Enable automatic update checking
        /// </summary>
        public bool EnableAutoUpdate { get; set; } = true;

        /// <summary>
        /// Interval between automatic update checks in hours
        /// </summary>
        public int CheckIntervalHours { get; set; } = 24;

        /// <summary>
        /// Update server base URL
        /// </summary>
        public string UpdateServerUrl { get; set; } = "http://localhost:5289";

        /// <summary>
        /// Update endpoint path
        /// </summary>
        public string UpdateEndpoint { get; set; } = "api/updates";

        /// <summary>
        /// Download timeout in seconds
        /// </summary>
        public int DownloadTimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Install timeout in seconds
        /// </summary>
        public int InstallTimeoutSeconds { get; set; } = 600;

        /// <summary>
        /// Enable silent updates (no user interaction)
        /// </summary>
        public bool EnableSilentUpdates { get; set; } = false;

        /// <summary>
        /// Automatically install security updates
        /// </summary>
        public bool AutoInstallSecurityUpdates { get; set; } = true;

        /// <summary>
        /// Automatically restart after update installation
        /// </summary>
        public bool AutoRestartAfterUpdate { get; set; } = false;

        /// <summary>
        /// Enable rollback capability
        /// </summary>
        public bool EnableRollback { get; set; } = true;

        /// <summary>
        /// Maximum number of backup versions to keep
        /// </summary>
        public int MaxBackupVersions { get; set; } = 3;

        /// <summary>
        /// Verify digital signatures of updates
        /// </summary>
        public bool VerifySignatures { get; set; } = true;

        /// <summary>
        /// Verify checksums of downloaded files
        /// </summary>
        public bool VerifyChecksums { get; set; } = true;

        /// <summary>
        /// Update channel (Stable, Beta, Alpha)
        /// </summary>
        public string UpdateChannel { get; set; } = "Stable";

        /// <summary>
        /// Allow downgrades
        /// </summary>
        public bool AllowDowngrades { get; set; } = false;

        /// <summary>
        /// Require administrator privileges for updates
        /// </summary>
        public bool RequireAdminPrivileges { get; set; } = true;

        /// <summary>
        /// Custom update directory (if not specified, uses temp directory)
        /// </summary>
        public string? CustomUpdateDirectory { get; set; }

        /// <summary>
        /// Enable update notifications
        /// </summary>
        public bool EnableNotifications { get; set; } = true;

        /// <summary>
        /// Delay before applying updates in minutes
        /// </summary>
        public int UpdateDelayMinutes { get; set; } = 0;

        /// <summary>
        /// Enable bandwidth throttling for downloads
        /// </summary>
        public bool EnableBandwidthThrottling { get; set; } = false;

        /// <summary>
        /// Maximum download speed in KB/s (0 = unlimited)
        /// </summary>
        public int MaxDownloadSpeedKbps { get; set; } = 0;

        /// <summary>
        /// Skip update if on metered connection
        /// </summary>
        public bool SkipOnMeteredConnection { get; set; } = true;
    }
} 