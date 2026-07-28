"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

/**
 * Narrow the case list to one member.
 *
 * A GET form in effect: it navigates, so the filtered list has its own URL and can be bookmarked or
 * sent to another admin. Digits only — the API parses this as a snowflake and a non-numeric value
 * would silently come back as "no filter", which reads like the filter is broken.
 */
export function CaseFilter({
  guildId,
  target,
  labels,
}: {
  guildId: string;
  target: string;
  labels: { label: string; hint: string; apply: string; clear: string };
}) {
  const router = useRouter();
  const [draft, setDraft] = useState(target);

  function apply(value: string) {
    const params = new URLSearchParams({ guild: guildId });
    if (value) {
      params.set("target", value);
    }

    router.push(`/server/moderation?${params.toString()}`);
  }

  return (
    <section className="card">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          apply(draft.replace(/\D/g, ""));
        }}
      >
        <label htmlFor="target">{labels.label}</label>
        <div className="field-row">
          <input
            id="target"
            type="text"
            inputMode="numeric"
            value={draft}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => setDraft(event.target.value)}
          />
          <button type="submit" className="btn btn-accent">
            {labels.apply}
          </button>
          {target ? (
            <button
              type="button"
              className="btn"
              onClick={() => {
                setDraft("");
                apply("");
              }}
            >
              {labels.clear}
            </button>
          ) : null}
        </div>
        <p className="hint">{labels.hint}</p>
      </form>
    </section>
  );
}
