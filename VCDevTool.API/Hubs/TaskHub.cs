using Microsoft.AspNetCore.SignalR;

namespace VCDevTool.API.Hubs
{
    public class TaskHub : Hub
    {
        // This hub will be used to broadcast task notifications
        // No custom methods needed as we'll use the built-in SendAsync from IHubContext
    }
} 