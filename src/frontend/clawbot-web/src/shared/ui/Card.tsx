import type { ReactNode } from "react";

export interface CardProps {
  readonly children: ReactNode;
  readonly className?: string;
}

// Level 1 surface: white card, 1px outline, soft 8px radius.
export function Card({ children, className = "" }: CardProps) {
  return (
    <div className={`bg-surface-container-lowest border border-outline rounded-lg p-card-padding ${className}`}>
      {children}
    </div>
  );
}
