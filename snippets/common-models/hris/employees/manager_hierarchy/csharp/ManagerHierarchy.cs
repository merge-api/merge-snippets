using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MergeScripts.ManagerHierarchy
{
    /// <summary>
    /// Build manager org chart hierarchy from Merge Employees by manager field
    /// Requires:
    ///  - MERGE_API_KEY (server-side API key)
    ///  - MERGE_ACCOUNT_TOKEN (linked account token)
    /// Optional:
    ///  - region (US/EU/APAC)
    ///  - employment_status (filter by ACTIVE, INACTIVE, PENDING, or ALL)
    ///
    /// Employee statuses: ACTIVE, INACTIVE, PENDING, ALL
    /// Creates hierarchical org chart based on Employee.manager relationships.
    /// </summary>
    public class ManagerHierarchy
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public class Employee
        {
            [JsonPropertyName("id")]
            public required string Id { get; set; }

            [JsonPropertyName("employee_number")]
            public string? EmployeeNumber { get; set; }

            [JsonPropertyName("first_name")]
            public string? FirstName { get; set; }

            [JsonPropertyName("last_name")]
            public string? LastName { get; set; }

            [JsonPropertyName("display_full_name")]
            public string? DisplayFullName { get; set; }

            [JsonPropertyName("work_email")]
            public string? WorkEmail { get; set; }

            [JsonPropertyName("manager")]
            public string? Manager { get; set; }

            [JsonPropertyName("employment_status")]
            public string? EmploymentStatus { get; set; }

            [JsonPropertyName("job_title")]
            public string? JobTitle { get; set; }
        }

        public class OrgEmployee
        {
            public required string Id { get; set; }
            public string? EmployeeNumber { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? DisplayFullName { get; set; }
            public string? WorkEmail { get; set; }
            public string? JobTitle { get; set; }
            public string? EmploymentStatus { get; set; }
            public bool IsManager { get; set; }
            public List<OrgEmployee> DirectReports { get; set; } = new List<OrgEmployee>();
        }

        /// <summary>
        /// Build manager hierarchy from Merge Employees API
        /// </summary>
        public static async Task<List<OrgEmployee>> BuildManagerHierarchy(
            string region = "US",
            string employmentStatus = "ACTIVE")
        {
            var hosts = new Dictionary<string, string>
            {
                { "US", "https://api.merge.dev" },
                { "EU", "https://api.eu.merge.dev" },
                { "APAC", "https://api.apac.merge.dev" }
            };

            if (!hosts.ContainsKey(region))
            {
                throw new ArgumentException($"Invalid region: {region}. Must be one of: US, EU, APAC");
            }

            var baseUrl = $"{hosts[region]}/api/hris/v1/employees";

            var mergeApiKey = Environment.GetEnvironmentVariable("MERGE_API_KEY") ?? "";
            var mergeAccountToken = Environment.GetEnvironmentVariable("MERGE_ACCOUNT_TOKEN") ?? "";

            // Fetch all employees using pagination
            var employees = await FetchAllEmployees(baseUrl, mergeApiKey, mergeAccountToken, employmentStatus);

            // Build manager hierarchy
            var hierarchy = BuildHierarchy(employees);

            return hierarchy;
        }

        /// <summary>
        /// Fetch all employees from the Merge API using cursor-based pagination
        /// </summary>
        private static async Task<List<Employee>> FetchAllEmployees(
            string baseUrl,
            string mergeApiKey,
            string mergeAccountToken,
            string employmentStatus)
        {
            var allEmployees = new List<Employee>();
            string? cursor = null;

            var queryParams = new Dictionary<string, string>
            {
                { "page_size", "100" }
            };

            if (employmentStatus != "ALL")
            {
                queryParams["employment_status"] = employmentStatus;
            }

            do
            {
                var query = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                var url = $"{baseUrl}?{query}";
                if (!string.IsNullOrEmpty(cursor))
                {
                    url += $"&cursor={Uri.EscapeDataString(cursor)}";
                }

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mergeApiKey);
                request.Headers.Add("X-Account-Token", mergeAccountToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Merge API error {response.StatusCode}: {errorText}");
                }

                var responseText = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;

                if (root.TryGetProperty("results", out var results))
                {
                    foreach (var item in results.EnumerateArray())
                    {
                        var employee = JsonSerializer.Deserialize<Employee>(item.GetRawText());
                        if (employee != null)
                        {
                            allEmployees.Add(employee);
                        }
                    }
                }

                cursor = root.TryGetProperty("next", out var nextProp) && nextProp.ValueKind == JsonValueKind.String
                    ? nextProp.GetString()
                    : null;

            } while (!string.IsNullOrEmpty(cursor));

            return allEmployees;
        }

        /// <summary>
        /// Render org chart employee as ASCII tree
        /// </summary>
        private static string RenderOrgChartEmployee(OrgEmployee employee, string prefix = "", bool isLast = true)
        {
            var output = new StringBuilder();

            // Current employee
            var connector = isLast ? "└── " : "├── ";
            var displayName = employee.DisplayFullName ?? $"{employee.FirstName} {employee.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = employee.WorkEmail ?? employee.EmployeeNumber ?? "(Unnamed)";
            }

            var jobTitle = !string.IsNullOrWhiteSpace(employee.JobTitle) ? $" - {employee.JobTitle}" : "";
            var managerTag = employee.IsManager ? " [Manager]" : "";
            output.AppendLine($"{prefix}{connector}{displayName}{jobTitle}{managerTag} [{employee.Id}]");

            // Direct reports
            var childPrefix = prefix + (isLast ? "    " : "│   ");
            var directReports = employee.DirectReports;

            for (int i = 0; i < directReports.Count; i++)
            {
                var isLastChild = i == directReports.Count - 1;
                output.Append(RenderOrgChartEmployee(directReports[i], childPrefix, isLastChild));
            }

            return output.ToString();
        }

        /// <summary>
        /// Render multiple org chart trees as ASCII
        /// </summary>
        private static string RenderOrgChartForest(List<OrgEmployee> employees)
        {
            if (employees.Count == 0)
            {
                return "(No employees)\n";
            }

            var output = new StringBuilder();

            for (int i = 0; i < employees.Count; i++)
            {
                if (i > 0) output.AppendLine(); // Separate multiple trees
                output.Append(RenderOrgChartEmployee(employees[i], "", true));
            }

            return output.ToString();
        }

        /// <summary>
        /// Count total employees in hierarchy
        /// </summary>
        private static int CountEmployees(List<OrgEmployee> employees)
        {
            int count = 0;

            void CountEmployee(OrgEmployee employee)
            {
                count++;
                foreach (var directReport in employee.DirectReports)
                {
                    CountEmployee(directReport);
                }
            }

            foreach (var employee in employees)
            {
                CountEmployee(employee);
            }

            return count;
        }

        /// <summary>
        /// Build manager hierarchy from employees
        /// </summary>
        private static List<OrgEmployee> BuildHierarchy(List<Employee> employees)
        {
            // Prepare indexes
            var byId = new Dictionary<string, Employee>();
            var childrenIndex = new Dictionary<string, List<string>>(); // managerId -> employeeIds

            foreach (var e in employees)
            {
                byId[e.Id] = e;

                // Create child relations via manager
                var managerId = e.Manager;
                if (!string.IsNullOrEmpty(managerId))
                {
                    if (!childrenIndex.ContainsKey(managerId))
                    {
                        childrenIndex[managerId] = new List<string>();
                    }
                    childrenIndex[managerId].Add(e.Id);
                }
            }

            // Helper to build an employee recursively
            OrgEmployee ToOrgEmployee(string id)
            {
                var e = byId[id];
                var directReports = childrenIndex.ContainsKey(id)
                    ? childrenIndex[id].Select(ToOrgEmployee).ToList()
                    : new List<OrgEmployee>();

                return new OrgEmployee
                {
                    Id = e.Id,
                    EmployeeNumber = e.EmployeeNumber,
                    FirstName = e.FirstName,
                    LastName = e.LastName,
                    DisplayFullName = e.DisplayFullName,
                    WorkEmail = e.WorkEmail,
                    JobTitle = e.JobTitle,
                    EmploymentStatus = e.EmploymentStatus,
                    IsManager = directReports.Count > 0,
                    DirectReports = directReports
                };
            }

            // Identify roots: employees with no manager or manager not in dataset
            var roots = new List<string>();

            foreach (var e in employees)
            {
                var managerId = e.Manager;

                if (string.IsNullOrEmpty(managerId))
                {
                    // No manager, this is a root
                    roots.Add(e.Id);
                }
                else if (!byId.ContainsKey(managerId))
                {
                    // Manager not in dataset, treat as root
                    roots.Add(e.Id);
                }
            }

            return roots.Select(ToOrgEmployee).ToList();
        }

        /// <summary>
        /// Save ASCII tree visualization to text file
        /// </summary>
        private static void SaveHierarchyToTextFile(
            List<OrgEmployee> hierarchy,
            string filename = "manager_hierarchy.txt")
        {
            // Save to the script directory (manager_hierarchy folder)
            var scriptDir = Path.GetDirectoryName(typeof(ManagerHierarchy).Assembly.Location);
            if (scriptDir != null)
            {
                // Navigate up from bin/Debug/net10.0 to the script directory
                scriptDir = Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."));
            }
            else
            {
                scriptDir = Directory.GetCurrentDirectory();
            }

            var outputPath = Path.Combine(scriptDir, filename);

            var output = new StringBuilder();

            output.AppendLine(new string('=', 80));
            output.AppendLine("MANAGER HIERARCHY - ASCII TREE VIEW");
            output.AppendLine(new string('=', 80));
            output.AppendLine();

            // Render ASCII tree
            output.Append(RenderOrgChartForest(hierarchy));

            // Add summary
            output.AppendLine();
            output.AppendLine(new string('=', 80));
            output.AppendLine("SUMMARY");
            output.AppendLine(new string('=', 80));
            var totalEmployees = CountEmployees(hierarchy);
            output.AppendLine($"  Root employees (top-level managers): {hierarchy.Count}");
            output.AppendLine($"  Total employees: {totalEmployees}");
            output.AppendLine(new string('=', 80));

            // Write to file
            File.WriteAllText(outputPath, output.ToString());
            Console.WriteLine($"Manager hierarchy (ASCII) saved to: {outputPath}");
        }

        private static void LoadEnvFile(string path)
        {
            var lines = File.ReadAllLines(path);
            foreach (var line in lines)
            {
                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                // Remove quotes (both single and double, including triple quotes)
                if (value.StartsWith("\"\"\"") && value.EndsWith("\"\"\"") && value.Length >= 6)
                {
                    value = value.Substring(3, value.Length - 6);
                }
                else if (value == "\"\"\"")
                {
                    value = "";
                }
                else if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                {
                    value = value.Substring(1, value.Length - 2);
                }
                else if (value == "\"\"")
                {
                    value = "";
                }
                else if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 2)
                {
                    value = value.Substring(1, value.Length - 2);
                }
                else if (value == "''")
                {
                    value = "";
                }

                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public static async Task Main(string[] args)
        {
            // Load environment variables from .env file
            var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (!File.Exists(envPath))
            {
                // Try parent directories
                var currentDir = Directory.GetCurrentDirectory();
                while (currentDir != null && !File.Exists(Path.Combine(currentDir, ".env")))
                {
                    var parent = Directory.GetParent(currentDir);
                    currentDir = parent?.FullName;
                }
                if (currentDir != null)
                {
                    envPath = Path.Combine(currentDir, ".env");
                }
            }

            // Try to load .env file
            try
            {
                if (File.Exists(envPath))
                {
                    LoadEnvFile(envPath);
                }
                else
                {
                    Console.WriteLine($"Warning: .env file not found at {envPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not parse .env file: {ex.Message}");
            }

            // Verify environment variables are set
            var apiKey = Environment.GetEnvironmentVariable("MERGE_API_KEY");
            var accountToken = Environment.GetEnvironmentVariable("MERGE_ACCOUNT_TOKEN");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(accountToken))
            {
                Console.WriteLine("ERROR: MERGE_API_KEY or MERGE_ACCOUNT_TOKEN not set!");
                Console.WriteLine($"MERGE_API_KEY: {(string.IsNullOrEmpty(apiKey) ? "(empty)" : "***set***")}");
                Console.WriteLine($"MERGE_ACCOUNT_TOKEN: {(string.IsNullOrEmpty(accountToken) ? "(empty)" : "***set***")}");
                return;
            }

            Console.WriteLine("Fetching employee data from Merge API...\n");

            // var hierarchy = await BuildManagerHierarchy(region: "US");
            var hierarchy = await BuildManagerHierarchy(employmentStatus: "ALL");

            Console.WriteLine(new string('=', 80));
            Console.WriteLine("MANAGER HIERARCHY - ASCII TREE VIEW");
            Console.WriteLine(new string('=', 80) + "\n");

            // Render ASCII tree
            Console.WriteLine(RenderOrgChartForest(hierarchy));

            Console.WriteLine(new string('=', 80));
            Console.WriteLine("SUMMARY");
            Console.WriteLine(new string('=', 80));
            var totalEmployees = CountEmployees(hierarchy);
            Console.WriteLine($"  Root employees (top-level managers): {hierarchy.Count}");
            Console.WriteLine($"  Total employees: {totalEmployees}");
            Console.WriteLine(new string('=', 80));

            SaveHierarchyToTextFile(hierarchy, "manager_hierarchy.txt");
        }
    }
}
