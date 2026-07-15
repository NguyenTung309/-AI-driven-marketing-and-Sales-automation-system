import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';

interface CommandPaletteProps {
  readonly open: boolean;
  readonly onClose: () => void;
}

interface CommandItem {
  readonly id: string;
  readonly label: string;
  readonly description: string;
  readonly action: () => void;
}

export default function CommandPalette({ open, onClose }: CommandPaletteProps) {
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [query, setQuery] = useState('');
  const [wasOpen, setWasOpen] = useState(open);

  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) setQuery('');
  }

  const commands: CommandItem[] = [
    { id: 'channels', label: 'Chon kenh', description: 'Chuyen den man hinh chon kenh', action: () => { navigate('/inbox'); onClose(); } },
    { id: 'dashboard', label: 'Dashboard', description: 'Ve trang chu', action: () => { navigate('/'); onClose(); } },
    { id: 'channels-admin', label: 'Quan ly kenh', description: 'Cau hinh inbox va token', action: () => { navigate('/system/channels'); onClose(); } },
  ];

  const filtered = query
    ? commands.filter((c) => c.label.toLowerCase().includes(query.toLowerCase()) || c.description.toLowerCase().includes(query.toLowerCase()))
    : commands;

  useEffect(() => {
    if (!open) return;
    const timer = window.setTimeout(() => inputRef.current?.focus(), 50);
    return () => window.clearTimeout(timer);
  }, [open]);

  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape' && open) onClose();
    }
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className='fixed inset-0 z-[100] flex items-start justify-center pt-[15vh]' onClick={onClose}>
      <div className='absolute inset-0 bg-black/40' />
      <div
        className='relative w-full max-w-lg rounded-xl border border-outline bg-surface-container-lowest shadow-2xl'
        onClick={(e) => e.stopPropagation()}
      >
        <div className='flex items-center border-b border-outline px-4'>
          <svg width='18' height='18' viewBox='0 0 24 24' fill='none' stroke='currentColor' strokeWidth='2' className='mr-2 text-on-surface-variant'>
            <circle cx='11' cy='11' r='8' />
            <path d='m21 21-4.35-4.35' />
          </svg>
          <input
            ref={inputRef}
            type='text'
            placeholder='Tim kenh hoac chuc nang...'
            className='w-full border-0 bg-transparent py-3 text-body-md outline-none placeholder:text-on-surface-variant'
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          <kbd className='rounded border border-outline px-1.5 py-0.5 text-label-xs text-on-surface-variant'>Esc</kbd>
        </div>
        <div className='max-h-64 overflow-y-auto p-2'>
          {filtered.length === 0 ? (
            <p className='p-2 text-center text-body-md text-on-surface-variant'>Khong tim thay ket qua</p>
          ) : (
            filtered.map((cmd) => (
              <button
                key={cmd.id}
                type='button'
                onClick={cmd.action}
                className='flex w-full items-center rounded-lg px-3 py-2.5 text-left hover:bg-primary/10'
              >
                <div className='flex-1'>
                  <p className='text-body-md font-semibold text-secondary'>{cmd.label}</p>
                  <p className='text-label-sm text-on-surface-variant'>{cmd.description}</p>
                </div>
              </button>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
