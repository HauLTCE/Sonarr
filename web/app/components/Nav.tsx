import Link from "next/link";

import {
  guildQuery,
  href,
  pagesIn,
  sectionHome,
  sectionsFor,
  SECTION_LABEL,
  type GuildOption,
  type Session,
  type Tier,
} from "../../lib/pages";
import type { Translate } from "../../lib/strings";

import { GuildPicker } from "./GuildPicker";

/**
 * The navbar: two rows, and the split between them is the point.
 *
 * Row one is which panel you are in — your own data, a server you run, the bot. Row two is the pages
 * inside that one panel. A visitor who only has the user side never sees a second row of tabs for a
 * panel they cannot open, and an admin looking for a server setting does not scroll past their own XP
 * to find it.
 *
 * Server component, and every tab is a real `<a>`: the whole nav works with JavaScript off, which is
 * what makes the guild picker the only client component here.
 */
export function Nav({
  session,
  current,
  section,
  guild,
  t,
}: {
  session: Session;
  /** The current page's `path` from `PAGES`. */
  current: string;
  /** Which panel the current page belongs to — decides which pages row is shown. */
  section: Tier;
  guild: GuildOption | null;
  t: Translate;
}) {
  const sections = sectionsFor(session.tier);

  // No tier filter needed: `sectionsFor` already dropped the sections this visitor cannot reach, and
  // every page in a section has that section's tier by definition.
  const pages = pagesIn(section);

  // Only the server side is about one guild; on the user side the picker still applies (the overview
  // and memory pages are per-guild), but on the bot side there is nothing to pick.
  const scoped = section !== "bot";
  const managed = session.guilds.filter((g) => g.canManage);
  const options = section === "guild" ? managed : session.guilds;

  /**
   * The guild a section's tab should carry.
   *
   * The two sides do not accept the same servers. Someone can be a member of five servers and an
   * admin of one, and their user pages are about all five — so carrying the current guild into the
   * Server admin tab hands it a server they do not manage. `pickGuild` then quietly substitutes a
   * different one, and the page they get describes a server the URL does not name, which reads as
   * the panel ignoring them.
   *
   * So the server tab carries the current guild only when it qualifies, and their first manageable
   * server otherwise. Undefined is fine — `pickGuild` chooses, and it chooses from the right list.
   */
  const sectionGuild = (s: Tier) =>
    s === "guild" && guild?.canManage !== true ? managed[0]?.guildId : guild?.guildId;

  return (
    <header className="topbar">
      <div className="topbar-row">
        {/* Carries the guild, like every other link here: the root is guild-scoped, so a bare "/"
            would drop the chosen server and land the visitor on their first one instead. */}
        <Link className="brand" href={`/${guildQuery(guild?.guildId)}`}>
          {t("app.name")}
        </Link>

        {/* The sections are the primary nav, so they are a <nav> of their own rather than a list
            inside the pages one: they answer a different question, and a screen reader user landing
            on "Panels" should not have to walk twelve page links to reach the other panel. */}
        {sections.length > 1 ? (
          <nav className="sections" aria-label={t("nav.sections")}>
            {sections.map((s) => (
              <Link
                key={s}
                className="tab"
                href={href(sectionHome(s), sectionGuild(s))}
                // The whole section is current, not just its first page — otherwise opening
                // Behaviour would leave no tab marked and the visitor loses where they are.
                aria-current={s === section ? "true" : undefined}
              >
                {t(SECTION_LABEL[s])}
              </Link>
            ))}
          </nav>
        ) : null}

        <div className="topbar-end">
          {scoped && options.length > 1 ? (
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
      </div>

      {/* One page in a section means the section tab already got you there. */}
      {pages.length > 1 ? (
        <nav
          className="pages"
          aria-label={t("nav.inSection", { section: t(SECTION_LABEL[section]) })}
        >
          {pages.map((p) => (
            <Link
              key={p.path}
              className="page-tab"
              href={href(p, guild?.guildId)}
              aria-current={p.path === current ? "page" : undefined}
            >
              {t(p.label)}
            </Link>
          ))}
        </nav>
      ) : null}
    </header>
  );
}
