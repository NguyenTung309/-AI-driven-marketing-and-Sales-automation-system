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

export function operationalPhaseLabel(value: string | null | undefined): string {
  const normalized = (value ?? "").trim().toLowerCase();
  if (!normalized) return "Thông tin";
  if (normalized.includes("error") || normalized.includes("fail")) return "Lỗi";
  if (normalized.includes("warn")) return "Cảnh báo";
  if (normalized === "input") return "Đầu vào";
  if (normalized === "reply") return "Phản hồi";
  if (normalized.includes("plan")) return "Lập kế hoạch";
  if (normalized.includes("block") || normalized.includes("missing")) return "Bị chặn";
  if (normalized.includes("prompt")) return "Prompt";
  if (normalized.includes("complete") || normalized.includes("success")) return "Hoàn tất";
  if (normalized.includes("start") || normalized.includes("running") || normalized.includes("process")) return "Đang xử lý";
  return value?.trim() || "Thông tin";
}
