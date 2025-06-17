using System;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VCDevTool.Shared;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VCDevTool.Client.Services
{
    public class NodeService : INodeService
    {
        private ApiClient _apiClient;
        private ComputerNode _currentNode;
        private System.Threading.Timer? _heartbeatTimer;
        private const int HeartbeatIntervalMs = 30000; // 30 seconds
        private int _heartbeatFailures = 0;
        private const int MaxHeartbeatFailures = 3;
        private readonly IAuthenticationService _authService;

        public NodeService(ApiClient apiClient, IAuthenticationService? authService = null)
        {
            _apiClient = apiClient;
            _authService = authService ?? new AuthenticationService(apiClient.GetBaseUrl());
            _currentNode = CreateCurrentNode();
        }
        
        public ComputerNode CurrentNode => _currentNode;

        public void SetNodeAvailability(bool isAvailable)
        {
            _currentNode.IsAvailable = isAvailable;
            _currentNode.LastHeartbeat = DateTime.UtcNow;
            
            // Immediately propagate availability status to server
            _ = Task.Run(async () =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"Updating server: SetNodeAvailability({_currentNode.Id})->{isAvailable}");
                    bool updated = await _apiClient.UpdateNodeAsync(_currentNode);
                    if (!updated)
                        System.Diagnostics.Debug.WriteLine("Server did not accept availability update.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating node availability: {ex.Message}");
                }
            });
            
            if (isAvailable)
            {
                // Start sending heartbeats if activated
                StartHeartbeat();
            }
            else
            {
                // Stop heartbeats if deactivated
                StopHeartbeat();
            }
        }
        
        public async Task<ComputerNode> RegisterNodeAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[NODE] Starting node registration for: {_currentNode.Id} - {_currentNode.Name}");
                System.Diagnostics.Debug.WriteLine($"[NODE] Node IP: {_currentNode.IpAddress}");
                System.Diagnostics.Debug.WriteLine($"[NODE] Hardware Fingerprint: {_currentNode.HardwareFingerprint}");
                
                // AUTHENTICATION DISABLED - Just return the current node
                System.Diagnostics.Debug.WriteLine($"[NODE] Authentication disabled - returning node: {_currentNode.Id}");
                return _currentNode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RegisterNodeAsync: {ex.Message}");
                throw new Exception($"Failed to register node: {ex.Message}", ex);
            }
        }
        
        public void StartHeartbeat()
        {
            if (_heartbeatTimer == null)
            {
                _heartbeatTimer = new System.Threading.Timer(
                    async _ => await SendHeartbeatAsync(),
                    null,
                    0,
                    HeartbeatIntervalMs
                );
            }
        }
        
        public void StopHeartbeat()
        {
            if (_heartbeatTimer != null)
            {
                _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _heartbeatTimer.Dispose();
                _heartbeatTimer = null;
            }
        }
        
        private async Task SendHeartbeatAsync()
        {
            try
            {
                bool success = await _apiClient.SendHeartbeatAsync(_currentNode.Id);
                if (success)
                {
                    // Reset failure count on successful heartbeat
                    _heartbeatFailures = 0;
                    return;
                }
                throw new Exception("Heartbeat API returned failure status");
            }
            catch (Exception ex)
            {
                _heartbeatFailures++;
                System.Diagnostics.Debug.WriteLine($"Error sending heartbeat ({_heartbeatFailures}/{MaxHeartbeatFailures}): {ex.Message}");
                if (_heartbeatFailures >= MaxHeartbeatFailures)
                {
                    // Too many failures, attempt to re-register node
                    System.Diagnostics.Debug.WriteLine("Max heartbeat failures reached, re-registering node");
                    _heartbeatFailures = 0;
                    try
                    {
                        await RegisterNodeAsync();
                    }
                    catch (Exception regEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error during automatic re-registration: {regEx.Message}");
                    }
                }
            }
        }
        
        // Execute a test task to validate device is working
        public async Task<bool> ExecuteTestTask(BatchTask task)
        {
            try
            {
                // Update task status to running
                await _apiClient.UpdateTaskStatusAsync(task.Id, BatchTaskStatus.Running);

                // Simulate task execution
                await Task.Delay(2000);

                // Update task status to completed
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id, 
                    BatchTaskStatus.Completed, 
                    $"Hello World executed on {_currentNode.Name} at {DateTime.Now}");
                
                return true;
            }
            catch (Exception ex)
            {
                // Update task status to failed
                await _apiClient.UpdateTaskStatusAsync(
                    task.Id, 
                    BatchTaskStatus.Failed, 
                    $"Error executing Hello World: {ex.Message}");
                
                return false;
            }
        }

        public void UpdateApiClient(ApiClient apiClient)
        {
            _apiClient = apiClient;
            // Update the authentication service with the new API client base URL
            var newAuthService = new AuthenticationService(apiClient.GetBaseUrl());
            // Copy any existing authentication state if needed
        }

        private ComputerNode CreateCurrentNode()
        {
            var nodeId = GenerateNodeId();
            var ipAddress = GetLocalIPAddress();
            var hardwareFingerprint = GenerateHardwareFingerprint();
            
            return new ComputerNode
            {
                Id = nodeId,
                Name = Environment.MachineName,
                IpAddress = ipAddress,
                HardwareFingerprint = hardwareFingerprint,
                IsAvailable = true,
                LastHeartbeat = DateTime.UtcNow
            };
        }

        private string GenerateNodeId()
        {
            // Generate a consistent node ID based on machine name and MAC address
            var macAddress = GetMacAddress();
            var machineInfo = $"{Environment.MachineName}-{macAddress}";
            
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineInfo));
                return Convert.ToHexString(hash)[..16]; // Use first 16 characters
            }
        }

        private string GetMacAddress()
        {
            try
            {
                var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
                
                return networkInterface?.GetPhysicalAddress().ToString() ?? "UNKNOWN";
            }
            catch
            {
                return "UNKNOWN";
            }
        }

        private string GenerateHardwareFingerprint()
        {
            try
            {
                // Combine multiple hardware identifiers for a more unique fingerprint
                var macAddress = GetMacAddress();
                var processorId = Environment.ProcessorCount.ToString();
                var userName = Environment.UserName;
                var osVersion = Environment.OSVersion.ToString();
                
                var combined = $"{macAddress}-{processorId}-{userName}-{osVersion}";
                
                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                    return Convert.ToHexString(hash)[..24]; // Use first 24 characters
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating hardware fingerprint: {ex.Message}");
                return $"FALLBACK-{Environment.MachineName}-{DateTime.UtcNow.Ticks}";
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                // Try multiple methods to get a valid IP address
                
                // Method 1: Get from network interfaces (most reliable)
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                    .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                                  !IPAddress.IsLoopback(addr.Address) &&
                                  !addr.Address.ToString().StartsWith("169.254")) // Exclude APIPA
                    .Select(addr => addr.Address.ToString())
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(networkInterfaces))
                    return networkInterfaces;

                // Method 2: DNS resolution fallback
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var localIP = host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && 
                                         !IPAddress.IsLoopback(ip) &&
                                         !ip.ToString().StartsWith("169.254"));
                
                if (localIP != null)
                    return localIP.ToString();

                // Method 3: Generate a unique localhost IP as fallback
                // This ensures we always have a valid IP address for validation
                var random = new Random();
                var uniqueId = random.Next(100, 254); // Avoid common addresses like 127.0.0.1
                return $"127.0.0.{uniqueId}";
            }
            catch (Exception ex)
            {
                // Last resort: use localhost fallback
                return "127.0.0.100"; // Safe default localhost IP
            }
        }
    }
} 