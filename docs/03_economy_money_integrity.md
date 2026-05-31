# 03 — The Economy & The Money-Integrity Saga

The economy lives in two places: `utils/economy_helpers.py` (the data
primitives) and `cogs/economy.py` (the commands — daily, work, rob, pay,
deposit, withdraw, balance, leaderboard). Currency comes in three flavors:
**wallet** (spendable, risky), **bank** (safe, capped), and **gems** (premium).

## The bug that made money out of nothing

This is the most instructive story in the repo, because it's a textbook
**TOCTOU** (time-of-check-to-time-of-use) race, and the fix is genuinely the
right one.

The original spend pattern everywhere was:

```python
bal = get_balance(user_id)          # 1. READ
if bal["wallet"] < cost:            # 2. CHECK
    return "too poor"
update_wallet(user_id, -cost)       # 3. DEDUCT
```

Looks fine. It isn't. `update_wallet` for a negative amount does:

```python
UPDATE economy SET wallet = MAX(0, wallet + ?) WHERE user_id = ?
```

That `MAX(0, ...)` clamp is the trap. It means a deduction **always
"succeeds"** — if you don't have enough, it just floors you at 0 instead of
failing. Combine that with the read-check-deduct gap and a user firing two
purchases concurrently (easy, via the `&&` chainer or just fast clicking):
both reads see the full balance, both checks pass, both deduct, and the clamp
hides the overdraft. You bought two things for the price of less-than-two.

## The fix: atomic conditional spends

`spend_wallet` and `spend_gems` ([economy_helpers.py:56](../utils/economy_helpers.py#L56))
collapse check-and-deduct into one atomic SQL statement:

```python
UPDATE economy SET wallet = wallet - ?
WHERE user_id = ? AND wallet >= ?     # the guard
# then: return cursor.rowcount > 0    # did it actually happen?
```

The `WHERE ... AND wallet >= ?` guard plus the `rowcount` check means the
deduction either happens completely or not at all, and you *know which*. No
gap, no clamp hiding a failure. This is the correct primitive for a purchase,
and `transfer_coins` was rebuilt on top of it: deduct the sender first, only
credit the recipient if that deduction actually happened — so a failure can
never create or destroy coins.

Every spend site (bank upgrades, `pay`, `deposit`, `withdraw`, shop tiers,
titles, consumables) was migrated to these. `update_wallet` still exists for
*adding* money and for refunds, where the clamp is harmless.

## The lingering caveat

The atomicity here is at the SQL-statement level on a **single shared
SQLite connection** (see `06_the_database.md`). It closes the logical race,
but it leans on SQLite serializing writes rather than on real transactions
with row locks. For this bot's scale (one small server) that's fine. At scale
it would not be, and the "Postgres HA mode" that was supposed to address that
is vaporware (also `06`).

## Smaller economy notes

- `calculate_cashout` ([economy_helpers.py:148](../utils/economy_helpers.py#L148))
  uses a logarithmic per-level payout (`3 + 2.5*ln(i+1)`). Diminishing
  returns baked in — a reasonable anti-inflation choice.
- `CashoutView` had a double-payable bug (you could click cash out twice
  before it disabled). A `done` re-entry guard fixed it.
- `ensure_account` is called at the top of nearly every helper. It's a few
  extra reads, but it means no command ever crashes on a missing row. Cheap
  insurance, used consistently — a good habit.
- Stats columns (`total_earned`, `total_gambled`, `total_lost`) are updated
  inline with the money moves, so the leaderboard and gamble stats stay
  roughly honest without a separate accounting pass.
