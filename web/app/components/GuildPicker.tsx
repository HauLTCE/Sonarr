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
    <label className="picker">
      <span className="picker-label">{label}</span>
      <select
        value={current ?? ""}
        aria-label={hint}
        onChange={(event) => router.push(`/${page}${guildQuery(event.target.value)}`)}
      >
        {guilds.map((g) => (
          <option key={g.guildId} value={g.guildId}>
            {g.name}
          </option>
        ))}
      </select>
    </label>
  );
}
