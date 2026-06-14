import { createContext, useContext } from "react";

export interface AuthState {
  readonly token: string | null;
  readonly setToken: (token: string | null) => void;
}

export const AuthCtx = createContext<AuthState>({ token: null, setToken: () => {} });

export function useAuth() {
  return useContext(AuthCtx);
}
