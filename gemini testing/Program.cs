using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JsonException = Newtonsoft.Json.JsonException;

namespace bs2API
{
    // --- DTO Classes for JSON Serialization/Deserialization ---

    public class UserCredentials
    {
        [JsonProperty("login_id")]
        public string LoginId { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }
    }

    public class LoginRequest
    {
        [JsonProperty("User")]
        public UserCredentials User { get; set; }
    }

    // Example DTOs for the /api/users response (adjust based on actual API response)
    public class User
    {
        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
        // Add other relevant user properties based on the API documentation
    }

    public class UserCollection
    {
        [JsonProperty("rows")]
        public List<User> Rows { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }

    public class UserCollectionResponse
    {
        [JsonProperty("UserCollection")]
        public UserCollection Collection { get; set; }
    }


    // --- API Client Class ---

    public class BioStar2ApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private string _sessionId; // Store the session ID internally

        // --- Configuration ---
        // Best practice: Load this from appsettings.json or another config source
        private const string DefaultBioStarServerUrl = "https://127.0.0.1"; // Use HTTPS

        public BioStar2ApiClient(string baseUrl = DefaultBioStarServerUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/'); // Ensure no trailing slash

            // --- HttpClient Setup ---
            // IMPORTANT: Handle potential self-signed certificates if needed.
            // For production, ensure proper certificate validation.
            // For testing/dev with self-signed certs, you *might* need this (use with caution!):
            /*
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _httpClient = new HttpClient(handler);
            */

            // Standard HttpClient (assumes valid certificate)
            _httpClient = new HttpClient();

            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Logs into the BioStar 2 API and stores the session ID.
        /// </summary>
        /// <param name="loginId">User's Login ID</param>
        /// <param name="password">User's Password</param>
        /// <returns>True if login successful, otherwise false.</returns>
        public async Task<bool> LoginAsync(string loginId, string password)
        {
            var loginRequest = new LoginRequest
            {
                User = new UserCredentials { LoginId = loginId, Password = password }
            };

            string jsonPayload = JsonConvert.SerializeObject(loginRequest);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                // Clear previous session ID before attempting login
                _sessionId = null;
                _httpClient.DefaultRequestHeaders.Remove("bs-session-id");

                var response = await _httpClient.PostAsync("/api/login", content);

                if (response.IsSuccessStatusCode)
                {
                    // Securely store the session ID from the response header
                    if (response.Headers.TryGetValues("bs-session-id", out var sessionIds))
                    {
                        _sessionId = sessionIds.FirstOrDefault();
                        if (!string.IsNullOrEmpty(_sessionId))
                        {
                            // Add session ID to default headers for subsequent requests
                            _httpClient.DefaultRequestHeaders.Add("bs-session-id", _sessionId);
                            Console.WriteLine("Login successful.");
                            return true;
                        }
                    }
                    // Handle case where header is missing even on success
                    Console.Error.WriteLine("Login succeeded but bs-session-id header was missing.");
                    return false;
                }
                else
                {
                    // Log detailed error information
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.Error.WriteLine($"Login failed. Status Code: {response.StatusCode}. Response: {errorContent}");
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                // Network or server connection errors
                Console.Error.WriteLine($"Login network error: {ex.Message}");
                // Log inner exception if needed: ex.InnerException?.Message
                return false;
            }
            catch (Exception ex) // Catch other potential exceptions
            {
                Console.Error.WriteLine($"An unexpected error occurred during login: {ex.Message}");
                return false;
            }
            finally
            {
                // IMPORTANT: Clear the password from memory as soon as possible
                // (While strings are immutable, this reduces the window it exists)
                loginRequest.User.Password = null;
                password = null; // If the original variable is still in scope
                // Consider using SecureString for more robust password handling if needed
            }
        }

        /// <summary>
        /// Example API Call: Gets users from a specific group.
        /// </summary>
        /// <param name="groupId">The ID of the user group.</param>
        /// <param name="limit">Maximum number of users to retrieve.</param>
        /// <param name="offset">Offset for pagination.</param>
        /// <returns>A UserCollectionResponse containing the list of users, or null on failure.</returns>
        public async Task<UserCollectionResponse> GetUsersAsync(int groupId = 1, int limit = 0, int offset = 0)
        {
            // Ensure user is logged in (session ID exists)
            if (string.IsNullOrEmpty(_sessionId))
            {
                Console.Error.WriteLine("Error: Not logged in. Please login first.");
                return null;
            }

            // Construct the request URI
            string requestUri = $"/api/users?group_id={groupId}&limit={limit}&offset={offset}&order_by=user_id:false&last_modified=0";

            try
            {
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    // Deserialize the JSON response into our DTO
                    var usersResponse = JsonConvert.DeserializeObject<UserCollectionResponse>(jsonResponse);
                    return usersResponse;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.Error.WriteLine($"Failed to get users. Status Code: {response.StatusCode}. Response: {errorContent}");
                    // Check for specific status codes (e.g., 401 Unauthorized might mean session expired)
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Console.Error.WriteLine("Session might have expired. Please login again.");
                        _sessionId = null; // Clear potentially invalid session ID
                        _httpClient.DefaultRequestHeaders.Remove("bs-session-id");
                    }
                    return null;
                }
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"GetUsers network error: {ex.Message}");
                return null;
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Error deserializing user data: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An unexpected error occurred while getting users: {ex.Message}");
                return null;
            }
        }

        // --- Add other API call methods here ---
        // Example:
        // public async Task<Door> GetDoorAsync(int doorId) { ... }
        // public async Task<bool> UpdateUserAsync(User user) { ... }

        // --- Dispose Pattern ---
        public void Dispose()
        {
            // Note: It's generally recommended *not* to dispose HttpClient
            // if it's intended to be reused across the application lifetime (e.g., singleton).
            // If this ApiClient has a short lifetime, disposing HttpClient might be okay,
            // but be aware of potential socket exhaustion issues if creating/disposing many HttpClients rapidly.
            // _httpClient?.Dispose(); // Uncomment ONLY if you are sure about HttpClient lifecycle management.
            GC.SuppressFinalize(this);
        }
    }


    // --- Main Program ---

    class Program
    {
        static async Task Main(string[] args)
        {
            // Use using statement for automatic disposal if ApiClient lifetime is limited to Main
            using (var apiClient = new BioStar2ApiClient()) // Optional: Pass custom URL if needed
            {
                // --- Securely Get Credentials ---
                Console.Write("Enter BioStar 2 Login ID: ");
                string loginId = Console.ReadLine();

                Console.Write("Enter BioStar 2 Password: ");
                string password = ReadPassword(); // Securely read password
                Console.WriteLine(); // New line after password input

                // --- Login ---
                bool loginSuccess = await apiClient.LoginAsync(loginId, password);

                // IMPORTANT: Clear password from memory immediately after use
                password = null;
                GC.Collect(); // Suggest garbage collection (though not guaranteed immediate removal)


                if (!loginSuccess)
                {
                    Console.WriteLine("Login failed. Exiting.");
                    Console.ReadKey(); // Keep console open
                    return;
                }

                // --- Example: Call Get Users API ---
                Console.WriteLine("\nAttempting to get users...");
                var usersResponse = await apiClient.GetUsersAsync(groupId: 1, limit: 10); // Get first 10 users from group 1

                if (usersResponse != null && usersResponse.Collection?.Rows != null)
                {
                    Console.WriteLine($"Successfully retrieved {usersResponse.Collection.Rows.Count} users (Total in group: {usersResponse.Collection.Total}):");
                    foreach (var user in usersResponse.Collection.Rows)
                    {
                        Console.WriteLine($"- ID: {user.UserId}, Name: {user.Name}");
                    }
                }
                else
                {
                    Console.WriteLine("Failed to retrieve users.");
                }

                // --- Add more API calls based on user input or program logic ---
                // Example structure for interactive console:
                /*
                while(true)
                {
                    Console.WriteLine("\nEnter command (e.g., 'getusers', 'exit'):");
                    string command = Console.ReadLine()?.ToLower();

                    switch(command)
                    {
                        case "getusers":
                            // Call GetUsersAsync again, maybe with different parameters
                            break;
                        case "exit":
                            return;
                        default:
                            Console.WriteLine("Unknown command.");
                            break;
                    }
                }
                */

                Console.WriteLine("\nPress any key to exit.");
                Console.ReadKey();
            } // ApiClient will be disposed here if using 'using'
        }

        /// <summary>
        /// Reads password securely from the console without echoing characters.
        /// </summary>
        public static string ReadPassword()
        {
            var password = new StringBuilder();
            ConsoleKeyInfo key;

            do
            {
                key = Console.ReadKey(true); // True hides the character

                // Ignore special keys like Enter, Backspace, etc. initially
                if (!char.IsControl(key.KeyChar))
                {
                    password.Append(key.KeyChar);
                    Console.Write("*"); // Display asterisk for feedback
                }
                else
                {
                    // Handle Backspace
                    if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                    {
                        password.Remove(password.Length - 1, 1);
                        // Move cursor back, write space, move back again
                        Console.Write("\b \b");
                    }
                }
                // Stop reading when Enter key is pressed
            } while (key.Key != ConsoleKey.Enter);

            return password.ToString();
        }
    }
}