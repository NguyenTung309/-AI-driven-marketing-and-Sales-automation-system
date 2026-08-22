import { useEffect, useState } from "react";
import { getContentAssetBlob } from "@/shared/api/content";

export interface ContentAssetImageProps {
  readonly alt: string;
  readonly className: string;
  readonly url: string;
}

// Ảnh nằm sau endpoint có xác thực (/api/...) nên không gắn thẳng vào src được: phải tải bằng
// axios kèm token rồi dựng object URL, và thu hồi khi unmount để không rò bộ nhớ.
export function ContentAssetImage({ alt, className, url }: ContentAssetImageProps) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const requiresAuthenticatedFetch = url.startsWith("/api/");

  useEffect(() => {
    if (!requiresAuthenticatedFetch) return undefined;

    let active = true;
    let nextObjectUrl: string | null = null;
    void getContentAssetBlob(url)
      .then((blob) => {
        if (!active) return;
        nextObjectUrl = URL.createObjectURL(blob);
        setObjectUrl(nextObjectUrl);
        setFailed(false);
      })
      .catch(() => {
        if (active) setFailed(true);
      });
    return () => {
      active = false;
      if (nextObjectUrl) URL.revokeObjectURL(nextObjectUrl);
    };
  }, [requiresAuthenticatedFetch, url]);

  const displayUrl = requiresAuthenticatedFetch ? objectUrl : url;
  if (requiresAuthenticatedFetch && failed) {
    return (
      <div className={`${className} flex items-center justify-center bg-surface text-label-sm text-on-surface-variant`}>
        Không tải được ảnh.
      </div>
    );
  }
  if (!displayUrl) return <div className={className + " animate-pulse bg-surface"} aria-label="Đang tải ảnh" />;
  return <img className={className} src={displayUrl} alt={alt} />;
}
