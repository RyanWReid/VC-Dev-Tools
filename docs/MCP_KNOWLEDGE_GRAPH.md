# VC-Dev-Tool MCP Knowledge Graph

## Overview

This document serves as the central repository for architectural knowledge, design patterns, security models, and operational procedures for the VC-Dev-Tool distributed file processing system. All architectural decisions, patterns, and operational knowledge should be documented here.

**Last Updated**: December 2024  
**Version**: 1.0  
**Maintained By**: Development Team with Cursor AI Integration

---

## 📋 Table of Contents

1. [System Architecture](#system-architecture)
2. [Security Framework](#security-framework)  
3. [Service Layer](#service-layer)
4. [Data Layer](#data-layer)
5. [Client Architecture](#client-architecture)
6. [Performance Patterns](#performance-patterns)
7. [Operations & Monitoring](#operations--monitoring)
8. [Testing Strategies](#testing-strategies)
9. [Known Issues & Technical Debt](#known-issues--technical-debt)
10. [Decision Records](#decision-records)

---

## System Architecture

### Knowledge Graph Entry: Core System Architecture

#### Context
- **Purpose**: Distributed file processing and task management system for Visual Computing workflows
- **Scope**: Multi-node processing with centralized coordination and file locking
- **Dependencies**: ASP.NET Core API, WPF Client, SQL Server, SignalR

#### Architecture
- **Design Pattern**: Distributed Service-Oriented Architecture with Hub-and-Spoke topology
- **Data Flow**: Client → API → Database, Nodes ↔ API ↔ Shared File Storage
- **Security Model**: JWT authentication with optional Windows Authentication integration

#### Implementation
- **Key Classes**: 
  - `Program.cs` - API startup and configuration
  - `TaskService.cs` - Core task management (885 lines - needs refactoring)
  - `AppDbContext.cs` - Entity Framework data access
- **Database Schema**: Tasks, Nodes, FileLocks tables with optimistic concurrency
- **Configuration**: Environment-based with appsettings.json structure

#### Decisions & Rationale
- **Trade-offs**: Chose Hub-and-Spoke over Peer-to-Peer for centralized control and monitoring
- **Performance**: Implemented file locking to prevent race conditions in distributed processing
- **Security**: Hybrid authentication to support both corporate AD and standalone deployments

#### Testing Strategy
- **Unit Tests**: Service layer testing with mocked dependencies
- **Integration Tests**: Full API workflow testing
- **Security Tests**: Authentication and authorization validation

#### Operations
- **Metrics**: Task completion rates, node health, file lock duration
- **Alerts**: Node disconnection, task failures, database connectivity
- **Troubleshooting**: PowerShell scripts for common operations (clear-locks.ps1, inspect-db.ps1)

#### Critical Issues Identified
🚨 **CRITICAL**: JWT secret key exposed in appsettings.json
🚨 **HIGH**: Race condition in node registration (TaskService.cs lines 159-222)
🚨 **HIGH**: File lock race condition (TaskService.cs lines 389-520)

---

## Security Framework

### Knowledge Graph Entry: Authentication & Authorization System

#### Context
- **Purpose**: Secure access control for distributed nodes and administrative users
- **Scope**: API endpoints, SignalR hubs, node registration, administrative functions
- **Dependencies**: JWT tokens, Windows Authentication (optional), Active Directory integration

#### Architecture
- **Design Pattern**: Hybrid authentication (JWT + Windows Auth) with role-based authorization
- **Data Flow**: Client → Authentication Middleware → JWT Token → Role-based Authorization
- **Security Model**: Bearer token authentication with configurable Windows Authentication fallback

#### Implementation
- **Key Classes**:
  - `JwtService.cs` - Token generation and validation
  - `ActiveDirectoryService.cs` - AD integration and group mapping
  - `WindowsAuthenticationMiddleware` - Automatic Windows authentication
- **Database Schema**: No user storage (external AD or JWT claims)
- **Configuration**: JWT settings, AD group mappings, Windows Authentication toggle

#### Decisions & Rationale
- **Trade-offs**: Hybrid approach supports both corporate and standalone environments
- **Performance**: Stateless JWT tokens reduce database load
- **Security**: Role-based policies with AD group mapping for enterprise environments

#### Critical Security Issues
🚨 **CRITICAL**: JWT secret key exposed in configuration files
🚨 **HIGH**: Weak default authentication configuration  
🚨 **MEDIUM**: Overly permissive CORS in development

#### Testing Strategy
- **Unit Tests**: JWT token validation, AD group parsing
- **Integration Tests**: End-to-end authentication flows
- **Security Tests**: Token manipulation, privilege escalation

#### Operations
- **Metrics**: Authentication success/failure rates, token expiration events
- **Alerts**: Authentication failures, unauthorized access attempts
- **Troubleshooting**: Authentication diagnostic scripts

---

## Service Layer

### Knowledge Graph Entry: TaskService Architecture

#### Context
- **Purpose**: Core business logic for task creation, assignment, and execution coordination
- **Scope**: Task lifecycle management, node coordination, file locking, progress tracking
- **Dependencies**: Entity Framework, SignalR, file system access

#### Architecture
- **Design Pattern**: Service layer with repository-like data access patterns
- **Data Flow**: Controllers → TaskService → Database, TaskService → SignalR → Clients
- **Security Model**: Role-based method-level authorization

#### Implementation
- **Key Classes**: 
  - `TaskService.cs` (885 lines - **VIOLATION**: Exceeds 300-line limit)
  - `TaskCompletionService.cs` - Task finalization logic
  - `TaskNotificationService.cs` - SignalR notification coordination
- **Database Schema**: Tasks, BatchTasks, FileLocks with FK relationships
- **Configuration**: Task timeouts, retry policies, lock expiration

#### Critical Issues
🔴 **ARCHITECTURE VIOLATION**: TaskService.cs (885 lines) exceeds 300-line service limit
🔴 **RACE CONDITION**: Node registration has potential data inconsistency
🔴 **RACE CONDITION**: File lock acquisition has timing windows

#### Decisions & Rationale
- **Trade-offs**: Single service for task operations vs. multiple focused services
- **Performance**: Direct EF Core queries for performance-critical operations
- **Security**: Method-level authorization for task operations

#### Required Refactoring
Split TaskService into focused services:
- ITaskManagementService (CRUD operations)
- ITaskAssignmentService (node assignment logic)  
- IFileLockService (distributed locking)
- ITaskExecutionService (execution coordination)

#### Testing Strategy
- **Unit Tests**: Task state transitions, node assignment logic
- **Integration Tests**: Multi-node task processing
- **Security Tests**: Authorization enforcement

#### Operations
- **Metrics**: Task creation rate, completion time, failure rate
- **Alerts**: Task timeout, node assignment failures
- **Troubleshooting**: Task state inspection, lock status queries

---

## Data Layer

### Knowledge Graph Entry: Entity Framework Data Access

#### Context
- **Purpose**: Object-relational mapping and database operations for task and node management
- **Scope**: All database interactions, migrations, performance optimization
- **Dependencies**: SQL Server, Entity Framework Core 9.0

#### Architecture
- **Design Pattern**: Code-First Entity Framework with DbContext pattern
- **Data Flow**: Services → DbContext → SQL Server
- **Security Model**: Connection string encryption, parameterized queries

#### Implementation
- **Key Classes**:
  - `AppDbContext.cs` - Main database context
  - `DbOptimizations.cs` - Performance configuration
  - Migration files in `/Migrations/`
- **Database Schema**: 
  - Tasks (Id, Type, Status, CreatedDate, CompletedDate)
  - Nodes (Id, Name, HardwareFingerprint, LastSeen, IsHealthy)
  - FileLocks (Id, FileName, NodeId, AcquiredAt, ExpiresAt)
- **Configuration**: Connection strings, retry policies, timeout settings

#### Performance Optimizations
- **Applied**: 
  - Connection pooling enabled
  - Query result caching for read-heavy operations
  - Optimized DbContext configuration
- **Missing**: 
  - Pagination on list operations
  - AsNoTracking() for read-only queries
  - Query optimization analysis

#### Critical Issues Identified
🔴 **PERFORMANCE**: Missing pagination on list endpoints
🔴 **PERFORMANCE**: Potential N+1 query problems
🔴 **SECURITY**: Direct SQL queries with string interpolation in TaskService.cs:410

#### Decisions & Rationale
- **Trade-offs**: Code-First for rapid development vs. Database-First for control
- **Performance**: Connection pooling and retry policies for resilience
- **Security**: Parameterized queries required, no dynamic SQL

#### Testing Strategy
- **Unit Tests**: Repository pattern testing with in-memory provider
- **Integration Tests**: Full database round-trip testing
- **Performance Tests**: Query execution time monitoring

#### Operations
- **Metrics**: Query execution times, connection pool utilization
- **Alerts**: Database connectivity issues, slow query detection
- **Troubleshooting**: Query profiling, index analysis

---

## Client Architecture

### Knowledge Graph Entry: WPF Client Application

#### Context
- **Purpose**: Desktop client for task management and system monitoring
- **Scope**: User interface, API communication, real-time updates
- **Dependencies**: WPF, SignalR client, HTTP client, MVVM pattern

#### Architecture
- **Design Pattern**: MVVM with Command pattern and SignalR integration
- **Data Flow**: View → ViewModel → Services → API
- **Security Model**: JWT token storage and automatic authentication

#### Implementation
- **Key Classes**:
  - `MainViewModel.cs` - Primary UI state management
  - `NodeService.cs` - API communication layer
  - `TaskExecutionService.cs` - Task processing coordination
- **Database Schema**: N/A (client-side only)
- **Configuration**: API endpoints, authentication settings

#### Issues Identified
🔴 **SECURITY**: Debug statements may leak sensitive information (lines 43-274 in NodeService.cs)
🔴 **ARCHITECTURE**: Large ViewModels violating SRP

#### Decisions & Rationale
- **Trade-offs**: WPF for rich desktop experience vs. web-based for cross-platform
- **Performance**: SignalR for real-time updates vs. polling
- **Security**: Client-side JWT storage with automatic renewal

#### Testing Strategy
- **Unit Tests**: ViewModel behavior and command execution
- **Integration Tests**: API communication scenarios
- **UI Tests**: Automated UI testing with testing frameworks

#### Operations
- **Metrics**: Client connection stability, UI responsiveness
- **Alerts**: API connection failures, authentication issues
- **Troubleshooting**: Client-side logging and error reporting

---

## Performance Patterns

### Knowledge Graph Entry: Performance Optimization Strategies

#### Context
- **Purpose**: Ensure system scalability and responsiveness under load
- **Scope**: Database queries, API responses, client updates, file processing
- **Dependencies**: EF Core, connection pooling, caching strategies

#### Architecture
- **Design Pattern**: Multi-layered performance optimization
- **Data Flow**: Request → Cache → Database → Response with monitoring
- **Performance Model**: Asynchronous operations with timeout controls

#### Implementation
- **Key Classes**:
  - `PerformanceMonitoringService.cs` - Performance tracking
  - `DbOptimizations.cs` - Database performance configuration
- **Database Schema**: Indexed columns for query optimization
- **Configuration**: Cache settings, timeout configurations, monitoring intervals

#### Current Performance Status
✅ **Implemented**: 
- Asynchronous operations throughout
- Connection pooling
- Basic monitoring

❌ **Missing**:
- Query result caching
- Pagination implementation
- Performance benchmarking

#### Decisions & Rationale
- **Trade-offs**: Memory usage vs. query performance with caching
- **Performance**: Async/await pattern for I/O operations
- **Security**: Performance monitoring without data exposure

#### Testing Strategy
- **Performance Tests**: Load testing with multiple nodes
- **Benchmark Tests**: Query execution time measurement
- **Stress Tests**: System behavior under extreme load

#### Operations
- **Metrics**: Response times, throughput, resource utilization
- **Alerts**: Performance degradation, resource exhaustion
- **Troubleshooting**: Performance profiling tools

---

## Operations & Monitoring

### Knowledge Graph Entry: System Monitoring and Operations

#### Context
- **Purpose**: Comprehensive system observability and operational procedures
- **Scope**: Logging, metrics, health checks, deployment procedures
- **Dependencies**: Serilog, ASP.NET Core health checks, custom monitoring

#### Architecture
- **Design Pattern**: Structured logging with correlation IDs and centralized monitoring
- **Data Flow**: Application → Structured Logs → File System
- **Security Model**: Audit trails without sensitive data exposure

#### Implementation
- **Key Classes**:
  - `PerformanceMonitoringService.cs` - Custom metrics collection
  - Serilog configuration in `Program.cs`
  - Health check endpoints
- **Configuration**: Log levels, retention policies, monitoring intervals

#### Current Monitoring Status
✅ **Implemented**:
- Structured logging with Serilog
- File-based log storage
- Basic health checks

❌ **Missing**:
- Centralized log aggregation
- Application metrics dashboard
- Automated alerting system

#### Decisions & Rationale
- **Trade-offs**: File-based vs. centralized logging for simplicity
- **Performance**: Structured logging for efficient querying
- **Security**: Log sanitization to prevent data exposure

#### Testing Strategy
- **Monitoring Tests**: Log output validation
- **Health Check Tests**: Endpoint availability verification
- **Alerting Tests**: Alert trigger validation

#### Operations
- **Metrics**: System health, performance counters, error rates
- **Alerts**: System failures, performance degradation
- **Troubleshooting**: Log analysis, health check diagnostics

---

## Testing Strategies

### Knowledge Graph Entry: Comprehensive Testing Framework

#### Context
- **Purpose**: Ensure code quality, security, and performance across all system components
- **Scope**: Unit testing, integration testing, security testing, performance testing
- **Dependencies**: xUnit, Moq, TestContainers, security testing tools

#### Architecture
- **Design Pattern**: Layered testing strategy with different test types
- **Data Flow**: Code → Unit Tests → Integration Tests → Security Tests
- **Security Model**: Isolated test environments with mock data

#### Implementation
- **Test Projects**:
  - `VCDevTool.API.Tests` - API layer testing
  - Integration test setup with TestContainers
- **Current Coverage**: Baseline established, needs expansion

#### Required Testing Improvements
❌ **Missing**:
- Comprehensive unit test coverage (target: 80%)
- Security penetration testing
- Performance benchmark testing
- Concurrency and race condition testing

#### Decisions & Rationale
- **Trade-offs**: Test coverage vs. development speed
- **Performance**: Isolated test environments for reliability
- **Security**: Security-focused testing scenarios

#### Testing Strategy Implementation
- **Unit Tests**: Service layer logic, validation, error handling
- **Integration Tests**: API endpoints, database operations, SignalR
- **Security Tests**: Authentication, authorization, input validation
- **Performance Tests**: Load testing, concurrency testing

#### Operations
- **Metrics**: Test coverage percentage, test execution time
- **Alerts**: Test failures in CI/CD pipeline
- **Troubleshooting**: Test failure analysis and resolution

---

## Known Issues & Technical Debt

### Critical Security Vulnerabilities

#### 🚨 CRITICAL: Exposed Secrets in Configuration
- **Location**: `VCDevTool.API/appsettings.json` lines 12-15
- **Risk**: Complete authentication bypass and database compromise
- **Resolution Priority**: 1 (Immediate)
- **Required Action**: Move to environment variables or Azure Key Vault

#### 🚨 HIGH: Race Condition in Node Registration  
- **Location**: `TaskService.cs` lines 159-222
- **Risk**: Data inconsistency with multiple nodes using same hardware fingerprint
- **Resolution Priority**: 2 (Week 1-2)
- **Required Action**: Implement atomic node registration with serializable transactions

#### 🚨 HIGH: File Lock Race Condition
- **Location**: `TaskService.cs` lines 389-520
- **Risk**: Potential data corruption in multi-step lock acquisition
- **Resolution Priority**: 2 (Week 1-2)
- **Required Action**: Implement Redis-based distributed locking

### Architectural Violations

#### 🔴 VIOLATION: Service Class Size Limit
- **Location**: `TaskService.cs` (885 lines)
- **Standard**: Maximum 300 lines per service class
- **Impact**: Difficult to maintain and test
- **Required Action**: Refactor into focused services

#### 🔴 VIOLATION: Missing Dependency Injection Interfaces
- **Impact**: Difficult to unit test and mock
- **Required Action**: Create interfaces for all services

### Performance Issues

#### 🔶 PERFORMANCE: Missing Pagination
- **Location**: Multiple list endpoints
- **Impact**: Poor performance with large datasets
- **Required Action**: Implement pagination for all list operations

#### 🔶 PERFORMANCE: Potential N+1 Query Problems
- **Location**: Various service methods
- **Impact**: Database performance degradation
- **Required Action**: Optimize queries and implement eager loading

---

## Decision Records

### ADR-001: Hybrid Authentication Strategy

**Date**: 2024-12  
**Status**: Implemented  
**Context**: Need to support both corporate Active Directory and standalone deployments

**Decision**: Implement hybrid authentication with JWT as primary and Windows Authentication as optional fallback

**Consequences**: 
- ✅ Supports multiple deployment scenarios
- ✅ Maintains security standards
- ❌ Increased configuration complexity
- ❌ Multiple authentication paths to maintain

### ADR-002: Hub-and-Spoke Architecture

**Date**: 2024-12  
**Status**: Implemented  
**Context**: Need centralized coordination for distributed file processing

**Decision**: Implement centralized API with distributed processing nodes

**Consequences**:
- ✅ Centralized monitoring and control
- ✅ Simplified file locking coordination
- ✅ Single point of configuration
- ❌ Single point of failure risk
- ❌ Potential bottleneck at API layer

### ADR-003: Entity Framework Code-First

**Date**: 2024-12  
**Status**: Implemented  
**Context**: Need rapid development with maintainable database schema

**Decision**: Use Entity Framework Code-First with migrations

**Consequences**:
- ✅ Rapid development and prototyping
- ✅ Version-controlled schema changes
- ✅ Strong typing and IntelliSense
- ❌ Less database optimization control
- ❌ Migration complexity in production

### ADR-004: WPF Client Technology

**Date**: 2024-12  
**Status**: Implemented  
**Context**: Need rich desktop experience for system administrators

**Decision**: Use WPF with MVVM pattern for desktop client

**Consequences**:
- ✅ Rich desktop user experience
- ✅ Strong integration with Windows environment
- ✅ Real-time updates with SignalR
- ❌ Windows-only deployment
- ❌ Limited cross-platform support

---

## Knowledge Graph Maintenance

### Update Triggers
This knowledge graph should be updated when:
- ✅ New architectural components are added
- ✅ Security models are modified
- ✅ Performance optimization strategies change
- ✅ New design patterns are implemented
- ✅ Critical issues are discovered or resolved
- ✅ Major refactoring occurs
- ✅ Deployment procedures change

### Validation Checklist
Before merging any PR, ensure:
- [ ] Knowledge graph has been reviewed for updates
- [ ] New patterns are documented
- [ ] Security implications are recorded
- [ ] Performance impact is analyzed
- [ ] Operational procedures are updated
- [ ] Decision records are created for major changes

### Cursor AI Integration Points
- ✅ Reference existing patterns before suggesting new code
- ✅ Validate security requirements against threat models
- ✅ Ensure architectural decisions are followed
- ✅ Suggest knowledge graph updates for new components
- ✅ Check for compliance with established standards

---

**Document Version**: 1.0  
**Last Updated**: December 2024  
**Next Review**: Quarterly or upon major architectural changes 