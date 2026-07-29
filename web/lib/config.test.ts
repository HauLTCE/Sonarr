import assert from "node:assert/strict";
import { test } from "node:test";

import {
  configFields,
  formatPercent,
  parseWeights,
  serializeWeights,
  toPercent,
  type Directory,
} from "./config.ts";
import { translator } from "./strings.ts";

/**
 * These cover the two bugs this file was changed for, both of which were invisible:
 *
 *   1. `configFields` guessed a `kind` for any key the API had not sent, defaulting to ChannelId. So
 *      `dj_role` on a guild that had never set it rendered a channel picker, and picking from it
 *      saved a channel id into a role setting — accepted by the API, because RoleId validation only
 *      checks that the value is a snowflake. No error anywhere, at any layer.
 *   2. The weights field was the raw `channelId:percent` string, so nothing normalized what a human
 *      typed, and the panel's idea of which pairs count could differ from the bot's.
 *
 * The percentage cases are the interesting half: they pin the fraction-vs-percent split, which is a
 * judgement call and the one thing here a reader would otherwise have to infer from the code.
 */

const t = translator("en");

test("toPercent reads a whole number as a percent and a decimal as a fraction", () => {
  // The stated contract: `0.7` is 70%, `70` is already 70%. `1` is deliberately 1%, not 100% —
  // guessing "100%" there would silently un-mute a channel someone meant to nearly silence.
  assert.equal(toPercent("0.7"), 70);
  assert.equal(toPercent("70"), 70);
  assert.equal(toPercent("1.5"), 150);
  assert.equal(toPercent("1"), 1);
  assert.equal(toPercent("0"), 0);
  assert.equal(toPercent(0.25), 25);
});

test("toPercent reads back its own output", () => {
  // The editor's field always shows a "%", so a row nobody edited still goes through here on blur.
  // Without the strip, `Number("70%")` is NaN and the row reverts — which reads as the field
  // rejecting a value it had just displayed itself.
  for (const percent of [0, 1, 70, 100, 500]) {
    assert.equal(toPercent(formatPercent(percent)), percent);
  }

  assert.equal(toPercent("70 %"), 70);
  assert.equal(toPercent(" 0.7% "), 70);
});

test("toPercent refuses what is not a number", () => {
  for (const bad of [null, undefined, "", "   ", "abc", "7%0", "%", "1e", "NaN"]) {
    assert.equal(toPercent(bad), null, `toPercent(${String(bad)})`);
  }

  // Negative is not a weight. It parses as a number, which is exactly why it needs its own case.
  assert.equal(toPercent("-5"), null);
  assert.equal(toPercent(-0.5), null);
});

test("toPercent rounds rather than truncating", () => {
  assert.equal(toPercent("0.755"), 76);
  assert.equal(toPercent("0.754"), 75);
});

const WEIGHT_BOUNDS = { minimum: 0, maximum: 500 };

test("parseWeights reads the stored pairs", () => {
  assert.deepEqual(parseWeights("123:150,456:0", WEIGHT_BOUNDS), [
    { id: "123", percent: 150 },
    { id: "456", percent: 0 },
  ]);

  assert.deepEqual(parseWeights(null, WEIGHT_BOUNDS), []);
  assert.deepEqual(parseWeights("", WEIGHT_BOUNDS), []);
  assert.deepEqual(parseWeights("   ", WEIGHT_BOUNDS), []);
});

test("parseWeights drops exactly what the bot drops", () => {
  // LevelsConfigKeys.ParseWeights skips a pair it cannot read and one outside 0-500. A row shown
  // here that the bot ignores would be the page promising a weight nothing applies.
  assert.deepEqual(parseWeights("123:600", WEIGHT_BOUNDS), []);
  assert.deepEqual(parseWeights("123:-5", WEIGHT_BOUNDS), []);
  assert.deepEqual(parseWeights("nope:100", WEIGHT_BOUNDS), []);
  assert.deepEqual(parseWeights("123", WEIGHT_BOUNDS), []);
  assert.deepEqual(parseWeights("123:", WEIGHT_BOUNDS), []);

  // A good pair beside a bad one still survives, same as the bot's loop.
  assert.deepEqual(parseWeights("123:600,456:50", WEIGHT_BOUNDS), [{ id: "456", percent: 50 }]);

  // Last wins on a repeat, matching the bot's dictionary assignment.
  assert.deepEqual(parseWeights("123:50,123:80", WEIGHT_BOUNDS), [{ id: "123", percent: 80 }]);
});

test("serializeWeights round-trips, and empty means cleared", () => {
  const rows = [
    { id: "123", percent: 150 },
    { id: "456", percent: 0 },
  ];

  assert.equal(serializeWeights(rows), "123:150,456:0");
  assert.deepEqual(parseWeights(serializeWeights(rows), WEIGHT_BOUNDS), rows);

  // null, not "": the API reads a null value as "clear this key", which is what removing the last
  // row has to mean. An empty string would be a validation failure instead.
  assert.equal(serializeWeights([]), null);
});

test("configFields takes the kind from the API and never guesses one", () => {
  const directory: Directory = {
    channels: [{ id: "1", name: "general" }],
    roles: [{ id: "2", name: "DJ" }],
  };

  // dj_role with no stored value: the bug was that this row's kind was invented here, and the guess
  // was ChannelId. It must be a role picker even when the guild has never set it.
  const fields = configFields(
    {
      dj_role: { value: null, kind: "RoleId" },
      log_channel: { value: "1", kind: "ChannelId" },
      xp_channel_weights: { value: null, kind: "ChannelWeights", minimum: 0, maximum: 500 },
    },
    t,
    directory,
  );

  const dj = fields.find((f) => f.key === "dj_role")!;
  assert.equal(dj.kind, "RoleId");
  assert.deepEqual(dj.options, directory!.roles);
  assert.equal(dj.sigil, "@");

  const log = fields.find((f) => f.key === "log_channel")!;
  assert.deepEqual(log.options, directory!.channels);
  assert.equal(log.sigil, "#");

  // The weights row gets the channel list and the bounds, and is not a picker.
  const weights = fields.find((f) => f.key === "xp_channel_weights")!;
  assert.equal(weights.options, undefined);
  assert.deepEqual(weights.channels, directory!.channels);
  assert.equal(weights.minimum, 0);
  assert.equal(weights.maximum, 500);
});

test("configFields renders only keys the API vouched for, in catalog order", () => {
  const fields = configFields(
    {
      // Out of order on purpose, plus one key this panel has no wording for.
      xp_decay: { value: "true", kind: "Boolean" },
      log_channel: { value: null, kind: "ChannelId" },
      brand_new_key: { value: "x", kind: "Integer" },
    },
    t,
  );

  assert.deepEqual(
    fields.map((f) => f.key),
    ["log_channel", "xp_decay", "brand_new_key"],
  );

  // An unknown key still renders, under its own id and with the "not known yet" hint — adding one
  // to the domain must never make it invisible here.
  const unknown = fields.at(-1)!;
  assert.equal(unknown.label, "brand_new_key");
  assert.equal(unknown.hint, t("behaviour.unknown"));

  // A key in CONFIG_ORDER that the API did not send gets no row at all, rather than a row with an
  // invented kind. Visible if it ever happens, unlike the guess it replaced.
  assert.equal(
    fields.some((f) => f.key === "dj_role"),
    false,
  );
});

test("configFields falls back to text boxes when the directory failed", () => {
  const fields = configFields(
    {
      log_channel: { value: "1", kind: "ChannelId" },
      xp_channel_weights: { value: "1:50", kind: "ChannelWeights", minimum: 0, maximum: 500 },
    },
    t,
    null,
  );

  // Losing the names must not lose the ability to change a setting: both go back to a text field
  // that still takes a pasted id, and the weights editor needs the list to exist at all.
  for (const field of fields) {
    assert.equal(field.options, undefined, field.key);
    assert.equal(field.channels, undefined, field.key);
  }
});
