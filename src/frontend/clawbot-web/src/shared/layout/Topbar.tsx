import { useState, useRef, useCallback } from "react";
import { NavLink, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { getUnreadNotificationCount, listNotifications, type AppNotification } from "@/shared/api/notifications";
import { useAuthStore } from "@/shared/auth/authStore";
import { logout } from "@/shared/api/auth";
import { useClickOutside } from "@/shared/hooks/useClickOutside";
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
  const navigate = useNavigate();
  const clearAuth = useAuthStore((s) => s.clear);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);
  useClickOutside(userMenuRef, () => setUserMenuOpen(false), userMenuOpen);

  const [notifOpen, setNotifOpen] = useState(false);
  const notifRef = useRef<HTMLDivElement>(null);
  useClickOutside(notifRef, () => setNotifOpen(false), notifOpen);

  const notifQuery = useQuery({
    queryKey: ["notifications", "recent"],
    queryFn: () => listNotifications({ pageSize: 5 }),
    enabled: notifOpen,
    staleTime: 30_000,
  });
  const recentNotifs = notifQuery.data?.items ?? [];

  const { data } = useQuery({
    queryKey: ["notifications", "unread-count"],
    queryFn: getUnreadNotificationCount,
    retry: false,
    staleTime: 30_000,
  });
  const unreadCount = data?.count ?? 0;

  async function handleLogout() {
    try { await logout(); } catch { /* best-effort */ }
    clearAuth();
    navigate("/login", { replace: true });
  }

  return (
    <header className="bg-surface text-on-surface fixed top-0 right-0 h-[64px] w-full md:w-[calc(100%-260px)] border-b border-surface-variant flex justify-between items-center px-4 md:px-gutter z-10">
      <div className="flex min-w-0 items-center gap-3">
        <details className="relative md:hidden">
          <summary className="flex size-10 cursor-pointer list-none items-center justify-center rounded border border-outline bg-surface-container-lowest text-on-surface">
            <span className="material-symbols-outlined">menu</span>
          </summary>
          <div className="absolute left-0 top-12 w-72 rounded-lg border border-outline bg-surface-container-lowest p-2 shadow-2xl">
            <div className="px-3 py-2">
              <p className="text-label-caps text-on-surface-variant">H�c Bá Education</p>
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
        <div ref={notifRef} className="relative">
          <button
            type="button"
            onClick={() => setNotifOpen((v) => !v)}
            className="relative text-on-surface-variant hover:text-on-surface transition-colors cursor-pointer"
            aria-label="Thông báo"
          >
            <span className="material-symbols-outlined text-[22px]">notifications</span>
            {unreadCount > 0 ? (
              <span className="absolute -right-2 -top-2 rounded-full border-2 border-surface bg-primary px-1.5 text-[10px] font-bold leading-4 text-on-primary">
                {unreadCount > 9 ? "9+" : unreadCount}
              </span>
            ) : null}
          </button>
          {notifOpen && (
            <div className="absolute right-0 top-full mt-2 w-80 rounded-lg border border-outline bg-surface-container-lowest shadow-xl z-50">
              <div className="px-4 py-3 border-b border-outline-variant flex items-center justify-between">
                <span className="text-label-lg font-bold text-on-surface">Thông báo</span>
                <button
                  type="button"
                  onClick={() => { setNotifOpen(false); navigate("/notifications"); }}
                  className="text-label-sm text-primary hover:underline"
                >
                  Xem tất cả
                </button>
              </div>
              <div className="max-h-72 overflow-y-auto">
                {recentNotifs.length === 0 ? (
                  <p className="px-4 py-6 text-center text-body-md text-on-surface-variant">
                    Chưa có thông báo
                  </p>
                ) : (
                  recentNotifs.map((n: AppNotification) => (
                    <button
                      key={n.id}
                      type="button"
                      onClick={() => { setNotifOpen(false); if (n.link) navigate(n.link); }}
                      className="w-full flex flex-col gap-1 px-4 py-3 text-left hover:bg-surface-container-low transition-colors border-b border-outline-variant/50 last:border-b-0"
                    >
                      <div className="flex items-center gap-2">
                        <span className={`size-2 rounded-full shrink-0 ${n.isRead ? "bg-transparent" : "bg-primary"}`} />
                        <span className="text-body-md font-semibold text-on-surface truncate">{n.title}</span>
                      </div>
                      {n.body && (
                        <p className="text-body-sm text-on-surface-variant line-clamp-2 pl-4">{n.body}</p>
                      )}
                    </button>
                  ))
                )}
              </div>
            </div>
          )}
        </div>
        <div ref={userMenuRef} className="relative">
          <button
            type="button"
            onClick={() => setUserMenuOpen((v) => !v)}
            className="size-9 rounded-full bg-primary text-on-primary flex items-center justify-center font-bold hover:bg-primary-hover transition-colors cursor-pointer"
            aria-label="Menu ngư�i dùng"
          >
            HB
          </button>
          {userMenuOpen && (
            <div className="absolute right-0 top-full mt-2 w-48 rounded-lg border border-outline bg-surface-container-lowest shadow-xl z-50 py-1">
              <button
                type="button"
                onClick={() => { setUserMenuOpen(false); navigate("/profile"); }}
                className="w-full flex items-center gap-3 px-4 py-2.5 text-body-md text-on-surface hover:bg-surface-container-low transition-colors text-left"
              >
                <span className="material-symbols-outlined text-[18px] text-on-surface-variant">person</span>
                Trang cá nhân
              </button>
              <hr className="border-outline-variant mx-3" />
              <button
                type="button"
                onClick={handleLogout}
                className="w-full flex items-center gap-3 px-4 py-2.5 text-body-md text-error hover:bg-error/5 transition-colors text-left"
              >
                <span className="material-symbols-outlined text-[18px]">logout</span>
                Đăng xuất
              </button>
            </div>
          )}
        </div>
      </div>
    </header>
  );
}
