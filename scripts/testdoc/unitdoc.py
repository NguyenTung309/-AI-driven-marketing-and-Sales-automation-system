"""Dung tai lieu Unit Test (5.1) tu chinh file mau, bang Excel COM.

Khac han 5.2/5.3: sheet method la MA TRAN NGANG - moi UTCID la mot COT (F..T),
moi dieu kien/ket qua mong doi la mot DONG (8..36). Vi vay phai chinh CA hai
chieu: so cot theo so test case, so dong theo so dieu kien.

Nguyen tac giu format giong mau 100% (giong rowdoc.py):
1. Sheet method luon NHAN BAN tu sheet co san trong chinh file mau.
2. Them/bot dong va cot deu bang Insert/Delete cua Excel tren dong/cot mau,
   khong bao gio tu dat lai style.
3. Moi hang so bo cuc (dong bat dau band, dong Result, cot dau/cuoi) deu DO
   TU FILE MAU luc chay.
4. Khoi dong 1..6 khong bao gio bi ClearContents: cot chen them nam trong
   vung merge L1:T1 / O4:T4 / O5:T5, xoa se mat luon gia tri cua merge.

Cach dung:
    python scripts/testdoc/unitdoc.py \
        --content scripts/testdoc/content/unit_test.sample.json \
        --out "out/Report_Unit_Test.xlsx"
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
XL_SHIFT_TO_RIGHT = -4161
XL_PASTE_FORMATS = -4122
XL_PASTE_VALIDATION = 6
XL_UP = -4162

TEMPLATE = "docs/test/Report5.1_Unit Test.xlsx"
SOURCE_SHEET = "methodName1"
KEEP_SHEETS = ("Guideline", "Cover", "MethodList", "Statistics")

METHOD_LIST_START = 9
STAT_START = 12
FIRST_CASE_COL = 6  # cot F, ngay sau cot an E (cot dem cua mau)


@dataclasses.dataclass(frozen=True)
class Layout:
    """Bo cuc doc tu sheet mau - khong hard-code o bat ky cho nao khac."""

    first_col: int
    last_col: int
    bands: tuple[tuple[int, int], ...]  # (dong dau, dong cuoi) cua tung band
    result_row: int

    @property
    def case_count(self) -> int:
        return self.last_col - self.first_col + 1


def col_letter(index: int) -> str:
    name = ""
    while index > 0:
        index, rem = divmod(index - 1, 26)
        name = chr(65 + rem) + name
    return name


def parse_date(value):
    if not value:
        return None
    if isinstance(value, dt.date):
        return value
    return dt.datetime.strptime(value, "%Y-%m-%d").date()


def cell_text(ws, addr: str) -> str:
    return str(ws.Range(addr).Value2 or "").strip()


def set_cell(ws, addr: str, value) -> None:
    """Ghi so ra so, chu ra chu - de khong doi kieu du lieu cua o mau."""
    if value is None or value == "":
        ws.Range(addr).ClearContents()
    elif isinstance(value, bool):
        xl.set_text(ws, addr, str(value))
    elif isinstance(value, (int, float)):
        xl.set_value(ws, addr, value)
    else:
        xl.set_text(ws, addr, value)


def read_layout(ws) -> Layout:
    """Doc bo cuc ma tran tu sheet mau.

    - band = khoi co nhan o cot A ('Condition', 'Confirm'), ket thuc truoc band sau
    - dong 'Result' dong khung ma tran
    - cot cuoi lay tu be rong merge cua o 'Test requirement' (C3:T3)
    """
    first_col = FIRST_CASE_COL
    requirement = ws.Range("C3").MergeArea
    last_col = requirement.Column + requirement.Columns.Count - 1
    if last_col <= first_col:
        raise ValueError("sheet mau khong co vung UTCID (merge C3 qua hep)")

    starts: list[int] = []
    result_row = None
    for row in range(8, 400):
        label = cell_text(ws, f"A{row}").lower()
        if label == "result":
            result_row = row
            break
        if label:
            starts.append(row)
    if result_row is None or not starts:
        raise ValueError("sheet mau thieu dong 'Result' hoac cac band dieu kien")

    bounds = [(start, (starts[i + 1] if i + 1 < len(starts) else result_row) - 1)
              for i, start in enumerate(starts)]
    for start, end in bounds:
        if end - start < 2:
            raise ValueError(f"band tai dong {start} cua mau chi co {end - start + 1} dong, "
                             "khong du dong giua de lam mau")
    return Layout(first_col=first_col, last_col=last_col, bands=tuple(bounds), result_row=result_row)


def duplicate_rows(app, ws, proto_row: int, at_row: int, count: int, last_col: str) -> None:
    """Nhan ban dong mau (Insert Copied Cells) - cach duy nhat mang theo ca DV."""
    for offset in range(count):
        source = proto_row + offset if proto_row >= at_row else proto_row
        ws.Rows(f"{source}:{source}").Copy()
        ws.Rows(f"{at_row}:{at_row}").Insert(Shift=XL_SHIFT_DOWN)
        app.CutCopyMode = False
        ws.Range(f"A{at_row}:{last_col}{at_row}").ClearContents()


def duplicate_cols(app, ws, proto_col: int, at_col: int, count: int, grid_row: int,
                   last_row: int) -> None:
    """Nhan ban cot mau. Chi xoa noi dung tu dong luoi tro xuong.

    Dong 1..6 co merge tran sang cot moi (L1:T1, O4:T4, O5:T5); ClearContents o
    do se xoa luon gia tri cua ca vung merge.
    """
    for offset in range(count):
        source = proto_col + offset if proto_col >= at_col else proto_col
        src, dest = col_letter(source), col_letter(at_col)
        ws.Columns(f"{src}:{src}").Copy()
        ws.Columns(f"{dest}:{dest}").Insert(Shift=XL_SHIFT_TO_RIGHT)
        app.CutCopyMode = False
        ws.Range(f"{dest}{grid_row}:{dest}{last_row}").ClearContents()


def fit_band(app, ws, start: int, end: int, needed: int, last_col: str) -> int:
    """Chinh so dong cua mot band, giu nguyen dong dau (vien tren) va dong cuoi
    (vien duoi) cua mau. Tra ve do lech dong de chinh cac band phia duoi.
    """
    if needed < 2:
        raise ValueError(f"band tai dong {start} can it nhat 2 dong (1 nhan + 1 gia tri)")
    current = end - start + 1
    if needed > current:
        duplicate_rows(app, ws, end - 1, end, needed - current, last_col)
    elif needed < current:
        ws.Rows(f"{start + needed - 1}:{end - 1}").Delete()
    return needed - current


def fit_cases(app, ws, layout: Layout, needed: int, grid_row: int, last_row: int) -> None:
    """Chinh so cot UTCID, giu nguyen cot cuoi (vien phai) cua mau.

    KHONG BAO GIO thu hep duoi be rong cua mau: khoi tieu de dong 1..5 duoc ghep
    o (L1:T1, O4:T4, O5:T5) nen xoa cot se pha o neo cua merge. Ban than mau
    cung ship luoi 15 cot ma chi dung 2 UTCID (methodName1) - cot thua de trong
    la dung y mau.
    """
    if needed < 1:
        raise ValueError("moi method phai co it nhat 1 UTCID")
    if needed > layout.case_count:
        duplicate_cols(app, ws, layout.last_col - 1, layout.last_col,
                       needed - layout.case_count, grid_row, last_row)


def band_rows(bands: list[dict]) -> list[int]:
    return [sum(1 + len(group.get("values", [])) for group in band.get("groups", []))
            for band in bands]


def write_band(ws, start: int, band: dict, marks: dict, case_cols: list[str]) -> None:
    """Ghi nhan nhom + gia tri, va danh dau 'O' cho tung UTCID."""
    row = start
    for group in band.get("groups", []):
        label = str(group.get("label", "")).strip()
        xl.set_text(ws, f"B{row}", group.get("label", ""))
        for index, value in enumerate(group.get("values", [])):
            set_cell(ws, f"D{row + 1 + index}", value)
            for case_index, marked in enumerate(marks.get(label.lower(), [])):
                if index in marked:
                    xl.set_text(ws, f"{case_cols[case_index]}{row + 1 + index}", "O")
        row += 1 + len(group.get("values", []))


def case_marks(cases: list[dict]) -> dict:
    """Doi marks cua tung case thanh: nhan nhom -> danh sach tap chi so theo case."""
    marks: dict[str, list[set]] = {}
    for index, case in enumerate(cases):
        for label, value in (case.get("marks") or {}).items():
            picked = value if isinstance(value, (list, tuple)) else [value]
            slots = marks.setdefault(str(label).strip().lower(), [set() for _ in cases])
            slots[index].update(int(v) for v in picked)
    return marks


def fill_method_sheet(app, ws, method: dict) -> None:
    layout = read_layout(ws)
    cases = method.get("cases") or []
    if not cases:
        raise ValueError(f"method {method.get('method')!r} khong co UTCID nao")
    bands = method.get("bands") or []
    if len(bands) > len(layout.bands):
        raise ValueError(f"mau chi co {len(layout.bands)} band, content dua {len(bands)}")

    grid_row = layout.result_row - 1  # dong cuoi ma tran, luon nam duoi dong tieu de
    needed = band_rows(bands)
    last_col_letter = col_letter(layout.last_col)

    # Chinh tu band DUOI len TREN de chi so dong cua band tren khong bi xe dich.
    shift = 0
    for index in reversed(range(len(layout.bands))):
        start, end = layout.bands[index]
        if index >= len(bands):  # content it band hon mau -> bo han band thua
            ws.Rows(f"{start}:{end}").Delete()
            shift -= end - start + 1
        else:
            shift += fit_band(app, ws, start, end, needed[index], last_col_letter)
    result_row = layout.result_row + shift
    fit_cases(app, ws, layout, len(cases), 7, result_row + 3)

    layout = read_layout(ws)
    case_cols = [col_letter(layout.first_col + i) for i in range(len(cases))]
    # Phai quet het ca cot dem E: mau co merge D17:E17, xoa nua vung merge thi
    # Excel bao loi "We can't do that to a merged cell".
    ws.Range(f"A8:E{layout.result_row - 1}").ClearContents()
    ws.Range(f"{case_cols[0]}7:{col_letter(layout.last_col)}{layout.result_row + 3}").ClearContents()

    xl.set_text(ws, "C1", method.get("module", ""))
    xl.set_text(ws, "L1", method.get("method", ""))
    xl.set_text(ws, "C2", method.get("created_by", ""))
    xl.set_text(ws, "L2", method.get("executed_by", ""))
    xl.set_text(ws, "C3", method.get("requirement", ""))

    marks = case_marks(cases)
    row = 8
    for index, band in enumerate(bands):
        xl.set_text(ws, f"A{row}", band.get("name", ""))
        write_band(ws, row, band, marks, case_cols)
        row += needed[index]

    for index, case in enumerate(cases):
        col = case_cols[index]
        xl.set_text(ws, f"{col}7", case.get("id", f"UTCID{index + 1:02d}"))
        xl.set_text(ws, f"{col}{layout.result_row}", case.get("type", ""))
        xl.set_text(ws, f"{col}{layout.result_row + 1}", case.get("result", ""))
        executed = parse_date(case.get("date"))
        if executed:
            xl.set_date(ws, f"{col}{layout.result_row + 2}", executed)
        xl.set_text(ws, f"{col}{layout.result_row + 3}", case.get("defect", ""))


def resize_list(app, ws, start_row: int, needed: int, key_col: str, last_col: str) -> None:
    """Chinh so dong cua danh sach don gian (MethodList / Statistics)."""
    current = 0
    while cell_text(ws, f"{key_col}{start_row + current}"):
        current += 1
    if current == 0:
        raise ValueError(f"sheet {ws.Name} khong co dong du lieu mau tu {key_col}{start_row}")
    if needed > current:
        duplicate_rows(app, ws, start_row + current - 1, start_row + current,
                       needed - current, last_col)
    elif needed < current:
        ws.Rows(f"{start_row + needed}:{start_row + current - 1}").Delete()


def fill_method_list(app, ws, methods: list[dict], environment: str, sheet_names: list[str]) -> None:
    xl.set_text(ws, "C6", environment)
    resize_list(app, ws, METHOD_LIST_START, len(methods), "C", "F")
    for index, (method, sheet) in enumerate(zip(methods, sheet_names)):
        row = METHOD_LIST_START + index
        xl.set_value(ws, f"A{row}", index + 1)
        xl.set_text(ws, f"B{row}", method.get("module", ""))
        xl.set_text(ws, f"C{row}", method.get("method", ""))
        xl.set_text(ws, f"D{row}", sheet)
        xl.set_text(ws, f"E{row}", method.get("description", ""))
        xl.set_text(ws, f"F{row}", method.get("precondition", ""))


def find_label_row(ws, label: str, start_row: int, limit: int = 10) -> int | None:
    wanted = label.strip().lower()
    for offset in range(limit):
        if cell_text(ws, f"B{start_row + offset}").lower() == wanted:
            return start_row + offset
    return None


def fix_statistics_formulas(ws, first_row: int, last_row: int) -> None:
    """Viet lai Sub total + cac dong ty le sau khi so dong thay doi.

    DEV-5: mau de =SUM(C10:C15) trong khi du lieu bat dau tu dong 12 (dong 10-11
    la tieu de) - loi co san trong mau. Viet lai theo dung khoi du lieu that.
    """
    sub_row = find_label_row(ws, "Sub total", last_row + 1)
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
        row = find_label_row(ws, label, sub_row + 1)
        if row is not None:
            xl.set_formula(ws, f"D{row}", formula)


def fill_statistics(app, ws, cover: dict, methods: list[dict], sheet_names: list[str]) -> None:
    xl.set_text(ws, "F4", cover.get("creator", ""))
    xl.set_text(ws, "F5", cover.get("reviewer", ""))
    issue_date = parse_date(cover.get("issue_date"))
    if issue_date:
        xl.set_date(ws, "F6", issue_date)
    xl.set_text(ws, "B7", cover.get("statistics_note", ""))

    resize_list(app, ws, STAT_START, len(methods), "B", "I")
    for index, (method, sheet) in enumerate(zip(methods, sheet_names)):
        row = STAT_START + index
        xl.set_value(ws, f"A{row}", index + 1)
        xl.set_text(ws, f"B{row}", method.get("method", ""))
        for col, source in zip("CDEFGHI", ("A5", "C5", "F5", "L5", "M5", "N5", "O5")):
            xl.set_formula(ws, f"{col}{row}", f"='{sheet}'!{source}")
    fix_statistics_formulas(ws, STAT_START, STAT_START + len(methods) - 1)


def build(content_path: str, out_path: str, template: str | None = None) -> list[str]:
    with open(content_path, encoding="utf-8") as fh:
        content = json.load(fh)

    methods = content.get("methods") or []
    if not methods:
        raise ValueError("content thieu 'methods'")
    sheet_names = [xl.safe_sheet_name(item["sheet"]) for item in methods]
    if len(set(sheet_names)) != len(sheet_names):
        raise ValueError("ten sheet method bi trung sau khi cat 31 ky tu")

    with xl.workbook_from_template(template or TEMPLATE, out_path) as (app, wb):
        all_sheets = [wb.Worksheets(i + 1).Name for i in range(wb.Worksheets.Count)]
        template_sheets = [name for name in all_sheets if name not in KEEP_SHEETS]
        if SOURCE_SHEET not in template_sheets:
            raise ValueError(f"mau thieu sheet {SOURCE_SHEET!r}")

        # Doi ten sheet khuon truoc: ten sheet ket qua co the trung ten sheet khuon.
        temp_names = {name: f"~tmp{i}" for i, name in enumerate(template_sheets, 1)}
        for name, temp in temp_names.items():
            wb.Worksheets(name).Name = temp

        anchor = temp_names[template_sheets[-1]]
        for name in sheet_names:
            anchor = xl.clone_sheet(wb, temp_names[SOURCE_SHEET], name, after_name=anchor).Name

        for method, name in zip(methods, sheet_names):
            fill_method_sheet(app, wb.Worksheets(name), method)

        fill_cover(wb.Worksheets("Cover"), content["cover"])
        fill_method_list(app, wb.Worksheets("MethodList"), methods,
                         content.get("environment", ""), sheet_names)
        fill_statistics(app, wb.Worksheets("Statistics"), content["cover"], methods, sheet_names)

        for temp in temp_names.values():
            xl.delete_sheet(wb, temp)

    return sheet_names


def fill_cover(ws, cover: dict) -> None:
    """Cover cua 5.1 trung bo cuc voi 5.2/5.3 (B4/F4, B5/F5, F6, Record of change)."""
    xl.set_text(ws, "B4", cover.get("project_name", ""))
    xl.set_text(ws, "F4", cover.get("creator", ""))
    xl.set_text(ws, "B5", cover.get("project_code", ""))
    issue_date = parse_date(cover.get("issue_date"))
    if issue_date:
        xl.set_date(ws, "F5", issue_date)
    xl.set_text(ws, "F6", cover.get("version", ""))

    for offset, change in enumerate(cover.get("changes", [])):
        row = 11 + offset
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


def main():
    ap = argparse.ArgumentParser(description="Dung tai lieu Unit Test (5.1) tu file mau")
    ap.add_argument("--content", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--template", help="ghi de duong dan mau")
    args = ap.parse_args()

    sheets = build(args.content, args.out, args.template)
    print(f"Da tao: {args.out}")
    print(f"So sheet method: {len(sheets)}")
    for name in sheets:
        print(f"  - {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
