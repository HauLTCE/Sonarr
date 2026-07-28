"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "@/lib/client";
import type { Locale } from "@/lib/strings";
import { translator } from "@/lib/strings";

/**
 * "Someone else has my cookie" — revokes every session, including this one, so the visitor lands
 * back on /login. No typed confirmation: it destroys nothing but access, and the worst case is
 * logging in again.
 */
export function LogoutEverywhere({ locale }: { locale: Locale }) {
  const t = translator(locale);
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  return (
    <>
      <div className="row">
        <button
          type="button"
          className="quiet"
          disabled={busy}
          onClick={async () => {
            setBusy(true);
            setFailed(false);

            const result = await write<{ revoked: number }>("/api/auth/logout-all", "POST");

            if (!result.ok) {
              setBusy(false);
              setFailed(true);
              return;
            }

            router.push("/login");
            router.refresh();
          }}
        >
          {t("privacy.logOutEverywhere")}
        </button>
      </div>

      {failed && (
        <p className="notice bad" role="alert">
          {t("common.error")}
        </p>
      )}
    </>
  );
}
