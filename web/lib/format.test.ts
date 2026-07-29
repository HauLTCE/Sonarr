import assert from "node:assert/strict";
import { test } from "node:test";

import { day, hour, when } from "./format.ts";
import { locales } from "./strings.ts";

/**
 * These exist because of one bug: `hour()` mixed `dateStyle` with `hour`, which `Intl` rejects with
 * `TypeError: Invalid option : option`. Nothing caught it — the formatter is only constructed when a
 * row renders, so `tsc` and `eslint` both passed and the Stats page 500'd in production while the
 * API behind it returned 200.
 *
 * So the point of every case below is *construction*, not wording: they call each formatter for real,
 * in every locale, and a throw is the failure. Output shape is deliberately not asserted — Intl's
 * exact spacing and separators are ICU's business and change between Node versions, and a test that
 * pins them would be rewritten on every upgrade instead of catching the next bad option.
 */

const formatters = { when, day, hour } as const;

// A timestamp, a DateOnly, and a bare hour bucket: the three shapes the API actually returns.
const values = ["2026-07-29T14:00:00+00:00", "2026-07-29", "2026-07-29T00:00:00Z"];

for (const [name, format] of Object.entries(formatters)) {
  test(`${name} builds its Intl formatter in every locale`, () => {
    for (const locale of locales) {
      for (const value of values) {
        const out = format(locale, value);

        assert.equal(typeof out, "string");
        assert.notEqual(out, "", `${name}(${locale}, ${value}) returned an empty string`);
      }
    }
  });

  test(`${name} returns the em dash for missing and unparseable input`, () => {
    for (const locale of locales) {
      // null/undefined/"" are the real cases: the API returns null for firstSeenAt on a member it
      // has never seen. "not a date" guards the Number.isNaN branch, which is the only other way
      // these can reach `.format()` with something it cannot render.
      for (const bad of [null, undefined, "", "not a date"]) {
        assert.equal(format(locale, bad), "—", `${name}(${locale}, ${String(bad)})`);
      }
    }
  });
}

test("hour keeps the UTC label its own comment promises", () => {
  // The pages render server-side, so the browser's zone never reaches us and the label is the only
  // thing telling a reader which zone they are looking at. Dropping it would silently show one
  // timezone's numbers under another's name.
  assert.match(hour("en", "2026-07-29T14:00:00+00:00"), /UTC$/);
  assert.match(when("en", "2026-07-29T14:00:00+00:00"), /UTC$/);
});
