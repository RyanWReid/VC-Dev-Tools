using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VCDevTool.Client.Services;
using VCDevTool.Shared;
using Forms = System.Windows.Forms;
using DialogResult = System.Windows.Forms.DialogResult;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace VCDevTool.Client.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private ApiClient _apiClient;
        private NodeService _nodeService;
        private TaskExecutionService _taskExecutionService;
        private AuthenticationService _authenticationService;
        private bool _isConnected;
        private bool _isLoading;
        private string? _statusMessage;
        private NodeViewModel? _selectedNode;
        private TaskType _selectedProcessType;
        private Process? _apiProcess;
        private bool _isNewProcessDialogOpen;
        private bool _isServerDialogOpen;
        private bool _isSettingsDialogOpen;
        private string _selectedDirectory = "";
        private List<string> _selectedDirectories = new List<string>();
        private bool _shouldDistributeTask;
        private ObservableCollection<DeviceSelectionViewModel> _availableDevices;
        private bool _isActivated;
        private string _messageText = "";
        private string _serverAddress = "http://localhost:5289";
        private string _connectionStatus = "";
        private System.Threading.Timer? _refreshTimer;
        private const int RefreshIntervalMs = 5000; // 5 seconds
        private bool _isNodeDetailViewVisible;
        private System.Windows.Forms.Keys _addFilesHotkey = System.Windows.Forms.Keys.Shift;
        private bool _isWaitingForKeyPress = false;
        private bool _isMaterialScannerOpen;
        private bool _isWatcherOpen;
        private readonly MaterialScannerService _materialScannerService;
        private string _scanCategoryPath = @"W:\PROJECTS\Scans";
        private bool _useFixedDimension = false;
        private bool _useSdDimension = false;
        private bool _useOverrideOutputDirectory = false;
        private string _outputDirectory = "";
        private bool _isDebugWindowVisible = false;
        private string _debugOutput = "";
        private bool _createOutputFolder = true; // Default to true
        private RelayCommand? _abortProcessCommand;
        private WatcherViewModel _watcher;
        private bool _isTaskDetailViewVisible;
        private TaskViewModel? _selectedTask;
        private RelayCommand? _abortTaskCommand;
        public ICommand AbortTaskCommand => _abortTaskCommand ??= new RelayCommand(_ => AbortTask(), _ => SelectedTask?.CanAbort == true);
        
        private bool CanAbortProcess() => SelectedNode?.HasActiveTask == true;
        
        private async void AbortProcess()
        {
            await AbortSelectedNodeProcessAsync();
        }

        // Property for controlling the add directory form popup
        private bool _isAddDirectoryFormOpen;
        public bool IsAddDirectoryFormOpen
        {
            get => _isAddDirectoryFormOpen;
            private set
            {
                if (_isAddDirectoryFormOpen != value)
                {
                    _isAddDirectoryFormOpen = value;
                    OnPropertyChanged(nameof(IsAddDirectoryFormOpen));
                }
            }
        }

        // Command to open the add directory form
        public ICommand OpenAddDirectoryFormCommand { get; }

        // Command to close the add directory form
        public ICommand CloseAddDirectoryFormCommand { get; }

        public ObservableCollection<NodeViewModel> Nodes { get; }
        public ObservableCollection<TaskViewModel> Tasks { get; }

        public MainViewModel()
        {
            // Create a shared authentication service
            _authenticationService = new AuthenticationService(_serverAddress);
            
            _apiClient = new ApiClient(_serverAddress, _authenticationService);
            _nodeService = new NodeService(_apiClient, _authenticationService);
            _taskExecutionService = new TaskExecutionService(_apiClient, _nodeService);
            
            // Subscribe to authentication changes
            _authenticationService.AuthenticationChanged += OnAuthenticationStatusChanged;
            
            // Initialize collections
            Nodes = new ObservableCollection<NodeViewModel>();
            Tasks = new ObservableCollection<TaskViewModel>();
            _availableDevices = new ObservableCollection<DeviceSelectionViewModel>();
            _materialCategories = new ObservableCollection<string>();
            
            // Initialize Watcher
            _watcher = new WatcherViewModel();
            _watcher.SyncPairAdded += OnSyncPairAdded;
            _watcher.UnsyncedChangesChanged += OnUnsyncedChangesChanged;
            
            // Only include TestMessage and PackageTask
            ProcessTypes = new List<TaskType> { TaskType.TestMessage, TaskType.PackageTask, TaskType.VolumeCompression, TaskType.RealityCapture };
            SelectedProcessType = TaskType.PackageTask;
            
            // Set activated by default
            _isActivated = true;
            
            // Initialize commands
            StartNewProcessCommand = new RelayCommand(_ => StartNewProcessAsync().ConfigureAwait(false), 
                                                     _ => CanStartProcess);
            OpenNewProcessDialogCommand = new RelayCommand(_ => {
                UpdateAvailableDevices();
                MessageText = ""; // Clear message text when opening dialog
                IsNewProcessDialogOpen = true;
            }, _ => IsConnected && !IsLoading);
            BrowseDirectoryCommand = new RelayCommand(_ => BrowseForDirectory());
            ToggleActivationCommand = new RelayCommand(_ => ToggleActivation());
            CancelNewProcessCommand = new RelayCommand(_ => IsNewProcessDialogOpen = false);
            OpenSettingsDialogCommand = new RelayCommand(_ => {
                IsSettingsDialogOpen = true;
                IsMaterialScannerOpen = false;
                IsWatcherOpen = false;
                IsNodeDetailViewVisible = false;
            });
            CancelSettingsDialogCommand = new RelayCommand(_ => IsSettingsDialogOpen = false);
            CloseSettingsCommand = new RelayCommand(_ => IsSettingsDialogOpen = false);
            ConfigureHotkeyCommand = new RelayCommand(_ => IsWaitingForKeyPress = true);
            ConnectToServerCommand = new RelayCommand(_ => {
                if (IsServerDialogOpen)
                    ConnectToServer();
                else if (IsSettingsDialogOpen)
                    ConnectToServer();
                else
                    IsSettingsDialogOpen = true;
            });
            CancelServerDialogCommand = new RelayCommand(_ => IsServerDialogOpen = false);
            OpenMaterialScannerCommand = new RelayCommand(_ => {
                IsMaterialScannerOpen = true;
                IsWatcherOpen = false;
                IsSettingsDialogOpen = false;
                IsNodeDetailViewVisible = false;
            });
            CloseMaterialScannerCommand = new RelayCommand(_ => IsMaterialScannerOpen = false);
            
            OpenWatcherCommand = new RelayCommand(_ => {
                IsWatcherOpen = true;
                IsMaterialScannerOpen = false;
                IsSettingsDialogOpen = false;
                IsNodeDetailViewVisible = false;
            });
            CloseWatcherCommand = new RelayCommand(_ => IsWatcherOpen = false);
            
            CloseAllViewsCommand = new RelayCommand(_ => {
                IsWatcherOpen = false;
                IsMaterialScannerOpen = false;
                IsSettingsDialogOpen = false;
                IsNodeDetailViewVisible = false;
            });
            
            // Auto-connect on startup
            Task.Run(async () => await InitializeAsync());
            
            // Handle application exit
            System.Windows.Application.Current.Exit += (s, e) => {
                ShutdownApiService();
                _refreshTimer?.Dispose();
            };

            // Set up property changed handler for the selected process type
            PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(SelectedProcessType) || 
                    e.PropertyName == nameof(MessageText) ||
                    e.PropertyName == nameof(AvailableDevices))
                {
                    OnPropertyChanged(nameof(IsTestMessageSelected));
                    OnPropertyChanged(nameof(IsFileProcessSelected));
                    OnPropertyChanged(nameof(CanStartProcess));
                }
            };

            ShowNodeDetailsCommand = new RelayCommand(node => {
                SelectedNode = (NodeViewModel)node;
                IsNodeDetailViewVisible = true;
            });
            
            CloseNodeDetailsCommand = new RelayCommand(_ => {
                IsNodeDetailViewVisible = false;
            });

            // Initialize services
            _materialScannerService = new MaterialScannerService();
            
            // Initialize Material Scanner options
            InitializeMaterialScannerOptions();
            MakeMaterialCommand = new RelayCommand(async _ => await MakeMaterial());
            CopyMaterialNameCommand = new RelayCommand(_ => CopyMaterialNameToClipboard());
            BrowseScanCategoryPathCommand = new RelayCommand(_ => BrowseForScanCategoryPath());

            ToggleDebugWindowCommand = new RelayCommand(_ => IsDebugWindowVisible = !IsDebugWindowVisible);
            ClearDebugOutputCommand = new RelayCommand(_ => DebugOutput = "");

            BrowseOutputDirectoryCommand = new RelayCommand(_ => BrowseForOutputDirectory());

            // Set up debug hub message handling
            var app = (App)System.Windows.Application.Current;
            if (app.DebugHubClient != null)
            {
                app.DebugHubClient.DebugMessageReceived += OnDebugMessageReceived;
            }

            // Add initialization to constructor
            OpenAddDirectoryFormCommand = new RelayCommand(_ => OpenAddDirectoryForm());
            CloseAddDirectoryFormCommand = new RelayCommand(_ => CloseAddDirectoryForm());

            ShowTaskDetailsCommand = new RelayCommand(ShowTaskDetails);
            CloseTaskDetailsCommand = new RelayCommand(_ => {
                IsTaskDetailViewVisible = false;
            });
        }

        

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<DeviceSelectionViewModel> AvailableDevices
        {
            get => _availableDevices;
            private set
            {
                _availableDevices = value;
                OnPropertyChanged(nameof(AvailableDevices));
            }
        }

        public List<TaskType> ProcessTypes { get; }
        
        public TaskType SelectedProcessType
        {
            get => _selectedProcessType;
            set
            {
                if (_selectedProcessType != value)
                {
                    _selectedProcessType = value;
                    OnPropertyChanged(nameof(SelectedProcessType));
                }
            }
        }
        
        public ICommand StartNewProcessCommand { get; }
        
        public ICommand OpenNewProcessDialogCommand { get; }
        
        public ICommand BrowseDirectoryCommand { get; }
        
        public ICommand ToggleActivationCommand { get; }

        public ICommand CancelNewProcessCommand { get; }

        public ICommand OpenSettingsDialogCommand { get; }

        public ICommand CancelSettingsDialogCommand { get; }

        public ICommand CloseSettingsCommand { get; }

        public ICommand ConfigureHotkeyCommand { get; }

        public ICommand ConnectToServerCommand { get; }

        public ICommand CancelServerDialogCommand { get; }

        public ICommand OpenMaterialScannerCommand { get; }
        
        public ICommand CloseMaterialScannerCommand { get; }

        public ICommand OpenWatcherCommand { get; }
        
        public ICommand CloseWatcherCommand { get; }

        public ICommand CloseAllViewsCommand { get; }

        public ICommand MakeMaterialCommand { get; }
        
        public ICommand CopyMaterialNameCommand { get; }

        public ICommand BrowseScanCategoryPathCommand { get; }

        public ICommand ToggleDebugWindowCommand { get; }

        public ICommand ClearDebugOutputCommand { get; }

        public ICommand BrowseOutputDirectoryCommand { get; }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged(nameof(IsConnected));
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                }
            }
        }
        
        public bool IsNewProcessDialogOpen
        {
            get => _isNewProcessDialogOpen;
            set
            {
                if (_isNewProcessDialogOpen != value)
                {
                    _isNewProcessDialogOpen = value;
                    OnPropertyChanged(nameof(IsNewProcessDialogOpen));
                    
                    // Update available devices when opening the dialog
                    if (value)
                    {
                        UpdateAvailableDevices();
                        
                        // Reset expansion states when opening the dialog
                        IsVolumeOptionsExpanded = false;
                        IsOutputOptionsExpanded = false;
                    }
                }
            }
        }
        
        public bool IsServerDialogOpen
        {
            get => _isServerDialogOpen;
            set
            {
                if (_isServerDialogOpen != value)
                {
                    _isServerDialogOpen = value;
                    OnPropertyChanged(nameof(IsServerDialogOpen));
                }
            }
        }
        
        public bool IsSettingsDialogOpen
        {
            get => _isSettingsDialogOpen;
            set
            {
                if (_isSettingsDialogOpen != value)
                {
                    _isSettingsDialogOpen = value;
                    OnPropertyChanged(nameof(IsSettingsDialogOpen));
                    
                    if (_isSettingsDialogOpen)
                    {
                        IsMaterialScannerOpen = false;
                    }
                }
            }
        }
        
        public string SelectedDirectory
        {
            get => _selectedDirectory;
            set
            {
                if (_selectedDirectory != value)
                {
                    _selectedDirectory = value;
                    OnPropertyChanged(nameof(SelectedDirectory));
                }
            }
        }
        
        public List<string> SelectedDirectories
        {
            get => _selectedDirectories;
            set
            {
                _selectedDirectories = value;
                // Update the display string
                SelectedDirectory = string.Join("; ", _selectedDirectories);
                OnPropertyChanged(nameof(SelectedDirectories));
            }
        }
        
        public bool ShouldDistributeTask
        {
            get => _shouldDistributeTask;
            set
            {
                if (_shouldDistributeTask != value)
                {
                    _shouldDistributeTask = value;
                    OnPropertyChanged(nameof(ShouldDistributeTask));
                    
                    // If distribution is turned on, update UI to show device selection
                    if (_shouldDistributeTask)
                    {
                        UpdateAvailableDevices();
                    }
                }
            }
        }

        public NodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode != value)
                {
                    // Deselect the old node
                    if (_selectedNode != null)
                    {
                        _selectedNode.IsSelected = false;
                    }

                    _selectedNode = value;

                    // Select the new node
                    if (_selectedNode != null)
                    {
                        _selectedNode.IsSelected = true;
                    }

                    OnPropertyChanged(nameof(SelectedNode));
                }
            }
        }

        public ComputerNode CurrentNode => _nodeService.CurrentNode;
        
        public bool IsActivated
        {
            get => _isActivated;
            set
            {
                if (_isActivated != value)
                {
                    _isActivated = value;
                    // Update node service to reflect activation state
                    _nodeService.SetNodeAvailability(_isActivated);
                    
                    // Find and update the current node's UI representation
                    UpdateCurrentNodeStatus();
                    
                    OnPropertyChanged(nameof(IsActivated));
                    OnPropertyChanged(nameof(ActivationStatus));
                }
            }
        }

        public string ActivationStatus => IsActivated ? "ACTIVATED" : "DEACTIVATED";

        public string MessageText
        {
            get => _messageText;
            set
            {
                if (_messageText != value)
                {
                    _messageText = value;
                    OnPropertyChanged(nameof(MessageText));
                }
            }
        }
        
        public System.Windows.Forms.Keys AddFilesHotkey
        {
            get => _addFilesHotkey;
            set
            {
                if (_addFilesHotkey != value)
                {
                    _addFilesHotkey = value;
                    OnPropertyChanged(nameof(AddFilesHotkey));
                    OnPropertyChanged(nameof(AddFilesHotkeyName));
                }
            }
        }
        
        public string AddFilesHotkeyName => AddFilesHotkey.ToString();
        
        public bool IsWaitingForKeyPress
        {
            get => _isWaitingForKeyPress;
            set
            {
                if (_isWaitingForKeyPress != value)
                {
                    _isWaitingForKeyPress = value;
                    OnPropertyChanged(nameof(IsWaitingForKeyPress));
                }
            }
        }

        public bool UseFixedDimension
        {
            get => _useFixedDimension;
            set
            {
                if (_useFixedDimension != value)
                {
                    _useFixedDimension = value;
                    if (value && _useSdDimension)
                    {
                        // Can't have both HD and SD at the same time
                        _useSdDimension = false;
                        OnPropertyChanged(nameof(UseSdDimension));
                    }
                    OnPropertyChanged(nameof(UseFixedDimension));
                    
                    // Update the SelectedDensityLevel to match
                    if (value)
                    {
                        _selectedDensityLevel = "HD";
                    }
                    else if (!_useSdDimension)
                    {
                        _selectedDensityLevel = "None";
                    }
                    OnPropertyChanged(nameof(SelectedDensityLevel));
                }
            }
        }

        public bool UseSdDimension
        {
            get => _useSdDimension;
            set
            {
                if (_useSdDimension != value)
                {
                    _useSdDimension = value;
                    if (value && _useFixedDimension)
                    {
                        // Can't have both HD and SD at the same time
                        _useFixedDimension = false;
                        OnPropertyChanged(nameof(UseFixedDimension));
                    }
                    OnPropertyChanged(nameof(UseSdDimension));
                    
                    // Update the SelectedDensityLevel to match
                    if (value)
                    {
                        _selectedDensityLevel = "SD";
                    }
                    else if (!_useFixedDimension)
                    {
                        _selectedDensityLevel = "None";
                    }
                    OnPropertyChanged(nameof(SelectedDensityLevel));
                }
            }
        }

        public bool UseOverrideOutputDirectory
        {
            get => _useOverrideOutputDirectory;
            set
            {
                if (_useOverrideOutputDirectory != value)
                {
                    _useOverrideOutputDirectory = value;
                    OnPropertyChanged(nameof(UseOverrideOutputDirectory));
                }
            }
        }

        public bool CreateOutputFolder
        {
            get => _createOutputFolder;
            set
            {
                if (_createOutputFolder != value)
                {
                    _createOutputFolder = value;
                    OnPropertyChanged(nameof(CreateOutputFolder));
                }
            }
        }

        public string OutputDirectory
        {
            get => _outputDirectory;
            set
            {
                if (_outputDirectory != value)
                {
                    _outputDirectory = value;
                    OnPropertyChanged(nameof(OutputDirectory));
                }
            }
        }

        public bool IsTestMessageSelected => SelectedProcessType == TaskType.TestMessage;

        public bool IsFileProcessSelected => SelectedProcessType == TaskType.FileProcessing ||
                                           SelectedProcessType == TaskType.RenderThumbnails || 
                                           SelectedProcessType == TaskType.RealityCapture ||
                                           SelectedProcessType == TaskType.PackageTask ||
                                           SelectedProcessType == TaskType.VolumeCompression;

        public bool CanStartProcess
        {
            get
            {
                // For TestMessage, we need message text and at least one selected device
                if (IsTestMessageSelected)
                {
                    return !string.IsNullOrWhiteSpace(MessageText) && 
                           AvailableDevices.Any(d => d.IsSelected);
                }
                
                // For file processes, we need a directory
                if (IsFileProcessSelected)
                {
                    return _selectedDirectories.Count > 0 && 
                           AvailableDevices.Any(d => d.IsSelected);
                }
                
                // For other processes, just need a selected device
                return AvailableDevices.Any(d => d.IsSelected);
            }
        }               

        public string ServerAddress
        {
            get => _serverAddress;
            set
            {
                if (_serverAddress != value)
                {
                    _serverAddress = value;
                    OnPropertyChanged(nameof(ServerAddress));
                }
            }
        }
        
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                if (_connectionStatus != value)
                {
                    _connectionStatus = value;
                    OnPropertyChanged(nameof(ConnectionStatus));
                }
            }
        }

        public bool IsNodeDetailViewVisible
        {
            get => _isNodeDetailViewVisible;
            set
            {
                if (_isNodeDetailViewVisible != value)
                {
                    _isNodeDetailViewVisible = value;
                    OnPropertyChanged(nameof(IsNodeDetailViewVisible));
                }
            }
        }

        public ICommand ShowNodeDetailsCommand { get; }
        
        public ICommand CloseNodeDetailsCommand { get; }

        public bool IsMaterialScannerOpen
        {
            get => _isMaterialScannerOpen;
            set
            {
                if (_isMaterialScannerOpen != value)
                {
                    _isMaterialScannerOpen = value;
                    OnPropertyChanged(nameof(IsMaterialScannerOpen));
                    
                    if (_isMaterialScannerOpen)
                    {
                        IsSettingsDialogOpen = false;
                        IsNodeDetailViewVisible = false;
                        IsWatcherOpen = false;
                    }
                }
            }
        }

        public bool IsWatcherOpen
        {
            get => _isWatcherOpen;
            set
            {
                if (_isWatcherOpen != value)
                {
                    _isWatcherOpen = value;
                    OnPropertyChanged(nameof(IsWatcherOpen));
                    
                    if (_isWatcherOpen)
                    {
                        IsSettingsDialogOpen = false;
                        IsNodeDetailViewVisible = false;
                        IsMaterialScannerOpen = false;
                    }
                }
            }
        }

        // Material Scanner Properties
        private ObservableCollection<string> _materialCategories;
        private string _selectedCategory;
        private string _materialName;
        private ObservableCollection<string> _colorOptions;
        private string _selectedColor;
        private ObservableCollection<string> _wearLevelOptions;
        private string _selectedWearLevel;
        private ObservableCollection<string> _roughnessLevelOptions;
        private string _selectedRoughnessLevel;
        private ObservableCollection<string> _compressionLevelOptions;
        private string _selectedCompressionLevel;
        private ObservableCollection<string> _densityLevelOptions;
        private string _selectedDensityLevel;
        private bool _isVolumeOptionsExpanded = false;
        private bool _isOutputOptionsExpanded = false;

        public ObservableCollection<string> MaterialCategories
        {
            get => _materialCategories;
            set
            {
                _materialCategories = value;
                OnPropertyChanged(nameof(MaterialCategories));
            }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged(nameof(SelectedCategory));
                UpdateMaterialFilename();
            }
        }

        public string MaterialName
        {
            get => _materialName;
            set
            {
                _materialName = value;
                OnPropertyChanged(nameof(MaterialName));
                OnPropertyChanged(nameof(HasMaterialName));
                UpdateMaterialFilename();
            }
        }

        public ObservableCollection<string> ColorOptions
        {
            get => _colorOptions;
            set
            {
                _colorOptions = value;
                OnPropertyChanged(nameof(ColorOptions));
            }
        }
        public string SelectedColor
        {
            get => _selectedColor;
            set
            {
                _selectedColor = value;
                OnPropertyChanged(nameof(SelectedColor));
                UpdateMaterialFilename();
            }
        }

        public ObservableCollection<string> WearLevelOptions
        {
            get => _wearLevelOptions;
            set
            {
                _wearLevelOptions = value;
                OnPropertyChanged(nameof(WearLevelOptions));
            }
        }

        public string SelectedWearLevel
        {
            get => _selectedWearLevel;
            set
            {
                _selectedWearLevel = value;
                OnPropertyChanged(nameof(SelectedWearLevel));
                UpdateMaterialFilename();
            }
        }

        public ObservableCollection<string> RoughnessLevelOptions
        {
            get => _roughnessLevelOptions;
            set
            {
                _roughnessLevelOptions = value;
                OnPropertyChanged(nameof(RoughnessLevelOptions));
            }
        }

        public string SelectedRoughnessLevel
        {
            get => _selectedRoughnessLevel;
            set
            {
                _selectedRoughnessLevel = value;
                OnPropertyChanged(nameof(SelectedRoughnessLevel));
                UpdateMaterialFilename();
            }
        }
        
        public ObservableCollection<string> CompressionLevelOptions
        {
            get => _compressionLevelOptions;
            set
            {
                _compressionLevelOptions = value;
                OnPropertyChanged(nameof(CompressionLevelOptions));
            }
        }

        public string SelectedCompressionLevel
        {
            get => _selectedCompressionLevel;
            set
            {
                _selectedCompressionLevel = value;
                OnPropertyChanged(nameof(SelectedCompressionLevel));
            }
        }
        
        public ObservableCollection<string> DensityLevelOptions
        {
            get => _densityLevelOptions;
            set
            {
                _densityLevelOptions = value;
                OnPropertyChanged(nameof(DensityLevelOptions));
            }
        }

        public string SelectedDensityLevel
        {
            get => _selectedDensityLevel;
            set
            {
                if (_selectedDensityLevel != value)
                {
                    _selectedDensityLevel = value;
                    OnPropertyChanged(nameof(SelectedDensityLevel));
                    
                    // Update the UseFixedDimension and UseSdDimension properties based on selection
                    switch (_selectedDensityLevel)
                    {
                        case "HD":
                            _useFixedDimension = true;
                            _useSdDimension = false;
                            break;
                        case "SD":
                            _useFixedDimension = false;
                            _useSdDimension = true;
                            break;
                        case "None":
                        default:
                            _useFixedDimension = false;
                            _useSdDimension = false;
                            break;
                    }
                    
                    OnPropertyChanged(nameof(UseFixedDimension));
                    OnPropertyChanged(nameof(UseSdDimension));
                }
            }
        }
        
        public bool IsVolumeOptionsExpanded
        {
            get => _isVolumeOptionsExpanded;
            set
            {
                if (_isVolumeOptionsExpanded != value)
                {
                    _isVolumeOptionsExpanded = value;
                    OnPropertyChanged(nameof(IsVolumeOptionsExpanded));
                }
            }
        }
        
        public bool IsOutputOptionsExpanded
        {
            get => _isOutputOptionsExpanded;
            set
            {
                if (_isOutputOptionsExpanded != value)
                {
                    _isOutputOptionsExpanded = value;
                    OnPropertyChanged(nameof(IsOutputOptionsExpanded));
                }
            }
        }
        
        public ICommand ToggleVolumeOptionsCommand => new RelayCommand(_ => IsVolumeOptionsExpanded = !IsVolumeOptionsExpanded);
        
        public ICommand ToggleOutputOptionsCommand => new RelayCommand(_ => IsOutputOptionsExpanded = !IsOutputOptionsExpanded);

        private string _materialFilename;
        public string MaterialFilename
        {
            get => _materialFilename;
            set
            {
                _materialFilename = value;
                OnPropertyChanged(nameof(MaterialFilename));
                OnPropertyChanged(nameof(IsMaterialFilenameValid));
            }
        }

        public bool IsMaterialFilenameValid => !string.IsNullOrWhiteSpace(MaterialName) && 
                                             !string.IsNullOrWhiteSpace(SelectedCategory);

        public bool HasMaterialName => !string.IsNullOrWhiteSpace(MaterialName);

        public string ScanCategoryPath
        {
            get => _scanCategoryPath;
            set
            {
                if (_scanCategoryPath != value)
                {
                    _scanCategoryPath = value;
                    _materialScannerService.ConfigureScanPath(value);
                    OnPropertyChanged(nameof(ScanCategoryPath));
                    // Refresh the material categories when path changes
                    RefreshMaterialCategories();
                }
            }
        }

        public bool IsDebugWindowVisible
        {
            get => _isDebugWindowVisible;
            set
            {
                if (_isDebugWindowVisible != value)
                {
                    _isDebugWindowVisible = value;
                    OnPropertyChanged(nameof(IsDebugWindowVisible));
                }
            }
        }

        public string DebugOutput
        {
            get => _debugOutput;
            set
            {
                if (_debugOutput != value)
                {
                    _debugOutput = value;
                    OnPropertyChanged(nameof(DebugOutput));
                }
            }
        }

        public WatcherViewModel Watcher => _watcher;

        // Authentication Status Properties
        public bool IsAuthenticationEnabled => _authenticationService?.IsAuthenticated ?? false;
        public string AuthenticationStatus => _authenticationService?.IsAuthenticated == true 
            ? $"Authenticated as {_authenticationService.CurrentNodeId}" 
            : "Not Authenticated";
        public string AuthenticationStatusColor => _authenticationService?.IsAuthenticated == true ? "#28A745" : "#DC3545";
        public string AuthenticationStatusTextColor => _authenticationService?.IsAuthenticated == true ? "#FFFFFF" : "#FFFFFF";
        public string AuthenticationStatusIconColor => _authenticationService?.IsAuthenticated == true ? "#FFFFFF" : "#FFFFFF";

        private void OnAuthenticationStatusChanged(object? sender, AuthenticationEventArgs e)
        {
            // Update authentication status properties on UI thread
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(IsAuthenticationEnabled));
                OnPropertyChanged(nameof(AuthenticationStatus));
                OnPropertyChanged(nameof(AuthenticationStatusColor));
                OnPropertyChanged(nameof(AuthenticationStatusTextColor));
                OnPropertyChanged(nameof(AuthenticationStatusIconColor));
            });
        }

        private void InitializeMaterialScannerOptions()
        {
            // Initialize with the default or previously saved scan path
            _scanCategoryPath = _materialScannerService.GetScanPath();
            OnPropertyChanged(nameof(ScanCategoryPath));
            
            // Initialize dropdown options
            ColorOptions = new ObservableCollection<string>
            {
                "",
                "Red",
                "Blue",
                "Green",
                "Yellow",
                "Black",
                "White",
                "Brown",
                "Gray"
            };
            
            WearLevelOptions = new ObservableCollection<string>
            {
                "",
                "New",
                "Light_Wear",
                "Medium_Wear",
                "Heavy_Wear"
            };
            
            RoughnessLevelOptions = new ObservableCollection<string>
            {
                "",
                "Smooth",
                "Rough",
                "Very_Rough"
            };
            
            CompressionLevelOptions = new ObservableCollection<string>
            {
                "No Compression",
                "4x Compression",
                "8x Compression"
            };
            
            DensityLevelOptions = new ObservableCollection<string>
            {
                "None",
                "HD",
                "SD"
            };
            
            // Set default selections
            SelectedColor = "";
            SelectedWearLevel = "";
            SelectedRoughnessLevel = "";
            SelectedCompressionLevel = "No Compression";
            SelectedDensityLevel = "None";
            MaterialName = "";
            
            // Ensure MaterialCategories is initialized with at least a default value if empty
            if (MaterialCategories == null || MaterialCategories.Count == 0)
            {
                MaterialCategories = new ObservableCollection<string> { "Default" };
                SelectedCategory = "Default";
            }
            
            // Get material categories from the scanner service based on the configured path
            RefreshMaterialCategories();
            
            // Initialize material filename
            UpdateMaterialFilename();
        }

        private void UpdateMaterialFilename()
        {
            if (!string.IsNullOrWhiteSpace(MaterialName))
            {
                MaterialFilename = $"{MaterialName}_{SelectedColor}_{SelectedWearLevel}_{SelectedRoughnessLevel}_{SelectedCompressionLevel}";
            }
            else
            {
                MaterialFilename = string.Empty;
            }
        }

        private async Task MakeMaterial()
        {
            if (IsMaterialFilenameValid)
            {
                try
                {
                    // Default to not skipping the rename prompt
                    bool skipPrompt = false;
                    
                    // Check if a material with this name already exists
                    string baseName = _materialScannerService.GenerateMaterialName(MaterialName, SelectedColor, SelectedWearLevel, SelectedRoughnessLevel);
                    
                    if (_materialScannerService.CheckMaterialNameExists(SelectedCategory, baseName))
                    {
                        // Show a dialog to confirm if the user wants to auto-increment
                        var result = MessageBox.Show(
                            $"A material with the name '{baseName}_01' already exists. Do you want to create it with the next available number?",
                            "Material Already Exists",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                            
                        if (result == MessageBoxResult.No)
                        {
                            return;
                        }
                        
                        skipPrompt = true;
                    }
                    
                    // Create the material
                    var (success, materialName, materialPath) = await _materialScannerService.CreateMaterialAsync(
                        SelectedCategory,
                        MaterialName,
                        SelectedColor,
                        SelectedWearLevel,
                        SelectedRoughnessLevel,
                        skipPrompt);
                        
                    if (success)
                    {
                        _materialScannerService.CopyMaterialNameToClipboard();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create material: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyMaterialNameToClipboard()
        {
            _materialScannerService.CopyMaterialNameToClipboard();
        }

        private void UpdateCurrentNodeStatus()
        {
            // Find the current node in the list
            var currentNodeId = _nodeService.CurrentNode.Id;
            var currentNodeVM = Nodes.FirstOrDefault(n => n.Id == currentNodeId);
            
            if (currentNodeVM != null)
            {
                // Update the node's availability
                ComputerNode updatedNode = new ComputerNode
                {
                    Id = _nodeService.CurrentNode.Id,
                    Name = _nodeService.CurrentNode.Name,
                    IpAddress = _nodeService.CurrentNode.IpAddress,
                    IsAvailable = _isActivated,
                    LastHeartbeat = DateTime.UtcNow
                };
                
                // Update the node view model
                currentNodeVM.UpdateFromNode(updatedNode);
                
                // Resort the nodes list
                SortNodesByStatus();
            }
        }

        // Populates the available devices collection with all available nodes
        private void UpdateAvailableDevices()
        {
            try
            {
                AvailableDevices.Clear();
                
                // Get all nodes, including both available and offline (for testing/debugging)
                var allNodes = Nodes.ToList();
                
                if (allNodes.Count == 0)
                {
                    // If no nodes are found, add at least the current node
                    var currentNodeVM = new NodeViewModel(_nodeService.CurrentNode);
                    allNodes.Add(currentNodeVM);
                }
                
                foreach (var node in allNodes)
                {
                    // Create device selection view model for all nodes (not just available ones)
                    var deviceVM = new DeviceSelectionViewModel(node);
                    
                    // Auto-select the current machine
                    if (node.Id == _nodeService.CurrentNode.Id)
                    {
                        deviceVM.IsSelected = true;
                    }
                    
                    AvailableDevices.Add(deviceVM);
                }
                
                // Ensure UI updates
                OnPropertyChanged(nameof(AvailableDevices));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating available devices: {ex.Message}");
            }
        }

        // Sort nodes with running tasks at top, available in middle, and offline at bottom
        private void SortNodes()
        {
            var sorted = Nodes.OrderBy(n => n.IsOffline ? 2 : (n.HasActiveTask ? 0 : 1)).ToList();
            
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                Nodes.Clear();
                foreach (var node in sorted)
                {
                    Nodes.Add(node);
                }
            });
        }

        private async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                ConnectionStatus = "Initializing...";
                
                // Test API connection first
                ConnectionStatus = "Testing API connection...";
                bool connected = await _apiClient.TestConnectionAsync();
                
                if (connected)
                {
                    ConnectionStatus = "API connected, authenticating...";
                    IsConnected = true;

                    try
                    {
                        // Register our node (this handles authentication internally)
                        var registeredNode = await _nodeService.RegisterNodeAsync();
                        ConnectionStatus = "Connected and authenticated";
                        
                        DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Initialized and authenticated as node: {registeredNode.Id}\n";
                        
                        // Refresh initial data (nodes and tasks)
                        await RefreshDataAsync();

                        // Start the refresh timer for periodic updates
                        StartPeriodicRefresh();

                        // Start the task polling to display messages
                        _taskExecutionService.StartTaskPolling();

                        // Start the debug hub connection if available
                        var app = (App)System.Windows.Application.Current;
                        if (app.DebugHubClient != null)
                        {
                            await app.DebugHubClient.StartAsync();
                        }
                    }
                    catch (Exception authEx)
                    {
                        ConnectionStatus = $"Authentication failed: {authEx.Message}";
                        IsConnected = false;
                        DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Authentication failed during initialization: {authEx.Message}\n";
                        
                        // Don't show message box during initialization, just log it
                        System.Diagnostics.Debug.WriteLine($"Authentication failed during initialization: {authEx.Message}");
                    }
                }
                else
                {
                    ConnectionStatus = "API not accessible - offline mode";
                    IsConnected = false;
                    DebugOutput += $"[{DateTime.Now:HH:mm:ss}] API not accessible during initialization - running in offline mode\n";
                    
                    // Still start polling and other services even if API is not available
                    StartPeriodicRefresh();
                    _taskExecutionService.StartTaskPolling();
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Initialization error: {ex.Message}";
                IsConnected = false;
                DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Initialization error: {ex.Message}\n";
                System.Diagnostics.Debug.WriteLine($"Error initializing: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RefreshNodesAsync()
        {
            try
            {
                // Get all nodes (including offline) from the API
                var nodes = await _apiClient.GetAllNodesAsync();
                
                // Update UI on the main thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Remove nodes that no longer exist
                    for (int i = Nodes.Count - 1; i >= 0; i--)
                    {
                        var nodeVM = Nodes[i];
                        if (!nodes.Any(n => n.Id == nodeVM.Id))
                        {
                            Nodes.RemoveAt(i);
                        }
                    }
                    
                    // Update existing nodes and add new ones
                    foreach (var node in nodes)
                    {
                        // No longer skipping the current node
                        // if (node.Id == _nodeService.CurrentNode.Id)
                        //    continue;
                            
                        var existingNodeVM = Nodes.FirstOrDefault(n => n.Id == node.Id);
                        if (existingNodeVM != null)
                        {
                            // Update existing node
                            existingNodeVM.UpdateFromNode(node);
                        }
                        else
                        {
                            // Add new node
                            Nodes.Add(new NodeViewModel(node));
                        }
                    }
                    
                    // Also ensure the current node is added
                    var currentNode = Nodes.FirstOrDefault(n => n.Id == _nodeService.CurrentNode.Id);
                    if (currentNode == null)
                    {
                        // Create a node for the current machine if it doesn't exist yet
                        Nodes.Add(new NodeViewModel(_nodeService.CurrentNode));
                    }
                    
                    // Sort nodes by status
                    SortNodesByStatus();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing nodes: {ex.Message}");
            }
        }
        
        private void SortNodesByStatus()
        {
            var sortedNodes = Nodes.OrderBy(node => 
            {
                // Priority order: Running (1), Available (2), Offline (3)
                if (node.HasActiveTask) return 1;  // Running tasks first
                if (node.IsAvailable) return 2;    // Available nodes second
                return 3;                         // Offline nodes last
            }).ToList();
            
            // Clear and repopulate with sorted nodes
            Nodes.Clear();
            foreach (var node in sortedNodes)
            {
                Nodes.Add(node);
            }
        }
        
        private async Task<bool> CheckApiConnectionAsync()
        {
            try
            {
                // First try the TestConnection method we added
                bool connectionTest = await _apiClient.TestConnectionAsync();
                if (connectionTest)
                    return true;
                    
                // If that fails, try to get nodes as a fallback
                var nodes = await _apiClient.GetAvailableNodesAsync();
                return true; // If we get here, the API is responsive
            }
            catch
            {
                return false; // API is not responsive
            }
        }

        private async Task LoadMockDataAsync()
        {
            // Clear existing nodes
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                Nodes.Clear();
            });
            
            // Add mock nodes to demonstrate the UI
            await AddSampleNodesAsync();
            
            // Add mock tasks if needed
            if (Tasks.Count == 0)
            {
                // Generate some sample tasks with different statuses
                var taskTypes = new[] { TaskType.FileProcessing, TaskType.RenderThumbnails, TaskType.RealityCapture, TaskType.PackageTask, TaskType.VolumeCompression };
                var statuses = new[] { BatchTaskStatus.Running, BatchTaskStatus.Pending, BatchTaskStatus.Completed, BatchTaskStatus.Failed };
                var random = new Random();
                
                for (int i = 0; i < 8; i++)
                {
                    var status = statuses[random.Next(statuses.Length)];
                    var taskType = taskTypes[random.Next(taskTypes.Length)];
                    
                    var task = new BatchTask
                    {
                        Id = i + 1,
                        Type = taskType,
                        Name = $"{taskType} {i + 1}",
                        Status = status,
                        CreatedAt = DateTime.Now.AddHours(-random.Next(1, 24))
                    };
                    
                    // Assign to nodes and set times based on status
                    if (status != BatchTaskStatus.Pending && Nodes.Count > 0)
                    {
                        var nodeIndex = random.Next(Nodes.Count);
                        task.AssignedNodeId = Nodes[nodeIndex].Id;
                        
                        if (status == BatchTaskStatus.Running)
                        {
                            task.StartedAt = DateTime.Now.AddMinutes(-random.Next(1, 30));
                        }
                        else
                        {
                            task.StartedAt = DateTime.Now.AddHours(-random.Next(1, 5));
                            task.CompletedAt = task.StartedAt?.AddMinutes(random.Next(5, 60));
                        }
                    }
                    
                    var taskVM = new TaskViewModel(task);
                    
                    // Set progress for running tasks
                    if (status == BatchTaskStatus.Running)
                    {
                        taskVM.UpdateProgress(random.NextDouble() * 0.9);
                    }
                    
                    // Set node name if assigned
                    if (!string.IsNullOrEmpty(task.AssignedNodeId))
                    {
                        var node = Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
                        if (node != null)
                        {
                            taskVM.AssignedNodeName = node.Name;
                        }
                    }
                    
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        Tasks.Add(taskVM);
                    });
                }
                
                // Sort tasks
                SortTasksByStatus();
            }
        }

        private void StartPeriodicRefresh()
        {
            // Dispose any existing timer
            _refreshTimer?.Dispose();
            
            // Create a new timer that refreshes data periodically
            _refreshTimer = new System.Threading.Timer(_ => 
            {
                // Use Dispatcher to ensure UI updates happen on UI thread
                System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => 
                {
                    if (IsConnected && !IsLoading)
                    {
                        try
                        {
                            await RefreshDataAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error refreshing data: {ex.Message}");
                        }
                    }
                });
            }, null, 0, RefreshIntervalMs);
        }
        
        private void UpdateTaskTimes()
        {
            try
            {
                // Update elapsed time for all nodes with active tasks
                foreach (var node in Nodes.Where(n => n.HasActiveTask))
                {
                    node.UpdateTaskTimes();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating task times: {ex.Message}");
            }
        }

        private async Task RefreshDataAsync()
        {
            try
            {
                // Get nodes and tasks from API
                List<ComputerNode> nodes;
                try
                {
                    nodes = await _apiClient.GetAvailableNodesAsync();
                    System.Diagnostics.Debug.WriteLine($"Received {nodes.Count} nodes from API");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting nodes from API: {ex.Message}");
                    // If we can't get nodes from API, start with empty list but always include current node
                    nodes = new List<ComputerNode>();
                }
                
                List<BatchTask> tasks;
                try
                {
                    tasks = await _apiClient.GetTasksAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting tasks from API: {ex.Message}");
                    tasks = new List<BatchTask>();
                }
                
                // Keep track of current node ID
                string currentNodeId = _nodeService.CurrentNode.Id;
                
                // Track node IDs we've seen
                var processedNodeIds = new HashSet<string>();
                
                // Use a dictionary to track the nodes by ID to prevent duplicates
                var uniqueNodes = new Dictionary<string, ComputerNode>();
                
                // ALWAYS add the current node first to ensure it's included
                var currentNode = new ComputerNode {
                    Id = currentNodeId,
                    Name = Environment.MachineName,
                    IpAddress = _nodeService.CurrentNode.IpAddress,
                    IsAvailable = _isActivated,
                    LastHeartbeat = DateTime.UtcNow
                };
                uniqueNodes[currentNodeId] = currentNode;
                System.Diagnostics.Debug.WriteLine($"Added current node: {currentNode.Id} - {currentNode.Name}");
                
                // Add or update with nodes from the API
                foreach (var node in nodes)
                {
                    // If this is our node, preserve our availability state
                    if (node.Id == currentNodeId)
                    {
                        var updatedNode = new ComputerNode
                        {
                            Id = node.Id,
                            Name = node.Name,
                            IpAddress = node.IpAddress,
                            IsAvailable = _isActivated,
                            LastHeartbeat = node.LastHeartbeat
                        };
                        uniqueNodes[node.Id] = updatedNode;
                        System.Diagnostics.Debug.WriteLine($"Updated current node from API: {updatedNode.Id} - {updatedNode.Name}");
                    }
                    else
                    {
                        uniqueNodes[node.Id] = node;
                        System.Diagnostics.Debug.WriteLine($"Added node from API: {node.Id} - {node.Name}");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Total unique nodes to process: {uniqueNodes.Count}");
                
                // First remove any nodes that are no longer present
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    for (int i = Nodes.Count - 1; i >= 0; i--)
                    {
                        var nodeVM = Nodes[i];
                        if (!uniqueNodes.ContainsKey(nodeVM.Id))
                        {
                            System.Diagnostics.Debug.WriteLine($"Removing node: {nodeVM.Id} - {nodeVM.Name}");
                            Nodes.RemoveAt(i);
                        }
                    }
                });
                
                // Then update existing nodes or add new ones
                foreach (var nodePair in uniqueNodes)
                {
                    var node = nodePair.Value;
                    var existingNode = Nodes.FirstOrDefault(n => n.Id == node.Id);
                    
                    if (existingNode != null)
                    {
                        // Update existing node
                        System.Diagnostics.Debug.WriteLine($"Updating existing node: {node.Id} - {node.Name}");
                        existingNode.UpdateFromNode(node);
                    }
                    else
                    {
                        // Add new node
                        var newNode = new NodeViewModel(node);
                        System.Diagnostics.Debug.WriteLine($"Adding new node: {node.Id} - {node.Name}");
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            Nodes.Add(newNode);
                        });
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Final node count in UI: {Nodes.Count}");
                
                // Update tasks for nodes
                foreach (var task in tasks)
                {
                    // If this is a test message task in Running status, mark it as completed if it's been running too long
                    if (task.Type == TaskType.TestMessage && task.Status == BatchTaskStatus.Running && 
                        task.StartedAt.HasValue && DateTime.UtcNow - task.StartedAt.Value > TimeSpan.FromSeconds(30))
                    {
                        // Mark test messages as completed if they've been running for more than 30 seconds
                        await _apiClient.UpdateTaskStatusAsync(
                            task.Id,
                            BatchTaskStatus.Completed,
                            $"Automatically completed test message task from {task.StartedAt}"
                        );
                        
                        // Skip this task as it's now completed
                        continue;
                    }
                
                    // If we have a completed task for a node, ensure it's shown as the last completed task
                    if (task.Status == BatchTaskStatus.Completed && 
                        !string.IsNullOrEmpty(task.AssignedNodeId) &&
                        task.CompletedAt.HasValue)
                    {
                        var targetNode = Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
                        if (targetNode != null && 
                            (targetNode.LastTaskName == null || 
                             (targetNode.GetLastCompletedTask != null && 
                              targetNode.GetLastCompletedTask.CompletedAt < task.CompletedAt)))
                        {
                            targetNode.SetLastCompletedTask(task);
                        }
                    }
                
                    if ((task.Status == BatchTaskStatus.Running || task.Status == BatchTaskStatus.Pending) && 
                        !string.IsNullOrEmpty(task.AssignedNodeId))
                    {
                        var targetNode = Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
                        if (targetNode != null)
                        {
                            // Estimate progress
                            double progress = 0;
                            if (task.StartedAt.HasValue)
                            {
                                var elapsed = DateTime.UtcNow - task.StartedAt.Value;
                                // Simple progress estimation (not accurate, just for testing)
                                var estimatedDuration = TimeSpan.FromMinutes(2);
                                progress = Math.Min(0.95, elapsed.TotalMilliseconds / estimatedDuration.TotalMilliseconds);
                            }
                            
                            targetNode.SetActiveTask(task, progress);
                        }
                    }
                }
                
                // Also check if any nodes have running tasks that are no longer in the task list
                foreach (var node in Nodes.Where(n => n.HasActiveTask).ToList())
                {
                    if (node.ActiveTask != null && 
                        !tasks.Any(t => t.Id == node.ActiveTask.Id && 
                                (t.Status == BatchTaskStatus.Running || t.Status == BatchTaskStatus.Pending)))
                    {
                        // Task is no longer running on the server, clear it from the node
                        node.ClearActiveTask();
                    }
                }
                
                // Sort nodes by status
                SortNodesByStatus();

                // Also refresh the task list to update the Tasks collection
                await RefreshTaskListAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing data: {ex.Message}");
                DebugOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR refreshing data: {ex.Message}\n";
            }
        }

        private void BrowseForDirectory()
        {
            if (SelectedProcessType == TaskType.VolumeCompression)
            {
                // For VolumeCompression, allow selecting folders
                var dialog = new Forms.FolderBrowserDialog
                {
                    Description = "Select folders containing VDB files to compress",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };
                
                // Check if the configured hotkey is pressed when dialog is shown
                bool isHotkeyPressed = (Forms.Control.ModifierKeys & AddFilesHotkey) != 0;
                
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    // If not in "add" mode (hotkey not pressed), clear existing directories first
                    if (!isHotkeyPressed)
                    {
                        ClearDirectories();
                    }
                    
                    // Add the selected folder to our collection
                    AddDirectory(dialog.SelectedPath);
                    
                    // Force re-evaluation of CanStartProcess
                    OnPropertyChanged(nameof(CanStartProcess));
                }
            }
            else
            {
                // For other task types, use the folder browser dialog
                BrowseForFolder("Select Directory", path => AddDirectory(path, IsAddFilesHotkeyPressed()));
            }
        }

        private bool IsAddFilesHotkeyPressed()
        {
            return (Forms.Control.ModifierKeys & AddFilesHotkey) != 0;
        }

        private void BrowseForFolder(string title, Action<string> onPathSelected)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = title,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                onPathSelected(dialog.SelectedPath);
                
                // Force re-evaluation of CanStartProcess
                OnPropertyChanged(nameof(CanStartProcess));
            }
        }

        private async Task StartNewProcessAsync()
        {
            try
            {
                IsLoading = true;
                
                // Get selected node IDs
                var selectedNodeIds = AvailableDevices
                    .Where(d => d.IsSelected)
                    .Select(d => d.NodeId)
                    .ToList();
                
                if (!selectedNodeIds.Any())
                {
                    System.Windows.MessageBox.Show("Please select at least one node to run the process.");
                    return;
                }
                
                // Create parameters dictionary
                var parameters = new Dictionary<string, string>
                {
                    ["Directories"] = JsonSerializer.Serialize(SelectedDirectories)
                };
                
                // Add task-specific parameters
                if (SelectedProcessType == TaskType.TestMessage)
                {
                    parameters["MessageText"] = MessageText;
                    parameters["SenderName"] = CurrentNode.Name;
                }
                else if (SelectedProcessType == TaskType.VolumeCompression)
                {
                    parameters["UseFixedDimension"] = UseFixedDimension.ToString();
                    parameters["UseSdDimension"] = UseSdDimension.ToString();
                    parameters["CompressionLevel"] = SelectedCompressionLevel;
                    parameters["CreateOutputFolder"] = CreateOutputFolder.ToString();
                    parameters["OverrideOutputDirectory"] = UseOverrideOutputDirectory.ToString();
                    if (UseOverrideOutputDirectory && !string.IsNullOrEmpty(OutputDirectory))
                    {
                        parameters["OutputDirectory"] = OutputDirectory;
                    }
                }
                
                // Create a single task with multiple assigned nodes
                var task = new BatchTask
                {
                    Name = SelectedProcessType.ToString(),
                    Type = SelectedProcessType,
                    Status = BatchTaskStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    AssignedNodeId = selectedNodeIds.First(), // Keep first node for backward compatibility
                    AssignedNodeIds = JsonSerializer.Serialize(selectedNodeIds), // Store all selected nodes
                    Parameters = JsonSerializer.Serialize(parameters)
                };
                
                var createdTask = await _apiClient.CreateTaskAsync(task);
                if (createdTask != null)
                {
                    // Pre-scan volume compression tasks to create folder progress records
                    if (createdTask.Type == TaskType.VolumeCompression)
                    {
                        await _taskExecutionService.PreScanVolumeCompressionTaskAsync(createdTask);
                    }
                    
                    // Assign the task to all selected nodes
                    bool allAssigned = true;
                    foreach (var nodeId in selectedNodeIds)
                    {
                        bool assigned = await _apiClient.AssignTaskToNodeAsync(createdTask.Id, nodeId);
                        if (!assigned)
                        {
                            allAssigned = false;
                            DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Warning: Failed to assign task to node {nodeId}\n";
                        }
                    }
                    
                    if (allAssigned)
                    {
                        DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Created task {createdTask.Id} assigned to {selectedNodeIds.Count} nodes\n";
                    }
                    else
                    {
                        DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Created task {createdTask.Id} but some node assignments failed\n";
                    }
                    
                    await RefreshDataAsync();
                    IsNewProcessDialogOpen = false; // Close the dialog on success
                }
                else
                {
                    System.Windows.MessageBox.Show("Failed to create task.");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error starting process: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task AbortSelectedNodeProcessAsync()
        {
            if (SelectedNode == null || !SelectedNode.HasActiveTask)
                return;
                
            try
            {
                // Show confirmation dialog
                var result = System.Windows.MessageBox.Show(
                    "Are you sure you want to abort the current process?",
                    "Abort Process",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning
                );
                
                if (result != System.Windows.MessageBoxResult.Yes) return;
                
                IsLoading = true;
                
                // Call the TaskExecutionService's AbortCurrentTask method
                await _taskExecutionService.AbortCurrentTask(SelectedNode.Id);
                
                // Log to debug output
                DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Process abort requested for node {SelectedNode.Name}\n";
                
                // Force refresh of data
                await RefreshDataAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to abort process: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
                
                DebugOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ToggleActivation()
        {
            IsActivated = !IsActivated;
        }

        private async Task ConnectToServer()
        {
            try
            {
                ConnectionStatus = "Attempting to connect...";
                
                if (string.IsNullOrWhiteSpace(ServerAddress))
                {
                    ConnectionStatus = "Invalid server address";
                    return;
                }

                // Create new API client with the specified address
                _apiClient = new ApiClient(ServerAddress);
                _nodeService.UpdateApiClient(_apiClient);
                _taskExecutionService.UpdateApiClient(_apiClient);

                // Update debug hub client with new server address
                var app = (App)System.Windows.Application.Current;
                if (app.DebugHubClient != null)
                {
                    app.DebugHubClient.UpdateServerUrl(ServerAddress);
                    await app.DebugHubClient.StartAsync();
                }

                // Test the connection first
                ConnectionStatus = "Testing connection...";
                bool connected = await _apiClient.TestConnectionAsync();
                if (!connected)
                {
                    ConnectionStatus = "Failed to connect to server";
                    IsConnected = false;
                    return;
                }

                ConnectionStatus = "Server connected, authenticating...";
                
                try
                {
                    // Register our node (this handles authentication internally)
                    var registeredNode = await _nodeService.RegisterNodeAsync();
                    
                    ConnectionStatus = "Connected and authenticated";
                    IsConnected = true;
                    
                    DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Successfully connected and authenticated as node: {registeredNode.Id}\n";
                    
                    // Refresh data
                    await RefreshDataAsync();
                }
                catch (UnauthorizedAccessException authEx)
                {
                    ConnectionStatus = $"Authentication failed: {authEx.Message}";
                    IsConnected = false;
                    DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Authentication error: {authEx.Message}\n";
                    
                    MessageBox.Show(
                        $"Failed to authenticate with the server:\n{authEx.Message}\n\nPlease check your server connection and try again.",
                        "Authentication Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                catch (Exception regEx)
                {
                    ConnectionStatus = $"Registration failed: {regEx.Message}";
                    IsConnected = false;
                    DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Registration error: {regEx.Message}\n";
                    
                    MessageBox.Show(
                        $"Failed to register with the server:\n{regEx.Message}\n\nThis may be due to server configuration issues.",
                        "Registration Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Connection error: {ex.Message}";
                IsConnected = false;
                DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Connection error: {ex.Message}\n";
                
                MessageBox.Show(
                    $"Failed to connect to the server:\n{ex.Message}",
                    "Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public void AddDirectory(string path, bool addToExisting = false)
        {
            // Check if it's a file or directory
            bool isFile = File.Exists(path);
            bool isDirectory = Directory.Exists(path);
            
            if (isFile || isDirectory)
            {
                // Skip if already exists in collection to avoid duplicates
                if (_selectedDirectories.Contains(path))
                {
                    return;
                }
                
                // If we're not adding to existing and this is the first entry,
                // clear any existing directories
                if (!addToExisting && _selectedDirectories.Count > 0)
                {
                    _selectedDirectories.Clear();
                }
                
                // Add the new path
                _selectedDirectories.Add(path);
                
                // Update the display string
                SelectedDirectory = string.Join("; ", _selectedDirectories);
                OnPropertyChanged(nameof(SelectedDirectories));
                OnPropertyChanged(nameof(CanStartProcess));
            }
        }
        
        public void ClearDirectories()
        {
            _selectedDirectories.Clear();
            SelectedDirectory = "";
            OnPropertyChanged(nameof(SelectedDirectories));
            OnPropertyChanged(nameof(CanStartProcess));
        }

        public void HandleKeyPress(System.Windows.Forms.Keys key)
        {
            if (IsWaitingForKeyPress)
            {
                // Only accept modifier keys
                if (key == System.Windows.Forms.Keys.Shift || 
                    key == System.Windows.Forms.Keys.Control || 
                    key == System.Windows.Forms.Keys.Alt)
                {
                    AddFilesHotkey = key;
                    IsWaitingForKeyPress = false;
                }
                else
                {
                    // Show message for invalid keys
                    System.Windows.MessageBox.Show(
                        "Please use only Shift, Ctrl, or Alt keys as hotkeys.",
                        "Invalid Hotkey",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void RefreshMaterialCategories()
        {
            try
            {
                // Get categories from service
                var categories = _materialScannerService.GetFolderCategories();
                
                // If no categories found, provide default ones
                if (categories.Count == 0)
                {
                    // First check if the scan path exists
                    if (!Directory.Exists(_scanCategoryPath))
                    {
                        // Set default categories
                        categories = new List<string> { "Fabric", "Metal", "Plastic", "Wood", "Leather", "Glass", "Ceramic", "Other" };
                    }
                    else
                    {
                        // Set default categories
                        categories = new List<string> { "Fabric", "Metal", "Plastic", "Wood", "Leather", "Glass", "Ceramic", "Other" };
                    }
                }
                
                // Update the UI collection
                MaterialCategories = new ObservableCollection<string>(categories);
                
                // Select the first category if available
                if (MaterialCategories.Count > 0)
                {
                    SelectedCategory = MaterialCategories[0];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to refresh categories: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                MaterialCategories = new ObservableCollection<string> { "Default" };
                SelectedCategory = "Default";
            }
        }

        private void BrowseForScanCategoryPath()
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Select Scan Category Path",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string newPath = dialog.SelectedPath;
                ScanCategoryPath = newPath;
                
                // Check if the directory is empty
                if (Directory.Exists(newPath) && !Directory.EnumerateFileSystemEntries(newPath).Any())
                {
                    var result = System.Windows.MessageBox.Show(
                        "The selected folder is empty. Do you want to create default category folders?",
                        "Create Default Categories",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);
                        
                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        try
                        {
                            // Create default category folders
                            string[] defaultCategories = { "Fabric", "Metal", "Plastic", "Wood", "Leather", "Glass", "Ceramic", "Other" };
                            foreach (var category in defaultCategories)
                            {
                                string categoryPath = Path.Combine(newPath, category);
                                if (!Directory.Exists(categoryPath))
                                {
                                    Directory.CreateDirectory(categoryPath);
                                }
                            }
                            
                            // Refresh categories
                            RefreshMaterialCategories();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Failed to create category folders: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private void BrowseForOutputDirectory()
        {
            BrowseForFolder("Select Output Directory", path => OutputDirectory = path);
        }

        // Method to open the add directory form popup
        private void OpenAddDirectoryForm()
        {
            IsAddDirectoryFormOpen = true;
        }

        // Method to close the add directory form popup
        private void CloseAddDirectoryForm()
        {
            IsAddDirectoryFormOpen = false;
        }

        private void OnSyncPairAdded(object? sender, EventArgs e)
        {
            // Close the add directory form when a sync pair has been added
            CloseAddDirectoryForm();
        }

        private void OnUnsyncedChangesChanged(object? sender, EventArgs e)
        {
            // Force UI update when unsynced changes status changes
            OnPropertyChanged(nameof(Watcher));
        }

        // Handle debug messages from the hub
        private void OnDebugMessageReceived(object? sender, DebugMessageEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Add the received debug message to the output
                if (e?.Message != null)
                {
                    string formattedMessage = $"[{e.Message.Timestamp:HH:mm:ss}] [{e.Message.Source}]: {e.Message.Message}";
                    DebugOutput += formattedMessage + Environment.NewLine;
                }
            });
        }

        private async Task RefreshTaskListAsync()
        {
            try
            {
                // Get all tasks from the API
                var tasks = await _apiClient.GetAllTasksAsync();
                
                // Update UI on the main thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Remove tasks that no longer exist
                    for (int i = Tasks.Count - 1; i >= 0; i--)
                    {
                        var taskVM = Tasks[i];
                        if (!tasks.Any(t => t.Id == taskVM.Id))
                        {
                            Tasks.RemoveAt(i);
                        }
                    }
                    
                    // Update existing tasks and add new ones
                    foreach (var task in tasks)
                    {
                        var existingTaskVM = Tasks.FirstOrDefault(t => t.Id == task.Id);
                        if (existingTaskVM != null)
                        {
                            // Update existing task
                            existingTaskVM.UpdateStatus(task.Status);
                            
                            // Estimate progress
                            double progress = 0;
                            if (task.StartedAt.HasValue && task.Status == BatchTaskStatus.Running)
                            {
                                var elapsed = DateTime.UtcNow - task.StartedAt.Value;
                                // Simple progress estimation (not accurate, just for testing)
                                var estimatedDuration = TimeSpan.FromMinutes(2);
                                progress = Math.Min(0.95, elapsed.TotalMilliseconds / estimatedDuration.TotalMilliseconds);
                                
                                existingTaskVM.UpdateProgress(progress);
                            }
                            
                            // Update node name assignment
                            if (!string.IsNullOrEmpty(task.AssignedNodeId))
                            {
                                var node = Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
                                if (node != null)
                                {
                                    existingTaskVM.AssignedNodeName = node.Name;
                                }
                            }
                        }
                        else
                        {
                            // Add new task
                            var taskVM = new TaskViewModel(task);
                            
                            // Set assigned node name if available
                            if (!string.IsNullOrEmpty(task.AssignedNodeId))
                            {
                                var node = Nodes.FirstOrDefault(n => n.Id == task.AssignedNodeId);
                                if (node != null)
                                {
                                    taskVM.AssignedNodeName = node.Name;
                                }
                            }
                            
                            // Estimate progress for running tasks
                            if (task.StartedAt.HasValue && task.Status == BatchTaskStatus.Running)
                            {
                                var elapsed = DateTime.UtcNow - task.StartedAt.Value;
                                // Simple progress estimation (not accurate, just for testing)
                                var estimatedDuration = TimeSpan.FromMinutes(2);
                                double progress = Math.Min(0.95, elapsed.TotalMilliseconds / estimatedDuration.TotalMilliseconds);
                                
                                taskVM.UpdateProgress(progress);
                            }
                            
                            Tasks.Add(taskVM);
                        }
                    }
                    
                    // Sort tasks by status and creation date
                    SortTasksByStatus();
                });
                
                // Refresh folder progress for the currently selected task if it's a volume compression task
                if (IsTaskDetailViewVisible && SelectedTask != null && SelectedTask.Type == TaskType.VolumeCompression)
                {
                    await RefreshCurrentTaskFolderProgressAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing tasks: {ex.Message}");
            }
        }
        
        private async Task RefreshCurrentTaskFolderProgressAsync()
        {
            if (SelectedTask == null) return;
            
            try
            {
                var folderProgressList = await _apiClient.GetTaskFoldersAsync(SelectedTask.Id);
                var folderProgressViewModels = new ObservableCollection<TaskFolderProgressViewModel>(
                    folderProgressList.Select(fp => new TaskFolderProgressViewModel(fp))
                );

                // Update the UI on the main thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (SelectedTask != null && IsTaskDetailViewVisible)
                    {
                        SelectedTask.UpdateFolderProgress(folderProgressViewModels);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing current task folder progress: {ex.Message}");
            }
        }

        private void ShowTaskDetails(object parameter)
        {
            if (parameter is TaskViewModel task)
            {
                SelectedTask = task;
                IsTaskDetailViewVisible = true;
                
                // Load folder progress for volume compression tasks
                if (task.Type == TaskType.VolumeCompression)
                {
                    _ = Task.Run(async () => await LoadTaskFolderProgressAsync(task.Id));
                }
            }
        }

        private async Task LoadTaskFolderProgressAsync(int taskId)
        {
            try
            {
                var folderProgressList = await _apiClient.GetTaskFoldersAsync(taskId);
                var folderProgressViewModels = new ObservableCollection<TaskFolderProgressViewModel>(
                    folderProgressList.Select(fp => new TaskFolderProgressViewModel(fp))
                );

                // Update the UI on the main thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (SelectedTask != null && SelectedTask.Id == taskId)
                    {
                        SelectedTask.UpdateFolderProgress(folderProgressViewModels);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading task folder progress: {ex.Message}");
            }
        }

        private void SortTasksByStatus()
        {
            // First filter to only include active tasks (Running and Pending),
            // then sort them with Running first, then Pending
            var activeTasks = Tasks.Where(task => 
                task.Status == BatchTaskStatus.Running || 
                task.Status == BatchTaskStatus.Pending)
                .OrderBy(task => task.Status == BatchTaskStatus.Running ? 0 : 1)
                .ThenByDescending(task => task.CreatedAt)
                .ToList();
            
            // Clear and repopulate with filtered active tasks
            Tasks.Clear();
            foreach (var task in activeTasks)
            {
                Tasks.Add(task);
            }
        }

        public bool IsTaskDetailViewVisible
        {
            get => _isTaskDetailViewVisible;
            set
            {
                if (_isTaskDetailViewVisible != value)
                {
                    _isTaskDetailViewVisible = value;
                    OnPropertyChanged(nameof(IsTaskDetailViewVisible));
                }
            }
        }

        public TaskViewModel? SelectedTask
        {
            get => _selectedTask;
            set
            {
                if (_selectedTask != value)
                {
                    _selectedTask = value;
                    OnPropertyChanged(nameof(SelectedTask));
                }
            }
        }

        public ICommand ShowTaskDetailsCommand { get; }
        public ICommand CloseTaskDetailsCommand { get; }

        private async void AbortTask()
        {
            if (SelectedTask == null || !SelectedTask.CanAbort)
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to cancel this task?",
                "Cancel Task",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsLoading = true;
                await _taskExecutionService.AbortCurrentTask(SelectedTask.AssignedNodeId);
                DebugOutput += $"[{DateTime.Now:HH:mm:ss}] Task abort requested for node {SelectedTask.AssignedNodeName}\n";
                await RefreshTaskListAsync();
                OnPropertyChanged(nameof(SelectedTask));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to cancel task: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DebugOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task AddSampleNodesAsync()
        {
            try {
                // Clear existing nodes for testing purposes to avoid duplicates
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    Nodes.Clear();
                });
                
                // Add the current node (user's own PC)
                var currentNodeData = new ComputerNode
                {
                    Id = _nodeService.CurrentNode.Id,
                    Name = _nodeService.CurrentNode.Name,
                    IpAddress = _nodeService.CurrentNode.IpAddress,
                    IsAvailable = _isActivated, // Set availability based on toggle
                    LastHeartbeat = DateTime.UtcNow
                };
                
                var currentNode = new NodeViewModel(currentNodeData);
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    Nodes.Add(currentNode);
                });
                
                // Just add one node with a running task
                var processNode = new ComputerNode
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "PC-1",
                    IpAddress = "192.168.1.101",
                    IsAvailable = true,
                    LastHeartbeat = DateTime.UtcNow
                };
                
                var processNodeVM = new NodeViewModel(processNode);
                
                var task = new BatchTask
                {
                    Id = 1,
                    AssignedNodeId = processNode.Id,
                    Type = TaskType.FileProcessing,
                    Status = BatchTaskStatus.Running,
                    StartedAt = DateTime.Now.AddMinutes(-5)
                };
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    Nodes.Add(processNodeVM);
                    processNodeVM.SetActiveTask(task, 0.45);
                });
                
                // Add an available node
                var availableNode = new ComputerNode
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "PC-2",
                    IpAddress = "192.168.1.102",
                    IsAvailable = true,
                    LastHeartbeat = DateTime.UtcNow
                };
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    Nodes.Add(new NodeViewModel(availableNode));
                });
                
                // Add an offline node
                var offlineNode = new ComputerNode
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "PC-3",
                    IpAddress = "192.168.1.103",
                    IsAvailable = false,
                    LastHeartbeat = DateTime.UtcNow.AddMinutes(-10) // Old heartbeat
                };
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    Nodes.Add(new NodeViewModel(offlineNode));
                    
                    // Sort all nodes by status
                    SortNodesByStatus();
                });
            }
            catch (Exception ex)
            {
                // Log exception but don't crash
                System.Diagnostics.Debug.WriteLine($"Error in AddSampleNodesAsync: {ex.Message}");
            }
        }

        private async Task StartApiServiceAsync()
        {
            try
            {
                // First check if the API is responding
                bool apiResponsive = await CheckApiConnectionAsync();
                
                if (apiResponsive)
                {
                    Debug.WriteLine("API service is already running and responsive.");
                    return;
                }
                
                Debug.WriteLine("API is not responsive, attempting to start it.");
                
                // Kill any existing API processes by name
                foreach (var proc in Process.GetProcessesByName("VCDevTool.API"))
                {
                    try
                    {
                        proc.Kill(true); // Force kill including child processes
                        proc.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to kill existing API process: {ex.Message}");
                    }
                }
                
                // Also look for dotnet processes that might be hosting the API
                foreach (var proc in Process.GetProcessesByName("dotnet"))
                {
                    try
                    {
                        // Try to check if this process is running our API
                        if (proc.MainWindowTitle.Contains("VCDevTool.API") || 
                            (proc.StartInfo != null && proc.StartInfo.Arguments != null && 
                            proc.StartInfo.Arguments.Contains("VCDevTool.API")))
                        {
                            proc.Kill(true);
                            proc.WaitForExit(5000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to check/kill dotnet process: {ex.Message}");
                    }
                }
                
                // Start the API service
                var currentDirectory = Directory.GetCurrentDirectory();
                var rootDirectory = Path.GetFullPath(Path.Combine(currentDirectory, ".."));
                
                _apiProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "run --project VCDevTool.API",
                        WorkingDirectory = rootDirectory,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    }
                };
                
                _apiProcess.Start();
                Debug.WriteLine("API service started successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting API service: {ex.Message}");
                System.Windows.MessageBox.Show($"Error starting API service: {ex.Message}", "API Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public void ShutdownApiService()
        {
            try
            {
                if (_apiProcess != null && !_apiProcess.HasExited)
                {
                    _apiProcess.Kill();
                    _apiProcess.Dispose();
                    _apiProcess = null;
                    Debug.WriteLine("API service shutdown successfully.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error shutting down API service: {ex.Message}");
            }
        }
    }

    public class RelayCommand : ICommand
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

    // New ViewModel for device selection with checkbox
    public class DeviceSelectionViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public DeviceSelectionViewModel(NodeViewModel node)
        {
            Node = node;
            NodeId = node.Id;
            Name = node.Name;
            IsSelected = false; // Default to unselected
        }
        
        public NodeViewModel Node { get; }
        public string NodeId { get; }
        public string Name { get; }
        
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
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 