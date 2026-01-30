using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Merge
{
    /// <summary>
    /// Compute earnings by type and category for current and last fiscal years
    /// Groups earnings by individual earning codes and target categories
    /// Requires:
    ///  - MERGE_API_KEY (server-side)
    ///  - MERGE_ACCOUNT_TOKEN (linked account token)
    ///  - employeeId (Merge UUID for the employee)
    /// Optional:
    ///  - currentFYStart (defaults to Jan 1 of current year)
    ///  - currentFYEnd (defaults to Dec 31 of current year)
    ///  - region (US/EU/APAC)
    /// </summary>
    public class EmployeePayrollRuns
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public class FiscalYearEarnings
        {
            public int Year { get; set; }
            public decimal NetPay { get; set; }
            public decimal TotalGrossEarnings { get; set; }
            public Dictionary<string, EarningDetail> EarningsByType { get; set; } = new Dictionary<string, EarningDetail>();
            public Dictionary<string, decimal> EarningsByCategory { get; set; } = new Dictionary<string, decimal>();
        }

        public class EarningDetail
        {
            public required string EarningCode { get; set; }
            public required string Label { get; set; }
            public decimal Amount { get; set; }
        }

        public class FiscalYearResult
        {
            public required string StartDate { get; set; }
            public required string EndDate { get; set; }
            public int Year { get; set; }
            public decimal NetPay { get; set; }
            public decimal TotalGrossEarnings { get; set; }
            public required Dictionary<string, EarningDetail> EarningsByType { get; set; }
            public required Dictionary<string, decimal> EarningsByCategory { get; set; }
        }

        public class SummaryResult
        {
            public required FiscalYearResult CurrentFY { get; set; }
            public required FiscalYearResult LastFY { get; set; }
        }

        public static async Task<SummaryResult> SummarizeEmployeePayrollRuns(
            string employeeId,
            DateTime? currentFYStart = null,
            DateTime? currentFYEnd = null,
            string region = "US",
            bool useCheckDateFiltering = false)
        {
            var hosts = new Dictionary<string, string>
            {
                { "US", "https://api.merge.dev" },
                { "EU", "https://api.eu.merge.dev" },
                { "APAC", "https://api.apac.merge.dev" }
            };

            var baseUrl = $"{hosts[region]}/api/hris/v1/employee-payroll-runs";

            var mergeApiKey = Environment.GetEnvironmentVariable("MERGE_API_KEY") ?? "";
            var mergeAccountToken = Environment.GetEnvironmentVariable("MERGE_ACCOUNT_TOKEN") ?? "";

            // Default to calendar year
            var today = DateTime.UtcNow;
            var currentYear = today.Year;

            var thisFYStart = currentFYStart ?? new DateTime(currentYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var thisFYEnd = currentFYEnd ?? new DateTime(currentYear, 12, 31, 0, 0, 0, DateTimeKind.Utc);

            // Calculate last fiscal year dates
            var lastFYStart = thisFYStart.AddYears(-1);
            var lastFYEnd = thisFYEnd.AddYears(-1);

            var thisFY = thisFYEnd.Year;
            var lastFY = lastFYEnd.Year;

            // Load earnings mapping
            // var earningsMapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "earnings_workday.json");
            var earningsMapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "earnings_ukg.json");
            if (!File.Exists(earningsMapPath))
            {
                // earningsMapPath = Path.Combine(Path.GetDirectoryName(typeof(EmployeePayrollRuns).Assembly.Location) ?? "", "earnings_workday.json");
                earningsMapPath = Path.Combine(Path.GetDirectoryName(typeof(EmployeePayrollRuns).Assembly.Location) ?? "", "earnings_ukg.json");
            }
            if (!File.Exists(earningsMapPath))
            {
                // earningsMapPath = Path.Combine(Directory.GetCurrentDirectory(), "payroll", "earnings_workday.json");
                earningsMapPath = Path.Combine(Directory.GetCurrentDirectory(), "payroll", "earnings_ukg.json");
            }
            if (!File.Exists(earningsMapPath))
            {
                // earningsMapPath = "earnings_workday.json";
                earningsMapPath = "earnings_ukg.json";
            }

            var earningsMapJson = File.ReadAllText(earningsMapPath);
            var earningsMap = JsonSerializer.Deserialize<Dictionary<string, string>>(earningsMapJson)
                              ?? new Dictionary<string, string>();

            // Load earnings-to-category mapping
            var categoryMapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "earnings_to_customer.json");
            if (!File.Exists(categoryMapPath))
            {
                categoryMapPath = Path.Combine(Path.GetDirectoryName(typeof(EmployeePayrollRuns).Assembly.Location) ?? "", "earnings_to_customer.json");
            }
            if (!File.Exists(categoryMapPath))
            {
                categoryMapPath = Path.Combine(Directory.GetCurrentDirectory(), "payroll", "earnings_to_customer.json");
            }
            if (!File.Exists(categoryMapPath))
            {
                categoryMapPath = "earnings_to_customer.json";
            }

            var categoryMapJson = File.ReadAllText(categoryMapPath);
            var categoryMap = JsonSerializer.Deserialize<Dictionary<string, string>>(categoryMapJson)
                              ?? new Dictionary<string, string>();

            var thisFYTask = FetchPayrollRunsForPeriod(
                baseUrl, employeeId, thisFYStart, thisFYEnd, thisFY,
                mergeApiKey, mergeAccountToken, earningsMap, categoryMap, useCheckDateFiltering);
            var lastFYTask = FetchPayrollRunsForPeriod(
                baseUrl, employeeId, lastFYStart, lastFYEnd, lastFY,
                mergeApiKey, mergeAccountToken, earningsMap, categoryMap, useCheckDateFiltering);

            await Task.WhenAll(thisFYTask, lastFYTask);

            var thisFYData = await thisFYTask;
            var lastFYData = await lastFYTask;

            return new SummaryResult
            {
                CurrentFY = new FiscalYearResult
                {
                    StartDate = thisFYStart.ToString("yyyy-MM-dd"),
                    EndDate = thisFYEnd.ToString("yyyy-MM-dd"),
                    Year = thisFYData.Year,
                    NetPay = thisFYData.NetPay,
                    TotalGrossEarnings = thisFYData.TotalGrossEarnings,
                    EarningsByType = thisFYData.EarningsByType,
                    EarningsByCategory = thisFYData.EarningsByCategory
                },
                LastFY = new FiscalYearResult
                {
                    StartDate = lastFYStart.ToString("yyyy-MM-dd"),
                    EndDate = lastFYEnd.ToString("yyyy-MM-dd"),
                    Year = lastFYData.Year,
                    NetPay = lastFYData.NetPay,
                    TotalGrossEarnings = lastFYData.TotalGrossEarnings,
                    EarningsByType = lastFYData.EarningsByType,
                    EarningsByCategory = lastFYData.EarningsByCategory
                }
            };
        }

        private static async Task<FiscalYearEarnings> FetchPayrollRunsForPeriod(
            string baseUrl,
            string employeeId,
            DateTime startDate,
            DateTime endDate,
            int year,
            string mergeApiKey,
            string mergeAccountToken,
            Dictionary<string, string> earningsMap,
            Dictionary<string, string> categoryMap,
            bool useCheckDateFiltering = false)
        {
            var result = new FiscalYearEarnings
            {
                Year = year,
                NetPay = 0,
                TotalGrossEarnings = 0,
                EarningsByType = new Dictionary<string, EarningDetail>()
            };

            string cursor = null;
            var queryParams = new Dictionary<string, string>
            {
                { "employee_id", employeeId },
                { "expand", "earnings,deductions,taxes" },
                { "page_size", "100" }
            };

            // Add date filters if using check_date filtering
            if (!useCheckDateFiltering)
            {
                queryParams.Add("ended_after", startDate.ToString("yyyy-MM-dd"));
                queryParams.Add("ended_before", endDate.ToString("yyyy-MM-dd"));
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
                    foreach (var run in results.EnumerateArray())
                    {
                        // If using check_date filtering, filter in-memory
                        if (useCheckDateFiltering)
                        {
                            DateTime? checkDate = null;
                            if (run.TryGetProperty("check_date", out var checkDateProp) &&
                                checkDateProp.ValueKind == JsonValueKind.String)
                            {
                                var checkDateStr = checkDateProp.GetString();
                                if (!string.IsNullOrEmpty(checkDateStr) &&
                                    DateTime.TryParse(checkDateStr, out var parsedDate))
                                {
                                    checkDate = parsedDate;
                                }
                            }

                            // Skip if check_date is outside our range
                            if (checkDate == null || checkDate < startDate || checkDate > endDate)
                            {
                                continue;
                            }
                        }

                        // Sum net pay
                        if (run.TryGetProperty("net_pay", out var netPayProp) && netPayProp.ValueKind == JsonValueKind.Number)
                        {
                            result.NetPay += netPayProp.GetDecimal();
                        }

                        // Process earnings
                        if (run.TryGetProperty("earnings", out var earnings) && earnings.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var earning in earnings.EnumerateArray())
                            {
                                var earningCode = "";
                                var amount = 0m;

                                if (earning.TryGetProperty("type", out var typeProp))
                                {
                                    earningCode = typeProp.GetString() ?? "";
                                }

                                if (earning.TryGetProperty("amount", out var amountProp) && amountProp.ValueKind == JsonValueKind.Number)
                                {
                                    amount = amountProp.GetDecimal();
                                }

                                if (amount == 0) continue;

                                // Get human-readable label from earnings map
                                var label = earningsMap.ContainsKey(earningCode) ? earningsMap[earningCode] : earningCode;

                                // Add to total gross earnings
                                result.TotalGrossEarnings += amount;

                                // Group by earning code/type
                                if (!result.EarningsByType.ContainsKey(earningCode))
                                {
                                    result.EarningsByType[earningCode] = new EarningDetail
                                    {
                                        EarningCode = earningCode,
                                        Label = label,
                                        Amount = 0
                                    };
                                }
                                result.EarningsByType[earningCode].Amount += amount;

                                // Group by target category
                                var category = categoryMap.ContainsKey(earningCode)
                                    ? categoryMap[earningCode]
                                    : (categoryMap.ContainsKey("Other Allowances or Earnings") ? categoryMap["Other Allowances or Earnings"] : "Other Allowances");

                                if (!result.EarningsByCategory.ContainsKey(category))
                                {
                                    result.EarningsByCategory[category] = 0;
                                }
                                result.EarningsByCategory[category] += amount;
                            }
                        }
                    }
                }

                cursor = root.TryGetProperty("next", out var nextProp) && nextProp.ValueKind == JsonValueKind.String
                    ? nextProp.GetString()
                    : null;

            } while (!string.IsNullOrEmpty(cursor));

            return result;
        }

        private static void LoadEnvFile(string path)
        {
            var lines = File.ReadAllLines(path);
            int lineNum = 0;
            foreach (var line in lines)
            {
                lineNum++;

                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

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

                if (key == "MERGE_API_KEY" || key == "MERGE_ACCOUNT_TOKEN")
                {
                    Console.WriteLine($"  Line {lineNum}: {key} = {(string.IsNullOrEmpty(value) ? "(empty)" : $"***{value.Length} chars***")}");
                }

                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public static async Task Main(string[] args)
        {
            var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (!File.Exists(envPath))
            {
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

            try
            {
                if (File.Exists(envPath))
                {
                    Console.WriteLine($"Loading .env file from: {envPath}");
                    LoadEnvFile(envPath);
                    Console.WriteLine($"✓ .env file loaded successfully");
                }
                else
                {
                    Console.WriteLine($"Warning: .env file not found at {envPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not parse .env file: {ex.Message}");
                Console.WriteLine("Continuing with existing environment variables...");
            }

            var apiKey = Environment.GetEnvironmentVariable("MERGE_API_KEY");
            var accountToken = Environment.GetEnvironmentVariable("MERGE_ACCOUNT_TOKEN");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(accountToken))
            {
                Console.WriteLine("ERROR: MERGE_API_KEY or MERGE_ACCOUNT_TOKEN not set!");
                Console.WriteLine($"MERGE_API_KEY: {(string.IsNullOrEmpty(apiKey) ? "(empty)" : "***set***")}");
                Console.WriteLine($"MERGE_ACCOUNT_TOKEN: {(string.IsNullOrEmpty(accountToken) ? "(empty)" : "***set***")}");
                return;
            }

            // Example call with sample employee ID and fiscal year dates
            var result = await SummarizeEmployeePayrollRuns(
                employeeId: "63ba045d-a1dc-465f-a5ba-798c1d333278",
                currentFYStart: new DateTime(2016, 7, 1),
                currentFYEnd: new DateTime(2017, 6, 30)
            );

            Console.WriteLine("Calculated Results for this Employee");
            var options = new JsonSerializerOptions { WriteIndented = true };
            Console.WriteLine(JsonSerializer.Serialize(result, options));
        }
    }
}
