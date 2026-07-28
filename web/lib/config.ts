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

/** What one row of the form needs, with its wording already resolved. */
export type ConfigField = {
  readonly key: string;
  readonly kind: string;
  readonly value: string | null;
  readonly label: string;
  readonly hint: string;
};

type ApiValues = Record<string, { value: string | null; kind: string }>;

/**
 * Turns the API's map into the form's rows: known keys first in the order above, then anything the
 * API sent that this panel has not been taught yet.
 */
export function configFields(values: ApiValues, t: Translate): ConfigField[] {
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

      return {
        key,
        kind: entry.kind,
        value: entry.value,
        // `t` echoes the key back when there is no entry, which is how an unknown key is spotted.
        label: label === labelKey ? key : label,
        hint: label === labelKey ? t("behaviour.unknown") : t(hintKey),
      };
    });
}
