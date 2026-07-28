import type { GuildOption } from "@/lib/guilds";
import type { Locale } from "@/lib/strings";
import { translator } from "@/lib/strings";

/**
 * The server picker every guild-scoped page carries.
 *
 * A plain GET form with no JavaScript: the browser submits `?guild=…` to the current page, which is
 * a server component, so switching servers is one request and works with scripting off. The submit
 * button stays rendered rather than relying on change events for the same reason.
 */
export function GuildPicker({
  locale,
  path,
  options,
  selected,
}: {
  locale: Locale;
  path: string;
  options: GuildOption[];
  selected: string;
}) {
  const t = translator(locale);

  // Nothing to pick between — rendering a one-item dropdown is noise.
  if (options.length < 2) {
    return null;
  }

  return (
    <form action={path} method="get" className="card">
      <div className="row">
        <label htmlFor="guild">
          {t("guild.label")}
          <select id="guild" name="guild" defaultValue={selected}>
            {options.map((g) => (
              <option key={g.guildId} value={g.guildId}>
                {g.name}
              </option>
            ))}
          </select>
        </label>

        <button type="submit" className="quiet">
          {t("guild.switch")}
        </button>
      </div>
    </form>
  );
}
