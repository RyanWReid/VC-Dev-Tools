@{
    # Script module or binary module file associated with this manifest
    RootModule = 'VCDevTool.psm1'

    # Version number of this module
    ModuleVersion = '1.0.0'

    # Supported PSEditions
    CompatiblePSEditions = @('Desktop', 'Core')

    # ID used to uniquely identify this module
    GUID = 'a1b2c3d4-e5f6-4789-a012-3456789abcde'

    # Author of this module
    Author = 'VCDevTool Development Team'

    # Company or vendor of this module
    CompanyName = 'Enterprise IT'

    # Copyright statement for this module
    Copyright = '(c) 2024 Enterprise IT. All rights reserved.'

    # Description of the functionality provided by this module
    Description = 'PowerShell module for managing VCDevTool tasks, nodes, and system administration.'

    # Minimum version of the PowerShell engine required by this module
    PowerShellVersion = '5.1'

    # Minimum version of Microsoft .NET Framework required by this module
    DotNetFrameworkVersion = '4.7.2'

    # Minimum version of the common language runtime (CLR) required by this module
    CLRVersion = '4.0'

    # Processor architecture (None, X86, Amd64) required by this module
    ProcessorArchitecture = 'None'

    # Modules that must be imported into the global environment prior to importing this module
    RequiredModules = @()

    # Assemblies that must be loaded prior to importing this module
    RequiredAssemblies = @()

    # Script files (.ps1) that are run in the caller's environment prior to importing this module
    ScriptsToProcess = @()

    # Type files (.ps1xml) to be loaded when importing this module
    TypesToProcess = @()

    # Format files (.ps1xml) to be loaded when importing this module
    FormatsToProcess = @('VCDevTool.Format.ps1xml')

    # Modules to import as nested modules of the module specified in RootModule/ModuleToProcess
    NestedModules = @()

    # Functions to export from this module
    FunctionsToExport = @(
        # Task Management
        'Get-VCTask',
        'Start-VCTask',
        'Stop-VCTask',
        'Restart-VCTask',
        'Remove-VCTask',
        'Set-VCTaskPriority',
        'Get-VCTaskHistory',
        'Export-VCTaskReport',
        
        # Node Management
        'Get-VCNode',
        'Register-VCNode',
        'Unregister-VCNode',
        'Test-VCNode',
        'Get-VCNodeHealth',
        'Set-VCNodeConfiguration',
        'Restart-VCNode',
        
        # System Administration
        'Get-VCSystemStatus',
        'Test-VCConnection',
        'Set-VCConfiguration',
        'Get-VCConfiguration',
        'Reset-VCConfiguration',
        'Backup-VCDatabase',
        'Get-VCLogs',
        'Clear-VCLogs',
        
        # Monitoring and Analytics
        'Get-VCPerformanceMetrics',
        'Get-VCTaskStatistics',
        'Get-VCNodeStatistics',
        'Export-VCDiagnostics',
        
        # Security and Compliance
        'Get-VCAuditLog',
        'Export-VCAuditReport',
        'Test-VCSecurity',
        
        # Utility Functions
        'Connect-VCDevTool',
        'Disconnect-VCDevTool',
        'Get-VCVersion'
    )

    # Cmdlets to export from this module
    CmdletsToExport = @()

    # Variables to export from this module
    VariablesToExport = @()

    # Aliases to export from this module
    AliasesToExport = @(
        'gvct',      # Get-VCTask
        'svct',      # Start-VCTask
        'spvct',     # Stop-VCTask
        'gvcn',      # Get-VCNode
        'gvcss',     # Get-VCSystemStatus
        'tvcn'       # Test-VCNode
    )

    # DSC resources to export from this module
    DscResourcesToExport = @()

    # List of all modules packaged with this module
    ModuleList = @()

    # List of all files packaged with this module
    FileList = @(
        'VCDevTool.psm1',
        'VCDevTool.psd1',
        'VCDevTool.Format.ps1xml',
        'Private\ConnectionManager.ps1',
        'Private\ApiHelper.ps1',
        'Private\ValidationHelper.ps1',
        'Public\TaskManagement.ps1',
        'Public\NodeManagement.ps1',
        'Public\SystemAdministration.ps1',
        'Public\Monitoring.ps1',
        'Public\Security.ps1',
        'Public\Utilities.ps1'
    )

    # Private data to pass to the module specified in RootModule/ModuleToProcess
    PrivateData = @{
        PSData = @{
            # Tags applied to this module
            Tags = @('VCDevTool', 'TaskManagement', 'Enterprise', 'Administration', 'Automation')

            # A URL to the license for this module
            LicenseUri = ''

            # A URL to the main website for this project
            ProjectUri = ''

            # A URL to an icon representing this module
            IconUri = ''

            # ReleaseNotes of this module
            ReleaseNotes = @'
Version 1.0.0
- Initial release of VCDevTool PowerShell module
- Complete task management functionality
- Node administration capabilities
- System monitoring and diagnostics
- Security and compliance features
'@

            # Prerelease string of this module
            Prerelease = ''

            # Flag to indicate whether the module requires explicit user acceptance for install/update
            RequireLicenseAcceptance = $false

            # External dependent modules of this module
            ExternalModuleDependencies = @()
        }
    }

    # HelpInfo URI of this module
    HelpInfoURI = ''

    # Default prefix for commands exported from this module
    DefaultCommandPrefix = ''
} 