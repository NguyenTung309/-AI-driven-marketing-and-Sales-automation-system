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
        message: "Tin nhắn có chứa từ ngữ không phù hợp. Bạn có muốn chỉnh sửa?",
      };
    }
  }

  // Check ALL CAPS (more than 5 chars in a row)
  const capsPattern = /[A-ZÀ-Ỹ]{6,}/;
  if (capsPattern.test(content)) {
    return {
      hasIssue: true,
      message: "Tin nhắn có nhiều chữ viết hoa, có thể gây hiểu lầm về thái độ.",
    };
  }

  // Check consecutive exclamation/question marks
  if (/!{2,}|\?{2,}/.test(content)) {
    return {
      hasIssue: true,
      message: "Tin nhắn có nhiều dấu câu, có thể gây áp lực cho khách.",
    };
  }

  return { hasIssue: false, message: "" };
}
