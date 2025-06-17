using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCDevTool.Shared;

namespace VCDevTool.Client.Services
{
    public static class ApiClientExtensions
    {
        public static async Task<List<BatchTask>> GetAllTasksAsync(this ApiClient apiClient)
        {
            return await apiClient.GetTasksAsync();
        }
    }
} 