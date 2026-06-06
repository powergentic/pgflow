#!/usr/bin/env bash
set -euo pipefail

analysis_file="${ANALYSIS_FILE:-}"

if [[ -z "$analysis_file" ]]; then
  echo "ANALYSIS_FILE was not provided." >&2
  exit 1
fi

if [[ ! -f "$analysis_file" ]]; then
  echo "Analysis file not found: $analysis_file" >&2
  exit 1
fi

echo "===== PGFLOW FINAL ANALYSIS ====="
cat "$analysis_file"
echo
echo "===== END PGFLOW FINAL ANALYSIS ====="

echo "publishedAnalysisFile=$analysis_file" >> "$ORCHESTRATOR_OUTPUT"
