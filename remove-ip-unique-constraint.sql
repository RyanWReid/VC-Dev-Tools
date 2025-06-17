-- Remove IP Address Unique Constraint Migration
-- This fixes authentication issues where random 127.x.x.x IP addresses
-- cause unique constraint violations

USE VCDevTool;
GO

-- Check if the unique constraint exists first
IF EXISTS (
    SELECT * FROM sys.indexes 
    WHERE object_id = OBJECT_ID('Nodes') 
    AND name = 'IX_Nodes_IpAddress' 
    AND is_unique = 1
)
BEGIN
    PRINT 'Dropping unique constraint on IpAddress column...'
    DROP INDEX IX_Nodes_IpAddress ON Nodes;
    
    -- Recreate as non-unique index for performance
    CREATE INDEX IX_Nodes_IpAddress ON Nodes (IpAddress);
    PRINT 'Successfully converted unique constraint to regular index.'
END
ELSE
BEGIN
    PRINT 'Unique constraint on IpAddress does not exist or already removed.'
    
    -- Ensure we have a regular index for performance
    IF NOT EXISTS (
        SELECT * FROM sys.indexes 
        WHERE object_id = OBJECT_ID('Nodes') 
        AND name = 'IX_Nodes_IpAddress'
    )
    BEGIN
        CREATE INDEX IX_Nodes_IpAddress ON Nodes (IpAddress);
        PRINT 'Created regular index on IpAddress column.'
    END
END

GO

PRINT 'Migration completed successfully!'