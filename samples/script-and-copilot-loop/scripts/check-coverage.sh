#!/usr/bin/env bash
set -uo pipefail

mkdir -p output/test-results
coverage_test_report="output/dotnet-test-coverage.txt"
coverage_report="output/code-coverage-summary.txt"
minimum_code_coverage_percent="${MINIMUM_CODE_COVERAGE_PERCENT:-90}"

find_first() {
  find . -type f \( -name "$1" \) -not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' | sort | head -n 1
}

coverage_target="$(find_first '*.slnx')"
if [[ -z "$coverage_target" ]]; then
  coverage_target="$(find_first '*.sln')"
fi
if [[ -z "$coverage_target" ]]; then
  coverage_target="$(find_first '*Tests.csproj')"
fi
if [[ -z "$coverage_target" ]]; then
  coverage_target="$(find_first '*.csproj')"
fi

if [[ -z "$coverage_target" ]]; then
  cat > "$coverage_report" <<'EOF'
No solution or test project was found in the target working directory.
GitHub Copilot should add tests for the ASP.NET Core MVC + Bootstrap application and configure code coverage collection.
EOF
  echo "coveragePassed=false" >> "$ORCHESTRATOR_OUTPUT"
  echo "coveragePercent=0.00" >> "$ORCHESTRATOR_OUTPUT"
  echo "coverageTarget=" >> "$ORCHESTRATOR_OUTPUT"
  echo "coverageReportFile=$coverage_report" >> "$ORCHESTRATOR_OUTPUT"
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  cat > "$coverage_report" <<'EOF'
python3 is required by this sample workflow to aggregate Cobertura coverage files.
Install python3 or update the sample script to use another XML parser.
EOF
  echo "coveragePassed=false" >> "$ORCHESTRATOR_OUTPUT"
  echo "coveragePercent=0.00" >> "$ORCHESTRATOR_OUTPUT"
  echo "coverageTarget=$coverage_target" >> "$ORCHESTRATOR_OUTPUT"
  echo "coverageReportFile=$coverage_report" >> "$ORCHESTRATOR_OUTPUT"
  exit 1
fi

rm -rf output/test-results/*
{
  echo "Running dotnet test with coverage against: $coverage_target"
  echo
  dotnet test "$coverage_target" --no-build --collect:"XPlat Code Coverage" --results-directory output/test-results --logger "console;verbosity=minimal"
} 2>&1 | tee "$coverage_test_report"
coverage_command_exit=${PIPESTATUS[0]}

coverage_files=()
while IFS= read -r file; do
  coverage_files+=("$file")
done < <(find output/test-results -type f -name 'coverage.cobertura.xml' | sort)

if [[ $coverage_command_exit -ne 0 || ${#coverage_files[@]} -eq 0 ]]; then
  {
    echo "Coverage collection did not succeed."
    echo "Command exit code: $coverage_command_exit"
    echo "Coverage files found: ${#coverage_files[@]}"
    echo
    echo "See $coverage_test_report for the full dotnet test output."
  } > "$coverage_report"
  echo "coveragePassed=false" >> "$ORCHESTRATOR_OUTPUT"
  echo "coveragePercent=0.00" >> "$ORCHESTRATOR_OUTPUT"
  echo "coverageTarget=$coverage_target" >> "$ORCHESTRATOR_OUTPUT"
  echo "coverageReportFile=$coverage_report" >> "$ORCHESTRATOR_OUTPUT"
  exit 1
fi

coverage_percent="$(python3 - "$minimum_code_coverage_percent" "${coverage_files[@]}" <<'PY'
import sys
import xml.etree.ElementTree as ET

minimum = float(sys.argv[1])
files = sys.argv[2:]
lines_covered = 0.0
lines_valid = 0.0
for path in files:
    root = ET.parse(path).getroot()
    lines_covered += float(root.attrib.get("lines-covered", 0) or 0)
    lines_valid += float(root.attrib.get("lines-valid", 0) or 0)

percent = 0.0 if lines_valid == 0 else (lines_covered / lines_valid) * 100.0
print(f"{percent:.2f}")
PY
)"

coverage_passed="false"
python3 - "$coverage_percent" "$minimum_code_coverage_percent" <<'PY'
import sys
percent = float(sys.argv[1])
minimum = float(sys.argv[2])
sys.exit(0 if percent >= minimum else 1)
PY
threshold_exit=$?
if [[ $threshold_exit -eq 0 ]]; then
  coverage_passed="true"
fi

{
  echo "Coverage target: $coverage_target"
  echo "Minimum coverage required: $minimum_code_coverage_percent%"
  echo "Measured coverage: $coverage_percent%"
  echo "Coverage files:"
  for file in "${coverage_files[@]}"; do
    echo "- $file"
  done
  echo
  echo "See $coverage_test_report for the full test execution output."
} > "$coverage_report"

echo "coveragePassed=$coverage_passed" >> "$ORCHESTRATOR_OUTPUT"
echo "coveragePercent=$coverage_percent" >> "$ORCHESTRATOR_OUTPUT"
echo "coverageTarget=$coverage_target" >> "$ORCHESTRATOR_OUTPUT"
echo "coverageReportFile=$coverage_report" >> "$ORCHESTRATOR_OUTPUT"

exit "$threshold_exit"
