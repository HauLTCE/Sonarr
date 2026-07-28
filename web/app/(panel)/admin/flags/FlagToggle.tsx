"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "@/lib/client";
import type { Locale } from "@/lib/strings";
import { translator } from "@/lib/strings";

export type FlagRow = { feature: string; enabled: boolean; source: string };

/**
 * One module's on/off switch (checklist 297).
 *
 * A button, not a checkbox: the state is server-held, and a checkbox that flips optimistically and
 * then has to flip back on a rejected write is a worse thing to look at than a button whose label
 * says what it will do.
 */
export function FlagToggle({
  locale,
  guildId,
  row,
}: {
  locale: Locale;
  guildId: string;
  row: FlagRow;
}) {
  const t = translator(locale);
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  return (
    <tr>
      <th scope="row" className="mono">
        {row.feature}
      </th>
      <td>{t(row.enabled ? "admin.flags.on" : "admin.flags.off")}</td>
      <td>
        <span className="pill">{row.source}</span>
      </td>
      <td>
        <button
          type="button"
          className={row.enabled ? "quiet" : undefined}
          disabled={busy}
          onClick={async () => {
            setBusy(true);
            setError(null);

            const result = await write<{ message: string }>(
              `/api/admin/flags/${guildId}`,
              "PUT",
              { feature: row.feature, enabled: !row.enabled },
            );

            setBusy(false);

            if (result.ok) {
              router.refresh();
              return;
            }

            setError(result.error ?? t("common.error"));
          }}
        >
          {t(row.enabled ? "admin.flags.disable" : "admin.flags.enable")}
        </button>

        <div aria-live="polite">
          {error && (
            <p className="hint" role="alert">
              {error}
            </p>
          )}
        </div>
      </td>
    </tr>
  );
}
