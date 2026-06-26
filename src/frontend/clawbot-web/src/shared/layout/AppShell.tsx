import type { ReactNode } from "react";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";

export interface AppShellProps {
  readonly children: ReactNode;
  readonly title?: string;
  readonly noPadding?: boolean;
}

// Fixed sidebar + fluid content layout (content offset by 260px sidebar + 64px topbar).
export function AppShell({ children, title, noPadding = false }: AppShellProps) {
  if (noPadding) {
    return (
      <div className="h-screen overflow-hidden bg-surface flex">
        <Sidebar />
        <div className="flex flex-col flex-1 md:ml-[260px] h-screen overflow-hidden">
          <Topbar title={title} />
          <main className="pt-[64px] flex-1 flex flex-col min-h-0 overflow-hidden">
            {children}
          </main>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-surface">
      <Sidebar />
      <div className="flex flex-col md:ml-[260px] min-h-screen">
        <Topbar title={title} />
        <main className="pt-[64px] p-stack-lg">{children}</main>
      </div>
    </div>
  );
}
