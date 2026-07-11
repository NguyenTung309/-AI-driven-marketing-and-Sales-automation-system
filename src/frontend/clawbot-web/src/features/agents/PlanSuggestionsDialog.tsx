import { useState } from "react";
import { Button } from "@/shared/ui";
import type { OrchestrationPlanSuggestion } from "@/shared/api/orchestrationV2";

const CADENCE_LABELS: Record<string, string> = {
  daily: "Hằng ngày",
  weekly: "Hằng tuần",
  monthly: "Hằng tháng",
  quarterly: "Hằng quý",
};

interface PlanSuggestionsDialogProps {
  readonly suggestions: readonly OrchestrationPlanSuggestion[];
  readonly skippedDuplicates: number;
  readonly applying: boolean;
  readonly onApply: (selected: readonly OrchestrationPlanSuggestion[]) => void;
  readonly onClose: () => void;
}

// Checklist kế hoạch do orchestrator đề xuất — user tick chọn rồi tạo hàng loạt schedule.
export function PlanSuggestionsDialog({ suggestions, skippedDuplicates, applying, onApply, onClose }: PlanSuggestionsDialogProps) {
  const [checked, setChecked] = useState<ReadonlySet<number>>(new Set(suggestions.map((_, i) => i)));

  function toggle(index: number) {
    setChecked((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  }

  const selectedCount = checked.size;

  return (
    <div className="fixed inset-0 z-[95] flex items-center justify-center bg-black/40 p-4" role="dialog" aria-modal="true">
      <div className="flex max-h-[85vh] w-full max-w-2xl flex-col overflow-hidden rounded-xl border border-outline bg-surface-container-lowest shadow-2xl">
        <header className="flex items-center justify-between border-b border-outline px-5 py-4">
          <div>
            <h2 className="text-headline-sm">Kế hoạch đề xuất cho hệ thống</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Orchestrator đã quét dữ liệu và đề xuất các kế hoạch định kỳ chưa có. Bỏ tick kế hoạch không cần.
              {skippedDuplicates > 0 ? ` (${skippedDuplicates} đề xuất trùng kế hoạch sẵn có đã bị loại)` : ""}
            </p>
          </div>
          <button type="button" aria-label="Đóng" onClick={onClose} className="rounded p-1 text-secondary hover:bg-surface">
            <span aria-hidden="true" className="material-symbols-outlined">close</span>
          </button>
        </header>

        <div className="flex-1 space-y-3 overflow-y-auto p-5">
          {suggestions.map((suggestion, index) => (
            <label
              key={`${suggestion.name}-${index}`}
              className={[
                "flex cursor-pointer items-start gap-3 rounded-lg border p-4 transition-colors",
                checked.has(index) ? "border-primary/40 bg-primary/5" : "border-outline bg-surface-container-lowest hover:bg-surface",
              ].join(" ")}
            >
              <input
                type="checkbox"
                className="mt-1 size-4 accent-primary"
                checked={checked.has(index)}
                onChange={() => toggle(index)}
              />
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-body-lg font-bold text-on-surface">{suggestion.name}</span>
                  <span className="rounded bg-surface-container px-2 py-0.5 text-label-sm font-semibold text-secondary">
                    {CADENCE_LABELS[suggestion.cadence] ?? suggestion.cadence}
                  </span>
                </div>
                <p className="mt-1 whitespace-pre-wrap text-body-md text-on-surface">{suggestion.goal}</p>
                {suggestion.reason ? (
                  <p className="mt-1 text-label-sm text-on-surface-variant">Lý do: {suggestion.reason}</p>
                ) : null}
              </div>
            </label>
          ))}
        </div>

        <footer className="flex items-center justify-between border-t border-outline px-5 py-4">
          <span className="text-body-md text-on-surface-variant">{selectedCount}/{suggestions.length} kế hoạch được chọn</span>
          <div className="flex gap-2">
            <Button type="button" variant="ghost" onClick={onClose} disabled={applying}>Bỏ qua</Button>
            <Button
              type="button"
              disabled={applying || selectedCount === 0}
              onClick={() => onApply(suggestions.filter((_, i) => checked.has(i)))}
            >
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">playlist_add_check</span>
              {applying ? "Đang tạo..." : `Tạo ${selectedCount} kế hoạch`}
            </Button>
          </div>
        </footer>
      </div>
    </div>
  );
}
