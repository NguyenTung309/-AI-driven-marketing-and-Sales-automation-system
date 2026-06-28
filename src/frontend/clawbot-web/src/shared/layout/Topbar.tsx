import { useEffect, useState } from "react";
import { NavLink, useNavigate } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { logout } from "@/shared/api/auth";
import { getUnreadNotificationCount, type AppNotification } from "@/shared/api/notifications";
import { useAuthStore } from "@/shared/auth/authStore";
import { useNotificationsRealtime } from "@/features/notifications/useNotificationsRealtime";
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
      <span aria-hidden="true" className="material-symbols-outlined text-[20px]">{item.icon}</span>
      <span>{item.label}</span>
    </NavLink>
  );
}

export function Topbar({ title }: TopbarProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const clearAuth = useAuthStore((state) => state.clear);
  const hasAuth = useAuthStore((state) => Boolean(state.accessToken));
  // SPEC-16 P3-5: transient toast for new notifications (auto-dismiss after 5s) so the user sees them on any page.
  const [toast, setToast] = useState<AppNotification | null>(null);
  useEffect(() => {
    if (!toast) return;
    const timer = setTimeout(() => setToast(null), 5_000);
    return () => clearTimeout(timer);
  }, [toast]);
  // SPEC-16 P3-5: mount the notification realtime hook globally so the bell stays live on every page
  // (previously only Dashboard/Notifications connected). It invalidates the unread-count query on push.
  useNotificationsRealtime(hasAuth, setToast);
  const { data } = useQuery({
    queryKey: ["notifications", "unread-count"],
    queryFn: getUnreadNotificationCount,
    retry: false,
    staleTime: 30_000,
  });
  const unreadCount = data?.count ?? 0;

  async function handleLogout() {
    try {
      await logout();
    } catch {
      // If the server is unreachable, still clear the local session.
    } finally {
      clearAuth();
      queryClient.clear();
      navigate("/login", { replace: true });
    }
  }

  return (
    <header className="fixed right-0 top-0 z-10 flex h-[64px] w-full items-center justify-between border-b border-surface-variant bg-surface px-4 text-on-surface md:w-[calc(100%-260px)] md:px-gutter">
      {toast ? (
        <div className="fixed left-1/2 top-[72px] z-50 -translate-x-1/2 rounded-lg border border-outline bg-surface-container-lowest px-4 py-2 shadow-2xl">
          <div className="flex items-center gap-2">
            <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-primary">notifications</span>
            <div className="min-w-0">
              <p className="text-label-sm font-semibold text-on-surface">{toast.title}</p>
              {toast.body ? <p className="truncate text-label-sm text-on-surface-variant">{toast.body}</p> : null}
            </div>
          </div>
        </div>
      ) : null}
      <div className="flex min-w-0 items-center gap-3">
        <details className="relative md:hidden">
          <summary className="flex size-10 cursor-pointer list-none items-center justify-center rounded border border-outline bg-surface-container-lowest text-on-surface">
            <span aria-hidden="true" className="material-symbols-outlined">menu</span>
          </summary>
          <div className="absolute left-0 top-12 w-72 rounded-lg border border-outline bg-surface-container-lowest p-2 shadow-2xl">
            <div className="px-3 py-2">
              <p className="text-label-caps text-on-surface-variant">Học Bá AI</p>
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
          <span aria-hidden="true" className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-[20px] text-on-surface-variant/50">
            search
          </span>
          <input
            aria-label="Tìm kiếm"
            className="w-56 rounded border border-surface-variant bg-surface-container-lowest py-2 pl-10 pr-4 text-body-md focus:outline-none focus:ring-2 focus:ring-primary/30 lg:w-72"
            placeholder="Tìm kiếm..."
          />
        </div>
      </div>

      <div className="flex min-w-0 items-center gap-3 md:gap-4">
        {title ? <span className="truncate text-headline-sm font-semibold">{title}</span> : null}
        <NavLink
          aria-label="Thông báo"
          className="relative text-on-surface-variant hover:text-on-surface"
          to="/notifications"
        >
          <span aria-hidden="true" className="material-symbols-outlined text-[22px]">notifications</span>
          {unreadCount > 0 ? (
            <span className="absolute -right-2 -top-2 rounded-full border-2 border-surface bg-primary px-1.5 text-[10px] font-bold leading-4 text-on-primary">
              {unreadCount > 9 ? "9+" : unreadCount}
            </span>
          ) : null}
        </NavLink>

        <details className="relative">
          <summary
            aria-label="Tài khoản"
            className="flex size-9 cursor-pointer list-none items-center justify-center rounded-full bg-primary font-bold text-on-primary outline-none transition-shadow hover:ring-2 hover:ring-primary/30 focus-visible:ring-2 focus-visible:ring-primary/40"
          >
            HB
          </summary>
          <div className="absolute right-0 top-11 w-48 rounded-lg border border-outline bg-surface-container-lowest p-2 shadow-2xl">
            <NavLink
              className="flex items-center gap-2 rounded px-3 py-2 text-body-md text-on-surface hover:bg-surface-container-low"
              to="/profile"
            >
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">account_circle</span>
              <span>Hồ sơ</span>
            </NavLink>
            <button
              className="flex w-full items-center gap-2 rounded px-3 py-2 text-left text-body-md text-error hover:bg-red-50"
              onClick={handleLogout}
              type="button"
            >
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">logout</span>
              <span>Đăng xuất</span>
            </button>
          </div>
        </details>
      </div>
    </header>
  );
}
