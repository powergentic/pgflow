#!/usr/bin/env bash
set -euo pipefail

mkdir -p output
status_file="output/workspace-status.txt"
summary_file="output/workspace-summary.txt"

solution_file="$(find . -type f \( -name '*.slnx' -o -name '*.sln' \) -not -path '*/node_modules/*' | sort | head -n 1)"
project_count="$(find . -type f -name '*.csproj' -not -path '*/node_modules/*' | wc -l | tr -d ' ')"
bootstrap_marker="$(find . -type f \( -name '*.cshtml' -o -name '_Layout.cshtml' -o -name '*.css' \) -not -path '*/bin/*' -not -path '*/obj/*' | sort | head -n 1)"

app_exists="false"
if [[ -n "$solution_file" || "$project_count" != "0" ]]; then
  app_exists="true"
fi

{
  echo "Target working directory: $PWD"
  echo "Existing solution: ${solution_file:-<none>}"
  echo "C# project count: $project_count"
  echo "MVC/Bootstrap marker: ${bootstrap_marker:-<none>}"
  echo "App exists: $app_exists"
} > "$summary_file"

{
  echo "Workspace assessment for the ASP.NET Core MVC + Bootstrap TODO app workflow"
  echo ""
  echo "Target working directory: $PWD"
  echo "Detected solution file: ${solution_file:-<none>}"
  echo "Detected C# project count: $project_count"
  echo "Detected MVC/Bootstrap marker: ${bootstrap_marker:-<none>}"
  echo "Existing app detected: $app_exists"
  echo ""
  echo "Top-level directory listing:"
  find . -maxdepth 2 \
    -not -path './.git' \
    -not -path './.git/*' \
    -not -path './node_modules' \
    -not -path './node_modules/*' \
    -not -path './bin' \
    -not -path './bin/*' \
    -not -path './obj' \
    -not -path './obj/*' \
    | sort
} > "$status_file"

echo "appExists=$app_exists" >> "$ORCHESTRATOR_OUTPUT"
echo "workspaceStatusFile=$status_file" >> "$ORCHESTRATOR_OUTPUT"
echo "workspaceSummaryFile=$summary_file" >> "$ORCHESTRATOR_OUTPUT"
echo "workspaceSummary=$(tr '\n' ' ' < "$summary_file" | sed 's/  */ /g')" >> "$ORCHESTRATOR_OUTPUT"
