"use client";

import Link from "next/link";
import { useEffect } from "react";
import { useRouter } from "next/navigation";

import { href, pagesFor, type Page, type Tier } from "../../lib/pages";

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
  labels,
  pagesLabel,
  goTo,
  position,
}: {
  tier: Tier;
  /** The `path` of the page being shown, e.g. `"memory"` or `""` for the root. */
  current: string;
  guildId: string | undefined;
  /**
   * Resolved strings rather than a `Translate`. This is a client component, so a function prop is a
   * render-time crash ("Functions cannot be passed directly to Client Components") — the translator
   * has to be called on the server and its answers passed as data.
   */
  labels: Record<string, string>;
  pagesLabel: string;
  /** `path` → "Go to <page>". */
  goTo: Record<string, string>;
  /** `path` → "3 of 12". */
  position: Record<string, string>;
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
    <nav className="rail" aria-label={pagesLabel}>
      <div className="rail-inner">
        <span className="seg-label">{at >= 0 ? labels[current] : ""}</span>

        <div className="segs">
          {pages.map((page) => (
            <Link
              key={page.path}
              href={href(page, guildId)}
              className="seg"
              aria-current={page.path === current ? "page" : undefined}
              aria-label={goTo[page.path]}
              title={labels[page.path]}
            />
          ))}
        </div>

        <span className="counter mono">{at >= 0 ? position[current] : ""}</span>
      </div>
    </nav>
  );
}
