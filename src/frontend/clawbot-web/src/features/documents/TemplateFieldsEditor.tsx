import type { TemplateField } from "@/shared/api/documents";
import { Button } from "@/shared/ui";
import { FIELD_TYPE_OPTIONS } from "./templateModel";

const CELL_CLASS =
  "w-full rounded border border-outline bg-white px-2 py-1.5 text-body-md outline-none focus:border-primary";

/**
 * Khai báo trường của mẫu: nhãn, kiểu dữ liệu, bắt buộc, giá trị mẫu.
 * Khóa lấy từ placeholder trong nội dung nên không cho sửa ở đây.
 */
export function TemplateFieldsEditor({
  fields,
  onChange,
  onSyncFromBody,
}: {
  readonly fields: readonly TemplateField[];
  readonly onChange: (fields: readonly TemplateField[]) => void;
  readonly onSyncFromBody: () => void;
}) {
  function update(key: string, patch: Partial<TemplateField>) {
    onChange(fields.map((item) => (item.key === key ? { ...item, ...patch } : item)));
  }

  return (
    <div>
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <p className="text-label-caps uppercase text-secondary">Trường cần nhập</p>
        <Button type="button" variant="ghost" size="sm" onClick={onSyncFromBody}>
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">sync</span>
          Quét lại từ nội dung
        </Button>
      </div>

      {!fields.length ? (
        <div className="rounded-lg border border-dashed border-outline bg-surface p-3 text-body-md text-on-surface-variant">
          Nội dung mẫu chưa có ô điền nào. Thêm ô bằng cú pháp {"{{ ten_khach }}"} rồi bấm quét lại.
        </div>
      ) : (
        <div className="space-y-2">
          {fields.map((field) => (
            <div key={field.key} className="rounded-lg border border-outline bg-white p-3">
              <div className="mb-2 flex items-center justify-between gap-2">
                <span className="font-mono text-mono-status text-primary">{`{{ ${field.key} }}`}</span>
                <label className="flex items-center gap-2 text-label-sm text-on-surface-variant">
                  <input
                    type="checkbox"
                    checked={field.required}
                    onChange={(event) => update(field.key, { required: event.target.checked })}
                  />
                  Bắt buộc
                </label>
              </div>
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-[minmax(0,1fr)_130px]">
                <label className="block">
                  <span className="mb-1 block text-label-sm text-on-surface-variant">Nhãn hiển thị</span>
                  <input
                    className={CELL_CLASS}
                    value={field.label}
                    onChange={(event) => update(field.key, { label: event.target.value })}
                  />
                </label>
                <label className="block">
                  <span className="mb-1 block text-label-sm text-on-surface-variant">Kiểu</span>
                  <select
                    className={CELL_CLASS}
                    value={field.type}
                    onChange={(event) => update(field.key, { type: event.target.value as TemplateField["type"] })}
                  >
                    {FIELD_TYPE_OPTIONS.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
              <label className="mt-2 block">
                <span className="mb-1 block text-label-sm text-on-surface-variant">Giá trị mẫu</span>
                <input
                  className={CELL_CLASS}
                  value={field.sample ?? ""}
                  placeholder="Dùng khi bấm điền dữ liệu mẫu"
                  onChange={(event) => update(field.key, { sample: event.target.value || null })}
                />
              </label>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
