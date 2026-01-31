"""
Compute earnings by type and category for current and last fiscal years
Groups earnings by individual earning codes and target categories
Requires:
 - MERGE_API_KEY (server-side)
 - MERGE_ACCOUNT_TOKEN (linked account token)
 - employee_id (Merge UUID for the employee)
Optional:
 - current_fy_start (defaults to Jan 1 of current year)
 - current_fy_end (defaults to Dec 31 of current year)
 - region (US/EU/APAC)
"""

import os
import json
import asyncio
from datetime import datetime, timezone
from typing import Dict, Optional, List
from dataclasses import dataclass, field, asdict
from pathlib import Path
import httpx


@dataclass
class EarningDetail:
    earning_code: str
    label: str
    amount: float = 0.0


@dataclass
class FiscalYearEarnings:
    year: int
    net_pay: float = 0.0
    total_gross_earnings: float = 0.0
    earnings_by_type: Dict[str, EarningDetail] = field(default_factory=dict)
    earnings_by_category: Dict[str, float] = field(default_factory=dict)


@dataclass
class FiscalYearResult:
    start_date: str
    end_date: str
    year: int
    net_pay: float
    total_gross_earnings: float
    earnings_by_type: Dict[str, dict]
    earnings_by_category: Dict[str, float]


@dataclass
class SummaryResult:
    current_fy: FiscalYearResult
    last_fy: FiscalYearResult


class EmployeePayrollRuns:
    HOSTS = {
        "US": "https://api.merge.dev",
        "EU": "https://api.eu.merge.dev",
        "APAC": "https://api.apac.merge.dev"
    }

    @staticmethod
    async def summarize_employee_payroll_runs(
        employee_id: str,
        current_fy_start: Optional[datetime] = None,
        current_fy_end: Optional[datetime] = None,
        region: str = "US",
        use_check_date_filtering: bool = False,
        earnings_map_file: str = "earnings_ukg.json",
        category_map_file: str = "earnings_to_aon.json"
    ) -> SummaryResult:
        """
        Summarize employee payroll runs for current and last fiscal years.

        Args:
            employee_id: Merge UUID for the employee
            current_fy_start: Start date for current fiscal year (defaults to Jan 1 of current year)
            current_fy_end: End date for current fiscal year (defaults to Dec 31 of current year)
            region: Region code (US/EU/APAC)
            use_check_date_filtering: Whether to filter by check_date instead of ended date
            earnings_map_file: JSON file mapping earning codes to labels (e.g., "earnings_workday.json")
            category_map_file: JSON file mapping earning codes to categories (e.g., "earnings_to_aon_workday.json")

        Returns:
            SummaryResult containing data for current and last fiscal years
        """
        base_url = f"{EmployeePayrollRuns.HOSTS[region]}/api/hris/v1/employee-payroll-runs"

        merge_api_key = os.getenv("MERGE_API_KEY", "")
        merge_account_token = os.getenv("MERGE_ACCOUNT_TOKEN", "")

        # Default to calendar year
        today = datetime.now(timezone.utc)
        current_year = today.year

        this_fy_start = current_fy_start or datetime(current_year, 1, 1, 0, 0, 0, tzinfo=timezone.utc)
        this_fy_end = current_fy_end or datetime(current_year, 12, 31, 0, 0, 0, tzinfo=timezone.utc)

        # Calculate last fiscal year dates
        last_fy_start = datetime(
            this_fy_start.year - 1,
            this_fy_start.month,
            this_fy_start.day,
            tzinfo=timezone.utc
        )
        last_fy_end = datetime(
            this_fy_end.year - 1,
            this_fy_end.month,
            this_fy_end.day,
            tzinfo=timezone.utc
        )

        this_fy = this_fy_end.year
        last_fy = last_fy_end.year

        # Load earnings mapping
        earnings_map = EmployeePayrollRuns._load_mapping_file(earnings_map_file)

        # Load earnings-to-category mapping
        category_map = EmployeePayrollRuns._load_mapping_file(category_map_file)

        # Fetch both fiscal years in parallel
        async with httpx.AsyncClient(timeout=30.0) as client:
            this_fy_task = EmployeePayrollRuns._fetch_payroll_runs_for_period(
                client, base_url, employee_id, this_fy_start, this_fy_end, this_fy,
                merge_api_key, merge_account_token, earnings_map, category_map,
                use_check_date_filtering
            )
            last_fy_task = EmployeePayrollRuns._fetch_payroll_runs_for_period(
                client, base_url, employee_id, last_fy_start, last_fy_end, last_fy,
                merge_api_key, merge_account_token, earnings_map, category_map,
                use_check_date_filtering
            )

            this_fy_data, last_fy_data = await asyncio.gather(this_fy_task, last_fy_task)

        # Convert earnings_by_type to dict format
        this_fy_earnings_dict = {
            code: asdict(detail) for code, detail in this_fy_data.earnings_by_type.items()
        }
        last_fy_earnings_dict = {
            code: asdict(detail) for code, detail in last_fy_data.earnings_by_type.items()
        }

        return SummaryResult(
            current_fy=FiscalYearResult(
                start_date=this_fy_start.strftime("%Y-%m-%d"),
                end_date=this_fy_end.strftime("%Y-%m-%d"),
                year=this_fy_data.year,
                net_pay=this_fy_data.net_pay,
                total_gross_earnings=this_fy_data.total_gross_earnings,
                earnings_by_type=this_fy_earnings_dict,
                earnings_by_category=this_fy_data.earnings_by_category
            ),
            last_fy=FiscalYearResult(
                start_date=last_fy_start.strftime("%Y-%m-%d"),
                end_date=last_fy_end.strftime("%Y-%m-%d"),
                year=last_fy_data.year,
                net_pay=last_fy_data.net_pay,
                total_gross_earnings=last_fy_data.total_gross_earnings,
                earnings_by_type=last_fy_earnings_dict,
                earnings_by_category=last_fy_data.earnings_by_category
            )
        )

    @staticmethod
    def _load_mapping_file(filename: str) -> Dict[str, str]:
        """Load a JSON mapping file from various possible locations."""
        possible_paths = [
            Path.cwd() / filename,
            Path.cwd() / "payroll" / filename,
            Path(__file__).parent / filename,
            Path(__file__).parent / "payroll" / filename,
        ]

        for path in possible_paths:
            if path.exists():
                with open(path, 'r') as f:
                    return json.load(f)

        # Return empty dict if not found
        return {}

    @staticmethod
    async def _fetch_payroll_runs_for_period(
        client: httpx.AsyncClient,
        base_url: str,
        employee_id: str,
        start_date: datetime,
        end_date: datetime,
        year: int,
        merge_api_key: str,
        merge_account_token: str,
        earnings_map: Dict[str, str],
        category_map: Dict[str, str],
        use_check_date_filtering: bool = False
    ) -> FiscalYearEarnings:
        """Fetch and process payroll runs for a specific period."""
        result = FiscalYearEarnings(
            year=year,
            net_pay=0.0,
            total_gross_earnings=0.0,
            earnings_by_type={},
            earnings_by_category={}
        )

        cursor = None
        query_params = {
            "employee_id": employee_id,
            "expand": "earnings,deductions,taxes",
            "page_size": "100"
        }

        # Add date filters if not using check_date filtering
        if not use_check_date_filtering:
            query_params["ended_after"] = start_date.strftime("%Y-%m-%d")
            query_params["ended_before"] = end_date.strftime("%Y-%m-%d")

        headers = {
            "Authorization": f"Bearer {merge_api_key}",
            "X-Account-Token": merge_account_token,
            "Accept": "application/json"
        }

        while True:
            # Build URL with query params
            params = query_params.copy()
            if cursor:
                params["cursor"] = cursor

            response = await client.get(base_url, params=params, headers=headers)

            if response.status_code != 200:
                raise Exception(f"Merge API error {response.status_code}: {response.text}")

            data = response.json()
            results = data.get("results", [])

            for run in results:
                # If using check_date filtering, filter in-memory
                if use_check_date_filtering:
                    check_date_str = run.get("check_date")
                    if check_date_str:
                        try:
                            check_date = datetime.fromisoformat(check_date_str.replace('Z', '+00:00'))
                            # Skip if check_date is outside our range
                            if check_date < start_date or check_date > end_date:
                                continue
                        except (ValueError, AttributeError):
                            continue
                    else:
                        continue

                # Sum net pay
                net_pay = run.get("net_pay")
                if net_pay is not None:
                    result.net_pay += float(net_pay)

                # Process earnings
                earnings = run.get("earnings", [])
                if earnings:
                    for earning in earnings:
                        earning_code = earning.get("type", "")
                        amount = earning.get("amount")

                        if amount is None or float(amount) == 0:
                            continue

                        amount = float(amount)

                        # Get human-readable label from earnings map
                        label = earnings_map.get(earning_code, earning_code)

                        # Add to total gross earnings
                        result.total_gross_earnings += amount

                        # Group by earning code/type
                        if earning_code not in result.earnings_by_type:
                            result.earnings_by_type[earning_code] = EarningDetail(
                                earning_code=earning_code,
                                label=label,
                                amount=0.0
                            )
                        result.earnings_by_type[earning_code].amount += amount

                        # Group by target category
                        category = category_map.get(
                            earning_code,
                            category_map.get("Other Allowances or Earnings", "Other Allowances")
                        )

                        if category not in result.earnings_by_category:
                            result.earnings_by_category[category] = 0.0
                        result.earnings_by_category[category] += amount

            # Check for next page
            cursor = data.get("next")
            if not cursor:
                break

        return result

    @staticmethod
    def _load_env_file(path: str):
        """Load environment variables from a .env file."""
        if not os.path.exists(path):
            return

        with open(path, 'r') as f:
            for line_num, line in enumerate(f, 1):
                line = line.strip()

                # Skip empty lines and comments
                if not line or line.startswith('#'):
                    continue

                # Split on first '='
                parts = line.split('=', 1)
                if len(parts) != 2:
                    continue

                key = parts[0].strip()
                value = parts[1].strip()

                # Handle quoted values
                if value.startswith('"""') and value.endswith('"""') and len(value) >= 6:
                    value = value[3:-3]
                elif value == '"""':
                    value = ""
                elif value.startswith('"') and value.endswith('"') and len(value) >= 2:
                    value = value[1:-1]
                elif value == '""':
                    value = ""
                elif value.startswith("'") and value.endswith("'") and len(value) >= 2:
                    value = value[1:-1]
                elif value == "''":
                    value = ""

                if key in ["MERGE_API_KEY", "MERGE_ACCOUNT_TOKEN"]:
                    print(f"  Line {line_num}: {key} = {'(empty)' if not value else f'***{len(value)} chars***'}")

                os.environ[key] = value


async def main():
    """Main entry point for the script."""
    # Find and load .env file
    env_path = Path.cwd() / ".env"
    if not env_path.exists():
        current_dir = Path.cwd()
        while current_dir.parent != current_dir:
            if (current_dir / ".env").exists():
                env_path = current_dir / ".env"
                break
            current_dir = current_dir.parent

    try:
        if env_path.exists():
            print(f"Loading .env file from: {env_path}")
            EmployeePayrollRuns._load_env_file(str(env_path))
            print("✓ .env file loaded successfully")
        else:
            print(f"Warning: .env file not found at {env_path}")
    except Exception as ex:
        print(f"Warning: Could not parse .env file: {ex}")
        print("Continuing with existing environment variables...")

    api_key = os.getenv("MERGE_API_KEY")
    account_token = os.getenv("MERGE_ACCOUNT_TOKEN")

    if not api_key or not account_token:
        print("ERROR: MERGE_API_KEY or MERGE_ACCOUNT_TOKEN not set!")
        print(f"MERGE_API_KEY: {'(empty)' if not api_key else '***set***'}")
        print(f"MERGE_ACCOUNT_TOKEN: {'(empty)' if not account_token else '***set***'}")
        return

    # Example call with sample employee ID and fiscal year dates
    result = await EmployeePayrollRuns.summarize_employee_payroll_runs(
        employee_id="63ba045d-a1dc-465f-a5ba-798c1d333278",
        current_fy_start=datetime(2016, 7, 1, tzinfo=timezone.utc),
        current_fy_end=datetime(2017, 6, 30, tzinfo=timezone.utc),
        earnings_map_file="earnings_ukg.json",
        category_map_file="earnings_to_aon.json"
    )

    print("\nCalculated Results for this Employee")
    print(json.dumps(asdict(result), indent=2, default=str))


if __name__ == "__main__":
    asyncio.run(main())
