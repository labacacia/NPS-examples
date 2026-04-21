#!/usr/bin/env bash
# Copyright 2026 INNO LOTUS PTY LTD
# SPDX-License-Identifier: Apache-2.0
#
# Cross-SDK interop runner.
#
#   1. Builds + starts the .NET Memory Node server on 127.0.0.1:17491.
#   2. Runs each available language client against /query.
#   3. Diffs the canonical (client-tag stripped) outputs — if all 4 clients
#      produce byte-identical result bodies, interop is proved.
#
# Requires: bash, dotnet 10 SDK.
# Optional (detected per-client): python3 >= 3.9, node >= 18, go >= 1.22.
# Missing runtimes are reported as "skipped" without failing the run.

set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$DIR"

echo "── cross-sdk-interop ──"

# ── 1. Build server + .NET client in parallel ─────────────────────────────────

echo "[build] dotnet server + client"
dotnet build server/NPS.Demo.InteropServer.csproj -nologo -v minimal >/dev/null
dotnet build clients/dotnet/NPS.Demo.InteropClient.csproj -nologo -v minimal >/dev/null

# ── 2. Start server ───────────────────────────────────────────────────────────

echo "[start] server on http://127.0.0.1:17491"
SERVER_LOG=$(mktemp)
dotnet run --project server/NPS.Demo.InteropServer.csproj --no-build >"$SERVER_LOG" 2>&1 &
SERVER_PID=$!
trap 'kill "$SERVER_PID" 2>/dev/null || true; rm -f "$SERVER_LOG"' EXIT

# Wait for /query to respond before launching clients (max 10 s).
for _ in $(seq 1 50); do
  if curl -sf -X POST -H "Content-Type: application/json" \
       -d '{"limit":1,"filter":{}}' http://127.0.0.1:17491/query >/dev/null; then
    break
  fi
  sleep 0.2
done

# ── 3. Run each client, capture stdout ───────────────────────────────────────

OUT_DIR=$(mktemp -d)
declare -a RAN=()

run_client() {
  local name=$1
  shift
  echo "[client:$name]"
  if "$@" > "$OUT_DIR/$name.json"; then
    RAN+=("$name")
    # Echo the JSON so the operator can eyeball it.
    cat "$OUT_DIR/$name.json"
    echo
  else
    echo "  ! $name failed — see $OUT_DIR/$name.json"
    return 1
  fi
}

# .NET is mandatory (the server itself is .NET so the toolchain is guaranteed).
run_client dotnet dotnet run --project clients/dotnet/NPS.Demo.InteropClient.csproj --no-build

# Optional runtimes — skipped with a note if the binary is missing.
if command -v python3 >/dev/null; then
  run_client python python3 clients/python.py
else
  echo "[client:python] skipped — python3 not on PATH"
fi

if command -v node >/dev/null; then
  run_client nodejs node clients/nodejs.mjs
else
  echo "[client:nodejs] skipped — node not on PATH"
fi

if command -v go >/dev/null; then
  run_client go go run clients/go.go
else
  echo "[client:go] skipped — go not on PATH"
fi

# ── 4. Diff canonical payloads ────────────────────────────────────────────────
#
# Strip the "client" tag from each file (that's the *only* intentional
# difference). Everything else MUST be byte-identical — count, anchor_ref,
# the data rows, key order, numeric representation of price, etc.

canon() {
  # Strip `"client": "…"` and the trailing comma/whitespace.
  sed -E 's/^[[:space:]]*"client":[[:space:]]*"[^"]*",[[:space:]]*$//' "$1" \
    | sed -E 's/,[[:space:]]*$//' \
    | tr -s '\n'
}

echo "── diff canonical outputs ──"

MASTER="${RAN[0]}"
FAIL=0
for name in "${RAN[@]:1}"; do
  if diff <(canon "$OUT_DIR/$MASTER.json") <(canon "$OUT_DIR/$name.json") >/dev/null; then
    echo "  ✓ $name == $MASTER"
  else
    echo "  ✗ $name differs from $MASTER"
    diff <(canon "$OUT_DIR/$MASTER.json") <(canon "$OUT_DIR/$name.json") || true
    FAIL=1
  fi
done

if [[ "$FAIL" -eq 0 ]]; then
  echo "── result: interop verified across ${#RAN[@]} clients ──"
else
  echo "── result: FAILED ──"
  exit 1
fi
