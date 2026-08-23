"""Dien ket qua 5.2 tu manifest .trx THAT - khong dien tay, khong bia.

Nguyen tac: mot TC chi duoc ghi Passed khi trong manifest .trx co test tu dong
DA CHAY VA PASS dung ten da khai trong integration_evidence.map.json. TC khong
co anh xa giu nguyen Pending kem ly do o cot Note, de nguoi doc report biet ro
cho nao da chay may va cho nao con phai chay tay.

    python scripts/testdoc/apply_integration_evidence.py \
        --manifest docs/test/evidence/2026-08-20_R1/manifest.json --round round1

Chay lai duoc nhieu lan (idempotent): moi lan deu ghi de tu manifest.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
HERE = os.path.dirname(os.path.abspath(__file__))

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

CONTENT = os.path.join(HERE, "content", "integration_test.sample.json")
MAPPING = os.path.join(HERE, "content", "integration_evidence.map.json")

# Cot Tester: day la lan chay tu dong, khong phai nguoi ngoi bam.
# Ghi ro nhu vay de khong ai hieu nham la co nguoi da test tay.
AUTOMATED_TESTER = "Automated suite"


def resolve(path: str) -> str:
    return path if os.path.isabs(path) else os.path.join(ROOT, path)


def load_manifest(path: str) -> tuple[dict, str]:
    """Tra ve ({testFullName: mappedResult}, ngay chay YYYY-MM-DD)."""
    data = json.load(open(resolve(path), encoding="utf-8"))
    results = {e["testFullName"]: e.get("mappedResult") for e in data.get("entries", [])}
    run_date = (data.get("createdAt") or "")[:10]
    return results, run_date


def lookup(results: dict, wanted: list[str]) -> tuple[bool, list[str]]:
    """Tim cac test da Pass khop tien to. Tra ve (tat ca deu co it nhat 1 pass, ten khop)."""
    matched: list[str] = []
    for name in wanted:
        hits = [full for full, res in results.items()
                if res == "P" and (full == name or full.startswith(name + ".")
                                   or full.startswith(name + "("))]
        if not hits:
            return False, matched
        matched.extend(sorted(hits)[:3])
    return True, matched


def main() -> int:
    ap = argparse.ArgumentParser(description="Dien 5.2 tu manifest .trx that")
    ap.add_argument("--manifest", action="append", required=True,
                    help="manifest.json cua lan chay (lap lai duoc)")
    ap.add_argument("--round", default="round1", choices=["round1", "round2", "round3"])
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    results: dict = {}
    run_date = ""
    manifest_ref = args.manifest[0]
    for m in args.manifest:
        part, date = load_manifest(m)
        results.update(part)
        run_date = date or run_date

    mapping = json.load(open(MAPPING, encoding="utf-8"))
    tc_map = mapping["map"]
    reasons = mapping["notRunReason"]

    data = json.load(open(CONTENT, encoding="utf-8"))
    filled = pending = missing = 0
    unmapped_no_reason: list[str] = []

    for sheet in data["sheets"]:
        for group in sheet.get("scenarios", []):
            for case in group.get("cases", []):
                tc = case["id"]
                wanted = tc_map.get(tc)
                if wanted:
                    ok, matched = lookup(results, wanted)
                    if ok:
                        case[args.round] = {
                            "result": "Passed",
                            "date": run_date,
                            "tester": AUTOMATED_TESTER,
                        }
                        case["evidence"] = {
                            "testFullNames": wanted,
                            "manifest": manifest_ref,
                            "matched": matched[:3],
                        }
                        filled += 1
                    else:
                        # Khai anh xa nhung manifest khong co -> KHONG duoc ghi Passed.
                        case[args.round] = {"result": "Pending", "date": None, "tester": ""}
                        case.pop("evidence", None)
                        case["note"] = ("Mapped automated test was not present in this run: "
                                        + ", ".join(wanted))
                        missing += 1
                        print(f"  THIEU TRONG MANIFEST: {tc} -> {wanted}")
                else:
                    case[args.round] = {"result": "Pending", "date": None, "tester": ""}
                    case.pop("evidence", None)
                    reason = reasons.get(tc)
                    if reason:
                        case["note"] = "Not executed in this round. " + reason
                    else:
                        unmapped_no_reason.append(tc)
                    pending += 1

                # Cac vong chua chay: de trong hoan toan.
                for other in ("round1", "round2", "round3"):
                    if other == args.round:
                        continue
                    case[other] = {"result": "Pending", "date": None, "tester": ""}

    if unmapped_no_reason:
        print("LOI: TC khong co anh xa VA khong co ly do trong notRunReason:")
        for tc in unmapped_no_reason:
            print(f"  - {tc}")
        return 1

    if not args.dry_run:
        json.dump(data, open(CONTENT, "w", encoding="utf-8"), ensure_ascii=False, indent=2)

    total = filled + pending + missing
    print(f"\n{args.round}: {filled}/{total} TC co test tu dong PASS that "
          f"(ngay {run_date}), {pending} TC de Pending kem ly do, {missing} TC hut manifest.")
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
