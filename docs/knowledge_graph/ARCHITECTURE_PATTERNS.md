# Architecture Patterns - VC-Dev-Tool Knowledge Graph

## Overview
This document contains detailed information about the architectural patterns used throughout the VC-Dev-Tool system, including implementation details, usage guidelines, and code examples.

---

## Service Layer Patterns

### Knowledge Graph Entry: Service Layer Architecture

#### Context
- **Purpose**: Encapsulate business logic and provide a clean API for controllers
- **Scope**: All business operations, task management, node coordination
- **Dependencies**: Entity Framework, dependency injection, logging

#### Architecture
- **Design Pattern**: Service Layer with Repository-like patterns
- **Data Flow**: Controllers → Services → Data Access Layer → Database
- **Security Model**: Method-level authorization attributes

#### Implementation Standards

```csharp
// ✅ CORRECT: Proper service interface
public interface ITaskManagementService
{
    Task<Result<BatchTask>> GetTaskByIdAsync(int taskId);
    Task<Result<BatchTask>> CreateTaskAsync(CreateTaskRequest request);
    Task<Result> UpdateTaskStatusAsync(int taskId, TaskStatus status);
}

// ✅ CORRECT: Service implementation
public class TaskManagementService : ITaskManagementService
{
    private readonly AppDbContext _context;
    private readonly ILogger<TaskManagementService> _logger;

    public TaskManagementService(AppDbContext context, ILogger<TaskManagementService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result<BatchTask>> GetTaskByIdAsync(int taskId)
    {
        try
        {
            var task = await _context.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == taskId);

            return task != null 
                ? Result<BatchTask>.Success(task)
                : Result<BatchTask>.Failure("Task not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving task {TaskId}", taskId);
            return Result<BatchTask>.Failure("Error retrieving task");
        }
    }
}
```

#### Current Violations
🔴 **TaskService.cs**: 885 lines (violates 300-line limit)
🔴 **Missing interfaces**: Some services registered as concrete classes
🔴 **Inconsistent error handling**: Mixed return patterns

#### Required Refactoring
1. Split TaskService into focused services
2. Create interfaces for all services  
3. Implement consistent Result pattern
4. Add comprehensive logging with correlation IDs

---

## Data Access Patterns

### Knowledge Graph Entry: Entity Framework Patterns

#### Context
- **Purpose**: Provide consistent, performant data access across the application
- **Scope**: All database operations, queries, updates, migrations
- **Dependencies**: Entity Framework Core, SQL Server

#### Architecture
- **Design Pattern**: Code-First Entity Framework with DbContext
- **Data Flow**: Services → DbContext → Database
- **Security Model**: Parameterized queries, no dynamic SQL

#### Implementation Standards

```csharp
// ✅ CORRECT: Optimized query with AsNoTracking
public async Task<List<BatchTask>> GetPendingTasksAsync(int skip, int take)
{
    return await _context.Tasks
        .AsNoTracking()
        .Where(t => t.Status == TaskStatus.Pending)
        .OrderBy(t => t.CreatedDate)
        .Skip(skip)
        .Take(take)
        .ToListAsync();
}

// ✅ CORRECT: Transaction with proper disposal
public async Task<Result> UpdateTaskWithLockAsync(int taskId, TaskStatus status, string nodeId)
{
    using var transaction = await _context.Database
        .BeginTransactionAsync(IsolationLevel.Serializable);
    
    try
    {
        var task = await _context.Tasks.FindAsync(taskId);
        if (task == null)
            return Result.Failure("Task not found");
            
        task.Status = status;
        task.NodeId = nodeId;
        task.UpdatedDate = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        
        return Result.Success();
    }
    catch (DbUpdateConcurrencyException ex)
    {
        await transaction.RollbackAsync();
        return Result.Failure("Concurrency conflict");
    }
}

// ❌ WRONG: String interpolation in SQL (security risk)
var sql = $"SELECT * FROM Tasks WHERE NodeId = '{nodeId}'";
var tasks = await _context.Tasks.FromSqlRaw(sql).ToListAsync();
```

#### Performance Optimizations
- ✅ Connection pooling enabled
- ✅ Query result caching for read-heavy operations  
- ❌ Missing pagination on list operations
- ❌ Missing AsNoTracking() for read-only queries

---

## Authentication Patterns

### Knowledge Graph Entry: Hybrid Authentication System

#### Context
- **Purpose**: Support both JWT and Windows Authentication for different deployment scenarios
- **Scope**: API authentication, SignalR authentication, role-based authorization
- **Dependencies**: JWT Bearer tokens, Windows Authentication, Active Directory

#### Architecture
- **Design Pattern**: Strategy pattern with multiple authentication providers
- **Data Flow**: Request → Auth Middleware → Token Validation → Role Authorization
- **Security Model**: Bearer tokens with role-based claims

#### Implementation

```csharp
// ✅ CORRECT: Hybrid authentication setup
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// ✅ CORRECT: Role-based authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("NodePolicy", policy => 
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            var hasNodeRole = context.User.IsInRole("Node");
            var isInNodeGroup = context.User.FindAll("Groups")
                .Any(claim => nodeGroups.Contains(claim.Value));
            return hasNodeRole || isInNodeGroup;
        });
    });
});
```

#### Security Issues
🚨 **CRITICAL**: JWT secret exposed in appsettings.json
🚨 **HIGH**: Default development secret is predictable
🚨 **MEDIUM**: CORS allows any origin in development

#### Required Secure Implementation

```csharp
// ✅ CORRECT: Secure configuration loading
var secretKey = Environment.GetEnvironmentVariable("VCDEVTOOL_JWT_SECRET") 
    ?? throw new InvalidOperationException("JWT secret must be configured");

// ✅ CORRECT: Configuration validation
services.AddOptions<JwtOptions>()
    .Bind(configuration.GetSection("Jwt"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

---

## Concurrency Patterns

### Knowledge Graph Entry: Distributed Locking and Concurrency Control

#### Context
- **Purpose**: Prevent race conditions in distributed file processing environment
- **Scope**: File locks, node registration, task assignment
- **Dependencies**: SQL Server transactions, optimistic concurrency

#### Architecture
- **Design Pattern**: Optimistic concurrency with distributed locking
- **Data Flow**: Lock Request → Database Check → Lock Acquisition → Processing → Lock Release
- **Security Model**: Node-based lock ownership validation

#### Current Implementation Issues

```csharp
// 🔴 RACE CONDITION: Check-then-act pattern
var existingLock = await _context.FileLocks
    .FirstOrDefaultAsync(l => l.FileName == fileName && l.ExpiresAt > DateTime.UtcNow);

if (existingLock == null)
{
    // ⚠️ Another process could acquire lock here
    var newLock = new FileLock { FileName = fileName, NodeId = nodeId };
    _context.FileLocks.Add(newLock);
    await _context.SaveChangesAsync();
}
```

#### Required Implementation

```csharp
// ✅ CORRECT: Atomic lock acquisition
public async Task<Result<FileLock>> AcquireLockAsync(string fileName, string nodeId)
{
    using var transaction = await _context.Database
        .BeginTransactionAsync(IsolationLevel.Serializable);
    
    try
    {
        // Check for existing locks atomically
        var existingLock = await _context.FileLocks
            .Where(l => l.FileName == fileName && l.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync();
            
        if (existingLock != null)
        {
            return Result<FileLock>.Failure("File is already locked");
        }
        
        var newLock = new FileLock
        {
            FileName = fileName,
            NodeId = nodeId,
            AcquiredAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        
        _context.FileLocks.Add(newLock);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        
        return Result<FileLock>.Success(newLock);
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Result<FileLock>.Failure($"Lock acquisition failed: {ex.Message}");
    }
}
```

---

## Error Handling Patterns

### Knowledge Graph Entry: Result Pattern Implementation

#### Context
- **Purpose**: Provide consistent, type-safe error handling across the application
- **Scope**: All service operations, API responses, business logic
- **Dependencies**: Custom Result types, logging framework

#### Architecture
- **Design Pattern**: Result pattern with typed success/failure states
- **Data Flow**: Operation → Result<T> → Response mapping
- **Security Model**: Safe error messages without sensitive data exposure

#### Implementation

```csharp
// ✅ CORRECT: Result pattern implementation
public class Result<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public string Error { get; }
    
    private Result(bool isSuccess, T value, string error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }
    
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}

// ✅ CORRECT: Usage in controllers
[HttpGet("{id}")]
public async Task<IActionResult> GetTask(int id)
{
    var result = await _taskService.GetTaskByIdAsync(id);
    
    return result.IsSuccess 
        ? Ok(result.Value)
        : NotFound(new { Error = result.Error });
}

// ❌ WRONG: Inconsistent error handling
public async Task<BatchTask> GetTaskByIdAsync(int taskId)
{
    return await _context.Tasks.FindAsync(taskId) ?? new BatchTask { Id = -1 };
}
```

---

## SignalR Real-time Patterns

### Knowledge Graph Entry: Real-time Communication Architecture

#### Context
- **Purpose**: Provide real-time updates for task progress and system status
- **Scope**: Client notifications, debug messaging, task status updates
- **Dependencies**: SignalR Core, JWT authentication for hubs

#### Architecture
- **Design Pattern**: Hub-based publish-subscribe with typed clients
- **Data Flow**: Service → Hub → Connected Clients
- **Security Model**: JWT authentication with group-based messaging

#### Implementation

```csharp
// ✅ CORRECT: Strongly-typed hub
public interface ITaskClient
{
    Task TaskStatusUpdated(int taskId, string status);
    Task TaskCompleted(int taskId, string result);
    Task DebugMessage(string message);
}

public class TaskHub : Hub<ITaskClient>
{
    [Authorize(Policy = "NodePolicy")]
    public async Task JoinTaskGroup(int taskId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Task_{taskId}");
    }
}

// ✅ CORRECT: Service integration
public class TaskNotificationService
{
    private readonly IHubContext<TaskHub, ITaskClient> _hubContext;
    
    public async Task NotifyTaskStatusAsync(int taskId, TaskStatus status)
    {
        await _hubContext.Clients.Group($"Task_{taskId}")
            .TaskStatusUpdated(taskId, status.ToString());
    }
}
```

---

## Validation Patterns

### Knowledge Graph Entry: Input Validation Framework

#### Context
- **Purpose**: Ensure all user inputs are validated consistently and securely
- **Scope**: API endpoints, model binding, business rule validation
- **Dependencies**: FluentValidation, model binding validation

#### Architecture
- **Design Pattern**: Fluent validation with automatic model binding
- **Data Flow**: Request → Model Binding → Validation → Business Logic
- **Security Model**: Server-side validation with sanitization

#### Implementation

```csharp
// ✅ CORRECT: FluentValidation setup
public class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.TaskType)
            .NotEmpty()
            .Must(BeValidTaskType)
            .WithMessage("Invalid task type");
            
        RuleFor(x => x.SourcePath)
            .NotEmpty()
            .Must(BeValidPath)
            .WithMessage("Invalid source path");
            
        RuleFor(x => x.Parameters)
            .Must(BeValidJsonOrNull)
            .WithMessage("Parameters must be valid JSON");
    }
    
    private bool BeValidTaskType(string taskType)
    {
        return Enum.TryParse<TaskType>(taskType, out _);
    }
}

// ✅ CORRECT: Controller validation
[HttpPost]
public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
{
    if (!ModelState.IsValid)
    {
        return BadRequest(ModelState);
    }
    
    var result = await _taskService.CreateTaskAsync(request);
    return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
}
```

---

## Testing Patterns

### Knowledge Graph Entry: Testing Architecture and Patterns

#### Context
- **Purpose**: Ensure comprehensive test coverage with maintainable test code
- **Scope**: Unit tests, integration tests, security tests, performance tests
- **Dependencies**: xUnit, Moq, TestContainers, FluentAssertions

#### Architecture
- **Design Pattern**: Layered testing with different test types and isolation
- **Data Flow**: Test Setup → System Under Test → Assertions
- **Security Model**: Isolated test environments with mock credentials

#### Implementation Standards

```csharp
// ✅ CORRECT: Unit test with mocking
public class TaskServiceTests
{
    private readonly Mock<AppDbContext> _mockContext;
    private readonly Mock<ILogger<TaskService>> _mockLogger;
    private readonly TaskService _service;
    
    public TaskServiceTests()
    {
        _mockContext = new Mock<AppDbContext>();
        _mockLogger = new Mock<ILogger<TaskService>>();
        _service = new TaskService(_mockContext.Object, _mockLogger.Object);
    }
    
    [Fact]
    public async Task GetTaskByIdAsync_ExistingTask_ReturnsSuccess()
    {
        // Arrange
        var taskId = 1;
        var expectedTask = new BatchTask { Id = taskId, Name = "Test Task" };
        
        _mockContext.Setup(c => c.Tasks.FindAsync(taskId))
            .ReturnsAsync(expectedTask);
        
        // Act
        var result = await _service.GetTaskByIdAsync(taskId);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(expectedTask);
    }
}

// ✅ CORRECT: Integration test with TestContainers
public class TaskControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    
    public TaskControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task GetTasks_ReturnsTaskList()
    {
        // Arrange
        await SeedTestDataAsync();
        
        // Act
        var response = await _client.GetAsync("/api/tasks");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.Content.ReadFromJsonAsync<List<BatchTask>>();
        tasks.Should().NotBeEmpty();
    }
}
```

---

## Performance Patterns

### Knowledge Graph Entry: Performance Optimization Strategies

#### Context
- **Purpose**: Ensure optimal performance across all system operations
- **Scope**: Database queries, API responses, memory usage, caching
- **Dependencies**: EF Core optimizations, memory profiling, monitoring

#### Architecture
- **Design Pattern**: Multi-layered performance optimization
- **Data Flow**: Request → Cache Check → Database → Response Caching
- **Security Model**: Performance monitoring without data exposure

#### Implementation Guidelines

```csharp
// ✅ CORRECT: Optimized query with pagination
public async Task<PagedResult<BatchTask>> GetTasksPagedAsync(int page, int pageSize)
{
    var totalCount = await _context.Tasks.CountAsync();
    
    var tasks = await _context.Tasks
        .AsNoTracking()
        .OrderByDescending(t => t.CreatedDate)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();
    
    return new PagedResult<BatchTask>
    {
        Items = tasks,
        TotalCount = totalCount,
        Page = page,
        PageSize = pageSize
    };
}

// ✅ CORRECT: Memory-efficient async enumeration
public async IAsyncEnumerable<BatchTask> GetTasksStreamAsync()
{
    await foreach (var task in _context.Tasks.AsAsyncEnumerable())
    {
        yield return task;
    }
}

// ❌ WRONG: Loading entire dataset into memory
public async Task<List<BatchTask>> GetAllTasksAsync()
{
    return await _context.Tasks.ToListAsync(); // Could be millions of records
}
```

#### Current Performance Issues
🔶 Missing pagination on list endpoints
🔶 Potential N+1 query problems
🔶 Missing query result caching
🔶 No performance monitoring dashboard

---

## Deployment Patterns

### Knowledge Graph Entry: Deployment and Configuration Management

#### Context
- **Purpose**: Standardize deployment procedures and configuration management
- **Scope**: Environment configuration, secret management, deployment automation
- **Dependencies**: PowerShell scripts, environment variables, configuration providers

#### Architecture
- **Design Pattern**: Environment-based configuration with secure secret management
- **Data Flow**: Deployment Script → Configuration → Application Startup
- **Security Model**: Secrets stored in secure configuration, never in code

#### Implementation Standards

```csharp
// ✅ CORRECT: Secure configuration loading
public class JwtOptions
{
    public const string Section = "Jwt";
    
    [Required]
    public string SecretKey { get; set; }
    
    [Required]
    public string Issuer { get; set; }
    
    [Required]
    public string Audience { get; set; }
    
    public TimeSpan Expiration { get; set; } = TimeSpan.FromHours(24);
}

// ✅ CORRECT: Configuration validation
services.AddOptions<JwtOptions>()
    .Bind(configuration.GetSection(JwtOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ✅ CORRECT: Environment variable override
var secretKey = Environment.GetEnvironmentVariable("VCDEVTOOL_JWT_SECRET") 
    ?? configuration["Jwt:SecretKey"];

if (string.IsNullOrEmpty(secretKey) && builder.Environment.IsProduction())
{
    throw new InvalidOperationException("JWT SecretKey must be configured for production");
}
```

#### Security Requirements
🔒 All secrets must be in environment variables or secure vaults
🔒 Configuration validation at startup
🔒 No sensitive data in logs or error messages
🔒 Environment-specific configuration isolation

---

**Document Version**: 1.0  
**Last Updated**: December 2024  
**Next Review**: Upon architectural changes 