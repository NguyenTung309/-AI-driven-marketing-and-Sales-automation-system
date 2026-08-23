"""Dung tai lieu Unit Test (5.1) tu chinh file mau .xls, bang Excel COM.

Mau moi (Report-5.1_Unit Test.xls) da doi han so voi ban ma-tran-ngang cu:
moi sheet module (ModuleName1) gio la BANG-THEO-DONG giong het 5.2/5.3:
  - dong nhom = ten method (chi co cot A)
  - dong case = UTCxxx voi cot B..P (B=Pre-conditions, C=Input, D=Expected,
    E=Type, F/G/H=Round1, I/J/K=Round2, L/M/N=Round3, O=Defect IDs, P=Note)
  - phia tren la khoi thong ke rieng cua sheet (dong 2-9): So TC, Testing Round,
    Testing Type -- toan cong thuc COUNTIF, khong dung tay.

Vi cau truc dong trung 5.2/5.3 nen tai su dung nguyen bo may nan-dong da kiem
chung cua rowdoc.py; unitdoc chi lo phan khac biet: cot E=Type, khoi thong ke
dau sheet, va sheet Test Statistics noi cong thuc kieu khac.

File mau la .xls that (BIFF8/OLE2). Excel COM mo-roi-Save giu nguyen dinh dang
BIFF8 (da kiem: mo-luu-khong-sua khac biet = 0), nen khong can SaveAs.

Cach dung:
    python scripts/testdoc/unitdoc.py \
        --content scripts/testdoc/content/unit_test.sample.json \
        --out "out/Report_Unit_Test.xls"
"""

from __future__ import annotations

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import xl  # noqa: E402
import rowdoc  # noqa: E402
from rowdoc import (  # noqa: E402
    RowDocProfile,
    ROUND_COLS,
    cell_text,
    count_block,
    detect_row_layout,
    fill_cover,
    fill_test_case_list,
    fit_row_layout,
    parse_date,
)

TEMPLATE = "docs/test/Report-5.1_Unit Test.xls"
SOURCE_SHEET = "ModuleName1"
KEEP_SHEETS = ("Cover", "Test Cases", "Test Statistics")

# Khoi thong ke tren dau sheet module: nhan (B4/C4/D4, B8/C8/D8) giu nguyen,
# chi viet lai vung tham chieu COUNTIF cho khop so dong that.
ROUND_RESULT_ROWS = {"round1": 5, "round2": 6, "round3": 7}
TYPE_COUNT_ROW = 9

# Test Statistics: chon vong lam nguon Passed/Failed/Untested (mac dinh Round 1
# vi bang chung that hien chi co 1 vong). Type count (Normal/Abnormal/Boundary)
# khong phu thuoc vong -> luon lay tu dong 9.
STAT_PASSFAIL_ROW = {1: 5, 2: 6, 3: 7}

# Bo cuc dong cua sheet module: trung 5.2/5.3 (case bat dau dong 12, cot cuoi P).
# clear_col=P de khong bao gio dung toi cot T (T2:T4 la nguon dropdown ket qua).
UNIT_PROFILE = RowDocProfile(
    template=TEMPLATE,
    source_sheet=SOURCE_SHEET,
    stat_issue_date="F6",
    stat_round_row=5,
    tc_index_float=True,
    data_start=12,
    last_col="P",
    clear_col="P",
    group_cols=("B", "C", "D", "E"),
    tc_start=9,
    stat_start=12,
)


def find_label_row_b(ws, label: str, start_row: int, limit: int = 12) -> int | None:
    """Tim dong co nhan o cot B (Sub total / Test coverage...) - 5.1 de nhan o B."""
    wanted = label.strip().lower()
    for offset in range(limit):
        if cell_text(ws, f"B{start_row + offset}").lower() == wanted:
            return start_row + offset
    return None


def fill_module_sheet(app, ws, module: dict, profile: RowDocProfile) -> int:
    """Ghi 1 sheet module, tra ve so UTC. Cot E=Type dieu khien Testing Type."""
    xl.set_text(ws, "B2", module.get("module", module.get("name", "")))

    needed, payloads = [], []
    for method in module.get("methods", []):
        needed.append("group")
        payloads.append(("group", method.get("method", "")))
        for case in method.get("cases", []):
            needed.append("case")
            payloads.append(("case", case))
    if not any(kind == "case" for kind, _ in payloads):
        raise ValueError(f"module {module.get('module')!r} khong co UTC nao")

    fit_row_layout(app, ws, profile.data_start, needed,
                   detect_row_layout(ws, profile), profile)

    last_row = profile.data_start + len(needed) - 1
    ws.Range(f"A{profile.data_start}:{profile.last_col}{last_row}").ClearContents()

    case_count = 0
    for offset, (kind, payload) in enumerate(payloads):
        row = profile.data_start + offset
        if kind == "group":
            xl.set_text(ws, f"A{row}", payload)
            continue
        case_count += 1
        xl.set_text(ws, f"A{row}", payload["id"])
        xl.set_text(ws, f"B{row}", payload.get("precondition", ""))
        xl.set_text(ws, f"C{row}", payload.get("input", ""))
        xl.set_text(ws, f"D{row}", payload.get("expected", ""))
        xl.set_text(ws, f"E{row}", payload.get("type", ""))
        for round_key, result_col, date_col, tester_col in ROUND_COLS:
            data = payload.get(round_key) or {}
            xl.set_text(ws, f"{result_col}{row}", data.get("result", "Pending"))
            round_date = parse_date(data.get("date"))
            if round_date:
                xl.set_date(ws, f"{date_col}{row}", round_date)
            else:
                ws.Range(f"{date_col}{row}").ClearContents()
            xl.set_text(ws, f"{tester_col}{row}", data.get("tester", ""))
        xl.set_text(ws, f"O{row}", payload.get("defect", ""))
        xl.set_text(ws, f"P{row}", payload.get("note", ""))

    _rewrite_block_formulas(ws, profile.data_start, last_row)
    return case_count


def _rewrite_block_formulas(ws, first_row: int, last_row: int) -> None:
    """Viet lai vung tham chieu COUNTIF cua khoi thong ke dau sheet theo so dong that.

    Mau de vung co dinh (F11:F1003...) chay qua dong tieu de va co the lech khi
    Excel dieu chinh bien vung luc chen/xoa dong. Viet lai theo dung khoi du lieu
    de so lieu luon dung, khong phu thuoc bien vung mac dinh cua mau.
    """
    # So TC = dem UTC* trong cot A (nhan method khong tinh vao).
    xl.set_formula(ws, "B3", f'=COUNTIF($A${first_row}:$A${last_row},"UTC*")')
    for round_key, result_col, _date, _tester in ROUND_COLS:
        block_row = ROUND_RESULT_ROWS[round_key]
        for out_col, label_col in (("B", "B"), ("C", "C"), ("D", "D")):
            xl.set_formula(
                ws, f"{out_col}{block_row}",
                f'=COUNTIF(${result_col}${first_row}:${result_col}${last_row},{label_col}4)')
    for out_col in ("B", "C", "D"):
        xl.set_formula(
            ws, f"{out_col}{TYPE_COUNT_ROW}",
            f'=COUNTIF($E${first_row}:$E${last_row},{out_col}8)')


def fill_statistics(app, ws, cover: dict, module_names: list[str], profile: RowDocProfile,
                    stat_round: int) -> None:
    """Test Statistics: moi dong keo so lieu tu khoi thong ke cua 1 sheet module."""
    xl.set_text(ws, "G3", cover.get("creator", ""))
    xl.set_text(ws, "G4", cover.get("creator", ""))
    xl.set_text(ws, "G5", cover.get("reviewer", ""))
    issue_date = parse_date(cover.get("issue_date"))
    if issue_date:
        xl.set_date(ws, profile.stat_issue_date, issue_date)
    xl.set_text(ws, "B7", cover.get("statistics_note", ""))

    template_rows = count_block(ws, profile.stat_start, "B")
    fit_row_layout(app, ws, profile.stat_start, ["case"] * len(module_names),
                   ["case"] * template_rows, profile)

    passfail_row = STAT_PASSFAIL_ROW[stat_round]
    for offset, name in enumerate(module_names):
        row = profile.stat_start + offset
        ref = f"'{name}'"
        xl.set_value(ws, f"A{row}", float(offset + 1))
        xl.set_formula(ws, f"B{row}", f"={ref}!B2")
        # Passed / Failed / Untested lay tu vong da chon (mac dinh Round 1).
        for out_col, src_col in zip("CDE", "BCD"):
            xl.set_formula(ws, f"{out_col}{row}", f"={ref}!{src_col}{passfail_row}")
        # N / A / B lay tu dong Testing Type (khong phu thuoc vong).
        for out_col, src_col in zip("FGH", "BCD"):
            xl.set_formula(ws, f"{out_col}{row}", f"={ref}!{src_col}{TYPE_COUNT_ROW}")
        xl.set_formula(ws, f"I{row}", f"={ref}!B3")

    _fix_statistics_formulas(ws, profile.stat_start, profile.stat_start + len(module_names) - 1)


def _fix_statistics_formulas(ws, first_row: int, last_row: int) -> None:
    """Viet lai Sub total + cac dong ty le sau khi so dong thay doi.

    Mau de SUM tren dung khoang dong mau; khi so module khac di, viet lai theo
    khoi du lieu that de khong con #REF! va tinh dung.
    """
    sub_row = find_label_row_b(ws, "Sub total", last_row + 1)
    if sub_row is None:
        raise ValueError(f"khong thay dong 'Sub total' sau dong {last_row} tren {ws.Name}")
    for col in "CDEFGHI":
        xl.set_formula(ws, f"{col}{sub_row}", f"=SUM({col}{first_row}:{col}{last_row})")
    ratios = (
        ("Test coverage", f"=(C{sub_row}+D{sub_row})*100/(I{sub_row})"),
        ("Test successful coverage", f"=C{sub_row}*100/(I{sub_row})"),
        ("Normal case", f"=F{sub_row}*100/I{sub_row}"),
        ("Abnormal case", f"=G{sub_row}*100/I{sub_row}"),
        ("Boundary case", f"=H{sub_row}*100/I{sub_row}"),
    )
    for label, formula in ratios:
        row = find_label_row_b(ws, label, sub_row + 1)
        if row is not None:
            xl.set_formula(ws, f"D{row}", formula)


def build(content_path: str, out_path: str, template: str | None = None,
          stat_round: int = 1) -> list[str]:
    with open(content_path, encoding="utf-8") as fh:
        content = json.load(fh)

    modules = content.get("modules") or []
    if not modules:
        raise ValueError("content thieu 'modules'")
    sheet_names = [xl.safe_sheet_name(item["sheet"]) for item in modules]
    if len(set(sheet_names)) != len(sheet_names):
        raise ValueError("ten sheet module bi trung sau khi cat con 31 ky tu")

    profile = rowdoc.dataclasses.replace(UNIT_PROFILE, template=template or TEMPLATE)
    with xl.workbook_from_template(profile.template, out_path) as (app, wb):
        template_sheets = [
            wb.Worksheets(i + 1).Name
            for i in range(wb.Worksheets.Count)
            if wb.Worksheets(i + 1).Name not in KEEP_SHEETS
        ]
        if SOURCE_SHEET not in template_sheets:
            raise ValueError(f"mau thieu sheet {SOURCE_SHEET!r}")

        # Doi ten sheet khuon truoc: ten sheet ket qua co the trung ten sheet khuon.
        temp_names = {name: f"~tmp{i}" for i, name in enumerate(template_sheets, 1)}
        for name, temp in temp_names.items():
            wb.Worksheets(name).Name = temp

        anchor = temp_names[template_sheets[-1]]
        for name in sheet_names:
            anchor = xl.clone_sheet(wb, temp_names[SOURCE_SHEET], name, after_name=anchor).Name

        entries = []
        for item, name in zip(modules, sheet_names):
            fill_module_sheet(app, wb.Worksheets(name), item, profile)
            entries.append(
                {
                    "function": item.get("module", item.get("name", "")),
                    "sheet": name,
                    "description": item.get("description", ""),
                    "precondition": item.get("precondition", ""),
                }
            )

        fill_cover(wb.Worksheets("Cover"), content["cover"])
        fill_test_case_list(app, wb.Worksheets("Test Cases"), content.get("environment", ""),
                            entries, profile)
        fill_statistics(app, wb.Worksheets("Test Statistics"), content["cover"], sheet_names,
                        profile, stat_round)

        for temp in temp_names.values():
            xl.delete_sheet(wb, temp)

    return sheet_names


def main():
    ap = argparse.ArgumentParser(description="Dung tai lieu Unit Test (5.1) tu file mau .xls")
    ap.add_argument("--content", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--template", help="ghi de duong dan mau")
    ap.add_argument("--stat-round", type=int, default=1, choices=(1, 2, 3),
                    help="vong lay Passed/Failed/Untested cho Test Statistics (mac dinh 1)")
    args = ap.parse_args()

    sheets = build(args.content, args.out, args.template, args.stat_round)
    print(f"Da tao: {args.out}")
    print(f"So sheet module: {len(sheets)}")
    for name in sheets:
        print(f"  - {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
