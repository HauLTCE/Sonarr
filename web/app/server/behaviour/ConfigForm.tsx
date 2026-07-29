"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "../../../lib/client";
import type { ConfigField } from "../../../lib/config";

export type ConfigLabels = {
  title: string;
  lead: string;
  save: string;
  saving: string;
  saved: string;
  reset: string;
  on: string;
  off: string;
  failed: string;
  /** The empty option on a channel/role picker — "use her default". */
  none: string;
  /** Said under a picker whose stored value is not in the list any more. */
  gone: string;
};

/**
 * The settings fields. One save per field rather than one save for the form: each key validates
 * separately in the API and returns its own reason ("xp_multiplier needs a whole number between 1
 * and 5"), and a single Save button would have to either discard those reasons or show ten at once.
 *
 * The wording is resolved on the server and handed down, so this component holds no dictionary — the
 * page it belongs to is server-rendered and already has one.
 */
export function ConfigForm({
  guildId,
  fields,
  labels,
}: {
  guildId: string;
  fields: readonly ConfigField[];
  labels: ConfigLabels;
}) {
  return (
    <section className="card">
      <div className="card-head">
        <h2>{labels.title}</h2>
      </div>
      <p className="hint">{labels.lead}</p>

      {fields.map((field) => (
        <ConfigRow key={field.key} guildId={guildId} field={field} labels={labels} />
      ))}
    </section>
  );
}

function ConfigRow({
  guildId,
  field,
  labels,
}: {
  guildId: string;
  field: ConfigField;
  labels: ConfigLabels;
}) {
  const router = useRouter();
  const [draft, setDraft] = useState(field.value ?? "");
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState<{ text: string; bad: boolean } | null>(null);

  const dirty = draft !== (field.value ?? "");

  async function send(value: string | null) {
    setBusy(true);
    setNote(null);

    const result = await write<{ message: string }>(`/api/admin/config/${guildId}`, "PUT", {
      key: field.key,
      value,
    });

    setBusy(false);

    if (!result.ok) {
      // The API's own validation reason, which is worth far more than a generic failure.
      setNote({ text: result.error ?? labels.failed, bad: true });
      return;
    }

    setNote({ text: labels.saved, bad: false });
    router.refresh();
  }

  const id = `cfg-${field.key}`;
  const noteId = `${id}-note`;

  // A stored id that is no longer in the list: a deleted channel, or one the bot lost sight of. The
  // option is rendered anyway, because a <select> whose value is absent silently shows the first
  // entry instead — which would make the page claim a setting the server does not have, and then
  // save that claim on the next press of any other field's button.
  const missing =
    field.options !== undefined &&
    field.value !== null &&
    !field.options.some((o) => o.id === field.value);

  return (
    <div className="field">
      <label htmlFor={id}>{field.label}</label>

      <div className="field-row">
        {field.kind === "Boolean" ? (
          <select
            id={id}
            value={draft}
            disabled={busy}
            aria-describedby={noteId}
            onChange={(event) => setDraft(event.target.value)}
          >
            <option value="">{labels.reset}</option>
            <option value="true">{labels.on}</option>
            <option value="false">{labels.off}</option>
          </select>
        ) : field.options !== undefined ? (
          <select
            id={id}
            value={draft}
            disabled={busy}
            aria-describedby={noteId}
            onChange={(event) => setDraft(event.target.value)}
          >
            <option value="">{labels.none}</option>
            {missing ? <option value={field.value!}>{field.value}</option> : null}
            {field.options.map((o) => (
              <option key={o.id} value={o.id}>
                {field.sigil}
                {o.name}
              </option>
            ))}
          </select>
        ) : (
          <input
            id={id}
            type="text"
            value={draft}
            disabled={busy}
            autoComplete="off"
            spellCheck={false}
            aria-describedby={noteId}
            onChange={(event) => setDraft(event.target.value)}
          />
        )}

        <button
          type="button"
          className="btn btn-accent"
          disabled={busy || !dirty}
          onClick={() => send(draft.trim() === "" ? null : draft.trim())}
        >
          {busy ? labels.saving : labels.save}
        </button>

        {/* Only shown when there is a stored value to clear — otherwise it would do nothing. */}
        {field.value === null ? null : (
          <button type="button" className="btn" disabled={busy} onClick={() => send(null)}>
            {labels.reset}
          </button>
        )}
      </div>

      {/* One line doing three jobs: the field's hint, a warning when the stored id is no longer a
          real channel or role, and after a save the API's own answer. It is a live region only while
          it carries that answer — a hint that speaks on render is noise. */}
      <p
        id={noteId}
        className={note?.bad ? "hint hint-bad" : "hint"}
        role={note?.bad ? "alert" : undefined}
        aria-live={note && !note.bad ? "polite" : undefined}
      >
        {note?.text ?? (missing ? `${labels.gone} ${field.hint}` : field.hint)}
      </p>
    </div>
  );
}
