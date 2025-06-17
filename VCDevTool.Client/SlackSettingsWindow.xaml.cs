using System;
using System.Windows;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using VCDevTool.Client.Services;

namespace VCDevTool.Client
{
    public partial class SlackSettingsWindow : Window
    {
        private readonly SlackNotificationService _slackService;
        private readonly string _configFilePath;
        private bool _isTestingConnection = false;

        public SlackSettingsWindow(SlackNotificationService slackService)
        {
            InitializeComponent();
            
            _slackService = slackService;
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "slackconfig.json");
            
            LoadSettings();
        }
        
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var settings = JsonSerializer.Deserialize<SlackSettings>(json);
                     
                    if (settings != null)
                    {
                        WebhookUrlTextBox.Text = settings.WebhookUrl ?? "";
                        EnableNotificationsCheckBox.IsChecked = settings.IsEnabled;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error loading settings: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
        
        private void SaveSettings()
        {
            try
            {
                string url = EnableNotificationsCheckBox.IsChecked == true 
                    ? WebhookUrlTextBox.Text.Trim() 
                    : "";
                
                var settings = new SlackSettings
                {
                    WebhookUrl = url,
                    IsEnabled = EnableNotificationsCheckBox.IsChecked == true
                };
                
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
                
                // Configure the service with the new URL
                _slackService.Configure(url);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error saving settings: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
        
        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (_isTestingConnection) return;
            
            string url = WebhookUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                StatusTextBlock.Text = "Please enter a webhook URL first.";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }
            
            try
            {
                _isTestingConnection = true;
                TestButton.IsEnabled = false;
                StatusTextBlock.Text = "Sending test notification...";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.White;
                
                // Temporarily configure the service with this URL for testing
                _slackService.Configure(url);
                
                await _slackService.SendTestNotificationAsync();
                
                StatusTextBlock.Text = "Test notification sent successfully! Check your Slack channel.";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Failed to send test notification: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                _isTestingConnection = false;
                TestButton.IsEnabled = true;
            }
        }
        
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            DialogResult = true;
            Close();
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 