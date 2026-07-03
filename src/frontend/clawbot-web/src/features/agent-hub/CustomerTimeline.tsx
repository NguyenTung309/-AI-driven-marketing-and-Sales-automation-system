import { useQuery } from '@tanstack/react-query';
import { listConversations, type ConversationListItem } from '@/shared/api/inbox';

interface CustomerTimelineProps {
  readonly contactId: string | null;
}

interface TimelineEvent {
  readonly id: string;
  readonly summary: string;
  readonly detail: string | null;
  readonly timestamp: string | null;
}

const STATUS_LABELS: Record<string, string> = {
  open: 'Đang mở',
  resolved: 'Đã xử lý',
  escalated: 'Đã chuyển cấp',
};

function toEvents(items: readonly ConversationListItem[], contactId: string): TimelineEvent[] {
  return items
    .filter((c) => c.contactId === contactId)
    .map((c) => ({
      id: c.id,
      summary: `${c.platform} · ${STATUS_LABELS[c.status] ?? c.status}`,
      detail: c.lastMessagePreview,
      timestamp: c.lastMessageAt,
    }))
    .sort((a, b) => (b.timestamp ?? '').localeCompare(a.timestamp ?? ''))
    .slice(0, 8);
}

export default function CustomerTimeline({ contactId }: CustomerTimelineProps) {
  const conversationsQuery = useQuery({
    queryKey: ['agent-hub', 'contact-timeline', contactId],
    queryFn: () => listConversations({ pageSize: 50 }),
    enabled: Boolean(contactId),
    staleTime: 30_000,
  });

  if (!contactId) {
    return <p className='text-label-sm text-on-surface-variant'>Chưa có dữ liệu khách hàng.</p>;
  }

  if (conversationsQuery.isLoading) {
    return <p className='text-label-sm text-on-surface-variant'>Đang tải hoạt động...</p>;
  }

  const events = toEvents(conversationsQuery.data?.items ?? [], contactId);
  if (events.length === 0) {
    return <p className='text-label-sm text-on-surface-variant'>Chưa có hoạt động.</p>;
  }

  return (
    <div className='relative pl-4'>
      {/* Vertical line */}
      <div className='absolute left-1.5 top-2 h-[calc(100%-8px)] w-px bg-outline' />
      {events.map((evt) => (
        <div key={evt.id} className='relative mb-3 last:mb-0'>
          <div className='absolute -left-3.5 top-1.5 size-2.5 rounded-full border-2 border-primary bg-surface-container-lowest' />
          <div className='ml-2'>
            <p className='text-label-sm text-secondary'>{evt.summary}</p>
            {evt.detail ? <p className='truncate text-label-xs text-on-surface'>{evt.detail}</p> : null}
            {evt.timestamp ? (
              <p className='text-label-xs text-on-surface-variant'>{new Date(evt.timestamp).toLocaleString('vi-VN')}</p>
            ) : null}
          </div>
        </div>
      ))}
    </div>
  );
}
