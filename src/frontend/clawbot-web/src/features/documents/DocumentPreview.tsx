import { splitPreviewTokens } from "./templateModel";

/**
 * Xem trước đúng như PDF sẽ in: chữ thuần, dòng đầu là tiêu đề in đậm.
 * Không dùng iframe HTML nữa vì bộ dựng PDF không hiểu thẻ HTML.
 */
export function DocumentPreview({ body }: { readonly body: string }) {
  const lines = body.replace(/\r\n/g, "\n").split("\n");
  // Dòng nội dung đầu tiên là tiêu đề — trùng quy ước của bộ dựng PDF.
  const titleIndex = lines.findIndex((line) => line.trim().length > 0);

  return (
    <div className="min-h-[420px] rounded-lg border border-outline bg-white px-8 py-7 text-secondary shadow-sm">
      {lines.map((line, index) => {
        const text = line.trimEnd();
        if (!text) {
          return <div key={index} className="h-3" />;
        }
        const tokens = splitPreviewTokens(text);
        const isTitle = index === titleIndex;
        return (
          <p
            key={index}
            className={isTitle ? "mb-3 text-headline-sm font-bold text-secondary" : "text-body-md leading-relaxed"}
          >
            {tokens.map((token, tokenIndex) =>
              token.missing ? (
                <span key={tokenIndex} className="rounded bg-amber-100 px-1 text-on-surface-variant">
                  {token.text}
                </span>
              ) : (
                <span key={tokenIndex}>{token.text}</span>
              ),
            )}
          </p>
        );
      })}
    </div>
  );
}
