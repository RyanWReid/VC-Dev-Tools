using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Reflection.Metadata.Ecma335;

namespace VCDevTool.Shared
{
    /// <summary>
    /// Provides robust device identification capabilities by collecting and combining
    /// multiple hardware identifiers to create a stable device fingerprint.
    /// </summary>
    public static class DeviceIdentifier
    {
        private const string IdentifierFileName = "device_identifier.dat";
        private static string _cachedIdentifier = null;

        /// <summary>
        /// Gets a unique, stable identifier for the current device.
        /// This identifier persists across application restarts and is hardware-based.
        /// </summary>
        /// <returns>A string representing the unique device identifier</returns>
        public static string GetDeviceId()
        {
            // Return cached value if available
            if (!string.IsNullOrEmpty(_cachedIdentifier))
            {
                return _cachedIdentifier;
            }

            // Try to load from persistent storage first
            var storedId = LoadStoredIdentifier();
            if (!string.IsNullOrEmpty(storedId))
            {
                _cachedIdentifier = storedId;
                return storedId;
            }

            // Generate a new identifier if none exists
            var hardwareId = GenerateHardwareIdentifier();
            StoreIdentifier(hardwareId);
            _cachedIdentifier = hardwareId;
            return hardwareId;
        }

        /// <summary>
        /// Gets a friendly name for the current device, typically the machine name.
        /// </summary>
        /// <returns>A string representing the device name</returns>
        public static string GetDeviceName()
        {
            return Environment.MachineName;
        }

        /// <summary>
        /// Generates a hardware-based identifier by combining multiple system characteristics.
        /// </summary>
        private static string GenerateHardwareIdentifier()
        {
            var components = new StringBuilder();

            // Add CPU information
            components.Append(GetCpuInfo());

            // Add motherboard/BIOS information
            components.Append(GetMotherboardInfo());

            // Add primary MAC address
            components.Append(GetMacAddress());
            
            // Add volume serial number from system drive
            components.Append(GetSystemDriveSerialNumber());

            // Fallback addition of machine name (less stable but helps with uniqueness)
            components.Append(Environment.MachineName);

            // Hash the combined components to create the final identifier
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(components.ToString()));
                var deviceId = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16);
                
                // Format it nicely with hyphens
                return $"{deviceId.Substring(0, 4)}-{deviceId.Substring(4, 4)}-{deviceId.Substring(8, 4)}-{deviceId.Substring(12, 4)}";
            }
        }

        private static string GetCpuInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (var mo in searcher.Get())
                    {
                        return mo["ProcessorId"]?.ToString() ?? string.Empty;
                    }
                }
            }

            catch (Exception)
            {
                // Silently fail and use a fallback
            }

            return Environment.ProcessorCount.ToString();
        }
        private static string GetMotherboardInfo()
        {
            try
            {
                // Try to get motherboard serial number
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (var mo in searcher.Get())
                    {
                        var serial = mo["SerialNumber"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(serial))
                            return serial;
                    }
                }

                // Fallback to BIOS serial
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS"))
                {
                    foreach (var mo in searcher.Get())
                    {
                        var serial = mo["SerialNumber"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(serial))
                            return serial;
                    }
                }
            }
            catch (Exception)
            {
                // Silently fail and use a fallback
            }

            return string.Empty;
        }

        private static string GetMacAddress()
        {
            try
            {
                // Get MAC address of the first operational network interface
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up && 
                                 nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(nic => nic.GetPhysicalAddress().ToString())
                    .FirstOrDefault() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string GetSystemDriveSerialNumber()
        {
            try
            {
                var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
                using (var searcher = new ManagementObjectSearcher($"SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID='{systemDrive}'"))
                {
                    foreach (var mo in searcher.Get())
                    {
                        return mo["VolumeSerialNumber"]?.ToString() ?? string.Empty;
                    }
                }
            }
            catch (Exception)
            {
                // Silently fail
            }

            return string.Empty;
        }

        private static string LoadStoredIdentifier()
        {
            try
            {
                var filePath = GetStorageFilePath();
                if (File.Exists(filePath))
                {
                    return File.ReadAllText(filePath).Trim();
                }
            }
            catch (Exception)
            {
                // Silently fail
            }

            return null;
        }

        private static void StoreIdentifier(string identifier)
        {
            try
            {
                var filePath = GetStorageFilePath();
                File.WriteAllText(filePath, identifier);
            }
            catch (Exception)
            {
                // Silently fail
            }
        }

        private static string GetStorageFilePath()
        {
            // Store in a location accessible to the application
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "VCDevTool");
            
            // Ensure the directory exists
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }
            
            return Path.Combine(appFolder, IdentifierFileName);
        }
    }
}  