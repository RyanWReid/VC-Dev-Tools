using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Windows;
using System.Linq;
using System.Collections.Generic;
using System.Timers;
using Timer = System.Timers.Timer;

namespace VCDevTool.Client.ViewModels
{
    public enum FileChangeType
    {
        None,
        Added,
        Modified,
        Removed,
        Missing,
        Renamed
    }

    public class FileChange : INotifyPropertyChanged
    {
        private string _filePath;
        private FileChangeType _changeType;
        private string _oldName;
        private string _fileTypeIcon;
        private string _displayName;

        public string FilePath 
        { 
            get => _filePath; 
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                    UpdateDisplayName();
                    UpdateFileTypeIcon();
                }
            }
        }

        public FileChangeType ChangeType 
        { 
            get => _changeType;
            set
            {
                if (_changeType != value)
                {
                    _changeType = value;
                    OnPropertyChanged(nameof(ChangeType));
                }
            }
        }
        
        public string OldName
        {
            get => _oldName;
            set
            {
                if (_oldName != value)
                {
                    _oldName = value;
                    OnPropertyChanged(nameof(OldName));
                }
            }
        }
        
        public string FileTypeIcon
        {
            get => _fileTypeIcon;
            private set
            {
                if (_fileTypeIcon != value)
                {
                    _fileTypeIcon = value;
                    OnPropertyChanged(nameof(FileTypeIcon));
                }
            }
        }
        
        public string DisplayName
        {
            get => _displayName;
            private set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }
        
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }

        public FileChange(string filePath, FileChangeType changeType)
        {
            FilePath = filePath;
            ChangeType = changeType;
            
            // Initialize with default values
            OldName = string.Empty;
            FileTypeIcon = "📄"; // Default icon
            
            if (File.Exists(filePath))
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    LastModified = fileInfo.LastWriteTime;
                    FileSize = fileInfo.Length;
                }
                catch
                {
                    LastModified = DateTime.MinValue;
                    FileSize = 0;
                }
            }
            else
            {
                LastModified = DateTime.MinValue;
                FileSize = 0;
            }
        }
        
        public FileChange(string filePath, FileChangeType changeType, string oldName) : this(filePath, changeType)
        {
            OldName = oldName ?? string.Empty;
        }
        
        private void UpdateDisplayName()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                DisplayName = string.Empty;
                return;
            }
            
            DisplayName = Path.GetFileName(FilePath);
        }
        
        private void UpdateFileTypeIcon()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                FileTypeIcon = "📄";
                return;
            }
            
            string extension = Path.GetExtension(FilePath).ToLowerInvariant();
            
            // Assign appropriate icon based on file extension
            switch (extension)
            {
                case ".txt":
                case ".md":
                case ".rtf":
                    FileTypeIcon = "📝"; // Text files
                    break;
                case ".doc":
                case ".docx":
                    FileTypeIcon = "📄"; // Word documents
                    break;
                case ".xls":
                case ".xlsx":
                case ".csv":
                    FileTypeIcon = "📊"; // Spreadsheets
                    break;
                case ".ppt":
                case ".pptx":
                    FileTypeIcon = "📊"; // Presentations
                    break;
                case ".pdf":
                    FileTypeIcon = "📕"; // PDF documents
                    break;
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".gif":
                case ".tif":
                case ".tiff":
                    FileTypeIcon = "🖼️"; // Images
                    break;
                case ".mp3":
                case ".wav":
                case ".ogg":
                case ".flac":
                    FileTypeIcon = "🎵"; // Audio files
                    break;
                case ".mp4":
                case ".mov":
                case ".avi":
                case ".wmv":
                case ".mkv":
                    FileTypeIcon = "🎬"; // Video files
                    break;
                case ".zip":
                case ".rar":
                case ".7z":
                case ".tar":
                case ".gz":
                    FileTypeIcon = "🗜️"; // Compressed files
                    break;
                case ".exe":
                case ".msi":
                case ".bat":
                    FileTypeIcon = "⚙️"; // Executable files
                    break;
                case ".html":
                case ".htm":
                case ".xml":
                case ".css":
                case ".js":
                    FileTypeIcon = "🌐"; // Web files
                    break;
                case ".c":
                case ".cpp":
                case ".cs":
                case ".java":
                case ".py":
                case ".php":
                case ".rb":
                case ".ts":
                    FileTypeIcon = "💻"; // Code files
                    break;
                default:
                    if (Directory.Exists(FilePath))
                        FileTypeIcon = "📁"; // Folder
                    else
                        FileTypeIcon = "📄"; // Default file icon
                    break;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DirectorySyncPair : INotifyPropertyChanged
    {
        private string _sourceDirectory = string.Empty;
        private string _targetDirectory = string.Empty;
        private int _addedCount;
        private int _modifiedCount;
        private int _removedCount;
        private int _missingCount;
        private DateTime _lastScanTime = DateTime.MinValue;
        private List<FileChange> _pendingChanges = new List<FileChange>();
        private bool _isAsyncEnabled;

        public string SourceDirectory
        {
            get => _sourceDirectory;
            set
            {
                if (_sourceDirectory != value)
                {
                    _sourceDirectory = value;
                    OnPropertyChanged(nameof(SourceDirectory));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        public string TargetDirectory
        {
            get => _targetDirectory;
            set
            {
                if (_targetDirectory != value)
                {
                    _targetDirectory = value;
                    OnPropertyChanged(nameof(TargetDirectory));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        public int AddedCount
        {
            get => _addedCount;
            set
            {
                if (_addedCount != value)
                {
                    _addedCount = value;
                    OnPropertyChanged(nameof(AddedCount));
                    OnPropertyChanged(nameof(TotalChangeCount));
                    OnPropertyChanged(nameof(HasChanges));
                    OnPropertyChanged(nameof(ChangeCountText));
                }
            }
        }

        public int ModifiedCount
        {
            get => _modifiedCount;
            set
            {
                if (_modifiedCount != value)
                {
                    _modifiedCount = value;
                    OnPropertyChanged(nameof(ModifiedCount));
                    OnPropertyChanged(nameof(TotalChangeCount));
                    OnPropertyChanged(nameof(HasChanges));
                    OnPropertyChanged(nameof(ChangeCountText));
                }
            }
        }

        public int RemovedCount
        {
            get => _removedCount;
            set
            {
                if (_removedCount != value)
                {
                    _removedCount = value;
                    OnPropertyChanged(nameof(RemovedCount));
                    OnPropertyChanged(nameof(TotalChangeCount));
                    OnPropertyChanged(nameof(HasChanges));
                    OnPropertyChanged(nameof(ChangeCountText));
                }
            }
        }

        public int MissingCount
        {
            get => _missingCount;
            set
            {
                if (_missingCount != value)
                {
                    _missingCount = value;
                    OnPropertyChanged(nameof(MissingCount));
                    OnPropertyChanged(nameof(TotalChangeCount));
                    OnPropertyChanged(nameof(HasChanges));
                    OnPropertyChanged(nameof(ChangeCountText));
                }
            }
        }

        public int TotalChangeCount => AddedCount + ModifiedCount + RemovedCount + MissingCount;

        public DateTime LastScanTime
        {
            get => _lastScanTime;
            set
            {
                if (_lastScanTime != value)
                {
                    _lastScanTime = value;
                    OnPropertyChanged(nameof(LastScanTime));
                }
            }
        }

        public List<FileChange> PendingChanges
        {
            get => _pendingChanges;
            set
            {
                _pendingChanges = value;
                OnPropertyChanged(nameof(PendingChanges));
            }
        }

        public bool HasChanges => TotalChangeCount > 0;

        public string ChangeCountText
        {
            get
            {
                if (TotalChangeCount == 0)
                    return "No changes";

                List<string> changes = new List<string>();
                
                if (AddedCount > 0)
                    changes.Add($"{AddedCount} added");
                
                if (ModifiedCount > 0)
                    changes.Add($"{ModifiedCount} modified");
                
                if (RemovedCount > 0)
                    changes.Add($"{RemovedCount} removed");
                
                if (MissingCount > 0)
                    changes.Add($"{MissingCount} missing");
                
                return string.Join(", ", changes);
            }
        }

        public int DisplayChangeCount => TotalChangeCount;

        public string SourceDirectoryName
        {
            get
            {
                if (string.IsNullOrEmpty(SourceDirectory))
                    return string.Empty;
                
                try
                {
                    return Path.GetFileName(SourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        public bool IsValid => !string.IsNullOrEmpty(SourceDirectory) && 
                              !string.IsNullOrEmpty(TargetDirectory) &&
                              Directory.Exists(SourceDirectory) &&
                              Directory.Exists(TargetDirectory);

        public bool IsAsyncEnabled
        {
            get => _isAsyncEnabled;
            set
            {
                if (_isAsyncEnabled != value)
                {
                    // If enabling async mode, confirm with user first
                    if (value)
                    {
                        var result = System.Windows.MessageBox.Show(
                            "Enabling async mode will automatically sync files without confirmation. " +
                            "Files may be automatically added, modified, or deleted.\n\n" +
                            "Are you sure you want to enable async sync?",
                            "Confirm Async Mode",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        
                        if (result != MessageBoxResult.Yes)
                            return; // Don't enable if user says no
                    }
                    
                    _isAsyncEnabled = value;
                    OnPropertyChanged(nameof(IsAsyncEnabled));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class WatcherViewModel : INotifyPropertyChanged
    {
        private string _sourceDirectory = string.Empty;
        private string _targetDirectory = string.Empty;
        private DirectorySyncPair? _selectedSyncPair;
        private bool _includeSubdirectories;
        private bool _isSyncing;
        private Timer _changeDetectionTimer;
        private bool _hasUnsynced = false;

        // Add event for successful pair addition
        public event EventHandler? SyncPairAdded;
        public event EventHandler? UnsyncedChangesChanged;

        public WatcherViewModel()
        {
            SyncPairs = new ObservableCollection<DirectorySyncPair>();
            
            AddSyncPairCommand = new RelayCommand(_ => AddSyncPair(), _ => CanAddSyncPair);
            RemoveSyncPairCommand = new RelayCommand(pair => RemoveSyncPair((DirectorySyncPair)pair));
            SyncDirectoriesCommand = new RelayCommand(pair => SyncDirectories((DirectorySyncPair)pair), _ => !IsSyncing);
            BrowseSourceCommand = new RelayCommand(_ => BrowseForDirectory(true));
            BrowseTargetCommand = new RelayCommand(_ => BrowseForDirectory(false));
            
            // Set up timer to check for changes
            _changeDetectionTimer = new Timer(60000); // Check every minute
            _changeDetectionTimer.Elapsed += OnChangeDetectionTimerElapsed;
            _changeDetectionTimer.AutoReset = true;
            _changeDetectionTimer.Start();
        }

        public ObservableCollection<DirectorySyncPair> SyncPairs { get; }

        public string SourceDirectory
        {
            get => _sourceDirectory;
            set
            {
                if (_sourceDirectory != value)
                {
                    _sourceDirectory = value;
                    OnPropertyChanged(nameof(SourceDirectory));
                    OnPropertyChanged(nameof(CanAddSyncPair));
                }
            }
        }

        public string TargetDirectory
        {
            get => _targetDirectory;
            set
            {
                if (_targetDirectory != value)
                {
                    _targetDirectory = value;
                    OnPropertyChanged(nameof(TargetDirectory));
                    OnPropertyChanged(nameof(CanAddSyncPair));
                }
            }
        }

        public DirectorySyncPair? SelectedSyncPair
        {
            get => _selectedSyncPair;
            set
            {
                if (_selectedSyncPair != value)
                {
                    _selectedSyncPair = value;
                    OnPropertyChanged(nameof(SelectedSyncPair));
                }
            }
        }

        public bool IncludeSubdirectories
        {
            get => _includeSubdirectories;
            set
            {
                if (_includeSubdirectories != value)
                {
                    _includeSubdirectories = value;
                    OnPropertyChanged(nameof(IncludeSubdirectories));
                }
            }
        }

        public bool IsSyncing
        {
            get => _isSyncing;
            private set
            {
                if (_isSyncing != value)
                {
                    _isSyncing = value;
                    OnPropertyChanged(nameof(IsSyncing));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }
        
        public bool HasUnsyncedChanges
        {
            get => _hasUnsynced;
            private set
            {
                if (_hasUnsynced != value)
                {
                    _hasUnsynced = value;
                    OnPropertyChanged(nameof(HasUnsyncedChanges));
                    UnsyncedChangesChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool CanAddSyncPair => 
            !string.IsNullOrWhiteSpace(SourceDirectory) && 
            !string.IsNullOrWhiteSpace(TargetDirectory) && 
            Directory.Exists(SourceDirectory) && 
            Directory.Exists(TargetDirectory) &&
            !SyncPairs.Any(p => p.SourceDirectory == SourceDirectory && p.TargetDirectory == TargetDirectory);

        public ICommand AddSyncPairCommand { get; }
        public ICommand RemoveSyncPairCommand { get; }
        public ICommand SyncDirectoriesCommand { get; }
        public ICommand BrowseSourceCommand { get; }
        public ICommand BrowseTargetCommand { get; }

        private void AddSyncPair()
        {
            if (!CanAddSyncPair)
                return;

            var syncPair = new DirectorySyncPair
            {
                SourceDirectory = SourceDirectory,
                TargetDirectory = TargetDirectory
            };

            SyncPairs.Add(syncPair);
            
            // Clear the input fields
            SourceDirectory = string.Empty;
            TargetDirectory = string.Empty;
            
            // Immediately check for changes
            syncPair.LastScanTime = DateTime.MinValue; // Ensure it's set to MinValue for initial scan
            CheckForChanges(syncPair);
            
            // Now set the LastScanTime to indicate this was the initial scan
            syncPair.LastScanTime = DateTime.Now;
            
            // Notify that a sync pair has been added
            SyncPairAdded?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveSyncPair(DirectorySyncPair syncPair)
        {
            if (syncPair != null && SyncPairs.Contains(syncPair))
            {
                SyncPairs.Remove(syncPair);
                UpdateHasUnsyncedChanges();
            }
        }

        private void SyncDirectories(DirectorySyncPair syncPair)
        {
            if (syncPair == null || !syncPair.IsValid || IsSyncing)
                return;

            try
            {
                IsSyncing = true;
                
                // Check if we need to confirm removal of files
                if (syncPair.RemovedCount > 0)
                {
                    MessageBoxResult result = System.Windows.MessageBox.Show(
                        $"This sync will remove {syncPair.RemovedCount} file(s) from the target directory. Do you want to proceed?",
                        "Confirm File Removal",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.No)
                    {
                        IsSyncing = false;
                        return;
                    }
                }
                
                // Create a new DirectoryInfo object for the source directory
                DirectoryInfo sourceDir = new DirectoryInfo(syncPair.SourceDirectory);
                DirectoryInfo targetDir = new DirectoryInfo(syncPair.TargetDirectory);
                
                // Create the target directory if it doesn't exist
                if (!Directory.Exists(syncPair.TargetDirectory))
                {
                    Directory.CreateDirectory(syncPair.TargetDirectory);
                }
                
                int successCount = 0;
                int errorCount = 0;
                List<string> errorMessages = new List<string>();
                
                // Process all pending changes
                foreach (var change in syncPair.PendingChanges)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(change.FilePath))
                            continue;
                        
                        string relativePath = GetRelativePath(change.FilePath, syncPair.SourceDirectory);
                        if (string.IsNullOrEmpty(relativePath))
                            continue;
                        
                        string targetPath = Path.Combine(syncPair.TargetDirectory, relativePath);
                        
                        switch (change.ChangeType)
                        {
                            case FileChangeType.Added:
                            case FileChangeType.Modified:
                                // Copy the file
                                if (File.Exists(change.FilePath))
                                {
                                    string targetDirectory = Path.GetDirectoryName(targetPath);
                                    if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
                                        Directory.CreateDirectory(targetDirectory);
                                    
                                    File.Copy(change.FilePath, targetPath, true);
                                    successCount++;
                                }
                                break;
                                
                            case FileChangeType.Removed:
                                // Delete file from target
                                if (File.Exists(targetPath))
                                {
                                    File.Delete(targetPath);
                                    successCount++;
                                }
                                break;
                                
                            case FileChangeType.Missing:
                                // Copy file back from target to source
                                if (File.Exists(targetPath))
                                {
                                    string sourceDirectory = Path.GetDirectoryName(change.FilePath);
                                    if (!string.IsNullOrEmpty(sourceDirectory) && !Directory.Exists(sourceDirectory))
                                        Directory.CreateDirectory(sourceDirectory);
                                    
                                    File.Copy(targetPath, change.FilePath, true);
                                    successCount++;
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        errorMessages.Add($"{change.ChangeType} error for {Path.GetFileName(change.FilePath)}: {ex.Message}");
                        
                        // Limit error messages to avoid huge message boxes
                        if (errorMessages.Count > 5)
                        {
                            errorMessages.Add("... and more errors (see application log for details)");
                            break;
                        }
                    }
                }
                
                // Reset change counts after sync
                syncPair.AddedCount = 0;
                syncPair.ModifiedCount = 0;
                syncPair.RemovedCount = 0;
                syncPair.MissingCount = 0;
                syncPair.PendingChanges.Clear();
                syncPair.LastScanTime = DateTime.Now;
                
                UpdateHasUnsyncedChanges();
                
                // Show success or error message
                if (errorCount == 0)
                {
                    System.Windows.MessageBox.Show($"Successfully synced {successCount} file(s).", 
                        "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Sync completed with {errorCount} error(s).\n\n{string.Join("\n", errorMessages)}", 
                        "Sync Partial Failure", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error syncing directories: {ex.Message}", "Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSyncing = false;
            }
        }

        private string GetRelativePath(string fullPath, string basePath)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(basePath))
            {
                // Return empty string if either path is null/empty
                return string.Empty;
            }

            try
            {
                // Ensure paths end with directory separator
                if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    basePath += Path.DirectorySeparatorChar;
                
                Uri baseUri = new Uri(basePath);
                Uri fullUri = new Uri(fullPath);
                
                Uri relativeUri = baseUri.MakeRelativeUri(fullUri);
                string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
                
                // Convert forward slashes to backslashes if on Windows
                return relativePath.Replace('/', Path.DirectorySeparatorChar);
            }
            catch (UriFormatException)
            {
                // Fallback method if URI creation fails (like with some UNC paths)
                if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar);
                }
                return Path.GetFileName(fullPath); // Just return the filename as last resort
            }
        }

        private void BrowseForDirectory(bool isSource)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = isSource ? "Select Source Directory" : "Select Target Directory",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (isSource)
                {
                    SourceDirectory = dialog.SelectedPath;
                }
                else
                {
                    TargetDirectory = dialog.SelectedPath;
                }
            }
        }
        
        private void OnChangeDetectionTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            // Check each pair for changes
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var pair in SyncPairs)
                {
                    CheckForChanges(pair);
                    
                    // If async is enabled and there are changes, sync automatically
                    if (pair.IsAsyncEnabled && pair.HasChanges)
                    {
                        SyncDirectoriesAsync(pair);
                    }
                }
                
                UpdateHasUnsyncedChanges();
            });
        }
        
        private void CheckForChanges(DirectorySyncPair pair)
        {
            if (!pair.IsValid)
                return;
                
            try
            {
                DirectoryInfo sourceDir = new DirectoryInfo(pair.SourceDirectory);
                DirectoryInfo targetDir = new DirectoryInfo(pair.TargetDirectory);
                
                // Clear existing pending changes
                List<FileChange> pendingChanges = new List<FileChange>();
                
                // Reset counters
                int addedCount = 0;
                int modifiedCount = 0;
                int removedCount = 0;
                int missingCount = 0;
                
                // 1. First check for added and modified files (files in source that need to be copied to target)
                CheckForAddedAndModifiedFiles(sourceDir, targetDir, pair.SourceDirectory, ref addedCount, ref modifiedCount, pendingChanges, IncludeSubdirectories);
                
                // 2. Check for removed files (files that exist in target but not in source - need to be deleted from target)
                CheckForRemovedFiles(sourceDir, targetDir, pair.SourceDirectory, pair.TargetDirectory, ref removedCount, pendingChanges, IncludeSubdirectories);
                
                // 3. Check for missing files (files missing from source but present in target - could be re-synced back)
                CheckForMissingFiles(sourceDir, targetDir, pair.SourceDirectory, pair.TargetDirectory, ref missingCount, pendingChanges, IncludeSubdirectories);
                
                // Detect renamed files
                DetectRenamedFiles(pair);
                
                // Update pair's change counts
                pair.AddedCount = addedCount;
                pair.ModifiedCount = modifiedCount;
                pair.RemovedCount = removedCount;
                pair.MissingCount = missingCount;
                pair.PendingChanges = pendingChanges;
                
                // Update LastScanTime if this is a full scan (not just the initial check)
                if (pair.LastScanTime != DateTime.MinValue)
                {
                    pair.LastScanTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                // Silent failure during automatic checks
                System.Diagnostics.Debug.WriteLine($"Error checking for changes: {ex}");
            }
        }
        
        private void CheckForAddedAndModifiedFiles(
            DirectoryInfo sourceDir, DirectoryInfo targetDir, string sourceDirPath,
            ref int addedCount, ref int modifiedCount, List<FileChange> pendingChanges, bool includeSubdirectories)
        {
            // Check each file in source
            foreach (FileInfo sourceFile in sourceDir.GetFiles())
            {
                string relativePath = GetRelativePath(sourceFile.FullName, sourceDirPath);
                string targetFilePath = Path.Combine(targetDir.FullName, relativePath);
                
                if (!File.Exists(targetFilePath))
                {
                    // File doesn't exist in target - need to add
                    addedCount++;
                    pendingChanges.Add(new FileChange(sourceFile.FullName, FileChangeType.Added));
                }
                else
                {
                    // File exists, check if it's different
                    FileInfo targetFile = new FileInfo(targetFilePath);
                    if (sourceFile.Length != targetFile.Length || 
                        sourceFile.LastWriteTime > targetFile.LastWriteTime)
                    {
                        // File modified
                        modifiedCount++;
                        pendingChanges.Add(new FileChange(sourceFile.FullName, FileChangeType.Modified));
                    }
                }
            }
            
            // Recursively check subdirectories
            if (includeSubdirectories)
            {
                foreach (DirectoryInfo sourceSubDir in sourceDir.GetDirectories())
                {
                    string targetSubDirPath = Path.Combine(targetDir.FullName, sourceSubDir.Name);
                    DirectoryInfo targetSubDir;
                    
                    if (!Directory.Exists(targetSubDirPath))
                    {
                        Directory.CreateDirectory(targetSubDirPath);
                        targetSubDir = new DirectoryInfo(targetSubDirPath);
                    }
                    else
                    {
                        targetSubDir = new DirectoryInfo(targetSubDirPath);
                    }
                    
                    CheckForAddedAndModifiedFiles(sourceSubDir, targetSubDir, sourceDirPath, 
                        ref addedCount, ref modifiedCount, pendingChanges, includeSubdirectories);
                }
            }
        }
        
        private void CheckForRemovedFiles(
            DirectoryInfo sourceDir, DirectoryInfo targetDir, string sourceDirPath, string targetDirPath,
            ref int removedCount, List<FileChange> pendingChanges, bool includeSubdirectories)
        {
            // Check for files in target that don't exist in source
            foreach (FileInfo targetFile in targetDir.GetFiles())
            {
                string relativePath = GetRelativePath(targetFile.FullName, targetDirPath);
                string sourceFilePath = Path.Combine(sourceDirPath, relativePath);
                
                if (!File.Exists(sourceFilePath))
                {
                    // File exists in target but not in source - should be removed
                    removedCount++;
                    pendingChanges.Add(new FileChange(targetFile.FullName, FileChangeType.Removed));
                }
            }
            
            // Recursively check subdirectories
            if (includeSubdirectories)
            {
                foreach (DirectoryInfo targetSubDir in targetDir.GetDirectories())
                {
                    string sourceSubDirPath = Path.Combine(sourceDir.FullName, targetSubDir.Name);
                    
                    if (Directory.Exists(sourceSubDirPath))
                    {
                        DirectoryInfo sourceSubDir = new DirectoryInfo(sourceSubDirPath);
                        CheckForRemovedFiles(sourceSubDir, targetSubDir, sourceDirPath, targetDirPath, 
                            ref removedCount, pendingChanges, includeSubdirectories);
                    }
                    else
                    {
                        // The entire directory is missing from source - all files in this target dir should be removed
                        CountAllFilesAsRemoved(targetSubDir, targetDirPath, ref removedCount, pendingChanges, includeSubdirectories);
                    }
                }
            }
        }
        
        private void CheckForMissingFiles(
            DirectoryInfo sourceDir, DirectoryInfo targetDir, string sourceDirPath, string targetDirPath,
            ref int missingCount, List<FileChange> pendingChanges, bool includeSubdirectories)
        {
            // Check target directory for files that could be copied back to source
            foreach (FileInfo targetFile in targetDir.GetFiles())
            {
                string relativePath = GetRelativePath(targetFile.FullName, targetDirPath);
                string sourceFilePath = Path.Combine(sourceDirPath, relativePath);
                
                if (!File.Exists(sourceFilePath))
                {
                    // File exists in target but is missing from source
                    missingCount++;
                    pendingChanges.Add(new FileChange(sourceFilePath, FileChangeType.Missing));
                }
            }
            
            // Recursively check subdirectories
            if (includeSubdirectories)
            {
                foreach (DirectoryInfo targetSubDir in targetDir.GetDirectories())
                {
                    string sourceSubDirPath = Path.Combine(sourceDir.FullName, targetSubDir.Name);
                    
                    if (!Directory.Exists(sourceSubDirPath))
                    {
                        try
                        {
                            // Create the source subdirectory if it doesn't exist
                            if (!string.IsNullOrEmpty(sourceSubDirPath))
                            {
                                Directory.CreateDirectory(sourceSubDirPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error creating directory {sourceSubDirPath}: {ex.Message}");
                            continue; // Skip this subdirectory if we can't create it
                        }
                    }
                    
                    DirectoryInfo sourceSubDir = new DirectoryInfo(sourceSubDirPath);
                    CheckForMissingFiles(sourceSubDir, targetSubDir, sourceDirPath, targetDirPath, 
                        ref missingCount, pendingChanges, includeSubdirectories);
                }
            }
        }
        
        private void CountAllFilesAsRemoved(
            DirectoryInfo dir, string targetDirPath, ref int removedCount, List<FileChange> pendingChanges, bool includeSubdirectories)
        {
            foreach (FileInfo file in dir.GetFiles())
            {
                removedCount++;
                pendingChanges.Add(new FileChange(file.FullName, FileChangeType.Removed));
            }
            
            if (includeSubdirectories)
            {
                foreach (DirectoryInfo subDir in dir.GetDirectories())
                {
                    CountAllFilesAsRemoved(subDir, targetDirPath, ref removedCount, pendingChanges, includeSubdirectories);
                }
            }
        }
        
        private void UpdateHasUnsyncedChanges()
        {
            HasUnsyncedChanges = SyncPairs.Any(p => p.HasChanges);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Class RelayCommand definition 
        private class RelayCommand : ICommand
        {
            private readonly Action<object> _execute;
            private readonly Predicate<object>? _canExecute;

            public RelayCommand(Action<object> execute, Predicate<object>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }

            public bool CanExecute(object? parameter)
            {
                return _canExecute == null || _canExecute(parameter!);
            }

            public void Execute(object? parameter)
            {
                _execute(parameter!);
            }
        }

        private void DetectRenamedFiles(DirectorySyncPair pair)
        {
            // First separate added and removed files
            var addedFiles = pair.PendingChanges.Where(c => c.ChangeType == FileChangeType.Added).ToList();
            var removedFiles = pair.PendingChanges.Where(c => c.ChangeType == FileChangeType.Removed).ToList();
            
            // For each removed file, check if there's a matching added file
            foreach (var removed in removedFiles.ToArray())
            {
                // Find potential matches based on file size and last modified date similarity
                var potentialMatches = addedFiles.Where(a => 
                    // Files with same size
                    (a.FileSize == removed.FileSize && a.FileSize > 0) &&
                    // And similar last modified time (within 2 seconds)
                    Math.Abs((a.LastModified - removed.LastModified).TotalSeconds) < 2
                ).ToList();
                
                if (potentialMatches.Count == 1)
                {
                    var match = potentialMatches[0];
                    
                    // Create a new renamed file change
                    var renamedChange = new FileChange(match.FilePath, FileChangeType.Renamed, Path.GetFileName(removed.FilePath));
                    renamedChange.LastModified = match.LastModified;
                    renamedChange.FileSize = match.FileSize;
                    
                    // Remove the added and removed changes
                    pair.PendingChanges.Remove(match);
                    pair.PendingChanges.Remove(removed);
                    addedFiles.Remove(match);
                    removedFiles.Remove(removed);
                    
                    // Add the renamed change
                    pair.PendingChanges.Add(renamedChange);
                    
                    // Adjust counts
                    pair.AddedCount--;
                    pair.RemovedCount--;
                    // We don't have a RenamedCount, so we increment ModifiedCount instead
                    pair.ModifiedCount++;
                }
            }
        }

        private void SyncDirectoriesAsync(DirectorySyncPair syncPair)
        {
            if (syncPair == null || !syncPair.IsValid || IsSyncing)
                return;

            try
            {
                IsSyncing = true;
                
                // For async mode, we don't prompt for confirmation
                
                DirectoryInfo sourceDir = new DirectoryInfo(syncPair.SourceDirectory);
                DirectoryInfo targetDir = new DirectoryInfo(syncPair.TargetDirectory);
                
                // Create the target directory if it doesn't exist
                if (!Directory.Exists(syncPair.TargetDirectory))
                {
                    Directory.CreateDirectory(syncPair.TargetDirectory);
                }
                
                int successCount = 0;
                int errorCount = 0;
                
                // Process all pending changes
                foreach (var change in syncPair.PendingChanges)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(change.FilePath))
                            continue;
                        
                        string relativePath = GetRelativePath(change.FilePath, syncPair.SourceDirectory);
                        if (string.IsNullOrEmpty(relativePath))
                            continue;
                        
                        string targetPath = Path.Combine(syncPair.TargetDirectory, relativePath);
                        
                        switch (change.ChangeType)
                        {
                            case FileChangeType.Added:
                            case FileChangeType.Modified:
                                // Copy the file
                                if (File.Exists(change.FilePath))
                                {
                                    string targetDirectory = Path.GetDirectoryName(targetPath);
                                    if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
                                        Directory.CreateDirectory(targetDirectory);
                                    
                                    File.Copy(change.FilePath, targetPath, true);
                                    successCount++;
                                }
                                break;
                                
                            case FileChangeType.Removed:
                                // Delete file from target
                                if (File.Exists(targetPath))
                                {
                                    File.Delete(targetPath);
                                    successCount++;
                                }
                                break;
                                
                            case FileChangeType.Missing:
                                // Copy file back from target to source
                                if (File.Exists(targetPath))
                                {
                                    string sourceDirectory = Path.GetDirectoryName(change.FilePath);
                                    if (!string.IsNullOrEmpty(sourceDirectory) && !Directory.Exists(sourceDirectory))
                                        Directory.CreateDirectory(sourceDirectory);
                                    
                                    File.Copy(targetPath, change.FilePath, true);
                                    successCount++;
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        System.Diagnostics.Debug.WriteLine($"Async sync error: {ex.Message}");
                    }
                }
                
                // Reset change counts after sync
                syncPair.AddedCount = 0;
                syncPair.ModifiedCount = 0;
                syncPair.RemovedCount = 0;
                syncPair.MissingCount = 0;
                syncPair.PendingChanges.Clear();
                syncPair.LastScanTime = DateTime.Now;
                
                UpdateHasUnsyncedChanges();
                
                // No user notifications in async mode
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in async sync: {ex.Message}");
            }
            finally
            {
                IsSyncing = false;
            }
        }
    }
} 