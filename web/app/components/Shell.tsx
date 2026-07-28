import Link from "next/link";

import { moodNames } from "@/lib/mood";
import type { Locale, StringKey } from "@/lib/strings";
import { translator } from "@/lib/strings";

import { LogoutButton } from "./LogoutButton";
import { MoodAccent } from "./MoodAccent";
import { Nav } from "./Nav";
import { PageSlide } from "./PageSlide";

/** One nav entry. `href` is a panel route; the label is a string key, never literal text. */
type Tab = { href: string; label: StringKey };

const userTabs: Tab[] = [
  { href: "/", label: "nav.overview" },
  { href: "/my-data", label: "nav.myData" },
  { href: "/sonarr", label: "nav.sonarrAndMe" },
  { href: "/errors", label: "nav.myErrors" },
  { href: "/music", label: "nav.music" },
  { href: "/privacy", label: "nav.privacy" },
];

const adminTabs: Tab[] = [
  { href: "/admin", label: "nav.status" },
  { href: "/admin/config", label: "nav.config" },
  { href: "/admin/flags", label: "nav.flags" },
  { href: "/admin/cases", label: "nav.modLog" },
  { href: "/admin/stats", label: "nav.stats" },
  { href: "/admin/audit", label: "nav.audit" },
];

/**
 * The page frame: skip link, brand, the two nav groups, and the main landmark.
 *
 * Accessibility is load-bearing here rather than decorative — the skip link is the first tab stop,
 * `aria-current` marks the open page for screen readers as well as sighted users, and the two nav
 * groups are labelled so "your panel" and "admin panel" are distinguishable out of context.
 */
export function Shell({
  locale,
  isAdmin,
  children,
}: {
  locale: Locale;
  isAdmin: boolean;
  children: React.ReactNode;
}) {
  const t = translator(locale);
  const label = (tabs: Tab[]) => tabs.map((tab) => ({ href: tab.href, label: t(tab.label) }));

  // The nav's own order is the direction the slide reads from, admin tabs after user tabs — so
  // moving from the last user tab to the first admin tab slides forward, which is how it looks.
  const order = [...userTabs, ...adminTabs].map((tab) => tab.href);

  return (
    <div className="shell">
      <a className="skip" href="#main">
        {t("nav.skipToContent")}
      </a>

      <header className="topbar">
        <Link href="/" className="brand">
          <MoodAccent names={moodNames(t)} label={t("mood.label")} />
          {t("app.name")}
        </Link>

        <Nav label={t("nav.userPanel")} items={label(userTabs)} />

        {isAdmin && <Nav label={t("nav.adminPanel")} items={label(adminTabs)} />}

        <span className="spacer" />

        <LogoutButton label={t("nav.logOut")} />
      </header>

      <main id="main">
        <PageSlide order={order}>{children}</PageSlide>
      </main>

      <footer className="footer">{t("app.tagline")}</footer>
    </div>
  );
}
