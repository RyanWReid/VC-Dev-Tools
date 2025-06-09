# VC-Dev-Tool Development Rules & Standards

## 🔒 Security Rules (MANDATORY)

### 1. **Secret Management**
- ❌ **NEVER** store secrets in configuration files or source code
- ✅ **ALWAYS** use environment variables or secure key management systems
- ✅ **ALWAYS** validate that production secrets are not default values
- ✅ **MUST** rotate secrets regularly (minimum every 90 days)

```csharp
// ❌ WRONG - Secret in config
"Jwt": { "SecretKey": "hardcoded-secret" }

// ✅ CORRECT - Environment variable
var secretKey = Environment.GetEnvironmentVariable("VCDEVTOOL_JWT_SECRET") 
    ?? throw new InvalidOperationException("JWT secret must be configured");
```

### 2. **Authentication & Authorization**
- ✅ **MUST** validate all JWT tokens with proper expiration
- ✅ **MUST** implement role-based access control (RBAC)
- ✅ **MUST** log all authentication failures
- ❌ **NEVER** trust client-side data for authorization decisions
- ✅ **MUST** implement API rate limiting (100 requests/minute default)

```csharp
// ✅ REQUIRED: Proper authorization check
[Authorize(Policy = "AdminPolicy")]
public async Task<IActionResult> SensitiveOperation()
{
    // Additional checks inside method
    if (!User.IsInRole("Admin"))
        return Forbid();
}
```

### 3. **Input Validation**
- ✅ **MUST** validate ALL user inputs server-side
- ✅ **MUST** sanitize inputs to prevent XSS and SQL injection
- ✅ **MUST** implement FluentValidation for complex objects
- ❌ **NEVER** trust file paths or names from clients

```csharp
// ✅ REQUIRED: Comprehensive validation
public class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Task name is required")
            .MaximumLength(200).WithMessage("Task name too long")
            .Must(ValidationHelpers.IsSafeString).WithMessage("Invalid characters in task name");
    }
}
```

### 4. **CORS & Network Security**
- ✅ **MUST** specify exact allowed origins in production
- ❌ **NEVER** use `AllowAnyOrigin()` in any environment
- ✅ **MUST** implement HTTPS in production
- ✅ **MUST** add security headers (HSTS, CSP, etc.)

```csharp
// ✅ REQUIRED: Secure CORS configuration
policy.WithOrigins("https://app.company.com", "https://admin.company.com")
      .AllowAnyMethod()
      .AllowAnyHeader()
      .AllowCredentials();
```

## 🏗️ Architecture Rules

### 1. **Service Design**
- ✅ **MUST** follow Single Responsibility Principle (max 300 lines per service)
- ✅ **MUST** create interfaces for all services
- ✅ **MUST** use dependency injection for all dependencies
- ✅ **MUST** separate read and write operations when appropriate

```csharp
// ✅ REQUIRED: Service interface
public interface ITaskService
{
    Task<Result<BatchTask>> GetTaskByIdAsync(int taskId);
    Task<Result<BatchTask>> CreateTaskAsync(CreateTaskRequest request);
}

// ✅ REQUIRED: Focused service implementation
public class TaskService : ITaskService
{
    // Implementation focused on task management only
}
```

### 2. **Database Access**
- ✅ **MUST** use parameterized queries or EF Core methods
- ❌ **NEVER** use string concatenation for SQL
- ✅ **MUST** implement proper transaction management
- ✅ **MUST** use optimistic concurrency for critical operations
- ✅ **MUST** implement pagination for list operations

```csharp
// ❌ WRONG - SQL injection risk
var sql = $"SELECT * FROM Tasks WHERE Id = {taskId}";

// ✅ CORRECT - Parameterized query
var task = await _context.Tasks
    .Where(t => t.Id == taskId)
    .FirstOrDefaultAsync();
```

### 3. **Error Handling**
- ✅ **MUST** use Result pattern for operations that can fail
- ✅ **MUST** implement global exception handling
- ✅ **MUST** log errors with correlation IDs
- ❌ **NEVER** expose internal error details to clients

```csharp
// ✅ REQUIRED: Result pattern
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}
```

## 🔄 Concurrency Rules

### 1. **Database Concurrency**
- ✅ **MUST** use appropriate isolation levels for transactions
- ✅ **MUST** implement optimistic concurrency for updates
- ✅ **MUST** handle `DbUpdateConcurrencyException` properly
- ✅ **MUST** use distributed locking for cross-service operations

```csharp
// ✅ REQUIRED: Proper concurrency handling
using var transaction = await _context.Database
    .BeginTransactionAsync(IsolationLevel.Serializable);
try
{
    // Critical section code
    await transaction.CommitAsync();
}
catch (DbUpdateConcurrencyException)
{
    await transaction.RollbackAsync();
    return Result<T>.Failure("Resource was modified by another user");
}
```

### 2. **File Locking**
- ✅ **MUST** implement timeout for all lock operations
- ✅ **MUST** have cleanup mechanism for stale locks
- ✅ **MUST** use atomic operations for lock acquisition
- ✅ **MUST** log all lock operations for debugging

### 3. **Task Assignment**
- ✅ **MUST** prevent multiple nodes from processing same task
- ✅ **MUST** implement heartbeat mechanism for node availability
- ✅ **MUST** have task reassignment logic for failed nodes

## 📏 Code Quality Rules

### 1. **General Guidelines**
- ✅ **MUST** maintain minimum 80% code coverage
- ✅ **MUST** follow C# naming conventions
- ✅ **MUST** write XML documentation for public APIs
- ✅ **MUST** limit method complexity (max 10 cyclomatic complexity)
- ✅ **MUST** use meaningful variable and method names

```csharp
// ❌ WRONG - Poor naming
var d = DateTime.Now;
var r = GetStuff(d);

// ✅ CORRECT - Descriptive naming
var currentTimestamp = DateTime.UtcNow;
var availableNodes = GetAvailableNodes(currentTimestamp);
```

### 2. **Performance Rules**
- ✅ **MUST** use `async/await` for I/O operations
- ✅ **MUST** implement caching for frequently accessed data
- ✅ **MUST** use `AsNoTracking()` for read-only queries
- ✅ **MUST** implement pagination for large datasets
- ✅ **MUST** profile and optimize database queries

```csharp
// ✅ REQUIRED: Efficient query patterns
public async Task<PagedResult<BatchTask>> GetTasksAsync(int page, int pageSize)
{
    var totalCount = await _context.Tasks.CountAsync();
    var tasks = await _context.Tasks
        .AsNoTracking()
        .OrderByDescending(t => t.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();
    
    return new PagedResult<BatchTask>(tasks, totalCount, page, pageSize);
}
```

### 3. **Testing Requirements**
- ✅ **MUST** write unit tests for all business logic
- ✅ **MUST** write integration tests for API endpoints
- ✅ **MUST** test concurrent scenarios for critical operations
- ✅ **MUST** test error scenarios and edge cases
- ✅ **MUST** use proper mocking for external dependencies

```csharp
// ✅ REQUIRED: Comprehensive test coverage
[Fact]
public async Task GetTaskById_WhenTaskExists_ReturnsTask()
{
    // Arrange
    var taskId = 1;
    var expectedTask = new BatchTask { Id = taskId, Name = "Test Task" };
    _mockContext.Setup(c => c.Tasks.FindAsync(taskId))
              .ReturnsAsync(expectedTask);
    
    // Act
    var result = await _taskService.GetTaskByIdAsync(taskId);
    
    // Assert
    Assert.True(result.IsSuccess);
    Assert.Equal(expectedTask, result.Value);
}
```

## 📊 Logging & Monitoring Rules

### 1. **Logging Standards**
- ✅ **MUST** use structured logging with Serilog
- ✅ **MUST** include correlation IDs in all log entries
- ✅ **MUST** log security events (failed auth, suspicious activity)
- ✅ **MUST** use appropriate log levels (Error, Warning, Information, Debug)
- ❌ **NEVER** log sensitive information (passwords, tokens, PII)

```csharp
// ✅ REQUIRED: Proper logging
_logger.LogInformation("Task {TaskId} assigned to node {NodeId} by user {UserId}", 
    taskId, nodeId, User.Identity.Name);

// ❌ WRONG - Logging sensitive data
_logger.LogInformation("User {Username} logged in with password {Password}", 
    username, password); // NEVER DO THIS
```

### 2. **Monitoring Requirements**
- ✅ **MUST** implement health checks for all dependencies
- ✅ **MUST** monitor key performance metrics
- ✅ **MUST** set up alerts for critical failures
- ✅ **MUST** track business metrics (task completion rates, etc.)

### 3. **Audit Trail**
- ✅ **MUST** log all data modifications with user context
- ✅ **MUST** maintain audit trail for security-sensitive operations
- ✅ **MUST** implement retention policies for audit logs

## 🚀 Deployment Rules

### 1. **Environment Configuration**
- ✅ **MUST** use different configurations per environment
- ✅ **MUST** validate all required environment variables at startup
- ✅ **MUST** implement configuration drift detection
- ❌ **NEVER** deploy with debug settings enabled in production

### 2. **Database Migrations**
- ✅ **MUST** test all migrations in staging first
- ✅ **MUST** have rollback plan for all migrations
- ✅ **MUST** backup database before major migrations
- ✅ **MUST** use zero-downtime deployment strategies

### 3. **Security Hardening**
- ✅ **MUST** scan for vulnerabilities before deployment
- ✅ **MUST** use least-privilege principle for service accounts
- ✅ **MUST** implement network segmentation
- ✅ **MUST** enable audit logging in production

## 📊 Knowledge Management & Documentation

### 1. **MCP Knowledge Graph Requirements**
- ✅ **MUST** update MCP Knowledge Graph with all architectural decisions
- ✅ **MUST** document design patterns and their usage contexts
- ✅ **MUST** record security considerations and threat models
- ✅ **MUST** maintain API contract documentation in knowledge graph
- ✅ **MUST** update knowledge graph before merging any PR

```markdown
<!-- ✅ REQUIRED: Knowledge Graph Entry Template -->
## Knowledge Graph Entry: [Feature/Component Name]

### Context
- **Purpose**: What problem does this solve?
- **Scope**: What parts of the system are affected?
- **Dependencies**: What other components does this rely on?

### Architecture
- **Design Pattern**: Repository/Service/Factory/etc.
- **Data Flow**: How information moves through the system
- **Security Model**: Authentication/Authorization requirements

### Implementation Details
- **Key Classes**: Main classes and interfaces
- **Database Schema**: Tables/indexes affected
- **Configuration**: Required settings and environment variables

### Decisions & Rationale
- **Trade-offs**: What alternatives were considered?
- **Performance**: Expected impact and optimization strategies
- **Security**: Threat model and mitigation strategies

### Testing Strategy
- **Unit Tests**: What scenarios are covered?
- **Integration Tests**: End-to-end workflows tested
- **Security Tests**: Penetration testing and vulnerability scanning

### Monitoring & Operations
- **Metrics**: What should be monitored?
- **Alerts**: When should operators be notified?
- **Troubleshooting**: Common issues and solutions
```

### 2. **Cursor AI Integration**
- ✅ **MUST** configure Cursor to automatically suggest knowledge graph updates
- ✅ **MUST** use Cursor rules to enforce documentation standards
- ✅ **MUST** leverage knowledge graph for code generation and refactoring
- ✅ **MUST** ensure Cursor has access to latest architectural decisions

```json
// ✅ REQUIRED: .cursorrules configuration
{
  "rules": [
    "Always check and update MCP Knowledge Graph when making changes",
    "Reference existing patterns and decisions from knowledge graph",
    "Suggest knowledge graph updates for new components or changes",
    "Ensure all new code follows established patterns in knowledge graph",
    "Validate security requirements against threat models in knowledge graph"
  ],
  "knowledgeGraph": {
    "autoUpdate": true,
    "enforceDocumentation": true,
    "validatePatterns": true
  }
}
```

### 3. **Documentation Standards**
- ✅ **MUST** maintain living documentation that updates with code changes
- ✅ **MUST** include decision records (ADRs) for significant architectural choices
- ✅ **MUST** document all external integrations and dependencies
- ✅ **MUST** maintain runbooks for operational procedures

## 🔧 Development Workflow Rules

### 1. **Git Workflow**
- ✅ **MUST** use feature branches for all changes
- ✅ **MUST** require pull request reviews for main branch
- ✅ **MUST** run all tests before merging
- ✅ **MUST** use conventional commit messages
- ✅ **MUST** squash commits before merging
- ✅ **MUST** update knowledge graph before creating PR

### 2. **Code Review Requirements**
- ✅ **MUST** review for security vulnerabilities
- ✅ **MUST** review for performance implications
- ✅ **MUST** review for architectural consistency
- ✅ **MUST** verify test coverage for new code
- ✅ **MUST** check for proper error handling
- ✅ **MUST** verify knowledge graph has been updated
- ✅ **MUST** ensure new patterns are documented in knowledge graph
- ✅ **MUST** validate against established architectural decisions

### 3. **CI/CD Pipeline**
- ✅ **MUST** run security scans on every commit
- ✅ **MUST** run full test suite including integration tests
- ✅ **MUST** perform static code analysis
- ✅ **MUST** check for dependency vulnerabilities
- ✅ **MUST** validate that builds are reproducible
- ✅ **MUST** validate knowledge graph consistency and completeness
- ✅ **MUST** generate documentation from knowledge graph
- ✅ **MUST** check architectural decision compliance

## 📋 Compliance Checklist

Before merging any PR, ensure:

### Security Checklist
- [ ] No secrets in code or config files
- [ ] All inputs are validated server-side
- [ ] Authentication and authorization are properly implemented
- [ ] No SQL injection or XSS vulnerabilities
- [ ] Error messages don't expose sensitive information

### Architecture Checklist
- [ ] Services follow single responsibility principle
- [ ] Interfaces are defined for all services
- [ ] Proper dependency injection is used
- [ ] Database access uses EF Core or parameterized queries
- [ ] Concurrency is handled appropriately

### Quality Checklist
- [ ] Code coverage is at least 80%
- [ ] All public APIs have XML documentation
- [ ] Logging includes correlation IDs
- [ ] Performance requirements are met
- [ ] Error scenarios are tested
- [ ] MCP Knowledge Graph has been updated
- [ ] Architectural decisions are documented
- [ ] Design patterns are recorded in knowledge graph

### Deployment Checklist
- [ ] Environment-specific configurations are correct
- [ ] Database migrations have been tested
- [ ] Health checks are implemented
- [ ] Monitoring and alerting are configured
- [ ] Security hardening is complete

## 🛠️ Tools & Enforcement

### Required Development Tools
- **SonarQube** - Code quality and security analysis
- **OWASP Dependency Check** - Vulnerability scanning
- **BenchmarkDotNet** - Performance benchmarking
- **Coverlet** - Code coverage analysis
- **StyleCop** - Code style enforcement
- **MCP Knowledge Graph** - Architectural decision recording and pattern documentation
- **Cursor AI** - AI-assisted development with knowledge graph integration

### IDE Extensions
- **Roslynator** - C# code analysis
- **SonarLint** - Real-time code quality feedback
- **EditorConfig** - Consistent formatting
- **GitLens** - Git integration and history
- **Cursor Rules** - AI-powered code suggestions with knowledge graph integration
- **MCP Tools** - Knowledge graph maintenance and validation

### Pre-commit Hooks
```bash
# Required checks before commit
- dotnet format --verify-no-changes
- dotnet test --no-build --verbosity normal
- security-scan.ps1
- dependency-check.ps1
- validate-knowledge-graph.ps1
- check-architectural-compliance.ps1
```

## 📚 Training Requirements

### Mandatory Training for All Developers
1. **OWASP Top 10** - Security vulnerabilities
2. **.NET Security Best Practices** - Platform-specific security
3. **Concurrency in .NET** - Thread safety and async patterns
4. **Entity Framework Performance** - Database optimization
5. **Clean Architecture Principles** - Design patterns

### Specialized Training
- **Senior Developers**: Architecture reviews, security auditing
- **DevOps Engineers**: Security infrastructure, monitoring
- **QA Engineers**: Security testing, performance testing

---

## 📞 Support & Escalation

### Code Review Escalation
1. **Peer Review** - Standard pull request review
2. **Senior Developer** - Complex architectural changes
3. **Security Team** - Security-sensitive modifications
4. **Architecture Committee** - Major design decisions

### Security Incident Response
1. **Immediate**: Stop deployment, assess impact
2. **Investigation**: Determine root cause and scope
3. **Remediation**: Fix vulnerability and test
4. **Post-mortem**: Update rules and training

---

*These rules are mandatory for all VC-Dev-Tool development. Violations must be addressed before code can be merged. This document should be reviewed and updated quarterly.* 