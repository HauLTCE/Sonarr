"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { write } from "@/lib/client";
import type { Locale } from "@/lib/strings";
import { translator } from "@/lib/strings";

/** The word the API insists on, echoed here so the label tells the truth. */
const confirmWord = "DELETE";

type Deletion = { deleted: number; total: number; note: string };

/**
 * One irreversible action behind a typed confirmation (checklist 294).
 *
 * The type-to-confirm is the API's rule, not decoration — this component would fail without it, so
 * the field is the same gate rather than a second one bolted on for looks. Her own backup notice
 * comes back in the response and is shown verbatim: the panel does not get to paraphrase how long a
 * copy of someone's data survives.
 */
export function DangerButton({ locale, path }: { locale: Locale; path: string }) {
  const t = translator(locale);
  const router = useRouter();
  const field = useId();

  const [typed, setTyped] = useState("");
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<Deletion | null>(null);
  const [failed, setFailed] = useState(false);

  const armed = typed.trim() === confirmWord;

  return (
    <form
      onSubmit={async (event) => {
        event.preventDefault();

        if (!armed || busy) {
          return;
        }

        setBusy(true);
        setFailed(false);

        const result = await write<Deletion>(path, "DELETE", { confirm: typed.trim() });

        setBusy(false);

        if (!result.ok) {
          setFailed(true);
          return;
        }

        setTyped("");
        setDone(result.data);
        // Every other page reads from the same rows; refresh so none of them keep showing deleted data.
        router.refresh();
      }}
    >
      <label htmlFor={field}>
        {t("privacy.confirmLabel", { word: confirmWord })}
        <input
          id={field}
          type="text"
          value={typed}
          autoComplete="off"
          spellCheck={false}
          onChange={(event) => setTyped(event.target.value)}
        />
      </label>

      <div className="row">
        <button type="submit" className="danger" disabled={!armed || busy}>
          {busy ? t("privacy.deleting") : t("privacy.confirmButton")}
        </button>
      </div>

      <div aria-live="polite">
        {done && (
          <p className="notice good">
            {t("privacy.deleted", { count: done.deleted })} {done.note}
          </p>
        )}
        {failed && (
          <p className="notice bad" role="alert">
            {t("common.error")}
          </p>
        )}
      </div>
    </form>
  );
}
