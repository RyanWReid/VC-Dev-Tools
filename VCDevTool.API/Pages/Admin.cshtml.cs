using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VCDevTool.API.Data;
using VCDevTool.API.Services;
using VCDevTool.Shared;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace VCDevTool.API.Pages
{
    public class AdminModel : PageModel
    {
        private readonly AppDbContext _dbContext;
        private readonly ITaskService _taskService;
        
        public AdminModel(AppDbContext dbContext, ITaskService taskService)
        {
            _dbContext = dbContext;
            _taskService = taskService;
        }

        public List<FileLock> FileLocks { get; set; } = new();
        public List<BatchTask> Tasks { get; set; } = new();
        public List<ComputerNode> Nodes { get; set; } = new();
        
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync()
        {
            FileLocks = await _dbContext.FileLocks.AsNoTracking().ToListAsync();
            Tasks = await _dbContext.Tasks.AsNoTracking().OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync();
            Nodes = await _dbContext.Nodes.AsNoTracking().OrderBy(n => n.Name).ToListAsync();
            
            // Check if we have a message from a previous redirect
            StatusMessage = TempData["Message"]?.ToString();
        }
        
        public async Task<IActionResult> OnPostClearLocksAsync()
        {
            var result = await _taskService.ResetAllFileLocksAsync();
            TempData["Message"] = result 
                ? "All file locks have been cleared successfully." 
                : "Failed to clear file locks. Check logs for details.";
            return RedirectToPage();
        }
        
        public async Task<IActionResult> OnPostClearOrphanedLocksAsync()
        {
            try
            {
                // Get all current locks
                var locks = await _dbContext.FileLocks.ToListAsync();
                
                // Get all active nodes
                var activeNodes = await _dbContext.Nodes
                    .Where(n => n.IsAvailable)
                    .Select(n => n.Id)
                    .ToListAsync();
                
                // Find orphaned locks (locks owned by non-existent or inactive nodes)
                var orphanedLocks = locks
                    .Where(l => !activeNodes.Contains(l.LockingNodeId))
                    .ToList();
                
                if (orphanedLocks.Any())
                {
                    // Remove orphaned locks
                    _dbContext.FileLocks.RemoveRange(orphanedLocks);
                    await _dbContext.SaveChangesAsync();
                    
                    TempData["Message"] = $"Successfully cleared {orphanedLocks.Count} orphaned locks.";
                }
                else
                {
                    TempData["Message"] = "No orphaned locks found.";
                }
                
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                TempData["Message"] = $"Error clearing orphaned locks: {ex.Message}";
                return RedirectToPage();
            }
        }
        
        public async Task<IActionResult> OnPostClearTasksAsync()
        {
            try
            {
                // Clear completed/failed tasks that are older than 24 hours
                var cutoffTime = System.DateTime.UtcNow.AddHours(-24);
                var tasksToRemove = await _dbContext.Tasks
                    .Where(t => (t.Status == BatchTaskStatus.Completed || 
                                t.Status == BatchTaskStatus.Failed || 
                                t.Status == BatchTaskStatus.Cancelled) && 
                                t.CompletedAt < cutoffTime)
                    .ToListAsync();
                
                _dbContext.Tasks.RemoveRange(tasksToRemove);
                await _dbContext.SaveChangesAsync();
                
                TempData["Message"] = $"Successfully removed {tasksToRemove.Count} completed/failed tasks.";
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                TempData["Message"] = $"Error clearing tasks: {ex.Message}";
                return RedirectToPage();
            }
        }
        
        public async Task<IActionResult> OnPostClearAllTasksAsync()
        {
            try
            {
                // Get all tasks in the database
                var allTasks = await _dbContext.Tasks.ToListAsync();
                
                if (allTasks.Any())
                {
                    _dbContext.Tasks.RemoveRange(allTasks);
                    await _dbContext.SaveChangesAsync();
                    
                    TempData["Message"] = $"Successfully removed all {allTasks.Count} tasks from the database.";
                }
                else
                {
                    TempData["Message"] = "No tasks found to remove.";
                }
                
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                TempData["Message"] = $"Error clearing all tasks: {ex.Message}";
                return RedirectToPage();
            }
        }
        
        public async Task<IActionResult> OnPostDisconnectNodeAsync(string nodeId)
        {
            try
            {
                var node = await _dbContext.Nodes.FindAsync(nodeId);
                if (node != null)
                {
                    // Mark node as unavailable
                    node.IsAvailable = false;
                    
                    // Release any locks held by this node
                    var nodeLocks = await _dbContext.FileLocks
                        .Where(l => l.LockingNodeId == nodeId)
                        .ToListAsync();
                    
                    _dbContext.FileLocks.RemoveRange(nodeLocks);
                    
                    // Optionally, reset tasks assigned to this node back to pending
                    var nodeTasks = await _dbContext.Tasks
                        .Where(t => t.AssignedNodeId == nodeId && t.Status == BatchTaskStatus.Running)
                        .ToListAsync();
                    
                    foreach (var task in nodeTasks)
                    {
                        task.Status = BatchTaskStatus.Pending;
                        task.AssignedNodeId = null;
                    }
                    
                    await _dbContext.SaveChangesAsync();
                    TempData["Message"] = $"Node '{node.Name}' has been disconnected.";
                }
                else
                {
                    TempData["Message"] = $"Node with ID '{nodeId}' not found.";
                }
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                TempData["Message"] = $"Error disconnecting node: {ex.Message}";
                return RedirectToPage();
            }
        }
    }
} 