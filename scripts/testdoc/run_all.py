"""Dung lai ca 3 ho tai lieu test roi nghiem thu format - mot lenh duy nhat.

Day la lenh chuan de kiem tra sau khi sua bat ky script nao trong scripts/testdoc.
Tat ca phai in "FORMAT KHOP 100%"; exit != 0 nghia la format da lech khoi mau.

    python scripts/testdoc/run_all.py                  # dung vao thu muc tam
    python scripts/testdoc/run_all.py --out-dir out    # giu lai file de mo xem
    python scripts/testdoc/run_all.py --keep           # khong xoa thu muc tam

Phep kiem quan trong nhat la ban replica cua 5.1: no dung lai dung kich thuoc
sheet mau nen anh xa la 1:1, tuc la so TUNG O voi mau.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
HERE = os.path.dirname(os.path.abspath(__file__))

TEMPLATE_51 = "docs/test/Report5.1_Unit Test.xlsx"
TEMPLATE_52 = "docs/test/Report5.2_Integration Test.xlsx"
TEMPLATE_53 = "docs/test/Report5.3_System Test.xlsx"

ROW_PAIRS = ["--header-rows", "10", "--data-row", "11", "--dv-cols", "F,I,L", "--check-ref"]


def build_steps(out_dir: str) -> list[tuple[str, list[str]]]:
    """Cap (mo ta, lenh). Duong dan file ra deu nam trong out_dir."""
    sys_out = os.path.join(out_dir, "system.xlsx")
    int_out = os.path.join(out_dir, "integration.xlsx")
    unit_out = os.path.join(out_dir, "unit.xlsx")
    replica_out = os.path.join(out_dir, "unit_replica.xlsx")

    rowdoc = os.path.join(HERE, "rowdoc.py")
    unitdoc = os.path.join(HERE, "unitdoc.py")
    cmp_row = os.path.join(HERE, "compare_format.py")
    cmp_unit = os.path.join(HERE, "compare_unit.py")
    check_en = os.path.join(HERE, "check_english.py")
    content = os.path.join(HERE, "content")

    return [
        ("dung 5.3 System Test", [
            rowdoc, "--profile", "system",
            "--content", os.path.join(content, "system_test.sample.json"), "--out", sys_out]),
        ("dung 5.2 Integration Test", [
            rowdoc, "--profile", "integration",
            "--content", os.path.join(content, "integration_test.sample.json"), "--out", int_out]),
        ("dung 5.1 Unit Test", [
            unitdoc, "--content", os.path.join(content, "unit_test.sample.json"),
            "--out", unit_out]),
        ("dung 5.1 ban replica", [
            unitdoc, "--content", os.path.join(content, "unit_test.replica.json"),
            "--out", replica_out]),

        # Format khop 100% van khong bao gi neu noi dung la tieng Viet - phai
        # co canh cong rieng vi report giao khach bat buoc 100% tieng Anh.
        ("nghiem thu ngon ngu (100% tieng Anh)", [
            check_en, "--file", sys_out, "--file", int_out,
            "--file", unit_out, "--file", replica_out]),

        ("nghiem thu 5.3", [
            cmp_row, "--base", TEMPLATE_53, "--target", sys_out,
            "--pair", "Workflow Name1=WF01 Onboarding",
            "--pair", "Workflow Name1=WF02 Auto-reply",
            "--pair", "Workflow Name1=WF03 Incident Recovery",
            "--pair", "Workflow Name1=WF04 Lead Pipeline",
            "--pair", "Workflow Name1=WF05 Content Pipeline",
            "--pair", "Cover=Cover", "--pair", "Test Cases=Test Cases",
            "--pair", "Test Statistics=Test Statistics"] + ROW_PAIRS),
        # 5.2: luon so voi sheet chuan 'Login & Authentication'. Cac sheet module
        # khac trong chinh file mau da bi lech format nen khong dung lam chuan.
        ("nghiem thu 5.2", [
            cmp_row, "--base", TEMPLATE_52, "--target", int_out,
            "--pair", "Login & Authentication=Login & Authentication",
            "--pair", "Login & Authentication=Omnichannel Inbox",
            "--pair", "Login & Authentication=Knowledge Base",
            "--pair", "Login & Authentication=Lead Management",
            "--pair", "Login & Authentication=Content Pipeline",
            "--pair", "Login & Authentication=Agent Orchestration",
            "--pair", "Cover=Cover", "--pair", "Test Cases=Test Cases",
            "--pair", "Test Statistics=Test Statistics"] + ROW_PAIRS),
        ("nghiem thu 5.1 sheet method", [
            cmp_unit, "--base", TEMPLATE_51, "--target", unit_out,
            "--pair", "methodName1=IsAllowedBaseUrl",
            "--pair", "methodName1=NormalizeApiKey",
            "--pair", "methodName1=ContentLintCheck",
            "--pair", "methodName1=ScenarioMatcher",
            "--pair", "methodName1=GoldenHourResolve",
            "--pair", "methodName1=RecurrenceCalc",
            "--pair", "methodName1=KbSuggestionApprove",
            "--pair", "methodName1=ConversationResume",
            "--pair", "methodName1=PlanValidator",
            "--pair", "methodName1=CostGuard",
            "--pair", "methodName1=PublicUrlSafety",
            "--pair", "methodName1=ReviewOutcomeParse",
            "--pair", "methodName1=LeadScoringWeights",
            "--pair", "methodName1=AuthPolicyConstants"]),
        ("nghiem thu 5.1 ban replica (so tung o)", [
            cmp_unit, "--base", TEMPLATE_51, "--target", replica_out,
            "--pair", "methodName1=methodName1"]),
        ("nghiem thu 5.1 MethodList", [
            cmp_row, "--base", TEMPLATE_51, "--target", unit_out,
            "--pair", "MethodList=MethodList", "--header-rows", "8", "--data-row", "9"]),

        # Doi chung am: 2 sheet khac nhau that su phai ra hang tram khac biet.
        # Neu buoc nay ra 0 thi bo nghiem thu dang bi mu, moi ket qua tren vo nghia.
        ("doi chung am (phai KHAC nhau)", [
            cmp_unit, "--base", TEMPLATE_51, "--target", "docs/test/Report_Unit_Test.xlsx",
            "--pair", "methodName1=LanguageMismatch", "--limit", "0"]),
    ]


def run(description: str, args: list[str], expect_ok: bool) -> bool:
    env = dict(os.environ, PYTHONIOENCODING="utf-8")  # cp1252 lam vo print tieng Viet
    proc = subprocess.run([sys.executable] + args, cwd=ROOT, env=env,
                          capture_output=True, text=True, encoding="utf-8", errors="replace")
    ok = (proc.returncode == 0) if expect_ok else (proc.returncode != 0)
    print(f"[{'OK ' if ok else 'LOI'}] {description}")
    if not ok:
        print((proc.stdout or "").strip()[-3000:])
        print((proc.stderr or "").strip()[-3000:])
    return ok


def main() -> int:
    ap = argparse.ArgumentParser(description="Dung + nghiem thu ca bo tai lieu test")
    ap.add_argument("--out-dir", help="thu muc chua file dung ra (mac dinh: thu muc tam)")
    ap.add_argument("--keep", action="store_true", help="giu lai thu muc tam")
    args = ap.parse_args()

    out_dir = args.out_dir or tempfile.mkdtemp(prefix="testdoc-")
    os.makedirs(out_dir, exist_ok=True)
    print(f"Thu muc ket qua: {out_dir}\n")

    failed = 0
    try:
        for description, command in build_steps(out_dir):
            expect_ok = not description.startswith("doi chung am")
            if not run(description, command, expect_ok):
                failed += 1
    finally:
        if not args.out_dir and not args.keep:
            shutil.rmtree(out_dir, ignore_errors=True)

    print("\nTAT CA DEU DAT" if failed == 0 else f"\nCO {failed} BUOC KHONG DAT")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
