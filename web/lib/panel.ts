import { redirect } from "next/navigation";

import { apiGet } from "./api";
import { serverTranslator } from "./locale";
import {
  PAGES,
  pickGuild,
  reaches,
  type GuildOption,
  type Page,
  type Session,
  type Tier,
} from "./pages";
import type { Translate } from "./strings";

/**
 * The four lines every panel page starts with: who is asking, which server they mean, their
 * language, and whether they are allowed on this page at all.
 *
 * Doing it here rather than per page is what stops a new page shipping without the tier check —
 * the page cannot render without calling this, and this cannot return without deciding.
 */

/** What a page's `searchParams` looks like in Next 16 — a promise, and every value is untrusted. */
export type Query = Promise<Record<string, string | string[] | undefined>>;

export type Panel = {
  readonly session: Session;
  /** Null only when the visitor shares no server with Sonarr; guild pages redirect before that. */
  readonly guild: GuildOption | null;
  readonly t: Translate;
};

function one(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

type MeResponse = { userId: string; isAdmin: boolean; expiresAt: string | null };

/**
 * Who is asking, and what they can reach. Two calls because the API keeps the session probe separate
 * from the guild list; they are independent, so they go together.
 *
 * Returns null when there is no valid session — every page treats that as "go to the login page"
 * rather than rendering an empty panel.
 *
 * Lives here rather than in `pages.ts` because it reads cookies: `pages.ts` is imported by the rail,
 * which is a client component, and a server-only import there is a broken build.
 */
export async function session(): Promise<Session | null> {
  const [me, guilds] = await Promise.all([
    apiGet<MeResponse>("/api/me"),
    apiGet<GuildOption[]>("/api/me/guilds"),
  ]);

  if (!me.ok) {
    return null;
  }

  // A failed guild list is not a failed session: the user pages that need no guild still work, and
  // the ones that do will say they have no server rather than 500.
  const list: GuildOption[] = guilds.ok ? guilds.data : [];

  const tier: Tier = me.data.isAdmin ? "bot" : list.some((g) => g.canManage) ? "guild" : "user";

  return { userId: me.data.userId, tier, guilds: list, expiresAt: me.data.expiresAt };
}

/**
 * Loads a page's context and enforces its tier.
 *
 * A visitor who does not reach `needs` is sent to their own panel root rather than shown a refusal:
 * they got here by typing a URL or following a stale link, and the honest answer to "you cannot see
 * this" is the page they can see. The API refuses the data independently, so this is the second
 * lock, not the only one.
 */
export async function panel(path: string, query?: Query): Promise<Panel> {
  const page: Page | undefined = PAGES.find((p) => p.path === path);
  const needs: Tier = page?.tier ?? "bot";

  const empty: Query = Promise.resolve({});

  const [me, t, params] = await Promise.all([session(), serverTranslator(), query ?? empty]);

  if (me === null) {
    redirect("/login");
  }

  if (!reaches(me.tier, needs)) {
    redirect("/");
  }

  // Guild-tier pages must land on a server the visitor actually manages; being a member of one is
  // not an answer to "which server's settings am I editing".
  const guild = pickGuild(me.guilds, one(params.guild), needs === "guild");

  if (needs === "guild" && guild === null) {
    redirect("/");
  }

  return { session: me, guild, t };
}
