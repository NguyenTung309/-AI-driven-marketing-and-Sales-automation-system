import type { ReactNode } from "react";

interface FieldProps {
  readonly label: string;
  readonly children: ReactNode;
}

export function Field({ label, children }: FieldProps) {
  return (
    <label className="block">
      <span className="mb-1 block text-label-sm font-semibold text-secondary">{label}</span>
      {children}
    </label>
  );
}
