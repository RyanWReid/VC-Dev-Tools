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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure ComputerNode
            modelBuilder.Entity<ComputerNode>()
                .HasKey(n => n.Id);
            
            modelBuilder.Entity<ComputerNode>()
                .HasIndex(n => n.IpAddress)
                .IsUnique();

            // Configure BatchTask
            modelBuilder.Entity<BatchTask>()
                .HasKey(t => t.Id);
            
            modelBuilder.Entity<BatchTask>()
                .Property(t => t.Id)
                .ValueGeneratedOnAdd();

            // Configure FileLock
            modelBuilder.Entity<FileLock>()
                .HasKey(l => l.Id);
            
            modelBuilder.Entity<FileLock>()
                .HasIndex(l => l.FilePath)
                .IsUnique();
        }
    }
} 