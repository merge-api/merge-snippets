# Manager Hierarchy Builder - C# Version

Builds organizational manager hierarchy from Merge Employees using manager field relationships, creating a reporting structure org chart.

## Prerequisites

1. **.NET 10.0 SDK or later** - [Download](https://dotnet.microsoft.com/download)

   ```bash
   dotnet --version
   ```

2. **Environment Variables** - `.env` file at project root:

   ```
   MERGE_API_KEY=your_api_key_here
   MERGE_ACCOUNT_TOKEN=your_account_token_here
   ```

3. The HRIS integration must have the `Employee.manager` field supported. You can check the Supported Integration Fields page, https://docs.merge.dev/integrations/hris/supported-fields/, to check this.

## Installation & Setup

### Run from the project root

```bash
# Run the script directly
dotnet run --project manager_hierarchy/ManagerHierarchy.csproj
```

## Usage

### Basic Usage

Fetch ACTIVE employees and build manager hierarchy (US region and ACTIVE status are defaults):

```csharp
var hierarchy = await BuildManagerHierarchy();
```

### Filter by Employment Status

By default, only ACTIVE employees are included:

```csharp
var hierarchy = await BuildManagerHierarchy(employmentStatus: "INACTIVE");  // Inactive only
var hierarchy = await BuildManagerHierarchy(employmentStatus: "ALL");       // All employees. Does not use the Merge's employment_status query param
```

Available employment statuses:

- `ACTIVE` - Currently employed (default)
- `INACTIVE` - No longer employed
- `PENDING` - Employment pending/not yet started
- `ALL` - Include all employees regardless of status

### Specify Region

```csharp
var hierarchy = await BuildManagerHierarchy(region: "EU");    // Europe
var hierarchy = await BuildManagerHierarchy(region: "APAC");  // Asia-Pacific
```

## Output File

The script generates an output file in the manager_hierarchy directory (`/manager_hierarchy`):

### manager_hierarchy.txt

ASCII tree visualization with manager tags:

```
================================================================================
MANAGER HIERARCHY - ASCII TREE VIEW
================================================================================

└── Jane Smith - CEO [Manager] [abc-123]
    ├── John Doe - VP Engineering [Manager] [def-456]
    │   ├── Alice Johnson - Engineering Manager [Manager] [ghi-789]
    │   │   ├── Bob Williams - Senior Engineer [jkl-012]
    │   │   └── Carol Davis - Senior Engineer [mno-345]
    │   └── David Brown - Engineering Manager [Manager] [pqr-678]
    │       └── Eve Martinez - Engineer [stu-901]
    └── Frank Anderson - VP Sales [Manager] [vwx-234]
        ├── Grace Taylor - Sales Manager [yza-567]
        └── Henry Thomas - Sales Manager [bcd-890]

================================================================================
SUMMARY
================================================================================
  Root employees (top-level managers): 1
  Total employees: 10
================================================================================
```

Employees with direct reports are tagged with `[Manager]` in the output.

## Console Output

The script displays the ASCII tree visualization in the console with:

- Manager hierarchy showing reporting relationships
- Employee names and job titles
- Summary of root employees and total employee count
- Output file path

## API Documentation

Uses the Merge HRIS Employees API:

- [Merge Employees API Documentation](https://docs.merge.dev/hris/employees/)
- [Org Chart Use Case Guide](https://docs.merge.dev/use-cases/org-chart/)

Employees have a `manager` field that references another employee's ID, creating a hierarchical reporting structure.

## Architecture Notes

### Data Flow

1. **Fetch Employees**: Paginate through all employees from the Merge API (defaults to ACTIVE employees only)
2. **Filter**: Filter by employment status (ACTIVE by default, can be overridden)
3. **Index Data**: Build lookup tables by ID and manager-report relationships
4. **Identify Roots**: Find employees with no manager or manager not in dataset
5. **Build Trees**: Recursively construct org chart trees from root nodes
6. **Render**: Generate console output and ASCII text file

### Key Functions

- `BuildManagerHierarchy()` - Main entry point for building manager hierarchy
- `FetchAllEmployees()` - Handles API pagination and data fetching
- `BuildHierarchy()` - Creates hierarchical structures based on manager relationships
- `RenderOrgChartEmployee()` - Generates ASCII tree representation
- `SaveHierarchyToTextFile()` - Exports to ASCII text format

### Root Identification

An employee is considered a "root" (top-level manager) if:

1. They have no `manager` field (null or empty), OR
2. Their `manager` references an employee ID not in the dataset

This allows for handling incomplete data where some managers may not be in the system.
