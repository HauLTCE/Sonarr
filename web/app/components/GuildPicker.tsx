"use client";

import { useRouter } from "next/navigation";

import { guildQuery, type GuildOption } from "../../lib/pages";

/**
 * Which server the guild-scoped pages are about. Only rendered when the visitor shares more than
 * one server with Sonarr — a picker with one option is a question with one answer.
 *
 * A native `<select>`, which is why `color-scheme: dark` is set on `:root`: without it the browser
 * draws this white-on-white. It navigates on change, so the choice survives a reload and can be
 * bookmarked, and it still works from the URL alone if the script has not loaded.
 */
export function GuildPicker({
  guilds,
  current,
  page,
  label,
  hint,
}: {
  guilds: readonly GuildOption[];
  current: string | undefined;
  /** The page's `path`, so switching server keeps you on the page you were reading. */
  page: string;
  label: string;
  /** Why there is a choice here at all, for a screen reader and for anyone who missed the label. */
  hint: string;
}) {
  const router = useRouter();

  return (
    <div className="picker">
      {/* htmlFor rather than a wrapping label, because the hint below must NOT be inside it: text
          inside a wrapping <label> becomes part of the control's accessible name, so the hint would
          be announced as part of the name and then again as the description. */}
      <label className="picker-label" htmlFor="picker">
        {label}
      </label>

      {/* Not `aria-label`: that replaces the accessible name, so it would silence the visible
          "Server" label and announce only the explanation. `title` alone was not enough either --
          it is announced inconsistently, so the hint could simply never be heard. describedby is
          the attribute for a supplementary description, and the text is real text. `title` stays
          for the sighted mouse user, who has no other way to see it. */}
      <span className="at-only" id="picker-hint">
        {hint}
      </span>

      <select
        id="picker"
        value={current ?? ""}
        title={hint}
        aria-describedby="picker-hint"
        onChange={(event) => router.push(`/${page}${guildQuery(event.target.value)}`)}
      >
        {guilds.map((g) => (
          <option key={g.guildId} value={g.guildId}>
            {g.name}
          </option>
        ))}
      </select>
    </div>
  );
}
