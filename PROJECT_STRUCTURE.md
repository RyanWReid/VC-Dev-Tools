# VCDevTool Project Structure Overview

## Introduction
VCDevTool is a distributed task processing system designed for media-related workflows. It consists of a client-server architecture where multiple nodes can register and execute various specialized tasks related to 3D modeling, volume compression, and image processing.

## Project Organization

### Folder Structure

- **VCDevTool.API/** - Backend API service that manages tasks, nodes, and distributed processing
  - **Controllers/** - API endpoints for tasks, nodes, file locks, and health monitoring
  - **Data/** - Database context and entity configurations
  - **Hubs/** - SignalR hubs for real-time notifications
  - **Migrations/** - Entity Framework Core database migrations
  - **Services/** - Core business logic implementation

- **VCDevTool.Client/** - WPF-based desktop client application
  - **Commands/** - Command implementations for UI interactions
  - **Controls/** - Custom WPF controls
  - **Converters/** - Value converters for data binding
  - **Models/** - Client-side data models
  - **Resources/** - UI resources like styles and images
  - **Services/** - Client-side services for task execution and API communication
  - **ViewModels/** - MVVM view models
  - **Views/** - WPF views and dialogs

- **VCDevTool.Shared/** - Shared models and interfaces used by both client and API
  - **Models/** - Core domain models shared across projects

- **VCDevTool.ShellExtension/** - Windows shell extension component
  
- **docs/** - Documentation files
  
- **tools/** - Utility scripts and tools

### Key Files
- **VCDevTool.sln** - Main solution file
- **VCDevTool.API/Program.cs** - API startup configuration
- **VCDevTool.Client/App.xaml.cs** - Client application entry point
- **VCDevTool.Client/MainWindow.xaml** - Main application UI

## Key Components

### Core Models

#### BatchTask
Represents a task in the system with the following properties:
- ID, Name, Type, Status
- Assigned Node
- Timing information (Created, Started, Completed)
- Parameters and results

#### ComputerNode
Represents a machine in the distributed network:
- ID, Name, IP Address
- Availability status and heartbeat
- Hardware fingerprint for unique identification

#### FileLock
Manages concurrent access to shared resources:
- File path being locked
- Node that owns the lock
- Acquisition and update timestamps

### API Controllers

#### TasksController
Provides endpoints for:
- Creating and retrieving tasks
- Updating task status
- Assigning tasks to nodes

#### NodesController
Manages node registration and monitoring:
- Node registration and heartbeat updates
- Retrieving available and all nodes

#### FileLocksController
Handles distributed file locking:
- Acquiring and releasing locks
- Retrieving active locks

### Client Services

#### TaskExecutionService
Manages task execution on the client node:
- Task polling and processing
- Task status updates
- Specialized task handlers for different task types:
  - Test messages
  - Thumbnail rendering
  - Package tasks
  - Volume compression
  - Reality Capture processing

#### ApiClient
Communicates with the API:
- Task management (retrieve, update status)
- Node registration and heartbeat
- File lock operations

#### NodeService
Manages the current node's identity and state:
- Registration with the API
- Heartbeat updates
- Hardware fingerprint generation

#### TaskHubClient
Connects to SignalR hub for real-time notifications:
- Task assignments
- Status updates
- Debugging messages

### ViewModels (MVVM)

#### MainViewModel
Main application coordinator:
- Managing nodes and tasks
- UI state and dialogs
- Command bindings
- Task execution

#### NodeViewModel
Represents a node in the UI:
- Status information
- Active task display
- Progress tracking

#### TaskViewModel
Represents a task in the UI:
- Status display
- Timing information
- Result messages

#### WatcherViewModel
Manages file system watching functionality:
- Monitoring directories for changes
- Synchronization operations

## Architectural Patterns

### MVVM (Model-View-ViewModel)
The client application follows the MVVM pattern:
- **Models** - Data representations (BatchTask, ComputerNode)
- **ViewModels** - UI state and behavior (MainViewModel, NodeViewModel)
- **Views** - User interface elements (XAML files)
- **Commands** - UI actions (RelayCommand implementation)

### Service-Oriented Architecture
- **API Services** - Core business logic implementation
- **Client Services** - Client-side business logic
- **Shared Interfaces** - Contracts between components

### Repository Pattern
The TaskService implements repository-like patterns for data access:
- CRUD operations for tasks and nodes
- Database interactions via Entity Framework Core

### Publish-Subscribe Pattern
Via SignalR for real-time communication:
- Task notifications
- Debug message broadcasting
- Client-server events

### Distributed Task Processing
- Nodes register with the API
- Tasks assigned to specific nodes
- Nodes poll for and execute tasks
- Centralized monitoring of distributed work

### File Locking Mechanism
Prevents concurrent access to shared resources:
- Atomic lock acquisition via the database
- Timeout-based lock expiration
- Lock release after processing

## Design Patterns

### Singleton Pattern
Services are generally registered as singletons:
- ApiClient, NodeService, TaskExecutionService

### Command Pattern
- RelayCommand implementation for UI actions
- Command bindings in XAML

### Factory Method Pattern
- TaskExecutionService creates appropriate task handlers

### Observer Pattern
- PropertyChanged events for UI updates
- SignalR notifications for real-time updates

## Areas of Concern

### Code Smells

1. **Large Classes**
   - TaskExecutionService (2200+ lines) has too many responsibilities
   - MainViewModel (2600+ lines) handles too many concerns

2. **Error Handling**
   - Some exception handling is inconsistent
   - Error propagation not always clear

3. **Timeout Management**
   - Hard-coded timeout values could be configurable

4. **Task-specific Logic**
   - Task type-specific code is mixed rather than having dedicated handlers

5. **Configuration Management**
   - Some paths are hardcoded (Cinema 4D, Reality Capture)

### Design Concerns

1. **Tight Coupling**
   - Some components have direct dependencies that could be abstracted

2. **Concurrency**
   - Potential race conditions in task status updates
   - Lock management could be more robust

3. **Testing**
   - No visible test projects or mocks

4. **Security**
   - No obvious authentication/authorization mechanisms
   - Distributed file access security concerns

## Enhancement Opportunities

1. **Modular Task Handlers**
   - Split task execution logic into specialized handlers

2. **Improved Configuration**
   - Move hard-coded paths to configuration

3. **Distributed Caching**
   - Add caching to reduce database load

4. **Resilience Patterns**
   - Implement circuit breakers, retries, and fallbacks

5. **Advanced Monitoring**
   - Add more comprehensive logging and performance metrics

6. **Task Scheduling**
   - Add scheduled tasks and recurring jobs 