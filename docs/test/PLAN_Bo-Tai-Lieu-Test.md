# Kế hoạch xây dựng bộ tài liệu test Clawbot (5.1 Unit / 5.2 Integration / 5.3 System)

Phiên bản plan: v1.0 - ngày lập: 2026-08-11
Đối tượng đọc: người thực hiện (QA/Dev) và các agent AI khác cùng tham gia xây tài liệu.
Nguyên tắc tối thượng: **output phải giống mẫu 100%** về cấu trúc, công thức, định dạng, data validation, vùng in.

---

## 1. Mục tiêu và phạm vi

### 1.0 Tiêu chí nghiệm thu (đã chốt lại 11/08/2026)

Hai tiêu chí đạt/không đạt, cả hai đều bắt buộc:

- **T1 - Format.** File dựng ra phải giống mẫu Excel về FORMAT. Nội dung trong các file `.xlsx` ở `docs/test/` chỉ là ví dụ minh hoạ định dạng, không phải dữ liệu phải bảo toàn.
- **T2 - Ngôn ngữ (chốt 11/08/2026).** Toàn bộ report bàn giao phải **100% tiếng Anh**, kể cả tên sheet. Đây là tiêu chí về nội dung duy nhất được nghiệm thu tự động, vì format khớp 100% vẫn không hề báo gì khi chữ là tiếng Việt.

Hệ quả với người đọc plan này:

- phần nội dung (mục 10, 11) là gợi ý cách viết, không phải điều kiện nghiệm thu - trừ ràng buộc ngôn ngữ T2;
- phần sổ lỗi (mục 6) giữ lại làm bằng chứng khảo sát, nhưng việc vá lỗi nội dung của file thật **không còn** là mục tiêu;
- thứ phải xanh trước khi bàn giao là lệnh ở mục 8.2;
- comment trong script vẫn viết tiếng Việt: T2 ràng buộc **file Excel bàn giao**, không ràng buộc mã nguồn.

### 1.1 Mục tiêu

Xây dựng bộ tài liệu test đầy đủ cho Clawbot gồm 3 tài liệu bàn giao, dựng từ 2 file mẫu và 1 file bản thật đang có trong `docs/test/`:

| Tài liệu bàn giao | File | Nguồn mẫu | Trạng thái |
|---|---|---|---|
| Unit Test Report | `docs/test/Report_Unit_Test_new.xlsx` | `Report5.1_Unit Test.xlsx!methodName1` | Đã dựng, format khớp 100% |
| Integration Test Report | `docs/test/Report_Integration_Test.xlsx` | `Report5.2!Login & Authentication` | Đã dựng, format khớp 100% |
| System Test Report | `docs/test/Report_System_Test.xlsx` | `Report5.3_System Test.xlsx!Workflow Name1` | Đã dựng, format khớp 100% |

Ba file trên đang mang **nội dung demo** trong `scripts/testdoc/content/*.sample.json`. Muốn ra bản thật thì sửa JSON rồi dựng lại - format không đổi. Tên `Report_Unit_Test_new.xlsx` được chọn để **không ghi đè** `Report_Unit_Test.xlsx` (bản thật 30 sheet method đang có sẵn).

### 1.2 Giả định đang áp dụng (nếu sai, xem mục 13 để chốt lại)

- **A1.** `Report5.1_Unit Test.xlsx` và `Report5.3_System Test.xlsx` là **file mẫu trắng, tuyệt đối không sửa**. Chúng là nguồn tham chiếu (source of truth) cho định nghĩa "giống mẫu 100%".
- **A2.** `Report_Unit_Test.xlsx` là bản thật của mẫu 5.1. `Report5.2_Integration Test.xlsx` là bản thật của họ 5.2 nhưng bị đặt trùng tên kiểu mẫu; giữ nguyên tên để không phá tham chiếu ngoài.
- **A3.** Bản System Test mới đặt tên `Report_System_Test.xlsx` cho khớp quy ước đặt tên của `Report_Unit_Test.xlsx`.
- **A4.** Trong mọi trường hợp, một sai lệch so với mẫu chỉ được chấp nhận khi (a) mẫu có lỗi rõ ràng và (b) sai lệch được ghi vào mục 6.1 kèm lý do.

### 1.3 Ngoài phạm vi

- Không sửa mã nguồn sản phẩm để phục vụ tài liệu.
- Không đổi tên/di chuyển 2 file mẫu.
- Không tự động hoá việc **chạy** test (chỉ ghi nhận kết quả); riêng Unit Test có phần đối chiếu với `tests/` - xem quyết định Đ5.

---

## 2. Hiện trạng 4 file (đã khảo sát trực tiếp)

| # | File | Dung lượng | Số sheet | Vai trò | Đặc điểm nổi bật |
|---|---|---|---|---|---|
| 1 | `Report5.1_Unit Test.xlsx` | ~60 KB | 7 | Mẫu trắng UT | `Guideline, Cover, MethodList, Statistics, methodName1, methodName2, Example` |
| 2 | `Report_Unit_Test.xlsx` | ~684 KB | 34 | Bản thật UT | 30 sheet method, ~292 UTCID, Creator `Diepnt`, Issue Date 2026-08-07 |
| 3 | `Report5.2_Integration Test.xlsx` | ~120 KB | 13 | Bản thật IT | 10 module, 174 TC, tester `Chienkv`, Issue Date 2026-08-09 |
| 4 | `Report5.3_System Test.xlsx` | ~45 KB | 5 | Mẫu trắng ST | `Cover, Test Cases, Test Statistics, Workflow Name1, Workflow Name2`; có comment (`vmlDrawing1-3.vml`) |

Ghi chú: file `.xls` gốc của mẫu 5.1 đã được convert sang `.xlsx` (bản `.xls` không còn trong thư mục). Mọi phân tích trong plan này dựa trên bản `.xlsx` hiện hành.

---

## 3. Định nghĩa "giống mẫu 100%" - 12 quy tắc bất biến

Mọi agent tham gia phải tuân thủ nguyên văn. Verifier ở mục 9.4 kiểm tự động từng quy tắc.

| ID | Quy tắc | Cách kiểm |
|---|---|---|
| R1 | **Tập sheet, thứ tự sheet, tên sheet** đúng khuôn mẫu. Tên sheet <= 31 ký tự, không chứa `[ ] : * ? / \` | so khớp `wb.sheetnames` |
| R2 | **Ô cố định** (nhãn, tiêu đề, hàng header) giữ nguyên chuỗi ký tự tuyệt đối, kể cả khoảng trắng thừa (ví dụ `Number of  test cases` có 2 dấu cách, `Precondition ` có dấu cách cuối) | so khớp chuỗi thô |
| R3 | **Công thức** phải cùng dạng với mẫu, chỉ thay đổi chỉ số dòng/cột và tên sheet. Cấm đổi sang hàm khác | regex + catalog mục 5 |
| R4 | **Merge cells** đúng bộ vùng của loại sheet tương ứng | so khớp `merged_cells.ranges` |
| R5 | **Data validation**: đúng `type`, đúng `formula1` (kể cả dấu nháy/khoảng trắng), phủ đủ vùng dữ liệu | so khớp `dataValidation` |
| R6 | **Font/fill/number format** nằm trong bộ mẫu cho phép (mục 5.x), không thêm màu/font mới | thống kê style |
| R7 | **Freeze panes, print area, cột ẩn** giữ nguyên (cột `R` ẩn ở sheet TC của 5.2/5.3, freeze `A11`) | so khớp thuộc tính sheet |
| R8 | **Không tồn tại `#REF!`** ở bất kỳ ô nào của file bàn giao | quét toàn workbook |
| R9 | **Nhất quán tham chiếu chéo**: mọi sheet được trỏ tới trong Statistics phải tồn tại; số dòng Statistics = số sheet dữ liệu | đối chiếu 2 chiều |
| R10 | **Số liệu sau recalc khớp đếm độc lập** bằng Python (P/F/N/A/B, tổng, %). Không chấp nhận giá trị cache cũ | recalc bằng Excel rồi so `data_only=True` |
| R11 | Không bao giờ mở file mẫu ở chế độ ghi. Mọi thao tác bắt đầu bằng `copy` sang file đích | quy trình + SHA256 |
| R12 | Không bao giờ dùng openpyxl để **lưu** file thuộc bộ này (xem cạm bẫy T1). openpyxl chỉ dùng để đọc/kiểm tra | review script |

---

## 4. Chiến lược kỹ thuật

### 4.1 Công cụ

| Việc | Công cụ | Lý do |
|---|---|---|
| Tạo/sửa/nhân bản sheet, ghi dữ liệu, chèn dòng | **Excel COM qua `pywin32`** (`win32com.client.Dispatch('Excel.Application')`, Excel 16.0 có sẵn trên máy) | Giữ nguyên style, DV, merge, comment VML, printer settings; `Rows.Insert` **tự dịch chuyển tham chiếu công thức**; có engine tính toán thật |
| Đọc, khảo sát, verify | **openpyxl (chỉ đọc)** | Nhanh, không cần Excel, đọc được cả formula string lẫn value cache |
| Recalc | Excel COM (`Application.CalculateFullRebuild()`) | LibreOffice **không có** trên máy nên `recalc.py` của skill xlsx không dùng được |

Skill `document-skills:xlsx` vẫn áp dụng cho phần đọc/khảo sát; riêng bước recalc và bước nhân bản sheet phải đi đường Excel COM như trên.

### 4.2 Vì sao bắt buộc COM (không dùng openpyxl để ghi)

`Report5.3_System Test.xlsx` chứa `xl/comments1-3.xml` + `xl/drawings/vmlDrawing1-3.vml` và 4 file `printerSettings*.bin`. openpyxl khi save sẽ làm mất VML/comment và printer settings, phá vỡ R7. Thêm nữa `openpyxl.insert_rows()` **không** dịch chuyển công thức, merge và data validation - vi phạm R3/R4/R5 ngay lập tức.

### 4.3 Kiến trúc script (tách nội dung khỏi cơ chế ghi)

Bố cục thực tế đã dựng (khác bản phác thảo đầu: bỏ `fingerprint.py`/`verify_testdocs.py`, gộp 2 builder theo dòng vào `rowdoc.py`):

```
scripts/testdoc/
  xl.py                 # helper COM: mở mẫu ra file mới, clone sheet, ghi ô, chèn dòng, recalc
  rowdoc.py             # dựng tài liệu theo dòng: --profile system (5.3) | integration (5.2)
  unitdoc.py            # dựng Unit Test 5.1 (ma trận ngang)
  compare_format.py     # nghiệm thu format cho 5.2/5.3
  compare_unit.py       # nghiệm thu format cho sheet method 5.1
  run_all.py            # dựng + nghiệm thu tất cả, một lệnh
  content/
    system_test.sample.json       # nội dung TC theo từng workflow
    integration_test.sample.json  # nội dung TC theo từng module
    unit_test.sample.json         # nội dung UTCID theo từng method
    unit_test.replica.json        # bản dựng lại y mẫu, chỉ để tự kiểm format
```

Lợi ích: agent viết **nội dung test** chỉ chạm file JSON trong `content/`; agent viết **cơ chế Excel** chỉ chạm `xl.py`/`rowdoc.py`/`unitdoc.py`. Hai luồng chạy song song không giẫm chân nhau, và cả hai đều bị `run_all.py` chặn nếu phá mẫu.

Vì sao bỏ `fingerprint.py` và `verify_testdocs.py`: khi tiêu chí nghiệm thu là "giống mẫu", so **trực tiếp** sheet kết quả với sheet mẫu vừa mạnh hơn vừa ít bảo trì hơn so với chụp vân tay rồi kiểm gián tiếp qua regex.

### 4.4 Khung `xl.py` (bắt buộc theo mẫu này)

```python
import contextlib, win32com.client as win32

XL_CALC_MANUAL, XL_CALC_AUTO = -4135, -4105

@contextlib.contextmanager
def excel_app(visible=False):
    app = win32.gencache.EnsureDispatch('Excel.Application')
    app.Visible, app.DisplayAlerts, app.ScreenUpdating = visible, False, False
    app.Calculation = XL_CALC_MANUAL          # tăng tốc khi ghi hàng loạt
    try:
        yield app
    finally:
        app.Calculation = XL_CALC_AUTO
        app.ScreenUpdating = True
        app.Quit()                            # bắt buộc, tránh EXCEL.EXE mồ côi

def clone_sheet(wb, src_name, new_name, after_name=None):
    """Nhân bản sheet mẫu: cách DUY NHẤT giữ đủ style/DV/merge/cột ẩn."""
    src = wb.Worksheets(src_name)
    anchor = wb.Worksheets(after_name or src_name)
    src.Copy(After=anchor)
    ws = wb.Worksheets(anchor.Index + 1)
    assert len(new_name) <= 31, f'ten sheet qua 31 ky tu: {new_name}'
    ws.Name = new_name
    return ws

def set_text(ws, addr, value):
    """Ghi chuỗi có thể bắt đầu bằng = + - @ mà không bị Excel hiểu là công thức."""
    cell = ws.Range(addr)
    cell.NumberFormat = '@'
    cell.Value2 = value

def set_formula(ws, addr, formula_en_us):
    ws.Range(addr).Formula = formula_en_us    # LUÔN dùng .Formula (dấu phẩy en-US)
```

Quy trình chuẩn của mọi script build:

```
shutil.copy2(template, target)        # R11
with excel_app() as app:
    wb = app.Workbooks.Open(abspath(target))
    ... clone_sheet / set_text / set_formula / Rows(n).Insert ...
    app.CalculateFullRebuild()        # R10
    wb.Save(); wb.Close(SaveChanges=False)
python scripts/testdoc/verify_testdocs.py target   # cổng chặn
```

---

## 5. Đặc tả cấu trúc từng tài liệu

Toàn bộ số liệu dưới đây lấy trực tiếp từ file, không phải suy đoán. Đây là hợp đồng mà mọi agent phải bám.

### 5.1 Unit Test (mẫu `Report5.1_Unit Test.xlsx` / bản thật `Report_Unit_Test.xlsx`)

**Sheet map mẫu:** `Guideline, Cover, MethodList, Statistics, methodName1, methodName2, Example`
**Sheet map bản thật:** `Guideline, Cover, MethodList, Statistics` + 30 sheet method:
`IsAllowedBaseUrl, CreateGuardedHttpClient, Decode, DecryptOrRaw, RotateAsync, ValidateStorageKey, CompleteAsyncAnthropicChat, CompleteAsyncOpenAiChat, ParseDirectReply, NormalizeApiKey, CountPromptCountText, ResolveFromConfig, CheckBody, LanguageMismatch, CopiesBrief, SelectHook, ParsePlanParseOutlineParsePacka, RunAsync, ResumeFromWriteAsync, ReviewAsync, LooksLikeInstructionInjection, ParseVision, CheckContainsAbsoluteGuarantee, RedactHistoryAsync, IsCostCapReached, ApplyOnceAsync, IsAmbiguousMetaFailure, ExecuteAsync, PollConversationsAsync, NeedsLlmReview`

#### 5.1.1 Sheet `Cover`

| Ô | Nội dung |
|---|---|
| `B2` | `UNIT TEST DOCUMENT` |
| `A4/B4` | `Project Name` / tên dự án; `E4` `Creator` |
| `A5/B5` | `Project Code` / mã dự án; `E5` `Issue Date`, `F5` ngày phát hành |
| `B6` | `=B5&"_"&"XXX"&"_"&"vx.x"`; `E6` `Version` |
| `A9` | `Record of change` |
| Hàng 10 | `Effective Date, Version, Change Item, *A,D,M, Change description, Reference` |
| Merge | `B2:F2, B4:D4, B5:D5, B6:D6` |

#### 5.1.2 Sheet `MethodList`

| Ô | Mẫu | Bản thật hiện tại |
|---|---|---|
| `C2` | `Method List` | như mẫu |
| `A4/C4` | `Project Name` / `=Cover!B4` | như mẫu |
| `A5/C5` | `Project Code` / `=Cover!B5` | như mẫu |
| `A6/C6` | `Test Environment Setup Description` | đã điền 10 dòng môi trường (FE :15876, API :15874, gRPC :15875, Gateway :15873, SQL :1433, Redis :6379, Qdrant :6333, MinIO :9001, RabbitMQ :15672, dev account) |
| Hàng 8 | `A8 No, B8 Module Name, C8 Method Name, D8 Sheet Name, E8 Description, F8 Pre-Condition` | **thiếu cột Description**, `E8` đang là `Pre-Condition` - xem lỗi D3 |
| Merge | `A4:B4, A5:B5, A6:B6, C4:F4, C5:F5, C6:F6` | như mẫu |
| Print area | `$A$1:$F$36` | cần mở rộng theo số dòng thật |
| Dữ liệu | từ dòng 9 | 30 dòng (9-38) |

#### 5.1.3 Sheet `Statistics`

| Mục | Mẫu | Bản thật |
|---|---|---|
| `A2` | `UNIT TEST REPORT` | như mẫu |
| `B6` | `=B5&"_"&"Test Report"&"_"&"vx.x"` | như mẫu |
| Hàng 11 | `No, Function code, Passed, Failed, Untested, N, A, B, Total Test Cases` | như mẫu |
| Dòng dữ liệu | 12-15 (2 dòng mẫu) | 12-41 (30 dòng) |
| Công thức dòng dữ liệu | `C=<Sheet>!A5`, `D=<Sheet>!C5`, `E=<Sheet>!F5`, `F=<Sheet>!L5`, `G=<Sheet>!M5`, `H=<Sheet>!N5`, `I=<Sheet>!O5` | như mẫu |
| Sub total | dòng 16: `=SUM(C10:C15)` ... `=SUM(I10:I15)` | dòng 44: **chỉ `C44`, `I44` đúng** - xem lỗi D1 |
| Coverage | dòng 18-22 | dòng 46-50 |
| Test coverage | `=(C16+D16)*100/(I16)` | `=(C44+D44)*100/(I44)` |
| Test successful coverage | `=C16*100/(I16)` | `=C44*100/(I44)` |
| Normal / Abnormal / Boundary case | `=F16*100/I16`, `=G16*100/I16`, `=H16*100/I16` | `=F44*100/I44`, `=G44*100/I44`, `=H44*100/I44` |
| Print area | `$A$1:$I$40` | mở rộng theo số dòng thật |

#### 5.1.4 Sheet method (khuôn quan trọng nhất)

Cấu trúc đầu sheet (giống nhau ở mẫu và bản thật):

```
A1 Code Module      | C1 <tên class>   | F1 Method | L1 <tên method>
A2 Created By       | C2 <người tạo>   | F2 Executed By
A3 Test requirement | C3 <mô tả yêu cầu được test>
A4 Passed | C4 Failed | F4 Untested | L4 N/A/B
A5..O5 = hàng công thức đếm (xem bảng dưới)
Hàng 7  = hàng UTCID: E7 trống, F7 'UTCID01', G7 'UTCID02', ...
```

Hàng công thức (dòng 5) - **X = dòng `Type`, Y = dòng `Passed/Failed`**:

| Ô | Công thức | Ý nghĩa |
|---|---|---|
| `A5` | `=COUNTIF(F{Y}:HW{Y},"P")` | số case Passed |
| `C5` | `=COUNTIF(F{Y}:HW{Y},"F")` | số case Failed |
| `F5` | `=SUM(O5,-A5,-C5)` | Untested |
| `L5` | `=COUNTIF(E{X}:HW{X},"N")` | Normal |
| `M5` | `=COUNTIF(E{X}:HW{X},"A")` | Abnormal |
| `N5` | `=COUNTIF(E{X}:HW{X},"B")` | Boundary |
| `O5` | `=COUNTA(E7:HZ7)` | tổng số UTCID |

Ví dụ thật, sheet `IsAllowedBaseUrl` (21 UTCID, `F7:Z7`):

| Vùng | Vị trí | Nội dung |
|---|---|---|
| Condition | `A8` `Condition`, `B8` `Input` | các dòng điều kiện, đánh dấu `O` |
| | `B28` `Option` | |
| | `B37` `DNS Stub` | |
| Confirm | `A43` `Confirm`, `B43` `Return` | |
| Result | `A72` `Result`, `B72` `Type(N : Normal, A : Abnormal, B : Boundary)` | X = 72 |
| | `B73` `Passed/Failed` | Y = 73 |
| | `B74` `Executed Date` | |
| | `B75` `Defect ID` | |
| Merge | `A8:A42`, `B72:D72`, `B73:D73`, `B74:D74`, `B75:D75` | |
| DV | `list "O"` trên `F8:Z71`; `list "N,A,B"` trên `F72:Z72`; `list "P,F"` trên `F73:Z73` | |
| Row-5 | `A5 =COUNTIF(F73:HW73,"P")`, `L5 =COUNTIF(E72:HW72,"N")`, `O5 =COUNTA(E7:HZ7)` | khớp X=72, Y=73 |

Khuôn mẫu trắng `methodName1` để đối chiếu: bands ở `A8 Condition / B8 'Precondition '`, `B12 Date`, `B16 Month`, `B20 Year`, `A29 Confirm / B29 Return`, `B32 Exception`, `B34 Log message`, `A37 Result / B37 Type(...)`, `B38 Passed/Failed`, `B39 Executed Date`, `B40 Defect ID`; DV `"O, "` trên `F8:T36`, `"N,A,B, "` trên `F37:T37`, `"P,F, "` trên `F38:T38`; print area `$A$1:$T$51`.

> Lưu ý R5: chuỗi DV của **mẫu trắng** có dấu phẩy + khoảng trắng ở cuối (`"P,F, "`), còn **bản thật** dùng `"P,F"`. Khi bổ sung sheet mới vào `Report_Unit_Test.xlsx` thì clone từ sheet **trong chính file đó** để nhất quán nội bộ - không copy chéo file.

Font cho phép ở họ 5.1: Tahoma 8/10 (regular + bold), Courier New 8/12. Mẫu có sẵn defined name `ACTION = #REF!` (di sản; giữ nguyên, không tính vi phạm R8 vì nằm ở defined name chứ không ở ô).

### 5.2 Integration Test (`Report5.2_Integration Test.xlsx`)

**Sheet map (13):** `Cover, Test Cases, Test Statistics` + 10 sheet module:
`Login & Authentication, Omnichannel Inbox, Knowledge Base, AI Agent Management, Sale Assist, Lead & CRM, Content Management, Document Generation, Analytics & Report, Admin & Security`
(số TC lần lượt 23/23/19/12/12/22/19/12/11/21 = **174 TC**).

#### 5.2.1 Sheet module (khuôn TC)

| Ô | Nội dung |
|---|---|
| `A2/B2` | `Feature` / tên module (Test Statistics lấy chính ô này làm Module code) |
| `A3/B3` | `Test requirement` / mô tả phạm vi |
| `A4/B4` | `Number of TCs` / `=COUNTA(A12:A1000)-3` (hằng số chỉnh tay theo số dòng nhóm scenario - xem lỗi D5) |
| Hàng 5 | `A5 Testing Round`, `B5 Passed`, `C5 Failed`, `D5 Pending`, `E5 N/A` |
| Hàng 6/7/8 | `Round 1/2/3`, mỗi hàng 4 công thức `=COUNTIF($F10:$F998,B5)`... (xem lỗi D6) |
| Hàng 10 | `Test Case ID, Test Case Description, Test Case Procedure, Expected Results, Pre-conditions, Round 1, Test date, Tester, Round 2, Test date, Tester, Round 3, Test date, Tester, Note` (A..O) |
| Dữ liệu | từ dòng 11: dòng nhóm scenario merge `A{n}:O{n}`, xen kẽ các dòng TC |
| Cột ẩn `R` | `R2 Passed`, `R3 Failed`, `R4 Pending`, `R5 N/A` - nguồn DV |
| DV | `list $R$2:$R$5` áp cho cột `F`, `I`, `L` của các dòng TC |
| Freeze | `A11` |
| Merge cố định | `B2:E2, B3:E3, B4:E4, F2:O9` + các dòng nhóm scenario |

#### 5.2.2 Sheet `Test Cases`

`D3 =Cover!B4`, `D4 =Cover!B5`, `D5` mô tả môi trường; hàng 8: `B8 No, C8 Test cases Name, D8 Sheet Name, E8 Description, F8 Pre-Condition`; dữ liệu dòng 9-182.

#### 5.2.3 Sheet `Test Statistics`

| Ô | Nội dung |
|---|---|
| `B1` | `TEST STATISTICS` |
| `C3` | `=Cover!B4`; `E3 Creator`, `G3 Chienkv` |
| `C4` | `=Cover!B5`; `E4 Reviewer/Approver` |
| `C5` | `=C4&"_"&"Test Report"&"_"&"vx.x"`; `E5 Issue Date`, `G5` ngày |
| `C6` | `Notes` - bảng chú thích mã HTTP (`s = 200 OK`, `s = 400 bad request`, ...) |
| Hàng 10 | `No, Module code, Passed, Failed, Pending, N/A, Number of  test cases` |
| Dòng 11-20 | `C='<Sheet>'!B2`, `D='<Sheet>'!B8`, `E='<Sheet>'!C8`, `F='<Sheet>'!D8`, `G='<Sheet>'!E8`, `H='<Sheet>'!B4` |
| Dòng 21 | `Sub total` `=SUM(D9:D20)` ... `=SUM(H9:H20)` |
| `E23` | `=(D21+E21)*100/(H21-G21)` - Test coverage |
| `E24` | `=D21*100/(H21-G21)` - Test successful coverage |

Style: chỉ font Tahoma 10; number format ngày `dd/MM/yyyy`; fill dùng `FFFFFFFF, FF76923C, FFE2EFDA, FF000080, FF333399`.

### 5.3 System Test (mẫu `Report5.3_System Test.xlsx` -> bản thật `Report_System_Test.xlsx`)

**Sheet map mẫu (5):** `Cover, Test Cases, Test Statistics, Workflow Name1, Workflow Name2`.

| Sheet | Điểm neo |
|---|---|
| `Cover` | `B2 SYSTEM TEST REPORT DOCUMENT`; `A4/B4 Project Name`, `A5/B5 Project Code`, `E5 Issue Date` + `F5`, `B6 =B5&"_"&"XXX"&"_"&"vx.x"`, `E6 Version`; `A9 Record of change`, hàng 10 header change log; merge `B2:F2, B4:D4, B5:D5, B6:D6` |
| `Test Cases` | `D3 =#REF!`, `D4 =#REF!` (**lỗi mẫu D7**, phải thành `=Cover!B4` / `=Cover!B5`); `D5` mô tả môi trường; hàng 8 `B8 No, C8 Function Name, D8 Sheet Name, E8 Description, F8 Pre-Condition`; dữ liệu 9-13 |
| `Test Statistics` | như 5.2 nhưng dòng dữ liệu 11-12, cột `D/E/F/G` trỏ **hàng 6 (Round 1)**: `='Workflow Name1'!B6` ...; `H` trỏ `!B4`; Sub total dòng 14 `=SUM(D9:D13)`; `E16 =(D14+E14)*100/(H14-G14)`; `E17 =D14*100/(H14-G14)` |
| `Workflow Name1/2` | `A2 Workflow / B2 <Workflow NameN>`; `B4 =COUNTA(A12:A1000)` (mẫu **không** trừ hằng số); hàng 5-8 giống 5.2; hàng 10 header A..O giống 5.2; dòng 11 `Scenario A`, dòng 12-14 `<ID1>..<ID3>` kèm văn bản hướng dẫn, dòng 15 `Scenario B` + `<ID4>..<ID6>`; `F/I/L` = `Pending`; cột ẩn `R2:R5`; freeze `A11`; DV `list $R$2:$R$5` + 1 DV rỗng (`type=None`) trên `F10 I10 L10` (giữ nguyên) |

---

## 6. Sổ lỗi đã phát hiện (defect register) - kèm bằng chứng

Mỗi lỗi có: mức độ, ô cụ thể, hậu quả, cách sửa. Toàn bộ đều đã kiểm chứng trực tiếp trên file.

| ID | Mức | File / Ô | Hiện trạng | Hậu quả | Cách sửa |
|---|---|---|---|---|---|
| **D1** | CAO | `Report_Unit_Test.xlsx` `Statistics!D44:H44` | `=SUM(D10:D16)`, `=SUM(E10:E16)`, `=SUM(F10:F16)`, `=SUM(G10:G16)`, `=SUM(H10:H16)` trong khi dữ liệu nằm ở dòng 12-41 (chỉ `C44 =SUM(C12:C41)` và `I44 =SUM(I12:I41)` đúng) | Sub total Failed/Untested/N/A/B sai; kéo theo `D46` Test coverage và `D48-D50` Normal/Abnormal/Boundary sai | Sửa thành `=SUM(D12:D41)` ... `=SUM(H12:H41)` |
| **D2** | CAO | `Report_Unit_Test.xlsx` `SelectHook!F5,N5,O5` | `F5 =SUM(P5,-A5,-C5)` (trỏ `P5` rỗng), `N5 =COUNTIF(E27:HR27,"A")` (trùng `M5`), `O5 =COUNTIF(E27:HR27,"B")` - thiếu hẳn `=COUNTA(E7:HZ7)` | Sheet duy nhất lệch khuôn: `Statistics!H27` đếm nhầm, `I27` (Total Test Cases) lấy số case Boundary thay vì 8 UTCID, Untested sai | Đưa về khuôn chuẩn: `F5 =SUM(O5,-A5,-C5)`, `N5 =COUNTIF(E27:HR27,"B")`, `O5 =COUNTA(E7:HZ7)` |
| **D3** | TRUNG BÌNH | `Report_Unit_Test.xlsx` `MethodList!E8` | `E8 = Pre-Condition`, thiếu cột `Description` mà mẫu quy định (`E8 Description`, `F8 Pre-Condition`) | Vi phạm R2; mất cột mô tả method | Chèn cột: `E` = Description, dời Pre-Condition sang `F`; điền mô tả đủ 30 dòng |
| **D4** | TRUNG BÌNH | `Report_Unit_Test.xlsx` `MethodList!C6` | Ghi thẳng `Dev account: admin@clawbot.local / Admin@12345`; `Report5.2` cũng nhắc cặp này trong nhiều Test Case Procedure | Tài liệu bàn giao chứa credential; dù là tài khoản dev cục bộ vẫn nên xử lý | Xem quyết định Đ6 |
| **D5** | TRUNG BÌNH | `Report5.2` `<module>!B4` | `=COUNTA(A12:A1000)-3` với hằng số chỉnh tay khác nhau từng sheet (-3,-5,-4,-3,-4,-5,-3,-2,-6,-6), đúng bằng số dòng nhóm scenario | Thêm/bớt 1 nhóm scenario là tổng TC sai âm thầm; `Test Statistics!H` sai theo | Thay bằng công thức tự thích ứng `=COUNTIF($A$12:$A$1000,"TC-*")` |
| **D6** | CAO | `Report5.2` **và** mẫu `Report5.3`, hàng 7 và 8 mọi sheet TC | `B7:E7` và `B8:E8` vẫn là `=COUNTIF($F10:$F998,...)` - trỏ cột `F` (Round 1) thay vì `I` (Round 2) và `L` (Round 3) | Thống kê Round 2/Round 3 luôn bằng Round 1; `Test Statistics` của 5.2 lấy hàng 8 nên số liệu bàn giao thực chất là Round 1 | Sửa `B7:E7` -> `$I10:$I998`, `B8:E8` -> `$L10:$L998`. Lỗi có sẵn trong mẫu -> cần chốt Đ3 |
| **D7** | CAO | mẫu `Report5.3` `Test Cases!D3,D4` | `=#REF!` | Vi phạm R8 ngay khi copy mẫu | Trong bản thật đặt `=Cover!B4`, `=Cover!B5` (khớp cách 5.2 làm) |
| **D8** | THẤP | `Report_Unit_Test.xlsx` tên sheet | `ParsePlanParseOutlineParsePacka` bị cắt còn 31 ký tự; nhiều tên sheet bỏ ký tự `/` | Tên sheet khác tên method thật | Giữ nguyên tên sheet; cột `Method Name` ghi tên đầy đủ (`ParsePlan / ParseOutline / ParsePackage`) - hiện đã đúng |
| **D9** | THẤP | `Report_Unit_Test.xlsx` `Cover` | Bảng Record of change chưa có dòng lịch sử phát hành | Thiếu vết phiên bản | Bổ sung ít nhất 1 dòng (ngày, version, `A`, mô tả, reference) |
| **D10** | THẤP | `Report_Unit_Test.xlsx` `<sheet method>!C3` | 30/30 sheet vẫn giữ placeholder `<Brief description about requirements which are tested in this function>` | Thiếu nội dung Test requirement | Điền mô tả thật cho từng method |

### 6.1 Sai lệch so với mẫu được phê duyệt (ghi nhận công khai)

| Mã | Sai lệch | Lý do |
|---|---|---|
| DEV-1 | `Number of TCs` dùng `=COUNTIF($A$12:$A$1000,"TC-*")` thay `=COUNTA(A12:A1000)` | Mẫu không tính tới dòng nhóm scenario; công thức mẫu cho số sai |
| DEV-2 | Hàng 7/8 sheet TC trỏ `$I` và `$L` | Sửa lỗi D6 của mẫu; nếu giữ nguyên thì Round 2/3 vô nghĩa |
| DEV-3 | `Test Cases!D3/D4` thay `#REF!` bằng `=Cover!B4/B5` | Bắt buộc bởi R8 |

Ba sai lệch trên chỉ áp cho **bản thật**; file mẫu không đụng tới.

---

## 7. Cạm bẫy kỹ thuật (đọc trước khi code)

| ID | Cạm bẫy | Cách né |
|---|---|---|
| T1 | openpyxl `save()` làm mất comment VML (`vmlDrawing*.vml` của 5.3), printerSettings, một số style | Chỉ đọc bằng openpyxl; ghi bằng COM (R12) |
| T2 | `openpyxl.insert_rows()` không dịch công thức/merge/DV | Dùng `ws.Rows(n).Insert()` của COM - Excel tự dịch tham chiếu |
| T3 | Không có LibreOffice -> `recalc.py` của skill xlsx không chạy | Recalc bằng `app.CalculateFullRebuild()` |
| T4 | Tiến trình `EXCEL.EXE` mồ côi khoá file ở lần chạy sau | Luôn `try/finally` + `app.Quit()`; kiểm `Get-Process EXCEL` trước khi build lại |
| T5 | Excel locale: `.FormulaLocal` dùng dấu `;`, `.Formula` dùng dấu `,` | **Chỉ dùng `.Formula`** với dấu phẩy en-US |
| T6 | Tên sheet có dấu cách hoặc `&` phải bọc nháy đơn trong công thức chéo sheet | `='Login & Authentication'!B2` |
| T7 | Giới hạn 31 ký tự tên sheet + nguy cơ trùng sau khi cắt | Hàm chuẩn hoá tên + assert độ dài + kiểm trùng |
| T8 | Chuỗi bắt đầu bằng `=`, `+`, `-`, `@` bị hiểu là công thức | Dùng `set_text()` (đặt `NumberFormat='@'` trước khi ghi) |
| T9 | Cột ẩn `R` là nguồn DV; clone sheet mà xoá cột R sẽ làm hỏng dropdown toàn sheet | Luôn clone từ sheet có sẵn, không dựng sheet trắng |
| T10 | Chèn dòng trong vùng Condition của sheet UT làm dịch dòng `Type`/`Passed`, người viết hay quên kiểm lại hàng 5 | Verifier bắt buộc so `X`/`Y` thực tế với tham chiếu trong `A5/C5/L5/M5/N5` |
| T11 | Ngày ghi dạng chuỗi không khớp `dd/MM/yyyy` và làm hỏng sắp xếp | Ghi `datetime`, set `NumberFormat='dd/MM/yyyy'` |
| T12 | Giá trị hiển thị chỉ là cache; sửa công thức mà không recalc thì số cũ vẫn nằm đó | Bắt buộc bước recalc + verify R10 |
| T13 | Sheet mẫu `methodName1/methodName2/Example` (hoặc `Workflow Name1/2`) lỡ còn sót trong bản thật sẽ phá R1 và làm thống kê sai | Verifier kiểm danh sách sheet cấm |
| T14 | Ghi từng ô qua COM rất chậm với hàng nghìn ô | Ghi theo khối: gán mảng 2 chiều cho `Range("A12:O40").Value2` |

---

## 8. Trạng thái thực thi (đã chạy được, không phải kế hoạch)

Toàn bộ mục này mô tả thứ đã tồn tại trong repo và đã chạy xanh, không phải việc dự kiến.

### 8.1 Bộ công cụ đã bàn giao

| File | Vai trò |
|---|---|
| `scripts/testdoc/xl.py` | Lớp bọc Excel COM: mở mẫu ra file mới (không bao giờ ghi đè mẫu), clone sheet, chèn dòng, ghi text/số/ngày/công thức, recalc `CalculateFullRebuild()` |
| `scripts/testdoc/rowdoc.py` | Dựng tài liệu **dạng bảng theo dòng**: `--profile system` (5.3) và `--profile integration` (5.2) |
| `scripts/testdoc/unitdoc.py` | Dựng tài liệu **Unit Test 5.1** (ma trận ngang: UTCID theo cột, điều kiện theo dòng) |
| `scripts/testdoc/compare_format.py` | Bộ nghiệm thu format cho tài liệu theo dòng (5.2/5.3) |
| `scripts/testdoc/compare_unit.py` | Bộ nghiệm thu format cho sheet method 5.1 (ánh xạ cả dòng lẫn cột) |
| `scripts/testdoc/check_english.py` | Cổng ngôn ngữ (tiêu chí T2): quét mọi ô chữ + tên sheet, bắt cả chữ có dấu lẫn tiếng Việt bỏ dấu |
| `scripts/testdoc/run_all.py` | Dựng lại cả 3 họ rồi nghiệm thu format + ngôn ngữ - **một lệnh duy nhất**, kèm đối chứng âm |
| `scripts/testdoc/content/*.json` | Nội dung demo: `system_test.sample.json`, `integration_test.sample.json`, `unit_test.sample.json`, `unit_test.replica.json` |

Script `build_system_test.py` trong bản plan đầu đã bị xóa: `rowdoc.py` làm cùng việc đó cho cả 2 họ tài liệu theo dòng.

`fingerprint.py` và `verify_testdocs.py` không được dựng. Lý do: khi tiêu chí nghiệm thu chuyển thành "format giống mẫu", so trực tiếp sheet kết quả với sheet mẫu mạnh hơn và ít bảo trì hơn so với việc chụp vân tay rồi kiểm gián tiếp qua regex.

### 8.2 Lệnh kiểm nhanh (chạy sau mỗi lần sửa script)

```bash
python scripts/testdoc/run_all.py
```

Dựng lại cả 3 họ tài liệu vào thư mục tạm rồi chạy hết 11 bước nghiệm thu (4 bước dựng, 1 cổng ngôn ngữ, 5 bước format, 1 đối chứng âm). Phải in `TAT CA DEU DAT`; exit khác 0 nghĩa là format đã lệch khỏi mẫu hoặc có chữ tiếng Việt lọt vào file ra. Thêm `--out-dir out` nếu muốn giữ file lại để mở xem.

Bước "đối chứng âm" trong runner cố tình yêu cầu 2 sheet khác nhau phải ra **khác biệt**. Nếu bước đó cũng "khớp 100%" thì bộ nghiệm thu đang mù và mọi kết quả xanh phía trên đều vô nghĩa.

### 8.3 Lệnh dựng

```bash
# 5.3 System Test
python scripts/testdoc/rowdoc.py --profile system \
    --content scripts/testdoc/content/system_test.sample.json \
    --out "docs/test/Report_System_Test.xlsx"

# 5.2 Integration Test
python scripts/testdoc/rowdoc.py --profile integration \
    --content scripts/testdoc/content/integration_test.sample.json \
    --out "docs/test/Report_Integration_Test.xlsx"

# 5.1 Unit Test
python scripts/testdoc/unitdoc.py \
    --content scripts/testdoc/content/unit_test.sample.json \
    --out "docs/test/Report_Unit_Test_new.xlsx"
```

### 8.4 Lệnh nghiệm thu format

```bash
# 5.3: sheet workflow + 3 sheet cố định
python scripts/testdoc/compare_format.py \
    --base "docs/test/Report5.3_System Test.xlsx" --target "<file ra>" \
    --pair "Workflow Name1=WF01 Onboarding" --pair "Workflow Name1=WF02 Auto-reply" \
    --pair "Workflow Name1=WF03 Incident Recovery" \
    --pair "Cover=Cover" --pair "Test Cases=Test Cases" \
    --pair "Test Statistics=Test Statistics" \
    --header-rows 10 --data-row 11 --dv-cols F,I,L --check-ref

# 5.2: LUÔN so với sheet chuẩn 'Login & Authentication'
python scripts/testdoc/compare_format.py \
    --base "docs/test/Report5.2_Integration Test.xlsx" --target "<file ra>" \
    --pair "Login & Authentication=Login & Authentication" \
    --pair "Login & Authentication=Omnichannel Inbox" \
    --pair "Cover=Cover" --pair "Test Cases=Test Cases" \
    --pair "Test Statistics=Test Statistics" \
    --header-rows 10 --data-row 11 --dv-cols F,I,L --check-ref

# 5.1: sheet method so với methodName1; 3 sheet còn lại so bằng compare_format
python scripts/testdoc/compare_unit.py \
    --base "docs/test/Report5.1_Unit Test.xlsx" --target "<file ra>" \
    --pair "methodName1=IsAllowedBaseUrl" --pair "methodName1=NormalizeApiKey"
python scripts/testdoc/compare_format.py --base "docs/test/Report5.1_Unit Test.xlsx" \
    --target "<file ra>" --pair "MethodList=MethodList" --header-rows 8 --data-row 9
```

Trên Windows phải đặt `PYTHONIOENCODING=utf-8` trước khi chạy comparator, nếu không dòng khác biệt có ký tự tiếng Việt sẽ làm crash `print` (cp1252).

### 8.4b Lệnh nghiệm thu ngôn ngữ (tiêu chí T2)

```bash
python scripts/testdoc/check_english.py \
    --file "docs/test/Report_System_Test.xlsx" \
    --file "docs/test/Report_Integration_Test.xlsx" \
    --file "docs/test/Report_Unit_Test_new.xlsx"
```

Phải in `TIENG ANH 100%`. Script bắt 2 dạng lọt lưới:

1. **chữ có dấu** - quét bảng chữ cái riêng của tiếng Việt (`ă â ê ô ơ ư đ` + nguyên âm có dấu thanh); tiếng Anh không dùng ký tự nào trong nhóm này nên chỉ cần 1 ký tự là đủ kết luận;
2. **tiếng Việt bỏ dấu** - đối chiếu danh sách từ (`khong`, `ket`, `noi`, `kenh`, `nap`, `lieu`...). Danh sách chỉ chứa từ **không trùng** với từ tiếng Anh nào; các từ như `can`, `may`, `them`, `day`, `he`, `so`, `to` cố tình bị loại để tránh báo nhầm câu tiếng Anh.

Cổng này cũng đã được đối chứng âm: dựng một workbook có sheet `WF03 Khoi phuc su co` + ô `Kết nối kênh Facebook` + ô `Nap tai lieu vao he thong` thì script bắt đủ 3 chỗ, còn ô tiếng Anh cùng file thì không bị báo.

### 8.5 Kết quả đã đo

| Kiểm tra | Kết quả |
|---|---|
| 5.3 System Test - sheet workflow + Cover/Test Cases/Test Statistics | `FORMAT KHỚP 100%` |
| 5.2 Integration Test - sheet module + 3 sheet cố định | `FORMAT KHỚP 100%` |
| 5.1 bản replica (dựng lại đúng kích thước `methodName1`) | `FORMAT KHỚP 100%`, so **từng ô** |
| 5.1 bản mẫu thật (18 UTCID nở cột, 6 UTCID + band ngắn hơn) | `FORMAT KHỚP 100%` |
| 5.1 `MethodList` | `FORMAT KHỚP 100%` |
| 5.1 `Cover` + `Statistics` | 24 dòng khác biệt, **trùng khít** với bản mở-rồi-lưu không sửa gì (mục 8.6) |
| Ngôn ngữ (T2) - cả 3 file bàn giao | `TIẾNG ANH 100%`, 0 chuỗi tiếng Việt |
| Đối chứng âm: `methodName1` vs `LanguageMismatch` (file thật) | 704 khác biệt - comparator không mù |
| Đối chứng âm: `Login & Authentication` vs `Knowledge Base` | 470+ khác biệt |
| Đối chứng âm cổng ngôn ngữ: workbook cài sẵn 3 chuỗi tiếng Việt | bắt đúng 3/3, không báo nhầm dòng tiếng Anh |

### 8.6 Sai lệch có chủ ý so với mẫu (DEV)

Mỗi mục dưới đây là chỗ **cố tình** không copy y mẫu, vì mẫu sai hoặc vì số dòng đã đổi.

| ID | Chỗ | Mẫu | Đang dùng |
|---|---|---|---|
| DEV-1 | 5.2 `B4` Number of TCs | `=COUNTA(A12:A1000)-3` (hằng số `-3` chỉnh tay) | `=COUNTIF($A$12:$A$1000,"TC-*")` |
| DEV-2 | 5.2/5.3 hàng đếm Round 2/3 | cả 3 vòng đều đếm cột `F` | Round 2 đếm `I`, Round 3 đếm `L` |
| DEV-3 | 5.3 `Test Cases!D3/D4` | `#REF!` | `=Cover!B4`, `=Cover!B5` |
| DEV-4 | 5.2/5.3 `Test Statistics` Sub total + 2 dòng coverage | SUM cố định theo số dòng mẫu | viết lại theo số dòng thật sau khi chèn/xóa |
| DEV-5 | 5.1 `Statistics` Sub total + 5 dòng tỷ lệ | `=SUM(C10:C15)` trong khi dữ liệu bắt đầu ở dòng 12 | viết lại theo khối dữ liệu thật |

### 8.7 Dung sai đã chứng minh (không phải lỗi builder)

Bốn nhóm khác biệt dưới đây do Excel/openpyxl biểu diễn, không do builder. Mỗi nhóm đều đã kiểm bằng đối chứng, không phải suy đoán.

| ID | Hiện tượng | Bằng chứng |
|---|---|---|
| ART-1 | `customHeight` của openpyxl coi cache chiều cao tự động là chiều cao đặt tay | Đọc thẳng thuộc tính XML thật (`raw_row_dims`) thì hết khác biệt |
| ART-2 | Định dạng ngày `dd/MM/yyyy` bị Excel ghi lại thành numFmtId 14 built-in theo locale máy | Mở mẫu bằng COM rồi lưu lại **không sửa gì** cũng bị đúng như vậy |
| ART-3 | Border rỗng lúc thì `None` lúc thì `(None, None)` | Chuẩn hóa trong `border_key`, 4608 khác biệt giả biến mất |
| ART-4 | 5.1: ô rỗng ở `Cover` dòng 7-8 và `Statistics` dòng 10 mất style Tahoma/fill indexed (mẫu gốc convert từ `.xls`) | Bản mở-rồi-lưu không sửa gì cho **đúng 24 dòng khác biệt y hệt** bản builder dựng ra; `comm -13` giữa 2 bản = rỗng |

Cách kiểm lại ART-4 khi nghi ngờ:

```bash
# mở mẫu bằng COM rồi lưu lại, không sửa một ô nào
python -c "import sys; sys.path.insert(0,'scripts/testdoc'); import xl
with xl.workbook_from_template('docs/test/Report5.1_Unit Test.xlsx','out/roundtrip.xlsx') as (app, wb):
    pass"
# rồi so bản roundtrip với mẫu: khác biệt còn lại chính là phần Excel tự làm
python scripts/testdoc/compare_format.py --base "docs/test/Report5.1_Unit Test.xlsx" \
    --target out/roundtrip.xlsx --pair "Cover=Cover" --header-rows 10 --data-row 11
```

---

## 9. Cơ chế co giãn - phần dễ làm hỏng format nhất

### 9.1 Tài liệu theo dòng (5.2, 5.3) - `rowdoc.py`

- Bố cục khối dữ liệu (dòng nhóm / dòng case) **đọc từ chính sheet mẫu** lúc chạy, không hard-code.
- Thứ tự bắt buộc: **thêm dòng ở cuối khối -> đổi loại từng dòng -> xóa phần dư ở cuối**. Nhờ vậy dòng mẫu không bao giờ bị dịch chỉ số hay bị ghi đè giữa chừng.
- Đổi loại dòng đi qua vùng tạm (staging) đặt **dưới** khối dữ liệu, xong thì xóa vùng tạm.
- Nhân bản dòng chỉ được làm bằng "copy dòng rồi Insert" (Insert Copied Cells). `PasteSpecial(xlPasteFormats)` **không** mang theo data validation.
- 5.2 dùng chính sheet module làm khuôn nên tên sheet kết quả có thể trùng tên sheet khuôn: phải đổi tên khuôn thành `~tmpN` trước khi clone, xóa khuôn sau cùng.

### 9.2 Unit Test (5.1) - `unitdoc.py`

Sheet method là ma trận ngang nên phải co giãn hai chiều.

**Chiều dòng (điều kiện).** Mỗi band (`Condition`, `Confirm`) có dạng `[dòng đầu mang viền trên][dòng giữa...][dòng cuối mang viền dưới]`. Nhóm điều kiện = 1 dòng nhãn (cột `B`) + N dòng giá trị (cột `D`). Co giãn:

- nở: nhân bản dòng `band_end - 1` rồi Insert tại `band_end`;
- co: xóa từ `band_start + needed - 1` đến `band_end - 1`;
- luôn chỉnh **từ band dưới lên band trên** để chỉ số dòng của band trên không bị xê dịch.

**Chiều cột (UTCID).** Lưới mẫu rộng 15 cột `F..T`, cột `T` mang viền phải.

- nở: nhân bản cột `T-1` rồi Insert tại `T` - merge tiêu đề `C3:T3`, `L1:T1`, `O4:T4`, `O5:T5` tự nới theo, cột viền phải vẫn nằm ngoài cùng;
- **không bao giờ co dưới 15 cột.** Ô neo của các merge tiêu đề nằm ở `L` và `O`, xóa cột sẽ phá neo. Bản thân mẫu cũng ship lưới 15 cột mà chỉ dùng 2 UTCID (`methodName1`), nên để trống cột thừa mới là đúng ý mẫu.

**Không được `ClearContents` dòng 1-6** của sheet method: cột chèn thêm nằm trong vùng merge `L1:T1` / `O4:T4` / `O5:T5`, xóa ở đó sẽ mất luôn giá trị của cả vùng merge.

**Phải quét cả cột đệm `E` khi xóa nội dung ma trận** (`A8:E...`): mẫu có merge lẻ `D17:E17`, xóa nửa vùng merge thì Excel báo `We can't do that to a merged cell`.

**Công thức hàng 5 tự đúng.** `=COUNTIF(F38:HQ38,"P")`, `=COUNTA(E7:HT7)`... trỏ tới dòng `Result` và vùng cột rất rộng; chèn/xóa dòng bên trên hoặc cột bên trong đều được Excel dịch tham chiếu tự động, không cần viết lại.

### 9.3 File thật `Report_Unit_Test.xlsx` KHÔNG phải chuẩn format

Đã đo trên 30 sheet method của file thật:

- sheet rộng (`IsAllowedBaseUrl`, 21 UTCID): người làm chèn cột **sau** `T`, nên viền phải kẹt lại ở giữa lưới (cột `T` = UTCID15) và 6 cột cuối mang style lạ;
- sheet hẹp (`LanguageMismatch`, 5 UTCID; 20/30 sheet có dưới 11 UTCID): lưới mất hẳn cột viền phải, các cột sau bỏ trắng;
- vì vậy so format phải lấy **mẫu trắng** `Report5.1_Unit Test.xlsx!methodName1` làm chuẩn, không lấy file thật.

### 9.4 Nguyên tắc của bộ nghiệm thu

`compare_unit.py` ánh xạ ô kết quả về ô mẫu rồi so style/DV/chiều cao/độ rộng/merge:

- dòng giữ lại từ mẫu ánh xạ về **chính nó** (bắt buộc giống hệt);
- chỉ dòng/cột **chèn thêm** mới được ánh xạ về dòng/cột mẫu của band;
- khi kích thước trùng mẫu, ánh xạ thành 1:1 nên phép so trở thành so từng ô - đây là phép kiểm chặt nhất, dùng cho bản replica;
- merge của mẫu nằm trên dòng mà kết quả không còn ánh xạ tới (band co ngắn hơn) thì bỏ qua, vì không thể đòi merge trên một dòng không tồn tại.

`unit_test.replica.json` tồn tại chỉ để phục vụ phép kiểm này: nó dựng lại đúng kích thước và nội dung của `methodName1`, nên kết quả phải khớp mẫu từng ô. Nếu sửa `unitdoc.py` mà bản replica không còn 0 khác biệt thì đã làm hỏng format.

---

## 10. Quy ước viết test case

### 10.1 Unit Test (5.1)

- Mỗi sheet = 1 method (hoặc nhóm method cùng bản chất). `A1` Code Module = tên class, `L1` = tên method.
- UTCID đánh liên tiếp `UTCID01, UTCID02, ...` bắt đầu từ cột `F` hàng 7.
- Vùng **Condition**: mỗi dòng là 1 điều kiện đầu vào; đánh dấu `O` ở cột UTCID áp dụng (chỉ dùng ký tự `O`, không dùng `X`, `x`, `v`).
- Vùng **Confirm**: kết quả kỳ vọng (Return / Exception / Log message...).
- Vùng **Result**: `Type` nhận đúng 1 trong `N` / `A` / `B`; `Passed/Failed` nhận `P` hoặc `F`; `Executed Date` là ngày thật; `Defect ID` để trống nếu không có lỗi.
- Tỷ lệ khuyến nghị cho method có tham số biên: N >= 50%, A >= 25%, B >= 15%.
- Mỗi UTCID `P` phải truy vết được tới 1 test tự động hoặc 1 biên bản desk-check (P3).

### 10.2 Integration Test (5.2)

- ID: `TC-<MOD>-NNN`, `<MOD>` là mã module (`LOG`, `INB`, `KB`, `AGT`, `SAL`, `LEAD`, `CNT`, `DOC`, `ANL`, `ADM`).
- `Test Case Procedure`: các bước đánh số, có URL/endpoint cụ thể, dữ liệu nhập cụ thể.
- `Expected Results`: mô tả trạng thái quan sát được (UI hiển thị gì, HTTP status nào, bản ghi DB đổi ra sao); tránh câu chung chung kiểu "thành công".
- `Pre-conditions`: điều kiện dữ liệu/quyền cần có trước khi chạy.
- Mỗi round ghi đủ 3 ô: kết quả (chọn từ dropdown `Passed/Failed/Pending/N/A`), Test date, Tester.
- Dòng nhóm scenario: chỉ ghi tên nhóm ở cột `A`, merge `A:O`, không chứa dữ liệu khác.

### 10.3 System Test (5.3)

- ID: `TC-ST-<WF>-NNN` (`<WF>` là mã workflow).
- Mỗi TC là 1 luồng nghiệp vụ đầu-cuối xuyên nhiều module, không lặp lại phạm vi của Integration Test.
- Mỗi workflow tối thiểu 2 nhóm scenario (luồng thuận + luồng ngoại lệ/khôi phục).

---

## 11. Nội dung cần bổ sung

### 11.1 Workflow đề xuất cho System Test (chốt ở Đ8)

| # | Workflow | Module liên quan | Ghi chú |
|---|---|---|---|
| 1 | Onboarding tenant và cấu hình ban đầu | Admin & Security, AI Agent Management | tạo tenant, gán quyền, cấu hình LLM |
| 2 | Kết nối kênh và đồng bộ hội thoại | Omnichannel Inbox | Pancake/Meta, poll + webhook |
| 3 | Auto-reply đầu-cuối có bàn giao cho sale | Omnichannel Inbox, Sale Assist | gồm tạm dừng AI khi sale trả lời tay |
| 4 | Xây knowledge base và kiểm chất lượng trả lời | Knowledge Base | ingest, retrieval, test accuracy |
| 5 | Vòng đời lead từ hội thoại tới chốt/mất | Lead & CRM | |
| 6 | Sản xuất nội dung: brief -> nháp AI -> duyệt -> đăng | Content Management | gồm chuỗi prompt chaining |
| 7 | Lịch chạy agent và can thiệp thủ công khi tạm dừng | AI Agent Management | khớp tính năng pause/resume mới |
| 8 | Sinh tài liệu từ template và tải về | Document Generation | |
| 9 | Báo cáo, thống kê và nhật ký lỗi hệ thống | Analytics & Report, Admin & Security | |
| 10 | Khôi phục sự cố: mất kết nối kênh / job lỗi / retry | toàn hệ | luồng ngoại lệ |

### 11.2 Rà coverage Integration Test

Đối chiếu 10 sheet module hiện có với `docs/module-checklist.md`, lập bảng thiếu/đủ trong `docs/test/coverage-gap-it.md` trước khi viết thêm TC.

---

## 12. Checklist nghiệm thu cuối

Bắt buộc (format + ngôn ngữ - tiêu chí đạt/không đạt):

- [ ] `python scripts/testdoc/run_all.py` in `TAT CA DEU DAT` (exit 0).
- [ ] `check_english.py` in `TIENG ANH 100%` cho cả 3 file bàn giao, kể cả tên sheet.
- [ ] Bản replica 5.1 vẫn 0 khác biệt (chứng minh cơ chế co giãn ma trận chưa hỏng).
- [ ] Khác biệt còn lại ở `Cover`/`Statistics` của 5.1 trùng khít bản mở-rồi-lưu-không-sửa (ART-4).
- [ ] Đối chứng âm vẫn ra hàng trăm khác biệt (comparator không mù).
- [ ] SHA256 của 2 file mẫu không đổi; không ghi đè `Report5.1`/`Report5.3`.
- [ ] Không còn `#REF!` trong file bàn giao.

Nên có (nội dung - không chặn bàn giao):

- [ ] Statistics khớp số đếm độc lập bằng Python sau recalc.
- [ ] `Test coverage` tính bằng công thức, không hardcode giá trị.
- [ ] Mọi sheet dữ liệu đều xuất hiện ở sheet danh sách (`MethodList` / `Test Cases`) và sheet thống kê.
- [ ] Ngày tháng là kiểu ngày, không phải chuỗi.
- [ ] Cover có ít nhất 1 dòng Record of change.
- [ ] Placeholder `<...>` đã được thay bằng thông tin dự án thật (cần Đ8, Đ9).

---

## 13. Vướng mắc và quyết định cần chốt

Mỗi mục có phương án khuyến nghị để không chặn tiến độ; nếu không có phản hồi, thực hiện theo khuyến nghị và ghi vào mục 6.1.

| Mã | Vướng mắc | Phương án | Khuyến nghị |
|---|---|---|---|
| **Đ1** | Bản System Test đặt tên gì? | (a) tạo `Report_System_Test.xlsx`, giữ mẫu trắng; (b) điền thẳng vào file mẫu | (a) - giữ được mẫu trắng để tái dùng |
| **Đ2** | `Report5.2_Integration Test.xlsx` là bản thật nhưng mang tên mẫu. Có tách thành `Report_Integration_Test.xlsx` + mẫu trắng không? | (a) giữ nguyên tên; (b) đổi tên bản thật, sinh thêm mẫu trắng | (a) - file đã phát hành 2026-08-09, đổi tên dễ vỡ tham chiếu ngoài |
| **Đ3** | Lỗi D6 (Round 2/3 trỏ nhầm cột F) **có sẵn trong mẫu 5.3**. Sửa hay giữ để "giống mẫu 100%"? | (a) sửa ở bản thật, giữ nguyên ở mẫu; (b) giữ nguyên cả hai | (a) - nếu giữ thì mọi số liệu Round 2/3 vô nghĩa và Test Statistics của 5.2 sai |
| **Đ4** | Lỗi D5 (hằng số `-3`) - thay bằng `COUNTIF(...,"TC-*")` là sai lệch mẫu | (a) thay công thức; (b) giữ, chỉnh tay mỗi lần thêm TC | (a) - tránh sai âm thầm |
| **Đ5** | Độ phủ Unit Test: hiện 30 sheet / ~292 UTCID, trong khi `tests/` có 228 `[Fact]/[Theory]`; nhiều sheet chưa có test tự động tương ứng | (a) ghi rõ căn cứ từng UTCID (test tự động / desk-check); (b) chạy `dotnet test` lấy kết quả thật rồi cập nhật Passed/Failed/Executed Date; (c) viết bổ sung test cho method còn thiếu | (a) + (b). Phương án (c) là việc phát triển, tách thành đầu việc riêng |
| **Đ6** | Credential `admin@clawbot.local / Admin@12345` xuất hiện trong `MethodList!C6` và nhiều Test Case Procedure của 5.2 | (a) giữ nguyên; (b) thay password bằng `<dev password>`; (c) thay cả cặp bằng `<dev account>` | (b) - vẫn đọc hiểu được kịch bản mà không in mật khẩu vào tài liệu bàn giao |
| **Đ7** | `Test Statistics` của 5.2 lấy số liệu từ **Round 3** (hàng 8), còn mẫu 5.3 lấy **Round 1** (hàng 6). Bản System Test theo cái nào? | (a) theo mẫu (Round 1); (b) theo 5.2 (Round 3 - vòng cuối) | (b) nếu chạy đủ 3 vòng kèm ghi chú; (a) nếu chỉ chạy 1 vòng |
| **Đ8** | Danh sách 10 workflow ở mục 11.1 có đúng phạm vi mong muốn không? Thiếu workflow bắt buộc nào? | - | chốt trước khi bắt đầu P5 |
| **Đ9** | Project Name, Project Code, Creator, Reviewer/Approver, Version cho bản System Test lấy giá trị gì? | - | cần thông tin từ phía anh |

---

## 14. Phụ lục - lệnh khảo sát nhanh

```python
# Đọc cấu trúc 1 sheet (chỉ đọc, không bao giờ save)
import openpyxl
wb  = openpyxl.load_workbook(path)                  # formula string
wbv = openpyxl.load_workbook(path, data_only=True)  # value cache sau recalc
ws  = wb['Statistics']
[(ws.cell(r, c).coordinate, ws.cell(r, c).value)
 for r in range(1, 60) for c in range(1, 10) if ws.cell(r, c).value is not None]
[str(m) for m in ws.merged_cells.ranges]
[(d.type, d.formula1, str(d.sqref)) for d in ws.data_validations.dataValidation]
ws.freeze_panes, ws.print_area
```

```powershell
# Dọn tiến trình Excel treo trước khi build lại
Get-Process EXCEL -ErrorAction SilentlyContinue | Stop-Process -Force
```

---

## 15. Phân công khi nhiều agent chạy song song

| Luồng | File được chạm | Không được chạm |
|---|---|---|
| Agent A - Tooling | `scripts/testdoc/*.py`, `fingerprints/*` | `content/*`, file `.xlsx` |
| Agent B - Nội dung UT | `content/unit_test.json`, `docs/test/traceability-unit-test.md` | script, file `.xlsx` |
| Agent C - Nội dung IT | `content/integration_test.json`, `docs/test/coverage-gap-it.md` | script, file `.xlsx` |
| Agent D - Nội dung ST | `content/system_test.json` | script, file `.xlsx` |
| Agent E - Build & verify | chạy script, sinh `.xlsx`, `docs/test/verify-report.md` | không sửa nội dung JSON |

Quy tắc chung: chỉ **một** luồng được ghi vào file `.xlsx` (Agent E); mọi luồng khác làm việc trên JSON/Markdown. Cách này loại bỏ xung đột nhị phân trong git và giữ được tính tái lập của output.
