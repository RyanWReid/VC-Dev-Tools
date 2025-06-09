using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using System.Text.Json;
using VCDevTool.Client.ViewModels;
using VCDevTool.Client.Services;

namespace VCDevTool.Client;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public SlackNotificationService? SlackService { get; private set; }
    public DebugHubClient? DebugHubClient { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Initialize the Slack notification service
        SlackService = new SlackNotificationService();
        
        // Default server address for debug hub client
        string serverAddress = "http://192.168.3.34:5289";
        
        // Initialize debug hub client
        DebugHubClient = new DebugHubClient(serverAddress);
        
        // Load Slack settings if available
        try
        {
            string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "slackconfig.json");
            if (File.Exists(configFilePath))
            {
                string json = File.ReadAllText(configFilePath);
                var settings = JsonSerializer.Deserialize<SlackSettings>(json);
                
                if (settings != null && settings.IsEnabled && !string.IsNullOrEmpty(settings.WebhookUrl))
                {
                    SlackService.Configure(settings.WebhookUrl);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading Slack settings: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clean up debug hub connection
        try
        {
            DebugHubClient?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disposing debug hub client: {ex.Message}");
        }
    
        // Ensure we clean up any running API processes started by this application
        try
        {
            var mainViewModel = Current.MainWindow?.DataContext as MainViewModel;
            mainViewModel?.ShutdownApiService();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during application exit: {ex.Message}");
        }
        
        base.OnExit(e);
    }
}

