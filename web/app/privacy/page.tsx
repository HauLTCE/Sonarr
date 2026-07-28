import { apiGet } from "../../lib/api";
import { count } from "../../lib/format";
import { currentLocale } from "../../lib/locale";
import { panel, type Query } from "../../lib/panel";
import type { StringKey } from "../../lib/strings";

import { Frame, PageHead } from "../components/Frame";
import { DangerButtons } from "./DangerButtons";

/** Only the counts are read from the export payload; the rest of it is what the download is for. */
type Data = {
  counts: {
    chatEpisodes: number;
    relationshipEvents: number;
    tracksPlayed: number;
    trackRatings: number;
    savedQuotes: number;
    reminders: number;
    capsules: number;
    modCases: number;
  };
};

/** Rendered in this order, which is roughly most-of-it to least. */
const AREAS: readonly (keyof Data["counts"])[] = [
  "chatEpisodes",
  "relationshipEvents",
  "tracksPlayed",
  "trackRatings",
  "savedQuotes",
  "reminders",
  "capsules",
  "modCases",
];

/**
 * Export and delete. The only page whose buttons cannot be undone, so every one of them says what
 * it removes and what it leaves before it is pressed, and the two deletes need the word typed.
 *
 * It opens with the row counts, because "type DELETE to erase everything" is not a decision anyone
 * can make without being told what everything is.
 */
export default async function PrivacyPage({ searchParams }: { searchParams: Query }) {
  const { session, guild, t } = await panel("privacy", searchParams);
  const locale = await currentLocale();

  // The full export payload for eight numbers. It is one read on a page nobody opens twice a day,
  // and the alternative is a counts-only endpoint that would duplicate the query behind this one.
  const data = await apiGet<Data>("/api/me/data");

  return (
    <Frame session={session} current="privacy" guild={guild} t={t}>
      <PageHead label={t("tier.user")} title={t("privacy.title")} lead={t("privacy.lead")} />

      {data.ok ? (
        <section className="card">
          <div className="card-head">
            <h2>{t("privacy.holdings")}</h2>
          </div>
          <p className="hint">{t("privacy.holdingsHint")}</p>

          <div className="grid">
            {AREAS.map((area) => (
              <div className="stat" key={area}>
                <div className="stat-label">{t(`privacy.area.${area}` as StringKey)}</div>
                <div className="stat-value mono">{count(locale, data.data.counts[area])}</div>
              </div>
            ))}
          </div>
        </section>
      ) : null}

      <section className="card">
        <div className="card-head">
          <h2>{t("privacy.export")}</h2>
        </div>
        {/* A plain link: the API answers with a file, so the browser's own download is the whole
            feature and there is nothing for JavaScript to add. */}
        <a className="btn" href="/api/me/export" download>
          {t("privacy.export")}
        </a>
        <p className="hint">{t("privacy.exportHint")}</p>
      </section>

      <DangerButtons
        labels={{
          forgetChat: t("privacy.forgetChat"),
          forgetChatHint: t("privacy.forgetChatHint"),
          forgetAll: t("privacy.forgetAll"),
          forgetAllHint: t("privacy.forgetAllHint"),
          confirmLabel: t("privacy.confirmLabel"),
          confirmHint: t("privacy.confirmHint"),
          confirmWrong: t("privacy.confirmWrong"),
          logoutAll: t("privacy.logoutAll"),
          logoutAllHint: t("privacy.logoutAllHint"),
          working: t("privacy.working"),
          failed: t("privacy.failed"),
          backupNote: t("privacy.backupNote"),
          // A function rather than a string: the counts are only known after the delete returns,
          // and the client component has no dictionary to interpolate with.
          deleted: (n, total) => t("privacy.deleted", { n, total }),
        }}
      />
    </Frame>
  );
}
