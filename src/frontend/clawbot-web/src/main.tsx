import { StrictMode, Suspense } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router-dom";
import { router } from "@/app/routes";
import Providers from "@/app/providers";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Providers>
      <Suspense fallback={<div className="min-h-screen bg-surface p-stack-lg text-body-md text-on-surface">Dang tai...</div>}>
        <RouterProvider router={router} />
      </Suspense>
    </Providers>
  </StrictMode>
);
