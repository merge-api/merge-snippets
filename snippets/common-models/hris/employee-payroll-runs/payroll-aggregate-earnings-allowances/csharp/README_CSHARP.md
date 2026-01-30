# Employee Payroll Runs - C# Version

Computes earnings by type and category for current and last fiscal years using the Merge API.

## Prerequisites

1. **Environment Variables** - `.env` file:

   ```
   MERGE_API_KEY=your_api_key_here
   MERGE_ACCOUNT_TOKEN=your_account_token_here
   ```

2. **Required JSON Files** - The following files must be present in the `payroll` directory:
   - `earnings_ukg.json` - Maps UKG Pro earning codes to human-readable labels
   - `earnings_workday.json` - Maps Workday earning codes to human-readable labels
   - `earnings_to_customer_ukg.json` - Maps UKG earnings to customer-specific fields
   - `earnings_to_customer_workday.json` - Maps Workday earning codes to customer-specific fields
   - `earnings_to_customer.json` - Combined Workday and UKG Pro earning codes to customer-specific fields. Combination of `earnings_to_customer_ukg.json` and `earnings_to_customer_workday.json`. Earning codes that do not have a clear mapping are classified as "Other Allowances" or "Other Earnings".

## Installation & Setup

### Run from the project root

```bash
# Run the script directly
dotnet run --project payroll/EmployeePayrollRuns.csproj
```

## Usage

### Basic Usage

Defaults to current calendar year (Jan 1 - Dec 31):

```csharp
var result = await SummarizeEmployeePayrollRuns(
    employeeId: "e55e289f-1f21-4144-ab02-acd851356cfd"
);
```

### Custom Fiscal Year

Specify custom fiscal year dates:

```csharp
var result = await SummarizeEmployeePayrollRuns(
    employeeId: "e55e289f-1f21-4144-ab02-acd851356cfd",
    currentFYStart: new DateTime(2024, 7, 1),
    currentFYEnd: new DateTime(2025, 6, 30)
);
```

### Specify Region

Use a different API region:

```csharp
var result = await SummarizeEmployeePayrollRuns(
    employeeId: "e55e289f-1f21-4144-ab02-acd851356cfd",
    region: "EU"  // Options: "US" (default), "EU", "APAC"
);
```

### Using Check Date Filtering

For integrations where `ended_at` is not available on payroll runs, use the `useCheckDateFiltering` parameter:

```csharp
var result = await SummarizeEmployeePayrollRuns(
    employeeId: "e55e289f-1f21-4144-ab02-acd851356cfd",
    currentFYStart: new DateTime(2024, 7, 1),
    currentFYEnd: new DateTime(2025, 6, 30),
    useCheckDateFiltering: true
);
```

#### When to Use Check Date Filtering

**Default behavior (useCheckDateFiltering = false):**

- Uses `ended_after` and `ended_before` query parameters in the Merge API request
- Filters payroll runs based on the `ended_at` field
- More efficient as it retrieves only relevant records
- **Use this for most integrations** (Workday, UKG Pro, etc.)

**Check date filtering (useCheckDateFiltering = true):**

- Does NOT use `ended_after`/`ended_before` query parameters
- Retrieves all payroll runs for the employee (no date filtering on API side)
- Filters in-memory based on the `check_date` field
- **Use this when:**
  - The integration doesn't populate `ended_at` fields (ADP SFTP)
  - Payroll runs only have `check_date` available
  - The `ended_at` field is consistently `null` in API responses

**Note:** This `useCheckDateFiltering` in-memory filtering approach is just for script purposes. In production environments, implementations will typically fetch all payroll run data from Merge once, store it in their own database, and then query their database for any filtering (including datetime filtering) and analytics. This avoids repeated API calls to Merge and provides better performance for analytics and reporting use cases.

#### Performance Considerations

- Check date filtering retrieves more data from the API (all payroll runs), so it may be slower
- However, it's necessary when `ended_at` is not available in the integration (ADP SFTP)

## Production Guidance

### Customer Onboarding

The `earnings_workday.json` file contains Workday sandbox earning codes, and the `earnings_ukg.json` file contains Workday sandbox earning codes. When onboarding a new customer:

1. **Verify Earning Codes** - Confirm that the customer's Workday/UKG earning codes are in `earnings_{integration}.json`
2. **Add Additional Codes** - If the customer has additional earning codes not present in `earnings_{integration}.json`, add them
3. **Map to CUSTOMER Taxonomy Fields** - For any new earning codes added, create a mapping in `earnings_to_customer.json` that maps the Workday/UKG/etc earning code to the appropriate CUSTOMER-specific taxonomy field.

Example mapping in `earnings_to_customer.json`:

```json
{
  "CUSTOMER_BONUS_CODE": "Cash Incentives",
  "CUSTOMER_CAR_ALLOWANCE": "Car Allowances",
  "CUSTOMER_OTHER_ALLOWANCE": "Other Allowances"
}
```

## Output

### Workday

JSON with earnings data for both fiscal years:

```json
{
  "CurrentFY": {
    "StartDate": "2023-07-01",
    "EndDate": "2024-06-30",
    "Year": 2024,
    "NetPay": 78069.74,
    "TotalGrossEarnings": 115511.4,
    "EarningsByType": {
      "BASE": {
        "EarningCode": "BASE",
        "Label": "Salary Base Pay [USA]",
        "Amount": 110130.12
      },
      "GTL-IMP": {
        "EarningCode": "GTL-IMP",
        "Label": "Imputed Income - GTL [USA]",
        "Amount": 51.84
      },
      "HOLIDAY": {
        "EarningCode": "HOLIDAY",
        "Label": "Holiday Pay",
        "Amount": 5329.44
      }
    },
    "EarningsByCategory": {
      "Base Pay / Salary": 110130.12,
      "Other Allowances or Earnings": 5381.28
    }
  },
  "LastFY": {
    "StartDate": "2022-07-01",
    "EndDate": "2023-06-30",
    "Year": 2023,
    "NetPay": 104316.36,
    "TotalGrossEarnings": 158448.03,
    "EarningsByType": {
      "BASE": {
        "EarningCode": "BASE",
        "Label": "Salary Base Pay [USA]",
        "Amount": 140597.66
      },
      "GTL-IMP": {
        "EarningCode": "GTL-IMP",
        "Label": "Imputed Income - GTL [USA]",
        "Amount": 66.24
      },
      "INCENT": {
        "EarningCode": "INCENT",
        "Label": "Incentive - Quarterly Run",
        "Amount": 11297.01
      },
      "HOLIDAY": {
        "EarningCode": "HOLIDAY",
        "Label": "Holiday Pay",
        "Amount": 6487.12
      }
    },
    "EarningsByCategory": {
      "Base Pay / Salary": 140597.66,
      "Other Allowances or Earnings": 6553.36,
      "Cash Incentives": 11297.01
    }
  }
}
```

### UKG Pro

JSON with earnings data for both fiscal years:

In UKG Pro, the Earnings record contains the earning code as the "remote_id" and the description of the Earning as the "type"
To keep the script consistent, note that EarningsByType's key and the EarningCode values are just the longdescription and will look differently than Workday.
You can adjust the script to have the expected output.

```json
{
  "CurrentFY": {
    "StartDate": "2016-07-01",
    "EndDate": "2017-06-30",
    "Year": 2017,
    "NetPay": 23801.22,
    "TotalGrossEarnings": 37647.25,
    "EarningsByType": {
      "Group Term Life": {
        "EarningCode": "Group Term Life",
        "Label": "Group Term Life",
        "Amount": 27.9
      },
      "Overtime 1.5": {
        "EarningCode": "Overtime 1.5",
        "Label": "Overtime 1.5",
        "Amount": 955.35
      },
      "Regular Pay": {
        "EarningCode": "Regular Pay",
        "Label": "Regular Pay",
        "Amount": 36664.0
      }
    },
    "EarningsByCategory": {
      "Other Earnings": 27.9,
      "Overtime": 955.35,
      "Base Pay / Salary": 36664.0
    }
  },
  "LastFY": {
    "StartDate": "2015-07-01",
    "EndDate": "2016-06-30",
    "Year": 2016,
    "NetPay": 26805.12,
    "TotalGrossEarnings": 42159.13,
    "EarningsByType": {
      "Regular Pay": {
        "EarningCode": "Regular Pay",
        "Label": "Regular Pay",
        "Amount": 40976.0
      },
      "Group Term Life": {
        "EarningCode": "Group Term Life",
        "Label": "Group Term Life",
        "Amount": 30.68
      },
      "Overtime 1.5": {
        "EarningCode": "Overtime 1.5",
        "Label": "Overtime 1.5",
        "Amount": 1152.45
      }
    },
    "EarningsByCategory": {
      "Base Pay / Salary": 40976.0,
      "Other Earnings": 30.68,
      "Overtime": 1152.45
    }
  }
}
```
