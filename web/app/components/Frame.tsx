import Link from "next/link";

import { PAGES, type GuildOption, type Session } from "../../lib/pages";
import type { StringKey, Translate } from "../../lib/strings";

import { GuildPicker } from "./GuildPicker";
import { Rail } from "./Rail";

/**
 * The shell every panel page renders inside: header, the sliding stage, the rail. A fixed grid, so
 * the rail never scrolls away — navigation you have to scroll to reach is a menu you have hidden.
 *
 * Server component: it takes the session a page already fetched rather than fetching again.
 */
export function Frame({
  session,
  current,
  guild,
  t,
  children,
}: {
  session: Session;
  /** The current page's `path` from `PAGES`. */
  current: string;
  /** The guild this page is about, if any — carried into every guild-scoped link. */
  guild: GuildOption | null;
  t: Translate;
  children: React.ReactNode;
}) {
  const tierLabel: StringKey = `tier.${session.tier}`;

  // On a guild page the picker may only offer servers the visitor manages: `panel()` refuses the
  // others and lands them on a different server instead, which reads as the picker ignoring them.
  const scoped = PAGES.find((p) => p.path === current)?.tier === "guild";
  const options = scoped ? session.guilds.filter((g) => g.canManage) : session.guilds;

  return (
    <div className="frame">
      <a className="skip" href="#content">
        {t("nav.skip")}
      </a>

      <header className="header">
        <div className="brand">
          <span>{t("app.name")}</span>
          <span className="tier">{t(tierLabel)}</span>
        </div>

        <div className="header-end">
          {options.length > 1 ? (
            <GuildPicker
              guilds={options}
              current={guild?.guildId}
              page={current}
              label={t("server.pick")}
              hint={t("server.pickHint")}
            />
          ) : null}
          <Link className="btn" href="/logout" prefetch={false}>
            {t("nav.logOut")}
          </Link>
        </div>
      </header>

      <main className="stage" id="content">
        <div className="sheet slide">{children}</div>
      </main>

      <Rail tier={session.tier} current={current} guildId={guild?.guildId} t={t} />
    </div>
  );
}

/**
 * A page's opening block. The lead sentence is the answer to "how does the user know what to do
 * here", so it is a required prop rather than an optional one.
 */
export function PageHead({
  label,
  title,
  lead,
}: {
  /** The small uppercase line above the title — which panel this page belongs to. */
  label: string;
  title: string;
  lead: string;
}) {
  return (
    <div className="page-head">
      <div className="page-label mono">{label}</div>
      <h1>{title}</h1>
      <p className="page-lead">{lead}</p>
    </div>
  );
}
