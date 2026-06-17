# Logout, Profile, and Notification Dropdown — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add logout button, profile link, and notification dropdown with click-outside-to-close in the Topbar.

**Architecture:** All changes are frontend-only. The logout calls existing `POST /auth/logout` then clears zustand auth store. The Topbar gets 2 dropdown menus: user menu (avatar ? Profile + Logout) and notification menu (bell icon ? recent notifications). Both use `useRef` + `useEffect` for click-outside detection. The notification dropdown fetches data via existing `listNotifications()` API.

**Tech Stack:** React, TypeScript, Zustand, TanStack Query, Tailwind CSS, Material Symbols

---

### Task 1: Add logout() to auth API

**Files:**
- Modify: `src/frontend/clawbot-web/src/shared/api/auth.ts:114-116`

- [ ] **Step 1: Add logout function**

Append before the last closing brace in `auth.ts`:

```ts
// POST /auth/logout ? 204 (revokes refresh cookie server-side).
export async function logout(): Promise<void> {
  await apiClient.post("/auth/logout");
}
```

- [ ] **Step 2: Quick build check**

Run: `cd src/frontend/clawbot-web && npx tsc --noEmit --pretty`
Expected: No errors mentioning `auth.ts`

- [ ] **Step 3: Commit**

```bash
git add src/frontend/clawbot-web/src/shared/api/auth.ts
git commit -m "feat(api): add logout() function"
```

---

### Task 2: Add useClickOutside hook

**Files:**
- Create: `src/frontend/clawbot-web/src/shared/hooks/useClickOutside.ts`

- [ ] **Step 1: Create the hook**

Create `src/frontend/clawbot-web/src/shared/hooks/useClickOutside.ts`:

```ts
import { useEffect, type RefObject } from "react";

/**
 * Calls `handler` when a click/touch happens outside of `ref` element.
 * Does nothing when `enabled` is false (useful for closed dropdowns).
 */
export function useClickOutside(
  ref: RefObject<HTMLElement | null>,
  handler: () => void,
  enabled: boolean = true,
): void {
  useEffect(() => {
    if (!enabled) return;
    function onPointerDown(e: PointerEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        handler();
      }
    }
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [ref, handler, enabled]);
}
```

- [ ] **Step 2: Build check**

Run: `cd src/frontend/clawbot-web && npx tsc --noEmit --pretty`
Expected: No errors

- [ ] **Step 3: Commit**

```bash
git add src/frontend/clawbot-web/src/shared/hooks/useClickOutside.ts
git commit -m "feat(hooks): add useClickOutside hook"
```

---

### Task 3: Refactor Topbar — user dropdown (avatar ? Profile + Logout)

**Files:**
- Modify: `src/frontend/clawbot-web/src/shared/layout/Topbar.tsx`

- [ ] **Step 1: Add imports to Topbar.tsx**

Add at top of file:

```tsx
import { useState, useRef, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { useAuthStore } from "@/shared/auth/authStore";
import { logout } from "@/shared/api/auth";
import { useClickOutside } from "@/shared/hooks/useClickOutside";
```

- [ ] **Step 2: Add user dropdown state + ref inside Topbar component**

After the `const { data }` line inside `Topbar()`:

```tsx
const navigate = useNavigate();
const clearAuth = useAuthStore((s) => s.clear);
const [userMenuOpen, setUserMenuOpen] = useState(false);
const userMenuRef = useRef<HTMLDivElement>(null);
useClickOutside(userMenuRef, () => setUserMenuOpen(false), userMenuOpen);

async function handleLogout() {
  try { await logout(); } catch { /* server-side cookie revoke is best-effort */ }
  clearAuth();
  navigate("/login", { replace: true });
}
```

- [ ] **Step 3: Replace the static avatar badge with a dropdown**

Find the block:
```tsx
<div className="size-9 rounded-full bg-primary text-on-primary flex items-center justify-center font-bold">
  HB
</div>
```

Replace with:
```tsx
<div ref={userMenuRef} className="relative">
  <button
    type="button"
    onClick={() => setUserMenuOpen((v) => !v)}
    className="size-9 rounded-full bg-primary text-on-primary flex items-center justify-center font-bold hover:bg-primary-hover transition-colors cursor-pointer"
    aria-label="Menu ngu?i dùng"
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
        Ðang xu?t
      </button>
    </div>
  )}
</div>
```

- [ ] **Step 4: Build check**

Run: `cd src/frontend/clawbot-web && npx tsc --noEmit --pretty`
Expected: No errors

- [ ] **Step 5: Start frontend dev server and verify**

```bash
cd src/frontend/clawbot-web && npm run dev
```

Check:
- Click avatar ? dropdown shows Profile + Ðang xu?t
- Profile link navigates to `/profile`
- Logout calls API ? clears auth ? redirects to `/login`
- Click outside dropdown ? it closes

- [ ] **Step 6: Commit**

```bash
git add src/frontend/clawbot-web/src/shared/layout/Topbar.tsx
git commit -m "feat(ui): add user dropdown with profile and logout"
```

---

### Task 4: Add notification dropdown to Topbar

**Files:**
- Modify: `src/frontend/clawbot-web/src/shared/layout/Topbar.tsx`

- [ ] **Step 1: Add imports for notifications**

Add to existing imports:

```tsx
import { useQuery } from "@tanstack/react-query";
import { listNotifications, type AppNotification, getUnreadNotificationCount } from "@/shared/api/notifications";
```

(move `getUnreadNotificationCount` import from the existing standalone import — currently `Topbar.tsx` line 4 imports `getUnreadNotificationCount` directly, the `useQuery` is already imported)

- [ ] **Step 2: Add notification dropdown state + ref + query**

After `const userMenuRef` block:

```tsx
const [notifOpen, setNotifOpen] = useState(false);
const notifRef = useRef<HTMLDivElement>(null);
useClickOutside(notifRef, () => setNotifOpen(false), notifOpen);

const { data: notifData } = useQuery({
  queryKey: ["notifications", "recent"],
  queryFn: () => listNotifications({ pageSize: 5 }),
  enabled: notifOpen,
  staleTime: 30_000,
});
const recentNotifs = notifData?.items ?? [];
```

- [ ] **Step 3: Replace the bell icon + badge with a dropdown**

Find the current bell notification block:
```tsx
<NavLink
  className="relative text-on-surface-variant hover:text-on-surface"
  to="/notifications"
  aria-label="Thông báo"
>
  <span className="material-symbols-outlined text-[22px]">notifications</span>
  {unreadCount > 0 ? (...)}
</NavLink>
```

Replace with:
```tsx
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
          onClick={() => setNotifOpen(false); void navigate("/notifications");}
          className="text-label-sm text-primary hover:underline"
        >
          Xem t?t c?
        </button>
      </div>

      <div className="max-h-72 overflow-y-auto">
        {recentNotifs.length === 0 ? (
          <p className="px-4 py-6 text-center text-body-md text-on-surface-variant">
            Chua có thông báo
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
                <span
                  className={`size-2 rounded-full shrink-0 ${n.isRead ? "bg-transparent" : "bg-primary"}`}
                />
                <span className="text-body-md font-semibold text-on-surface truncate">
                  {n.title}
                </span>
              </div>
              {n.body && (
                <p className="text-body-sm text-on-surface-variant line-clamp-2 pl-4">
                  {n.body}
                </p>
              )}
            </button>
          ))
        )}
      </div>
    </div>
  )}
</div>
```

- [ ] **Step 4: Build check**

Run: `cd src/frontend/clawbot-web && npx tsc --noEmit --pretty`
Expected: No errors

- [ ] **Step 5: Verify**

Check in browser:
- Bell icon ? click ? dropdown shows 5 recent notifications
- Unread dot shows beside unread items
- "Xem t?t c?" navigates to `/notifications`
- Empty state shows "Chua có thông báo"
- Click outside ? dropdown closes
- Bell badge count still shows correctly

- [ ] **Step 6: Commit**

```bash
git add src/frontend/clawbot-web/src/shared/layout/Topbar.tsx
git commit -m "feat(ui): add notification dropdown to Topbar"
```
