#!/usr/bin/env bash
# What macOS already knows about a crash, printed.
#
# Three osx-arm64 runs ended with a dead test host and produced, between them, the name of one
# test. That is an instrumentation gap rather than a hard problem: macOS writes a symbolicated
# backtrace for every crash, and nothing in CI had ever read one.
#
# Two independent sources, because neither is guaranteed on a hosted runner. ReportCrash may be
# switched off, and lldb may be unable to attach — but they fail for unrelated reasons, so asking
# both is far more likely to yield a stack than choosing one. lldb is the better answer when it
# works: it reproduces the crash under a debugger instead of reading what was left behind.
#
# Never fatal. This exists to explain a failure that has already happened, and a diagnostic that
# can turn a red run into a differently-red run is worse than no diagnostic.
set -u

# The target framework is part of this path, and a stale one does not fail: it reads as "the prober
# is not built" below, and the debugger reproduction quietly stops happening. Retargeting the
# projects means changing it here too.
prober=tests/SharpSlang.Prober/bin/Release/net11.0/SharpSlang.Prober

if command -v lldb >/dev/null 2>&1 && [ -x "$prober" ]; then
  echo "=== the out-of-order teardown, under lldb"
  # --batch keeps going through the command list when the target stops on a signal, which is
  # exactly the case being investigated. A clean exit just prints the process status.
  lldb --batch -o run -o 'bt all' -o quit -- "$prober" dispose-outermost-first 2>&1 | tail -100
  echo
else
  echo "no lldb, or the prober is not built — skipping the debugger reproduction"
fi

reports="$HOME/Library/Logs/DiagnosticReports"

# ReportCrash writes asynchronously and can lag the process it reports on. A bounded wait rather
# than a blind sleep, so a runner with crash reporting off costs half a minute and not more.
for _ in $(seq 1 15); do
  ls "$reports"/*.ips >/dev/null 2>&1 && break
  sleep 2
done

echo "=== $reports"
ls -la "$reports" 2>/dev/null || echo "    (no such directory — crash reporting is off on this runner)"

# Names first, in case the interesting process is called something nobody predicted. A filter that
# silently matches nothing looks exactly like a runner with crash reporting switched off.
interesting=""
for ips in "$reports"/*.ips; do
  [ -e "$ips" ] || continue
  case "$(basename "$ips")" in
    dotnet*|testhost*|SharpSlang*|slang*) interesting="$interesting $ips" ;;
  esac
done

if [ -z "$interesting" ]; then
  echo "    no report matched dotnet/testhost/SharpSlang/slang — falling back to the newest three"
  interesting="$(ls -t "$reports"/*.ips 2>/dev/null | head -3 | tr '\n' ' ')"
fi

for ips in $interesting; do
  [ -e "$ips" ] || continue

  echo
  echo "=== $ips"
  # Some crash types are still written in the legacy plain-text layout, so a parse failure means
  # "print it raw" rather than "give up" — the header alone carries the exception type.
  python3 - "$ips" <<'PY' || head -60 "$ips"
import json
import sys

# An .ips is a one-line JSON header followed by a JSON body.
with open(sys.argv[1]) as handle:
    handle.readline()
    body = json.load(handle)

exception = body.get("exception", {})
print("    exception   {} {} {}".format(
    exception.get("type"), exception.get("signal", ""), exception.get("codes", "")))
print("    termination {}".format(body.get("termination", {})))
print("    asi         {}".format(body.get("asi", {})))

# Frames carry an index into usedImages rather than a name, so join them back up. An
# unsymbolicated `libslang-compiler.dylib + 0x...` is still worth printing: atos resolves it
# afterwards, and the image name alone says whose code faulted.
images = body.get("usedImages", [])
faulting = body.get("faultingThread")

for index, thread in enumerate(body.get("threads", [])):
    if faulting is not None and index != faulting:
        continue
    print("    --- thread {} (faulting) {}".format(index, thread.get("name", "")))
    for frame in thread.get("frames", []):
        image = images[frame["imageIndex"]] if frame.get("imageIndex") is not None else {}
        name = image.get("name") or image.get("path", "?").split("/")[-1]
        print("        {} + {}  {}".format(name, frame.get("imageOffset"), frame.get("symbol", "")))
PY
done
