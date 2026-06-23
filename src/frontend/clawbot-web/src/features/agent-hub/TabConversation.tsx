import type { ConversationListItem } from '@/shared/api/inbox';

interface TabConversationProps {
  readonly conversation: ConversationListItem;
  readonly isActive: boolean;
  readonly onSelect: () => void;
  readonly onClose: () => void;
}

function platformIcon(platform: string): string {
  switch (platform) {
    case 'facebook': return 'fb';
    case 'zalo': return 'ZL';
    case 'web': return 'WWW';
    default: return 'CH';
  }
}

export default function TabConversation({ conversation, isActive, onSelect, onClose }: TabConversationProps) {
  return (
    <button
      type='button'
      onClick={onSelect}
      className={
        'group flex items-center gap-1.5 rounded-t-lg px-3 py-1.5 text-label-sm transition-colors ' +
        (isActive
          ? 'bg-surface-container-lowest text-secondary border border-b-0 border-outline'
          : 'bg-surface-container-high text-on-surface-variant hover:bg-surface-container-low hover:text-secondary')
      }
    >
      <span className='flex size-4 items-center justify-center rounded-full bg-surface-variant text-label-xs font-bold'>
        {platformIcon(conversation.platform)}
      </span>
      <span className='max-w-[120px] truncate'>{conversation.contactDisplayName || conversation.externalThreadId}</span>
      {conversation.unreadCount > 0 && (
        <span className='flex size-4 items-center justify-center rounded-full bg-error text-label-xs text-on-error'>
          {conversation.unreadCount > 9 ? '9+' : conversation.unreadCount}
        </span>
      )}
      <button
        type='button'
        onClick={(e) => { e.stopPropagation(); onClose(); }}
        className='ml-auto text-on-surface-variant opacity-0 group-hover:opacity-100 hover:text-secondary'
        aria-label='Dong tab'
      >
        <svg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='currentColor' strokeWidth='2'>
          <path d='M18 6 6 18M6 6l12 12' />
        </svg>
      </button>
    </button>
  );
}
