import { cookies } from "next/headers";

import {
  defaultLocale,
  isLocale,
  type Locale,
  type StringKey,
  type Translate,
  translator,
} from "./strings";

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

/**
 * A page's `<title>`, localised, for `export const generateMetadata = () => pageTitle("nav.memory")`.
 *
 * The root layout holds the `"%s · Sonarr"` template, so this returns the bare page name. It takes a
 * `StringKey` rather than a string so a page cannot hard-code English into its title — the same rule
 * the rest of the panel follows — and the keys it wants already exist as the navbar's tab labels.
 */
export async function pageTitle(key: StringKey): Promise<{ title: string }> {
  const t = await serverTranslator();

  return { title: t(key) };
}
