-- VCDevTool Database Initialization Script
-- This script creates the production database and user with appropriate permissions

USE master;
GO

-- Create the database if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'VCDevToolDb')
BEGIN
    CREATE DATABASE VCDevToolDb
    COLLATE SQL_Latin1_General_CP1_CI_AS;
    
    ALTER DATABASE VCDevToolDb SET RECOVERY FULL;
    ALTER DATABASE VCDevToolDb SET AUTO_CLOSE OFF;
    ALTER DATABASE VCDevToolDb SET AUTO_SHRINK OFF;
    ALTER DATABASE VCDevToolDb SET READ_COMMITTED_SNAPSHOT ON;
    
    PRINT 'Database VCDevToolDb created successfully';
END
ELSE
BEGIN
    PRINT 'Database VCDevToolDb already exists';
END
GO

-- Switch to the VCDevTool database
USE VCDevToolDb;
GO

-- Create application user if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = 'vcdevtool_user')
BEGIN
    -- Create login at server level
    IF NOT EXISTS (SELECT name FROM sys.server_principals WHERE name = 'vcdevtool_user')
    BEGIN
        CREATE LOGIN vcdevtool_user WITH PASSWORD = 'SecurePassword123!';
        PRINT 'Login vcdevtool_user created successfully';
    END
    
    -- Create user in the database
    CREATE USER vcdevtool_user FOR LOGIN vcdevtool_user;
    
    -- Grant necessary permissions
    ALTER ROLE db_datareader ADD MEMBER vcdevtool_user;
    ALTER ROLE db_datawriter ADD MEMBER vcdevtool_user;
    ALTER ROLE db_ddladmin ADD MEMBER vcdevtool_user;
    
    -- Grant specific permissions for Entity Framework migrations
    GRANT CREATE TABLE TO vcdevtool_user;
    GRANT ALTER ON SCHEMA::dbo TO vcdevtool_user;
    GRANT VIEW DEFINITION TO vcdevtool_user;
    GRANT VIEW DATABASE STATE TO vcdevtool_user;
    
    PRINT 'User vcdevtool_user created and configured successfully';
END
ELSE
BEGIN
    PRINT 'User vcdevtool_user already exists';
END
GO

-- Create backup directory if needed
EXEC xp_create_subdir 'C:\var\opt\mssql\backup';
GO

-- Set database optimization options for production
ALTER DATABASE VCDevToolDb SET PARAMETERIZATION FORCED;
ALTER DATABASE VCDevToolDb SET QUERY_STORE = ON;
ALTER DATABASE VCDevToolDb SET QUERY_STORE (
    OPERATION_MODE = READ_WRITE,
    CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30),
    DATA_FLUSH_INTERVAL_SECONDS = 900,
    INTERVAL_LENGTH_MINUTES = 60,
    MAX_STORAGE_SIZE_MB = 100,
    QUERY_CAPTURE_MODE = AUTO,
    SIZE_BASED_CLEANUP_MODE = AUTO
);
GO

-- Enable backup compression for production
EXEC sp_configure 'backup compression default', 1;
RECONFIGURE;
GO

PRINT 'Database initialization completed successfully';
GO 