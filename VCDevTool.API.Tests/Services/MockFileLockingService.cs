using System.Collections.Concurrent;
using VCDevTool.API.Data;
using VCDevTool.Shared;
using Microsoft.EntityFrameworkCore;

namespace VCDevTool.API.Tests.Services
{
    /// <summary>
    /// Mock file locking service for testing that handles SQLite limitations
    /// </summary>
    public class MockFileLockingService
    {
        private readonly ConcurrentDictionary<string, string> _fileLocks = new();
        private readonly AppDbContext _context;

        public MockFileLockingService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<bool> TryAcquireFileLockAsync(string filePath, string nodeId)
        {
            // Normalize path for consistent comparison
            var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();

            // Check if lock already exists in database
            var existingLock = await _context.FileLocks
                .FirstOrDefaultAsync(l => l.FilePath.ToLower() == normalizedPath);

            if (existingLock != null)
            {
                return false; // Lock already exists
            }

            // Try to acquire lock in memory (for concurrent testing)
            if (_fileLocks.TryAdd(normalizedPath, nodeId))
            {
                // Add to database
                var fileLock = new FileLock
                {
                    FilePath = normalizedPath,
                    LockingNodeId = nodeId,
                    AcquiredAt = DateTime.UtcNow,
                    LastUpdatedAt = DateTime.UtcNow
                };

                _context.FileLocks.Add(fileLock);
                
                try
                {
                    await _context.SaveChangesAsync();
                    return true;
                }
                catch
                {
                    // Remove from memory if database save fails
                    _fileLocks.TryRemove(normalizedPath, out _);
                    return false;
                }
            }

            return false;
        }

        public async Task<bool> ReleaseFileLockAsync(string filePath, string nodeId)
        {
            var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();

            // Remove from database
            var fileLock = await _context.FileLocks
                .FirstOrDefaultAsync(l => l.FilePath.ToLower() == normalizedPath && l.LockingNodeId == nodeId);

            if (fileLock != null)
            {
                _context.FileLocks.Remove(fileLock);
                await _context.SaveChangesAsync();
                
                // Remove from memory
                _fileLocks.TryRemove(normalizedPath, out _);
                return true;
            }

            return false;
        }

        public async Task ResetAllFileLocksAsync()
        {
            _fileLocks.Clear();
            
            var allLocks = await _context.FileLocks.ToListAsync();
            _context.FileLocks.RemoveRange(allLocks);
            await _context.SaveChangesAsync();
        }

        public async Task<List<FileLock>> GetActiveLocksAsync()
        {
            return await _context.FileLocks.ToListAsync();
        }
    }
} 