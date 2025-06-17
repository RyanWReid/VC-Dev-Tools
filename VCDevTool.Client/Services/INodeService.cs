using System.Threading.Tasks;
using VCDevTool.Shared;

namespace VCDevTool.Client.Services
{
    public interface INodeService
    {
        ComputerNode CurrentNode { get; }
        Task<ComputerNode> RegisterNodeAsync();
        void UpdateApiClient(ApiClient apiClient);
        void SetNodeAvailability(bool isAvailable);
    }
} 