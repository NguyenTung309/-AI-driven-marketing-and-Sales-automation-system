export type SemanticTone = "info" | "success" | "warning" | "error";

const TONE_CLASSES: Record<SemanticTone, string> = {
  info: "bg-blue-100 text-blue-700",
  success: "bg-emerald-100 text-emerald-700",
  warning: "bg-amber-100 text-amber-700",
  error: "bg-red-100 text-red-700",
};

export function toneClasses(tone: SemanticTone): string {
  return TONE_CLASSES[tone];
}

export type PlatformKey = "facebook" | "zalo" | "tiktok" | "website" | "other";

const PLATFORM_CLASSES: Record<PlatformKey, string> = {
  facebook: "bg-blue-100 text-blue-700 border-blue-200",
  zalo: "bg-indigo-100 text-indigo-700 border-indigo-200",
  tiktok: "bg-slate-100 text-slate-800 border-slate-200",
  website: "bg-emerald-100 text-emerald-700 border-emerald-200",
  other: "bg-surface-container text-secondary border-outline",
};

export function platformKey(platform: string): PlatformKey {
  const value = platform.toLowerCase();
  if (value.includes("facebook") || value === "fb") return "facebook";
  if (value.includes("zalo") || value === "zl") return "zalo";
  if (value.includes("tiktok")) return "tiktok";
  if (value.includes("web")) return "website";
  return "other";
}

export function platformClasses(platform: string): string {
  return PLATFORM_CLASSES[platformKey(platform)];
}
