"""Cổng T4: sheet 5.1 khai code chưa hề có test -> chặn.

Đọc content JSON của 5.1, với mỗi method lấy sheet + method name rồi quét
thư mục tests/ xem có file nào nhắc tới class/method đó không. Nếu sheet
vẫn ghi P (Passed) mà repo không có dòng test nào chạm tới -> FAIL.

Đối chứng âm bắt buộc:
    python scripts/testdoc/check_coverage_claim.py                              # trên sample hiện tại PHẢI báo 4 sheet ma
    python scripts/testdoc/check_coverage_claim.py --allow-ghosts               # bỏ qua để run_all đi tiếp (khi chưa vá)

Sau khi vá xong 4 class, chạy không cờ phải xanh.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Fix Windows console cp1252 khi in tiếng Việt (giống run_all.py dùng PYTHONIOENCODING=utf-8)
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# Map sheet -> các token phải xuất hiện trong tests/ để được coi là "có test".
# Nếu không map, suy ra từ field `method` (lấy phần trước dấu chấm).
GHOST_SHEETS = {
    "RecurrenceCalc": ["RecurrenceCalculator"],
    "PlanValidator": ["OrchestrationPlanValidator"],
    "PublicUrlSafety": ["DnsPublicUrlSafetyValidator", "PublicUrlSafetyValidator"],
    "ReviewOutcomeParse": ["StrictContentReviewOutcomeParser"],
    # Sheet biên: hằng số, không phải class test — có test gián tiếp, không tính là ma.
    "AuthPolicyConstants": ["AuthPolicy"],
}

# Sheet -> số UTCID đang khai P trong sample (để thông báo rõ hơn)
GHOST_COUNTS = {
    "RecurrenceCalc": 10,
    "PlanValidator": 16,
    "PublicUrlSafety": 21,
    "ReviewOutcomeParse": 25,
}


def load_methods(content_path: str) -> list[dict]:
    """Tra ve danh sach method da chuan hoa: {sheet, method, cases:[{result}]}.

    Ho tro ca 2 luoc do:
      - cu (phang): methods[].{sheet, method, cases[].result = "P"}
      - moi (hang): modules[].{sheet, methods[].{method, cases[].round1.result}}
    Chuan hoa result ve "P" khi Passed de phan con lai dung chung.
    """
    data = json.loads(open(content_path, encoding="utf-8").read())
    modules = data.get("modules")
    if modules is None:
        return data.get("methods", [])

    normalized: list[dict] = []
    for mod in modules:
        sheet = mod.get("sheet", "")
        for meth in mod.get("methods", []):
            cases = []
            for c in meth.get("cases", []):
                result = c.get("round1", {}).get("result")
                cases.append({"result": "P" if result == "Passed" else result})
            normalized.append({
                "sheet": sheet,
                "method": meth.get("method", mod.get("module", sheet)),
                "cases": cases,
            })
    return normalized


def collect_test_corpus(tests_root: str) -> str:
    """Gộp toàn bộ .cs trong tests/ thành một chuỗi để tìm token nhanh."""
    parts: list[str] = []
    for dirpath, _, files in os.walk(tests_root):
        for fn in files:
            if fn.endswith(".cs"):
                try:
                    parts.append(open(os.path.join(dirpath, fn), encoding="utf-8", errors="ignore").read())
                except OSError:
                    pass
    return "\n".join(parts)


def sheet_has_test(sheet: str, method: str, corpus: str) -> bool:
    tokens = GHOST_SHEETS.get(sheet)
    if tokens is None:
        # Suy ra token từ method: "LlmBaseUrlGuard.IsAllowedBaseUrl" -> "LlmBaseUrlGuard"
        base = method.split(".")[0].strip() if method else sheet
        # Fallback: chính tên sheet
        tokens = [base, sheet]
    for tok in tokens:
        if tok and re.search(re.escape(tok), corpus):
            return True
    return False


def main() -> int:
    ap = argparse.ArgumentParser(description="T4: sheet 5.1 không được khai khống")
    ap.add_argument("--content", default="scripts/testdoc/content/unit_test.sample.json",
                    help="đường dẫn tới content JSON của 5.1")
    ap.add_argument("--tests-root", default="tests", help="thư mục tests/")
    ap.add_argument("--allow-ghosts", action="store_true",
                    help="chỉ cảnh báo, không exit 1 (dùng tạm khi chưa vá)")
    args = ap.parse_args()

    content_path = os.path.join(ROOT, args.content) if not os.path.isabs(args.content) else args.content
    tests_root = os.path.join(ROOT, args.tests_root) if not os.path.isabs(args.tests_root) else args.tests_root

    if not os.path.isfile(content_path):
        print(f"Không tìm thấy content: {content_path}", file=sys.stderr)
        return 1

    methods = load_methods(content_path)
    corpus = collect_test_corpus(tests_root) if os.path.isdir(tests_root) else ""

    ghosts: list[str] = []
    for m in methods:
        sheet = m.get("sheet", "")
        method = m.get("method", "")
        cases = m.get("cases", [])
        has_passed = any(c.get("result") == "P" for c in cases)
        if not has_passed:
            continue
        if not sheet_has_test(sheet, method, corpus):
            count = len([c for c in cases if c.get("result") == "P"])
            ghosts.append(f"  - {sheet} ({method}): {count} UTCID ghi P nhưng tests/ không có dòng nào nhắc tới {GHOST_SHEETS.get(sheet, [method.split('.')[0] if method else sheet])}")

    if ghosts:
        print("PHAT HIEN SHEET KHAI KHONG (T4 FAIL):", flush=True)
        for line in ghosts:
            print(line, flush=True)
        flagged_sheets = set()
        for g in ghosts:
            name = g.split()[1]
            flagged_sheets.add(name)
        flagged_count = sum(len([c for c in m.get("cases", []) if c.get("result") == "P"]) for m in methods if m.get("sheet") in flagged_sheets)
        print(f"\nTong {len(flagged_sheets)} sheet / {flagged_count} UTCID khai P cho code chua co test.", flush=True)
        print("Goi y: viet test that cho cac class nay hoac go sheet khoi report (ghi ro ly do thu hep pham vi).", flush=True)
        if args.allow_ghosts:
            print("\n(--allow-ghosts: chi canh bao, exit 0)", flush=True)
            return 0
        return 1

    print("T4 DAT: moi sheet co P deu co test tuong ung trong tests/.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
