import { isAxiosError } from "axios";

const DEFAULT_ERROR = "Không xử lý được yêu cầu. Vui lòng thử lại.";

const STATUS_MESSAGES: Partial<Record<number, string>> = {
  400: "Thông tin gửi lên chưa hợp lệ. Vui lòng kiểm tra lại.",
  401: "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
  403: "Bạn chưa có quyền thực hiện thao tác này.",
  404: "Không tìm thấy dữ liệu cần thao tác.",
  409: "Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",
  422: "Một số thông tin chưa hợp lệ. Vui lòng kiểm tra lại.",
  423: "Tài khoản hoặc thao tác đang bị tạm khóa.",
  429: "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.",
};

const CONTENT_ERROR_MESSAGES: Readonly<Record<string, string>> = {
  "content.meta_page_required": "Chưa có Facebook Page sẵn sàng đăng. Hãy kết nối Page trong phần Quản trị hệ thống rồi thử lại.",
  "content.item_not_schedulable": "Nội dung này chưa đủ điều kiện lên lịch. Hãy duyệt lại bản nội dung hiện tại rồi thử lại.",
  "content.approval_context_missing": "Nội dung này thiếu thông tin phê duyệt. Hãy duyệt lại bản nội dung hiện tại rồi lên lịch.",
  "content.schedule_in_past": "Thời điểm đăng phải ở tương lai. Hãy chọn lại ngày giờ.",
  "content.meta_page_invalid": "Page đã chọn không phù hợp với kênh đăng của nội dung này.",
  "content.instagram_credentials_invalid": "Thông tin kết nối Instagram chưa hợp lệ. Hãy kiểm tra lại trong phần Quản trị hệ thống.",
  "content.instagram_target_mode_conflict": "Cấu hình đích đăng Instagram đang xung đột. Hãy chọn lại tài khoản hoặc Meta Page.",
  "content.instagram_target_required": "Hãy chọn tài khoản hoặc Meta Page để đăng Instagram.",
  "content.instagram_reconnect_required": "Kết nối Instagram đã hết hiệu lực. Hãy kết nối lại rồi thử lại.",
  "content.instagram_permissions_missing": "Kết nối Instagram đang thiếu quyền cần thiết. Hãy kết nối lại và cấp đủ quyền.",
  "content.instagram_not_linked": "Instagram chưa được liên kết với Meta Page đã chọn.",
  "content.instagram_target_unavailable": "Không thể dùng tài khoản Instagram đã chọn. Hãy chọn lại đích đăng.",
  "content.instagram_meta_unavailable": "Không thể kiểm tra tài khoản Meta/Instagram lúc này. Hãy thử lại sau.",
};

type ApiErrorBody = Readonly<{ errorCode?: unknown; message?: unknown }>;

export function contentErrorMessage(errorCode: string | null | undefined): string | null {
  return errorCode ? CONTENT_ERROR_MESSAGES[errorCode] ?? null : null;
}

function errorCodeFromResponse(value: unknown): string | null {
  if (!value || typeof value !== "object") return null;
  const { errorCode } = value as ApiErrorBody;
  return typeof errorCode === "string" ? errorCode : null;
}

const ORCHESTRATION_FAILURE_HINTS: readonly { readonly needle: string; readonly message: string }[] = [
  { needle: "llm_config_not_configured", message: "Có agent trong kế hoạch chưa được gắn LLM. Mở Sơ đồ agent → Cấu hình → tab LLM để gắn, rồi gửi lại mục tiêu." },
  { needle: "cost_cap", message: "Phiên bị chặn vì vượt hạn mức chi phí AI của tháng. Kiểm tra thẻ Chi phí AI hoặc nâng hạn mức trước khi chạy lại." },
  { needle: "planning_failed", message: "Orchestrator không lập được kế hoạch từ mục tiêu này. Viết mục tiêu cụ thể hơn (kênh, số lượng, thời hạn) rồi gửi lại." },
  { needle: "tool_permission_denied", message: "Một agent bị chặn vì thiếu quyền dùng công cụ. Kiểm tra danh sách công cụ được phép của agent trong phần Cấu hình." },
  { needle: "refused_without_tool_use", message: "Agent chưa gọi công cụ nào nên không có kết quả thực tế. Hãy chạy lại mục tiêu sau khi kiểm tra công cụ được cấp cho agent." },
  { needle: "blocked_missing_tool_use", message: "Agent kết thúc khi chưa gọi công cụ cần thiết. Hãy chạy lại mục tiêu để hệ thống yêu cầu agent thực hiện hành động." },
  { needle: "tool_execution_incomplete", message: "Agent đã thử công cụ nhưng chưa hoàn tất bước thực thi. Kiểm tra nhật ký công cụ rồi chạy lại mục tiêu." },
  { needle: "unknown_tool", message: "Agent gọi một công cụ không tồn tại nên hành động đó chưa được thực hiện. Kiểm tra danh sách công cụ được cấp cho agent rồi chạy lại." },
  { needle: "tool_error", message: "Công cụ gặp lỗi khi chạy nên bước này chưa hoàn tất. Xem nhật ký công cụ để biết chi tiết rồi chạy lại mục tiêu." },
  { needle: "re_act_loop_exhausted", message: "Agent dùng hết số bước gọi công cụ mà chưa ra kết quả cuối. Thu hẹp phạm vi mục tiêu rồi chạy lại." },
  { needle: "max_rounds", message: "Đã dùng hết số lần lập lại kế hoạch cho phép — mục tiêu có thể quá phức tạp, thử chia nhỏ thành nhiều mục tiêu." },
];

export function toUserFriendlyOrchestrationError(value: string | null | undefined): string | null {
  const haystack = (value ?? "").toLowerCase();
  return ORCHESTRATION_FAILURE_HINTS.find((hint) => haystack.includes(hint.needle))?.message ?? null;
}

export interface UserFriendlyErrorOptions {
  readonly fallback?: string;
  readonly statusMessages?: Partial<Record<number, string>>;
}

export function toUserFriendlyError(error: unknown, options: UserFriendlyErrorOptions | string = {}): string {
  if (!error) return "";

  const normalizedOptions: UserFriendlyErrorOptions = typeof options === "string" ? { fallback: options } : options;
  const fallback = normalizedOptions.fallback ?? DEFAULT_ERROR;

  if (isAxiosError(error)) {
    const status = error.response?.status;
    const errorCode = errorCodeFromResponse(error.response?.data);
    if (errorCode && CONTENT_ERROR_MESSAGES[errorCode]) return CONTENT_ERROR_MESSAGES[errorCode];
    if (status && normalizedOptions.statusMessages?.[status]) return normalizedOptions.statusMessages[status] as string;
    if (status && STATUS_MESSAGES[status]) return STATUS_MESSAGES[status] as string;
    if (status && status >= 500) return "Hệ thống đang gặp sự cố. Vui lòng thử lại sau.";
    if (error.code === "ECONNABORTED") return "Kết nối mất quá lâu. Vui lòng thử lại.";
    return fallback;
  }

  return fallback;
}

const TECHNICAL_TEXT_PATTERNS = [
  /\bhttps?:\/\/\S+/i,
  /\b\/api\/[^\s)"']+/i,
  /\b[A-Z]:\\[^\s)"']+/i,
  /\bat\s+\S+\s+\(/i,
  /\b(exception|stack trace|traceback|axioserror|httprequestexception)\b/i,
  /\b(traceid|requestid|connectionid)\b/i,
  /\b(sql|select|insert|update|delete)\s+/i,
];

export function toSafeOperationalText(value: string | null | undefined, fallback = "Đã ghi nhận sự kiện vận hành."): string {
  const text = (value ?? "").trim();
  if (!text) return fallback;
  if (TECHNICAL_TEXT_PATTERNS.some((pattern) => pattern.test(text))) return fallback;
  return text;
}

export function formatOperationalTraceMessage(
  phase: string | null | undefined,
  message: string | null | undefined,
): string {
  const normalizedPhase = (phase ?? "").trim().toLowerCase();
  return normalizedPhase === "input" || normalizedPhase === "reply"
    ? (message ?? "").trim()
    : toSafeOperationalText(message);
}

export function toSafeCsvCell(value: unknown): string {
  const text = String(value ?? "");
  const formulaSafe = /^[=+\-@]/.test(text) ? `'${text}` : text;
  return `"${formulaSafe.replaceAll('"', '""')}"`;
}

export function operationalPhaseLabel(value: string | null | undefined): string {
  const normalized = (value ?? "").trim().toLowerCase();
  if (!normalized) return "Thông tin";
  // Phase máy của orchestrator — đặt trước heuristic để phân biệt "đang tự thử lại" với lỗi hẳn.
  if (normalized === "transient_retry") return "Tự thử lại";
  if (normalized === "dependency_blocked") return "Chờ phụ thuộc";
  if (normalized === "planning_failed") return "Lập kế hoạch thất bại";
  if (normalized === "replan") return "Lập lại kế hoạch";
  // Review gate: phiên dừng sau task hoàn tất để người dùng duyệt hoặc sửa trước khi chạy tiếp.
  if (normalized === "awaiting_approval") return "Chờ bạn duyệt";
  if (normalized === "awaiting_intervention") return "Chờ bạn xử lý";
  if (normalized === "task_edited") return "Người dùng sửa kết quả";
  if (normalized === "task_retry") return "Cho chạy lại bước";
  if (normalized === "task_skipped") return "Bỏ qua bước";
  if (normalized.includes("error") || normalized.includes("fail")) return "Lỗi";
  if (normalized.includes("warn")) return "Cảnh báo";
  if (normalized === "input") return "Đầu vào";
  if (normalized === "reply") return "Phản hồi";
  if (normalized === "tool_blocked") return "Công cụ bị chặn";
  if (normalized === "tool_skipped") return "Chưa gọi công cụ";
  if (normalized.includes("tool")) return "Công cụ";
  if (normalized.includes("plan")) return "Lập kế hoạch";
  if (normalized.includes("block") || normalized.includes("missing")) return "Bị chặn";
  if (normalized.includes("prompt")) return "Prompt";
  if (normalized.includes("complete") || normalized.includes("success")) return "Hoàn tất";
  if (normalized.includes("start") || normalized.includes("running") || normalized.includes("process")) return "Đang xử lý";
  return value?.trim() || "Thông tin";
}

const TOOL_RESULTS_MARKER = "[tool_results]";

// Splits an agent task output into its human text and the structured tool-result block the worker appends
// (`[tool_results]\n{json}`). Tool results are operational identifiers (content_id, schedule_id, post_url) the
// user explicitly wants to see, so they are returned verbatim — they are not redacted like free-text traces.
export function toHumanTaskSummary(output: string | null | undefined): string {
  const { text, toolResults } = splitToolResults(output);
  const cleaned = text
    .replaceAll("```json", "")
    .replaceAll("```", "")
    .trim();
  if (cleaned) return cleaned;
  if (!toolResults || Object.keys(toolResults).length === 0) return "Chưa có kết quả để hiển thị.";

  const resultLabels: Readonly<Record<string, string>> = {
    content_id: "Đã tạo nội dung.",
    schedule_id: "Đã tạo lịch đăng.",
    post_url: "Đã đăng nội dung.",
    lead_id: "Đã cập nhật khách hàng tiềm năng.",
    conversation_id: "Đã cập nhật cuộc hội thoại.",
  };
  return Object.keys(toolResults)
    .map((key) => resultLabels[key] ?? `Đã hoàn tất: ${key.replaceAll("_", " ")}.`)
    .join(" ");
}

export function splitToolResults(output: string | null | undefined): {
  readonly text: string;
  readonly toolResults: Readonly<Record<string, string>> | null;
} {
  const raw = (output ?? "").trim();
  if (!raw) return { text: "", toolResults: null };
  const idx = raw.indexOf(TOOL_RESULTS_MARKER);
  if (idx < 0) return { text: raw, toolResults: null };
  const text = raw.slice(0, idx).trim();
  const jsonPart = raw.slice(idx + TOOL_RESULTS_MARKER.length).trim();
  try {
    const parsed: unknown = JSON.parse(jsonPart);
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      const flat: Record<string, string> = {};
      for (const [key, val] of Object.entries(parsed as Record<string, unknown>))
        flat[key] = typeof val === "string" ? val : JSON.stringify(val);
      return { text, toolResults: flat };
    }
  } catch {
    /* not parseable JSON — fall through and treat the whole thing as text */
  }
  return { text: raw, toolResults: null };
}

// Nghịch đảo của splitToolResults: ghép phần văn bản với khối tool_results theo đúng định dạng worker sinh ra,
// để output do người sửa tay vẫn được agent kế tiếp đọc như output máy sinh.
export function joinToolResults(text: string, toolResults: Readonly<Record<string, string>> | null): string {
  const body = text.trim();
  if (!toolResults || Object.keys(toolResults).length === 0) return body;
  const block = `${TOOL_RESULTS_MARKER}\n${JSON.stringify(toolResults, null, 2)}`;
  return body ? `${body}\n${block}` : block;
}
