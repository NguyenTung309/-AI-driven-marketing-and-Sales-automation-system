export interface TopbarProps {
  readonly title?: string;
}

// Fixed 64px top bar: search left, actions + avatar right.
export function Topbar({ title }: TopbarProps) {
  return (
    <header className="bg-surface text-on-surface fixed top-0 right-0 h-[64px] w-full md:w-[calc(100%-260px)] border-b border-surface-variant flex justify-between items-center px-gutter z-10">
      <div className="relative">
        <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant/50 text-[20px]">
          search
        </span>
        <input
          className="bg-surface-container-lowest border border-surface-variant rounded pl-10 pr-4 py-2 w-72 text-body-md focus:outline-none focus:ring-2 focus:ring-primary/30"
          placeholder="Tìm kiếm..."
          aria-label="Tìm kiếm"
        />
      </div>
      <div className="flex items-center gap-4">
        {title ? <span className="text-headline-sm font-semibold">{title}</span> : null}
        <button className="text-on-surface-variant hover:text-on-surface" aria-label="Thông báo" type="button">
          <span className="material-symbols-outlined text-[22px]">notifications</span>
        </button>
        <div className="size-9 rounded-full bg-primary text-on-primary flex items-center justify-center font-bold">
          HB
        </div>
      </div>
    </header>
  );
}
