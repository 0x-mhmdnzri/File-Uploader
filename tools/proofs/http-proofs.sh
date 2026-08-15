#!/usr/bin/env bash
# P4.5 HTTP proofs against a running WebApi (single node is enough for these;
# double-complete still exercises CAS). Multi-node: set BASE_A and BASE_B.
set -euo pipefail

BASE="${BASE:-http://localhost:5073}"
BASE_A="${BASE_A:-$BASE}"
BASE_B="${BASE_B:-$BASE}"

PASS=0
FAIL=0

ok() { echo "  PASS: $*"; PASS=$((PASS + 1)); }
bad() { echo "  FAIL: $*"; FAIL=$((FAIL + 1)); }

need_jq() {
  if ! command -v jq >/dev/null 2>&1; then
    echo "jq is required (brew install jq / apt install jq)"
    exit 1
  fi
}

need_jq

echo "=== D16 Happy path (${BASE_A}) ==="
INIT=$(curl -sf -X POST "$BASE_A/api/uploads/initiate" \
  -F "fileName=proof-happy.bin" \
  -F "totalSize=12" \
  -F "chunkSize=8")
UPLOAD_ID=$(echo "$INIT" | jq -r .uploadId)
TOTAL=$(echo "$INIT" | jq -r .totalChunks)
echo "  uploadId=$UPLOAD_ID totalChunks=$TOTAL"

# two chunks: 8 + 4 bytes
printf '12345678' | curl -sf -X PUT "$BASE_A/api/uploads/$UPLOAD_ID/chunk/0" -H 'Content-Type: application/octet-stream' --data-binary @- >/dev/null
printf 'ABCD' | curl -sf -X PUT "$BASE_A/api/uploads/$UPLOAD_ID/chunk/1" -H 'Content-Type: application/octet-stream' --data-binary @- >/dev/null

STATUS=$(curl -sf "$BASE_A/api/uploads/$UPLOAD_ID/status")
RC=$(echo "$STATUS" | jq -r .receivedCount)
if [[ "$RC" == "2" ]]; then ok "status receivedCount=2"; else bad "status receivedCount=$RC expected 2"; fi

COMP=$(curl -sf -X POST "$BASE_A/api/uploads/$UPLOAD_ID/complete")
PATH_OUT=$(echo "$COMP" | jq -r .path)
if [[ -n "$PATH_OUT" && "$PATH_OUT" != "null" ]]; then ok "complete path=$PATH_OUT"; else bad "complete missing path: $COMP"; fi

ST2=$(curl -sf "$BASE_A/api/uploads/$UPLOAD_ID/status")
ST_STATUS=$(echo "$ST2" | jq -r .status)
if [[ "$ST_STATUS" == "Completed" ]]; then ok "status Completed"; else bad "status=$ST_STATUS"; fi

echo "=== D10 Idempotent PUT ==="
INIT2=$(curl -sf -X POST "$BASE_A/api/uploads/initiate" \
  -F "fileName=proof-idemp.bin" \
  -F "totalSize=4" \
  -F "chunkSize=4")
ID2=$(echo "$INIT2" | jq -r .uploadId)
printf 'WXYZ' | curl -sf -X PUT "$BASE_A/api/uploads/$ID2/chunk/0" -H 'Content-Type: application/octet-stream' --data-binary @- >/dev/null
R1=$(curl -sf -X PUT "$BASE_A/api/uploads/$ID2/chunk/0" -H 'Content-Type: application/octet-stream' --data-binary @<(printf 'WXYZ'))
IDEMP=$(echo "$R1" | jq -r .idempotent)
if [[ "$IDEMP" == "true" ]]; then ok "second PUT idempotent=true"; else bad "idempotent=$IDEMP body=$R1"; fi
curl -sf -X POST "$BASE_A/api/uploads/$ID2/complete" >/dev/null
ok "idempotent session completed"

echo "=== D17 Double complete (parallel against A and B) ==="
INIT3=$(curl -sf -X POST "$BASE_A/api/uploads/initiate" \
  -F "fileName=proof-double.bin" \
  -F "totalSize=4" \
  -F "chunkSize=4")
ID3=$(echo "$INIT3" | jq -r .uploadId)
printf 'ZZZZ' | curl -sf -X PUT "$BASE_A/api/uploads/$ID3/chunk/0" -H 'Content-Type: application/octet-stream' --data-binary @- >/dev/null

# Fire two completes; at least one must succeed with path; the other may 400 or also return completed path.
TMPDIR=$(mktemp -d)
curl -s -o "$TMPDIR/a.json" -w "%{http_code}" -X POST "$BASE_A/api/uploads/$ID3/complete" >"$TMPDIR/a.code" &
curl -s -o "$TMPDIR/b.json" -w "%{http_code}" -X POST "$BASE_B/api/uploads/$ID3/complete" >"$TMPDIR/b.code" &
wait
CODE_A=$(cat "$TMPDIR/a.code")
CODE_B=$(cat "$TMPDIR/b.code")
echo "  complete A http=$CODE_A body=$(cat "$TMPDIR/a.json")"
echo "  complete B http=$CODE_B body=$(cat "$TMPDIR/b.json")"

ST3=$(curl -sf "$BASE_A/api/uploads/$ID3/status")
ST3S=$(echo "$ST3" | jq -r .status)
if [[ "$ST3S" == "Completed" ]]; then
  ok "after double-complete status=Completed"
elif [[ "$ST3S" == "Failed" ]]; then
  bad "double-complete left Failed (unexpected for full parts)"
else
  # one request may still be Completing briefly
  sleep 1
  ST3=$(curl -sf "$BASE_A/api/uploads/$ID3/status")
  ST3S=$(echo "$ST3" | jq -r .status)
  if [[ "$ST3S" == "Completed" ]]; then ok "status Completed after brief wait"; else bad "status=$ST3S"; fi
fi

# Exactly one 200 with path, or one 200 and one 400 CAS conflict — both acceptable
SUCCESS=0
[[ "$CODE_A" == "200" ]] && SUCCESS=$((SUCCESS + 1))
[[ "$CODE_B" == "200" ]] && SUCCESS=$((SUCCESS + 1))
if [[ "$SUCCESS" -ge 1 ]]; then ok "at least one complete HTTP 200"; else bad "no successful complete"; fi

rm -rf "$TMPDIR"

echo "=== D14 Health readiness ==="
LIVE=$(curl -sf "$BASE_A/health/live" | jq -r .status)
READY=$(curl -sf "$BASE_A/health/ready" | jq -r .status)
if [[ "$LIVE" == "Healthy" ]]; then ok "/health/live Healthy"; else bad "live=$LIVE"; fi
if [[ "$READY" == "Healthy" ]]; then ok "/health/ready Healthy"; else bad "ready=$READY"; fi

echo ""
echo "Results: PASS=$PASS FAIL=$FAIL"
if [[ "$FAIL" -gt 0 ]]; then exit 1; fi
