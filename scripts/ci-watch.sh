#!/usr/bin/env bash
# Đọc trạng thái GitHub Actions qua REST API, dùng token sẵn có của Git Credential Manager.
# Không cần cài GitHub CLI.
#
#   scripts/ci-watch.sh list [count]        # các run gần nhất (mọi workflow)
#   scripts/ci-watch.sh status <run_id>     # job + step nào fail
#   scripts/ci-watch.sh logs <run_id> [n]   # n dòng cuối của mỗi job fail (mặc định 60)
#   scripts/ci-watch.sh watch [sha] [secs]  # poll tới khi run xong, tự in log fail

set -uo pipefail

WORKDIR="${TMPDIR:-/tmp}/clawbot-ci-watch"
mkdir -p "$WORKDIR"

repo_slug() {
  git remote get-url origin \
    | sed -E 's#^git@github\.com:#https://github.com/#' \
    | sed -E 's#^https://github\.com/##; s#\.git$##'
}

gh_token() {
  printf 'protocol=https\nhost=github.com\n\n' | git credential fill | sed -n 's/^password=//p'
}

REPO="$(repo_slug)"
TOKEN="$(gh_token)"

if [ -z "$TOKEN" ]; then
  echo "Không lấy được token GitHub từ git credential." >&2
  exit 1
fi

api() {
  # api <relative-path> <output-file>
  curl -sL -o "$2" -w '%{http_code}' \
    -H "Authorization: Bearer $TOKEN" \
    -H "Accept: application/vnd.github+json" \
    -H "X-GitHub-Api-Version: 2022-11-28" \
    "https://api.github.com/repos/$REPO/$1"
}

# Mã Python nằm trong chuỗi nháy đơn của bash nên không dùng được nháy đơn bên trong;
# dùng %-format thay f-string vì f-string có dấu \" chỉ hợp lệ từ Python 3.12.
py() { PYTHONIOENCODING=utf-8 python -c "$1" "${@:2}"; }

cmd_list() {
  local n="${1:-10}"
  local f="$WORKDIR/runs.json"
  local code
  code="$(api "actions/runs?per_page=$n" "$f")"
  [ "$code" = "200" ] || { echo "HTTP $code khi lấy danh sách run" >&2; cat "$f" >&2; return 1; }
  py '
import io,json,sys
d=json.load(io.open(sys.argv[1],encoding="utf-8"))
row="%-12s %-8s %-28s %-12s %-10s %s"
print(row % ("RUN ID","SHA","WORKFLOW","STATUS","RESULT","CREATED"))
for r in d.get("workflow_runs",[]):
    print(row % (r["id"], r["head_sha"][:7], r["name"][:27], r["status"], str(r["conclusion"]), r["created_at"]))
' "$f"
}

cmd_status() {
  local run_id="$1"
  local f="$WORKDIR/jobs-$run_id.json"
  local code
  code="$(api "actions/runs/$run_id/jobs?per_page=50" "$f")"
  [ "$code" = "200" ] || { echo "HTTP $code khi lấy job của run $run_id" >&2; cat "$f" >&2; return 1; }
  py '
import io,json,sys
d=json.load(io.open(sys.argv[1],encoding="utf-8"))
for j in d.get("jobs",[]):
    print("job %s | %s | %s | %s" % (j["id"], j["name"], j["status"], j["conclusion"]))
    for s in j.get("steps") or []:
        if s.get("conclusion") not in ("success","skipped",None):
            print("    FAIL step %s: %s -> %s" % (s["number"], s["name"], s["conclusion"]))
' "$f"
}

cmd_logs() {
  local run_id="$1" tail_lines="${2:-60}"
  local jf="$WORKDIR/jobs-$run_id.json"
  local code
  code="$(api "actions/runs/$run_id/jobs?per_page=50" "$jf")"
  [ "$code" = "200" ] || { echo "HTTP $code khi lấy job của run $run_id" >&2; return 1; }

  local job_ids
  job_ids="$(py '
import io,json,sys
d=json.load(io.open(sys.argv[1],encoding="utf-8"))
print(" ".join(str(j["id"]) for j in d.get("jobs",[]) if j.get("conclusion")=="failure"))
' "$jf")"

  if [ -z "${job_ids// /}" ]; then
    echo "Run $run_id: không có job nào fail."
    return 0
  fi

  for job_id in $job_ids; do
    local lf="$WORKDIR/job-$job_id.log"
    code="$(api "actions/jobs/$job_id/logs" "$lf")"
    [ "$code" = "200" ] || { echo "HTTP $code khi tải log job $job_id" >&2; continue; }
    echo "===== job $job_id (log: $lf) ====="
    py '
import io,re,sys
path,keep=sys.argv[1],int(sys.argv[2])
raw=io.open(path,encoding="utf-8",errors="replace").read().splitlines()
strip=re.compile(r"\x1b\[[0-9;]*m")
lines=[strip.sub("",l) for l in raw]
# Cắt từ nhóm lệnh cuối cùng trở đi để bỏ phần build dài dòng.
start=0
for i,l in enumerate(lines):
    if "##[group]Run " in l:
        start=i
tail=lines[start:]
if len(tail)>keep*3:
    tail=tail[:keep]+["... (lược bớt) ..."]+tail[-keep*2:]
print("\n".join(tail))
' "$lf" "$tail_lines"
  done
}

cmd_watch() {
  local want_sha="${1:-}" interval="${2:-30}"
  [ -n "$want_sha" ] || want_sha="$(git rev-parse HEAD)"
  local short="${want_sha:0:7}"
  echo "Theo dõi run của commit $short trên $REPO (poll ${interval}s)"

  while true; do
    local f="$WORKDIR/runs.json"
    local code
    code="$(api "actions/runs?per_page=20" "$f")"
    if [ "$code" != "200" ]; then
      echo "HTTP $code khi poll, thử lại sau ${interval}s" >&2
      sleep "$interval"; continue
    fi

    local line
    line="$(py '
import io,json,sys
d=json.load(io.open(sys.argv[1],encoding="utf-8"))
sha=sys.argv[2]
rows=[r for r in d.get("workflow_runs",[]) if r["head_sha"].startswith(sha)]
if not rows:
    print("NONE")
else:
    pend=[r for r in rows if r["status"]!="completed"]
    bad=[r for r in rows if r.get("conclusion")=="failure"]
    if pend:
        r=pend[0]
        print("RUNNING %s %s %s" % (r["id"], r["name"], r["status"]))
    elif bad:
        print("FAILED " + " ".join(str(r["id"]) for r in bad))
    else:
        print("SUCCESS " + " ".join(str(r["id"]) for r in rows))
' "$f" "$short")"

    local state="${line%% *}" rest="${line#* }"
    case "$state" in
      NONE)    echo "[$(date +%H:%M:%S)] chưa có run nào cho $short" ;;
      RUNNING) echo "[$(date +%H:%M:%S)] đang chạy: $rest" ;;
      SUCCESS) echo "[$(date +%H:%M:%S)] TẤT CẢ RUN XANH: $rest"; return 0 ;;
      FAILED)
        echo "[$(date +%H:%M:%S)] CÓ RUN FAIL: $rest"
        for rid in $rest; do
          cmd_status "$rid"
          cmd_logs "$rid" 60
        done
        return 2 ;;
    esac
    sleep "$interval"
  done
}

case "${1:-}" in
  list)   shift; cmd_list "$@" ;;
  status) shift; cmd_status "$@" ;;
  logs)   shift; cmd_logs "$@" ;;
  watch)  shift; cmd_watch "$@" ;;
  *) sed -n '2,10p' "$0"; exit 1 ;;
esac
