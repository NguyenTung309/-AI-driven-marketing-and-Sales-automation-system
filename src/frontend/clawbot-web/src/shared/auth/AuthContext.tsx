import { createContext, useContext, useState, type ReactNode } from "react";

interface AuthState {
  token: string | null;
  setToken: (t: string | null) => void;
}

const AuthCtx = createContext<AuthState>({ token: null, setToken: () => {} });

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTokenState] = useState<string | null>(() =>
    localStorage.getItem("clawbot.access_token")
  );

  const setToken = (t: string | null) => {
    if (t) localStorage.setItem("clawbot.access_token", t);
    else localStorage.removeItem("clawbot.access_token");
    setTokenState(t);
  };

  return <AuthCtx.Provider value={{ token, setToken }}>{children}</AuthCtx.Provider>;
}

export const useAuth = () => useContext(AuthCtx);
