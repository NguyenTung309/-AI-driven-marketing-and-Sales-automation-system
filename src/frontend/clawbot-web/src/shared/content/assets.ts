// Asset của bài viết lưu dưới dạng chuỗi JSON trong content_items.assets_json.
// Parse phòng thủ: dữ liệu hỏng chỉ dẫn tới "không có ảnh", không được làm vỡ màn hình.
export interface ContentAsset {
  readonly type?: string;
  readonly url?: string;
  readonly fileName?: string;
  readonly contentType?: string;
}

export function parseAssets(value: string | null | undefined): readonly ContentAsset[] {
  if (!value || value === "[]") return [];
  try {
    const parsed = JSON.parse(value) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((item): item is ContentAsset => typeof item === "object" && item !== null && "url" in item);
  } catch {
    return [];
  }
}

export function assetsSummary(value: string | null | undefined): string {
  const count = parseAssets(value).length;
  return count ? `${count} tệp đính kèm` : "Chưa có tệp đính kèm";
}

export function imageAssets(value: string | null | undefined): readonly ContentAsset[] {
  return parseAssets(value).filter((asset) => asset.url && (!asset.type || asset.type === "image"));
}

export function firstImageAsset(value: string | null | undefined): ContentAsset | null {
  return imageAssets(value)[0] ?? null;
}
