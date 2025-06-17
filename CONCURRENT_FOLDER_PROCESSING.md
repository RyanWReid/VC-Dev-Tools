# Concurrent Folder Processing for Volume Compression

## Overview

The volume compression task now supports **concurrent folder-level processing** where multiple nodes can work simultaneously on different folders containing VDB files.

## How It Works

### 1. Folder-Level Locking
- Each folder containing VDB files gets its own lock
- Only one node can process a specific folder at a time
- Multiple nodes can work on different folders simultaneously

### 2. Work-Stealing Algorithm
Instead of processing folders sequentially, each node:
1. **Continuously looks** for available (unlocked) folders
2. **Attempts to acquire** a lock on the first available folder
3. **Processes all VDB files** in that folder
4. **Releases the lock** and looks for the next available folder
5. **Repeats** until all folders are processed

### 3. Dynamic Load Balancing
- Nodes automatically find work without manual coordination
- Faster nodes will process more folders
- If a node fails, other nodes continue with remaining folders

## Key Benefits

✅ **True Parallelism**: Multiple nodes work simultaneously  
✅ **Automatic Load Balancing**: Faster nodes process more work  
✅ **Fault Tolerance**: Node failures don't block other nodes  
✅ **Efficient Resource Usage**: No idle nodes waiting for others  

## Example Scenario

**Before (Sequential)**:
```
Node A: Folder1 → Folder2 → Folder3 → Folder4
Node B: [waits] → [waits] → [waits] → [waits]
```

**After (Concurrent)**:
```
Node A: Folder1 → Folder3 → Folder5 → ...
Node B: Folder2 → Folder4 → Folder6 → ...
```

## Monitoring

The system provides detailed logging showing:
- Which node acquired which folder lock
- Processing progress for each folder
- When locks are released
- Overall task completion status

## Task Completion

A background service (`TaskCompletionService`) automatically detects when all folders are processed and marks the overall task as completed.

## Configuration

No additional configuration is required. The system automatically:
- Detects available folders during task creation (pre-scan)
- Creates folder progress records in the database
- Manages locks through the central coordination store
- Handles cleanup and error recovery

## Troubleshooting

If you see only one node working:
1. Check that multiple nodes are assigned to the task
2. Verify folder locks are being released properly
3. Look for error messages in the debug output
4. Ensure the database connection is working on all nodes 