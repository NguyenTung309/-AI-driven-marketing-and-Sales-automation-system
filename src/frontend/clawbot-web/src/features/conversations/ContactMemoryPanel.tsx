import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Card } from "@/shared/ui/Card";
import {
  deleteContactMemory,
  listContactMemories,
  type ContactMemoryItem,
} from "@/shared/api/contactMemories";

const CATEGORY_LABELS: Record<ContactMemoryItem["category"], string> = {
  profile: "Hồ sơ",
  preference: "Sở thích",
  commitment: "Cam kết",
  history: "Lịch sử",
};

interface ContactMemoryPanelProps {
  readonly contactId: string | null;
  readonly onNotify?: (message: string) => void;
}

// "Ghi nhớ về khách" (ai-self-learning-memory Lớp 2): facts AI trích từ hội thoại, inject vào
// prompt khi AI trả lời. Sale gỡ được fact sai — gỡ là hạ cờ, AI ngừng dùng ngay.
export function ContactMemoryPanel({ contactId, onNotify }: ContactMemoryPanelProps) {
  const queryClient = useQueryClient();
  const memoriesQuery = useQuery({
    queryKey: ["contacts", contactId, "memories"],
    queryFn: () => listContactMemories(contactId ?? ""),
    enabled: Boolean(contactId),
  });

  const removeMutation = useMutation({
    mutationFn: (memoryId: string) => deleteContactMemory(contactId ?? "", memoryId),
    onSuccess: async () => {
      onNotify?.("Đã gỡ ghi nhớ — AI sẽ không dùng thông tin này nữa.");
      await queryClient.invalidateQueries({ queryKey: ["contacts", contactId, "memories"] });
    },
  });

  const memories = memoriesQuery.data ?? [];
  if (!contactId || (memories.length === 0 && !memoriesQuery.isLoading)) return null;

  return (
    <Card>
      <h3 className="mb-3 text-label-caps uppercase text-secondary">Ghi nhớ về khách</h3>
      {memoriesQuery.isLoading ? (
        <p className="text-body-sm text-on-surface-variant">Đang tải...</p>
      ) : (
        <ul className="space-y-2">
          {memories.map((memory) => (
            <li className="flex items-start justify-between gap-2 text-body-sm" key={memory.id}>
              <div className="min-w-0">
                <p className="text-secondary">{memory.fact}</p>
                <p className="text-label-sm text-on-surface-variant">{CATEGORY_LABELS[memory.category] ?? memory.category}</p>
              </div>
              <button
                aria-label="Gỡ ghi nhớ này"
                className="shrink-0 rounded p-1 text-on-surface-variant hover:bg-surface-variant hover:text-error disabled:opacity-50"
                disabled={removeMutation.isPending}
                onClick={() => removeMutation.mutate(memory.id)}
                title="Gỡ fact sai — AI ngừng dùng ngay"
                type="button"
              >
                <span aria-hidden="true" className="material-symbols-outlined text-[16px]">close</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
