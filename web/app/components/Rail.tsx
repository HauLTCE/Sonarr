"use client";

import Link from "next/link";
import { useEffect } from "react";
import { useRouter } from "next/navigation";

import { href, pagesFor, type Page, type Tier } from "../../lib/pages";
import type { Translate } from "../../lib/strings";

/**
 * The rail: position indicator and navigation in one strip, a segment per page the visitor can
 * reach. Real `<a>` elements, so it works with a keyboard, a screen reader and a middle click, and
 * so every page stays reachable with JavaScript off.
 *
 * A visitor's rail only ever contains their own pages — there is no greyed-out segment hinting at a
 * page they cannot open. Absence, not disablement.
 */
export function Rail({
  tier,
  current,
  guildId,
  t,
}: {
  tier: Tier;
  /** The `path` of the page being shown, e.g. `"memory"` or `""` for the root. */
  current: string;
  guildId: string | undefined;
  t: Translate;
}) {
  const pages = pagesFor(tier);
  const at = pages.findIndex((p) => p.path === current);
  const router = useRouter();

  const prev: Page | undefined = at > 0 ? pages[at - 1] : undefined;
  const next: Page | undefined = at >= 0 ? pages[at + 1] : undefined;

  // Arrow keys move between pages, which is what a deck-shaped layout implies. Prefetched rather
  // than pushed on keydown alone so the slide starts instantly; the click path is identical.
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
        return;
      }

      // Never steal an arrow key from something the user is typing or scrubbing in.
      const target = event.target as HTMLElement | null;
      if (target?.closest("input, select, textarea, [contenteditable='true']")) {
        return;
      }

      const wanted =
        event.key === "ArrowRight" ? next : event.key === "ArrowLeft" ? prev : undefined;

      if (wanted) {
        event.preventDefault();
        router.push(href(wanted, guildId));
      }
    }

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [router, prev, next, guildId]);

  return (
    <nav className="rail" aria-label={t("nav.pages")}>
      <div className="rail-inner">
        <span className="seg-label">{at >= 0 ? t(pages[at].label) : ""}</span>

        <div className="segs">
          {pages.map((page) => (
            <Link
              key={page.path}
              href={href(page, guildId)}
              className="seg"
              aria-current={page.path === current ? "page" : undefined}
              aria-label={t("nav.goTo", { page: t(page.label) })}
              title={t(page.label)}
            />
          ))}
        </div>

        <span className="counter mono">
          {at >= 0 ? t("nav.of", { n: at + 1, total: pages.length }) : ""}
        </span>
      </div>
    </nav>
  );
}
