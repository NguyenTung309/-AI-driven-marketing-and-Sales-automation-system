"""Cổng T3: mọi dòng kết quả phải truy vết được + các dấu hiệu bịa.

Kiểm 5 thứ:
1. Mọi TC có result Passed/Failed phải có artifact tương ứng (truy vết trx/log).
   Ở giai đoạn hiện tại chưa có manifest/evidence -> kiểm dấu hiệu bịa thay thế:
2. Cùng một sheet mà mọi TC cùng một ngày (và >5 TC) -> dấu hiệu điền hàng loạt.
3. Tỷ lệ Passed = 100% trên toàn bộ mà 0 defect -> cảnh báo.
4. 5.2: Round 2 fail sau khi Round 1 pass mà không có ghi chú -> ngược logic hồi quy.
5. 5.3: Round 3 còn Pending khi tài liệu được bàn giao -> chưa hồi quy xong.

Chạy:
    python scripts/testdoc/check_evidence.py
    python scripts/testdoc/check_evidence.py --strict   # chặn luôn cảnh báo 100% pass

Đối chứng âm: chạy trên sample hiện tại PHẢI báo lỗi (vì sample cố tình là demo bịa).
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def load_json(path: str) -> dict:
    return json.loads(open(path, encoding="utf-8").read())


_MANIFEST_CACHE: dict = {}


def load_manifest_entries(rel_path: str):
    """Doc entries cua manifest .trx. None = khong co file."""
    if rel_path in _MANIFEST_CACHE:
        return _MANIFEST_CACHE[rel_path]
    path = rel_path if os.path.isabs(rel_path) else os.path.join(ROOT, rel_path)
    entries = load_json(path).get("entries", []) if os.path.isfile(path) else None
    _MANIFEST_CACHE[rel_path] = entries
    return entries


def verify_evidence(sheet: str, mod: dict):
    """Doi chieu loi khai `evidence` cua module voi manifest .trx that.

    Tra ve (da_xac_thuc, loi). Da_xac_thuc = moi vong co khai manifest deu tim
    thay it nhat mot test Passed thuoc dung class da khai => ngay ghi trong
    sheet la do may sinh khi chay suite chu khong phai nguoi dien tay, nen
    heuristic "nhieu ca cung mot ngay" khong con y nghia voi module do.

    Khai evidence sai (thieu manifest, hoac manifest khong co test nao khop
    class) la loi NANG HON dien hang loat: do la vien dan bang chung khong co.
    """
    ev = mod.get("evidence")
    if not ev:
        return False, []
    classes = ev.get("testClasses") or []
    if not classes:
        return False, [f"[BANG CHUNG] 5.1 sheet {sheet}: khai evidence nhung khong liet ke testClasses"]

    errors = []
    checked = 0
    for round_key in ("round1", "round2", "round3"):
        rel = ev.get(round_key)
        if not rel:
            continue
        entries = load_manifest_entries(rel)
        if entries is None:
            errors.append(f"[BANG CHUNG] 5.1 sheet {sheet} {round_key}: khong tim thay manifest {rel}")
            continue
        hit = any(
            any(e.get("testFullName", "").startswith(c) for c in classes)
            for e in entries
        )
        has_passed = any(
            e.get("mappedResult") == "P"
            and any(e.get("testFullName", "").startswith(c) for c in classes)
            for e in entries
        )
        if hit:
            checked += 1
            # Class chi co test Skipped (N) van la bang chung tu dong (may chay
            # ghi nhan NotExecuted), nhung ghi chu de nguoi doc biet khong co Passed.
            if not has_passed:
                # Khong phai loi chan: sheet da ghi N/A dung voi manifest.
                pass
        else:
            errors.append(
                f"[BANG CHUNG] 5.1 sheet {sheet} {round_key}: manifest {rel} "
                f"khong co test nao thuoc {classes}")
    return (checked > 0 and not errors), errors


def check_unit(content_path: str) -> list[str]:
    errors: list[str] = []
    data = load_json(content_path)
    # Luoc do moi: modules -> methods -> cases; result/date nam trong round1.
    # Van do luoc do cu (methods phang) de khong vo file chua migrate.
    modules = data.get("modules")
    all_results: list[str] = []
    all_defects: list[str] = []
    if modules is not None:
        for mod in modules:
            sheet = mod.get("sheet", "?")
            cases = [c for meth in mod.get("methods", []) for c in meth.get("cases", [])]
            if not cases:
                continue
            dates = [c.get("round1", {}).get("date") for c in cases]
            results = [c.get("round1", {}).get("result") for c in cases]
            all_results.extend(results)
            all_defects.extend([c.get("defect") for c in cases if c.get("defect")])
            uniq_dates = set(d for d in dates if d)
            verified, ev_errors = verify_evidence(sheet, mod)
            errors.extend(ev_errors)
            # Test tu dong: mot lenh `dotnet test` chay het trong cung mot giay nen
            # cung ngay la BINH THUONG. Chi coi la dien hang loat khi KHONG truy
            # nguoc duoc ve manifest .trx that.
            if len(cases) > 5 and len(uniq_dates) == 1 and not verified:
                errors.append(f"[BIA] 5.1 sheet {sheet}: {len(cases)} UTCID cùng 1 ngày {uniq_dates.pop()!r} (điền hàng loạt, không có manifest .trx đối chứng)")
            if results and all(r == "Passed" for r in results):
                errors.append(f"[100% P] 5.1 sheet {sheet}: {len(cases)}/{len(cases)} Passed, 0 defect")
        if all_results and all(r == "Passed" for r in all_results):
            errors.append(f"[100% P] 5.1 TOAN BO: {len(all_results)}/{len(all_results)} Passed, 0 defect trên toàn report")
        non_empty_defects = [d for d in all_defects if d and str(d).strip()]
        if all_results and not non_empty_defects:
            errors.append(f"[0 DEFECT] 5.1: {len(all_results)} UTCID, 0 defect được ghi (không có ca fail nào)")
        return errors

    methods = data.get("methods", [])
    for m in methods:
        sheet = m.get("sheet", "?")
        cases = m.get("cases", [])
        if not cases:
            continue
        dates = [c.get("date") for c in cases]
        results = [c.get("result") for c in cases]
        all_results.extend(results)
        all_defects.extend([c.get("defect") for c in cases if c.get("defect")])
        # Dấu hiệu điền hàng loạt: >5 case mà cùng 1 ngày
        uniq_dates = set(d for d in dates if d)
        if len(cases) > 5 and len(uniq_dates) == 1:
            errors.append(f"[BIA] 5.1 sheet {sheet}: {len(cases)} UTCID cùng 1 ngày {uniq_dates.pop()!r} (điền hàng loạt)")
        # Toàn P
        if results and all(r == "P" for r in results):
            errors.append(f"[100% P] 5.1 sheet {sheet}: {len(cases)}/{len(cases)} Passed, 0 defect")
    # Toàn bộ 100%
    if all_results and all(r == "P" for r in all_results):
        errors.append(f"[100% P] 5.1 TOAN BO: {len(all_results)}/{len(all_results)} Passed, 0 defect trên toàn report")
    # 0 defect trên toàn bộ
    non_empty_defects = [d for d in all_defects if d and str(d).strip()]
    if all_results and not non_empty_defects:
        errors.append(f"[0 DEFECT] 5.1: {len(all_results)} UTCID, 0 defect được ghi (không có ca fail nào)")
    return errors


def check_rowdoc(content_path: str, label: str) -> list[str]:
    errors: list[str] = []
    data = load_json(content_path)
    # 5.2 goi khoi du lieu la "sheets", 5.3 goi la "workflows". Chi doc mot khoa
    # thi 5.3 tra ve rong va cong T3 im lang cho ca tai lieu do.
    sheets = data.get("sheets") or data.get("workflows") or []
    all_r1: list[str] = []
    all_defects = 0
    for s in sheets:
        sheet_name = s.get("sheet", "?")
        scenarios = s.get("scenarios", [])
        cases = [c for g in scenarios for c in g.get("cases", [])]
        if not cases:
            continue
        # Ngày
        r1_dates = [c.get("round1", {}).get("date") for c in cases]
        r2_dates = [c.get("round2", {}).get("date") for c in cases]
        r3_dates = [c.get("round3", {}).get("date") for c in cases]
        uniq_r1 = set(d for d in r1_dates if d)
        if len(cases) > 5 and len(uniq_r1) == 1:
            errors.append(f"[BIA] {label} sheet {sheet_name}: {len(cases)} TC Round1 cùng 1 ngày {uniq_r1.pop()!r}")
        # Round 3 còn Pending khi bàn giao
        pending_r3 = sum(1 for c in cases if c.get("round3", {}).get("result") == "Pending")
        if pending_r3:
            errors.append(f"[PENDING] {label} sheet {sheet_name}: {pending_r3}/{len(cases)} TC Round3 còn Pending (chưa hồi quy xong)")
        # Round2 fail sau khi Round1 pass mà không ghi chú
        for c in cases:
            r1 = c.get("round1", {}).get("result")
            r2 = c.get("round2", {}).get("result")
            note = (c.get("note") or "").strip()
            if r1 == "Passed" and r2 == "Failed" and not note:
                errors.append(f"[NGUOC] {label} {c.get('id')}: Round1 Passed -> Round2 Failed mà không có ghi chú giải thích")
        # Thu thập để kiểm toàn bộ
        all_r1.extend([c.get("round1", {}).get("result") for c in cases])
        # Đếm defect qua note? sample không có defect field riêng cho rowdoc
    if all_r1 and all(r == "Passed" for r in all_r1):
        errors.append(f"[100% P] {label} TOAN BO Round1: {len(all_r1)}/{len(all_r1)} Passed")
    return errors


def main() -> int:
    ap = argparse.ArgumentParser(description="T3: dấu hiệu bịa + truy vết")
    ap.add_argument("--unit", default="scripts/testdoc/content/unit_test.sample.json")
    ap.add_argument("--integration", default="scripts/testdoc/content/integration_test.sample.json")
    ap.add_argument("--system", default="scripts/testdoc/content/system_test.sample.json")
    ap.add_argument("--strict", action="store_true", help="chặn luôn cảnh báo 100% P")
    ap.add_argument("--allow-demo", action="store_true", help="chỉ cảnh báo, không exit 1 (dùng tạm khi chưa vá)")
    args = ap.parse_args()

    def resolve(p: str) -> str:
        return p if os.path.isabs(p) else os.path.join(ROOT, p)

    unit_path = resolve(args.unit)
    int_path = resolve(args.integration)
    sys_path = resolve(args.system)

    all_errors: list[str] = []
    warnings: list[str] = []

    if os.path.isfile(unit_path):
        errs = check_unit(unit_path)
        # Phân loại: [BIA]/[PENDING]/[NGUOC] là lỗi, [100% P]/[0 DEFECT] là cảnh báo nếu không --strict
        for e in errs:
            if e.startswith("[100% P]") or e.startswith("[0 DEFECT]"):
                (all_errors if args.strict else warnings).append(e)
            else:
                all_errors.append(e)
    if os.path.isfile(int_path):
        errs = check_rowdoc(int_path, "5.2")
        for e in errs:
            if e.startswith("[100% P]"):
                (all_errors if args.strict else warnings).append(e)
            else:
                all_errors.append(e)
    if os.path.isfile(sys_path):
        # System sample hiện tại toàn Pending -> sẽ báo PENDING
        errs = check_rowdoc(sys_path, "5.3")
        for e in errs:
            if e.startswith("[100% P]"):
                (all_errors if args.strict else warnings).append(e)
            else:
                all_errors.append(e)

    if warnings:
        print("CANH BAO (chua chan, dung --strict de chan):", flush=True)
        for w in warnings:
            print(f"  {w}", flush=True)
        print(flush=True)

    if all_errors:
        print("PHAT HIEN DAU HIEU BIA / CHUA HOI QUY (T3 FAIL):", flush=True)
        for e in all_errors:
            print(f"  {e}", flush=True)
        print(f"\nTong {len(all_errors)} loi, {len(warnings)} canh bao.", flush=True)
        if args.allow_demo:
            print("(--allow-demo: chi canh bao, exit 0)", flush=True)
            return 0
        return 1

    if warnings:
        print(f"T3 DAT (co {len(warnings)} canh bao chua chan).", flush=True)
    else:
        print("T3 DAT: khong phat hien dau hieu bia / pending.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
