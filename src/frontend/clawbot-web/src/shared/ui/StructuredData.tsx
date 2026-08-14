import { useMemo, useState } from "react";
import {
  formatScalar,
  humanizeKey,
  isGuid,
  isPlainObject,
  parseStructured,
  toPrettyJson,
} from "@/shared/utils/structuredText";

// Hiển thị payload kỹ thuật (đầu vào task, kết quả tool, properties log, diff audit) dưới dạng bảng
// nhãn - giá trị thay vì đổ nguyên JSON. Chuỗi JSON lồng nhau được bóc tiếp, tiếng Việt bị escape
// \uXXXX được giải mã. Vẫn giữ nút xem JSON gốc cho người cần dữ liệu thô.

const MAX_NESTING_DEPTH = 4;
const LONG_TEXT_THRESHOLD = 120;

export interface StructuredDataProps {
  readonly value: unknown;
  readonly emptyText?: string;
  readonly maxHeightClass?: string;
  readonly showRawToggle?: boolean;
}

export function StructuredData({
  value,
  emptyText = "Không có dữ liệu.",
  maxHeightClass = "max-h-72",
  showRawToggle = true,
}: StructuredDataProps) {
  const parsed = useMemo(() => parseStructured(value), [value]);
  const [showRaw, setShowRaw] = useState(false);

  if (isEmptyValue(parsed)) return <p className="text-body-sm text-on-surface-variant">{emptyText}</p>;

  const isStructured = isPlainObject(parsed) || Array.isArray(parsed);

  return (
    <div className="flex flex-col gap-1.5">
      <div className={`overflow-auto ${maxHeightClass}`}>
        {showRaw ? (
          <pre className="whitespace-pre-wrap break-words font-mono text-mono-status text-on-surface-variant">
            {toPrettyJson(parsed)}
          </pre>
        ) : (
          <ValueNode value={parsed} depth={0} />
        )}
      </div>
      {showRawToggle && isStructured ? (
        <button
          className="self-start text-label-sm text-primary hover:underline"
          onClick={() => setShowRaw((current) => !current)}
          type="button"
        >
          {showRaw ? "Xem dạng bảng" : "Xem JSON gốc"}
        </button>
      ) : null}
    </div>
  );
}

function isEmptyValue(value: unknown): boolean {
  if (value === null || value === undefined || value === "") return true;
  if (Array.isArray(value)) return value.length === 0;
  if (isPlainObject(value)) return Object.keys(value).length === 0;
  return false;
}

function ValueNode({ value, depth }: { readonly value: unknown; readonly depth: number }) {
  if (depth >= MAX_NESTING_DEPTH && (isPlainObject(value) || Array.isArray(value))) {
    return (
      <pre className="whitespace-pre-wrap break-words font-mono text-mono-status text-on-surface-variant">
        {toPrettyJson(value)}
      </pre>
    );
  }
  if (Array.isArray(value)) return <ArrayNode items={value} depth={depth} />;
  if (isPlainObject(value)) return <ObjectNode data={value} depth={depth} />;
  return <ScalarNode value={value} />;
}

function ObjectNode({ data, depth }: { readonly data: Record<string, unknown>; readonly depth: number }) {
  return (
    <dl className="flex min-w-0 flex-col gap-1">
      {Object.entries(data).map(([key, raw]) => {
        const child = parseStructured(raw);
        const nested = isPlainObject(child) || Array.isArray(child);
        return (
          <div className={nested ? "flex min-w-0 flex-col gap-1" : "grid min-w-0 grid-cols-[minmax(0,9rem)_minmax(0,1fr)] gap-x-3"} key={key}>
            <dt className="truncate text-label-sm text-on-surface-variant" title={key}>
              {humanizeKey(key)}
            </dt>
            <dd className={nested ? "min-w-0 rounded border border-outline-variant bg-surface p-2" : "min-w-0"}>
              <ValueNode value={child} depth={depth + 1} />
            </dd>
          </div>
        );
      })}
    </dl>
  );
}

function ArrayNode({ items, depth }: { readonly items: readonly unknown[]; readonly depth: number }) {
  const allScalars = items.every((item) => !isPlainObject(item) && !Array.isArray(item));
  if (allScalars) {
    return (
      <ul className="flex flex-wrap gap-1">
        {items.map((item, index) => (
          <li
            className="max-w-full truncate rounded-full bg-surface-variant px-2 py-0.5 text-label-sm text-on-surface-variant"
            key={`${index}-${String(item)}`}
          >
            {formatScalar(item)}
          </li>
        ))}
      </ul>
    );
  }

  return (
    <ol className="flex min-w-0 flex-col gap-1">
      {items.map((item, index) => (
        <li className="min-w-0 rounded border border-outline-variant bg-surface p-2" key={`item-${index}`}>
          <p className="text-label-sm text-on-surface-variant">#{index + 1}</p>
          <ValueNode value={parseStructured(item)} depth={depth + 1} />
        </li>
      ))}
    </ol>
  );
}

function ScalarNode({ value }: { readonly value: unknown }) {
  const text = formatScalar(value);
  if (typeof value === "string" && isGuid(value)) {
    return (
      <span className="block truncate font-mono text-mono-status text-on-surface-variant" title={value}>
        {text}
      </span>
    );
  }
  if (text.includes("\n") || text.length > LONG_TEXT_THRESHOLD) {
    return <p className="whitespace-pre-wrap break-words text-body-sm text-on-surface">{text}</p>;
  }
  return <span className="break-words text-body-sm text-on-surface">{text}</span>;
}
