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

namespace MergeScripts.OrgChart
{
    /// <summary>
    /// Build org charts from Merge Groups by parent_group, per Group.type
    /// Requires:
    ///  - MERGE_API_KEY (server-side API key)
    ///  - MERGE_ACCOUNT_TOKEN (linked account token)
    /// Optional:
    ///  - region (US/EU/APAC)
    ///  - types (filter by specific group types)
    ///
    /// Group types: TEAM, DEPARTMENT, COST_CENTER, BUSINESS_UNIT, GROUP
    /// Creates separate hierarchies for each type.
    /// </summary>
    public class OrgChart
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public class Group
        {
            [JsonPropertyName("id")]
            public required string Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("parent_group")]
            public string? ParentGroup { get; set; }
        }

        public class OrgGroup
        {
            public required string Id { get; set; }
            public string? Name { get; set; }
            public string? Type { get; set; }
            public List<OrgGroup> Children { get; set; } = new List<OrgGroup>();
        }

        /// <summary>
        /// Build org charts from Merge Groups API
        /// </summary>
        public static async Task<Dictionary<string, List<OrgGroup>>> BuildOrgCharts(
            string region = "US",
            List<string>? types = null)
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

            var baseUrl = $"{hosts[region]}/api/hris/v1/groups";

            var mergeApiKey = Environment.GetEnvironmentVariable("MERGE_API_KEY") ?? "";
            var mergeAccountToken = Environment.GetEnvironmentVariable("MERGE_ACCOUNT_TOKEN") ?? "";

            // Fetch all groups using pagination
            var groups = await FetchAllGroups(baseUrl, mergeApiKey, mergeAccountToken);

            // Filter by types if specified
            var filteredGroups = types != null && types.Count > 0
                ? groups.Where(g => g.Type != null && types.Contains(g.Type)).ToList()
                : groups;

            // Build org charts by type
            var chartsByType = BuildOrgChartsByType(filteredGroups);

            return chartsByType;
        }

        /// <summary>
        /// Fetch all groups from the Merge API using cursor-based pagination
        /// </summary>
        private static async Task<List<Group>> FetchAllGroups(
            string baseUrl,
            string mergeApiKey,
            string mergeAccountToken)
        {
            var allGroups = new List<Group>();
            string? cursor = null;

            var queryParams = new Dictionary<string, string>
            {
                { "page_size", "100" }
            };

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
                        var group = JsonSerializer.Deserialize<Group>(item.GetRawText());
                        if (group != null)
                        {
                            allGroups.Add(group);
                        }
                    }
                }

                cursor = root.TryGetProperty("next", out var nextProp) && nextProp.ValueKind == JsonValueKind.String
                    ? nextProp.GetString()
                    : null;

            } while (!string.IsNullOrEmpty(cursor));

            return allGroups;
        }

        /// <summary>
        /// Render org chart group as ASCII tree
        /// </summary>
        private static string RenderOrgChartGroup(OrgGroup group, string prefix = "", bool isLast = true)
        {
            var output = new StringBuilder();

            // Current group
            var connector = isLast ? "└── " : "├── ";
            var groupName = group.Name ?? "(Unnamed)";
            output.AppendLine($"{prefix}{connector}{groupName} [{group.Id}]");

            // Children
            var childPrefix = prefix + (isLast ? "    " : "│   ");
            var children = group.Children;

            for (int i = 0; i < children.Count; i++)
            {
                var isLastChild = i == children.Count - 1;
                output.Append(RenderOrgChartGroup(children[i], childPrefix, isLastChild));
            }

            return output.ToString();
        }

        /// <summary>
        /// Render multiple org chart trees as ASCII
        /// </summary>
        private static string RenderOrgChartForest(List<OrgGroup> groups, string typeName)
        {
            if (groups.Count == 0)
            {
                return $"{typeName}: (No groups)\n";
            }

            var output = new StringBuilder();
            output.AppendLine($"{typeName}:");

            for (int i = 0; i < groups.Count; i++)
            {
                if (i > 0) output.AppendLine(); // Separate multiple trees
                output.Append(RenderOrgChartGroup(groups[i], "", true));
            }

            return output.ToString();
        }

        /// <summary>
        /// Count total groups in org chart
        /// </summary>
        private static int CountGroups(List<OrgGroup> groups)
        {
            int count = 0;

            void CountGroup(OrgGroup group)
            {
                count++;
                foreach (var child in group.Children)
                {
                    CountGroup(child);
                }
            }

            foreach (var group in groups)
            {
                CountGroup(group);
            }

            return count;
        }

        /// <summary>
        /// Build org charts keyed by type
        /// </summary>
        private static Dictionary<string, List<OrgGroup>> BuildOrgChartsByType(List<Group> groups)
        {
            // Prepare indexes
            var byId = new Dictionary<string, Group>();
            var childrenIndex = new Dictionary<string, List<string>>(); // parentId -> childIds
            var typeBuckets = new Dictionary<string, HashSet<string>>(); // type -> groupIds

            foreach (var g in groups)
            {
                byId[g.Id] = g;
                var t = g.Type ?? "UNKNOWN";
                if (!typeBuckets.ContainsKey(t))
                {
                    typeBuckets[t] = new HashSet<string>();
                }
                typeBuckets[t].Add(g.Id);

                // Create child relations via parent_group
                var parent = g.ParentGroup;
                if (!string.IsNullOrEmpty(parent))
                {
                    if (!childrenIndex.ContainsKey(parent))
                    {
                        childrenIndex[parent] = new List<string>();
                    }
                    childrenIndex[parent].Add(g.Id);
                }
            }

            // Helper to build a group recursively
            OrgGroup ToGroup(string id)
            {
                var g = byId[id];
                var kids = childrenIndex.ContainsKey(id)
                    ? childrenIndex[id].Select(ToGroup).ToList()
                    : new List<OrgGroup>();

                return new OrgGroup
                {
                    Id = g.Id,
                    Name = g.Name,
                    Type = g.Type,
                    Children = kids
                };
            }

            // For each type, identify roots and build trees
            var charts = new Dictionary<string, List<OrgGroup>>();

            foreach (var (type, ids) in typeBuckets)
            {
                // A root is any group of this type whose parent_group either:
                // - is null/empty, or
                // - references a parent of a different type
                var roots = new List<string>();

                foreach (var id in ids)
                {
                    var g = byId[id];
                    var parent = g.ParentGroup;

                    if (string.IsNullOrEmpty(parent))
                    {
                        // No parent, this is a root
                        roots.Add(id);
                    }
                    else
                    {
                        var parentType = byId.ContainsKey(parent) ? (byId[parent].Type ?? "UNKNOWN") : "UNKNOWN";
                        if (parentType != type)
                        {
                            // Parent is of different type, treat as root for this type
                            roots.Add(id);
                        }
                    }
                }

                charts[type] = roots.Select(ToGroup).ToList();
            }

            return charts;
        }

        /// <summary>
        /// Save ASCII tree visualization to text file
        /// </summary>
        private static void SaveOrgChartsToTextFile(
            Dictionary<string, List<OrgGroup>> chartsByType,
            string filename = "org_charts.txt")
        {
            // Save to the script directory (org folder)
            var scriptDir = Path.GetDirectoryName(typeof(OrgChart).Assembly.Location);
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
            output.AppendLine("ORG CHARTS - ASCII TREE VIEW");
            output.AppendLine(new string('=', 80));
            output.AppendLine();

            // Render each type as ASCII tree
            foreach (var (type, charts) in chartsByType)
            {
                output.Append(RenderOrgChartForest(charts, type));
                output.AppendLine();
            }

            // Add summary
            output.AppendLine(new string('=', 80));
            output.AppendLine("SUMMARY");
            output.AppendLine(new string('=', 80));
            foreach (var (type, charts) in chartsByType)
            {
                var totalGroups = CountGroups(charts);
                output.AppendLine($"  {type}: {charts.Count} root group(s), {totalGroups} total group(s)");
            }
            output.AppendLine(new string('=', 80));

            // Write to file
            File.WriteAllText(outputPath, output.ToString());
            Console.WriteLine($"Org charts (ASCII) saved to: {outputPath}");
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

            Console.WriteLine("Fetching org chart data from Merge API...\n");

            var chartsByType = await BuildOrgCharts(
                region: "US"
                // Optional: filter by specific types
                // types: new List<string> { "TEAM", "DEPARTMENT", "COST_CENTER", "BUSINESS_UNIT", "GROUP" }
            );

            Console.WriteLine(new string('=', 80));
            Console.WriteLine("ORG CHARTS - ASCII TREE VIEW");
            Console.WriteLine(new string('=', 80) + "\n");

            // Render each type as ASCII tree
            foreach (var (type, charts) in chartsByType)
            {
                Console.WriteLine(RenderOrgChartForest(charts, type));
            }

            Console.WriteLine(new string('=', 80));
            Console.WriteLine("SUMMARY");
            Console.WriteLine(new string('=', 80));
            foreach (var (type, charts) in chartsByType)
            {
                var totalGroups = CountGroups(charts);
                Console.WriteLine($"  {type}: {charts.Count} root group(s), {totalGroups} total group(s)");
            }
            Console.WriteLine(new string('=', 80));

            SaveOrgChartsToTextFile(chartsByType, "org_charts.txt");
        }
    }
}
