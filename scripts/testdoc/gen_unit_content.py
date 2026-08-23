"""Sinh noi dung 5.1 cho TOAN BO test tu dong trong repo, tu manifest .trx.

Ban curated viet tay chi phu 14 class / 172 UTCID trong khi repo co 197 class /
1850 test result, va con thieu ngay trong chinh cac class no mo ta (vi du
ContentChainGatesTests: 10 dong tai lieu / 96 test that). Script nay xuat het,
va moi dong deu truy nguoc duoc ve mot testFullName co that trong .trx.

Nguon cua tung cot:
  - sheet          = ten class test (cat con 31 ky tu cho hop le Excel)
  - dong nhom      = ten method test
  - Input          = tham so THAT cua Theory (xUnit ghi vao ten hien thi trong
                     .trx); voi [Fact] khong co tham so thi lay tu doan giua cua
                     ten method theo quy uoc Method_DieuKien_KetQua
  - Expected       = doan cuoi cua ten method, dien giai ra cau tieng Anh
  - Type           = suy tu tu khoa trong ten method (bien / bat thuong / binh thuong)
  - Round 1 / 2    = ket qua that trong manifest cua tung lan chay
  - Round 3        = Pending (chua chay)

CHO Y: Input/Expected cua [Fact] la DIEN GIAI TEN TEST, khong phai doc than ham.
Ten test la loi khai cua chinh nguoi viet test nen dung duoc, nhung no khong chi
tiet bang mot dong nguoi doc code viet ra.

    python scripts/testdoc/gen_unit_content.py
        --round1 docs/test/evidence/2026-08-19_R0/manifest.json
        --round2 docs/test/evidence/2026-08-20_R2/manifest.json
        --out scripts/testdoc/content/unit_test.full.json
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
HERE = os.path.dirname(os.path.abspath(__file__))

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TESTER = "Automated suite"
MAX_SHEET_NAME = 31

# Type suy tu ten test. Kiem BIEN truoc: mot ten vua co "Max" vua co "Throws"
# thi cai dang duoc thu la gia tri bien, con nem loi chi la cach no bao loi.
BOUNDARY_TOKENS = (
    "max", "min", "zero", "limit", "clamp", "boundary", "exceed", "toolong",
    "toolarge", "toobig", "overflow", "cap", "threshold", "truncat",
    "firstpage", "lastpage", "singleitem", "onlyone",
)
ABNORMAL_TOKENS = (
    "throw", "invalid", "reject", "fail", "error", "unauthori", "forbidden",
    "notfound", "conflict", "badrequest", "malformed", "unknown", "missing",
    "denied", "unsupported", "corrupt", "garbage", "expired", "disallow",
    "wrong", "unavailable", "unreachable", "null", "empty", "blank",
    "whitespace", "duplicate", "refus", "blocked", "stale", "mismatch",
)


# Viet tat phai giu chu hoa: ha xuong "dns", "http", "sql" doc rat kho chiu.
ACRONYMS = frozenset("""
ai api cli cors crud csrf csv db dns dto grpc guid hmac html http https id
imap io json jwt kb llm otp pii rbac rpc rss smtp sql ssl ssrf tls ui uri url
utc uuid xss
""".split())


def camel_words(text: str) -> str:
    text = re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", text)
    text = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1 \2", text)
    # "FewerThan3History" khong tach chu voi so se doc thanh "than3".
    text = re.sub(r"([A-Za-z])([0-9])", r"\1 \2", text)
    text = re.sub(r"([0-9])([A-Za-z])", r"\1 \2", text)
    return re.sub(r"\s+", " ", text).strip()


def humanise(segment: str) -> str:
    """PascalCase -> cau tieng Anh doc duoc, giu nguyen cac tu viet tat."""
    words = camel_words(segment).split()
    if not words:
        return ""

    def norm(word: str) -> str:
        return word.upper() if word.isupper() or word.lower() in ACRONYMS else word.lower()

    head = words[0]
    head = head.upper() if head.lower() in ACRONYMS else head[:1].upper() + head[1:]
    return " ".join([head] + [norm(w) for w in words[1:]])


def split_arguments(raw: str) -> list[str]:
    """Tach danh sach tham so, bo qua dau phay nam trong chuoi hoac trong ngoac."""
    parts, buf, depth, in_string, escaped = [], "", 0, False, False
    for ch in raw:
        if in_string:
            buf += ch
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
            buf += ch
            continue
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append(buf.strip())
            buf = ""
            continue
        buf += ch
    if buf.strip():
        parts.append(buf.strip())
    return parts


def format_arguments(raw: str) -> str:
    """Doi 'code: "x", n: 3' thanh moi tham so mot dong dang 'code = "x"'."""
    lines = []
    for part in split_arguments(raw):
        match = re.match(r"^([A-Za-z_]\w*)\s*:\s*(.*)$", part, re.S)
        lines.append(f"{match.group(1)} = {match.group(2)}" if match else part)
    return "\n".join(lines)


def classify_type(method_name: str) -> str:
    low = method_name.lower()
    if any(tok in low for tok in BOUNDARY_TOKENS):
        return "Boundary"
    if any(tok in low for tok in ABNORMAL_TOKENS):
        return "Abnormal"
    return "Normal"


def describe(method_name: str) -> tuple[str, str]:
    """Tra ve (dieu kien, ket qua mong doi) theo quy uoc Method_DieuKien_KetQua."""
    segments = [s for s in method_name.split("_") if s]
    if len(segments) >= 3:
        condition = " and ".join(humanise(s).lower() for s in segments[1:-1])
        return condition[:1].upper() + condition[1:], humanise(segments[-1])
    if len(segments) == 2:
        return "", humanise(segments[1])
    return "", humanise(segments[0]) if segments else ""


def parse_full_name(full_name: str) -> tuple[str, str, str]:
    """Tach 'Ns.Class.Method(args)' thanh (Ns.Class, Method, args)."""
    head, args = full_name, ""
    open_paren = full_name.find("(")
    if open_paren != -1 and full_name.endswith(")"):
        head, args = full_name[:open_paren], full_name[open_paren + 1:-1]
    class_name, _, method = head.rpartition(".")
    return class_name, method, args


def safe_sheet_names(class_names: list[str]) -> dict[str, str]:
    """Ten class -> ten sheet: bo hau to Tests, toi da 31 ky tu, khong trung."""
    taken: set[str] = set()
    mapping: dict[str, str] = {}
    for fqcn in class_names:
        short = fqcn.rsplit(".", 1)[-1]
        for suffix in ("Tests", "Test"):
            if short.endswith(suffix) and len(short) > len(suffix):
                short = short[: -len(suffix)]
                break
        base = re.sub(r"[\[\]:*?/\\]", "", short)[:MAX_SHEET_NAME] or "Sheet"
        name, counter = base, 2
        while name.lower() in taken:
            tail = f"_{counter}"
            name = base[: MAX_SHEET_NAME - len(tail)] + tail
            counter += 1
        taken.add(name.lower())
        mapping[fqcn] = name
    return mapping


def load_run(path: str) -> tuple[dict[str, str], str]:
    """Doc manifest, tra ve ({testFullName: mappedResult}, ngay chay)."""
    full = path if os.path.isabs(path) else os.path.join(ROOT, path)
    data = json.load(open(full, encoding="utf-8"))
    results = {e["testFullName"]: e.get("mappedResult") for e in data.get("entries", [])}
    return results, (data.get("createdAt") or "")[:10]


def round_payload(results: dict, full_name: str, date: str) -> dict:
    mapped = results.get(full_name)
    if mapped is None:
        return {"result": "Pending"}
    if mapped == "P":
        return {"result": "Passed", "date": date, "tester": TESTER}
    return {"result": "N/A", "date": date, "tester": TESTER}


def build_module(class_name: str, methods: dict, sheet: str,
                 r1_results: dict, r1_date: str, r1_path: str,
                 r2_results: dict, r2_date: str, r2_path: str) -> dict:
    methods_out = []
    index = 0
    class_in_r1 = False
    for method in sorted(methods):
        condition, expected = describe(method)
        fallback = "Behaves as the test name states"
        cases = []
        for full_name in sorted(methods[method]):
            _, _, args = parse_full_name(full_name)
            if args:
                input_text = format_arguments(args)
            else:
                input_text = condition or "Default arrangement set up in the test body"
                input_text += "\n(the test method takes no arguments)"
            index += 1
            r1 = round_payload(r1_results, full_name, r1_date)
            r2 = round_payload(r2_results, full_name, r2_date)
            class_in_r1 = class_in_r1 or r1.get("result") == "Passed"
            note = ""
            if r1["result"] == "Pending":
                note = "Not present in the round 1 run; first recorded in round 2."
            if r2["result"] == "N/A":
                note = "Skipped by the suite in this run, so no result was produced."
            cases.append({
                "id": f"UTCID{index:02d}",
                "precondition": "N/A",
                "input": input_text,
                "expected": expected or fallback,
                "type": classify_type(method),
                "round1": r1,
                "round2": r2,
                "round3": {"result": "Pending"},
                "defect": "",
                "note": note,
            })
        outcome = expected or fallback
        requirement = f"{humanise(method.split('_')[0])}: {outcome[:1].lower()}{outcome[1:]}"
        requirement += f" when {condition[:1].lower()}{condition[1:]}." if condition else "."
        methods_out.append({"method": method, "requirement": requirement, "cases": cases})

    evidence = {"testClasses": [class_name], "round2": r2_path}
    if class_in_r1:
        evidence["round1"] = r1_path
    short = class_name.rpartition(".")[2]
    return {
        "sheet": sheet,
        "module": short,
        "description": f"Automated unit tests in {class_name}",
        "precondition": f"The {short} fixture is constructed by the xUnit runner",
        "methods": methods_out,
        "evidence": evidence,
    }


def build(round1_path: str, round2_path: str, base_content: str) -> dict:
    r1_results, r1_date = load_run(round1_path)
    r2_results, r2_date = load_run(round2_path)

    by_class: dict[str, dict[str, list[str]]] = {}
    for full_name in r2_results:
        class_name, method, _ = parse_full_name(full_name)
        by_class.setdefault(class_name, {}).setdefault(method, []).append(full_name)

    sheets = safe_sheet_names(sorted(by_class))
    base = json.load(open(base_content, encoding="utf-8"))

    modules = [
        build_module(class_name, by_class[class_name], sheets[class_name],
                     r1_results, r1_date, round1_path,
                     r2_results, r2_date, round2_path)
        for class_name in sorted(by_class)
    ]
    total_cases = sum(len(m["cases"]) for mod in modules for m in mod["methods"])

    project_counts: dict[str, int] = {}
    for full_name in r2_results:
        project = full_name.split(".Tests.")[0] + ".Tests"
        project_counts[project] = project_counts.get(project, 0) + 1
    note = ", ".join(f"{name} ({count})" for name, count in sorted(project_counts.items()))

    cover = dict(base["cover"])
    cover["statistics_note"] = (
        f"All {total_cases} automated test results across {len(modules)} test classes: {note}")
    cover["issue_date"] = r2_date

    return {
        "_doc": [
            "SINH TU MAY - dung scripts/testdoc/gen_unit_content.py, dung sua tay.",
            "Moi UTCID tuong ung mot testFullName co that trong manifest .trx.",
            "Input cua Theory la tham so that; Input/Expected cua Fact dien giai tu ten test.",
        ],
        "cover": cover,
        "environment": base.get("environment", ""),
        "modules": modules,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description="Sinh noi dung 5.1 tu manifest .trx")
    ap.add_argument("--round1", default="docs/test/evidence/2026-08-19_R0/manifest.json")
    ap.add_argument("--round2", default="docs/test/evidence/2026-08-20_R2/manifest.json")
    ap.add_argument("--base", default=os.path.join(HERE, "content", "unit_test.sample.json"),
                    help="lay cover + environment tu file nay")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    data = build(args.round1, args.round2, args.base)
    out = args.out if os.path.isabs(args.out) else os.path.join(ROOT, args.out)
    json.dump(data, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=2)

    cases = sum(len(m["cases"]) for mod in data["modules"] for m in mod["methods"])
    methods = sum(len(mod["methods"]) for mod in data["modules"])
    print(f"Ghi {out}")
    print(f"  {len(data['modules'])} sheet / {methods} method / {cases} UTCID")
    return 0


if __name__ == "__main__":
    sys.exit(main())
