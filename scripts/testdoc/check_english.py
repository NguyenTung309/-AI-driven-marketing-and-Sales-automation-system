"""Canh cong ngon ngu: file dung ra phai 100% tieng Anh.

Yeu cau nghiep vu la toan bo report giao cho khach phai bang tieng Anh, ke ca
ten sheet. Noi dung lay tu file JSON trong scripts/testdoc/content nen rat de
lot mot cau tieng Viet vao ma bo nghiem thu format khong he bao loi - format
van khop 100% du chu la tieng Viet.

Script bat 2 dang:
1. Chu co dau tieng Viet (a, e, o... co dau) - bat bang bang chu cai.
2. Chu tieng Viet da bo dau ("ket noi kenh") - bat bang danh sach tu.
   Danh sach chi chua tu KHONG trung voi tu tieng Anh nao, de tranh bao nham:
   vi du khong dua vao "can", "may", "them", "day", "he", "so", "to".

    python scripts/testdoc/check_english.py --file out.xlsx
"""

from __future__ import annotations

import argparse
import re
import sys
import unicodedata

import openpyxl

# Nguyen am tieng Viet co dau + d gach ngang. Tieng Anh khong dung ky tu nao
# trong so nay nen chi can thay mot ky tu la chac chan khong phai tieng Anh.
VIETNAMESE_LETTERS = set(
    "ăĂâÂêÊôÔơƠưƯ"
    "đĐàáảãạèéẻẽẹ"
    "ìíỉĩịòóỏõọùú"
    "ủũụỳýỷỹỵ"
)

# Tu tieng Viet bo dau, chon loc de KHONG trung tu tieng Anh.
# LUU Y: khong dua tu nao trung tu tieng Anh pho bien ("long", "can", "may"...).
VIETNAMESE_WORDS = frozenset("""
khong duoc nguoi nhung viec thong trong truoc cua voi cho lieu kenh nap ket noi
phuc khoi kiem danh sach thanh cong gan nhan tim moi tao xoa sua luu hien chuc
nang nhap xuat gui quan buoc phai neu khi dong dung mot tren duoi giua ngoai
cau hinh loi mau thu tra bai xong chua roi ngay thang nam gio phut giay nguon
dich mac dinh bat tat bot chay dang xem sao chep dan ket qua tiep tuc
vui thuc thanh phan gia tri truong hop dieu diem dau cuoi
""".split())

WORD_RE = re.compile(r"[a-z]{2,}")

# Dong tham so do gen_unit_content.py sinh ra co dang 'ten = gia tri'. Gia tri
# trong ngoac kep la DU LIEU THAT truyen vao test - san pham phuc vu khach Viet
# nen no tieng Viet moi dung; dich no di la ghi sai dau vao cua test.
ARG_LINE_RE = re.compile(r"^\w+ = .*$")
STRING_LITERAL_RE = re.compile(r'"[^"]*"')


def strip_marks(text: str) -> str:
    return "".join(c for c in unicodedata.normalize("NFD", text)
                   if unicodedata.category(c) != "Mn")


def mask_arg_literals(text: str, counter: list[int]) -> str:
    """Che phan trong ngoac kep cua o CHi gom cac dong tham so.

    Mot o co bat ky dong van xuoi nao se khong khop -> quet binh thuong, nen
    khong the giau mot cau tieng Viet bang cach bo no vao ngoac kep.
    """
    lines = [ln for ln in text.split("\n") if ln.strip()]
    if not lines or not all(ARG_LINE_RE.match(ln) for ln in lines):
        return text
    masked, hits = STRING_LITERAL_RE.subn('""', text)
    counter[0] += hits
    return masked


def scan_text(text: str, arg_literals: list[int] | None = None) -> str | None:
    """Tra ve ly do neu chuoi khong phai tieng Anh, None neu dat."""
    if arg_literals is not None:
        text = mask_arg_literals(text, arg_literals)
    bad_letters = sorted({c for c in text if c in VIETNAMESE_LETTERS})
    if bad_letters:
        return f"chu co dau tieng Viet: {''.join(bad_letters)}"
    hits = sorted({w for w in WORD_RE.findall(strip_marks(text).lower())
                   if w in VIETNAMESE_WORDS})
    if hits:
        return f"tu tieng Viet: {', '.join(hits)}"
    return None


def scan_workbook_xlsx(path: str, skip_sheets: set[str] | None = None,
                       arg_literals: list[int] | None = None) -> list[str]:
    skip_sheets = skip_sheets or set()
    wb = openpyxl.load_workbook(path, data_only=False)
    problems: list[str] = []
    for ws in wb.worksheets:
        if ws.title in skip_sheets:
            continue
        reason = scan_text(ws.title)
        if reason:
            problems.append(f"ten sheet {ws.title!r}: {reason}")
        for row in ws.iter_rows():
            for cell in row:
                if not isinstance(cell.value, str):
                    continue
                reason = scan_text(cell.value, arg_literals)
                if reason:
                    snippet = " ".join(cell.value.split())[:70]
                    problems.append(f"{ws.title}!{cell.coordinate}: {reason} | {snippet}")
    return problems


def scan_workbook_xls(path: str, skip_sheets: set[str] | None = None,
                      arg_literals: list[int] | None = None) -> list[str]:
    """Mau 5.1 moi la .xls (BIFF8) - openpyxl khong doc duoc, phai dung xlrd."""
    import xlrd  # noi bo: chi .xls moi can

    skip_sheets = skip_sheets or set()
    book = xlrd.open_workbook(path)
    problems: list[str] = []
    for sheet in book.sheets():
        if sheet.name in skip_sheets:
            continue
        reason = scan_text(sheet.name)
        if reason:
            problems.append(f"ten sheet {sheet.name!r}: {reason}")
        for r in range(sheet.nrows):
            for c in range(sheet.ncols):
                value = sheet.cell_value(r, c)
                if not isinstance(value, str):
                    continue
                reason = scan_text(value, arg_literals)
                if reason:
                    coord = f"{xlrd.colname(c)}{r + 1}"
                    snippet = " ".join(value.split())[:70]
                    problems.append(f"{sheet.name}!{coord}: {reason} | {snippet}")
    return problems


def scan_workbook(path: str, skip_sheets: set[str] | None = None,
                  arg_literals: list[int] | None = None) -> list[str]:
    if path.lower().endswith(".xls"):
        return scan_workbook_xls(path, skip_sheets, arg_literals)
    return scan_workbook_xlsx(path, skip_sheets, arg_literals)


def main() -> int:
    ap = argparse.ArgumentParser(description="Kiem tra file ra co 100% tieng Anh khong")
    ap.add_argument("--file", action="append", required=True, help="file .xlsx (lap lai duoc)")
    ap.add_argument("--skip-sheet", action="append", default=[],
                    help="ten sheet bo qua (gian giao giu tu mau, lap lai duoc)")
    ap.add_argument("--limit", type=int, default=40)
    ap.add_argument("--allow-arg-literals", action="store_true",
                    help="bo qua chuoi trong ngoac kep cua dong tham so test "
                         "(du lieu that truyen vao test, khong phai van xuoi)")
    args = ap.parse_args()

    skip_sheets = set(args.skip_sheet)
    arg_literals = [0] if args.allow_arg_literals else None
    total = 0
    for path in args.file:
        problems = scan_workbook(path, skip_sheets, arg_literals)
        total += len(problems)
        print(f"== {path}: {len(problems)} cho khong phai tieng Anh")
        for line in problems[:args.limit]:
            print(f"   {line}")
        if len(problems) > args.limit:
            print(f"   ... con {len(problems) - args.limit} dong nua")

    if arg_literals and arg_literals[0]:
        print(f"\n(bo qua {arg_literals[0]} chuoi du lieu trong dong tham so test)")
    print("\nTIENG ANH 100% - khong co chuoi tieng Viet" if total == 0
          else f"\nCON {total} CHUOI KHONG PHAI TIENG ANH")
    return 0 if total == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
