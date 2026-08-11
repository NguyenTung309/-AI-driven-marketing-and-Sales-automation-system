import type { ButtonHTMLAttributes } from "react";

type ButtonVariant = "primary" | "outline" | "ghost" | "danger";
type ButtonSize = "sm" | "md";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  readonly variant?: ButtonVariant;
  readonly size?: ButtonSize;
}

const VARIANT: Record<ButtonVariant, string> = {
  primary: "bg-primary text-on-primary hover:bg-primary-hover",
  outline: "border border-outline text-on-surface hover:bg-surface-variant",
  ghost: "text-on-surface-variant hover:bg-surface-variant",
  danger: "bg-error text-on-error hover:bg-error/90 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-error",
};

const SIZE: Record<ButtonSize, string> = {
  sm: "px-3 py-1.5 text-mono-status",
  md: "px-4 py-2 text-body-md",
};

export function Button({ variant = "primary", size = "md", className = "", ...rest }: ButtonProps) {
  return (
    <button
      className={`inline-flex items-center justify-center gap-2 rounded font-medium transition-colors disabled:opacity-50 disabled:pointer-events-none ${VARIANT[variant]} ${SIZE[size]} ${className}`}
      {...rest}
    />
  );
}
