"""Chuyen .trx cua dotnet test sang JSON cho 5.1 (khong dung COM).

Doc moi file .trx (XML MSTest), trich:
- testName (Fully displayName ke ca [Theory] args)
- outcome (Passed/Failed/Skipped/NotExecuted)
- duration (TimeSpan)
- startTime (UTC)

Gom thanh manifest + mapping UTCID.

Dang chay that lay so lieu:
    dotnet test tests/Clawbot.Domain.Tests --logger \"trx;LogFileName=Clawbot.Domain.Tests.trx\" --results-directory docs/test/evidence/<run-id>/unit
    python scripts/testdoc/trx_to_unit.py --trx-dir docs/test/evidence/<run-id>/unit --out scripts/testdoc/content/unit_test.real.json --manifest docs/test/evidence/<run-id>/manifest.json

Hien tai chua co trx that -> script chay tren sample van tao ra file hop le (de dan wiring).
"""

from __future__ import annotations

import argparse
import datetime as dt
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# Map trx outcome -> P/F/S
OUTCOME_MAP = {
    "Passed": "P",
    "Failed": "F",
    "Skipped": "S",
    "NotExecuted": "N",
}


def parse_trx(path: str) -> list[dict]:
    """Parse 1 file .trx -> list test result dict."""
    try:
        tree = ET.parse(path)
    except ET.ParseError as e:
        print(f"  Canh bao: khong parse duoc {path}: {e}", file=sys.stderr)
        return []
    ns = {"vsm": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    results: list[dict] = []
    for node in tree.findall(".//vsm:UnitTestResult", ns):
        # Attributes are not namespaced
        name = node.get("testName", "") or ""
        outcome = node.get("outcome", "NotExecuted")
        duration_raw = node.get("duration", "00:00:00")
        start_raw = node.get("startTime", "")
        # startTime is "2026-08-19T00:00:00.0000000+07:00" or similar
        try:
            duration_ms = int(dt.timedelta(
                hours=int(duration_raw.split(":")[0]),
                minutes=int(duration_raw.split(":")[1]),
                seconds=float(duration_raw.split(":")[2]),
            ).total_seconds() * 1000) if duration_raw else 0
        except Exception:
            duration_ms = 0
        try:
            started = dt.datetime.fromisoformat(start_raw.replace("Z", "+00:00")) if start_raw else None
        except Exception:
            started = None
        # executionId links to UnitTest definition for class name
        execution_id = node.get("executionId", "")
        results.append({
            "testName": name,
            "outcome": outcome,
            "mappedResult": OUTCOME_MAP.get(outcome, "N"),
            "durationMs": duration_ms,
            "startedAt": started.isoformat() if started else start_raw,
            "trxFile": os.path.basename(path),
            "executionId": execution_id,
            "rawOutcome": outcome,
        })
    # Fallback without namespace (older trx)
    if not results:
        for node in tree.findall(".//UnitTestResult"):
            name = node.get("testName", "") or ""
            outcome = node.get("outcome", "NotExecuted")
            results.append({
                "testName": name,
                "outcome": outcome,
                "mappedResult": OUTCOME_MAP.get(outcome, "N"),
                "durationMs": 0,
                "startedAt": node.get("startTime", ""),
                "trxFile": os.path.basename(path),
                "executionId": node.get("executionId", ""),
                "rawOutcome": outcome,
            })
    return results


def collect_trx(trx_dir: str) -> list[dict]:
    all_results: list[dict] = []
    for path in sorted(glob.glob(os.path.join(trx_dir, "*.trx"))):
        results = parse_trx(path)
        all_results.extend(results)
        print(f"  {os.path.basename(path)}: {len(results)} test results")
    return all_results


def build_manifest(results: list[dict], run_id: str, git_commit: str) -> dict:
    now = dt.datetime.now(dt.timezone(dt.timedelta(hours=7))).isoformat()
    # Group by trx file for summary
    by_file: dict[str, int] = {}
    for r in results:
        by_file[r["trxFile"]] = by_file.get(r["trxFile"], 0) + 1
    total = len(results)
    passed = sum(1 for r in results if r["mappedResult"] == "P")
    failed = sum(1 for r in results if r["mappedResult"] == "F")
    skipped = sum(1 for r in results if r["mappedResult"] == "S")
    return {
        "runId": run_id,
        "createdAt": now,
        "gitCommit": git_commit,
        "summary": {
            "total": total,
            "passed": passed,
            "failed": failed,
            "skipped": skipped,
            "passRate": round(passed / total * 100, 1) if total else 0,
            "byFile": by_file,
        },
        "entries": [
            {
                "testFullName": r["testName"],
                "outcome": r["outcome"],
                "mappedResult": r["mappedResult"],
                "durationMs": r["durationMs"],
                "startedAt": r["startedAt"],
                "artifact": f"unit/{r['trxFile']}#{r['testName']}",
            }
            for r in results
        ],
    }


def git_head() -> str:
    import subprocess
    try:
        return subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    except Exception:
        return "unknown"


def main() -> int:
    ap = argparse.ArgumentParser(description="Chuyen .trx sang JSON/manifest cho 5.1")
    ap.add_argument("--trx-dir", required=True, help="thu muc chua *.trx")
    ap.add_argument("--out", help="file JSON chua list test results (de tham chieu)")
    ap.add_argument("--manifest", help="file manifest.json")
    ap.add_argument("--run-id", default=None, help="run id (mac dinh: YYYY-MM-DD_R1)")
    args = ap.parse_args()

    trx_dir = args.trx_dir if os.path.isabs(args.trx_dir) else os.path.join(ROOT, args.trx_dir)
    if not os.path.isdir(trx_dir):
        print(f"Khong tim thay thu muc trx: {trx_dir}", file=sys.stderr)
        return 1

    results = collect_trx(trx_dir)
    print(f"Tong {len(results)} test results tu {trx_dir}")

    run_id = args.run_id or dt.datetime.now(dt.timezone(dt.timedelta(hours=7))).strftime("%Y-%m-%d_R1")
    manifest = build_manifest(results, run_id, git_head())

    if args.out:
        out_path = args.out if os.path.isabs(args.out) else os.path.join(ROOT, args.out)
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(results, f, ensure_ascii=False, indent=2)
        print(f"Ghi {len(results)} results -> {out_path}")

    if args.manifest:
        m_path = args.manifest if os.path.isabs(args.manifest) else os.path.join(ROOT, args.manifest)
        os.makedirs(os.path.dirname(m_path), exist_ok=True)
        with open(m_path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, ensure_ascii=False, indent=2)
        print(f"Ghi manifest -> {m_path} (total={manifest['summary']['total']}, passed={manifest['summary']['passed']})")

    if not results:
        print("CANH BAO: khong co test result nao (trx rong hoac sai duong dan).", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
