/**
 * Every user-visible string in the panel (docs/09: "i18n-ready strings from day one, Vietnamese
 * later"). No library: one dictionary per locale and a `t()` that reads the active one is what a
 * two-locale app needs, and it costs nothing at runtime.
 *
 * The rule this file exists to enforce: no page hard-codes English. Adding Vietnamese later means
 * adding one object below and a locale cookie — not finding every string in the tree.
 *
 * The second rule it enforces is the wording one. Every page has a `*.lead` saying what the page is
 * for, every empty list says what would fill it, and every control that changes or deletes something
 * has a `*.hint` saying what will happen before it is pressed. A missing key here is a page missing
 * its explanation.
 */

export const locales = ["en", "vi"] as const;

export type Locale = (typeof locales)[number];

export const defaultLocale: Locale = "en";

const en = {
  "app.name": "Sonarr",

  "tier.user": "You",
  "tier.guild": "Server admin",
  "tier.bot": "Bot admin",

  "nav.you": "You",
  "nav.memory": "Memory",
  "nav.music": "Music",
  "nav.activity": "Activity",
  "nav.privacy": "Privacy",
  "nav.server": "Server",
  "nav.behaviour": "Behaviour",
  "nav.moderation": "Moderation",
  "nav.stats": "Stats",
  "nav.health": "Health",
  "nav.errors": "Errors",
  "nav.audit": "Audit",

  "nav.pages": "Pages",
  "nav.of": "{n} of {total}",
  "nav.prev": "Previous",
  "nav.next": "Next",
  "nav.skip": "Skip to content",
  "nav.logOut": "Log out",
  "nav.goTo": "Go to {page}",

  // ---------------------------------------------------------------- login
  "login.title": "Log in",
  "login.lead":
    "Type your Discord handle. Sonarr DMs you a code on Discord — there is no password to set and nothing to remember.",
  "login.handle": "Discord handle",
  "login.handleHint": "Your Discord name, without the @.",
  "login.send": "Send me a code",
  "login.sending": "Sending…",
  "login.code": "Code from the DM",
  "login.codeHint": "8 characters, good for ten minutes. Five wrong tries and you start over.",
  "login.verify": "Log in",
  "login.verifying": "Logging in…",
  "login.remember": "Keep me logged in on this device",
  "login.sent": "If that account is known here, a DM is on its way. Type the code below.",
  "login.noDmTitle": "No DM arrived?",
  "login.noDm":
    "Sonarr can only DM you if you share a server with her and that server allows direct messages from members. In Discord, open the server menu, then Privacy Settings, and turn on direct messages. Then ask for another code.",
  "login.needHandle": "Type your Discord handle first.",
  // One message for every 401, because the API deliberately returns one status for a wrong code, an
  // expired one and an unknown handle — so this says what to check rather than guessing which it was.
  "login.badCode":
    "That did not match. Check the code in the DM, and ask for a new one if it is more than ten minutes old.",
  "login.tooMany": "Too many wrong codes. Ask for a new one.",
  "login.rateLimited": "Too many attempts just now. Wait a few minutes.",
  "login.failed": "That did not work. Try again in a moment.",
  "login.restart": "Use a different handle",

  "logout.title": "Log out",
  "logout.lead":
    "This ends the login on this device only. Your data and your server's settings are untouched, and you can log back in with a new DM code any time.",
  "logout.confirm": "Log out of this device",
  "logout.cancel": "Stay logged in",
  "logout.failed": "Could not log out. Your login is still active — try again.",
  "logout.working": "Logging out…",

  // ---------------------------------------------------------------- you
  "you.title": "You",
  "you.lead":
    "What Sonarr knows about you, and what you can change about it. Nobody else can open this page.",
  "you.who": "Who you are to her",
  "you.nickname": "Nickname she uses",
  "you.mood": "Her mood with you",
  "you.talked": "Turns talked",
  "you.facts": "Things she remembers",
  "you.factsSub": "Manage them on Memory",
  "you.servers": "Servers you share",
  "you.none": "None yet",
  "you.sessionEnds": "This login expires",
  "you.whereNext": "Where to go next",
  "you.railHint":
    "The bar at the bottom is every page you have. Click a segment, or use the left and right arrow keys.",

  // ---------------------------------------------------------------- memory
  "memory.title": "Memory",
  "memory.lead":
    "Facts Sonarr has picked up from talking to you. Remove any of them and she stops bringing it up.",
  "memory.empty":
    "She has not learned anything about you yet. Tell her something in chat — your name, what you like — and it shows up here.",
  "memory.forget": "Forget this",
  "memory.forgetHint": "Removes this one fact now. She can learn it again if you tell her again.",
  "memory.forgetting": "Forgetting…",
  "memory.failed": "Could not forget that. Try again.",

  // ---------------------------------------------------------------- music
  "music.title": "Music",
  "music.lead": "What you have played through her, and what you thought of it.",
  "music.history": "Recently played",
  "music.historyEmpty": "Nothing yet. Queue something with the music commands and it lands here.",
  "music.ratings": "Your ratings",
  "music.ratingsEmpty": "You have not rated anything yet.",
  "music.top": "Top rated in this server",
  "music.topEmpty": "Nobody here has rated a track yet.",
  "music.plays": "{n} plays",
  "music.liked": "Liked",
  "music.disliked": "Disliked",
  "music.votes": "{up} up · {down} down",

  // ---------------------------------------------------------------- activity
  "activity.title": "Activity",
  "activity.lead": "Your level and streak in this server, plus any reminders you have set.",
  "activity.level": "Level",
  "activity.rank": "Rank",
  "activity.xp": "XP",
  "activity.toNext": "{n} XP to level {level}",
  "activity.streak": "Day streak",
  "activity.messages": "Messages",
  "activity.firstSeen": "First seen",
  "activity.reminders": "Your reminders",
  "activity.remindersEmpty": "None set. Use the reminder command and they appear here.",
  "activity.due": "Due {when}",

  // ---------------------------------------------------------------- privacy
  "privacy.title": "Privacy",
  "privacy.lead":
    "Take your data with you, or delete it. These are the only buttons here that cannot be undone, and each one says exactly what it removes.",
  "privacy.holdings": "What she holds about you",
  "privacy.holdingsHint":
    "Row counts, area by area. The download below has the rows themselves; the deletes further down remove them.",
  "privacy.area.chatEpisodes": "Conversations",
  "privacy.area.relationshipEvents": "Relationship events",
  "privacy.area.tracksPlayed": "Tracks played",
  "privacy.area.trackRatings": "Track ratings",
  "privacy.area.savedQuotes": "Saved quotes",
  "privacy.area.reminders": "Reminders",
  "privacy.area.capsules": "Time capsules",
  "privacy.area.modCases": "Moderation cases",
  "privacy.export": "Download everything",
  "privacy.exportHint":
    "A JSON file with every row Sonarr holds about you. Nothing is deleted and nothing changes.",
  "privacy.forgetChat": "Delete what she remembers",
  "privacy.forgetChatHint":
    "Removes the facts, the relationship history and the chat memory. Your levels, music history and moderation record stay. This cannot be undone.",
  "privacy.forgetAll": "Delete everything",
  "privacy.forgetAllHint":
    "Removes every row about you: memory, levels, music, quotes, reminders and logins. Moderation cases stay, because a server needs its own record. This cannot be undone.",
  "privacy.confirmLabel": "Type DELETE to confirm",
  "privacy.confirmHint": "The word in capitals, so this cannot happen by accident.",
  "privacy.confirmWrong": "Type DELETE exactly to continue.",
  "privacy.logoutAll": "Log out everywhere",
  "privacy.logoutAllHint":
    "Ends every login including this one. You will need a new code to get back in.",
  "privacy.deleted": "Done — {n} of {total} rows removed.",
  "privacy.backupNote":
    "Backups are kept for a while, so a copy can survive in one until it rotates out.",
  "privacy.working": "Working…",
  "privacy.failed": "That did not work. Nothing was deleted.",

  // ---------------------------------------------------------------- server (guild tier)
  "server.title": "This server",
  "server.lead":
    "How Sonarr behaves in {guild}, in plain words. You see this because you have Manage Server here.",
  "server.pick": "Server",
  "server.pickHint": "You manage more than one. Pick which one these pages are about.",
  "server.summary": "Right now",
  "server.chatOn": "She talks here",
  "server.chatOff": "She stays quiet",
  "server.levelsOn": "Levels are on",
  "server.levelsOff": "Levels are off",
  "server.musicOn": "Music is on",
  "server.musicOff": "Music is off",
  "server.logChannel": "Cases logged to",
  "server.notSet": "Not set",
  "server.cases": "Moderation cases",
  "server.scopeTitle": "What this covers",
  "server.onlyThis":
    "Everything on these pages is about {guild} only. Other servers you are in are not shown, and other admins cannot see this one.",

  "behaviour.title": "Behaviour",
  "behaviour.lead":
    "Settings for {guild}. Each one says what it changes; a blank field means she uses the default.",
  "behaviour.save": "Save",
  "behaviour.saving": "Saving…",
  "behaviour.saved": "Saved.",
  "behaviour.reset": "Use the default",
  "behaviour.features": "Features",
  "behaviour.featuresLead": "Turn a whole area on or off in this server.",
  "behaviour.on": "On",
  "behaviour.off": "Off",
  "behaviour.sourceDefault": "default",
  "behaviour.sourceGuild": "set here",
  "behaviour.sourceGlobal": "set bot-wide",
  "behaviour.failed": "Could not save that.",
  // One entry per FeatureNames member. Each hint says what turning it off actually stops, because
  // "chat: off" tells an admin nothing about what their members will notice.
  "feat.chat": "Chatting",
  "feat.chat.hint": "Off means she does not reply to messages or mentions here.",
  "feat.music": "Music",
  "feat.music.hint": "Off means the music commands refuse in this server.",
  "feat.levels": "Levels",
  "feat.levels.hint": "Off means no XP is earned and no level-ups are announced.",
  "feat.moderation": "Moderation",
  "feat.moderation.hint": "Off means warn, mute, kick and ban stop working here.",
  "feat.social": "Social",
  "feat.social.hint": "Off means the profile, marry and hug style commands are unavailable.",
  "feat.welcome": "Welcome messages",
  "feat.welcome.hint": "Off means joins and leaves are not announced.",
  "feat.tickets": "Tickets",
  "feat.tickets.hint": "Off means members cannot open support tickets.",
  "feat.events": "Events",
  "feat.events.hint": "Off means scheduled events and giveaways do not run.",
  "feat.reminders": "Reminders",
  "feat.reminders.hint": "Off means reminders cannot be set in this server.",

  "behaviour.values": "Settings",
  "behaviour.valuesLead":
    "A blank field means Sonarr uses her default. Channels and roles take an id — right-click one in Discord and Copy ID.",
  "behaviour.unknown": "Not a setting this panel knows yet.",

  // One entry per key in ConfigKeys.All. Here rather than served from the API so they can be
  // translated; the API's own descriptions are written for Discord autocomplete.
  "cfg.welcome_channel": "Welcome channel",
  "cfg.welcome_channel.hint": "Where joins and leaves get posted. Blank means she says nothing.",
  "cfg.log_channel": "Moderation log",
  "cfg.log_channel.hint": "Where warnings, mutes, kicks and bans are recorded.",
  "cfg.music_channel": "Music channel",
  "cfg.music_channel.hint": "Music commands only work here. Blank means anywhere.",
  "cfg.levelup_channel": "Level-up channel",
  "cfg.levelup_channel.hint": "Where level-ups are announced. Blank posts them where it happened.",
  "cfg.announce_channel": "Announce channel",
  "cfg.announce_channel.hint": "The default target for the announce command.",
  "cfg.autorole_id": "Role for new members",
  "cfg.autorole_id.hint": "Handed to everyone who joins. Blank means no role.",
  "cfg.dj_role": "DJ role",
  "cfg.dj_role.hint": "Skips the vote on music commands. Blank means everyone votes.",
  "cfg.timezone": "Timezone",
  "cfg.timezone.hint": "An IANA id like Asia/Ho_Chi_Minh. Used for reminders and daily counts.",
  "cfg.xp_multiplier": "XP multiplier",
  "cfg.xp_multiplier.hint": "A whole number from 1 to 5. Multiplies XP from every message.",
  "cfg.levelup_dm": "DM level-ups",
  "cfg.levelup_dm.hint": "On sends level-ups as a DM instead of posting them in the server.",
  "cfg.xp_decay": "XP decay",
  "cfg.xp_decay.hint": "On means inactive members slowly lose XP.",
  "cfg.xp_channel_weights": "Per-channel XP weight",
  "cfg.xp_channel_weights.hint":
    "channelId:percent pairs, comma separated — 123:150,456:0. 100 is normal, 0 earns nothing.",

  "moderation.title": "Moderation",
  "moderation.lead": "Every warning, mute, kick and ban recorded in {guild}.",
  "moderation.empty": "No cases in this server. Nothing has needed action.",
  "moderation.case": "Case {id}",
  "moderation.target": "Member",
  "moderation.by": "By",
  "moderation.noReason": "No reason recorded",
  "moderation.expires": "Ends {when}",
  "moderation.permanent": "No end date",
  "moderation.filter": "Filter by member ID",
  "moderation.filterHint": "Paste a Discord user ID to see only their cases.",
  "moderation.apply": "Filter",
  "moderation.clear": "Show all",
  "moderation.page": "Page {n} of {total}",

  "stats.title": "Stats",
  "stats.lead":
    "Counts for {guild} over the last {days} days. Totals only — this page never shows one member's timeline.",
  "stats.window": "Window",
  "stats.days30": "30 days",
  "stats.days7": "7 days",
  "stats.days90": "90 days",
  "stats.commands": "Most used commands",
  "stats.commandsEmpty": "No commands used in this window.",
  "stats.activity": "Messages per hour",
  "stats.activityEmpty": "No activity recorded in this window.",
  "stats.growth": "New members per day",
  "stats.growthEmpty": "Nobody joined in this window.",
  "stats.joined": "{n} joined",
  "stats.uses": "{n} uses",

  // ---------------------------------------------------------------- bot tier
  "health.title": "Health",
  "health.lead": "Is she up, what is she connected to, and what is playing. Bot-wide.",
  "health.status": "Status",
  "health.up": "Up",
  "health.degraded": "Struggling",
  "health.unknown": "Not sure yet",
  "health.unchecked": "Not checked yet",
  "health.uptime": "Uptime",
  // Uptime, in the largest unit that still says something. Days alone hide a restart an hour ago.
  "health.dhm": "{d}d {h}h {m}m",
  "health.hm": "{h}h {m}m",
  "health.m": "{m}m",
  "health.guilds": "Servers",
  "health.mood": "Mood",
  "health.quiet": "Even",
  "health.checks": "Checks",
  "health.checksEmpty": "She has not reported any checks yet. They appear once the first run finishes.",
  "health.pass": "Fine",
  "health.fail": "Failing",
  "health.players": "Playing now",
  "health.playersEmpty": "Nothing playing.",
  "health.noTrack": "Connected, nothing queued",
  "health.queued": "{n} waiting",
  "health.failed": "Could not reach her. If the page loaded, the panel is up and the bot is not.",

  "errors.title": "Errors",
  "errors.lead":
    "Commands of yours that failed recently, newest first. Quote the reference if you report one.",
  "errors.empty": "Nothing has failed for you. Good sign.",
  "errors.reset":
    "This list is kept in memory, so it starts empty again whenever Sonarr restarts.",
  "errors.case": "Reference {id}",

  "audit.title": "Audit",
  "audit.lead":
    "Every change made through this panel, across every server. Bot-wide, because it spans servers.",
  "audit.empty": "Nothing yet. Panel changes show up here as they happen.",
  "audit.endEmpty": "You have reached the end of the log. Go back a page for the newest entries.",
  "audit.from": "From entry {n}",
  "audit.botWide": "Every server",
  "audit.who": "By",
  "audit.server": "Server",
  "audit.more": "Show more",

  // ---------------------------------------------------------------- shared states
  // No "unauthorized" message: an expired session is a login, not a notice, so `Fail` redirects.
  "state.forbidden":
    "You do not have access to that. If you think you should, check you still have Manage Server in that server.",
  "state.error": "Could not load that. It is usually temporary — try again.",
  "state.noGuilds":
    "You do not share a server with Sonarr yet, so there is nothing to show. Join a server she is in and this fills up.",
  "state.notFound": "There is no page here.",
  "state.goHome": "Back to your panel",
} as const;

export type StringKey = keyof typeof en;

/**
 * Vietnamese lands here. Partial on purpose: a missing key falls back to English rather than
 * rendering a raw key, so the panel is usable from the first translated string.
 */
const vi: Partial<Record<StringKey, string>> = {};

const dictionaries: Record<Locale, Partial<Record<StringKey, string>>> = { en, vi };

export function isLocale(value: string | undefined): value is Locale {
  return locales.includes(value as Locale);
}

/**
 * Looks up one string and fills `{name}` placeholders.
 *
 * Values are interpolated as plain text into JSX, never into HTML, so there is nothing to escape
 * here — React does it at the render site.
 */
export function translate(
  locale: Locale,
  key: StringKey,
  values?: Record<string, string | number>,
): string {
  // The key itself when there is no entry, rather than throwing. A key built at runtime — a config
  // key from the API, say — is allowed to miss, and the caller checks for the echo.
  const text = dictionaries[locale]?.[key] ?? en[key] ?? key;

  if (!values) {
    return text;
  }

  return text.replace(/\{(\w+)\}/g, (match, name: string) =>
    name in values ? String(values[name]) : match,
  );
}

/** A bound `t` for one locale, which is what pages and components actually take. */
export type Translate = (key: StringKey, values?: Record<string, string | number>) => string;

export function translator(locale: Locale): Translate {
  return (key, values) => translate(locale, key, values);
}
