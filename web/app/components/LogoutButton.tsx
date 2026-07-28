"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

/**
 * Client-side because the API answers 204 with no redirect: a plain form POST would leave the
 * browser sitting on a blank response body. The cookie is cleared by the API's Set-Cookie either
 * way, so this only has to send the request and then move.
 */
export function LogoutButton({ label }: { label: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  return (
    <button
      type="button"
      className="quiet"
      disabled={busy}
      onClick={async () => {
        setBusy(true);
        try {
          await fetch("/api/auth/logout", { method: "POST" });
        } catch {
          // A failed logout still means "get me out of here": the cookie may survive, but leaving
          // the visitor on an authenticated page is the worse outcome.
        }
        router.push("/login");
        router.refresh();
      }}
    >
      {label}
    </button>
  );
}
