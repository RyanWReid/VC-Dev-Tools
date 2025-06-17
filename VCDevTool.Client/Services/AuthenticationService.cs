using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using VCDevTool.Shared;
using System.Net;
using System.Security.Principal;
using System.Threading;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VCDevTool.Client.Services
{
    public interface IAuthenticationService
    {
        string? CurrentToken { get; }
        string? CurrentNodeId { get; }
        string? CurrentApiKey { get; }
        bool IsAuthenticated { get; }
        Task<bool> RegisterAsync(ComputerNode node);
        Task<bool> LoginAsync(string nodeId, string hardwareFingerprint);
        Task<bool> RefreshTokenAsync();
        Task<bool> AuthenticateAsync(ComputerNode node);
        void Logout();
        event EventHandler<AuthenticationEventArgs>? AuthenticationChanged;
    }

    public class AuthenticationEventArgs : EventArgs
    {
        public bool IsAuthenticated { get; set; }
        public string? Token { get; set; }
        public string? NodeId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class AuthenticationService : IAuthenticationService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly System.Threading.Timer? _tokenRefreshTimer;
        private string? _currentToken;
        private string? _currentNodeId;
        private string? _currentApiKey;
        private DateTime _tokenExpiry = DateTime.MinValue;
        private readonly SemaphoreSlim _authSemaphore = new(1, 1);
        private readonly string _credentialsFilePath;
        
        public AuthenticationService(string baseUrl)
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Set up credentials file path in user's app data
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCDevTool");
            Directory.CreateDirectory(appDataPath);
            _credentialsFilePath = Path.Combine(appDataPath, "auth.dat");

            // Load persisted credentials on startup
            LoadPersistedCredentials();

            // Start automatic token refresh timer (check every 30 minutes)
            _tokenRefreshTimer = new System.Threading.Timer(AutoRefreshToken, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        }

        public string? CurrentToken => _currentToken;
        public string? CurrentNodeId => _currentNodeId;
        public string? CurrentApiKey => _currentApiKey;
        public bool IsAuthenticated => !string.IsNullOrEmpty(_currentToken) && _tokenExpiry > DateTime.UtcNow;

        public event EventHandler<AuthenticationEventArgs>? AuthenticationChanged;

        private void LoadPersistedCredentials()
        {
            try
            {
                if (File.Exists(_credentialsFilePath))
                {
                    var encryptedData = File.ReadAllBytes(_credentialsFilePath);
                    var decryptedData = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
                    var credentialsJson = Encoding.UTF8.GetString(decryptedData);
                    var credentials = JsonSerializer.Deserialize<PersistedCredentials>(credentialsJson, _jsonOptions);
                    
                    if (credentials != null && credentials.ExpiresAt > DateTime.UtcNow)
                    {
                        _currentToken = credentials.Token;
                        _currentNodeId = credentials.NodeId;
                        _currentApiKey = credentials.ApiKey;
                        _tokenExpiry = credentials.ExpiresAt;
                        
                        // Update HTTP client with the token
                        _httpClient.DefaultRequestHeaders.Authorization = 
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _currentToken);
                            
                        System.Diagnostics.Debug.WriteLine($"Loaded persisted credentials for node: {_currentNodeId}");
                    }
                    else
                    {
                        // Credentials expired, delete the file
                        File.Delete(_credentialsFilePath);
                        System.Diagnostics.Debug.WriteLine("Persisted credentials expired, deleted");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load persisted credentials: {ex.Message}");
                // If loading fails, delete the possibly corrupted file
                try { File.Delete(_credentialsFilePath); } catch { }
            }
        }

        private void SaveCredentials()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentToken) || string.IsNullOrEmpty(_currentNodeId))
                {
                    // No credentials to save, delete existing file if it exists
                    if (File.Exists(_credentialsFilePath))
                        File.Delete(_credentialsFilePath);
                    return;
                }

                var credentials = new PersistedCredentials
                {
                    Token = _currentToken,
                    NodeId = _currentNodeId,
                    ApiKey = _currentApiKey,
                    ExpiresAt = _tokenExpiry
                };

                var credentialsJson = JsonSerializer.Serialize(credentials, _jsonOptions);
                var dataToEncrypt = Encoding.UTF8.GetBytes(credentialsJson);
                var encryptedData = ProtectedData.Protect(dataToEncrypt, null, DataProtectionScope.CurrentUser);
                
                File.WriteAllBytes(_credentialsFilePath, encryptedData);
                System.Diagnostics.Debug.WriteLine($"Saved credentials for node: {_currentNodeId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save credentials: {ex.Message}");
            }
        }

        public async Task<bool> AuthenticateAsync(ComputerNode node)
        {
            await _authSemaphore.WaitAsync();
            try
            {
                // If we already have valid credentials, use them
                if (IsAuthenticated && _currentNodeId == node.Id)
                {
                    OnAuthenticationChanged(true);
                    return true;
                }

                // Try registration first (for new nodes)
                var success = await RegisterAsync(node);
                if (success)
                {
                    return true;
                }

                // If registration fails, try login (node might already exist)
                success = await LoginAsync(node.Id, node.HardwareFingerprint ?? "");
                if (success)
                {
                    return true;
                }

                // Both failed, notify and return false
                OnAuthenticationChanged(false, "Authentication failed: Unable to register or login");
                return false;
            }
            finally
            {
                _authSemaphore.Release();
            }
        }

        public async Task<bool> RegisterAsync(ComputerNode node)
        {
            try
            {
                var registerRequest = new
                {
                    Id = node.Id,
                    Name = node.Name,
                    IpAddress = node.IpAddress,
                    HardwareFingerprint = node.HardwareFingerprint,
                    // Include Windows identity information for enhanced authentication
                    WindowsIdentity = GetWindowsIdentityInfo()
                };

                // DEBUG: Log what we're sending
                var requestJson = JsonSerializer.Serialize(registerRequest, _jsonOptions);
                System.Diagnostics.Debug.WriteLine($"[AUTH] Sending registration request to: {_httpClient.BaseAddress}api/auth/register");
                System.Diagnostics.Debug.WriteLine($"[AUTH] Request body: {requestJson}");
                
                var response = await _httpClient.PostAsJsonAsync("api/auth/register", registerRequest, _jsonOptions);
                
                // DEBUG: Log the response
                System.Diagnostics.Debug.WriteLine($"[AUTH] Response status: {response.StatusCode}");
                var responseContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[AUTH] Response content: {responseContent}");
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
                    if (result != null && !string.IsNullOrEmpty(result.Token))
                    {
                        UpdateTokens(result);
                        OnAuthenticationChanged(true);
                        return true;
                    }
                }
                else if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    // Node already exists, this is expected for re-registration
                    System.Diagnostics.Debug.WriteLine($"Node {node.Id} already exists, will attempt login");
                    return false;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Registration failed with status {response.StatusCode}: {errorContent}");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Registration failed: {ex.Message}");
                OnAuthenticationChanged(false, $"Registration error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> LoginAsync(string nodeId, string hardwareFingerprint)
        {
            try
            {
                var loginRequest = new
                {
                    NodeId = nodeId,
                    ApiKey = "", // Empty API key - server will validate based on hardware fingerprint
                    HardwareFingerprint = hardwareFingerprint,
                    // Include Windows identity information for enhanced authentication
                    WindowsIdentity = GetWindowsIdentityInfo()
                };

                var response = await _httpClient.PostAsJsonAsync("api/auth/login", loginRequest, _jsonOptions);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
                    if (result != null && !string.IsNullOrEmpty(result.Token))
                    {
                        UpdateTokens(result);
                        OnAuthenticationChanged(true);
                        return true;
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Login failed with status {response.StatusCode}: {errorContent}");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login failed: {ex.Message}");
                OnAuthenticationChanged(false, $"Login error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RefreshTokenAsync()
        {
            if (string.IsNullOrEmpty(_currentToken) || string.IsNullOrEmpty(_currentNodeId))
                return false;

            await _authSemaphore.WaitAsync();
            try
            {
                var response = await _httpClient.PostAsync("api/auth/refresh", null);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
                    if (result != null && !string.IsNullOrEmpty(result.Token))
                    {
                        UpdateTokens(result);
                        OnAuthenticationChanged(true);
                        System.Diagnostics.Debug.WriteLine("Token refreshed successfully");
                        return true;
                    }
                }
                
                // Refresh failed, clear token
                System.Diagnostics.Debug.WriteLine($"Token refresh failed with status: {response.StatusCode}");
                Logout();
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Token refresh failed: {ex.Message}");
                Logout();
                return false;
            }
            finally
            {
                _authSemaphore.Release();
            }
        }

        public void Logout()
        {
            _currentToken = null;
            _currentNodeId = null;
            _currentApiKey = null;
            _tokenExpiry = DateTime.MinValue;
            _httpClient.DefaultRequestHeaders.Authorization = null;
            
            // Clear saved credentials
            SaveCredentials();
            
            OnAuthenticationChanged(false);
        }

        private void UpdateTokens(AuthResponse result)
        {
            _currentToken = result.Token;
            _currentNodeId = result.NodeId;
            _currentApiKey = result.ApiKey;
            
            // Calculate token expiry (assuming 24-hour tokens as configured in the API)
            _tokenExpiry = result.ExpiresAt ?? DateTime.UtcNow.AddHours(23); // Refresh 1 hour early
            
            // Update HTTP client with the new token
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _currentToken);
                
            // Save credentials for persistence
            SaveCredentials();
        }

        private void OnAuthenticationChanged(bool isAuthenticated, string? errorMessage = null)
        {
            AuthenticationChanged?.Invoke(this, new AuthenticationEventArgs
            {
                IsAuthenticated = isAuthenticated,
                Token = _currentToken,
                NodeId = _currentNodeId,
                ErrorMessage = errorMessage
            });
        }

        private void AutoRefreshToken(object? state)
        {
            if (IsAuthenticated && _tokenExpiry.Subtract(DateTime.UtcNow).TotalHours < 2)
            {
                // Token expires in less than 2 hours, refresh it
                Task.Run(async () =>
                {
                    var success = await RefreshTokenAsync();
                    if (!success)
                    {
                        System.Diagnostics.Debug.WriteLine("Automatic token refresh failed");
                    }
                });
            }
        }

        private object GetWindowsIdentityInfo()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                return new
                {
                    Name = identity.Name,
                    User = identity.User?.ToString(),
                    AuthenticationType = identity.AuthenticationType,
                    IsAuthenticated = identity.IsAuthenticated,
                    Groups = identity.Groups?.Select(g => g.ToString()).ToArray() ?? Array.Empty<string>()
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get Windows identity: {ex.Message}");
                return new { };
            }
        }

        public void Dispose()
        {
            _tokenRefreshTimer?.Dispose();
            _authSemaphore?.Dispose();
            _httpClient?.Dispose();
        }
    }

    public class AuthResponse
    {
        public string? Token { get; set; }
        public string? ApiKey { get; set; }
        public string? NodeId { get; set; }
        public string? NodeName { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public List<string>? Roles { get; set; }
        public string? Message { get; set; }
    }

    public class PersistedCredentials
    {
        public string? Token { get; set; }
        public string? NodeId { get; set; }
        public string? ApiKey { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
} 