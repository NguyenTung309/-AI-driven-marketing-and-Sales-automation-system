import { useState, type ReactNode } from "react";
import { AuthCtx } from "./authState";

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTokenState] = useState<string | null>(() =>
    localStorage.getItem("clawbot.access_token")
  );

  const setToken = (nextToken: string | null) => {
    if (nextToken) localStorage.setItem("clawbot.access_token", nextToken);
    else localStorage.removeItem("clawbot.access_token");
    setTokenState(nextToken);
  };

  return <AuthCtx.Provider value={{ token, setToken }}>{children}</AuthCtx.Provider>;
}
