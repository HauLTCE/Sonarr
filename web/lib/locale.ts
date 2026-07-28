import { cookies } from "next/headers";

import { defaultLocale, isLocale, type Locale, type Translate, translator } from "./strings";

/** Cookie the locale switcher writes. No negotiation from Accept-Language: an explicit choice only. */
export const localeCookie = "sonarr_locale";

/**
 * The active locale for a server component. Reads the cookie, falls back to English.
 */
export async function currentLocale(): Promise<Locale> {
  const store = await cookies();
  const value = store.get(localeCookie)?.value;

  return isLocale(value) ? value : defaultLocale;
}

/** Bound `t` for a server component — one call instead of two at every page top. */
export async function serverTranslator(): Promise<Translate> {
  return translator(await currentLocale());
}
