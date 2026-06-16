import { NavLink } from "react-router-dom";
import { NAV_ITEMS, NAV_SYSTEM, type NavItem } from "./nav";

export interface SidebarProps {
  readonly className?: string;
}

function itemClass(isActive: boolean): string {
  return [
    "px-6 py-3 flex items-center gap-3 transition-colors",
    isActive
      ? "bg-white/15 border-l-4 border-white text-white font-bold"
      : "text-white/80 hover:text-white hover:bg-white/10",
  ].join(" ");
}

// Fixed 260px Học Bá-Red sidebar (Level: structural anchor, no elevation).
export function Sidebar({ className = "" }: SidebarProps) {
  return (
    <aside
      className={`bg-primary text-on-primary fixed left-0 top-0 hidden h-full w-[260px] flex-col md:flex z-20 shadow-xl ${className}`}
    >
      <div className="px-gutter py-6 flex flex-col items-start gap-2">
        <h1 className="text-headline-md font-bold text-white">Học Bá Education</h1>
        <span className="text-label-caps text-white/70">AI Automation Hub</span>
      </div>

      <nav className="flex-1 mt-stack-md flex flex-col overflow-y-auto">
        {NAV_ITEMS.map((item: NavItem) => (
          <NavLink key={item.to} to={item.to} end className={({ isActive }) => itemClass(isActive)}>
            <span className="material-symbols-outlined">{item.icon}</span>
            <span className="font-medium">{item.label}</span>
          </NavLink>
        ))}
        <NavLink to={NAV_SYSTEM.to} className={({ isActive }) => `mt-auto ${itemClass(isActive)}`}>
          <span className="material-symbols-outlined">{NAV_SYSTEM.icon}</span>
          <span>{NAV_SYSTEM.label}</span>
        </NavLink>
      </nav>

      <div className="px-6 py-4 border-t border-white/10">
        <div className="flex items-center gap-3 text-white/80 text-sm">
          <span className="material-symbols-outlined text-success text-[18px]">dns</span>
          <span>Server Status: Active</span>
        </div>
      </div>
    </aside>
  );
}
