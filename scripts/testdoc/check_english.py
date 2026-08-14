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
VIETNAMESE_WORDS = frozenset("""
khong duoc nguoi nhung viec thong trong truoc cua voi cho lieu kenh nap ket noi
phuc khoi kiem danh sach thanh cong gan nhan tim moi tao xoa sua luu hien chuc
nang nhap xuat gui quan buoc phai neu khi dong dung mot tren duoi giua ngoai
cau hinh loi mau thu tra bai xong chua roi ngay thang nam gio phut giay nguon
dich mac dinh bat tat them bot chay dang xem sao chep dan ket qua tiep tuc
vui long thuc thanh phan gia tri truong hop dieu diem dau cuoi
""".split())

WORD_RE = re.compile(r"[a-z]{2,}")


def strip_marks(text: str) -> str:
    return "".join(c for c in unicodedata.normalize("NFD", text)
                   if unicodedata.category(c) != "Mn")


def scan_text(text: str) -> str | None:
    """Tra ve ly do neu chuoi khong phai tieng Anh, None neu dat."""
    bad_letters = sorted({c for c in text if c in VIETNAMESE_LETTERS})
    if bad_letters:
        return f"chu co dau tieng Viet: {''.join(bad_letters)}"
    hits = sorted({w for w in WORD_RE.findall(strip_marks(text).lower())
                   if w in VIETNAMESE_WORDS})
    if hits:
        return f"tu tieng Viet: {', '.join(hits)}"
    return None


def scan_workbook(path: str) -> list[str]:
    wb = openpyxl.load_workbook(path, data_only=False)
    problems: list[str] = []
    for ws in wb.worksheets:
        reason = scan_text(ws.title)
        if reason:
            problems.append(f"ten sheet {ws.title!r}: {reason}")
        for row in ws.iter_rows():
            for cell in row:
                if not isinstance(cell.value, str):
                    continue
                reason = scan_text(cell.value)
                if reason:
                    snippet = " ".join(cell.value.split())[:70]
                    problems.append(f"{ws.title}!{cell.coordinate}: {reason} | {snippet}")
    return problems


def main() -> int:
    ap = argparse.ArgumentParser(description="Kiem tra file ra co 100% tieng Anh khong")
    ap.add_argument("--file", action="append", required=True, help="file .xlsx (lap lai duoc)")
    ap.add_argument("--limit", type=int, default=40)
    args = ap.parse_args()

    total = 0
    for path in args.file:
        problems = scan_workbook(path)
        total += len(problems)
        print(f"== {path}: {len(problems)} cho khong phai tieng Anh")
        for line in problems[:args.limit]:
            print(f"   {line}")
        if len(problems) > args.limit:
            print(f"   ... con {len(problems) - args.limit} dong nua")

    print("\nTIENG ANH 100% - khong co chuoi tieng Viet" if total == 0
          else f"\nCON {total} CHUOI KHONG PHAI TIENG ANH")
    return 0 if total == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
