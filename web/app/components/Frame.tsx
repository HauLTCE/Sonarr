import { currentLocale } from "../../lib/locale";
import { sectionOf, type GuildOption, type Session } from "../../lib/pages";
import type { Translate } from "../../lib/strings";

import { Footer } from "./Footer";
import { Nav } from "./Nav";

/**
 * The shell every panel page renders inside: navbar, content, footer.
 *
 * A fixed-height stage with its own scrollbar was the wrong shape — it made the content scroll inside
 * a box while the browser's own scrollbar stayed empty, and it existed only to keep the rail glued to
 * the bottom. The document scrolls now and the footer sits after the content, which is what every
 * other site does and therefore what a visitor already knows how to use.
 *
 * Server component: it takes the session a page already fetched rather than fetching again. Async
 * only because the footer formats a timestamp, which needs the locale.
 */
export async function Frame({
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
  const locale = await currentLocale();

  return (
    <div className="frame">
      <a className="skip" href="#content">
        {t("nav.skip")}
      </a>

      {/* Which panel this page belongs to is derived from the page list, not passed in: a page
          cannot then claim to be in a section it is not gated for. */}
      <Nav session={session} current={current} section={sectionOf(current)} guild={guild} t={t} />

      <main className="stage" id="content">
        <div className="sheet">{children}</div>
      </main>

      <Footer session={session} locale={locale} t={t} />
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
