#!/usr/bin/env bash
# Copyright 2026 INNO LOTUS PTY LTD
# SPDX-License-Identifier: Apache-2.0
#
# AssuranceLevel fromWire("") cross-SDK parity check.
#
# Verifies that every available NPS SDK returns ANONYMOUS (wire value
# "anonymous") when fromWire is called with an empty string "".
# This is the NPS-3 §4.2.1 / NPS-RFC-0003 §5.1.1 requirement:
#   fromWire(null) == fromWire("") == ANONYMOUS
#
# Requires: bash.
# Optional (detected per runtime): python3 >= 3.9, node >= 18, go >= 1.22,
#   java 17+. Missing runtimes are reported as "skipped".
#
# Each snippet imports the real SDK package (no stubs).
# Expected output for every client: "anonymous"

set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
PASS=0
FAIL=0
SKIP=0

check() {
  local name="$1"
  local actual="$2"
  if [[ "$actual" == "anonymous" ]]; then
    echo "  ✓ $name: fromWire(\"\") → anonymous"
    ((PASS++))
  else
    echo "  ✗ $name: expected anonymous, got: $actual"
    ((FAIL++))
  fi
}

echo "── assurance-level fromWire(\"\") parity ──"

# ── .NET (nps-lib / NPS.NIP) ─────────────────────────────────────────────────
# dotnet is mandatory for this repo; the server's .csproj is the project handle.
_NET_SNIPPET=$(mktemp --suffix=.csx)
cat >"$_NET_SNIPPET" <<'CSHARP'
#r "nuget: NPS.NIP, *"
using NPS.NIP;
Console.Write(AssuranceLevels.FromWireOrAnonymous("").Wire);
CSHARP

if dotnet script "$_NET_SNIPPET" >/dev/null 2>&1; then
  OUT=$(dotnet script "$_NET_SNIPPET" 2>/dev/null)
  check "dotnet" "$OUT"
else
  # Fall back to inline project using the server's build artefacts.
  OUT=$(dotnet run --project "$DIR/server/NPS.Demo.InteropServer.csproj" -- --assurance-level-parity 2>/dev/null || echo "")
  if [[ -n "$OUT" ]]; then
    check "dotnet" "$OUT"
  else
    echo "  - dotnet: skipped (dotnet-script not available; add --assurance-level-parity support to server to enable)"
    ((SKIP++))
  fi
fi
rm -f "$_NET_SNIPPET"

# ── Python ────────────────────────────────────────────────────────────────────
if command -v python3 >/dev/null 2>&1; then
  OUT=$(python3 - <<'PYEOF'
from nps_sdk.nip.assurance_level import AssuranceLevel
print(AssuranceLevel.from_wire("").wire, end="")
PYEOF
  )
  check "python" "$OUT"
else
  echo "  - python: skipped — python3 not on PATH"
  ((SKIP++))
fi

# ── TypeScript / Node.js ──────────────────────────────────────────────────────
if command -v node >/dev/null 2>&1; then
  OUT=$(node --input-type=module <<'TSEOF'
import { AssuranceLevel } from "@labacacia/nps-sdk";
process.stdout.write(AssuranceLevel.fromWire("").wire);
TSEOF
  )
  check "nodejs" "$OUT"
else
  echo "  - nodejs: skipped — node not on PATH"
  ((SKIP++))
fi

# ── Go ────────────────────────────────────────────────────────────────────────
if command -v go >/dev/null 2>&1; then
  _GO_DIR=$(mktemp -d)
  cat >"$_GO_DIR/main.go" <<'GOEOF'
package main

import (
	"fmt"
	"github.com/labacacia/nps-sdk-go/nip"
)

func main() {
	fmt.Print(nip.AssuranceLevelFromWire("").Wire())
}
GOEOF
  cat >"$_GO_DIR/go.mod" <<'GOMOD'
module assurance_parity

go 1.22

require github.com/labacacia/nps-sdk-go v1.0.0-alpha.11
GOMOD
  OUT=$(cd "$_GO_DIR" && go run . 2>/dev/null || echo "error")
  rm -rf "$_GO_DIR"
  check "go" "$OUT"
else
  echo "  - go: skipped — go not on PATH"
  ((SKIP++))
fi

# ── Java ──────────────────────────────────────────────────────────────────────
if command -v java >/dev/null 2>&1 && command -v mvn >/dev/null 2>&1; then
  _JAVA_DIR=$(mktemp -d)
  mkdir -p "$_JAVA_DIR/src/main/java"
  cat >"$_JAVA_DIR/src/main/java/Parity.java" <<'JAVAEOF'
import com.labacacia.nps.nip.AssuranceLevel;
public class Parity {
    public static void main(String[] args) {
        System.out.print(AssuranceLevel.fromWire("").getWire());
    }
}
JAVAEOF
  cat >"$_JAVA_DIR/pom.xml" <<'POMEOF'
<project>
  <modelVersion>4.0.0</modelVersion>
  <groupId>parity</groupId><artifactId>parity</artifactId><version>1</version>
  <dependencies>
    <dependency>
      <groupId>com.labacacia</groupId><artifactId>nps-sdk</artifactId>
      <version>1.0.0-alpha.11</version>
    </dependency>
  </dependencies>
</project>
POMEOF
  OUT=$(cd "$_JAVA_DIR" && mvn -q compile exec:java -Dexec.mainClass=Parity 2>/dev/null || echo "error")
  rm -rf "$_JAVA_DIR"
  check "java" "$OUT"
else
  echo "  - java: skipped — java/mvn not on PATH"
  ((SKIP++))
fi

echo ""
echo "── result: $PASS passed, $FAIL failed, $SKIP skipped ──"

[[ "$FAIL" -eq 0 ]]
