import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchGeneratedDocumentBlob } from "@/shared/api/documents";

const FILE_ERROR = "Không tải được file PDF đã tạo.";

/**
 * Object URL cho PDF đã sinh, tải qua endpoint có xác thực.
 *
 * Không thể trỏ thẳng <iframe src> vào `fileUrl` (`/generated-docs/...`): đường dẫn đó không được
 * host nào phục vụ nên rơi vào SPA fallback và react-router bắn "Unexpected Application Error! 404".
 * Điều hướng thuần của trình duyệt cũng không mang Bearer token của axios interceptor.
 */
export function useGeneratedDocumentUrl(documentId: string | null): {
  readonly url: string | null;
  readonly error: string | null;
  readonly isLoading: boolean;
} {
  const query = useQuery({
    queryKey: ["docs", "generated", "file", documentId],
    queryFn: () => fetchGeneratedDocumentBlob(documentId as string),
    enabled: Boolean(documentId),
    staleTime: 5 * 60_000,
    retry: false,
  });

  // Sinh object URL khi render (useMemo) chứ không setState trong effect — effect chỉ lo thu hồi
  // URL cũ, tránh cascading render mà react-hooks/set-state-in-effect cảnh báo.
  const url = useMemo(() => (query.data ? URL.createObjectURL(query.data) : null), [query.data]);

  useEffect(() => {
    if (!url) return;
    return () => URL.revokeObjectURL(url);
  }, [url]);

  return {
    url,
    error: query.isError ? FILE_ERROR : null,
    isLoading: query.isFetching,
  };
}

/**
 * Mở/lưu file PDF theo yêu cầu người dùng. Dùng thẻ <a download> thay vì window.open để không bị
 * popup blocker chặn (blob chỉ có sau khi request async trả về, đã ra khỏi phạm vi user gesture).
 */
export function useOpenGeneratedDocument(): {
  readonly open: (documentId: string, fileName: string) => Promise<void>;
  readonly error: string | null;
  readonly pendingId: string | null;
} {
  const [error, setError] = useState<string | null>(null);
  const [pendingId, setPendingId] = useState<string | null>(null);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const open = useCallback(async (documentId: string, fileName: string) => {
    setPendingId(documentId);
    setError(null);
    try {
      const blob = await fetchGeneratedDocumentBlob(documentId);
      const objectUrl = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = objectUrl;
      anchor.download = fileName;
      anchor.rel = "noreferrer";
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      // Thu hồi sau khi trình duyệt kịp đọc blob.
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
    } catch {
      if (mounted.current) setError(FILE_ERROR);
    } finally {
      if (mounted.current) setPendingId(null);
    }
  }, []);

  return { open, error, pendingId };
}
