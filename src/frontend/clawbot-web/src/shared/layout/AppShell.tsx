import type { ReactNode } from "react";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";

export interface AppShellProps {
  readonly children: ReactNode;
  readonly title?: string;
}

// Fixed sidebar + fluid content layout (content offset by 260px sidebar + 64px topbar).
export function AppShell({ children, title }: AppShellProps) {
  return (
    <div className="min-h-screen bg-surface">
      <Sidebar />
      <div className="flex flex-col md:ml-[260px] min-h-screen">
        <Topbar title={title} />
        <main className="pt-[80px] p-stack-lg">{children}</main>
      </div>
    </div>
  );
}
