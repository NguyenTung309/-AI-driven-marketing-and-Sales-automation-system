"""Dung tai lieu test dang BANG-PHANG (mot bang moi sheet) tu file mau, bang COM.

Dung chung cho 2 ho tai lieu co cung cau truc bang phang:
  - nfr : Report-5.4_System Test_NFRs.xlsx        (sheet Security, Performance)
  - uat : Report-5.5_Acceptance Test Scripts.xlsx (sheet UAT, Exploratory)

Khac voi rowdoc: KHONG co dong nhom/dong case, KHONG merge, KHONG clone sheet.
Moi sheet la 1 bang: header o dong 1, du lieu tu dong 2. Mau da co san:
  - vien (border) toi mot so dong trong,
  - data validation (dropdown) toi tan dong 300.

Nguyen tac giu format dung y mau:
  1. Mo file mau -> target (workbook_from_template), sheet da co san du style/DV.
  2. Ghi gia tri vao dong 2..N. Neu N vuot qua so dong da co vien thi NHAN BAN
     format cua dong 2 xuong (duplicate_row) - dong 2 mang du vien + DV.
  3. Xoa noi dung cac dong demo con thua sau dong N (giu lai vien nhu mau).
  4. Ghi bang set_text de khong lam doi number format cua o.

Cach dung:
  python scripts/testdoc/flatdoc.py --profile nfr \
      --content scripts/testdoc/content/nfr_test.sample.json \
      --out "out/Report_System_Test_NFRs.xlsx"
"""

from __future__ import annotations

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import xl  # noqa: E402

XL_UP = -4162
XL_TO_LEFT = -4159
XL_LINE_NONE = -4142
XL_SHIFT_DOWN = -4121
XL_PASTE_FORMATS = -4122
XL_PASTE_VALIDATION = 6

# Ho tai lieu -> file mau. Ten sheet + so cot doc tu chinh mau luc chay.
PROFILES = {
    "nfr": "docs/test/Report-5.4_System Test_NFRs.xlsx",
    "uat": "docs/test/Report-5.5_Acceptance Test Scripts.xlsx",
}

DATA_START = 2  # header o dong 1, du lieu tu dong 2


def col_letter(index1: int) -> str:
    """1-indexed -> ten cot (1=A)."""
    name, index = "", index1
    while index > 0:
        index, rem = divmod(index - 1, 26)
        name = chr(65 + rem) + name
    return name


def header_width(ws) -> int:
    """So cot cua bang = so o co chu o dong header (dong 1)."""
    last = ws.Cells(1, ws.Columns.Count).End(XL_TO_LEFT)
    return last.Column


def last_bordered_row(ws, width: int, probe_to: int = 400) -> int:
    """Dong cuoi cung con la o du lieu co VIEN DAY DU (4 canh) o cot A.

    Chi tinh o co du 4 canh moi la dong du lieu that; o chi co 1 canh (vd
    canh duoi vay tu hop tren) khong tinh - neu khong se lech 1 dong.
    """
    last = DATA_START - 1
    for row in range(DATA_START, probe_to + 1):
        edges = ws.Range(f"A{row}").Borders
        # xlEdgeLeft=7 xlEdgeTop=8 xlEdgeBottom=9 xlEdgeRight=10; None style = -4142
        full_box = all(edges(i).LineStyle != XL_LINE_NONE for i in (7, 8, 9, 10))
        if full_box:
            last = row
    return last


def extend_format_down(app, ws, proto_row: int, first_new: int, last_new: int,
                       width: int) -> None:
    """Trai format (style + DV) cua dong mau (proto) len cac dong moi ben duoi.

    Dung PasteSpecial (Formats + Validation) thay vi Insert-Copied-Cells de tranh
    lech chi so: khong chen/xoa dong nao, chi son format len dong san co ben duoi
    vung da co vien cua mau.
    """
    if last_new < first_new:
        return
    proto = col_letter(width)
    ws.Range(f"A{proto_row}:{proto}{proto_row}").Copy()
    dest = ws.Range(f"A{first_new}:{proto}{last_new}")
    dest.PasteSpecial(Paste=XL_PASTE_FORMATS)
    dest.PasteSpecial(Paste=XL_PASTE_VALIDATION)
    app.CutCopyMode = False


def fill_sheet(app, ws, rows: list[list], probe_to: int) -> None:
    """Ghi mot bang phang vao sheet, giu nguyen format cac dong san co cua mau."""
    width = header_width(ws)
    clear_col = col_letter(width)
    bordered = last_bordered_row(ws, width, probe_to)
    available = bordered - DATA_START + 1  # so dong da co vien de dung

    needed = len(rows)
    if needed > available:
        # Thieu dong: son format dong 2 (mang du vien + DV) len cac dong ben duoi
        # vung co vien cua mau. Khong chen dong -> khong lech chi so.
        first_new = bordered + 1
        last_new = DATA_START + needed - 1
        extend_format_down(app, ws, DATA_START, first_new, last_new, width)
        bordered = last_new

    # Ghi noi dung tung dong.
    for idx, row in enumerate(rows):
        r = DATA_START + idx
        for col_idx, value in enumerate(row, start=1):
            if col_idx > width:
                break
            if value in (None, ""):
                continue
            xl.set_text(ws, f"{col_letter(col_idx)}{r}", value)

    # Xoa noi dung demo con thua sau dong N (giu lai vien nhu mau).
    for r in range(DATA_START + needed, bordered + 1):
        ws.Range(f"A{r}:{clear_col}{r}").ClearContents()


def build(profile: str, content_path: str, out_path: str, template: str | None = None,
          probe_to: int = 400) -> None:
    tmpl = template or PROFILES[profile]
    with open(content_path, encoding="utf-8") as fh:
        content = json.load(fh)
    sheets = content.get("sheets", {})

    with xl.workbook_from_template(tmpl, out_path) as (app, wb):
        existing = {wb.Worksheets(i + 1).Name for i in range(wb.Worksheets.Count)}
        for sheet_name, spec in sheets.items():
            if sheet_name not in existing:
                raise ValueError(f"sheet {sheet_name!r} khong co trong mau {tmpl}")
            ws = wb.Worksheets(sheet_name)
            fill_sheet(app, ws, spec.get("rows", []), probe_to)


def main():
    ap = argparse.ArgumentParser(description="Dung tai lieu test dang bang phang (COM)")
    ap.add_argument("--profile", required=True, choices=sorted(PROFILES))
    ap.add_argument("--content", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--template", default=None)
    args = ap.parse_args()
    build(args.profile, args.content, args.out, template=args.template)
    print(f"Da dung: {args.out}")


if __name__ == "__main__":
    main()
