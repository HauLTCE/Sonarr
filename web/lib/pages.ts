import type { StringKey } from "./strings";

/**
 * The page list and the tier model.
 *
 * Kept free of any server-only import — the guild picker is a client component and imports from
 * here, so anything that reaches `next/headers` lands in the browser bundle and fails the build.
 * The session read that used to live here is in `panel.ts`, next to its only caller.
 *
 * One list drives three things: which pages exist for a visitor, which side of the panel each one
 * belongs to, and the order they appear in the navbar. A normal user's nav has no gap and nothing
 * greyed out — the pages they cannot reach are absent, not disabled, because a disabled control is
 * an invitation to wonder what you are missing.
 *
 * The `tier` column does double duty, and that is deliberate: it is both the lowest tier that may
 * open a page and which of the three sides it lives on. One column, so the nav cannot come to
 * disagree with the gate — and it is the same line the API enforces.
 *
 * Every page here has an endpoint behind it. If a page has no data source, it does not belong in
 * the nav pretending it will fill in later.
 */

export type Tier = "user" | "guild" | "bot";

export type Page = {
  /** Appended to "/" — the empty string is the panel root. */
  readonly path: string;
  readonly label: StringKey;
  /** The lowest tier that may open it. */
  readonly tier: Tier;
  /** Needs a `?guild=` to mean anything, so its link carries the current one. */
  readonly guildScoped?: boolean;
};

/**
 * Ordered as a widening scope: your own data first, then the server you run, then the bot. Reading
 * left to right is moving outward, which is also the order of how much trust each page needs — so
 * the sides row and the pages row inside it both run least-privileged first.
 *
 * The order is load-bearing in one place: `sectionHome` takes the first page of a side, so whatever
 * sits at the top of each block is what a side's tab opens.
 */
export const PAGES: readonly Page[] = [
  { path: "", label: "nav.you", tier: "user", guildScoped: true },
  { path: "memory", label: "nav.memory", tier: "user", guildScoped: true },
  { path: "music", label: "nav.music", tier: "user", guildScoped: true },
  { path: "activity", label: "nav.activity", tier: "user", guildScoped: true },
  { path: "errors", label: "nav.errors", tier: "user" },
  { path: "privacy", label: "nav.privacy", tier: "user" },
  { path: "server", label: "nav.server", tier: "guild", guildScoped: true },
  { path: "server/behaviour", label: "nav.behaviour", tier: "guild", guildScoped: true },
  { path: "server/moderation", label: "nav.moderation", tier: "guild", guildScoped: true },
  { path: "server/stats", label: "nav.stats", tier: "guild", guildScoped: true },
  { path: "bot", label: "nav.health", tier: "bot" },
  { path: "bot/audit", label: "nav.audit", tier: "bot" },
];

const RANK: Record<Tier, number> = { user: 0, guild: 1, bot: 2 };

/** Does a visitor at `held` reach something that needs `needs`? Each tier contains the ones below. */
export function reaches(held: Tier, needs: Tier): boolean {
  return RANK[held] >= RANK[needs];
}

export function pagesFor(tier: Tier): readonly Page[] {
  return PAGES.filter((p) => reaches(tier, p.tier));
}

/**
 * The nav's top level. Your own data, the server you run and the bot are three different jobs done
 * by three different people who happen to share one login, so they are three sections rather than
 * one list of twelve — an admin looking for a setting should not scroll past their own XP to find it.
 *
 * The tier already draws that line (it is the same line the API gates on), so this is the existing
 * field read as a grouping instead of a new one to keep in sync.
 */
export const SECTIONS: readonly Tier[] = ["user", "guild", "bot"];

/** Section headings — `tier.*` names the badge, this names the tab. */
export const SECTION_LABEL: Record<Tier, StringKey> = {
  user: "sec.you",
  guild: "sec.server",
  bot: "sec.bot",
};

export function sectionsFor(tier: Tier): readonly Tier[] {
  return SECTIONS.filter((s) => reaches(tier, s));
}

export function pagesIn(section: Tier): readonly Page[] {
  return PAGES.filter((p) => p.tier === section);
}

/** Which section a page belongs to, by `path`. Falls back to the user side for an unknown path. */
export function sectionOf(path: string): Tier {
  return PAGES.find((p) => p.path === path)?.tier ?? "user";
}

/** The page a section's tab points at: its first, which is also its overview. */
export function sectionHome(section: Tier): Page {
  return pagesIn(section)[0];
}

export type GuildOption = {
  readonly guildId: string;
  readonly name: string;
  /** Manage Server in that guild, resolved live by the API — never inferred here. */
  readonly canManage: boolean;
};

export type Session = {
  readonly userId: string;
  readonly tier: Tier;
  readonly guilds: readonly GuildOption[];
  readonly expiresAt: string | null;
};

/**
 * Which guild a page is about. `requested` comes from the query string, so it is checked against
 * the visitor's own list rather than trusted — a panel that renders another server because the URL
 * said so is not something to leave to the backend alone, even though the backend refuses too.
 *
 * `managedOnly` is set by the guild-tier pages: for those, a server the visitor is merely a member
 * of is not a valid answer.
 */
export function pickGuild(
  guilds: readonly GuildOption[],
  requested: string | undefined,
  managedOnly = false,
): GuildOption | null {
  const eligible = managedOnly ? guilds.filter((g) => g.canManage) : guilds;
  const asked = requested ? eligible.find((g) => g.guildId === requested) : undefined;

  return asked ?? eligible[0] ?? null;
}

/** `?guild=…` for a link, or an empty string when there is nothing to carry. */
export function guildQuery(guildId: string | undefined): string {
  return guildId ? `?guild=${encodeURIComponent(guildId)}` : "";
}

/** The href for one page, carrying the guild only where it means something. */
export function href(page: Page, guildId: string | undefined): string {
  return `/${page.path}${page.guildScoped ? guildQuery(guildId) : ""}`;
}
