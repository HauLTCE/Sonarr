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
]);

export default eslintConfig;
