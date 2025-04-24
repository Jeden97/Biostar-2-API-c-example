using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO; // Required for File operations if using GetBase64Image
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace bs2API
{
    // --- DTO Classes for JSON Serialization/Deserialization ---

    // --- Login DTOs ---
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

    // --- Get Users (/api/users GET) Response DTOs ---
    public class User // Represents a single user in the GET response list
    {
        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
        // Add other relevant user properties based on the API documentation for GET /api/users
    }

    public class UserCollection // Represents the collection part of the GET response
    {
        [JsonProperty("rows")]
        public List<User> Rows { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }

    public class UserCollectionResponse // Represents the full GET /api/users response
    {
        [JsonProperty("UserCollection")]
        public UserCollection Collection { get; set; }
    }

    // --- Create User (/api/users POST) Request DTOs ---
    // These DTOs are needed for your CreateUserAsync implementation

    // Represents the nested {"id": ...} structure with a numeric ID
    public class ID
    {
        [JsonProperty("id")]
        public int Id { get; set; }
    }

    // Represents the main user data payload for the POST request
    public class UserProperties
    {
        [JsonProperty("user_id", NullValueHandling = NullValueHandling.Ignore)]
        public string UserId { get; set; }

        [JsonProperty("user_group_id")]
        public ID UserGroupId { get; set; }

        [JsonProperty("start_datetime")]
        public DateTimeOffset StartDateTime { get; set; }

        [JsonProperty("expiry_datetime")]
        public DateTimeOffset ExpiryDateTime { get; set; }

        [JsonProperty("disabled")]
        public bool Disabled { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("department", NullValueHandling = NullValueHandling.Ignore)]
        public string Department { get; set; }

        [JsonProperty("user_title", NullValueHandling = NullValueHandling.Ignore)]
        public string UserTitle { get; set; }

        [JsonProperty("photo", NullValueHandling = NullValueHandling.Ignore)]
        public string Photo { get; set; } // Assign Base64 string here

        [JsonProperty("phone", NullValueHandling = NullValueHandling.Ignore)]
        public string Phone { get; set; }

        [JsonProperty("permission")]
        public ID Permission { get; set; }

        [JsonProperty("access_groups")]
        public List<ID> AccessGroups { get; set; }

        [JsonProperty("login_id", NullValueHandling = NullValueHandling.Ignore)]
        public string LoginId { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }

        [JsonProperty("user_ip", NullValueHandling = NullValueHandling.Ignore)]
        public string UserIp { get; set; }
    }

    // Represents the top-level request structure for creating a user: { "User": { ... } }
    public class NewUserObject
    {
        [JsonProperty("User")]
        public UserProperties User { get; set; }
    }

    // --- API Client Class ---

    public class BioStar2ApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private string _sessionId;

        private const string DefaultBioStarServerUrl = "https://127.0.0.1";

        public BioStar2ApiClient(string baseUrl = DefaultBioStarServerUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');

            // Standard HttpClient (Add handler for self-signed certs if needed)
            /*
           var handler = new HttpClientHandler
           {
               ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
           };
           _httpClient = new HttpClient(handler);
           */
            _httpClient = new HttpClient();

            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private string ReadPassword()
        {
            var password = new StringBuilder();
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey(true);
                if (!char.IsControl(key.KeyChar))
                {
                    password.Append(key.KeyChar);
                    Console.Write("*");
                }
                else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }
            } while (key.Key != ConsoleKey.Enter);
            Console.WriteLine();
            return password.ToString();
        }

        public async Task<bool> LoginAsync()
        {
            Console.Write("Enter BioStar 2 Login ID: ");
            string loginId = Console.ReadLine();
            Console.Write("Enter BioStar 2 Password: ");
            string password = ReadPassword();

            var loginRequest = new LoginRequest { User = new UserCredentials { LoginId = loginId, Password = password } };
            string jsonPayload = JsonConvert.SerializeObject(loginRequest);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                _sessionId = null;
                _httpClient.DefaultRequestHeaders.Remove("bs-session-id");
                var response = await _httpClient.PostAsync("/api/login", content);

                if (response.IsSuccessStatusCode)
                {
                    if (response.Headers.TryGetValues("bs-session-id", out var sessionIds))
                    {
                        _sessionId = sessionIds.FirstOrDefault();
                        if (!string.IsNullOrEmpty(_sessionId))
                        {
                            _httpClient.DefaultRequestHeaders.Add("bs-session-id", _sessionId);
                            Console.WriteLine("Login successful.");
                            return true;
                        }
                    }
                    Console.Error.WriteLine("Login succeeded but bs-session-id header was missing.");
                    return false;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.Error.WriteLine($"Login failed. Status Code: {response.StatusCode}. Response: {errorContent}");
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Login network error: {ex.Message} (Check Base URL and connectivity)");
                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An unexpected error occurred during login: {ex.Message}");
                return false;
            }
            finally
            {
                loginRequest.User.Password = null;
                password = null;
            }
        }

        public async Task<UserCollectionResponse> GetUsersAsync(int groupId = 1, int limit = 10, int offset = 0)
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                Console.Error.WriteLine("Error: Not logged in. Please login first.");
                return null;
            }
            string requestUri = $"/api/users?group_id={groupId}&limit={limit}&offset={offset}&order_by=user_id:false&last_modified=0";

            try
            {
                var response = await _httpClient.GetAsync(requestUri);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    try
                    {
                        return JsonConvert.DeserializeObject<UserCollectionResponse>(jsonResponse);
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.Error.WriteLine($"Error deserializing GET users response: {jsonEx.Message}");
                        Console.Error.WriteLine($"Received JSON: {jsonResponse}");
                        return null;
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.Error.WriteLine($"Failed to get users. Status Code: {response.StatusCode}. Response: {errorContent}");
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Console.Error.WriteLine("Session might have expired. Please login again.");
                        _sessionId = null;
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
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An unexpected error occurred while getting users: {ex.Message}");
                return null;
            }
        }


        public async Task<NewUserObject> CreatUserAsync()
        {
            

            if (string.IsNullOrEmpty(_sessionId))
            {
                Console.Error.WriteLine("Error: Not logged in. Please login first.");
                return null;
            }
            string requestUri = $"/api/users";

            Console.WriteLine("Enter User ID Not Used Above: ");
            string uID = Console.ReadLine();

            Console.WriteLine("Enter Name: ");
            string name = Console.ReadLine();

            Console.WriteLine("Enter Start Period: ");
            string startDate = Console.ReadLine();
            DateTime convertedStartDate = DateTime.Parse(startDate);

            Console.WriteLine("Enter End Period: ");
            string endDate = Console.ReadLine();
            DateTime convertedEndDate = DateTime.Parse(endDate);

            Console.WriteLine("Enter User Group ID(default all users is 1): ");
            int userID = int.Parse(Console.ReadLine());


            var newUserRequest = new NewUserObject { User = new UserProperties { UserId = uID, Name = name, StartDateTime = convertedStartDate, ExpiryDateTime = convertedEndDate, UserGroupId = new ID { Id = userID } } };
            string jsonPayload = JsonConvert.SerializeObject(newUserRequest);



        }

        public void Dispose()
        {
            // _httpClient?.Dispose(); // Usually leave commented out for HttpClient reuse
            GC.SuppressFinalize(this);
        }
    }


    // --- Main Program ---
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("BioStar 2 API Client Demo");
            Console.WriteLine("=========================");

            using (var apiClient = new BioStar2ApiClient())
            {
                // --- Login ---
                bool loginSuccess = await apiClient.LoginAsync();
                GC.Collect();

                if (!loginSuccess)
                {
                    Console.WriteLine("\nLogin failed. Exiting.");
                    Console.ReadKey();
                    return;
                }

                // --- Example 1: Get Users ---
                Console.WriteLine("\nAttempting to get users (first 10)...");
                var usersResponse = await apiClient.GetUsersAsync(limit: 10);

                if (usersResponse != null && usersResponse.Collection?.Rows != null)
                {
                    Console.WriteLine($"Successfully retrieved {usersResponse.Collection.Rows.Count} users (Total matching query: {usersResponse.Collection.Total}):");
                    foreach (var user in usersResponse.Collection.Rows)
                    {
                        Console.WriteLine($"- ID: {user.UserId}, Name: {user.Name}");
                    }
                }
                else
                {
                    Console.WriteLine("Failed to retrieve users or no users found.");
                }

                

                


                Console.WriteLine("\nPress any key to exit.");
                Console.ReadKey();
            }
        }
    }
}