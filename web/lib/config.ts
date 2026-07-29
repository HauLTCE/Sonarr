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
};

type ApiValues = Record<string, { value: string | null; kind: string }>;

/**
 * The kind for a key the API did not send, so the row still renders the right control. Only Boolean
 * changes the control, so everything else can stay a text field.
 */
const KINDS: Record<string, string> = {
  levelup_dm: "Boolean",
  xp_decay: "Boolean",
};

/**
 * Turns the API's map into the form's rows: known keys first in the order above, then anything the
 * API sent that this panel has not been taught yet.
 *
 * Every known key gets a row whether or not the API sent it. `GET /api/admin/config/{id}` only
 * returns keys that already have a stored value, so filtering to what it sent would mean a setting
 * that has never been set cannot be set — the page would show fewer fields the less configured the
 * server is, which is backwards.
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
    .map((key) => {
      const entry = values[key] ?? { value: null, kind: KINDS[key] ?? "ChannelId" };
      const labelKey = `cfg.${key}` as StringKey;
      const hintKey = `cfg.${key}.hint` as StringKey;
      const label = t(labelKey);

      // Only the two id kinds become pickers. ChannelWeights is `id:percent` pairs, so a list of
      // names cannot express it, and Timezone/Integer/Boolean are not ids at all.
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
      };
    });
}
