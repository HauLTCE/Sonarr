"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { write } from "../../../lib/client";
import {
  formatPercent,
  parseWeights,
  serializeWeights,
  toPercent,
  type ChannelWeight,
  type ConfigField,
  type NamedEntity,
} from "../../../lib/config";

/** What `#` means here. A literal in JSX would trip `react/jsx-no-literals`. */
const SIGIL_CHANNEL = "#";

/** What an unweighted channel already earns, so a new row starts as a no-op. */
const NORMAL_PERCENT = 100;

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
  /** The weights editor's own wording. */
  weightAdd: string;
  weightAddNone: string;
  weightRemove: string;
  weightPercent: string;
  weightEmpty: string;
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
  const stored = field.value ?? "";
  const [draft, setDraft] = useState(stored);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState<{ text: string; bad: boolean } | null>(null);

  // The stored value changed under us, which here only ever means our own save landed and
  // `router.refresh()` re-rendered the page. The draft is state, so without this it goes on showing
  // what was typed: after a Reset the row would claim a value the server just cleared, and every
  // weight row would stay listed after the key was gone.
  //
  // Mirroring the value that came back rather than the one we sent, because the domain normalizes on
  // write — weights are re-sorted by channel id, so `456:0,123:150` is stored as `123:150,456:0`.
  // Echoing the sent string would leave the row permanently dirty with Save lit and nothing to do.
  const [mirror, setMirror] = useState(stored);
  if (mirror !== stored) {
    setMirror(stored);
    setDraft(stored);
  }

  const dirty = draft !== stored;

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

    // The mirror above picks the new value up from the re-render, so nothing is set here.
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

  // The weights editor replaces both the label and the control: it has no single input for a label
  // to point at, so it names itself with a <legend> rather than leaving one aimed at an id nothing
  // has. Undefined channels means the directory failed, and then this falls back to the text field
  // it used to be — a pasted `123:150` still works, which beats not being able to set it at all.
  const weights = field.kind === "ChannelWeights" ? field.channels : undefined;

  return (
    <div className="field">
      {weights === undefined ? (
        <label htmlFor={id}>{field.label}</label>
      ) : (
        <Weights
          id={id}
          legend={field.label}
          draft={draft}
          channels={weights}
          minimum={field.minimum ?? 0}
          maximum={field.maximum ?? 500}
          busy={busy}
          noteId={noteId}
          labels={labels}
          onChange={setDraft}
        />
      )}

      <div className="field-row">
        {weights !== undefined ? null : field.kind === "Boolean" ? (
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

/**
 * The per-channel XP weight editor: one row per weighted channel, a picker to add another.
 *
 * It was a single text field holding the raw `123:150,456:0` string, which meant choosing a weight
 * for a channel required knowing the channel's snowflake and the storage syntax — from a page that
 * was already showing every channel by name two fields above.
 *
 * The draft stays that same string. Parsing it on every render rather than holding rows in state
 * keeps one source of truth, so the parent's dirty check, Save and Reset all work unchanged, and a
 * row this panel would drop is a row the bot drops too.
 */
function Weights({
  id,
  legend,
  draft,
  channels,
  minimum,
  maximum,
  busy,
  noteId,
  labels,
  onChange,
}: {
  id: string;
  legend: string;
  draft: string;
  channels: readonly NamedEntity[];
  minimum: number;
  maximum: number;
  busy: boolean;
  noteId: string;
  labels: ConfigLabels;
  onChange: (next: string) => void;
}) {
  const rows = parseWeights(draft, { minimum, maximum });
  const weighted = new Set(rows.map((row) => row.id));
  const spare = channels.filter((channel) => !weighted.has(channel.id));

  function commit(next: readonly ChannelWeight[]) {
    onChange(serializeWeights(next) ?? "");
  }

  return (
    // A fieldset because this is a group of controls, not one control: <legend> names the whole thing
    // natively, where the plain <label> the other fields use would have to point at an id no single
    // input here owns. Each row's input is named by its own channel.
    <fieldset className="weights" aria-describedby={noteId}>
      <legend>{legend}</legend>

      {rows.length === 0 ? <p className="hint">{labels.weightEmpty}</p> : null}

      {rows.map((row) => (
        <WeightRow
          key={row.id}
          id={`${id}-${row.id}`}
          // A weight can outlive its channel: the id is shown when the name is gone, because it is
          // still the thing that has to be removed.
          name={channels.find((channel) => channel.id === row.id)?.name ?? row.id}
          percent={row.percent}
          minimum={minimum}
          maximum={maximum}
          busy={busy}
          labels={labels}
          onPercent={(percent) =>
            commit(rows.map((other) => (other.id === row.id ? { ...other, percent } : other)))
          }
          onRemove={() => commit(rows.filter((other) => other.id !== row.id))}
        />
      ))}

      {spare.length === 0 ? (
        <p className="hint">{labels.weightAddNone}</p>
      ) : (
        <div className="weight">
          <label className="at-only" htmlFor={`${id}-add`}>
            {labels.weightAdd}
          </label>
          <select
            id={`${id}-add`}
            className="weight-add"
            // Always back to the placeholder: this select is an action, not a value. Leaving the
            // picked channel selected would show a channel that is already listed in a row below.
            value=""
            disabled={busy}
            onChange={(event) => {
              if (event.target.value !== "") {
                commit([...rows, { id: event.target.value, percent: NORMAL_PERCENT }]);
              }
            }}
          >
            <option value="">{labels.weightAdd}</option>
            {spare.map((channel) => (
              <option key={channel.id} value={channel.id}>
                {SIGIL_CHANNEL}
                {channel.name}
              </option>
            ))}
          </select>
        </div>
      )}
    </fieldset>
  );
}

/**
 * One channel's weight, shown as a percentage.
 *
 * The text is local state so it can be half-typed — the committed value is a number, and reformatting
 * on every keystroke would fight the cursor. Blur is where anything the user wrote becomes a
 * percentage: `0.7`, `70` and `70%` all land on `70%`.
 *
 * But it commits on *change* as well as blur, and that is not belt-and-braces. Blur alone meant a
 * typed percent did not reach the parent's draft until focus left, so Save stayed disabled — and
 * clicking a disabled button does not blur the input, so the obvious "type it, press Save" could not
 * save at all. Committing as you type lights Save on the first keystroke.
 */
function WeightRow({
  id,
  name,
  percent,
  minimum,
  maximum,
  busy,
  labels,
  onPercent,
  onRemove,
}: {
  id: string;
  name: string;
  percent: number;
  minimum: number;
  maximum: number;
  busy: boolean;
  labels: ConfigLabels;
  onPercent: (percent: number) => void;
  onRemove: () => void;
}) {
  const shown = formatPercent(percent);
  const [text, setText] = useState(shown);

  // The committed percent changed without this row asking — another admin's save landing under a
  // `router.refresh()`. Then the text is stale and has to follow.
  //
  // `commit` below moves the mirror itself, so a change this row made is not mistaken for one of
  // those. Without that, committing as you type would reformat the field mid-keystroke: typing "7"
  // would commit 7, come back as "7%", and put the cursor in front of the sign.
  const [mirror, setMirror] = useState(percent);
  if (mirror !== percent) {
    setMirror(percent);
    setText(formatPercent(percent));
  }

  /** In range and a number. Anything else is not committed, so the parent keeps the old value. */
  function usable(value: number | null): value is number {
    return value !== null && value >= minimum && value <= maximum;
  }

  function commit(next: number) {
    if (next !== percent) {
      setMirror(next);
      onPercent(next);
    }
  }

  /** As typed: commit what is usable so Save lights up, and leave the text exactly as written. */
  function type(next: string) {
    setText(next);

    const parsed = toPercent(next);
    if (usable(parsed)) {
      commit(parsed);
    }
  }

  /** On the way out: this is where the text becomes a percentage. */
  function settle() {
    const parsed = toPercent(text);

    // ponytail: out of range or not a number at all reverts to the committed value, with the range
    // in the field's hint as the only explanation. A per-row message would need a per-row live
    // region, and the field already has one for what the API says. Upgrade path: if this confuses
    // anyone, clamp instead of reverting and say so in the note.
    if (!usable(parsed)) {
      setText(shown);
      return;
    }

    setText(formatPercent(parsed));
    commit(parsed);
  }

  return (
    <div className="weight">
      <label htmlFor={id}>
        {SIGIL_CHANNEL}
        {name}
        <span className="at-only">{labels.weightPercent}</span>
      </label>

      <input
        id={id}
        type="text"
        className="weight-value"
        inputMode="numeric"
        value={text}
        disabled={busy}
        autoComplete="off"
        spellCheck={false}
        onChange={(event) => type(event.target.value)}
        onBlur={settle}
      />

      <button type="button" className="btn btn-small" disabled={busy} onClick={onRemove}>
        {labels.weightRemove}
        <span className="at-only">
          {SIGIL_CHANNEL}
          {name}
        </span>
      </button>
    </div>
  );
}
