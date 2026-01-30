# Org Chart Builder - C# Version

Builds organizational charts from Merge Groups using parent_group relationships, creating separate hierarchies per Group.type.

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

3. The HRIS integration must have the `Group.parent_group` field supported. You can check the Supported Integration Fields page, https://docs.merge.dev/integrations/hris/supported-fields/, to check this.

## Installation & Setup

### Run from the project root

```bash
# Run the script directly
dotnet run --project org/OrgChart.csproj
```

## Usage

### Basic Usage

Fetch all group types and build org charts for each:

```csharp
var chartsByType = await BuildOrgCharts(
    region: "US"
);
```

### Filter by Group Types

Filter by specific group types (e.g., TEAM and DEPARTMENT):

```csharp
var chartsByType = await BuildOrgCharts(
    region: "US",
    types: new List<string> { "TEAM", "DEPARTMENT" }
);
```

Available group types include:

- `TEAM` - Team structures
- `DEPARTMENT` - Departmental hierarchies
- `COST_CENTER` - Cost center organizations
- `BUSINESS_UNIT` - Business unit structures
- `GROUP` - Generic group structures

### Specify Region

Use a different API region:

```csharp
var chartsByType = await BuildOrgCharts(
    region: "EU"  // Options: "US" (default), "EU", "APAC"
);
```

## Output File

The script generates an output file in the org directory (`/org`):

### org_charts.txt

ASCII tree visualization:

```
================================================================================
ORG CHARTS - ASCII TREE VIEW
================================================================================

TEAM:
└── Engineering [abc-123]
    ├── Backend Team [def-456]
    │   └── Database Team [ghi-789]
    └── Frontend Team [jkl-012]

DEPARTMENT:
└── Sales [xyz-789]
    ├── East Region [uvw-456]
    └── West Region [rst-123]

================================================================================
SUMMARY
================================================================================
  TEAM: 1 root group(s), 4 total group(s)
  DEPARTMENT: 1 root group(s), 3 total group(s)
================================================================================
```

## Console Output

The script displays the ASCII tree visualization in the console with:

- Summary of root groups per type
- Output file path

## API Documentation

Uses the Merge HRIS Groups API:

- [Merge Groups API Documentation](https://docs.merge.dev/hris/groups/)
- [Org Chart Use Case Guide](https://docs.merge.dev/use-cases/org-chart/)

Groups represent organizational structures (teams, departments, cost centers, business units). Each group has a `parent_group` field that references another group's ID for hierarchical relationships.

## Architecture Notes

### Data Flow

1. **Fetch Groups**: Paginate through all groups from the Merge API
2. **Filter (Optional)**: Filter by specified group types if provided
3. **Index Data**: Build lookup tables by ID, parent-child relationships, and type buckets
4. **Identify Roots**: For each type, find groups with no parent or parent of different type
5. **Build Trees**: Recursively construct org chart trees from root nodes
6. **Render**: Generate console output and ASCII text file

### Key Functions

- `BuildOrgCharts()` - Main entry point for building org charts
- `FetchAllGroups()` - Handles API pagination and data fetching
- `BuildOrgChartsByType()` - Creates hierarchical structures grouped by type
- `RenderOrgChartGroup()` - Generates ASCII tree representation
- `SaveOrgChartsToTextFile()` - Exports to ASCII text format

### Type Handling

Groups are separated by their `type` field, with separate org charts built for each type:

- Department structure (DEPARTMENT)
- Team structure (TEAM)
- Cost center structure (COST_CENTER)
- Business unit structure (BUSINESS_UNIT)

A group is a "root" for its type if:

1. It has no `parent_group`, OR
2. Its `parent_group` references a different type

This allows cross-type relationships while maintaining clean hierarchies per type.
