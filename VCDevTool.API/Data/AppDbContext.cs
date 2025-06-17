using Microsoft.EntityFrameworkCore;
using VCDevTool.Shared;

namespace VCDevTool.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<BatchTask> Tasks { get; set; }
        public DbSet<ComputerNode> Nodes { get; set; }
        public DbSet<FileLock> FileLocks { get; set; }
        public DbSet<TaskFolderProgress> TaskFolderProgress { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure ComputerNode
            modelBuilder.Entity<ComputerNode>(entity =>
            {
                entity.HasKey(n => n.Id);
                
                // Note: IP address is not unique - same IP can be shared by multiple nodes
                // due to NAT, VPN, containerization, etc.
                entity.HasIndex(n => n.IpAddress)
                    .HasDatabaseName("IX_Nodes_IpAddress");
                
                // Index for heartbeat monitoring and availability queries
                entity.HasIndex(n => new { n.IsAvailable, n.LastHeartbeat })
                    .HasDatabaseName("IX_Nodes_Availability_Heartbeat");
                
                // Index for node name searches
                entity.HasIndex(n => n.Name)
                    .HasDatabaseName("IX_Nodes_Name");

                // Active Directory indexes
                entity.HasIndex(n => n.ActiveDirectoryName)
                    .HasDatabaseName("IX_Nodes_ADName");
                
                entity.HasIndex(n => n.DistinguishedName)
                    .HasDatabaseName("IX_Nodes_DistinguishedName");
                
                entity.HasIndex(n => new { n.IsAdEnabled, n.LastAdSync })
                    .HasDatabaseName("IX_Nodes_ADEnabled_LastSync");

                // Configure string lengths for better performance
                entity.Property(n => n.Id)
                    .HasMaxLength(50);
                
                entity.Property(n => n.Name)
                    .HasMaxLength(100)
                    .IsRequired();
                
                entity.Property(n => n.IpAddress)
                    .HasMaxLength(45) // IPv6 max length
                    .IsRequired();
                
                entity.Property(n => n.HardwareFingerprint)
                    .HasMaxLength(256);

                // Active Directory properties
                entity.Property(n => n.ActiveDirectoryName)
                    .HasMaxLength(100);
                
                entity.Property(n => n.DomainController)
                    .HasMaxLength(255);
                
                entity.Property(n => n.OrganizationalUnit)
                    .HasMaxLength(500);
                
                entity.Property(n => n.DistinguishedName)
                    .HasMaxLength(1000);
                
                entity.Property(n => n.DnsHostName)
                    .HasMaxLength(255);
                
                entity.Property(n => n.OperatingSystem)
                    .HasMaxLength(200);
                
                entity.Property(n => n.AdGroups)
                    .HasMaxLength(4000); // JSON array of group memberships
                
                entity.Property(n => n.ServicePrincipalName)
                    .HasMaxLength(500);
            });

            // Configure BatchTask
            modelBuilder.Entity<BatchTask>(entity =>
            {
                entity.HasKey(t => t.Id);
                
                entity.Property(t => t.Id)
                    .ValueGeneratedOnAdd();
                    
                // Configure RowVersion for optimistic concurrency
                entity.Property(t => t.RowVersion)
                    .IsRowVersion();

                // Configure AssignedNodeIds to map to AssignedNodeIdsJson column
                entity.Property(t => t.AssignedNodeIds)
                    .HasColumnName("AssignedNodeIdsJson")
                    .HasMaxLength(2000); // Reasonable limit for JSON array

                // Critical performance indexes
                entity.HasIndex(t => t.Status)
                    .HasDatabaseName("IX_Tasks_Status");
                
                entity.HasIndex(t => t.Type)
                    .HasDatabaseName("IX_Tasks_Type");
                
                entity.HasIndex(t => t.AssignedNodeId)
                    .HasDatabaseName("IX_Tasks_AssignedNodeId");
                
                // Composite index for common queries (status + created date)
                entity.HasIndex(t => new { t.Status, t.CreatedAt })
                    .HasDatabaseName("IX_Tasks_Status_CreatedAt");
                
                // Composite index for node assignment queries
                entity.HasIndex(t => new { t.AssignedNodeId, t.Status })
                    .HasDatabaseName("IX_Tasks_AssignedNodeId_Status");
                
                // Index for time-based queries
                entity.HasIndex(t => t.CreatedAt)
                    .HasDatabaseName("IX_Tasks_CreatedAt");
                
                entity.HasIndex(t => t.StartedAt)
                    .HasDatabaseName("IX_Tasks_StartedAt");
                
                entity.HasIndex(t => t.CompletedAt)
                    .HasDatabaseName("IX_Tasks_CompletedAt");

                // Configure string lengths
                entity.Property(t => t.Name)
                    .HasMaxLength(200)
                    .IsRequired();
                
                entity.Property(t => t.AssignedNodeId)
                    .HasMaxLength(50);
                
                entity.Property(t => t.Parameters)
                    .HasMaxLength(4000); // JSON parameters
                
                entity.Property(t => t.ResultMessage)
                    .HasMaxLength(2000);
            });

            // Configure FileLock
            modelBuilder.Entity<FileLock>(entity =>
            {
                entity.HasKey(l => l.Id);
                
                entity.HasIndex(l => l.FilePath)
                    .IsUnique();
                
                // Index for cleanup operations (old locks)
                entity.HasIndex(l => new { l.LastUpdatedAt, l.LockingNodeId })
                    .HasDatabaseName("IX_FileLocks_LastUpdated_NodeId");
                
                // Index for node-specific lock queries
                entity.HasIndex(l => l.LockingNodeId)
                    .HasDatabaseName("IX_FileLocks_LockingNodeId");
                
                // Index for lock acquisition time
                entity.HasIndex(l => l.AcquiredAt)
                    .HasDatabaseName("IX_FileLocks_AcquiredAt");

                // Configure string lengths
                entity.Property(l => l.FilePath)
                    .HasMaxLength(500)
                    .IsRequired();
                
                entity.Property(l => l.LockingNodeId)
                    .HasMaxLength(50)
                    .IsRequired();
            });

            // Configure TaskFolderProgress
            modelBuilder.Entity<TaskFolderProgress>(entity =>
            {
                entity.HasKey(tfp => tfp.Id);
                
                entity.Property(tfp => tfp.Id)
                    .ValueGeneratedOnAdd();

                entity.HasIndex(tfp => new { tfp.TaskId, tfp.FolderPath })
                    .IsUnique();
                
                // Performance indexes for common queries
                entity.HasIndex(tfp => tfp.TaskId)
                    .HasDatabaseName("IX_TaskFolderProgress_TaskId");
                
                entity.HasIndex(tfp => tfp.Status)
                    .HasDatabaseName("IX_TaskFolderProgress_Status");
                
                entity.HasIndex(tfp => new { tfp.Status, tfp.AssignedNodeId })
                    .HasDatabaseName("IX_TaskFolderProgress_Status_NodeId");
                
                entity.HasIndex(tfp => tfp.CreatedAt)
                    .HasDatabaseName("IX_TaskFolderProgress_CreatedAt");

                // Foreign key relationship to BatchTask
                entity.HasOne<BatchTask>()
                    .WithMany()
                    .HasForeignKey(tfp => tfp.TaskId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Configure string lengths
                entity.Property(tfp => tfp.FolderPath)
                    .HasMaxLength(500)
                    .IsRequired();
                
                entity.Property(tfp => tfp.FolderName)
                    .HasMaxLength(255)
                    .IsRequired();
                
                entity.Property(tfp => tfp.AssignedNodeId)
                    .HasMaxLength(50);
                
                entity.Property(tfp => tfp.AssignedNodeName)
                    .HasMaxLength(100);
                
                entity.Property(tfp => tfp.ErrorMessage)
                    .HasMaxLength(2000);
                
                entity.Property(tfp => tfp.OutputPath)
                    .HasMaxLength(500);
            });
        }
    }
} 