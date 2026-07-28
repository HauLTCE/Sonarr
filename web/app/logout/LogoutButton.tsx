"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "../../lib/client";

/**
 * The button that actually ends the session.
 *
 * A POST rather than the link that led here: the API requires the CSRF header, and a logout that a
 * GET could trigger is a logout any embedded image on any page could trigger.
 */
export function LogoutButton({
  labels,
}: {
  labels: { confirm: string; cancel: string; working: string; failed: string };
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  async function logout() {
    setBusy(true);
    setFailed(false);

    const result = await write("/api/auth/logout", "POST");

    if (result.ok) {
      // replace, not push — Back into a logged-out panel would only bounce to /login anyway.
      router.replace("/login");
      router.refresh();

      return;
    }

    setBusy(false);
    setFailed(true);
  }

  return (
    <>
      {failed ? (
        <p className="notice notice-danger" role="alert">
          {labels.failed}
        </p>
      ) : null}

      <div className="actions">
        <button className="btn btn-accent" type="button" onClick={logout} disabled={busy}>
          {busy ? labels.working : labels.confirm}
        </button>
        <button className="btn" type="button" onClick={() => router.back()} disabled={busy}>
          {labels.cancel}
        </button>
      </div>
    </>
  );
}
