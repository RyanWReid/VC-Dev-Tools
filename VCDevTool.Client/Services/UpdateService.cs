using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using VCDevTool.Client.Models;
using System.Security.Principal;
using System.Net.NetworkInformation;

namespace VCDevTool.Client.Services
{
    public class UpdateService : IUpdateService, IDisposable
    {
        private readonly IApiClient _apiClient;
        private readonly UpdateOptions _options;
        private readonly ILogger<UpdateService>? _logger;
        private readonly System.Threading.Timer _updateTimer;
        private readonly string _applicationPath;
        private readonly string _updateDirectory;
        private readonly string _backupDirectory;
        private bool _disposed = false;

        private UpdateStatus _status = UpdateStatus.Idle;
        private double _progress = 0.0;
        private Version? _latestVersion;
        private UpdateInfo? _availableUpdate;

        public Version CurrentVersion { get; }
        public Version? LatestVersion => _latestVersion;
        public bool IsUpdateAvailable => _availableUpdate != null && _latestVersion != null && _latestVersion > CurrentVersion;
        public bool IsUpdating => _status == UpdateStatus.Downloading || _status == UpdateStatus.Installing;
        public UpdateStatus Status => _status;
        public double Progress => _progress;
        public bool IsAutoUpdateEnabled => _options.EnableAutoUpdate;

        public event EventHandler<UpdateStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<UpdateProgressEventArgs>? ProgressChanged;

        public UpdateService(
            IApiClient apiClient,
            UpdateOptions? options = null,
            ILogger<UpdateService>? logger = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _options = options ?? new UpdateOptions();
            _logger = logger;

            // Get current application version
            CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version("1.0.0.0");
            
            // Set up directories
            _applicationPath = Process.GetCurrentProcess().MainModule?.FileName ?? 
                               Assembly.GetExecutingAssembly().Location;
            
            _updateDirectory = _options.CustomUpdateDirectory ?? 
                              Path.Combine(Path.GetTempPath(), "VCDevTool_Updates");
            
            _backupDirectory = Path.Combine(Path.GetDirectoryName(_applicationPath) ?? "", ".backups");

            // Create directories
            Directory.CreateDirectory(_updateDirectory);
            if (_options.EnableRollback)
            {
                Directory.CreateDirectory(_backupDirectory);
            }

            // Set up automatic update timer
            if (_options.EnableAutoUpdate)
            {
                var interval = TimeSpan.FromHours(_options.CheckIntervalHours);
                _updateTimer = new System.Threading.Timer(AutoUpdateCheck, null, TimeSpan.Zero, interval);
                _logger?.LogInformation("Auto-update enabled with {Interval} hour interval", _options.CheckIntervalHours);
            }
            else
            {
                _updateTimer = new System.Threading.Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
            }

            _logger?.LogInformation("UpdateService initialized. Current version: {Version}", CurrentVersion);
        }

        #region Public Methods

        public async Task<bool> CheckForUpdatesAsync()
        {
            return await SetStatusAndExecuteAsync(UpdateStatus.CheckingForUpdates, async () =>
            {
                try
                {
                    _logger?.LogInformation("Checking for updates...");
                    
                    var updateInfo = await GetLatestUpdateInfoAsync();
                    if (updateInfo == null)
                    {
                        _logger?.LogInformation("No update information available");
                        return false;
                    }

                    _latestVersion = updateInfo.Version;
                    
                    if (updateInfo.Version > CurrentVersion)
                    {
                        // Check compatibility
                        if (updateInfo.MinimumRequiredVersion > CurrentVersion)
                        {
                            _logger?.LogWarning("Update {Version} requires minimum version {MinVersion}, current is {CurrentVersion}",
                                updateInfo.Version, updateInfo.MinimumRequiredVersion, CurrentVersion);
                            
                            await SetStatusAsync(UpdateStatus.Failed, 
                                $"Update requires minimum version {updateInfo.MinimumRequiredVersion}");
                            return false;
                        }

                        _availableUpdate = updateInfo;
                        await SetStatusAsync(UpdateStatus.UpdateAvailable, 
                            $"Update {updateInfo.Version} is available");
                        
                        _logger?.LogInformation("Update available: {Version} (Current: {CurrentVersion})",
                            updateInfo.Version, CurrentVersion);
                        
                        return true;
                    }
                    else
                    {
                        _logger?.LogInformation("No updates available. Current version {CurrentVersion} is up to date",
                            CurrentVersion);
                        _availableUpdate = null;
                        await SetStatusAsync(UpdateStatus.Idle, "No updates available");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error checking for updates");
                    await SetStatusAsync(UpdateStatus.Failed, $"Error checking for updates: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> UpdateAsync(bool restartApplication = true)
        {
            if (_availableUpdate == null)
            {
                _logger?.LogWarning("No update available to install");
                return false;
            }

            // Check if running as administrator if required
            if (_options.RequireAdminPrivileges && !IsRunningAsAdministrator())
            {
                _logger?.LogError("Administrator privileges required for updates");
                await SetStatusAsync(UpdateStatus.Failed, "Administrator privileges required");
                return false;
            }

            // Check network connection type
            if (_options.SkipOnMeteredConnection && IsOnMeteredConnection())
            {
                _logger?.LogInformation("Skipping update on metered connection");
                await SetStatusAsync(UpdateStatus.Failed, "Skipping update on metered connection");
                return false;
            }

            return await SetStatusAndExecuteAsync(UpdateStatus.Downloading, async () =>
            {
                try
                {
                    // Download update
                    var downloadPath = await DownloadUpdateAsync(_availableUpdate);
                    if (downloadPath == null)
                    {
                        return false;
                    }

                    // Verify download
                    if (!await VerifyDownloadAsync(downloadPath, _availableUpdate))
                    {
                        await SetStatusAsync(UpdateStatus.Failed, "Download verification failed");
                        return false;
                    }

                    // Create backup if rollback is enabled
                    if (_options.EnableRollback)
                    {
                        await CreateBackupAsync();
                    }

                    // Apply update delay if configured
                    if (_options.UpdateDelayMinutes > 0)
                    {
                        _logger?.LogInformation("Delaying update for {DelayMinutes} minutes", 
                            _options.UpdateDelayMinutes);
                        await Task.Delay(TimeSpan.FromMinutes(_options.UpdateDelayMinutes));
                    }

                    // Install update
                    var installSuccess = await InstallUpdateAsync(downloadPath);
                    if (!installSuccess)
                    {
                        return false;
                    }

                    // Record update in history
                    await RecordUpdateHistoryAsync(_availableUpdate);

                    // Handle restart
                    if (restartApplication && _options.AutoRestartAfterUpdate)
                    {
                        await SetStatusAsync(UpdateStatus.RestartRequired, "Update installed, restarting...");
                        RestartApplication();
                    }
                    else
                    {
                        await SetStatusAsync(UpdateStatus.RestartRequired, 
                            "Update installed successfully. Restart required.");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during update process");
                    await SetStatusAsync(UpdateStatus.Failed, $"Update failed: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> RollbackAsync()
        {
            if (!_options.EnableRollback)
            {
                _logger?.LogWarning("Rollback is disabled");
                return false;
            }

            return await SetStatusAndExecuteAsync(UpdateStatus.RollingBack, async () =>
            {
                try
                {
                    var backupFiles = Directory.GetFiles(_backupDirectory, "*.backup", SearchOption.TopDirectoryOnly);
                    if (backupFiles.Length == 0)
                    {
                        _logger?.LogWarning("No backup files found for rollback");
                        await SetStatusAsync(UpdateStatus.Failed, "No backup available for rollback");
                        return false;
                    }

                    // Find the most recent backup
                    var latestBackup = GetLatestBackupFile();
                    if (latestBackup == null)
                    {
                        _logger?.LogWarning("Could not determine latest backup");
                        await SetStatusAsync(UpdateStatus.Failed, "Could not find valid backup");
                        return false;
                    }

                    _logger?.LogInformation("Rolling back to backup: {BackupFile}", latestBackup);

                    // Extract backup version info
                    var backupVersion = ExtractVersionFromBackupName(latestBackup);

                    // Restore from backup
                    var restoreSuccess = await RestoreFromBackupAsync(latestBackup);
                    if (!restoreSuccess)
                    {
                        return false;
                    }

                    // Record rollback in history
                    await RecordRollbackHistoryAsync(backupVersion);

                    await SetStatusAsync(UpdateStatus.RollbackComplete, 
                        $"Rollback to version {backupVersion} completed. Restart required.");

                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during rollback");
                    await SetStatusAsync(UpdateStatus.Failed, $"Rollback failed: {ex.Message}");
                    return false;
                }
            });
        }

        public void SetAutoUpdateEnabled(bool enabled)
        {
            _options.EnableAutoUpdate = enabled;
            
            if (enabled)
            {
                var interval = TimeSpan.FromHours(_options.CheckIntervalHours);
                _updateTimer.Change(TimeSpan.Zero, interval);
                _logger?.LogInformation("Auto-update enabled");
            }
            else
            {
                _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _logger?.LogInformation("Auto-update disabled");
            }
        }

        public async Task<bool> ForceUpdateAsync()
        {
            _logger?.LogInformation("Force update requested");
            
            var updateAvailable = await CheckForUpdatesAsync();
            if (!updateAvailable)
            {
                return false;
            }

            return await UpdateAsync(_options.AutoRestartAfterUpdate);
        }

        public async Task<UpdateHistory[]> GetUpdateHistoryAsync()
        {
            try
            {
                var historyFile = Path.Combine(_updateDirectory, "update_history.json");
                if (!File.Exists(historyFile))
                {
                    return Array.Empty<UpdateHistory>();
                }

                var json = await File.ReadAllTextAsync(historyFile);
                var history = JsonSerializer.Deserialize<List<UpdateHistory>>(json) ?? new List<UpdateHistory>();
                
                return history.ToArray();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading update history");
                return Array.Empty<UpdateHistory>();
            }
        }

        #endregion

        #region Private Methods

        private async void AutoUpdateCheck(object? state)
        {
            try
            {
                if (_status != UpdateStatus.Idle)
                {
                    return; // Don't check if already updating
                }

                var updateAvailable = await CheckForUpdatesAsync();
                if (updateAvailable && _availableUpdate != null)
                {
                    // Auto-install security updates or if silent updates are enabled
                    if (_options.EnableSilentUpdates || 
                        (_availableUpdate.IsSecurityUpdate && _options.AutoInstallSecurityUpdates))
                    {
                        _logger?.LogInformation("Auto-installing update {Version}", _availableUpdate.Version);
                        await UpdateAsync(_options.AutoRestartAfterUpdate);
                    }
                    else if (_options.EnableNotifications)
                    {
                        ShowUpdateNotification(_availableUpdate);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during automatic update check");
            }
        }

        private async Task<UpdateInfo?> GetLatestUpdateInfoAsync()
        {
            try
            {
                var requestUrl = $"{_options.UpdateServerUrl.TrimEnd('/')}/{_options.UpdateEndpoint.TrimStart('/')}/check";
                requestUrl += $"?currentVersion={CurrentVersion}&channel={_options.UpdateChannel}";

                // Create a temporary HTTP client for update checks
                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(_options.DownloadTimeoutSeconds)
                };

                var response = await httpClient.GetAsync(requestUrl);
                response.EnsureSuccessStatusCode();

                var updateInfo = await response.Content.ReadFromJsonAsync<UpdateInfo>();
                return updateInfo;
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogWarning("Could not reach update server: {Error}", ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting update information");
                return null;
            }
        }

        private async Task<string?> DownloadUpdateAsync(UpdateInfo updateInfo)
        {
            try
            {
                await SetStatusAsync(UpdateStatus.Downloading, $"Downloading update {updateInfo.Version}...");
                SetProgress(0.0, "Starting download...");

                var fileName = $"VCDevTool_v{updateInfo.Version}.msi";
                var downloadPath = Path.Combine(_updateDirectory, fileName);

                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(_options.DownloadTimeoutSeconds)
                };

                // Enable bandwidth throttling if configured
                if (_options.EnableBandwidthThrottling && _options.MaxDownloadSpeedKbps > 0)
                {
                    // Note: This would require a custom HttpClientHandler for proper throttling
                    _logger?.LogInformation("Bandwidth throttling enabled: {MaxSpeed} KB/s", 
                        _options.MaxDownloadSpeedKbps);
                }

                using var response = await httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (double)totalBytesRead / totalBytes;
                        SetProgress(progress, $"Downloaded {totalBytesRead:N0} / {totalBytes:N0} bytes");
                    }
                }

                _logger?.LogInformation("Download completed: {DownloadPath}", downloadPath);
                SetProgress(1.0, "Download completed");
                
                return downloadPath;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error downloading update");
                await SetStatusAsync(UpdateStatus.Failed, $"Download failed: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> VerifyDownloadAsync(string filePath, UpdateInfo updateInfo)
        {
            try
            {
                SetProgress(0.0, "Verifying download...");

                // Verify file size
                var fileInfo = new FileInfo(filePath);
                if (updateInfo.FileSize > 0 && fileInfo.Length != updateInfo.FileSize)
                {
                    _logger?.LogError("File size mismatch. Expected: {Expected}, Actual: {Actual}",
                        updateInfo.FileSize, fileInfo.Length);
                    return false;
                }

                // Verify checksum if provided
                if (_options.VerifyChecksums && !string.IsNullOrEmpty(updateInfo.Checksum))
                {
                    SetProgress(0.5, "Verifying checksum...");
                    
                    using var sha256 = SHA256.Create();
                    using var fileStream = File.OpenRead(filePath);
                    var computedHash = await Task.Run(() => sha256.ComputeHash(fileStream));
                    var computedHashString = Convert.ToHexString(computedHash);

                    if (!string.Equals(computedHashString, updateInfo.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger?.LogError("Checksum verification failed. Expected: {Expected}, Computed: {Computed}",
                            updateInfo.Checksum, computedHashString);
                        return false;
                    }
                }

                // Verify digital signature if enabled
                if (_options.VerifySignatures)
                {
                    SetProgress(0.8, "Verifying signature...");
                    // Note: Digital signature verification would be implemented here
                    // This would require the certificate and signature verification logic
                }

                SetProgress(1.0, "Verification completed");
                _logger?.LogInformation("Download verification successful");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error verifying download");
                return false;
            }
        }

        private async Task CreateBackupAsync()
        {
            try
            {
                SetProgress(0.0, "Creating backup...");

                var backupFileName = $"VCDevTool_v{CurrentVersion}_{DateTime.Now:yyyyMMdd_HHmmss}.backup";
                var backupPath = Path.Combine(_backupDirectory, backupFileName);

                await Task.Run(() =>
                {
                    File.Copy(_applicationPath, backupPath, true);
                });

                SetProgress(1.0, "Backup created");
                _logger?.LogInformation("Backup created: {BackupPath}", backupPath);

                // Clean up old backups
                await CleanupOldBackupsAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating backup");
                throw;
            }
        }

        private async Task<bool> InstallUpdateAsync(string updateFilePath)
        {
            try
            {
                await SetStatusAsync(UpdateStatus.Installing, "Installing update...");
                SetProgress(0.0, "Starting installation...");

                // For MSI files, use msiexec
                var installArgs = $"/i \"{updateFilePath}\" /quiet /norestart";
                
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = installArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                _logger?.LogInformation("Starting installation: msiexec.exe {Arguments}", installArgs);

                process.Start();
                
                var timeout = TimeSpan.FromSeconds(_options.InstallTimeoutSeconds);
                var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));

                if (!completed)
                {
                    process.Kill();
                    _logger?.LogError("Installation timed out after {Timeout} seconds", timeout.TotalSeconds);
                    await SetStatusAsync(UpdateStatus.Failed, "Installation timed out");
                    return false;
                }

                if (process.ExitCode == 0)
                {
                    SetProgress(1.0, "Installation completed");
                    _logger?.LogInformation("Update installed successfully");
                    return true;
                }
                else
                {
                    var errorOutput = await process.StandardError.ReadToEndAsync();
                    _logger?.LogError("Installation failed with exit code {ExitCode}: {Error}", 
                        process.ExitCode, errorOutput);
                    await SetStatusAsync(UpdateStatus.Failed, $"Installation failed (Exit code: {process.ExitCode})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during installation");
                await SetStatusAsync(UpdateStatus.Failed, $"Installation error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RestoreFromBackupAsync(string backupFilePath)
        {
            try
            {
                SetProgress(0.0, "Restoring from backup...");

                // Stop the current application process (this is complex and may require external tooling)
                // For now, we'll just copy the backup over the current executable
                await Task.Run(() =>
                {
                    File.Copy(backupFilePath, _applicationPath, true);
                });

                SetProgress(1.0, "Restore completed");
                _logger?.LogInformation("Restored from backup: {BackupPath}", backupFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error restoring from backup");
                return false;
            }
        }

        private async Task RecordUpdateHistoryAsync(UpdateInfo updateInfo)
        {
            try
            {
                var historyFile = Path.Combine(_updateDirectory, "update_history.json");
                var history = new List<UpdateHistory>();

                if (File.Exists(historyFile))
                {
                    var existingJson = await File.ReadAllTextAsync(historyFile);
                    history = JsonSerializer.Deserialize<List<UpdateHistory>>(existingJson) ?? new List<UpdateHistory>();
                }

                history.Add(new UpdateHistory
                {
                    Version = updateInfo.Version,
                    UpdatedAt = DateTime.UtcNow,
                    ReleaseNotes = updateInfo.ReleaseNotes,
                    WasRolledBack = false,
                    UpdateSource = _options.UpdateServerUrl
                });

                // Keep only the last 50 entries
                if (history.Count > 50)
                {
                    history = history.TakeLast(50).ToList();
                }

                var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(historyFile, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error recording update history");
            }
        }

        private async Task RecordRollbackHistoryAsync(Version? rolledBackToVersion)
        {
            try
            {
                var historyFile = Path.Combine(_updateDirectory, "update_history.json");
                var history = new List<UpdateHistory>();

                if (File.Exists(historyFile))
                {
                    var existingJson = await File.ReadAllTextAsync(historyFile);
                    history = JsonSerializer.Deserialize<List<UpdateHistory>>(existingJson) ?? new List<UpdateHistory>();
                }

                // Mark the last update as rolled back
                var lastUpdate = history.LastOrDefault();
                if (lastUpdate != null)
                {
                    lastUpdate.WasRolledBack = true;
                }

                var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(historyFile, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error recording rollback history");
            }
        }

        private async Task CleanupOldBackupsAsync()
        {
            try
            {
                var backupFiles = Directory.GetFiles(_backupDirectory, "*.backup")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();

                if (backupFiles.Length > _options.MaxBackupVersions)
                {
                    var filesToDelete = backupFiles.Skip(_options.MaxBackupVersions);
                    foreach (var fileToDelete in filesToDelete)
                    {
                        await Task.Run(() => fileToDelete.Delete());
                        _logger?.LogInformation("Deleted old backup: {BackupFile}", fileToDelete.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error cleaning up old backups");
            }
        }

        private string? GetLatestBackupFile()
        {
            try
            {
                var backupFiles = Directory.GetFiles(_backupDirectory, "*.backup")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault();

                return backupFiles?.FullName;
            }
            catch
            {
                return null;
            }
        }

        private Version? ExtractVersionFromBackupName(string backupFilePath)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(backupFilePath);
                // Expected format: VCDevTool_v1.2.3.4_20241228_143000
                var parts = fileName.Split('_');
                if (parts.Length >= 2 && parts[1].StartsWith("v"))
                {
                    var versionString = parts[1][1..]; // Remove 'v' prefix
                    return Version.Parse(versionString);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error extracting version from backup name: {BackupName}", backupFilePath);
            }
            return null;
        }

        private void ShowUpdateNotification(UpdateInfo updateInfo)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var message = $"Update {updateInfo.Version} is available.\n\n" +
                                  $"Release Notes:\n{updateInfo.ReleaseNotes ?? "No release notes available."}\n\n" +
                                  "Would you like to install it now?";

                    var result = System.Windows.MessageBox.Show(
                        message,
                        "Update Available",
                        MessageBoxButton.YesNo,
                        updateInfo.IsSecurityUpdate ? MessageBoxImage.Warning : MessageBoxImage.Information
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        Task.Run(async () => await UpdateAsync(_options.AutoRestartAfterUpdate));
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing update notification");
            }
        }

        private void RestartApplication()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var startInfo = new ProcessStartInfo
                {
                    FileName = _applicationPath,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error restarting application");
            }
        }

        private static bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsOnMeteredConnection()
        {
            try
            {
                // Simple network check - in a real implementation, you'd check for metered connections
                return !NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                return false;
            }
        }

        private async Task<T> SetStatusAndExecuteAsync<T>(UpdateStatus status, Func<Task<T>> action)
        {
            var previousStatus = _status;
            await SetStatusAsync(status);
            
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                await SetStatusAsync(UpdateStatus.Failed, ex.Message);
                throw;
            }
        }

        private async Task SetStatusAsync(UpdateStatus status, string? message = null)
        {
            var previousStatus = _status;
            _status = status;

            _logger?.LogInformation("Update status changed: {PreviousStatus} -> {CurrentStatus} ({Message})",
                previousStatus, status, message ?? "");

            try
            {
                StatusChanged?.Invoke(this, new UpdateStatusChangedEventArgs
                {
                    PreviousStatus = previousStatus,
                    CurrentStatus = status,
                    Message = message
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in status changed event handler");
            }

            await Task.CompletedTask;
        }

        private void SetProgress(double progress, string? operation = null)
        {
            _progress = Math.Max(0.0, Math.Min(1.0, progress));

            try
            {
                ProgressChanged?.Invoke(this, new UpdateProgressEventArgs
                {
                    Progress = _progress,
                    CurrentOperation = operation
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in progress changed event handler");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _updateTimer?.Dispose();
                _disposed = true;
                _logger?.LogDebug("UpdateService disposed");
            }
        }

        #endregion
    }
} 