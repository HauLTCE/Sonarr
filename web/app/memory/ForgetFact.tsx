"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";

import { write } from "../../lib/client";

/**
 * "Forget this" for one fact.
 *
 * The hint sits under the button and says what will happen before it is pressed, which is why there
 * is no confirmation dialog here: one fact is small and she can learn it again. The two irreversible
 * buttons live on Privacy and those do ask.
 */
export function ForgetFact({
  predicate,
  guildId,
  labels,
}: {
  predicate: string;
  guildId: string;
  labels: { forget: string; forgetting: string; hint: string; failed: string };
}) {
  const router = useRouter();
  const [pending, start] = useTransition();
  const [error, setError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  async function forget() {
    setSending(true);
    setError(null);

    const result = await write(
      `/api/me/facts/${encodeURIComponent(predicate)}?guildId=${guildId}`,
      "DELETE",
    );

    setSending(false);

    if (!result.ok) {
      setError(result.error ?? labels.failed);
      return;
    }

    // The list is server-rendered, so a refresh is what removes the row — no local copy to keep in
    // step with the database.
    start(() => router.refresh());
  }

  const busy = sending || pending;

  return (
    <div className="row-action">
      <button type="button" className="btn btn-danger" onClick={forget} disabled={busy}>
        {busy ? labels.forgetting : labels.forget}
      </button>
      <p className="hint">{error ?? labels.hint}</p>
    </div>
  );
}
