"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

/** A nav entry with its label already translated — labels cross the boundary as text, not keys. */
export type NavItem = { href: string; label: string };

/**
 * One labelled nav group.
 *
 * A client component only because `aria-current` needs the active path, and a server layout has no
 * pathname. Nothing else about the shell is interactive.
 */
export function Nav({ label, items }: { label: string; items: NavItem[] }) {
  const path = usePathname();

  return (
    <nav aria-label={label}>
      {items.map((item) => (
        <Link
          key={item.href}
          href={item.href}
          className="tab"
          // Exact match, not prefix: /admin would otherwise light up on every admin page.
          aria-current={path === item.href ? "page" : undefined}
        >
          {item.label}
        </Link>
      ))}
    </nav>
  );
}
