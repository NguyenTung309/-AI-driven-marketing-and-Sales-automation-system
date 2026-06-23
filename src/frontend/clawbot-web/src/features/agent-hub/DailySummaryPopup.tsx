import { useQuery } from "@tanstack/react-query";
import { getDailySummary } from "@/shared/api/inbox";

interface DailySummaryPopupProps {
  readonly onClose: () => void;
}

export default function DailySummaryPopup({ onClose }: DailySummaryPopupProps) {
  const { data, isLoading } = useQuery({
    queryKey: ["inbox", "daily-summary"],
    queryFn: getDailySummary,
    refetchOnMount: true,
  });

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center pt-[10vh]" onClick={onClose}>
      <div className="absolute inset-0 bg-black/30" />
      <div
        className="relative w-full max-w-sm rounded-xl border border-outline bg-surface-container-lowest shadow-2xl p-5"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-heading-sm font-bold text-secondary">Bao cao cuoi ngay</h3>
          <button type="button" onClick={onClose} className="text-on-surface-variant hover:text-secondary">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <div className="size-6 animate-spin rounded-full border-2 border-primary border-t-transparent" />
          </div>
        ) : data ? (
          <div className="grid grid-cols-2 gap-3">
            <div className="rounded-lg bg-primary/5 p-3 text-center">
              <p className="text-heading-lg font-bold text-primary">{data.conversationsHandled}</p>
              <p className="text-label-sm text-on-surface-variant">Hoi thoai</p>
            </div>
            <div className="rounded-lg bg-primary/5 p-3 text-center">
              <p className="text-heading-lg font-bold text-primary">{data.messagesSent}</p>
              <p className="text-label-sm text-on-surface-variant">Tin nhan</p>
            </div>
            <div className="rounded-lg bg-warning-container/30 p-3 text-center">
              <p className="text-heading-lg font-bold text-warning">{data.openConversations}</p>
              <p className="text-label-sm text-on-surface-variant">Dang mo</p>
            </div>
            <div className="rounded-lg bg-success-container/30 p-3 text-center">
              <p className="text-heading-lg font-bold text-success">{data.closeRate}%</p>
              <p className="text-label-sm text-on-surface-variant">Chot</p>
            </div>
          </div>
        ) : (
          <p className="text-center text-body-md text-on-surface-variant py-6">Chua co du lieu hom nay.</p>
        )}

        {data && (
          <p className="mt-4 text-center text-label-sm text-on-surface-variant">
            Ngay: {data.date}
          </p>
        )}
      </div>
    </div>
  );
}
