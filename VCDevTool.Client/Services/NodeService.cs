using System;
using System.Net.NetworkInformation;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using VCDevTool.Shared;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace VCDevTool.Client.Services
{
    public class NodeService
    {
        private ApiClient _apiClient;
        private ComputerNode _currentNode;
        private System.Threading.Timer? _heartbeatTimer;
        private const int HeartbeatIntervalMs = 30000; // 30 seconds
        private int _heartbeatFailures = 0;
        private const int MaxHeartbeatFailures = 3;

        public NodeService(ApiClient apiClient)
        {
            _apiClient = apiClient;
            
            // Use the robust device identifier to create a unique, stable node ID
            _currentNode = new ComputerNode
            {
                Id = DeviceIdentifier.GetDeviceId(),
                Name = DeviceIdentifier.GetDeviceName(),
                IpAddress = GetLocalIpAddress(),
                HardwareFingerprint = GenerateHardwareFingerprint()
            };
        }
        
        public ComputerNode CurrentNode => _currentNode;

        public void SetNodeAvailability(bool isAvailable)
        {
            _currentNode.IsAvailable = isAvailable;
            
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
        
        public async Task<bool> RegisterNodeAsync()
        {
            try
            {
                var registeredNode = await _apiClient.RegisterNodeAsync(_currentNode);
                if (registeredNode != null)
                {
                    // Update local node info with any server-side updates
                    _currentNode.Name = registeredNode.Name;
                    _currentNode.IpAddress = registeredNode.IpAddress;
                    _currentNode.IsAvailable = registeredNode.IsAvailable;
                    _currentNode.LastHeartbeat = registeredNode.LastHeartbeat;
                    // Start sending heartbeats to keep node alive
                    StartHeartbeat();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error registering node: {ex.Message}");
                return false;
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
            if (apiClient == null)
                throw new ArgumentNullException(nameof(apiClient));
            
            _apiClient = apiClient;
            
            // Restart heartbeat and re-register with the new API client if active
            if (_heartbeatTimer != null)
            {
                StopHeartbeat();
                // Attempt to re-register and start heartbeats under new API client
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RegisterNodeAsync();
                    }
                    catch { /* swallow to avoid unobserved */ }
                });
            }
        }

        private string GetLocalIpAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }

        /// <summary>
        /// Generates additional hardware fingerprint information beyond the basic device ID
        /// </summary>
        private string GenerateHardwareFingerprint()
        {
            // Combine multiple hardware details to create a comprehensive fingerprint
            // This is in addition to the DeviceId which is already hardware-based
            try
            {
                var macAddress = GetPrimaryMacAddress();
                var totalMemory = GetTotalPhysicalMemory();
                var fingerprint = $"{macAddress}|{totalMemory}";
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fingerprint));
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetPrimaryMacAddress()
        {
            try
            {
                // Find the most suitable network interface
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => 
                        n.OperationalStatus == OperationalStatus.Up && 
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.GetPhysicalAddress().ToString().Length > 0);

                if (nic != null)
                {
                    return nic.GetPhysicalAddress().ToString();
                }
            }
            catch {}

            return "UNKNOWN";
        }

        private string GetTotalPhysicalMemory()
        {
            try
            {
                return Environment.WorkingSet.ToString();
            }
            catch
            {
                return "0";
            }
        }
    }
} 