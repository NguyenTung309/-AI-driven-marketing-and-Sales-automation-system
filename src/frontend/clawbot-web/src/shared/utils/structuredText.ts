// Payload kỹ thuật trong hệ thống thường là JSON lồng JSON: giá trị của một key lại là chuỗi JSON, và
// tiếng Việt bị System.Text.Json escape thành \uXXXX. Các hàm dưới gỡ hai lớp đó để UI dựng bảng đọc được
// thay vì đổ nguyên khối JSON ra màn hình.

const MAX_UNWRAP_DEPTH = 3;
const ESCAPE_PATTERN = /\\(u[0-9a-fA-F]{4}|n|r|t|"|\\|\/)/g;
const TASK_REF_PATTERN = /^task_(\d+)_output$/;
const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/;
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Nhãn tiếng Việt cho các key hay gặp trong đầu vào/kết quả của agent. */
const KEY_LABELS: Readonly<Record<string, string>> = {
  tenant_id: "Tổ chức",
  content_id: "Mã nội dung",
  content_item_id: "Mã nội dung",
  content_revision: "Phiên bản nội dung",
  schedule_id: "Mã lịch đăng",
  scheduled_at: "Thời điểm đăng",
  post_url: "Link bài đăng",
  lead_id: "Mã khách tiềm năng",
  lead_ids: "Danh sách khách tiềm năng",
  contact_id: "Mã liên hệ",
  contact_keys: "Danh sách liên hệ",
  seed_contact_keys: "Liên hệ nguồn",
  conversation_id: "Mã hội thoại",
  campaign_id: "Mã chiến dịch",
  audience_name: "Tên tập đối tượng",
  platform: "Kênh",
  brief: "Yêu cầu nội dung",
  tone: "Giọng văn",
  language: "Ngôn ngữ",
  operation: "Thao tác",
  action: "Hành động",
  metric: "Chỉ số",
  date: "Ngày",
  new_budget: "Ngân sách mới",
  daily_budget: "Ngân sách ngày",
  workflow_state: "Trạng thái luồng",
  decision: "Quyết định",
  reason: "Lý do",
  verdict: "Kết luận",
  goal: "Mục tiêu",
  geo: "Khu vực",
  keywords: "Từ khóa",
  limit: "Số lượng",
  top_n: "Số lượng",
  stage: "Giai đoạn",
  user_text: "Tin nhắn khách",
  history: "Lịch sử hội thoại",
  turns_json: "Diễn biến hội thoại",
  template_code: "Mã mẫu",
  template_body: "Nội dung mẫu",
  vars_json: "Biến của mẫu",
  doc_type: "Loại tài liệu",
  upstream_results: "Kết quả bước trước",
  research_output: "Dữ liệu nghiên cứu",
  content_output: "Nội dung đã soạn",
  applied: "Đã áp dụng",
  ok: "Kết quả",
  title: "Tiêu đề",
  body: "Nội dung",
  summary: "Tóm tắt",
  error: "Lỗi",
  status: "Trạng thái",
  count: "Số lượng",
  items: "Danh sách",
};

export function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** Gỡ escape của JSON (\uXXXX, \n, \") trong chuỗi đã bị serialize hai lần. */
export function decodeEscapes(text: string): string {
  if (!text.includes("\\")) return text;
  return text.replace(ESCAPE_PATTERN, (_match, group: string) => {
    if (group.startsWith("u")) return String.fromCharCode(Number.parseInt(group.slice(1), 16));
    if (group === "n") return "\n";
    if (group === "r") return "";
    if (group === "t") return "\t";
    if (group === '"') return '"';
    if (group === "/") return "/";
    return "\\";
  });
}

function stripCodeFence(text: string): string {
  const trimmed = text.trim();
  if (!trimmed.startsWith("```")) return trimmed;
  return trimmed
    .replace(/^```[a-zA-Z]*\s*/, "")
    .replace(/```$/, "")
    .trim();
}

function looksLikeJson(text: string): boolean {
  const first = text[0];
  const last = text.at(-1);
  return (first === "{" && last === "}") || (first === "[" && last === "]") || (first === '"' && last === '"');
}

/** Bóc chuỗi JSON (kể cả JSON lồng JSON) thành object/array; chuỗi thường thì chỉ gỡ escape. */
export function parseStructured(value: unknown): unknown {
  let current: unknown = value;
  for (let depth = 0; depth < MAX_UNWRAP_DEPTH; depth += 1) {
    if (typeof current !== "string") return current;
    // Giữ bản string riêng: gán lại current trong try làm TypeScript mất narrowing ở nhánh catch.
    const text = current;
    const candidate = stripCodeFence(text);
    if (!looksLikeJson(candidate)) return decodeEscapes(text);
    try {
      current = JSON.parse(candidate) as unknown;
    } catch {
      return decodeEscapes(text);
    }
  }
  return current;
}

/** snake_case / camelCase -> nhãn đọc được; ưu tiên từ điển tiếng Việt. */
export function humanizeKey(key: string): string {
  const normalized = key.trim();
  const mapped = KEY_LABELS[normalized.toLowerCase()];
  if (mapped) return mapped;

  const spaced = normalized
    .replace(/[_-]+/g, " ")
    .replace(/([a-z\d])([A-Z])/g, "$1 $2")
    .trim();
  if (!spaced) return key;
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** Giá trị vô hướng -> chuỗi hiển thị (ngày giờ, số, boolean theo tiếng Việt). */
export function formatScalar(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "boolean") return value ? "Có" : "Không";
  if (typeof value === "number") return Number.isFinite(value) ? value.toLocaleString("vi-VN") : String(value);
  if (typeof value !== "string") return String(value);

  const text = decodeEscapes(value).trim();
  const taskRef = TASK_REF_PATTERN.exec(text);
  if (taskRef) return `Kết quả của bước ${taskRef[1]}`;
  if (ISO_DATE_PATTERN.test(text)) {
    const parsed = new Date(text);
    if (!Number.isNaN(parsed.getTime())) return parsed.toLocaleString("vi-VN");
  }
  return text;
}

/** GUID hiển thị dạng rút gọn để bảng không bị vỡ; giữ nguyên bản đầy đủ ở tooltip. */
export function isGuid(value: string): boolean {
  return GUID_PATTERN.test(value.trim());
}

export function toPrettyJson(value: unknown): string {
  try {
    return typeof value === "string" ? value : JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}
