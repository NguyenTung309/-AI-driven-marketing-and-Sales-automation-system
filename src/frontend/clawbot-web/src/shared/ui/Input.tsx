import type { InputHTMLAttributes } from "react";

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  readonly icon?: string; // optional leading Material Symbols icon
}

export function Input({ icon, className = "", ...rest }: InputProps) {
  return (
    <div className="relative">
      {icon ? (
        <span aria-hidden="true" className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant/50 text-[20px]">
          {icon}
        </span>
      ) : null}
      <input
        className={`bg-surface-container-lowest border border-surface-variant rounded ${icon ? "pl-10" : "pl-3"} pr-3 py-2 text-body-md w-full focus:outline-none focus:ring-2 focus:ring-primary/30 ${className}`}
        {...rest}
      />
    </div>
  );
}
