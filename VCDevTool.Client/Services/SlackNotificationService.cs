using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VCDevTool.Shared;

namespace VCDevTool.Client.Services
{
    public class SlackNotificationService
    {
        private readonly HttpClient _httpClient;
        private string _webhookUrl;

        public bool IsEnabled => !string.IsNullOrEmpty(_webhookUrl);

        public SlackNotificationService()
        {
            _httpClient = new HttpClient();
            _webhookUrl = "https://hooks.slack.com/services/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";
        }

        public void Configure(string webhookUrl)
        {
            _webhookUrl = webhookUrl;
        }

        public async Task SendTaskCompletionNotificationAsync(string nodeName, BatchTask task)
        {
            if (!IsEnabled) return;

            try
            {
                var duration = task.CompletedAt - task.StartedAt;
                string formattedDuration = duration.HasValue 
                    ? $"{duration.Value.Hours:D2}:{duration.Value.Minutes:D2}:{duration.Value.Seconds:D2}" 
                    : "Unknown";

                var message = new
                {
                    blocks = new object[]
                    {
                        new
                        {
                            type = "header",
                            text = new
                            {
                                type = "plain_text",
                                text = "✅ Task Completed"
                            }
                        },
                        new
                        {
                            type = "section",
                            fields = new object[]
                            {
                                new
                                {
                                    type = "mrkdwn",
                                    text = $"*Node:*\n{nodeName}"
                                },
                                new
                                {
                                    type = "mrkdwn",
                                    text = $"*Task Type:*\n{task.Type}"
                                },
                                new
                                {
                                    type = "mrkdwn",
                                    text = $"*Started:*\n{task.StartedAt?.ToString("MM/dd/yyyy h:mm tt") ?? "Unknown"}"
                                },
                                new
                                {
                                    type = "mrkdwn",
                                    text = $"*Duration:*\n{formattedDuration}"
                                }
                            }
                        },
                        new
                        {
                            type = "divider"
                        }
                    }
                };

                var json = JsonSerializer.Serialize(message);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(_webhookUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error sending Slack notification: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending Slack notification: {ex.Message}");
                // Consider logging this error more formally
            }
        }

        public async Task SendTestNotificationAsync()
        {
            if (!IsEnabled) return;

            try
            {
                var message = new
                {
                    blocks = new object[]
                    {
                        new
                        {
                            type = "header",
                            text = new
                            {
                                type = "plain_text",
                                text = "🔔 Test Notification"
                            }
                        },
                        new
                        {
                            type = "section",
                            text = new
                            {
                                type = "mrkdwn",
                                text = "This is a test notification from VC Dev Tool. If you can see this, your Slack integration is working correctly!"
                            }
                        },
                        new
                        {
                            type = "divider"
                        }
                    }
                };

                var json = JsonSerializer.Serialize(message);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(_webhookUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Slack responded with status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send test notification: {ex.Message}", ex);
            }
        }
    }
} 