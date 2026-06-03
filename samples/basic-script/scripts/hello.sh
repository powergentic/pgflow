#!/usr/bin/env bash
set -euo pipefail

echo "$GREETING"
echo "message=$GREETING" >> "$ORCHESTRATOR_OUTPUT"
