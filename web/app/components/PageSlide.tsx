"use client";

import { usePathname } from "next/navigation";
import { useState } from "react";

/**
 * The sliding page transition.
 *
 * The new page enters from the side the visitor moved toward: forward through the nav slides in
 * from the right, back from the left. Direction comes from the nav's own order — the same array
 * that draws the tabs — so it matches the underline sliding along the bar rather than guessing
 * from history, which cannot tell a click from a browser Back.
 *
 * Re-keying on the pathname is what replays the animation; there is no timer and no exit
 * animation, so a fast clicker never sees a half-faded page waiting on a `setTimeout`.
 * `prefers-reduced-motion` collapses it to an instant swap in globals.css.
 */
export function PageSlide({ order, children }: { order: string[]; children: React.ReactNode }) {
  const path = usePathname();

  // React's "adjust state when a prop changes" pattern rather than a ref: the direction has to be
  // known in the very render that swaps the key, and a ref written during render is neither
  // allowed nor visible to that render.
  const [seen, setSeen] = useState(path);
  const [back, setBack] = useState(false);

  if (seen !== path) {
    const from = order.indexOf(seen);
    const to = order.indexOf(path);

    // Unknown routes (a page not in the nav) slide forward: that direction reads as "deeper in",
    // which is what following a link off a page usually is.
    setBack(from >= 0 && to >= 0 && to < from);
    setSeen(path);
  }

  return (
    <div key={path} className="page" data-dir={back ? "back" : "forward"}>
      <div className="page-inner">{children}</div>
    </div>
  );
}
