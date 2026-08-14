import { expect, test } from "@playwright/test";
import type { TemplateField } from "../src/shared/api/documents";
import {
  formatDateForDocument,
  formatVarsForDocument,
  toDateInputValue,
} from "../src/features/documents/templateModel";

/** Trường "Hạn báo giá" là kiểu ngày — ô <input type="date"> chỉ nhận yyyy-MM-dd. */

const fields: readonly TemplateField[] = [
  { key: "han_bao_gia", label: "Hạn báo giá", type: "date", required: false, placeholder: null, sample: null },
  { key: "hoc_phi", label: "Học phí", type: "currency", required: true, placeholder: null, sample: null },
];

test.describe("toDateInputValue", () => {
  test("giữ nguyên giá trị ISO", () => {
    expect(toDateInputValue("2026-09-20")).toBe("2026-09-20");
  });

  test("chuyển dd/MM/yyyy sang ISO để ô ngày không bị trống", () => {
    expect(toDateInputValue("20/09/2026")).toBe("2026-09-20");
    expect(toDateInputValue("5/9/2026")).toBe("2026-09-05");
  });

  test("giá trị không phải ngày trả về rỗng thay vì để trình duyệt tự nuốt", () => {
    expect(toDateInputValue("dd/MM/yyyy")).toBe("");
    expect(toDateInputValue("")).toBe("");
  });
});

test.describe("formatDateForDocument", () => {
  test("tài liệu gửi khách hiện dd/MM/yyyy", () => {
    expect(formatDateForDocument("2026-09-20")).toBe("20/09/2026");
  });

  test("giá trị không phải ISO giữ nguyên", () => {
    expect(formatDateForDocument("còn hiệu lực 30 ngày")).toBe("còn hiệu lực 30 ngày");
  });
});

test.describe("formatVarsForDocument", () => {
  test("chỉ đổi trường kiểu ngày", () => {
    const result = formatVarsForDocument(fields, {
      han_bao_gia: "2026-09-20",
      hoc_phi: "4.500.000đ",
    });

    expect(result).toEqual({ han_bao_gia: "20/09/2026", hoc_phi: "4.500.000đ" });
  });

  test("bỏ trống hạn báo giá thì không sinh giá trị lạ", () => {
    expect(formatVarsForDocument(fields, { han_bao_gia: "" })).toEqual({ han_bao_gia: "" });
  });
});
