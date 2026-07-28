import type { Translate } from "./strings";

/**
 * The mood mode ids the accent has colours for (persona/sonarr.yaml → modes).
 *
 * Here rather than in MoodAccent.tsx because the labels have to be translated on the server, and
 * every export of a `"use client"` module is a client reference — calling one during a server
 * render fails. The colours stay in the client component; only the words cross.
 */
export const moodIds = [
  "SEETHING",
  "ANNOYED",
  "BORED",
  "SMUG",
  "PLAYFUL",
  "FOND",
  "NEUTRAL",
] as const;

/** Mode id → the word for it in the active locale. */
export function moodNames(t: Translate): Record<string, string> {
  return Object.fromEntries(moodIds.map((id) => [id, t(`mood.${id}`)]));
}
