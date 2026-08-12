"""Dung tai lieu test dang BANG-THEO-DONG tu chinh file mau, bang Excel COM.

Dung chung cho 2 ho tai lieu co cung cau truc:
  - system      : Report5.3_System Test.xlsx   (moi sheet = 1 workflow)
  - integration : Report5.2_Integration Test.xlsx (moi sheet = 1 module)

Nguyen tac bat di bat dich de format ra dung y mau:
  1. Moi sheet du lieu deu NHAN BAN tu sheet co san trong chinh file mau
     (Worksheet.Copy giu du style, data validation, merge, cot an, print setup).
  2. So dong chinh bang Insert/Delete cua Excel, khong dung tay dat lai style.
  3. Moi hang so ve bo cuc (so dong mau, thu tu dong nhom/dong case) deu DO TU
     FILE MAU luc chay, khong hard-code - hard-code tung lam sai bo cuc mot lan roi.
  4. Cong thuc ghi SAU khi chen/xoa dong, de Excel khong dich vung tham chieu.

Cach dung:
  python scripts/testdoc/rowdoc.py --profile system \
      --content scripts/testdoc/content/system_test.sample.json \
      --out "out/Report_System_Test.xlsx"
"""

from __future__ import annotations

import argparse
import dataclasses
import datetime as dt
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import xl  # noqa: E402

XL_SHIFT_DOWN = -4121
XL_PASTE_FORMATS = -4122
XL_PASTE_VALIDATION = 6

KEEP_SHEETS = ("Cover", "Test Cases", "Test Statistics")

ROUND_COLS = (
    ("round1", "F", "G", "H"),
    ("round2", "I", "J", "K"),
    ("round3", "L", "M", "N"),
)


@dataclasses.dataclass(frozen=True)
class RowDocProfile:
    """Cho khac nhau giua 2 ho tai lieu - phan con lai dung chung."""

    template: str
    source_sheet: str                  # sheet du lieu duoc nhan ban ra cac sheet moi
    stat_issue_date: str               # o Issue Date tren sheet Test Statistics
    stat_round_row: int                # dong Round lam nguon so lieu cho Statistics
    tc_index_float: bool               # 5.2 danh so 1.0 / 2.0, 5.3 danh so 1 / 2
    data_start: int = 11
    last_col: str = "O"
    clear_col: str = "R"               # xoa den het cot an R khi don dong
    group_cols: tuple[str, ...] = ("B", "C", "D", "E")
    tc_start: int = 9
    stat_start: int = 11


PROFILES = {
    "system": RowDocProfile(
        template="docs/test/Report5.3_System Test.xlsx",
        source_sheet="Workflow Name1",
        stat_issue_date="H5",
        stat_round_row=6,
        tc_index_float=False,
    ),
    "integration": RowDocProfile(
        template="docs/test/Report5.2_Integration Test.xlsx",
        source_sheet="Login & Authentication",
        stat_issue_date="G5",
        stat_round_row=8,
        tc_index_float=True,
    ),
}


def parse_date(value):
    if not value:
        return None
    if isinstance(value, dt.date):
        return value
    return dt.datetime.strptime(value, "%Y-%m-%d").date()


def cell_text(ws, addr: str) -> str:
    return str(ws.Range(addr).Value2 or "").strip()


def count_block(ws, start_row: int, key_col: str, max_rows: int = 2000) -> int:
    """Dem so dong du lieu cua mau: dung o dong trong dau tien."""
    for offset in range(max_rows):
        if not cell_text(ws, f"{key_col}{start_row + offset}"):
            return offset
    return max_rows


def detect_row_layout(ws, profile: RowDocProfile, max_rows: int = 2000) -> list[str]:
    """Doc bo cuc khoi du lieu tu chinh sheet mau.

    Dong 'group' (scenario/nhom) chi co cot A; dong 'case' co them cot B..E.
    """
    layout: list[str] = []
    for offset in range(max_rows):
        row = profile.data_start + offset
        if not cell_text(ws, f"A{row}"):
            break
        has_detail = any(cell_text(ws, f"{col}{row}") for col in profile.group_cols)
        layout.append("case" if has_detail else "group")
    return layout


def is_merged_row(ws, row: int) -> bool:
    return ws.Range(f"A{row}").MergeArea.Columns.Count > 1


def duplicate_row(app, ws, source_row: int, at_row: int, clear_col: str) -> None:
    """Nhan ban nguyen 1 dong (style + merge + DV + chieu cao) roi xoa noi dung.

    'Copy roi Insert' chinh la Insert Copied Cells cua Excel - cach duy nhat
    sao chep du moi thuoc tinh. Chi dung khi at_row > source_row de khong lech chi so.
    """
    ws.Rows(f"{source_row}:{source_row}").Copy()
    ws.Rows(f"{at_row}:{at_row}").Insert(Shift=XL_SHIFT_DOWN)
    app.CutCopyMode = False
    ws.Range(f"A{at_row}:{clear_col}{at_row}").ClearContents()


def apply_row_format(app, ws, at_row: int, proto_row: int, profile: RowDocProfile) -> None:
    """Doi 'loai' cua 1 dong san co: style + DV + chieu cao + trang thai merge."""
    if at_row == proto_row:
        return
    ws.Rows(f"{proto_row}:{proto_row}").Copy()
    ws.Rows(f"{at_row}:{at_row}").PasteSpecial(Paste=XL_PASTE_FORMATS)
    ws.Rows(f"{at_row}:{at_row}").PasteSpecial(Paste=XL_PASTE_VALIDATION)
    app.CutCopyMode = False
    ws.Rows(f"{at_row}:{at_row}").RowHeight = ws.Rows(f"{proto_row}:{proto_row}").RowHeight

    # PasteSpecial khong doi trang thai merge -> phai chinh tay (5.2 merge A:O dong nhom)
    span = ws.Range(f"A{at_row}:{profile.last_col}{at_row}")
    if is_merged_row(ws, at_row):
        span.UnMerge()
    if is_merged_row(ws, proto_row):
        span.Merge()
    ws.Range(f"A{at_row}:{profile.clear_col}{at_row}").ClearContents()


def fit_row_layout(app, ws, start_row: int, needed: list[str], layout: list[str],
                   profile: RowDocProfile) -> None:
    """Chinh khoi du lieu cho khop 'needed', giu nguyen style tung loai dong.

    Thu tu bat buoc: THEM o cuoi khoi -> doi loai tung dong -> XOA phan du o cuoi.
    Nho vay dong mau khong bao gio bi dich chi so hay bi ghi de giua chung.
    """
    current = list(layout)
    if not current:
        raise ValueError("sheet mau khong co dong du lieu nao de lam mau")
    # proto lay dong CUOI cung cua moi loai: dong dau tien cua mau thuong bi dat
    # chieu cao rieng chi de chua chu placeholder, khong dai dien cho dong thuong
    proto = {
        kind: start_row + len(current) - 1 - current[::-1].index(kind)
        for kind in set(current)
    }
    missing = set(needed) - set(proto)
    if missing:
        raise ValueError(f"sheet mau khong co dong mau cho loai: {sorted(missing)}")

    if len(needed) > len(current):
        fill_kind = current[-1]
        for _ in range(len(needed) - len(current)):
            duplicate_row(app, ws, proto[fill_kind], start_row + len(current), profile.clear_col)
            current.append(fill_kind)

    # Dong mau nam ngay trong khoi du lieu nen se bi ghi de trong luc doi loai:
    # nhan ban ra vung tam NGAY DUOI khoi truoc, doi xong moi xoa vung tam di.
    changes = [idx for idx, kind in enumerate(needed) if current[idx] != kind]
    if changes:
        stage_start = start_row + len(current)
        stage = {}
        for offset, kind in enumerate(sorted({needed[idx] for idx in changes})):
            duplicate_row(app, ws, proto[kind], stage_start + offset, profile.clear_col)
            stage[kind] = stage_start + offset
        for idx in changes:
            apply_row_format(app, ws, start_row + idx, stage[needed[idx]], profile)
            current[idx] = needed[idx]
        ws.Rows(f"{stage_start}:{stage_start + len(stage) - 1}").Delete()

    if len(current) > len(needed):
        first = start_row + len(needed)
        ws.Rows(f"{first}:{start_row + len(current) - 1}").Delete()


def fill_cover(ws, cover: dict) -> None:
    xl.set_text(ws, "B4", cover.get("project_name", ""))
    xl.set_text(ws, "F4", cover.get("creator", ""))
    xl.set_text(ws, "B5", cover.get("project_code", ""))
    issue_date = parse_date(cover.get("issue_date"))
    if issue_date:
        xl.set_date(ws, "F5", issue_date)
    xl.set_text(ws, "F6", cover.get("version", ""))

    changes = cover.get("changes", [])
    rows_in_template = count_block(ws, 11, "B")
    for offset in range(max(len(changes), rows_in_template)):
        row = 11 + offset
        change = changes[offset] if offset < len(changes) else {}
        change_date = parse_date(change.get("date"))
        if change_date:
            xl.set_date(ws, f"A{row}", change_date)
        else:
            ws.Range(f"A{row}").ClearContents()
        xl.set_text(ws, f"B{row}", change.get("version", ""))
        xl.set_text(ws, f"C{row}", change.get("item", ""))
        xl.set_text(ws, f"D{row}", change.get("type", ""))
        xl.set_text(ws, f"E{row}", change.get("description", ""))
        xl.set_text(ws, f"F{row}", change.get("reference", ""))


def fill_data_sheet(app, ws, sheet: dict, profile: RowDocProfile) -> int:
    """Ghi 1 sheet du lieu (workflow cua 5.3 / module cua 5.2), tra ve so test case."""
    xl.set_text(ws, "B2", sheet["name"])
    xl.set_text(ws, "B3", sheet.get("requirement", ""))

    needed, payloads = [], []
    for group in sheet["scenarios"]:
        needed.append("group")
        payloads.append(("group", group["group"]))
        for case in group["cases"]:
            needed.append("case")
            payloads.append(("case", case))

    fit_row_layout(app, ws, profile.data_start, needed, detect_row_layout(ws, profile), profile)

    # Ghi cong thuc SAU khi chen/xoa dong de Excel khong dich vung tham chieu.
    # DEV-1: dem thang theo tien to TC- thay vi hang so tru tay ("-3") nhu mau.
    xl.set_formula(ws, "B4", '=COUNTIF($A$12:$A$1000,"TC-*")')
    # DEV-2: mau de ca 3 vong deu dem cot F -> Round 2/3 luon bang Round 1.
    for row, col in ((7, "I"), (8, "L")):
        for out_col in "BCDE":
            xl.set_formula(ws, f"{out_col}{row}", f'=COUNTIF(${col}10:${col}998,{out_col}5)')

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
        xl.set_text(ws, f"B{row}", payload.get("description", ""))
        xl.set_text(ws, f"C{row}", payload.get("procedure", ""))
        xl.set_text(ws, f"D{row}", payload.get("expected", ""))
        xl.set_text(ws, f"E{row}", payload.get("precondition", ""))
        for round_key, result_col, date_col, tester_col in ROUND_COLS:
            data = payload.get(round_key) or {}
            xl.set_text(ws, f"{result_col}{row}", data.get("result", "Pending"))
            round_date = parse_date(data.get("date"))
            if round_date:
                xl.set_date(ws, f"{date_col}{row}", round_date)
            else:
                ws.Range(f"{date_col}{row}").ClearContents()
            xl.set_text(ws, f"{tester_col}{row}", data.get("tester", ""))
        xl.set_text(ws, f"{profile.last_col}{row}", payload.get("note", ""))
    return case_count


def fill_test_case_list(app, ws, environment: str, entries: list[dict], profile: RowDocProfile) -> None:
    xl.set_formula(ws, "D3", "=Cover!B4")   # DEV-3: mau 5.3 dang de =#REF!
    xl.set_formula(ws, "D4", "=Cover!B5")
    xl.set_text(ws, "D5", environment)

    template_rows = count_block(ws, profile.tc_start, "B")
    fit_row_layout(app, ws, profile.tc_start, ["case"] * len(entries),
                   ["case"] * template_rows, profile)

    last_row = profile.tc_start + len(entries) - 1
    ws.Range(f"B{profile.tc_start}:F{last_row}").ClearContents()
    for offset, entry in enumerate(entries):
        row = profile.tc_start + offset
        index = offset + 1
        xl.set_value(ws, f"B{row}", float(index) if profile.tc_index_float else index)
        xl.set_text(ws, f"C{row}", entry["function"])
        xl.set_text(ws, f"D{row}", entry["sheet"])
        xl.set_text(ws, f"E{row}", entry.get("description", ""))
        xl.set_text(ws, f"F{row}", entry.get("precondition", ""))


def fill_statistics(app, ws, cover: dict, sheet_names: list[str], profile: RowDocProfile,
                    round_row: int) -> None:
    xl.set_text(ws, "C3", cover.get("project_name", ""))
    xl.set_text(ws, "C4", cover.get("project_code", ""))
    xl.set_text(ws, "G3", cover.get("creator", ""))
    issue_date = parse_date(cover.get("issue_date"))
    if issue_date:
        xl.set_date(ws, profile.stat_issue_date, issue_date)
    xl.set_text(ws, "C6", cover.get("statistics_note", ""))

    # dem theo cot B (so thu tu): cot C cua dong 'Sub total' cung co chu,
    # dem theo C se an ca dong tong vao khoi du lieu roi xoa mat no
    template_rows = count_block(ws, profile.stat_start, "B")
    fit_row_layout(app, ws, profile.stat_start, ["case"] * len(sheet_names),
                   ["case"] * template_rows, profile)

    for offset, name in enumerate(sheet_names):
        row = profile.stat_start + offset
        ref = f"'{name}'"
        index = offset + 1
        xl.set_value(ws, f"B{row}", float(index) if profile.tc_index_float else index)
        xl.set_formula(ws, f"C{row}", f"={ref}!B2")
        for out_col, src_col in zip("DEFG", "BCDE"):
            xl.set_formula(ws, f"{out_col}{row}", f"={ref}!{src_col}{round_row}")
        xl.set_formula(ws, f"H{row}", f"={ref}!B4")

    fix_summary_formulas(ws, profile.stat_start, profile.stat_start + len(sheet_names) - 1)


def find_label_row(ws, label: str, start_row: int, limit: int = 8) -> int | None:
    """Tim dong co nhan o cot C (Sub total / Test coverage...) quanh khoi du lieu."""
    wanted = label.strip().lower()
    for offset in range(limit):
        if cell_text(ws, f"C{start_row + offset}").strip().lower() == wanted:
            return start_row + offset
    return None


def fix_summary_formulas(ws, first_row: int, last_row: int) -> None:
    """Viet lai Sub total va Test coverage sau khi so dong thay doi.

    DEV-4: mau tinh SUM tren dung khoang dong cua mau; khi bot dong thi Excel
    thu nho vung tham chieu va cac o coverage thanh #REF!. Viet lai theo so
    dong that de khong bao gio con #REF!.
    """
    sub_row = find_label_row(ws, "Sub total", last_row + 1)
    if sub_row is None:
        raise ValueError(
            f"khong thay dong 'Sub total' sau dong {last_row} tren sheet {ws.Name}: "
            "co the khoi du lieu da bi cat lan sang dong tong"
        )
    for col in "DEFGH":
        xl.set_formula(ws, f"{col}{sub_row}", f"=SUM({col}{first_row}:{col}{last_row})")
    coverage = (
        ("Test coverage", f"=(D{sub_row}+E{sub_row})*100/(H{sub_row}-G{sub_row})"),
        ("Test successful coverage", f"=D{sub_row}*100/(H{sub_row}-G{sub_row})"),
    )
    for label, formula in coverage:
        row = find_label_row(ws, label, sub_row + 1)
        if row is not None:
            xl.set_formula(ws, f"E{row}", formula)


def load_sheets(content: dict) -> list[dict]:
    for key in ("sheets", "workflows", "modules"):
        if content.get(key):
            return content[key]
    raise ValueError("content phai co khoa 'sheets' (hoac 'workflows'/'modules')")


def build(profile: RowDocProfile, content_path: str, out_path: str, round_row: int) -> list[str]:
    with open(content_path, encoding="utf-8") as fh:
        content = json.load(fh)

    sheets = load_sheets(content)
    sheet_names = [xl.safe_sheet_name(item["sheet"]) for item in sheets]
    if len(set(sheet_names)) != len(sheet_names):
        raise ValueError("ten sheet bi trung sau khi cat con 31 ky tu")

    with xl.workbook_from_template(profile.template, out_path) as (app, wb):
        template_sheets = [
            wb.Worksheets(i + 1).Name
            for i in range(wb.Worksheets.Count)
            if wb.Worksheets(i + 1).Name not in KEEP_SHEETS
        ]
        if profile.source_sheet not in template_sheets:
            raise ValueError(f"mau khong co sheet nguon {profile.source_sheet!r}")

        # Doi ten sheet khuon sang ten tam truoc: sheet ket qua co the trung ten
        # sheet khuon (5.2 dung chinh sheet module lam mau). Excel tu sua tham
        # chieu cheo sheet khi doi ten nen khong hong cong thuc nao.
        temp_names = {name: f"~tmp{i}" for i, name in enumerate(template_sheets, 1)}
        for name, temp in temp_names.items():
            wb.Worksheets(name).Name = temp

        anchor = temp_names[template_sheets[-1]]
        for name in sheet_names:
            anchor = xl.clone_sheet(wb, temp_names[profile.source_sheet], name, after_name=anchor).Name

        entries = []
        for item, name in zip(sheets, sheet_names):
            fill_data_sheet(app, wb.Worksheets(name), item, profile)
            entries.append(
                {
                    "function": item["name"],
                    "sheet": name,
                    "description": item.get("requirement", ""),
                    "precondition": item.get("precondition", ""),
                }
            )

        fill_cover(wb.Worksheets("Cover"), content["cover"])
        fill_test_case_list(app, wb.Worksheets("Test Cases"), content.get("environment", ""),
                            entries, profile)
        fill_statistics(app, wb.Worksheets("Test Statistics"), content["cover"], sheet_names,
                        profile, round_row)

        # bo sheet khuon sau cung, khi moi tham chieu da tro sang sheet moi
        for temp in temp_names.values():
            xl.delete_sheet(wb, temp)

    return sheet_names


def main():
    ap = argparse.ArgumentParser(description="Dung tai lieu test dang bang tu file mau")
    ap.add_argument("--profile", choices=sorted(PROFILES), required=True)
    ap.add_argument("--content", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--template", help="ghi de duong dan mau mac dinh cua profile")
    ap.add_argument(
        "--round-row",
        type=int,
        help="dong Round lam nguon cho Test Statistics (6=Round 1, 8=Round 3)",
    )
    args = ap.parse_args()

    profile = PROFILES[args.profile]
    if args.template:
        profile = dataclasses.replace(profile, template=args.template)
    round_row = args.round_row or profile.stat_round_row

    names = build(profile, args.content, args.out, round_row)
    print(f"Da tao: {args.out}")
    print(f"So sheet du lieu: {len(names)}")
    for name in names:
        print(f"  - {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
