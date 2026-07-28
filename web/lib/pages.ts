import type { StringKey } from "./strings";

/**
 * The page list and the tier model.
 *
 * Kept free of any server-only import — the rail is a client component, so anything that reaches
 * `next/headers` from here lands in the browser bundle and fails the build. The session read that
 * used to live here is in `panel.ts`, next to its only caller.
 *
 * One list drives three things: the rail at the bottom, which pages exist for a visitor, and the
 * order the slide moves in. A normal user's rail has no gap and no greyed-out segment — the pages
 * they cannot reach are absent, not disabled, because a disabled control is an invitation to wonder
 * what you are missing.
 *
 * Every page here has an endpoint behind it. If a page has no data source, it does not belong on
 * the rail pretending it will fill in later.
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
 * Ordered so the slide reads as a widening scope: your own data first, then the server you run,
 * then the bot. Someone sliding right is moving outward, which is also the order of how much
 * trust each page needs.
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
