interface ToneCheckResult {
  readonly hasIssue: boolean;
  readonly message: string;
}

const BLACKLISTED_WORDS = [
  "may ngu", "dien", "khung", "xau", "ngu", "cc", "cl", "dm", "đm",
  "vl", "vkl", "loz", "cặc", "lồn", "đĩ", "chó", "thằng", "con mẹ",
];

export function checkTone(content: string): ToneCheckResult {
  const lower = content.toLowerCase().trim();

  // Skip check empty or too short
  if (lower.length < 3) return { hasIssue: false, message: "" };

  // Check blacklisted words
  for (const word of BLACKLISTED_WORDS) {
    if (lower.includes(word)) {
      return {
        hasIssue: true,
        message: "Tin nhan co chua tu ngu khong phu hop. Ban co muon chinh sua?",
      };
    }
  }

  // Check ALL CAPS (more than 5 chars in a row)
  const capsPattern = /[A-ZÀ-Ỹ]{6,}/;
  if (capsPattern.test(content)) {
    return {
      hasIssue: true,
      message: "Tin nhan co nhieu chu viet hoa, co the gay hieu lam ve thai do.",
    };
  }

  // Check consecutive exclamation/question marks
  if (/[{2,}|?{2,}/.test(content)) {
    return {
      hasIssue: true,
      message: "Tin nhan co nhieu dau cau, co the gay ap luc cho khach.",
    };
  }

  return { hasIssue: false, message: "" };
}
