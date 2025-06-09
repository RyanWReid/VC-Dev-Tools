using Microsoft.AspNetCore.SignalR;

namespace VCDevTool.API.Hubs
{
    public class DebugHub : Hub
    {
        // This hub will be used to broadcast debug messages
        // No custom methods needed as we'll use the built-in SendAsync from IHubContext
    }
} 