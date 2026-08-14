import type { DocumentTemplate, TemplateField, TemplateFieldType } from "@/shared/api/documents";

/** Placeholder trong nội dung mẫu: {{ ten_khach }} */
const PLACEHOLDER_PATTERN = /\{\{\s*([\w.-]+)\s*\}\}/g;

/**
 * Khóa do hệ thống tự điền (hồ sơ khách hàng, kiến thức nội bộ) khi tạo tài liệu.
 * Người dùng không cần nhập tay nên không đưa vào biểu mẫu bắt buộc.
 */
export const AUTO_FILLED_KEYS: ReadonlySet<string> = new Set([
  "contact_name",
  "customer_name",
  "contact_phone",
  "contact_email",
  "knowledge",
  "kb_content",
  "kb_module_codes",
]);

export const FIELD_TYPE_OPTIONS: readonly { readonly value: TemplateFieldType; readonly label: string }[] = [
  { value: "text", label: "Chữ ngắn" },
  { value: "multiline", label: "Đoạn văn" },
  { value: "number", label: "Số" },
  { value: "currency", label: "Số tiền" },
  { value: "date", label: "Ngày" },
];

export interface TemplatePreset {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly code: string;
  readonly docType: string;
  readonly body: string;
  readonly fields: readonly TemplateField[];
}

function field(
  key: string,
  label: string,
  type: TemplateFieldType,
  required: boolean,
  sample: string | null,
  placeholder: string | null = null,
): TemplateField {
  return { key, label, type, required, placeholder, sample };
}

/**
 * Mẫu dựng sẵn: người dùng chọn mẫu rồi điền form, không phải tự viết nội dung.
 * Tên khách dùng đúng khóa `customer_name` trong AUTO_FILLED_KEYS để hệ thống tự điền từ hồ sơ
 * khách hàng — đặt khóa riêng (ten_khach...) sẽ khiến mẫu này không tự điền như mẫu seed.
 */
export const TEMPLATE_PRESETS: readonly TemplatePreset[] = [
  {
    id: "quote",
    name: "Báo giá khóa học",
    description: "Báo giá một khóa học kèm học phí và ưu đãi.",
    code: "BAO-GIA-KHOA-HOC",
    docType: "quote",
    body: [
      "BÁO GIÁ KHÓA HỌC",
      "",
      "Kính gửi {{ customer_name }},",
      "",
      "Cảm ơn anh/chị đã quan tâm tới chương trình của chúng tôi. Dưới đây là thông tin học phí:",
      "",
      "Khóa học: {{ khoa_hoc }}",
      "Thời lượng: {{ thoi_luong }}",
      "Học phí: {{ hoc_phi }}",
      "Ưu đãi: {{ uu_dai }}",
      "Báo giá có hiệu lực đến: {{ han_bao_gia }}",
      "",
      "Anh/chị phản hồi lại giúp em để em giữ suất học nhé.",
    ].join("\n"),
    fields: [
      field("customer_name", "Tên khách hàng", "text", false, "Nguyễn Minh Anh"),
      field("khoa_hoc", "Khóa học", "text", true, "HSK 4 cấp tốc"),
      field("thoi_luong", "Thời lượng", "text", false, "3 tháng, 36 buổi"),
      field("hoc_phi", "Học phí", "currency", true, "4.500.000đ"),
      field("uu_dai", "Ưu đãi", "text", false, "Giảm 10% khi đóng đủ"),
      field("han_bao_gia", "Hạn báo giá", "date", false, null),
    ],
  },
  {
    id: "brochure",
    name: "Tờ giới thiệu",
    description: "Giới thiệu ngắn về chương trình học và điểm mạnh.",
    code: "TO-GIOI-THIEU",
    docType: "brochure",
    body: [
      "GIỚI THIỆU CHƯƠNG TRÌNH {{ ten_chuong_trinh }}",
      "",
      "Đối tượng phù hợp: {{ doi_tuong }}",
      "Mục tiêu đầu ra: {{ muc_tieu }}",
      "",
      "Điểm mạnh của chương trình:",
      "{{ diem_manh }}",
      "",
      "Liên hệ tư vấn: {{ lien_he }}",
    ].join("\n"),
    fields: [
      field("ten_chuong_trinh", "Tên chương trình", "text", true, "Tiếng Trung giao tiếp"),
      field("doi_tuong", "Đối tượng phù hợp", "text", true, "Người mới bắt đầu"),
      field("muc_tieu", "Mục tiêu đầu ra", "text", true, "Giao tiếp cơ bản sau 3 tháng"),
      field("diem_manh", "Điểm mạnh", "multiline", false, "Lớp nhỏ 8 học viên\nGiáo viên bản ngữ\nHọc bù miễn phí"),
      field("lien_he", "Thông tin liên hệ", "text", false, "0900 000 000"),
    ],
  },
  {
    id: "onboarding",
    name: "Hồ sơ nhập học",
    description: "Xác nhận nhập học và các mốc cần chuẩn bị.",
    code: "HO-SO-NHAP-HOC",
    docType: "onboarding",
    body: [
      "XÁC NHẬN NHẬP HỌC",
      "",
      "Học viên: {{ customer_name }}",
      "Lớp: {{ ten_lop }}",
      "Ngày khai giảng: {{ ngay_khai_giang }}",
      "Lịch học: {{ lich_hoc }}",
      "Học phí đã đóng: {{ hoc_phi_da_dong }}",
      "Còn lại: {{ hoc_phi_con_lai }}",
      "",
      "Ghi chú: {{ ghi_chu }}",
    ].join("\n"),
    fields: [
      field("customer_name", "Tên học viên", "text", false, "Nguyễn Minh Anh"),
      field("ten_lop", "Lớp", "text", true, "HSK4-T7"),
      field("ngay_khai_giang", "Ngày khai giảng", "date", true, null),
      field("lich_hoc", "Lịch học", "text", false, "Thứ 3 - 5 - 7, 19h00"),
      field("hoc_phi_da_dong", "Học phí đã đóng", "currency", false, "2.000.000đ"),
      field("hoc_phi_con_lai", "Học phí còn lại", "currency", false, "2.500.000đ"),
      field("ghi_chu", "Ghi chú", "multiline", false, null),
    ],
  },
  {
    id: "blank",
    name: "Mẫu trống",
    description: "Tự soạn nội dung và tự khai báo trường cần điền.",
    code: "",
    docType: "quote",
    body: ["TIÊU ĐỀ TÀI LIỆU", "", "Kính gửi {{ customer_name }},", "", "Nội dung tài liệu."].join("\n"),
    fields: [field("customer_name", "Tên khách hàng", "text", false, "Nguyễn Minh Anh")],
  },
];

/** Lấy danh sách khóa placeholder trong nội dung, giữ thứ tự xuất hiện và bỏ trùng. */
export function extractPlaceholderKeys(body: string): readonly string[] {
  const keys: string[] = [];
  for (const match of body.matchAll(PLACEHOLDER_PATTERN)) {
    const key = match[1];
    if (key && !keys.includes(key)) keys.push(key);
  }
  return keys;
}

/** Đổi khóa kỹ thuật thành nhãn dễ đọc: ten_khach -> "Ten khach". */
export function humanizeKey(key: string): string {
  const words = key.replace(/[_.-]+/g, " ").trim();
  if (!words) return key;
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function guessType(key: string): TemplateFieldType {
  const lower = key.toLowerCase();
  if (lower.includes("ngay") || lower.includes("han") || lower.includes("date")) return "date";
  if (lower.includes("phi") || lower.includes("gia") || lower.includes("tien") || lower.includes("price")) return "currency";
  if (lower.includes("so_luong") || lower.includes("quantity")) return "number";
  if (lower.includes("ghi_chu") || lower.includes("note") || lower.includes("noi_dung")) return "multiline";
  return "text";
}

export function fieldFromKey(key: string): TemplateField {
  return {
    key,
    label: humanizeKey(key),
    type: guessType(key),
    required: !AUTO_FILLED_KEYS.has(key),
    placeholder: null,
    sample: null,
  };
}

/**
 * Đồng bộ danh sách trường với placeholder trong nội dung:
 * giữ nguyên cấu hình trường đã khai báo, thêm trường mới, bỏ trường không còn dùng.
 */
export function syncFieldsWithBody(body: string, fields: readonly TemplateField[]): readonly TemplateField[] {
  const keys = extractPlaceholderKeys(body);
  const byKey = new Map(fields.map((item) => [item.key, item] as const));
  return keys.map((key) => byKey.get(key) ?? fieldFromKey(key));
}

/** Trường để dựng biểu mẫu tạo tài liệu: ưu tiên schema đã khai báo, nếu trống thì suy ra từ nội dung. */
export function formFieldsFor(template: DocumentTemplate | null): readonly TemplateField[] {
  if (!template) return [];
  const declared = template.fields ?? [];
  if (declared.length) return declared;
  return extractPlaceholderKeys(template.templateHtml)
    .filter((key) => !AUTO_FILLED_KEYS.has(key))
    .map(fieldFromKey);
}

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const VN_DATE = /^(\d{1,2})[/-](\d{1,2})[/-](\d{4})$/;

/**
 * Chuẩn hóa về yyyy-MM-dd cho <input type="date">.
 * Ô ngày của trình duyệt vứt bỏ mọi giá trị sai định dạng mà không báo gì, nên giá trị mẫu kiểu
 * "20/09/2026" hoặc dữ liệu mẫu cũ sẽ làm ô trống trơn — nhìn như nhập xong không lưu được.
 */
export function toDateInputValue(value: string): string {
  const raw = value.trim();
  if (!raw) return "";
  if (ISO_DATE.test(raw)) return raw;
  const vn = VN_DATE.exec(raw);
  if (!vn) return "";
  const [, day, month, year] = vn;
  return `${year}-${month!.padStart(2, "0")}-${day!.padStart(2, "0")}`;
}

/** Ngày trong tài liệu gửi khách viết theo dd/MM/yyyy, không để lộ dạng ISO của ô nhập. */
export function formatDateForDocument(value: string): string {
  const iso = ISO_DATE.exec(value.trim());
  if (!iso) return value;
  const [, year, month, day] = iso;
  return `${day}/${month}/${year}`;
}

/** Đổi giá trị của các trường kiểu ngày sang dạng người Việt đọc, dùng cho cả xem trước lẫn lúc gửi API. */
export function formatVarsForDocument(
  fields: readonly TemplateField[],
  vars: Readonly<Record<string, string>>,
): Record<string, string> {
  const dateKeys = new Set(fields.filter((item) => item.type === "date").map((item) => item.key));
  return Object.fromEntries(
    Object.entries(vars).map(([key, value]) => [key, dateKeys.has(key) ? formatDateForDocument(value) : value]),
  );
}

export function applyVars(body: string, vars: Readonly<Record<string, string>>): string {
  return body.replace(PLACEHOLDER_PATTERN, (match, key: string) => {
    const value = vars[key];
    return value !== undefined && value.trim().length > 0 ? value : match;
  });
}

export function missingRequired(
  fields: readonly TemplateField[],
  vars: Readonly<Record<string, string>>,
): readonly TemplateField[] {
  return fields.filter((item) => item.required && !(vars[item.key] ?? "").trim());
}

/** Giá trị mẫu để người dùng bấm "Điền dữ liệu mẫu" và thấy ngay kết quả. */
export function sampleVars(fields: readonly TemplateField[]): Record<string, string> {
  const result: Record<string, string> = {};
  for (const item of fields) {
    if (item.sample && item.sample.trim()) result[item.key] = item.sample;
  }
  return result;
}

/** Chỉ gửi lên API các giá trị người dùng thực sự điền. */
export function cleanVars(vars: Readonly<Record<string, string>>): Record<string, string> | null {
  const entries = Object.entries(vars).filter(([, value]) => value.trim().length > 0);
  if (!entries.length) return null;
  return Object.fromEntries(entries.map(([key, value]) => [key, value.trim()]));
}

/** Tách một dòng thành các đoạn chữ và placeholder chưa điền để tô màu cảnh báo khi xem trước. */
export function splitPreviewTokens(line: string): readonly { readonly text: string; readonly missing: boolean }[] {
  const tokens: { text: string; missing: boolean }[] = [];
  let lastIndex = 0;
  for (const match of line.matchAll(PLACEHOLDER_PATTERN)) {
    const index = match.index ?? 0;
    if (index > lastIndex) tokens.push({ text: line.slice(lastIndex, index), missing: false });
    tokens.push({ text: `[chưa nhập: ${humanizeKey(match[1] ?? "")}]`, missing: true });
    lastIndex = index + match[0].length;
  }
  if (lastIndex < line.length) tokens.push({ text: line.slice(lastIndex), missing: false });
  return tokens.length ? tokens : [{ text: line, missing: false }];
}
