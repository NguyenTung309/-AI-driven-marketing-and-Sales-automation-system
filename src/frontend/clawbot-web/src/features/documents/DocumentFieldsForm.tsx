import type { TemplateField } from "@/shared/api/documents";
import { toDateInputValue } from "./templateModel";

const INPUT_CLASS =
  "w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary";

function inputType(type: TemplateField["type"]): string {
  if (type === "date") return "date";
  if (type === "number") return "number";
  return "text";
}

function hint(field: TemplateField): string | null {
  if (field.type === "currency") return "Nhập số tiền, ví dụ 4.500.000đ";
  if (field.type === "date") return null;
  return null;
}

/** Biểu mẫu nhập dữ liệu tài liệu, dựng động từ danh sách trường của mẫu. */
export function DocumentFieldsForm({
  fields,
  values,
  missingKeys,
  onChange,
}: {
  readonly fields: readonly TemplateField[];
  readonly values: Readonly<Record<string, string>>;
  readonly missingKeys: readonly string[];
  readonly onChange: (key: string, value: string) => void;
}) {
  if (!fields.length) {
    return (
      <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
        Mẫu này không có trường nào cần nhập. Hệ thống sẽ tự điền thông tin khách hàng.
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {fields.map((field) => {
        const raw = values[field.key] ?? "";
        // Ô ngày chỉ nhận yyyy-MM-dd; giá trị mẫu/dữ liệu cũ dạng dd/MM/yyyy sẽ bị nuốt mất.
        const value = field.type === "date" ? toDateInputValue(raw) : raw;
        const isMissing = missingKeys.includes(field.key);
        const borderClass = isMissing ? "border-error" : "";
        const describedBy = hint(field) ? `${field.key}-hint` : undefined;
        return (
          <label key={field.key} className="block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">
              {field.label}
              {field.required ? <span className="ml-1 text-primary">*</span> : null}
            </span>
            {field.type === "multiline" ? (
              <textarea
                className={`${INPUT_CLASS} min-h-[88px] resize-y ${borderClass}`}
                value={value}
                required={field.required}
                aria-describedby={describedBy}
                placeholder={field.placeholder ?? field.sample ?? ""}
                onChange={(event) => onChange(field.key, event.target.value)}
              />
            ) : (
              <input
                className={`${INPUT_CLASS} ${borderClass}`}
                type={inputType(field.type)}
                value={value}
                required={field.required}
                aria-describedby={describedBy}
                placeholder={field.placeholder ?? field.sample ?? ""}
                onChange={(event) => onChange(field.key, event.target.value)}
              />
            )}
            {hint(field) ? (
              <span id={describedBy} className="mt-1 block text-label-sm text-on-surface-variant">
                {hint(field)}
              </span>
            ) : null}
            {isMissing ? (
              <span className="mt-1 block text-label-sm text-error">Cần nhập thông tin này.</span>
            ) : null}
          </label>
        );
      })}
    </div>
  );
}
