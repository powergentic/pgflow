#!/usr/bin/env bash
set -uo pipefail

mkdir -p output
test_report="output/dotnet-test.txt"

find_first() {
  find . -type f \( -name "$1" \) -not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' | sort | head -n 1
}

test_target="$(find_first '*.slnx')"
if [[ -z "$test_target" ]]; then
  test_target="$(find_first '*.sln')"
fi
if [[ -z "$test_target" ]]; then
  test_target="$(find_first '*Tests.csproj')"
fi
if [[ -z "$test_target" ]]; then
  test_target="$(find_first '*.csproj')"
fi

if [[ -z "$test_target" ]]; then
  cat > "$test_report" <<'EOF'
No solution or test project was found in the target working directory.
GitHub Copilot should add the ASP.NET Core MVC + Bootstrap application and its test projects before tests can succeed.
EOF
  echo "testSucceeded=false" >> "$ORCHESTRATOR_OUTPUT"
  echo "testTarget=" >> "$ORCHESTRATOR_OUTPUT"
  echo "testReportFile=$test_report" >> "$ORCHESTRATOR_OUTPUT"
  exit 1
fi

{
  echo "Running dotnet test against: $test_target"
  echo
  dotnet test "$test_target" --no-build --logger "console;verbosity=minimal"
} 2>&1 | tee "$test_report"
exit_code=${PIPESTATUS[0]}

if [[ $exit_code -eq 0 ]]; then
  test_succeeded=true
else
  test_succeeded=false
fi

echo "testSucceeded=$test_succeeded" >> "$ORCHESTRATOR_OUTPUT"
echo "testTarget=$test_target" >> "$ORCHESTRATOR_OUTPUT"
echo "testReportFile=$test_report" >> "$ORCHESTRATOR_OUTPUT"

exit "$exit_code"
