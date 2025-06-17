using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class TaskCancelTest
{
    private static readonly HttpClient client = new HttpClient();
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing task cancellation functionality...");
        
        client.BaseAddress = new Uri("http://localhost:5289");
        
        try
        {
            // Test connection first
            Console.WriteLine("1. Testing API connection...");
            var healthResponse = await client.GetAsync("/api/nodes");
            Console.WriteLine($"API connection: {healthResponse.StatusCode}");
            
            // Create a test task
            Console.WriteLine("\n2. Creating test task...");
            var taskData = new
            {
                Name = "Test Cancellation Task",
                Type = 1, // TestMessage
                TargetPath = "C:\\temp\\test",
                Status = 0 // Pending
            };
            
            var taskJson = JsonSerializer.Serialize(taskData);
            var taskContent = new StringContent(taskJson, Encoding.UTF8, "application/json");
            var taskResponse = await client.PostAsync("/api/tasks", taskContent);
            
            if (!taskResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to create task: {taskResponse.StatusCode}");
                Console.WriteLine(await taskResponse.Content.ReadAsStringAsync());
                return;
            }
            
            var taskResponseContent = await taskResponse.Content.ReadAsStringAsync();
            var task = JsonSerializer.Deserialize<JsonElement>(taskResponseContent);
            var taskId = task.GetProperty("id").GetInt32();
            Console.WriteLine($"Created task ID: {taskId}");
            
            // Set task to Running
            Console.WriteLine("\n3. Setting task to Running...");
            var runningData = new
            {
                Status = 1, // Running
                ResultMessage = "Task started"
            };
            
            var runningJson = JsonSerializer.Serialize(runningData);
            var runningContent = new StringContent(runningJson, Encoding.UTF8, "application/json");
            var runningResponse = await client.PutAsync($"/api/tasks/{taskId}/status", runningContent);
            
            Console.WriteLine($"Set to Running: {runningResponse.StatusCode}");
            
            // Cancel the task
            Console.WriteLine("\n4. Cancelling task...");
            var cancelData = new
            {
                Status = 4, // Cancelled
                ResultMessage = "Task was manually aborted by user"
            };
            
            var cancelJson = JsonSerializer.Serialize(cancelData);
            var cancelContent = new StringContent(cancelJson, Encoding.UTF8, "application/json");
            var cancelResponse = await client.PutAsync($"/api/tasks/{taskId}/status", cancelContent);
            
            Console.WriteLine($"Cancel response: {cancelResponse.StatusCode}");
            
            if (cancelResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("SUCCESS: Task cancelled successfully!");
                
                // Verify final status
                var verifyResponse = await client.GetAsync($"/api/tasks/{taskId}");
                var verifyContent = await verifyResponse.Content.ReadAsStringAsync();
                var verifyTask = JsonSerializer.Deserialize<JsonElement>(verifyContent);
                var finalStatus = verifyTask.GetProperty("status").GetInt32();
                Console.WriteLine($"Final task status: {finalStatus} (4 = Cancelled)");
            }
            else
            {
                Console.WriteLine("FAILED: Task cancellation failed");
                Console.WriteLine(await cancelResponse.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            client.Dispose();
        }
    }
}