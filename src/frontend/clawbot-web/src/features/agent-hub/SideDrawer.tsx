import type { ReactNode } from 'react';
import CustomerTimeline from './CustomerTimeline';

interface SideDrawerProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly contactDisplayName: string | null;
  readonly contactId: string | null;
  readonly platform: string;
  readonly children?: ReactNode;
}

export default function SideDrawer({ open, onClose, contactDisplayName, contactId, platform, children }: SideDrawerProps) {
  return (
    <>
      {open && <div className='fixed inset-0 z-30 bg-black/20 md:hidden' onClick={onClose} />}
      <aside
        className={
          'fixed right-0 top-16 z-40 h-[calc(100vh-4rem)] w-80 border-l border-outline bg-surface-container-lowest shadow-xl transition-transform md:static md:z-auto md:shadow-none ' +
          (open ? 'translate-x-0' : 'translate-x-full md:hidden')
        }
      >
        <div className='flex items-center justify-between border-b border-outline px-4 py-3'>
          <h3 className='text-label-md font-bold text-secondary'>Khách hàng</h3>
          <button type='button' onClick={onClose} className='text-on-surface-variant hover:text-secondary md:hidden'>
            <svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' strokeWidth='2'>
              <path d='M18 6 6 18M6 6l12 12' />
            </svg>
          </button>
        </div>

        <div className='overflow-y-auto h-full pb-8'>
          {/* Thông tin cơ bản */}
          <div className='border-b border-outline px-4 py-3'>
            <p className='text-body-md font-semibold text-secondary'>{contactDisplayName ?? 'Chưa có tên'}</p>
            <p className='text-label-sm text-on-surface-variant capitalize'>{platform}</p>
            {contactId && <p className='text-label-xs text-on-surface-variant font-mono mt-1'>ID: {contactId}</p>}
          </div>

          {/* Timeline */}
          <div className='px-4 py-3'>
            <h4 className='mb-2 text-label-sm font-bold uppercase text-secondary'>Hoạt động</h4>
            <CustomerTimeline contactId={contactId} />
          </div>

          {/* Custom content (notes, quick actions etc.) */}
          {children && <div className='px-4 py-3'>{children}</div>}
        </div>
      </aside>
    </>
  );
}
