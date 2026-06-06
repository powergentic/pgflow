#!/usr/bin/env bash
set -uo pipefail

mkdir -p output
build_report="output/dotnet-build.txt"

find_first() {
  find . -type f \( -name "$1" \) -not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' | sort | head -n 1
}

build_target="$(find_first '*.slnx')"
if [[ -z "$build_target" ]]; then
  build_target="$(find_first '*.sln')"
fi
if [[ -z "$build_target" ]]; then
  build_target="$(find_first '*.csproj')"
fi

if [[ -z "$build_target" ]]; then
  cat > "$build_report" <<'EOF'
No .slnx, .sln, or .csproj file was found in the target working directory.
GitHub Copilot should scaffold the ASP.NET Core MVC + Bootstrap TODO application before build can succeed.
EOF
  echo "buildSucceeded=false" >> "$ORCHESTRATOR_OUTPUT"
  echo "buildTarget=" >> "$ORCHESTRATOR_OUTPUT"
  echo "buildReportFile=$build_report" >> "$ORCHESTRATOR_OUTPUT"
  exit 1
fi

{
  echo "Running dotnet build against: $build_target"
  echo
  dotnet build "$build_target"
} 2>&1 | tee "$build_report"
exit_code=${PIPESTATUS[0]}

if [[ $exit_code -eq 0 ]]; then
  build_succeeded=true
else
  build_succeeded=false
fi

echo "buildSucceeded=$build_succeeded" >> "$ORCHESTRATOR_OUTPUT"
echo "buildTarget=$build_target" >> "$ORCHESTRATOR_OUTPUT"
echo "buildReportFile=$build_report" >> "$ORCHESTRATOR_OUTPUT"

exit "$exit_code"
