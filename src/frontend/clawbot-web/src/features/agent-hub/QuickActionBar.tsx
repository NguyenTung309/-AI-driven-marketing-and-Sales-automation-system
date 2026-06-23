import { Button } from "@/shared/ui";

interface Props {
  readonly conversationId: string | null;
  readonly status: string;
  readonly onResolve: () => void;
  readonly onAssign: () => void;
}

export default function QuickActionBar({ conversationId, status, onResolve, onAssign }: Props) {
  if (!conversationId) return null;
  return (
    <div className="flex items-center gap-2 border-t border-outline bg-surface-container-lowest px-4 py-2">
      {status !== "resolved" ? (
        <Button type="button" size="sm" variant="outline" onClick={onResolve}>
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">check_circle</span>
          Resolve
        </Button>
      ) : null}
      <Button type="button" size="sm" variant="outline" onClick={onAssign}>
        <span aria-hidden="true" className="material-symbols-outlined text-[18px]">person_add</span>
        Assign
      </Button>
    </div>
  );
}