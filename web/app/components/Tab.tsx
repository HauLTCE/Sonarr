"use client";

import Link, { useLinkStatus } from "next/link";

/**
 * A navbar tab that admits it is working.
 *
 * Every page here is a server component reading the API with `cache: "no-store"`, so a tab click is a
 * round trip to Kestrel before anything on screen changes. There was no feedback at all: the old tab
 * stayed marked, the new one stayed unmarked, and on a slow read the only honest reading of the screen
 * was that the click had missed. People click again, which starts a second navigation.
 *
 * `useLinkStatus` is Next's own answer and it has to be called from a descendant of the `<Link>`,
 * which is the whole reason this component exists. No spinner library, no router-event subscription,
 * no global loading store — the platform already tracks this and nothing else needs to know.
 *
 * The dot is `aria-hidden`: Next announces route changes to assistive tech itself, so a second
 * announcement here would be one interruption per click for no new information.
 */
function Pending() {
  const { pending } = useLinkStatus();

  return pending ? <span className="tab-pending" aria-hidden="true" /> : null;
}

export function Tab({
  className,
  href,
  current,
  children,
}: {
  className: string;
  href: string;
  /** What to put in `aria-current` — "page" for a page tab, "true" for a whole section. */
  current: "page" | "true" | undefined;
  children: React.ReactNode;
}) {
  return (
    <Link className={className} href={href} aria-current={current}>
      {children}
      <Pending />
    </Link>
  );
}
