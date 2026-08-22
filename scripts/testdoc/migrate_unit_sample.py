"""Chuyen unit_test.sample.json tu luoc do 'bands/marks' (mau 5.1 cu, dang
ma tran quyet dinh) sang luoc do hang-ngang cua mau 5.1 moi (Report-5.1_Unit
Test.xls: modules -> methods -> cases voi input/expected).

Mau cu ghi moi test case bang cach danh dau (mark) chi so gia tri trong tung
nhom dieu kien (Condition) va nhom xac nhan (Confirm). Mau moi doi hoi cot
'Input parameters' + 'Expected Results' dang van ban. Script nay bung marks
thanh chuoi doc duoc, giu nguyen 100% ngu nghia nghiep vu that cua Clawbot:

    Condition group  -> dong 'label = <gia tri tai chi so mark>' trong Input
    Confirm  group   -> dong '<label>: <gia tri tai chi so mark>' trong Expected

Mark thieu cho 1 label nghia la 'khong ap dung' cho case do -> bo qua.

    python scripts/testdoc/migrate_unit_sample.py \
        --in scripts/testdoc/content/unit_test.sample.json \
        --out scripts/testdoc/content/unit_test.sample.json
"""

from __future__ import annotations

import argparse
import json
import sys

# Type mau cu (1 ky tu) -> nhan day du cot 'Type' cua mau moi.
TYPE_MAP = {"N": "Normal", "A": "Abnormal", "B": "Boundary"}
# Ket qua mau cu -> tu vung dropdown Round cua mau moi (T2:T4 = Passed/Pending/N/A).
RESULT_MAP = {"P": "Passed", "F": "Failed", "": "Pending", None: "Pending"}


def band_groups(method: dict, band_name: str) -> list[dict]:
    for band in method.get("bands", []):
        if band.get("name") == band_name:
            return band.get("groups", [])
    return []


def value_at(group: dict, index) -> str | None:
    """Gia tri tai chi so mark; None neu chi so ngoai pham vi (du lieu hong)."""
    values = group.get("values", [])
    if not isinstance(index, int) or index < 0 or index >= len(values):
        return None
    return str(values[index]).strip()


def build_input(method: dict, marks: dict) -> str:
    """Dong 'label = value' cho tung nhom Condition co mark o case nay."""
    lines: list[str] = []
    for group in band_groups(method, "Condition"):
        label = group.get("label", "")
        if label not in marks:
            continue  # khong ap dung cho case nay
        value = value_at(group, marks[label])
        if value is None:
            continue
        lines.append(f"{label} = {value}")
    return "\n".join(lines)


def build_expected(method: dict, marks: dict) -> str:
    """Dong '<label>: value' cho tung nhom Confirm co mark o case nay."""
    lines: list[str] = []
    for group in band_groups(method, "Confirm"):
        label = group.get("label", "")
        if label not in marks:
            continue
        value = value_at(group, marks[label])
        if value is None:
            continue
        lines.append(f"{label}: {value}")
    return "\n".join(lines)


def convert_case(method: dict, case: dict) -> dict:
    marks = case.get("marks", {})
    # Sample la ban demo bia (de cong doi-chung-am bat duoc): round1 = Passed het,
    # 0 defect -> gate check_evidence phai keu [100% P]/[0 DEFECT]. KHONG ghi
    # ngay/tester: o Test date de trong dung nhu mau (General) nen format khop
    # 1:1; ngay/tester that thuoc pha bang chung (chay test that), khong phai day.
    return {
        "id": case.get("id", ""),
        "precondition": method.get("precondition", "N/A") or "N/A",
        "input": build_input(method, marks),
        "expected": build_expected(method, marks),
        "type": TYPE_MAP.get(case.get("type", "N"), "Normal"),
        "round1": {"result": RESULT_MAP.get(case.get("result"), "Pending")},
        "round2": {"result": "Pending"},
        "round3": {"result": "Pending"},
        "defect": case.get("defect", ""),
        "note": "",
    }


def convert(old: dict) -> dict:
    modules = []
    for method in old.get("methods", []):
        cases = [convert_case(method, c) for c in method.get("cases", [])]
        modules.append({
            "sheet": method["sheet"],
            "module": method.get("module", method["sheet"]),
            "description": method.get("description", ""),
            "precondition": method.get("precondition", ""),
            # Mot sheet = mot module chua dung 1 method (giu nguyen 14 sheet cu).
            "methods": [{
                "method": method.get("method", method["sheet"]),
                "requirement": method.get("requirement", ""),
                "cases": cases,
            }],
        })
    return {
        "cover": old.get("cover", {}),
        "environment": old.get("environment", ""),
        "modules": modules,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description="Migrate unit sample sang luoc do hang-ngang")
    ap.add_argument("--in", dest="src", required=True)
    ap.add_argument("--out", dest="dst", required=True)
    args = ap.parse_args()

    with open(args.src, encoding="utf-8") as f:
        old = json.load(f)

    if "modules" in old and "methods" not in old:
        print("Da o luoc do moi (co 'modules'), khong can migrate.")
        return 0

    new = convert(old)
    with open(args.dst, "w", encoding="utf-8") as f:
        json.dump(new, f, ensure_ascii=False, indent=2)
    total_cases = sum(len(m["methods"][0]["cases"]) for m in new["modules"])
    print(f"Xong: {len(new['modules'])} module, {total_cases} case -> {args.dst}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
