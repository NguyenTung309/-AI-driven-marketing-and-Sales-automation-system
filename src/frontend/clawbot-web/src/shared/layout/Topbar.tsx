import { NavLink } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { getUnreadNotificationCount } from "@/shared/api/notifications";
import { NAV_ITEMS, NAV_SYSTEM, type NavItem } from "./nav";

export interface TopbarProps {
  readonly title?: string;
}

function mobileItemClass(isActive: boolean): string {
  return [
    "flex items-center gap-3 rounded px-3 py-2 text-body-md transition-colors",
    isActive ? "bg-primary/10 text-primary font-bold" : "text-on-surface hover:bg-surface-container-low",
  ].join(" ");
}

function MobileNavItem({ item }: { readonly item: NavItem }) {
  return (
    <NavLink to={item.to} className={({ isActive }) => mobileItemClass(isActive)}>
      <span className="material-symbols-outlined text-[20px]">{item.icon}</span>
      <span>{item.label}</span>
    </NavLink>
  );
}

// Fixed 64px top bar: search left, actions + avatar right.
export function Topbar({ title }: TopbarProps) {
  const { data } = useQuery({
    queryKey: ["notifications", "unread-count"],
    queryFn: getUnreadNotificationCount,
    retry: false,
    staleTime: 30_000,
  });
  const unreadCount = data?.count ?? 0;

  return (
    <header className="bg-surface text-on-surface fixed top-0 right-0 h-[64px] w-full md:w-[calc(100%-260px)] border-b border-surface-variant flex justify-between items-center px-4 md:px-gutter z-10">
      <div className="flex min-w-0 items-center gap-3">
        <details className="relative md:hidden">
          <summary className="flex size-10 cursor-pointer list-none items-center justify-center rounded border border-outline bg-surface-container-lowest text-on-surface">
            <span className="material-symbols-outlined">menu</span>
          </summary>
          <div className="absolute left-0 top-12 w-72 rounded-lg border border-outline bg-surface-container-lowest p-2 shadow-2xl">
            <div className="px-3 py-2">
              <p className="text-label-caps text-on-surface-variant">Học Bá Education</p>
            </div>
            {NAV_ITEMS.map((item) => (
              <MobileNavItem key={item.to} item={item} />
            ))}
            <div className="mt-2 border-t border-outline pt-2">
              <MobileNavItem item={NAV_SYSTEM} />
            </div>
          </div>
        </details>
        <div className="relative hidden sm:block">
        <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant/50 text-[20px]">
          search
        </span>
        <input
          className="bg-surface-container-lowest border border-surface-variant rounded pl-10 pr-4 py-2 w-56 lg:w-72 text-body-md focus:outline-none focus:ring-2 focus:ring-primary/30"
          placeholder="Tìm kiếm..."
          aria-label="Tìm kiếm"
        />
        </div>
      </div>
      <div className="flex min-w-0 items-center gap-3 md:gap-4">
        {title ? <span className="truncate text-headline-sm font-semibold">{title}</span> : null}
        <NavLink
          className="relative text-on-surface-variant hover:text-on-surface"
          to="/notifications"
          aria-label="Thông báo"
        >
          <span className="material-symbols-outlined text-[22px]">notifications</span>
          {unreadCount > 0 ? (
            <span className="absolute -right-2 -top-2 rounded-full border-2 border-surface bg-primary px-1.5 text-[10px] font-bold leading-4 text-on-primary">
              {unreadCount > 9 ? "9+" : unreadCount}
            </span>
          ) : null}
        </NavLink>
        <div className="size-9 rounded-full bg-primary text-on-primary flex items-center justify-center font-bold">
          HB
        </div>
      </div>
    </header>
  );
}
