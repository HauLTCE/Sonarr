"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "@/lib/client";
import type { Locale } from "@/lib/strings";
import { translator } from "@/lib/strings";

export type ConfigRow = { key: string; value: string | null; kind: string };

/**
 * One config key: the current value, a field, save and reset (checklist 296).
 *
 * The field is plain text for every kind. Validation lives in the same Application service
 * `/config set` uses, and the message it returns is shown as-is — a second set of rules in the
 * browser would be a second thing to keep in step, and it would disagree eventually.
 */
export function ConfigForm({ locale, guildId, row }: { locale: Locale; guildId: string; row: ConfigRow }) {
  const t = translator(locale);
  const router = useRouter();

  const [value, setValue] = useState(row.value ?? "");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  const send = async (next: string | null) => {
    setBusy(true);
    setMessage(null);

    const result = await write<{ message: string }>(`/api/admin/config/${guildId}`, "PUT", {
      key: row.key,
      value: next,
    });

    setBusy(false);

    setMessage(
      result.ok
        ? { ok: true, text: result.data.message || t("admin.config.saved") }
        : { ok: false, text: result.error ?? t("common.error") },
    );

    if (result.ok) {
      router.refresh();
    }
  };

  return (
    <tr>
      <th scope="row">
        <span className="mono">{row.key}</span>
        <br />
        <span className="pill">{row.kind}</span>
      </th>
      <td>
        <label>
          <span className="hint">{t("admin.config.value")}</span>
          <input
            type="text"
            value={value}
            disabled={busy}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => setValue(event.target.value)}
          />
        </label>
      </td>
      <td>
        <div className="row">
          <button
            type="button"
            disabled={busy}
            onClick={() => send(value.trim().length === 0 ? null : value.trim())}
          >
            {busy ? t("admin.config.saving") : t("admin.config.save")}
          </button>
          <button
            type="button"
            className="quiet"
            disabled={busy || (row.value ?? "") === ""}
            onClick={() => {
              setValue("");
              void send(null);
            }}
          >
            {t("admin.config.clear")}
          </button>
        </div>

        <div aria-live="polite">
          {message && (
            <p className="hint" role={message.ok ? undefined : "alert"}>
              {message.text}
            </p>
          )}
        </div>
      </td>
    </tr>
  );
}
