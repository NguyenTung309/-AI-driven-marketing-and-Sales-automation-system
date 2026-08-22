"""So sanh FORMAT giua file mau .xls va file ket qua .xls (bo qua noi dung).

Doi ung compare_format.py nhung cho file BIFF8 (.xls): openpyxl khong doc duoc
.xls nen phai dung xlrd voi formatting_info=True.

Trong BIFF8, moi o tro toi mot ban ghi XF (eXtended Format) gom font + fill +
border + alignment + number format. CHU Y: chi so XF chi la con tro vao bang
style RIENG cua tung file; Excel/COM don lai bang nay khi luu nen hai o co dinh
dang Y HET van co chi so khac nhau giua hai file. Vi vay phai PHAN GIAI chi so
XF thanh chu ky dinh dang (numfmt + font + align + border + fill) roi so sanh
chu ky, khong so sanh chi so tho.

So sanh nhung gi (giong compare_format.py):
  1. danh sach sheet
  2. merge o vung header
  3. XF (style) tung o vung header: khop tuyet doi
  4. XF dong du lieu: moi dong du lieu ket qua phai khop MOT trong cac XF ma
     sheet mau dang dung cho cot do (nhom vs case tach rieng)
  5. do rong / an cua cot
  6. chieu cao / an cua dong vung header

Cach dung:
  python compare_unit.py --base "docs/test/Report-5.1_Unit Test.xls" \
      --target out.xls --pair "ModuleName1=ModuleName1" \
      --header-rows 11 --data-row 12 --same-name
"""

from __future__ import annotations

import argparse
import sys

import xlrd


def col_letter(index0: int) -> str:
    """0-indexed -> ten cot (0=A)."""
    name, index = "", index0 + 1
    while index > 0:
        index, rem = divmod(index - 1, 26)
        name = chr(65 + rem) + name
    return name


def cell_value(sheet, r: int, c: int):
    if r >= sheet.nrows or c >= sheet.ncols:
        return ""
    v = sheet.cell_value(r, c)
    return v if v not in (None,) else ""


def is_group_row(sheet, r: int, span_cols=(1, 2, 3, 4)) -> bool:
    """Dong nhom (ten method): chi co gia tri o cot A."""
    if cell_value(sheet, r, 0) in (None, ""):
        return False
    return all(cell_value(sheet, r, c) in (None, "") for c in span_cols)


def data_rows(sheet, data_row0: int, limit: int = 2000) -> list[int]:
    rows = []
    for r in range(data_row0, min(sheet.nrows, data_row0 + limit)):
        if cell_value(sheet, r, 0) not in (None, ""):
            rows.append(r)
    return rows


def xf_at(sheet, r: int, c: int):
    if r >= sheet.nrows or c >= sheet.ncols:
        return None
    return sheet.cell_xf_index(r, c)


def xf_sig(book, xfi):
    """Phan giai chi so XF thanh chu ky dinh dang co the bam (hashable).

    Chi so XF chi co nghia trong noi bo tung file; phai quy ve thuoc tinh
    dinh dang thuc su (numfmt + font + align + border + fill) de so sanh
    giua hai file.
    """
    if xfi is None:
        return None
    xf = book.xf_list[xfi]
    font = book.font_list[xf.font_index]
    fmt = book.format_map.get(xf.format_key)
    al = xf.alignment
    br = xf.border
    bg = xf.background

    def norm_black(idx):
        # 8 = den tuong minh trong palette, 64 = "automatic" (mac dinh he thong,
        # hien thi den). Hai gia tri render giong het nhau -> quy ve 1 chuan.
        return 64 if idx in (0, 8, 64) else idx

    return (
        fmt.format_str if fmt else None,
        (font.name, font.height, font.weight, font.italic,
         font.underline_type, font.colour_index, font.struck_out),
        (al.hor_align, al.vert_align, al.text_wrapped, al.rotation, al.indent_level),
        (br.top_line_style, br.bottom_line_style, br.left_line_style, br.right_line_style,
         norm_black(br.top_colour_index), norm_black(br.bottom_colour_index),
         norm_black(br.left_colour_index), norm_black(br.right_colour_index)),
        (bg.fill_pattern, bg.pattern_colour_index, bg.background_colour_index),
    )


def is_date_fmt(fmt_str) -> bool:
    if not fmt_str:
        return False
    low = fmt_str.lower()
    return any(tok in low for tok in ("yy", "mm/", "/mm", "dd", "mmm"))


def sig_matches(base_sig, target_sig) -> bool:
    """Chu ky khop, chi bo qua rieng khac biet number format ngay thang.

    O cot Test date cua mau con TRONG nen mang numfmt General; khi dien ngay
    that Excel bat buoc gan number format ngay -> khac numfmt o day la dung y
    mau chu khong phai lech format. Moi thanh phan con lai (font, vien, can le,
    to nen) van phai khop tuyet doi.
    """
    if base_sig == target_sig:
        return True
    if base_sig is None or target_sig is None:
        return False
    if base_sig[1:] != target_sig[1:]:
        return False
    return is_date_fmt(target_sig[0]) and (
        base_sig[0] in (None, "General") or is_date_fmt(base_sig[0]))


def colinfo(sheet, max_col: int):
    """{cot: (do rong, an)} - width don vi 1/256 ky tu, None neu khong dat rieng."""
    out = {}
    for c in range(max_col):
        info = sheet.colinfo_map.get(c)
        if info is None:
            out[c] = (None, False)
        else:
            out[c] = (info.width, bool(info.hidden))
    return out


def rowinfo(sheet, rows):
    out = {}
    for r in rows:
        info = sheet.rowinfo_map.get(r)
        if info is None:
            out[r] = (None, False)
        else:
            # height_mismatch=1 nghia la chieu cao do Excel tu tinh (khong phai
            # nguoi dat tay) -> khong tinh la format khac.
            custom = not getattr(info, "height_mismatch", 0)
            out[r] = (info.height if custom else None, bool(info.hidden))
    return out


def merges_in_rows(sheet, max_row0: int) -> set:
    out = set()
    for (rlo, rhi, clo, chi) in sheet.merged_cells:
        if rhi <= max_row0 + 1:
            out.add((rlo, rhi, clo, chi))
    return out


def size_equal(a, b, tol: float) -> bool:
    (a_size, a_hidden), (b_size, b_hidden) = a, b
    if bool(a_hidden) != bool(b_hidden):
        return False
    if a_size is None or b_size is None:
        return a_size == b_size
    return abs(float(a_size) - float(b_size)) <= tol


def compare_sheet(base_wb, target_wb, base, target, header_rows: int, data_row: int,
                  max_data_rows: int, size_tol: float) -> list[str]:
    diffs = []
    tag = f"{base.name} -> {target.name}"
    header0 = range(header_rows)              # 0-indexed dong header
    data_row0 = data_row - 1
    max_col = max(base.ncols, target.ncols, 20)  # it nhat toi cot T (nguon dropdown)

    def bsig(r, c):
        return xf_sig(base_wb, xf_at(base, r, c))

    def tsig(r, c):
        return xf_sig(target_wb, xf_at(target, r, c))

    # 2. merge vung header
    b_merge = merges_in_rows(base, header_rows - 1)
    t_merge = merges_in_rows(target, header_rows - 1)
    for missing in sorted(b_merge - t_merge):
        diffs.append(f"[{tag}] thieu merge {missing}")
    for extra in sorted(t_merge - b_merge):
        diffs.append(f"[{tag}] merge thua {extra}")

    # 3. chu ky dinh dang vung header - khop tuyet doi
    for r in header0:
        for c in range(max_col):
            if bsig(r, c) != tsig(r, c):
                diffs.append(
                    f"[{tag}] style {col_letter(c)}{r + 1}: "
                    f"mau={bsig(r, c)} ket_qua={tsig(r, c)}"
                )

    # 4. chu ky dong du lieu - moi cot chap nhan cac bien the cua mau
    protos = {c: set() for c in range(max_col)}
    group_protos = set()
    for r in data_rows(base, data_row0, limit=40):
        if is_group_row(base, r):
            group_protos.add(bsig(r, 0))
            continue
        for c in range(max_col):
            protos[c].add(bsig(r, c))
    for c in protos:
        if not protos[c]:
            protos[c].add(bsig(data_row0, c))

    checked = 0
    for r in data_rows(target, data_row0):
        checked += 1
        if checked > max_data_rows:
            break
        if is_group_row(target, r):
            sig = tsig(r, 0)
            if group_protos and sig not in group_protos:
                diffs.append(
                    f"[{tag}] style dong nhom A{r + 1}: khac chu ky nhom"
                )
            continue
        for c in range(max_col):
            sig = tsig(r, c)
            if sig not in protos[c] and not any(sig_matches(p, sig) for p in protos[c]):
                diffs.append(
                    f"[{tag}] style dong du lieu {col_letter(c)}{r + 1}: khac chu ky cot"
                )

    # 5. cot
    b_cols, t_cols = colinfo(base, max_col), colinfo(target, max_col)
    for c in range(max_col):
        if not size_equal(b_cols[c], t_cols[c], size_tol * 256):
            diffs.append(f"[{tag}] cot {col_letter(c)}: mau={b_cols[c]} ket_qua={t_cols[c]}")

    # 6. dong vung header
    b_rows, t_rows = rowinfo(base, header0), rowinfo(target, header0)
    for r in header0:
        if not size_equal(b_rows[r], t_rows[r], size_tol * 20):
            diffs.append(f"[{tag}] dong {r + 1}: mau={b_rows[r]} ket_qua={t_rows[r]}")

    return diffs


def main():
    ap = argparse.ArgumentParser(description="So sanh format .xls giua mau va ket qua (xlrd)")
    ap.add_argument("--base", required=True)
    ap.add_argument("--target", required=True)
    ap.add_argument("--pair", action="append", default=[], help='"sheet mau=sheet ket qua"')
    ap.add_argument("--same-name", action="store_true")
    ap.add_argument("--header-rows", type=int, default=11)
    ap.add_argument("--data-row", type=int, default=12)
    ap.add_argument("--max-data-rows", type=int, default=400)
    ap.add_argument("--size-tol", type=float, default=0.35)
    args = ap.parse_args()

    base_wb = xlrd.open_workbook(args.base, formatting_info=True)
    target_wb = xlrd.open_workbook(args.target, formatting_info=True)
    base_names = base_wb.sheet_names()
    target_names = target_wb.sheet_names()

    pairs = []
    for item in args.pair:
        if "=" not in item:
            print(f"--pair sai dinh dang: {item}", file=sys.stderr)
            return 2
        b, t = item.split("=", 1)
        pairs.append((b.strip(), t.strip()))
    if args.same_name:
        for name in base_names:
            if name in target_names and (name, name) not in pairs:
                pairs.append((name, name))

    diffs = []
    for base_name, target_name in pairs:
        if base_name not in base_names:
            diffs.append(f"[mau] khong co sheet {base_name!r}")
            continue
        if target_name not in target_names:
            diffs.append(f"[ket qua] khong co sheet {target_name!r}")
            continue
        diffs += compare_sheet(
            base_wb, target_wb,
            base_wb.sheet_by_name(base_name),
            target_wb.sheet_by_name(target_name),
            args.header_rows, args.data_row, args.max_data_rows, args.size_tol,
        )

    print(f"Cap sheet so sanh: {len(pairs)}")
    for b, t in pairs:
        print(f"  {b} -> {t}")
    if diffs:
        print(f"\nKHAC BIET FORMAT: {len(diffs)}")
        for line in diffs[:200]:
            print("  " + line)
        if len(diffs) > 200:
            print(f"  ... con {len(diffs) - 200} dong nua")
    else:
        print("\nFORMAT KHOP 100% - khong co khac biet")
    return 1 if diffs else 0


if __name__ == "__main__":
    sys.exit(main())
