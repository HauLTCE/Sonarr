"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "../../../lib/client";

export type FlagLabels = {
  title: string;
  lead: string;
  on: string;
  off: string;
  working: string;
  failed: string;
  sourceDefault: string;
  sourceGuild: string;
  sourceGlobal: string;
};

export type Flag = {
  feature: string;
  /** Already-translated name and one line on what turning it off stops. */
  label: string;
  hint: string;
  enabled: boolean;
  source: string;
};

/**
 * On/off per feature area.
 *
 * The row says where the current answer comes from — her default, a setting made here, or a bot-wide
 * one — because an admin who flips a switch that a bot-wide setting overrides deserves to know that
 * before wondering why nothing changed.
 */
export function FlagToggles({
  guildId,
  flags,
  labels,
}: {
  guildId: string;
  flags: readonly Flag[];
  labels: FlagLabels;
}) {
  return (
    <section className="card">
      <div className="card-head">
        <h2>{labels.title}</h2>
      </div>
      <p className="hint">{labels.lead}</p>

      {flags.map((flag) => (
        <FlagRow key={flag.feature} guildId={guildId} flag={flag} labels={labels} />
      ))}
    </section>
  );
}

function FlagRow({
  guildId,
  flag,
  labels,
}: {
  guildId: string;
  flag: Flag;
  labels: FlagLabels;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function toggle() {
    setBusy(true);
    setError(null);

    const result = await write<{ message: string }>(`/api/admin/flags/${guildId}`, "PUT", {
      feature: flag.feature,
      enabled: !flag.enabled,
    });

    setBusy(false);

    if (!result.ok) {
      setError(result.error ?? labels.failed);
      return;
    }

    router.refresh();
  }

  const source =
    flag.source === "Guild"
      ? labels.sourceGuild
      : flag.source === "Global"
        ? labels.sourceGlobal
        : labels.sourceDefault;

  const hintId = `flag-${flag.feature}-hint`;

  return (
    <div className="row">
      <div className="row-main">
        <div className="row-title">{flag.label}</div>
        {/* role=alert only when it is an error: a hint that announces itself on every render is noise. */}
        <div className="row-sub" id={hintId} role={error ? "alert" : undefined}>
          {error ?? flag.hint}
        </div>
      </div>

      <div className="row-action">
        {/* A real checkbox behind the switch, so it is reachable by keyboard and announced as a
            checkbox rather than as a div someone styled.

            The feature name is in the row title, outside this label, so the checkbox needs it
            spelled out — otherwise a screen reader announces "checkbox, On" with no idea what of. */}
        <label className="switch">
          <input
            type="checkbox"
            checked={flag.enabled}
            disabled={busy}
            onChange={toggle}
            aria-label={flag.label}
            aria-describedby={hintId}
          />
          <span className="switch-track" aria-hidden="true" />
          <span className="switch-text">
            {busy ? labels.working : flag.enabled ? labels.on : labels.off}
          </span>
        </label>
        <p className="hint mono">{source}</p>
      </div>
    </div>
  );
}
