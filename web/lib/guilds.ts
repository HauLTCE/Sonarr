import { apiGet, type ApiFailure, type ApiResult } from "./api";
import { currentLocale } from "./locale";
import type { Locale } from "./strings";

export type MeResponse = { userId: string; isAdmin: boolean; expiresAt: string };

export type GuildOption = { guildId: string; name: string };

export function me(): Promise<ApiResult<MeResponse>> {
  return apiGet<MeResponse>("/api/me");
}

export function guilds(): Promise<ApiResult<GuildOption[]>> {
  return apiGet<GuildOption[]>("/api/me/guilds");
}

/**
 * Which server a guild-scoped page is looking at: the `guild` query parameter, else the first one
 * the user is in.
 *
 * A query parameter rather than a cookie, so the picker is a plain GET form that works without
 * JavaScript and a link to "my levels on that server" is shareable.
 *
 * The value is checked against the user's own guild list rather than passed through — the API
 * would answer for any guild id a visitor typed, and a panel that renders someone else's server
 * because the URL said so is not a thing to leave to the backend alone.
 */
export function selectedGuild(
  options: GuildOption[],
  requested: string | undefined,
): GuildOption | null {
  if (options.length === 0) {
    return null;
  }

  return options.find((g) => g.guildId === requested) ?? options[0];
}

/** First value when a search param arrives repeated (`?guild=1&guild=2`). */
export function one(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/** What Next hands a page as `searchParams`. */
export type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export type GuildScope =
  | { ok: true; locale: Locale; options: GuildOption[]; guild: GuildOption }
  | { ok: false; locale: Locale; failure: ApiFailure }
  | { ok: false; locale: Locale; failure: null };

/**
 * The three lines every guild-scoped page starts with: locale, the user's guilds, and which one the
 * URL asked for. `failure: null` means the read worked and the user is in no guilds at all.
 */
export async function guildScope(searchParams: SearchParams): Promise<GuildScope> {
  const [locale, list, params] = await Promise.all([
    currentLocale(),
    guilds(),
    searchParams,
  ]);

  if (!list.ok) {
    return { ok: false, locale, failure: list.failure };
  }

  const guild = selectedGuild(list.data, one(params.guild));

  return guild
    ? { ok: true, locale, options: list.data, guild }
    : { ok: false, locale, failure: null };
}
