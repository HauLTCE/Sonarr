import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
  {
    /**
     * Accessibility, at error rather than warning.
     *
     * eslint-config-next turns six jsx-a11y rules on as warnings. `npm run lint` does not fail on a
     * warning, so CI stayed green through every accessibility mistake found by hand in this tree —
     * a chart that announced as twenty-four empty list items, two pages with no `main`, a select
     * whose only explanation was a `title`. A warning nobody reads is not a guard.
     *
     * Every rule below passes today, so this costs nothing now and fails the build the next time
     * something regresses. Listed one per line rather than pulling in the plugin's `recommended`
     * preset: that preset moves under us on a minor bump, and a rule that appears on an npm update
     * and breaks the build is how a guard gets switched off wholesale.
     *
     * What this cannot check is everything that mattered most here: whether a hidden string is the
     * right string, whether contrast passes, whether the reading order makes sense. Those stay in
     * persona/LIMITS.md's territory — read by a person, not a linter.
     */
    rules: {
      // The six from core-web-vitals, promoted from warning.
      "jsx-a11y/alt-text": "error",
      "jsx-a11y/aria-props": "error",
      "jsx-a11y/aria-proptypes": "error",
      "jsx-a11y/aria-unsupported-elements": "error",
      "jsx-a11y/role-has-required-aria-props": "error",
      "jsx-a11y/role-supports-aria-props": "error",

      // Not in the Next preset at all.
      "jsx-a11y/anchor-has-content": "error",
      "jsx-a11y/anchor-is-valid": "error",
      "jsx-a11y/aria-role": "error",
      "jsx-a11y/click-events-have-key-events": "error",
      "jsx-a11y/heading-has-content": "error",
      "jsx-a11y/html-has-lang": "error",
      "jsx-a11y/label-has-associated-control": "error",
      "jsx-a11y/no-noninteractive-element-interactions": "error",
      "jsx-a11y/no-redundant-roles": "error",
      "jsx-a11y/no-static-element-interactions": "error",
      "jsx-a11y/tabindex-no-positive": "error",
    },
  },
  {
    /**
     * No page hard-codes English.
     *
     * `lib/strings.ts` opens by stating this as the rule it exists to enforce, and until now nothing
     * enforced it — the tree happened to comply because it was written that way, which lasts exactly
     * until someone types a word into JSX. Adding Vietnamese should mean filling in one object, not
     * hunting the tree for strings.
     *
     * `ignoreProps` because a prop is as likely to be a className or an href as user-visible text,
     * and the ones that *are* text (`<PageHead title=…>`) already take a translated string; catching
     * those would need a rule that knows which props render.
     *
     * The three allowed strings are separators between values that are already translated — "12 · 4",
     * "3/9", "14:00". They are punctuation, not wording: there is no Vietnamese for "·". A "#" is not
     * on this list on purpose. It looks like punctuation and is not — it is a Western ordinal
     * convention, Vietnamese writes "hạng 4" — and this rule is what caught it sitting in
     * app/activity/page.tsx.
     */
    files: ["**/*.tsx"],
    rules: {
      "react/jsx-no-literals": [
        "error",
        { noStrings: true, ignoreProps: true, allowedStrings: ["·", "/", ":"] },
      ],
    },
  },
]);

export default eslintConfig;
