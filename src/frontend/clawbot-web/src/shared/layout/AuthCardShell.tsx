import type { ReactNode } from "react";

export interface AuthCardShellProps {
  readonly children: ReactNode;
}

// Centered white card on canvas with faint brand watermark + footer (forgot-password flow).
export function AuthCardShell({ children }: AuthCardShellProps) {
  return (
    <div className="min-h-screen flex flex-col justify-center items-center overflow-hidden bg-surface relative">
      <div className="fixed top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 z-0 pointer-events-none opacity-[0.03] w-4/5 max-w-[800px] flex flex-col items-center select-none">
        <span className="material-symbols-outlined text-[300px] [font-variation-settings:'wght'_700]">school</span>
        <h1 className="text-9xl font-bold uppercase tracking-widest mt-8 text-center">Học Bá Education</h1>
      </div>

      <main className="relative z-10 w-full px-4 flex justify-center">
        <div className="bg-white w-full max-w-[460px] rounded-[12px] shadow-[0px_1px_3px_rgba(0,0,0,0.05),0px_1px_2px_rgba(0,0,0,0.03)] p-8 md:p-12 flex flex-col items-stretch">
          {children}
        </div>
      </main>

      <footer className="absolute bottom-0 left-0 right-0 w-full flex flex-col md:flex-row justify-between items-center gap-4 px-8 py-4 z-10 text-on-surface-variant">
        <span className="text-label-lg font-bold text-on-surface">Học Bá Education</span>
        <nav className="flex gap-6">
          <a href="#" className="text-label-sm hover:text-primary hover:underline transition-colors">Privacy Policy</a>
          <a href="#" className="text-label-sm hover:text-primary hover:underline transition-colors">Terms of Service</a>
          <a href="#" className="text-label-sm hover:text-primary hover:underline transition-colors">Support</a>
        </nav>
        <span className="text-label-sm">© 2024 Học Bá Education. All rights reserved.</span>
      </footer>
    </div>
  );
}
