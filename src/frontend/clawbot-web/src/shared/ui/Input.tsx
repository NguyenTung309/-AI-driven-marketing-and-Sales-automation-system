import { useId, type InputHTMLAttributes } from "react";

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  readonly icon?: string; // optional leading Material Symbols icon
  readonly error?: string;
}

export function Input({ icon, error, className = "", id, "aria-describedby": describedBy, ...rest }: InputProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const errorId = `${inputId}-error`;

  return (
    <div className="relative">
      {icon ? (
        <span aria-hidden="true" className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant/50 text-[20px]">
          {icon}
        </span>
      ) : null}
      <input
        id={inputId}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? [errorId, describedBy].filter(Boolean).join(" ") : describedBy}
        className={`bg-surface-container-lowest border rounded ${icon ? "pl-10" : "pl-3"} pr-3 py-2 text-body-md w-full focus:outline-none focus:ring-2 ${error ? "border-error focus:ring-error/30" : "border-surface-variant focus:ring-primary/30"} ${className}`}
        {...rest}
      />
      {error ? (
        <p id={errorId} role="alert" className="mt-1 text-body-sm text-error">
          {error}
        </p>
      ) : null}
    </div>
  );
}
