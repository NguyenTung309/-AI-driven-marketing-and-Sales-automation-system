"""Helper Excel COM cho bo tai lieu test.

Chi dung Excel COM de GHI file .xlsx thuoc bo docs/test:
- clone sheet giu nguyen 100% style, data validation, merge, cot an, print setup
- Rows.Insert tu dich chuyen tham chieu cong thuc (openpyxl khong lam duoc)
- co engine tinh toan that (may khong co LibreOffice nen recalc.py khong dung duoc)

openpyxl chi duoc dung de DOC/kiem tra (xem compare_format.py).
"""

from __future__ import annotations

import contextlib
import datetime
import os
import shutil

import win32com.client as win32

XL_CALC_MANUAL = -4135
XL_CALC_AUTOMATIC = -4105
XL_SHIFT_DOWN = -4121
XL_VALIDATE_LIST = 3
XL_VALID_ALERT_STOP = 1
XL_A1 = 1

MAX_SHEET_NAME = 31
_INVALID_SHEET_CHARS = set('[]:*?/\\')


@contextlib.contextmanager
def excel_app(visible: bool = False):
    """Mo Excel, dam bao luon Quit() de khong de lai EXCEL.EXE mo coi."""
    app = win32.gencache.EnsureDispatch("Excel.Application")
    app.Visible = visible
    app.DisplayAlerts = False
    app.ScreenUpdating = False
    # Calculation chi set duoc khi da co workbook mo -> xem set_manual_calc()
    try:
        yield app
    finally:
        with contextlib.suppress(Exception):
            app.Calculation = XL_CALC_AUTOMATIC
        with contextlib.suppress(Exception):
            app.ScreenUpdating = True
        with contextlib.suppress(Exception):
            app.Quit()


@contextlib.contextmanager
def workbook_from_template(template: str, target: str, visible: bool = False):
    """Copy template -> target roi mo target. Khong bao gio mo template de ghi."""
    template, target = os.path.abspath(template), os.path.abspath(target)
    if os.path.normcase(template) == os.path.normcase(target):
        raise ValueError("target trung template: cam ghi de file mau")
    os.makedirs(os.path.dirname(target), exist_ok=True)
    shutil.copy2(template, target)
    with excel_app(visible=visible) as app:
        wb = app.Workbooks.Open(target)
        with contextlib.suppress(Exception):
            app.Calculation = XL_CALC_MANUAL  # chi set duoc sau khi da mo workbook
        try:
            yield app, wb
            app.CalculateFullRebuild()
            wb.Save()
        finally:
            with contextlib.suppress(Exception):
                wb.Close(SaveChanges=False)


def safe_sheet_name(name: str) -> str:
    """Chuan hoa ten sheet theo gioi han cua Excel (31 ky tu, ky tu cam)."""
    cleaned = "".join(" " if ch in _INVALID_SHEET_CHARS else ch for ch in name).strip()
    cleaned = " ".join(cleaned.split())
    if not cleaned:
        raise ValueError(f"ten sheet rong sau khi chuan hoa: {name!r}")
    return cleaned[:MAX_SHEET_NAME]


def clone_sheet(wb, src_name: str, new_name: str, after_name: str | None = None):
    """Nhan ban sheet mau - cach DUY NHAT giu du style/DV/merge/cot an/print setup."""
    new_name = safe_sheet_name(new_name)
    existing = {wb.Worksheets(i + 1).Name for i in range(wb.Worksheets.Count)}
    if new_name in existing:
        raise ValueError(f"ten sheet da ton tai: {new_name}")
    anchor = wb.Worksheets(after_name or src_name)
    wb.Worksheets(src_name).Copy(After=anchor)
    ws = wb.Worksheets(anchor.Index + 1)
    ws.Name = new_name
    return ws


def rename_sheet(wb, src_name: str, new_name: str):
    ws = wb.Worksheets(src_name)
    ws.Name = safe_sheet_name(new_name)
    return ws


def delete_sheet(wb, name: str) -> None:
    with contextlib.suppress(Exception):
        wb.Worksheets(name).Delete()


_FORMULA_PREFIX = ("=", "+", "-", "@")


def set_text(ws, addr: str, value) -> None:
    """Ghi chuoi ma khong lam doi number format cua o (giu dung format mau).

    Chi voi chuoi bat dau bang = + - @ moi phai tam chuyen sang dinh dang Text
    de Excel khong hieu nham la cong thuc, sau do tra lai format cu.
    """
    cell = ws.Range(addr)
    text = "" if value is None else str(value)
    if text.startswith(_FORMULA_PREFIX):
        original = cell.NumberFormat
        cell.NumberFormat = "@"
        cell.Value2 = text
        cell.NumberFormat = original
    else:
        cell.Value2 = text


def set_value(ws, addr: str, value) -> None:
    ws.Range(addr).Value2 = value


def set_date(ws, addr: str, value, fmt: str = "dd/MM/yyyy") -> None:
    """Ghi ngay dang so Excel + number format, khong ghi chuoi.

    Chi dat number format khi o dang de General: o nhan ban tu mau da co san
    dinh dang ngay cua mau, dat de len se bi Excel quy ve dinh dang ngay mac
    dinh theo locale (numFmtId 14) -> lech format so voi mau.
    """
    cell = ws.Range(addr)
    if value in (None, ""):
        cell.ClearContents()
        return
    if isinstance(value, datetime.date) and not isinstance(value, datetime.datetime):
        value = datetime.datetime(value.year, value.month, value.day)
    if str(cell.NumberFormat).strip().lower() in ("general", "@"):
        cell.NumberFormat = fmt
    cell.Value = value


def set_formula(ws, addr: str, formula: str) -> None:
    """LUON dung .Formula (dau phay en-US), khong dung .FormulaLocal."""
    ws.Range(addr).Formula = formula


def write_block(ws, first_cell: str, rows: list[list]) -> None:
    """Ghi theo khoi cho nhanh: 1 lan gan mang 2 chieu thay vi ghi tung o."""
    if not rows:
        return
    height, width = len(rows), max(len(r) for r in rows)
    padded = [list(r) + [None] * (width - len(r)) for r in rows]
    start = ws.Range(first_cell)
    target = ws.Range(start, start.Offset(height, width))
    target.Value2 = tuple(tuple(r) for r in padded)


def insert_rows(ws, at_row: int, count: int, copy_format_from: int | None = None) -> None:
    """Chen dong bang COM: Excel tu dich cong thuc, merge va data validation."""
    if count <= 0:
        return
    ws.Rows(f"{at_row}:{at_row + count - 1}").Insert(Shift=XL_SHIFT_DOWN)
    if copy_format_from:
        src = ws.Rows(copy_format_from + count if copy_format_from >= at_row else copy_format_from)
        src.Copy()
        ws.Rows(f"{at_row}:{at_row + count - 1}").PasteSpecial(Paste=-4122)  # xlPasteFormats
        ws.Application.CutCopyMode = False


def apply_list_validation(ws, addr: str, source: str) -> None:
    """Ap dropdown cho vung moi. source: '=$R$2:$R$5' hoac '"P,F"'."""
    rng = ws.Range(addr)
    with contextlib.suppress(Exception):
        rng.Validation.Delete()
    rng.Validation.Add(Type=XL_VALIDATE_LIST, AlertStyle=XL_VALID_ALERT_STOP, Formula1=source)
    rng.Validation.IgnoreBlank = True
    rng.Validation.InCellDropdown = True


def merge(ws, addr: str) -> None:
    ws.Range(addr).Merge()


def set_print_area(ws, addr: str) -> None:
    ws.PageSetup.PrintArea = addr


def used_last_row(ws, column: str = "A") -> int:
    return ws.Cells(ws.Rows.Count, ws.Range(f"{column}1").Column).End(-4162).Row  # xlUp
