#!/usr/bin/env bash
# Gate for every wave. Run from anywhere: dev-docs/verify.sh
set -uo pipefail
cd "$(dirname "$0")/.." || exit 1
mkdir -p .verify
fail=0

echo "== build"
if ! dotnet build -c Release 2>&1 | tail -4; then
  echo "GATE_FAIL build"; fail=1
fi

echo "== async void guard"
async_fail=0
while IFS=: read -r file line _; do
  if ! sed -n "$((line + 1)),$((line + 3))p" "$file" | grep -q 'try'; then
    echo "CHECK no_unguarded_async_void FAIL ($file:$line)"
    async_fail=1
  fi
done < <(rg -n 'async void' --glob '*.cs' --glob '!reference/**' .)
if [ "$async_fail" -eq 0 ]; then echo "CHECK no_unguarded_async_void PASS"; else fail=1; fi

echo "== selftest"
out=$(dotnet run -c Release --no-build -- --selftest .verify/gate.png 2>&1)
status=$?
echo "$out" | grep -E '^(CHECK|SELFTEST_OK|SELFTEST_FAIL)' || echo "$out" | tail -20
if [ $status -ne 0 ]; then echo "GATE_FAIL selftest exit=$status"; fail=1; fi
if ! echo "$out" | grep -q '^SELFTEST_OK'; then echo "GATE_FAIL no SELFTEST_OK"; fail=1; fi
if echo "$out" | grep -q 'CHECK .* FAIL'; then echo "GATE_FAIL check failed"; fail=1; fi

echo "== cli contract"
dotnet run -c Release --no-build -- --selftest >/dev/null 2>&1; code=$?
echo "no-path exit=$code (expect 2)"
[ "$code" -eq 2 ] || { echo "GATE_FAIL cli"; fail=1; }
dotnet run -c Release --no-build -- --bogus >/dev/null 2>&1; code=$?
echo "unknown-arg exit=$code (expect 2)"
[ "$code" -eq 2 ] || { echo "GATE_FAIL cli"; fail=1; }
help_out=$(dotnet run -c Release --no-build -- --help 2>/dev/null); code=$?
echo "help exit=$code (expect 0)"
if [ "$code" -eq 0 ] && printf '%s\n' "$help_out" | grep -q '^Usage:'; then
  echo "CHECK cli_help PASS"
else
  echo "CHECK cli_help FAIL"; fail=1
fi

echo "== artifacts"
ls -l .verify/gate.png .verify/gate-export.png .verify/gate-shapes.png 2>/dev/null
sha256sum .verify/gate.png .verify/gate-export.png .verify/gate-shapes.png 2>/dev/null

echo "== tree"
git status --short

if [ $fail -eq 0 ]; then echo "GATE_OK"; else echo "GATE_FAILED"; fi
exit $fail
