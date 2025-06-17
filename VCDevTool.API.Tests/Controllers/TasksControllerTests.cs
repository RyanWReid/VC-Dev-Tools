using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VCDevTool.API.Data;
using VCDevTool.API.Models;
using VCDevTool.Shared;
using VCDevTool.API.Tests.Data;
using VCDevTool.API.Tests.Infrastructure;
using Xunit;

namespace VCDevTool.API.Tests.Controllers
{
    public class TasksControllerTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public TasksControllerTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task GetTasks_ShouldReturnOkResult()
        {
            // Act
            var response = await _client.GetAsync("/api/tasks");

            // Assert
            response.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task CreateTask_ValidTask_ShouldReturnCreatedResult()
        {
            // Arrange
            var createRequest = new CreateTaskRequest
            {
                Name = "Test Task",
                Type = TaskType.FileProcessing,
                Parameters = "{\"inputPath\": \"/test/input\"}"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/tasks", createRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var createdTask = await response.Content.ReadFromJsonAsync<BatchTask>();
            Assert.NotNull(createdTask);
            Assert.Equal("Test Task", createdTask.Name);
            Assert.Equal(TaskType.FileProcessing, createdTask.Type);
        }

        [Fact]
        public async Task CreateTask_InvalidTask_ShouldReturnBadRequest()
        {
            // Arrange - Task with invalid name (too long)
            var createRequest = new CreateTaskRequest
            {
                Name = new string('A', 300), // Too long
                Type = TaskType.FileProcessing,
                Parameters = "{\"inputPath\": \"/test/input\"}"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/tasks", createRequest);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GetTask_ExistingTask_ShouldReturnTask()
        {
            // Arrange - Create a task first
            var createRequest = new CreateTaskRequest
            {
                Name = "Get Test Task",
                Type = TaskType.RenderThumbnails,
                Parameters = "{\"inputPath\": \"/test/get\"}"
            };

            var createResponse = await _client.PostAsJsonAsync("/api/tasks", createRequest);
            var createdTask = await createResponse.Content.ReadFromJsonAsync<BatchTask>();

            // Act
            var response = await _client.GetAsync($"/api/tasks/{createdTask!.Id}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var task = await response.Content.ReadFromJsonAsync<BatchTask>();
            Assert.NotNull(task);
            Assert.Equal(createdTask.Id, task.Id);
        }

        [Fact]
        public async Task GetTask_NonExistentTask_ShouldReturnNotFound()
        {
            // Act
            var response = await _client.GetAsync("/api/tasks/999999");

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task UpdateTask_ValidUpdate_ShouldReturnOkResult()
        {
            // Arrange - Create a task first
            var createRequest = new CreateTaskRequest
            {
                Name = "Update Test Task",
                Type = TaskType.FileProcessing,
                Parameters = "{\"inputPath\": \"/test/update\"}"
            };

            var createResponse = await _client.PostAsJsonAsync("/api/tasks", createRequest);
            var createdTask = await createResponse.Content.ReadFromJsonAsync<BatchTask>();

            var updateRequest = new UpdateTaskRequest
            {
                Status = BatchTaskStatus.Running,
                ResultMessage = "Task is now running"
            };

            // Act
            var response = await _client.PutAsJsonAsync($"/api/tasks/{createdTask!.Id}", updateRequest);

            // Assert
            response.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task DeleteTask_ExistingTask_ShouldReturnNoContent()
        {
            // Arrange - Create a task first
            var createRequest = new CreateTaskRequest
            {
                Name = "Delete Test Task",
                Type = TaskType.FileProcessing,
                Parameters = "{\"inputPath\": \"/test/delete\"}"
            };

            var createResponse = await _client.PostAsJsonAsync("/api/tasks", createRequest);
            var createdTask = await createResponse.Content.ReadFromJsonAsync<BatchTask>();

            // Act
            var response = await _client.DeleteAsync($"/api/tasks/{createdTask!.Id}");

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task GetTasksWithFilters_ShouldReturnFilteredResults()
        {
            // Arrange - Create multiple tasks
            var tasks = new[]
            {
                new CreateTaskRequest { Name = "Filter Test 1", Type = TaskType.FileProcessing, Parameters = "{}" },
                new CreateTaskRequest { Name = "Filter Test 2", Type = TaskType.VolumeCompression, Parameters = "{}" },
                new CreateTaskRequest { Name = "Filter Test 3", Type = TaskType.FileProcessing, Parameters = "{}" }
            };

            foreach (var task in tasks)
            {
                await _client.PostAsJsonAsync("/api/tasks", task);
            }

            // Act - Filter by type
            var response = await _client.GetAsync("/api/tasks?type=FileProcessing");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var filteredTasks = await response.Content.ReadFromJsonAsync<List<BatchTask>>();
            Assert.NotNull(filteredTasks);
            Assert.Equal(2, filteredTasks.Count);
        }
    }
} 