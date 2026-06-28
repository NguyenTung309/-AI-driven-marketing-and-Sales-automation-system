interface CustomerTimelineProps {
  readonly contactId: string | null;
}

interface TimelineEvent {
  readonly id: string;
  readonly type: string;
  readonly summary: string;
  readonly timestamp: string;
}

function mockTimeline(contactId: string | null): TimelineEvent[] {
  if (!contactId) return [];
  return [
    { id: '1', type: 'inbound', summary: 'Khách gửi tin nhắn', timestamp: new Date(Date.now() - 3600000).toISOString() },
    { id: '2', type: 'outbound', summary: 'Sale đã phản hồi', timestamp: new Date(Date.now() - 1800000).toISOString() },
  ];
}

export default function CustomerTimeline({ contactId }: CustomerTimelineProps) {
  const events = mockTimeline(contactId);

  if (!contactId) {
    return <p className='text-label-sm text-on-surface-variant'>Chưa có dữ liệu khách hàng.</p>;
  }

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
            <p className='text-label-xs text-on-surface-variant'>{new Date(evt.timestamp).toLocaleString('vi-VN')}</p>
          </div>
        </div>
      ))}
    </div>
  );
}
