import { useState } from 'react';
import type { ConversationListItem } from '@/shared/api/inbox';
import TabConversation from './TabConversation';

const MAX_VISIBLE_TABS = 7;

interface ConversationTabsProps {
  readonly conversations: readonly ConversationListItem[];
  readonly activeId: string | null;
  readonly onSelect: (id: string) => void;
  readonly onClose: (id: string) => void;
}

export default function ConversationTabs({ conversations, activeId, onSelect, onClose }: ConversationTabsProps) {
  const [overflowOpen, setOverflowOpen] = useState(false);

  const visible = conversations.slice(0, MAX_VISIBLE_TABS);
  const overflow = conversations.slice(MAX_VISIBLE_TABS);

  return (
    <div className='flex items-center border-b border-outline bg-surface-container-lowest px-2'>
      <div className='flex items-center gap-0.5 overflow-x-auto'>
        {visible.map((conv) => (
          <TabConversation
            key={conv.id}
            conversation={conv}
            isActive={activeId === conv.id}
            onSelect={() => onSelect(conv.id)}
            onClose={() => onClose(conv.id)}
          />
        ))}
      </div>
      {overflow.length > 0 && (
        <div className='relative'>
          <button
            type='button'
            onClick={() => setOverflowOpen(!overflowOpen)}
            className='rounded px-2 py-1 text-label-sm text-on-surface-variant hover:bg-surface-container-high'
          >
            +{overflow.length}
          </button>
          {overflowOpen && (
            <div className='absolute right-0 top-full z-50 mt-1 w-56 rounded-lg border border-outline bg-surface-container-lowest shadow-xl'>
              {overflow.map((conv) => (
                <button
                  key={conv.id}
                  type='button'
                  onClick={() => { onSelect(conv.id); setOverflowOpen(false); }}
                  className='flex w-full items-center gap-2 px-3 py-2 text-left text-label-sm hover:bg-surface-container-high'
                >
                  <span className='truncate'>{conv.contactDisplayName || conv.externalThreadId}</span>
                </button>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
