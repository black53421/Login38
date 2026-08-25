#!/usr/bin/env bash
# Project sensor. Exit 0 = the tree is in a shippable state.
# Invoked by the Stop hook at the end of every turn.
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1

fail=0

echo "== build (warnings are errors) =="
if ! dotnet build Login38.slnx -c Debug --nologo -v minimal 2>&1 | tail -40; then
  fail=1
fi
# tail() masks the build exit code, so re-check it explicitly.
if ! dotnet build Login38.slnx -c Debug --nologo -v quiet >/dev/null 2>&1; then
  echo "BUILD FAILED"
  fail=1
fi

echo
echo "== tests =="
# Test projects start out empty; xunit exits non-zero when a project has no tests,
# which is not a failure while the port is still in progress.
# A trx per run, so a red sensor says which test rather than only how many. A failure
# that will not reproduce on its own is the kind this has produced twice, and "1 failed"
# with no name is nothing to go on.
results=$(mktemp -d)
test_output=$(dotnet test Login38.slnx -c Debug --nologo -v quiet \
  --logger trx --results-directory "$results" 2>&1)
test_code=$?
if [ $test_code -ne 0 ] && ! grep -qi "no test.*available\|沒有可用的測試" <<<"$test_output"; then
  echo "$test_output" | tail -40
  echo
  echo "-- failed:"
  python - "$results" <<'PY' 2>/dev/null || true
import glob, sys, xml.etree.ElementTree as ET

ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

for path in glob.glob(sys.argv[1] + "/*.trx"):
    for result in ET.parse(path).getroot().iterfind(".//t:UnitTestResult", ns):
        if result.get("outcome") == "Failed":
            print("  " + (result.get("testName") or "?"))
            message = result.find(".//t:Message", ns)
            if message is not None and message.text:
                print("    " + message.text.strip().splitlines()[0])
PY
  echo "TESTS FAILED"
  fail=1
else
  echo "$test_output" | grep -iE "passed|failed|通過|失敗" | tail -10
fi

rm -rf "$results"

echo
if [ $fail -eq 0 ]; then
  echo "CHECK OK"
else
  echo "CHECK FAILED"
fi
exit $fail
