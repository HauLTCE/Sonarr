import type { Locale } from "@/lib/strings";
import { translator } from "@/lib/strings";

/** The read worked and the user shares no server with Sonarr — not an error, just nothing to show. */
export function NoGuilds({ locale }: { locale: Locale }) {
  return <p className="notice">{translator(locale)("guild.none")}</p>;
}
