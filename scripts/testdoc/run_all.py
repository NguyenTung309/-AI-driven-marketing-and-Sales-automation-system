"""Dung lai ca 5 ho tai lieu test roi nghiem thu format - mot lenh duy nhat.

Day la lenh chuan de kiem tra sau khi sua bat ky script nao trong scripts/testdoc.
Tat ca phai in "FORMAT KHOP 100%"; exit != 0 nghia la format da lech khoi mau.

    python scripts/testdoc/run_all.py                  # dung vao thu muc tam
    python scripts/testdoc/run_all.py --out-dir out    # giu lai file de mo xem
    python scripts/testdoc/run_all.py --keep           # khong xoa thu muc tam
    python scripts/testdoc/run_all.py --deliver docs/test/generated   # xuat ban giao nop

Bo mau moi (2026-08) gom 5 file:
  5.1 Report-5.1_Unit Test.xls              (.xls BIFF8, sheet du lieu ModuleName1)
  5.2 Report-5.2_Integration Test.xlsx      (sheet du lieu Feature Name1)
  5.3 Report-5.3_System Test_FRs.xlsx       (sheet du lieu Workflow Name1)
  5.4 Report-5.4_System Test_NFRs.xlsx      (bang phang: Security, Performance)
  5.5 Report-5.5_Acceptance Test Scripts.xlsx (bang phang: UAT, Exploratory)

Phep kiem quan trong nhat la cac ban replica: chung dung lai dung kich thuoc
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

TEMPLATE_51 = "docs/test/Report-5.1_Unit Test.xls"
TEMPLATE_52 = "docs/test/Report-5.2_Integration Test.xlsx"
TEMPLATE_53 = "docs/test/Report-5.3_System Test_FRs.xlsx"
TEMPLATE_54 = "docs/test/Report-5.4_System Test_NFRs.xlsx"
TEMPLATE_55 = "docs/test/Report-5.5_Acceptance Test Scripts.xlsx"

# 5.2/5.3: header 10 dong, case bat dau dong 11, dropdown o cot F/I/L.
ROW_PAIRS = ["--header-rows", "10", "--data-row", "11", "--dv-cols", "F,I,L", "--check-ref"]
# 5.4/5.5: bang phang, header 1 dong, du lieu tu dong 2.
FLAT_PAIRS = ["--same-name", "--header-rows", "1", "--data-row", "2", "--check-ref"]

# Ten file giao nop (--deliver): giu dung ten mau de nguoi cham doi chieu duoc.
DELIVER_NAMES = {
    "unit": "Report-5.1_Unit Test.xls",
    "integration": "Report-5.2_Integration Test.xlsx",
    "system": "Report-5.3_System Test_FRs.xlsx",
    "nfr": "Report-5.4_System Test_NFRs.xlsx",
    "uat": "Report-5.5_Acceptance Test Scripts.xlsx",
}


def build_steps(out_dir: str) -> list[tuple[str, list[str]]]:
    """Cap (mo ta, lenh). Duong dan file ra deu nam trong out_dir."""
    sys_out = os.path.join(out_dir, "system.xlsx")
    int_out = os.path.join(out_dir, "integration.xlsx")
    unit_out = os.path.join(out_dir, "unit.xls")
    replica_out = os.path.join(out_dir, "unit_replica.xls")
    nfr_out = os.path.join(out_dir, "nfr.xlsx")
    uat_out = os.path.join(out_dir, "uat.xlsx")
    # Ban NOI DUNG that cua 5.4/5.5. Khac ban replica: replica chi de so tung o
    # voi mau, con ban sample moi la thu giao khach nen phai qua cong tieng Anh.
    nfr_sample_out = os.path.join(out_dir, "nfr_sample.xlsx")
    uat_sample_out = os.path.join(out_dir, "uat_sample.xlsx")

    rowdoc = os.path.join(HERE, "rowdoc.py")
    unitdoc = os.path.join(HERE, "unitdoc.py")
    flatdoc = os.path.join(HERE, "flatdoc.py")
    cmp_row = os.path.join(HERE, "compare_format.py")
    cmp_unit = os.path.join(HERE, "compare_unit.py")
    check_en = os.path.join(HERE, "check_english.py")
    content = os.path.join(HERE, "content")
    # T3/T4 tren ban full 1850 neu co, khong thi roi ve sample (de pipeline van chay khi chua co full).
    unit_for_evidence = os.path.join(content, "unit_test.full.json")
    if not os.path.isfile(unit_for_evidence):
        unit_for_evidence = os.path.join(content, "unit_test.sample.json")

    return [
        # ---- Dung tai lieu ----
        ("dung 5.3 System Test (FRs)", [
            rowdoc, "--profile", "system",
            "--content", os.path.join(content, "system_test.sample.json"), "--out", sys_out]),
        ("dung 5.2 Integration Test", [
            rowdoc, "--profile", "integration",
            "--content", os.path.join(content, "integration_test.sample.json"), "--out", int_out]),
        ("dung 5.1 Unit Test (sample 14 sheets, kiem format nhanh)", [
            unitdoc, "--content", os.path.join(content, "unit_test.sample.json"),
            "--out", unit_out]),
        ("dung 5.1 ban replica", [
            unitdoc, "--content", os.path.join(content, "unit_test.replica.json"),
            "--out", replica_out]),
        ("dung 5.4 ban replica", [
            flatdoc, "--profile", "nfr",
            "--content", os.path.join(content, "nfr_test.replica.json"), "--out", nfr_out]),
        ("dung 5.5 ban replica", [
            flatdoc, "--profile", "uat",
            "--content", os.path.join(content, "uat_test.replica.json"), "--out", uat_out]),
        ("dung 5.4 System Test (NFRs)", [
            flatdoc, "--profile", "nfr",
            "--content", os.path.join(content, "nfr_test.sample.json"), "--out", nfr_sample_out]),
        ("dung 5.5 Acceptance Test Scripts", [
            flatdoc, "--profile", "uat",
            "--content", os.path.join(content, "uat_test.sample.json"), "--out", uat_sample_out]),

        # Format khop 100% van khong bao gi neu noi dung la tieng Viet - phai
        # co canh cong rieng vi report giao khach bat buoc 100% tieng Anh.
        # Chi soi cac file NOI DUNG that (sample) se giao khach.
        # KHONG soi cac ban replica: chung la gian giao kiem format 1:1, noi dung
        # con nguyen tieng Viet cua mau goc.
        # Sheet SecurityTest la gian giao giu nguyen tu mau (extra_keep_sheets),
        # khong phai noi dung sample soan - bo qua nhu cac ban replica.
        ("nghiem thu ngon ngu (100% tieng Anh)", [
            check_en, "--allow-arg-literals",
            "--file", sys_out, "--file", int_out, "--file", unit_out,
            "--file", nfr_sample_out, "--file", uat_sample_out,
            "--skip-sheet", "SecurityTest"]),

        # ---- Nghiem thu format ----
        ("nghiem thu 5.3", [
            cmp_row, "--base", TEMPLATE_53, "--target", sys_out,
            "--pair", "Workflow Name1=WF01 Onboarding",
            "--pair", "Workflow Name1=WF02 Auto-reply",
            "--pair", "Workflow Name1=WF03 Incident Recovery",
            "--pair", "Workflow Name1=WF04 Lead Pipeline",
            "--pair", "Workflow Name1=WF05 Content Pipeline",
            "--pair", "Cover=Cover", "--pair", "Test Cases=Test Cases",
            "--pair", "Test Statistics=Test Statistics"] + ROW_PAIRS),
        # 5.2: luon so voi sheet chuan 'Feature Name1' cua chinh mau.
        ("nghiem thu 5.2", [
            cmp_row, "--base", TEMPLATE_52, "--target", int_out,
            "--pair", "Feature Name1=Login & Authentication",
            "--pair", "Feature Name1=Omnichannel Inbox",
            "--pair", "Feature Name1=Knowledge Base",
            "--pair", "Feature Name1=Lead Management",
            "--pair", "Feature Name1=Content Pipeline",
            "--pair", "Feature Name1=Agent Orchestration",
            "--pair", "Cover=Cover", "--pair", "Test Cases=Test Cases",
            "--pair", "Test Statistics=Test Statistics"] + ROW_PAIRS),
        # 5.1: sheet du lieu mau la ModuleName1; moi module thuc so voi no.
        # Pipeline kiem mau 14 sheets curate de chay nhanh; ban giao nop dung full 197.
        ("nghiem thu 5.1 sheet module", [
            cmp_unit, "--base", TEMPLATE_51, "--target", unit_out,
            "--pair", "ModuleName1=IsAllowedBaseUrl",
            "--pair", "ModuleName1=NormalizeApiKey",
            "--pair", "ModuleName1=ContentLintCheck",
            "--pair", "ModuleName1=ScenarioMatcher",
            "--pair", "ModuleName1=GoldenHourResolve",
            "--pair", "ModuleName1=RecurrenceCalc",
            "--pair", "ModuleName1=KbSuggestionApprove",
            "--pair", "ModuleName1=ConversationResume",
            "--pair", "ModuleName1=PlanValidator",
            "--pair", "ModuleName1=CostGuard",
            "--pair", "ModuleName1=PublicUrlSafety",
            "--pair", "ModuleName1=ReviewOutcomeParse",
            "--pair", "ModuleName1=LeadScoringWeights",
            "--pair", "ModuleName1=AuthPolicyConstants",
            "--header-rows", "11", "--data-row", "12"]),
        ("nghiem thu 5.1 ban replica (so tung o)", [
            cmp_unit, "--base", TEMPLATE_51, "--target", replica_out,
            "--pair", "ModuleName1=ModuleName1",
            "--header-rows", "11", "--data-row", "12"]),
        ("nghiem thu 5.4 ban replica (so tung o)", [
            cmp_row, "--base", TEMPLATE_54, "--target", nfr_out] + FLAT_PAIRS),
        ("nghiem thu 5.5 ban replica (so tung o)", [
            cmp_row, "--base", TEMPLATE_55, "--target", uat_out] + FLAT_PAIRS),
        ("nghiem thu 5.4 (NFRs, noi dung that)", [
            cmp_row, "--base", TEMPLATE_54, "--target", nfr_sample_out] + FLAT_PAIRS),
        ("nghiem thu 5.5 (UAT, noi dung that)", [
            cmp_row, "--base", TEMPLATE_55, "--target", uat_sample_out] + FLAT_PAIRS),

        # ---- Cong bang chung (T3/T4) ----
        # T4: sau khi va 4 sheet ma, cho chay tren repo that (khong phai sample) -> phai xanh.
        ("cong T4 tren repo thuc (phai DAT - 4 sheet ma da co test)", [
            os.path.join(HERE, "check_coverage_claim.py"),
            "--content", unit_for_evidence]),
        # T3 tren noi dung se giao khach: moi dong ket qua phai truy nguoc duoc ve
        # manifest .trx hoac ve mot lan chay tay co ngay/nguoi cu the, va vong 3 phai chay xong.
        # Khi co ban full 1850 thi kiem tren full; khong co thi kiem tren sample.
        ("cong T3 tren noi dung giao nop (phai DAT)", [
            os.path.join(HERE, "check_evidence.py"),
            "--unit", unit_for_evidence,
            "--integration", os.path.join(content, "integration_test.sample.json"),
            "--system", os.path.join(content, "system_test.sample.json")]),

        # ---- Doi chung am (phai KHAC nhau) ----
        # 2 sheet khac layout that su phai ra hang tram khac biet. Neu buoc nay
        # ra 0 thi bo nghiem thu dang bi mu, moi ket qua tren vo nghia.
        ("doi chung am (phai KHAC nhau)", [
            cmp_unit, "--base", TEMPLATE_51, "--target", TEMPLATE_51,
            "--pair", "ModuleName1=Test Statistics",
            "--header-rows", "11", "--data-row", "12"]),
        # Bo du lieu BIA CO Y trong content/negative: cong T3 chay tren do PHAI do.
        # Khong co buoc nay thi mot cong T3 luon xanh (vi hong) van trong nhu dat.
        ("doi chung am T3 tren du lieu bia (PHAI do)", [
            os.path.join(HERE, "check_evidence.py"),
            "--unit", os.path.join(content, "negative", "unit.fabricated.json"),
            "--integration", os.path.join(content, "negative", "rowdoc.fabricated.json"),
            "--system", os.path.join(content, "negative", "rowdoc.fabricated.json")]),
    ]


def deliver_steps(deliver_dir: str) -> list[tuple[str, list[str]]]:
    """Dung 5 file giao nop (ten chuan) vao deliver_dir. Chi chay khi nghiem thu DAT."""
    rowdoc = os.path.join(HERE, "rowdoc.py")
    unitdoc = os.path.join(HERE, "unitdoc.py")
    flatdoc = os.path.join(HERE, "flatdoc.py")
    content = os.path.join(HERE, "content")

    def out(key: str) -> str:
        return os.path.join(deliver_dir, DELIVER_NAMES[key])

    # 5.1 giao nop uu tien ban full 1850 neu co.
    unit_deliver_content = os.path.join(content, "unit_test.full.json")
    if not os.path.isfile(unit_deliver_content):
        unit_deliver_content = os.path.join(content, "unit_test.sample.json")

    return [
        ("giao nop 5.1 Unit Test", [
            unitdoc, "--content", unit_deliver_content,
            "--out", out("unit")]),
        ("giao nop 5.2 Integration Test", [
            rowdoc, "--profile", "integration",
            "--content", os.path.join(content, "integration_test.sample.json"),
            "--out", out("integration")]),
        ("giao nop 5.3 System Test (FRs)", [
            rowdoc, "--profile", "system",
            "--content", os.path.join(content, "system_test.sample.json"),
            "--out", out("system")]),
        ("giao nop 5.4 System Test (NFRs)", [
            flatdoc, "--profile", "nfr",
            "--content", os.path.join(content, "nfr_test.sample.json"),
            "--out", out("nfr")]),
        ("giao nop 5.5 Acceptance Test Scripts", [
            flatdoc, "--profile", "uat",
            "--content", os.path.join(content, "uat_test.sample.json"),
            "--out", out("uat")]),
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
    ap.add_argument("--deliver", help="thu muc xuat 5 file giao nop (chi khi nghiem thu DAT)")
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

    if args.deliver:
        if failed:
            print("\nBO QUA BUOC GIAO NOP: nghiem thu chua DAT")
        else:
            deliver_dir = args.deliver if os.path.isabs(args.deliver) \
                else os.path.join(ROOT, args.deliver)
            os.makedirs(deliver_dir, exist_ok=True)
            print(f"\nXuat ban giao nop vao: {deliver_dir}")
            for description, command in deliver_steps(deliver_dir):
                if not run(description, command, True):
                    failed += 1

    print("\nTAT CA DEU DAT" if failed == 0 else f"\nCO {failed} BUOC KHONG DAT")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
