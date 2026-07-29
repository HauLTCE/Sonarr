import type { StringKey, Translate } from "./strings";

/**
 * The config keys this panel renders, in the order it renders them.
 *
 * Mirrors `ConfigKeys.All` in the domain, and deliberately: the API sends its keys with a `kind` but
 * no wording, because its descriptions are written for Discord's autocomplete and cannot be
 * translated. The list here is what gives each one a label, a hint and a place on the page.
 *
 * A key the API sends that is missing here still renders — as a plain text field with a note saying
 * the panel does not know it yet — so adding a key to the domain can never make it invisible.
 */
export const CONFIG_ORDER: readonly string[] = [
  "log_channel",
  "welcome_channel",
  "announce_channel",
  "levelup_channel",
  "music_channel",
  "autorole_id",
  "dj_role",
  "timezone",
  "xp_multiplier",
  "levelup_dm",
  "xp_decay",
  "xp_channel_weights",
];

/** One channel or role, as `/api/admin/directory/{guildId}` sends it. */
export type NamedEntity = { readonly id: string; readonly name: string };

/** That endpoint's whole payload. Null when it failed — every field falls back to a text box. */
export type Directory = {
  readonly channels: readonly NamedEntity[];
  readonly roles: readonly NamedEntity[];
} | null;

/** What one row of the form needs, with its wording already resolved. */
export type ConfigField = {
  readonly key: string;
  readonly kind: string;
  readonly value: string | null;
  readonly label: string;
  readonly hint: string;
  /**
   * The list to pick from, for the keys that name a channel or a role. Undefined means "type it" —
   * either because the key is a timezone or a number, or because the directory could not be read.
   */
  readonly options?: readonly NamedEntity[];
  /** The sigil a picked value is shown with: `#` for a channel, `@` for a role. */
  readonly sigil?: string;
  /** Bounds from the domain catalog, for the kinds that have them. Null when unbounded. */
  readonly minimum?: number | null;
  readonly maximum?: number | null;
  /** Every channel in the guild, for the weights editor — it needs the list *and* a number each. */
  readonly channels?: readonly NamedEntity[];
};

type ApiValues = Record<
  string,
  {
    value: string | null;
    kind: string;
    minimum?: number | null;
    maximum?: number | null;
  }
>;

/**
 * Turns the API's map into the form's rows: catalog keys first in the order above, then anything the
 * API sent that this panel has not been taught wording for yet.
 *
 * Every key the API sends gets a row whether or not it has a stored value — `kind` comes from the
 * domain catalog either way, so an unset key still renders the control it deserves. It used to come
 * from a two-entry guess table here, defaulting to ChannelId, which is how `dj_role` on a fresh
 * guild became a channel picker that saved a channel id into a role setting without complaint.
 *
 * Hence the `key in values` filter: a row is only rendered for a key the API vouched for. If a panel
 * ever runs against an older bot that sends stored keys only, the unset ones go missing from the page
 * — visible, and fixed by deploying the pair together. The alternative is inventing a `kind` for them
 * again, which is invisible and writes the wrong id into the wrong setting.
 */
export function configFields(
  values: ApiValues,
  t: Translate,
  directory: Directory = null,
): ConfigField[] {
  const known = new Set(CONFIG_ORDER);
  const extra = Object.keys(values)
    .filter((key) => !known.has(key))
    .sort();

  return [...CONFIG_ORDER, ...extra]
    .filter((key) => key in values)
    .map((key) => {
      const entry = values[key];
      const labelKey = `cfg.${key}` as StringKey;
      const hintKey = `cfg.${key}.hint` as StringKey;
      const label = t(labelKey);

      // Only the two id kinds become <select>s of names. ChannelWeights needs the channel list too,
      // but a name per row plus a number — so it gets its own editor and `channels` below.
      const options =
        entry.kind === "ChannelId"
          ? directory?.channels
          : entry.kind === "RoleId"
            ? directory?.roles
            : undefined;

      return {
        key,
        kind: entry.kind,
        value: entry.value,
        // `t` echoes the key back when there is no entry, which is how an unknown key is spotted.
        label: label === labelKey ? key : label,
        hint: label === labelKey ? t("behaviour.unknown") : t(hintKey),
        options,
        sigil: options === undefined ? undefined : entry.kind === "ChannelId" ? "#" : "@",
        minimum: entry.minimum ?? null,
        maximum: entry.maximum ?? null,
        channels: entry.kind === "ChannelWeights" ? directory?.channels : undefined,
      };
    });
}

/** One row of the weights editor: a channel, and the percent it earns. */
export type ChannelWeight = { readonly id: string; readonly percent: number };

/**
 * Reads the stored `channelId:percent,…` string into rows.
 *
 * Drops exactly what `LevelsConfigKeys.ParseWeights` drops, bounds included — hence `field`, which
 * carries the domain's min/max. A pair the bot skips must not become a row here: a hand-edited `600`
 * is ignored by the bot, and showing it would have the page promise a weight nothing applies. Order is
 * the stored order, which the domain normalizes by channel id on write.
 */
export function parseWeights(
  raw: string | null,
  bounds: { minimum?: number | null; maximum?: number | null } = {},
): ChannelWeight[] {
  if (raw === null || raw.trim() === "") {
    return [];
  }

  const rows = new Map<string, number>();

  for (const pair of raw.split(",")) {
    const [id, value] = pair.split(":", 2);
    const percent = toPercent(value);

    // /^\d+$/ and not Number(): the id is a snowflake, so it goes nowhere near a float. A blank
    // percent is a dropped row rather than a 0 — 0 means "earns nothing", which is a real setting.
    if (
      id !== undefined &&
      /^\d+$/.test(id.trim()) &&
      percent !== null &&
      percent >= (bounds.minimum ?? 0) &&
      percent <= (bounds.maximum ?? Infinity)
    ) {
      rows.set(id.trim(), percent);
    }
  }

  return [...rows].map(([id, percent]) => ({ id, percent }));
}

/** Back to the stored form. Empty means the key gets cleared, which is what "no weights" is. */
export function serializeWeights(rows: readonly ChannelWeight[]): string | null {
  const text = rows.map((row) => `${row.id}:${row.percent}`).join(",");
  return text === "" ? null : text;
}

/**
 * A percent out of whatever a human or an old row wrote, or null if it is not a number at all.
 *
 * A decimal is read as a fraction and a whole number as a percent already: `0.7` → 70, `70` → 70,
 * `1.5` → 150. That split is the only unambiguous one — `1` alone could be 1% or 100%, and guessing
 * "100%" there would silently multiply a channel that was meant to be nearly muted.
 */
export function toPercent(raw: string | number | null | undefined): number | null {
  if (raw === null || raw === undefined) {
    return null;
  }

  // A trailing "%" is stripped, because the editor's own field shows one: without this, reading back
  // a value nobody edited (`70%`) gives NaN and the row reverts to what it already said, which looks
  // like the field refusing input for no reason. A "%" anywhere else is still junk and stays junk.
  const text = typeof raw === "number" ? String(raw) : raw.trim().replace(/%$/, "").trim();
  if (text === "") {
    return null;
  }

  const value = Number(text);
  if (!Number.isFinite(value) || value < 0) {
    return null;
  }

  return Math.round(Number.isInteger(value) ? value : value * 100);
}

/**
 * `70` → `70%`. One place writes the sign, so no two rows can disagree about it — and it keeps the
 * sign out of JSX, where `react/jsx-no-literals` (at error here) would reject a bare "%".
 */
export function formatPercent(percent: number): string {
  return `${percent}%`;
}
