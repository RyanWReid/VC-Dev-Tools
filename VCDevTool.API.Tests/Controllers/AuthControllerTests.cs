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
    public class AuthControllerTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public AuthControllerTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task Register_ValidNode_ShouldReturnCreated()
        {
            // Arrange
            var registerRequest = new RegisterNodeRequest
            {
                Id = "test-node-001",
                Name = "Test Node",
                IpAddress = "192.168.1.100",
                HardwareFingerprint = "ABC123-DEF456-GHI789"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(result.TryGetProperty("nodeId", out var nodeId));
            Assert.Equal("test-node-001", nodeId.GetString());
            Assert.True(result.TryGetProperty("token", out var token));
            Assert.False(string.IsNullOrEmpty(token.GetString()));
        }

        [Fact]
        public async Task Register_DuplicateNode_ShouldReturnConflict()
        {
            // Arrange
            var registerRequest = new RegisterNodeRequest
            {
                Id = "duplicate-node",
                Name = "Duplicate Node",
                IpAddress = "192.168.1.101",
                HardwareFingerprint = "DUPLICATE-123"
            };

            // First registration
            await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Act - Try to register again
            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Register_InvalidNodeData_ShouldReturnBadRequest()
        {
            // Arrange
            var registerRequest = new RegisterNodeRequest
            {
                Id = "", // Invalid - empty ID
                Name = "Test Node",
                IpAddress = "192.168.1.100",
                HardwareFingerprint = "ABC123"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Login_ValidCredentials_ShouldReturnToken()
        {
            // Arrange - First register a node
            var registerRequest = new RegisterNodeRequest
            {
                Id = "login-test-node",
                Name = "Login Test Node",
                IpAddress = "192.168.1.101",
                HardwareFingerprint = "LOGIN-TEST-123"
            };
            
            await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            var loginRequest = new LoginRequest
            {
                NodeId = "login-test-node",
                HardwareFingerprint = "LOGIN-TEST-123"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(result.TryGetProperty("token", out var token));
            Assert.False(string.IsNullOrEmpty(token.GetString()));
        }

        [Fact]
        public async Task Login_InvalidCredentials_ShouldReturnUnauthorized()
        {
            // Arrange
            var loginRequest = new LoginRequest
            {
                NodeId = "non-existent-node",
                HardwareFingerprint = "INVALID-FINGERPRINT"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Login_EmptyCredentials_ShouldReturnBadRequest()
        {
            // Arrange
            var loginRequest = new LoginRequest
            {
                NodeId = "",
                HardwareFingerprint = ""
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Register_InvalidIpAddress_ShouldReturnBadRequest()
        {
            // Arrange
            var registerRequest = new RegisterNodeRequest
            {
                Id = "test-node-invalid-ip",
                Name = "Test Node",
                IpAddress = "999.999.999.999", // Invalid IP
                HardwareFingerprint = "ABC123"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Register_SpecialCharactersInNodeId_ShouldReturnBadRequest()
        {
            // Arrange
            var registerRequest = new RegisterNodeRequest
            {
                Id = "test@node#123!", // Invalid characters
                Name = "Test Node",
                IpAddress = "192.168.1.102",
                HardwareFingerprint = "ABC123"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("192.168.1.1")]
        [InlineData("127.0.0.1")]
        [InlineData("::1")]
        [InlineData("2001:db8::1")]
        public async Task Register_ValidIpAddresses_ShouldSucceed(string ipAddress)
        {
            // Arrange
            var registerRequest = new RegisterNodeRequest
            {
                Id = $"ip-test-{ipAddress.Replace(":", "-").Replace(".", "-")}",
                Name = $"IP Test Node {ipAddress}",
                IpAddress = ipAddress,
                HardwareFingerprint = $"IP-TEST-{ipAddress.GetHashCode()}"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            Assert.True(response.StatusCode == HttpStatusCode.Created || 
                       response.StatusCode == HttpStatusCode.Conflict, // Might already exist
                $"Failed for IP address: {ipAddress}. Status: {response.StatusCode}");
        }
    }
} 