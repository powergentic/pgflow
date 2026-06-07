#!/usr/bin/env bash
set -euo pipefail
mkdir -p output
printf 'ready\n' > output/status.txt
echo "statusFile=output/status.txt" >> "$ORCHESTRATOR_OUTPUT"
