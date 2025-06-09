using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VCDevTool.Client.ViewModels;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Interop;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Threading;
using System.Security;

namespace VCDevTool.Client;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private SolidColorBrush _normalBorderBrush = new SolidColorBrush(Colors.Transparent);
    private SolidColorBrush _dropTargetBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204)); // #007ACC
    private DispatcherTimer _clipboardCheckTimer;
    private string _lastClipboardText;
    private bool _isWaitingForClipboard;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilter(int msg, int action);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterDragDrop(IntPtr hwnd, IntPtr pDropTarget);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll", SetLastError = true)]
    private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

    [DllImport("ole32.dll", SetLastError = true)]
    private static extern void CoUninitialize();

    private const int WM_DROPFILES = 0x0233;
    private const int WM_COPYDATA = 0x004A;
    private const int MSGFLT_ALLOW = 1;
    private const int COINIT_APARTMENTTHREADED = 0x2;

    private IntPtr _dropTargetHandle;

    public MainWindow()
    {
        InitializeComponent();
        
        // Add key down event handler for the window
        this.KeyDown += MainWindow_KeyDown;
        
        // Initialize COM for drag and drop
        CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
        
        // Initialize clipboard monitoring
        _clipboardCheckTimer = new DispatcherTimer();
        _clipboardCheckTimer.Interval = TimeSpan.FromMilliseconds(100);
        _clipboardCheckTimer.Tick += ClipboardCheckTimer_Tick;
        
        // Enable drag and drop regardless of admin privileges
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            try
            {
                ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ALLOW);
                ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ALLOW);
                
                // Register for drag and drop
                RegisterDragDrop(hwnd, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting up drag and drop: {ex.Message}");
            }
        }
        // Check for Volume Compressor executable
        string volumeCompressorPath = Path.Combine(AppContext.BaseDirectory, "volume_compressor.exe"); 
        string volumeCompressorBatchPath = Path.Combine(AppContext.BaseDirectory, "volume_compressor.bat");
        
        if (!File.Exists(volumeCompressorPath) && !File.Exists(volumeCompressorBatchPath))
        {
            try
            {
                // Create a placeholder batch file for demo purposes
                using (var writer = new StreamWriter(volumeCompressorBatchPath))
                {
                    writer.WriteLine("@echo off");
                    writer.WriteLine("echo Volume Compressor Placeholder");
                    writer.WriteLine("echo --------------------------------");
                    writer.WriteLine("echo VideoCopilot VDB compressor");
                    writer.WriteLine("echo.");
                    
                    // Parse arguments
                    writer.WriteLine("set OVERWRITE=0");
                    writer.WriteLine("set USEFIXEDDIMENSION=0");
                    writer.WriteLine("set OUTPUTFILE=");
                    writer.WriteLine("set INPUTFOLDER=");
                    
                    writer.WriteLine(":parseArgs");
                    writer.WriteLine("if \"%~1\"==\"\" goto startProcess");
                    writer.WriteLine("if \"%~1\"==\"-o\" (");
                    writer.WriteLine("    set OUTPUTFILE=%~2");
                    writer.WriteLine("    shift");
                    writer.WriteLine("    shift");
                    writer.WriteLine("    goto parseArgs");
                    writer.WriteLine(")");
                    writer.WriteLine("if \"%~1\"==\"-w\" (");
                    writer.WriteLine("    set OVERWRITE=1");
                    writer.WriteLine("    shift");
                    writer.WriteLine("    goto parseArgs");
                    writer.WriteLine(")");
                    writer.WriteLine("if \"%~1\"==\"-d\" (");
                    writer.WriteLine("    set USEFIXEDDIMENSION=1");
                    writer.WriteLine("    shift");
                    writer.WriteLine("    shift");
                    writer.WriteLine("    goto parseArgs");
                    writer.WriteLine(")");
                    writer.WriteLine("if \"%INPUTFOLDER%\"==\"\" (");
                    writer.WriteLine("    set INPUTFOLDER=%~1");
                    writer.WriteLine("    for /f \"delims=\" %%i in (\"%INPUTFOLDER%\") do set FOLDERNAME=%%~ni");
                    writer.WriteLine("    if \"%OUTPUTFILE%\"==\"\" set OUTPUTFILE=%FOLDERNAME%.vcvol");
                    writer.WriteLine("    echo Input folder: %INPUTFOLDER%");
                    writer.WriteLine(")");
                    writer.WriteLine("shift");
                    writer.WriteLine("goto parseArgs");
                    
                    writer.WriteLine(":startProcess");
                    writer.WriteLine("echo Processing folder: %INPUTFOLDER%");
                    writer.WriteLine("echo Output file: %OUTPUTFILE%");
                    writer.WriteLine("if \"%OVERWRITE%\"==\"1\" echo Overwrite flag is set");
                    writer.WriteLine("if \"%USEFIXEDDIMENSION%\"==\"1\" echo Fixed dimension (-d 512) is set");
                    writer.WriteLine("echo This is a placeholder executable for demo purposes.");
                    writer.WriteLine("echo In a real implementation, all VDBs from the folder would be compressed into a single VCVOL file.");
                    writer.WriteLine("timeout /t 1");
                    writer.WriteLine("echo Creating %OUTPUTFILE% from %INPUTFOLDER% (placeholder)");
                    writer.WriteLine("echo Volume compression complete!");
                    writer.WriteLine("exit /b 0");
                }
                
                System.Windows.MessageBox.Show($"Created Volume Compressor placeholder at: {volumeCompressorBatchPath}\n\nNote: This is just a placeholder. For real compression, please place the actual volume_compressor.exe and its DLLs in this directory.", 
                            "Volume Compressor Setup", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to create Volume Compressor placeholder: {ex.Message}", 
                            "Volume Compressor Setup", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Warning);
            }
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        
        // Clean up drag and drop
        if (_dropTargetHandle != IntPtr.Zero)
        {
            RevokeDragDrop(new WindowInteropHelper(this).Handle);
        }
        
        // Uninitialize COM
        CoUninitialize();
    }
    
    private bool IsRunAsAdmin()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    } 
    
    private void DirectoryTextBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        bool isValid = false;
        try
        {
            // Check if the data contains file drop
            isValid = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop);
            if (isValid)
            {
                // Get the data
                string[] droppedItems = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                
                // Check if at least one path is a directory
                isValid = false;
                if (droppedItems?.Length > 0)
                {
                    foreach (string path in droppedItems)
                    {
                        if (Directory.Exists(path))
                        {
                            isValid = true;
                            break;
                        }
                    }
                }
            }
            
            // Update effects and visual feedback
            e.Effects = isValid ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            
            // Change border color when valid folder is dragged over
            DirectoryBorder.BorderBrush = isValid ? _dropTargetBrush : _normalBorderBrush;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error during drag over: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Effects = System.Windows.DragDropEffects.None;
            DirectoryBorder.BorderBrush = _normalBorderBrush;
        }
        
        // Mark as handled so TextBox doesn't interfere
        e.Handled = true;
    }
    private void DirectoryTextBox_PreviewDrop(object sender, System.Windows.DragEventArgs e)
    {
        try
        {
            // Check if the data contains file drop
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                // Get the data
                string[] droppedItems = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);

                if (droppedItems?.Length > 0)    
                {
                    // Get the view model
                    var viewModel = DataContext as MainViewModel;
                    if (viewModel != null)
                    {
                        // Check if the configured hotkey is pressed
                        bool isHotkeyPressed = false;
                        if (viewModel.AddFilesHotkey == System.Windows.Forms.Keys.Shift)
                        {
                            isHotkeyPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                        }
                        else if (viewModel.AddFilesHotkey == System.Windows.Forms.Keys.Control)
                        {
                            isHotkeyPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                        }
                        else if (viewModel.AddFilesHotkey == System.Windows.Forms.Keys.Alt)
                        {
                            isHotkeyPressed = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
                        }
                        
                        // If not in "add" mode (hotkey not pressed), clear existing directories first
                        if (!isHotkeyPressed)
                        {
                            viewModel.ClearDirectories();
                        }
                        
                        // Add each valid directory to the collection
                        foreach (string path in droppedItems)
                        {
                            if (Directory.Exists(path))
                            {
                                // Since we've already handled the clearing above, always add in this case
                                viewModel.AddDirectory(path, true);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error during drag and drop: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        
        // Mark as handled so TextBox doesn't interfere
        e.Handled = true;
    }
    
    private void ClearDirectories_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearDirectories();
        }
    }
    
    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Get the view model
        if (DataContext is MainViewModel viewModel && viewModel.IsWaitingForKeyPress)
        {
            // Convert WPF key to Windows Forms key
            System.Windows.Forms.Keys key = System.Windows.Forms.Keys.None;
            
            if (e.Key == System.Windows.Input.Key.LeftShift || e.Key == System.Windows.Input.Key.RightShift)
                key = System.Windows.Forms.Keys.Shift;
            else if (e.Key == System.Windows.Input.Key.LeftCtrl || e.Key == System.Windows.Input.Key.RightCtrl)
                key = System.Windows.Forms.Keys.Control;
            else if (e.Key == System.Windows.Input.Key.LeftAlt || e.Key == System.Windows.Input.Key.RightAlt)
                key = System.Windows.Forms.Keys.Alt;
            else
                key = (System.Windows.Forms.Keys)System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.Key);
            
            viewModel.HandleKeyPress(key);
            e.Handled = true;
        }
    }

    private void ClipboardCheckTimer_Tick(object sender, EventArgs e)
    {
        if (!_isWaitingForClipboard) return;

        try
        {
            string currentClipboard = System.Windows.Clipboard.GetText();
            if (currentClipboard != _lastClipboardText && !string.IsNullOrEmpty(currentClipboard))
            {
                _isWaitingForClipboard = false;
                _clipboardCheckTimer.Stop();
                
                // Process the clipboard content
                ProcessClipboardContent(currentClipboard);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error checking clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _isWaitingForClipboard = false;
            _clipboardCheckTimer.Stop();
        }
    }

    private void ProcessClipboardContent(string content)
    {
        try
        {
            // Split the content into lines (assuming each path is on a new line)
            string[] paths = content.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            
            if (DataContext is MainViewModel viewModel)
            {
                bool isHotkeyPressed = false;
                
                // Check if the configured hotkey is pressed
                if (viewModel.AddFilesHotkey == System.Windows.Forms.Keys.Shift)
                {
                    isHotkeyPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                }
                else if (viewModel.AddFilesHotkey == System.Windows.Forms.Keys.Control)
                {
                    isHotkeyPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                }
                else if (viewModel.AddFilesHotkey == System.Windows.Forms.Keys.Alt)
                {
                    isHotkeyPressed = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
                }
                
                // If not in "add" mode (hotkey not pressed), clear existing directories first
                if (!isHotkeyPressed)
                {
                    viewModel.ClearDirectories();
                }
                
                // Add each valid directory to the collection
                foreach (string path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        viewModel.AddDirectory(path, true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error processing clipboard content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Event handlers for custom window controls
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
            this.WindowState = WindowState.Normal;
        else
            this.WindowState = WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
    
    // Make the window draggable
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        
        // Allow dragging the window when clicking on any non-control area
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            // Make sure we're not clicking on a control
            if (e.Source is Window || 
                ((e.Source is FrameworkElement element) && 
                !(element is System.Windows.Controls.Button) && 
                !(element is System.Windows.Controls.TextBox) && 
                !(element is System.Windows.Controls.Primitives.ToggleButton)))
            {
                this.DragMove();
            }
        }
    }
}