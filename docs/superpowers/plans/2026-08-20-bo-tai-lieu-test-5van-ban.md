# Kế hoạch: Bộ tài liệu test 5 văn bản + nghiệm thu ≥80% coverage cho toàn hệ thống ClawBot

Ngày lập: 2026-08-20. Nguồn yêu cầu: user cập nhật lại 5 file mẫu format trong `docs/test/`
(`Report-5.1 … Report-5.5`) và yêu cầu dựng test "đúng đủ theo tài liệu cho toàn bộ hệ thống",
nghiệm thu tối thiểu 80% coverage dự án; vì gấp, được phép suy test từ source + chạy chọn lọc.

Quyết định đã chốt với user (2026-08-20):
1. **Coverage = CẢ HAI**: code coverage ≥80% cho tầng unit+integration (5.1/5.2), và requirement/feature
   coverage (mọi feature có test case tài liệu hoá) cho 5.3/5.4/5.5.
2. **Mức chứng cứ = suy từ source + chạy chọn lọc**: 5.1/5.2 chạy thật lấy coverage; 5.3/5.4/5.5 soạn
   test case từ đọc code + chạy tay/E2E các ca then chốt, ca còn lại đánh dấu `Not Run`/deferred **trung thực**.
3. **Nội dung = nghiệp vụ ClawBot thật** (không giữ demo e-commerce của mẫu 5.4/5.5). Giữ đúng FORMAT mẫu.

Đây là KẾ HOẠCH, chưa sửa code/script. Đọc xong mới bắt tay.

---

## 0. Hiện trạng đã xác minh (không phải suy đoán)

### 0.1 File mẫu mới thay thế bộ cũ

| File mới (untracked) | Sheet | Ghi chú so với bộ cũ |
|---|---|---|
| `Report-5.1_Unit Test.xls` | `Cover`, `Test Cases`, `Test Statistics`, `ModuleName1` | **Định dạng `.xls` cũ (BIFF)**; sheet dữ liệu đổi từ ma trận ngang `methodName1` sang **dọc** `ModuleName1` (UTC001… theo dòng, có cột `Type` Normal/Abnormal/Boundary) |
| `Report-5.2_Integration Test.xlsx` | `Cover`, `Test Cases`, `Test Statistics`, `Feature Name1/2`, `SecurityTest` | Layout dòng dịch xuống: header row 10, nhãn nhóm row 11, case đầu **row 12**; cột chạy tới **P**; thêm sheet `SecurityTest` |
| `Report-5.3_System Test_FRs.xlsx` | `Cover`, `Test Cases`, `Test Statistics`, `Workflow Name1/2` | System Test **tách FR riêng**; cùng layout row 12 / cột P |
| `Report-5.4_System Test_NFRs.xlsx` | `Security`, `Performance` | **File mới**; đang chứa demo e-commerce tiếng Việt |
| `Report-5.5_Acceptance Test Scripts.xlsx` | `UAT`, `Exploratory` | **File mới**; đang chứa demo e-commerce tiếng Việt |

Bộ cũ (`Report5.1/5.2/5.3` + `Report_*_Test.xlsx` + `generated/`) đã bị `git rm` (nằm ở `D` trong git status)
→ tooling `scripts/testdoc/` trỏ vào tên cũ sẽ hỏng.

### 0.2 Tooling `scripts/testdoc/` — điều gì còn dùng được, điều gì phải sửa

- `xl.py` (Excel COM helper) — **dùng lại nguyên si**.
- `rowdoc.py` (dựng 5.2/5.3) — profile hard-code `template=Report5.2/5.3...` tên cũ, `data_start=11`,
  `last_col="O"`. Layout mới là `data_start=12`, `last_col="P"`, template đổi tên → **phải sửa profile**.
  Thêm profile `system_fr` trỏ `Report-5.3_System Test_FRs.xlsx`.
- `unitdoc.py` + `compare_unit.py` (5.1 ma trận ngang) — **layout 5.1 đã đổi hoàn toàn** sang dọc, và file
  là `.xls`. Hai script này viết lại theo layout dọc mới (hoặc thay bằng builder mới `unitdoc_v2`).
  COM ghi `.xls` được (SaveAs xlExcel8) nhưng openpyxl không đọc `.xls` → comparator 5.1 phải đọc bằng
  `xlrd` (đã có, 2.0.2) thay openpyxl.
- `compare_format.py` — generic theo dòng; chỉ cần truyền `--data-row 12` và cột tới P; sửa `run_all.py` args.
- `trx_to_unit.py` — chuyển `.trx` → JSON cho 5.1; **dùng lại được**, chỉ đổi schema case cho layout mới.
- `check_english.py` / `check_evidence.py` / `check_coverage_claim.py` — cổng nghiệm thu, **dùng lại**;
  cập nhật đường dẫn file + (với 5.1) đọc `.xls`.
- `run_all.py` — điều phối 11 bước, trỏ toàn tên cũ + `--pair` theo sheet cũ → **viết lại danh sách bước**
  cho 5 file mới (thêm bước cho 5.4/5.5).

Bẫy Windows đã biết (memory `testdoc-format-fidelity-tooling`): luôn `PYTHONIOENCODING=utf-8`; ghi bằng
Excel COM chứ không openpyxl (openpyxl phá VML/printerSettings, `insert_rows` không dịch công thức/merge/DV);
LibreOffice không có nên recalc bằng `Application.CalculateFullRebuild()`; sheet mới clone từ sheet có sẵn
trong chính file đó.

### 0.3 Tài sản test thật đang có

- 6 project test trong `Clawbot.sln`: `Agents` (22 file), `Api` (103), `Application` (7), `Domain` (66),
  `Infrastructure` (24), `SharedKernel` (27). Central package management đã có xunit/FluentAssertions/
  NSubstitute/coverlet (memory `dotnet-test-suite-removed`).
- Evidence R0 (`docs/test/evidence/2026-08-19_R0/manifest.json`): **1487 test / 1484 passed (99.8%)**,
  0 failed. Phân bố: Domain 763, Agents 297, SharedKernel 255, Infrastructure 145, Application 27.
  (Api.Tests chạy riêng, chưa nằm trong manifest R0 vì dùng WebApplicationFactory.)
- `Clawbot.Api` coverage ~66%→~90% qua các batch gần đây (memory `api-tests-coverage-batch-aug19`).
- Cổng build (memory `clawbot-build-gates`): `TreatWarningsAsErrors` + CA analyzers là **lỗi build**;
  `tests/Directory.Build.props` phải `NoWarn CA1707;CA2007;CA1062;CA1515;CA1861`. EF InMemory không hỗ trợ
  `EF.Functions.Like`/`FromSqlInterpolated`/`ExecuteUpdateAsync`. CI net10 roll-forward: EF8 `Guid[].Contains`
  ném `TypeLoadException` (memory `ci-net10-runtime-ef8-span-contains`).

### 0.4 Phạm vi "toàn bộ hệ thống" — các module nghiệp vụ ClawBot (thay demo e-commerce)

Dựa theo src + memory, các domain module để phủ trong 5.2/5.3/5.4/5.5:
Auth & RBAC · Omnichannel Inbox (Pancake/Meta) · Knowledge Base (KB/RAG) · Lead Management & lifecycle ·
Content Pipeline (chain Plan→Outline→Write→Package + review + publish + calendar/performance) ·
Agent Orchestration (autonomous run, intervention, schedule) · LLM config & gateway cost guard ·
Background jobs & notifications · Admin/Meta business · System logs.

---

## 1. Mục tiêu nghiệm thu (Definition of Done)

Bộ tài liệu + test đạt khi **tất cả** các cổng sau xanh:

**G-FORMAT** — 5 file build ra khớp format mẫu 100% (`compare_format`/`compare_unit` = 0 khác biệt trên
vùng dữ liệu; đối chứng âm vẫn ra hàng trăm khác biệt để chứng minh cổng không mù).

**G-EN** — 100% tiếng Anh trong report bàn giao (kể cả tên sheet); comment code vẫn tiếng Việt
(`check_english.py`). *Lưu ý:* mẫu 5.4/5.5 đang tiếng Việt → nội dung ClawBot thật phải viết tiếng Anh.

**G-COV** — code coverage hợp nhất (coverlet, toàn solution test) **≥ 80% line** (và báo cáo branch).
Đo bằng `dotnet test --collect:"XPlat Code Coverage"` gộp 6 project, ráp bằng ReportGenerator.

**G-TRACE** — mọi TC ghi `Passed/Failed` trong 5.1/5.2 truy vết được về `.trx`/log thật (`check_evidence.py`,
`check_coverage_claim.py` không còn "sheet ma"). TC chưa chạy trong 5.3/5.4/5.5 để `Not Run`/`Pending`
trung thực, kèm ghi chú deferred.

**G-REQ** — mọi feature/module ở §0.4 có ít nhất một TC tài liệu hoá trong 5.3 (FR workflow) và được điểm
danh trong 5.4 (NFR) / 5.5 (UAT) theo mức độ áp dụng.

**G-BUILD** — `dotnet build Clawbot.sln -c Release` = 0 error (CI gate hiện tại).

---

## 2. Các pha thực hiện

### Pha A — Cập nhật tooling khớp 5 file mẫu mới (nền tảng, làm trước)

A1. **Rà layout mẫu bằng script đọc** (đã làm 1 phần): xác nhận từng ô neo/merge của 5.2/5.3 (row 12,
    col P), 5.1 (`.xls`, dọc), 5.4/5.5 (bảng phẳng 1 header row).
A2. **Sửa `rowdoc.py`**: đổi `PROFILES` sang tên file mới; `data_start=12`, `last_col="P"`, `stat_round_row`
    theo mẫu mới; thêm profile `system_fr`. Kiểm nhãn nhóm ở row 11, `Number of TCs=COUNTA(A12:A1000)`.
A3. **Viết builder 5.1 dọc mới** (`unitdoc.py` viết lại): mỗi module = 1 sheet clone từ `ModuleName1`;
    ghi `UTC00x` theo dòng, cột `Type`, block `Testing Round`/`Testing Type`/`Count`; ghi ra `.xls`
    (COM `SaveAs FileFormat=56` xlExcel8). Comparator 5.1 đọc bằng `xlrd`.
A4. **Viết builder mới cho 5.4 + 5.5** (`nfrdoc.py`, `uatdoc.py`): bảng phẳng, clone sheet mẫu, thay demo
    e-commerce bằng nội dung ClawBot; giữ đúng cột (`Security`: A–J; `Performance`: A–H; `UAT`: A–I;
    `Exploratory`: A–F).
A5. **Cập nhật comparator + `run_all.py`**: danh sách 11→~15 bước cho 5 file; `--pair` theo sheet ClawBot
    thật; thêm đối chứng âm cho mỗi builder mới. Chạy tới khi in `TAT CA DEU DAT`.

**Rủi ro A**: `.xls` + COM SaveAs có thể mất định dạng nếu template gốc là `.xls` mà ta mở/lưu qua COM —
phải kiểm bằng bản "mở-rồi-lưu không sửa" so với chính nó (khác biệt = 0) trước khi dựng nội dung.

### Pha B — Nâng code coverage lên ≥80% (5.1 Unit + 5.2 Integration)

B0. **Đo baseline hợp nhất**: chạy cả 6 project với coverlet, ráp ReportGenerator → biết % hiện tại từng
    assembly. (Api ~90%, các assembly khác chưa rõ % hợp nhất.)
    ```bash
    for p in Agents Api Application Domain Infrastructure SharedKernel; do \
      dotnet test tests/Clawbot.$p.Tests --collect:"XPlat Code Coverage" \
      --results-directory D:/tmp/cov-all/$p; done
    reportgenerator -reports:D:/tmp/cov-all/**/coverage.cobertura.xml -targetdir:D:/tmp/cov-all/report
    ```
B1. **Xác định assembly < 80%** từ report, xếp theo % thấp → cao (chiến lược "file 0% trước" đã hiệu quả,
    memory `api-tests-coverage-batch-aug19`).
B2. **Viết test bù** theo tầng:
    - Unit (5.1): domain service/util thuần (validators, guards, calculators, parsers…) — pattern
      SqliteConnection in-memory + `AppDbContext(options, NullTenantAccessor)`; domain factory `internal static`
      seed qua scope riêng. Mỗi class test có case Normal/Abnormal/Boundary (khớp cột `Type` của 5.1).
    - Integration (5.2): endpoint qua `ApiTestFactory` (`WebApplicationFactory<Program>` + EF InMemory +
      Hangfire passive). Nhóm theo module §0.4; thêm `SecurityTest` (RBAC/authz/IDOR) cho sheet SecurityTest.
B3. **Tránh bẫy đã biết**: EF InMemory bỏ `Like`/`FromSqlInterpolated`/`ExecuteUpdateAsync` (ghi chú, không cố
    test); minimal API scalar param non-nullable = required (thiếu → 400, không phải bug); lambda statement-body
    không vào expression tree; helper không đụng instance → `static` (CA1822); fully-qualify class trùng tên
    (vd `DocsAgent`); net10 EF8 `Guid[].Contains` → dùng IN subquery.
B4. **Chạy tuần tự 1 tiến trình** khi có nhiều batch (tránh N `dotnet test` tranh chấp DB test).
B5. Lặp B0→B4 tới khi report hợp nhất ≥ 80% line.

### Pha C — Sinh evidence thật + đổ vào 5.1/5.2

C1. Chạy full test với logger trx vào `docs/test/evidence/2026-08-20_R1/`:
    ```bash
    dotnet test tests/Clawbot.<P>.Tests --logger "trx;LogFileName=Clawbot.<P>.Tests.trx" \
      --results-directory docs/test/evidence/2026-08-20_R1/unit
    ```
C2. `trx_to_unit.py` → JSON cho 5.1 (map test → UTCID + Normal/Abnormal/Boundary + Passed/Failed/date).
C3. Với 5.2: soạn `integration_test.real.json` từ các integration test đã pass (Round 1 = ngày chạy thật,
    Round 2/3 để Pending nếu chưa hồi quy — trung thực).
C4. Dựng 5.1 (`unitdoc`) + 5.2 (`rowdoc --profile integration`) từ JSON thật; chạy `check_coverage_claim.py`
    (không cờ) + `check_evidence.py` (không `--allow-demo`) → phải xanh (không sheet ma, không dấu hiệu bịa).

### Pha D — Soạn 5.3 (System FR) / 5.4 (NFR) / 5.5 (UAT) theo nghiệp vụ ClawBot

D1. **5.3 FR workflows**: mỗi sheet = 1 luồng đầu-cuối (WF Onboarding, Auto-reply đa kênh, Lead pipeline,
    Content pipeline, Agent orchestration…). Mỗi TC: mô tả/thủ tục/kỳ vọng/tiền đề. Ca chạy tay/E2E được →
    `Passed` kèm ngày; ca chưa chạy → `Not Run`/`Pending` + ghi chú.
D2. **5.4 NFR**: `Security` (authz/IDOR/HTTPS/CSP/rate-limit — soi từ code RBAC + endpoint), `Performance`
    (mục tiêu + tool `Manual`/`k6 nếu có`); ca chưa đo → `Not Run` + note "deferred v2" khi hợp lý.
D3. **5.5 UAT/Exploratory**: kịch bản ngôn ngữ nghiệp vụ theo vai (Admin/Sale/Manager) trên ClawBot; kết quả
    `Not Executed` cho tới khi có buổi UAT thật — không tô xanh khống.
D4. Dựng 3 file bằng builder Pha A; chạy `check_english.py` (100% English) + `compare_*` (format khớp).

### Pha E — Nghiệm thu tổng + báo cáo

E1. `python scripts/testdoc/run_all.py` → `TAT CA DEU DAT` (G-FORMAT + G-EN + đối chứng âm).
E2. Report hợp nhất coverage ≥80% (G-COV) — kèm ảnh/summary vào `docs/test/evidence/2026-08-20_R1/`.
E3. `dotnet build Clawbot.sln -c Release` = 0 (G-BUILD).
E4. Bảng đối chiếu module §0.4 ↔ sheet 5.3/5.4/5.5 (G-REQ) + danh sách ca `Not Run`/deferred trung thực.

---

## 3. Thứ tự & phụ thuộc

A (tooling) → B (coverage) chạy song song được với D-nháp nội dung; C phụ thuộc B (cần test pass để có trx);
D phụ thuộc A; E cuối cùng. Đề xuất: **A trước** (mở khoá mọi thứ), rồi **B** (khối lượng lớn nhất, quyết định
G-COV), C bám theo B, D làm song song khi A xong, E chốt.

## 4. Rủi ro / cần khách xác nhận

- **`.xls` cho 5.1**: nếu COM SaveAs `.xls` làm lệch format so với mẫu, sẽ đề nghị giữ nội dung nhưng xuất
  `.xlsx` (hoặc xin mẫu `.xlsx` của 5.1). Xác nhận sau khi thử "mở-rồi-lưu".
- **≥80% hợp nhất** có thể tốn nhiều test cho Agents/Infrastructure (job handler/hub 0% theo memory). Nếu một
  vài assembly khó chạm 80% do phụ thuộc hạ tầng ngoài (gRPC/Hangfire/SignalR), sẽ báo rõ assembly nào + lý do
  và bù bằng integration thay unit — không hạ ngưỡng âm thầm.
- **5.4/5.5 chủ yếu Not Run** trong lần gấp này (đúng quyết định #2): G-REQ đạt bằng *có test case tài liệu hoá*,
  không phải *đã chạy hết*. Cần khách đồng ý coverage NFR/UAT là requirement-coverage, không phải execution-coverage.
- Không tự ý sửa file mẫu (`Report-5.*`) — chỉ dùng làm chuẩn format; nội dung dựng ra file riêng trong `generated/`.
