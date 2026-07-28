/**
 * Every user-visible string in the panel (docs/09: "i18n-ready strings from day one, Vietnamese
 * later"). No library: one dictionary per locale and a `t()` that reads the active one is what a
 * two-locale app needs, and it costs nothing at runtime.
 *
 * The rule this file exists to enforce: no page hard-codes English. Adding Vietnamese later means
 * adding one object below and a locale cookie — not finding every string in the tree.
 */

export const locales = ["en", "vi"] as const;

export type Locale = (typeof locales)[number];

export const defaultLocale: Locale = "en";

const en = {
  "app.name": "Sonarr",
  "app.tagline": "Your data, your settings, in one place.",

  "nav.overview": "Overview",
  "nav.myData": "My data",
  "nav.sonarrAndMe": "Sonarr & me",
  "nav.myErrors": "My errors",
  "nav.music": "Music",
  "nav.privacy": "Privacy",
  "nav.admin": "Admin",
  "nav.status": "Status",
  "nav.config": "Config",
  "nav.flags": "Feature flags",
  "nav.modLog": "Mod log",
  "nav.stats": "Stats",
  "nav.audit": "Audit",
  "nav.logOut": "Log out",
  "nav.skipToContent": "Skip to content",
  "nav.userPanel": "Your panel",
  "nav.adminPanel": "Admin panel",

  "login.title": "Log in",
  "login.lead": "Enter your Discord handle and Sonarr will DM you a code.",
  "login.handleLabel": "Discord handle",
  "login.handleHint": "Without the @ — for example, someone_here",
  "login.codeLabel": "Code from your DMs",
  "login.codeHint": "8 characters, valid for 10 minutes.",
  "login.remember": "Keep me logged in on this device",
  "login.sendCode": "Send me a code",
  "login.verify": "Log in",
  "login.sending": "Sending…",
  "login.verifying": "Logging in…",
  "login.sent":
    "If that account is known to Sonarr, a DM is on its way. Paste the code below.",
  "login.dmsClosedTitle": "No DM arrived?",
  "login.dmsClosed":
    "Sonarr can only DM you if you allow direct messages from server members. In Discord, open the server, then Server Settings → Privacy Settings, and turn on direct messages. You also need to share a server with Sonarr.",
  "login.badCode": "That code is not right. Check the DM and try again.",
  "login.expiredCode": "That code has expired or was already used. Ask for a new one.",
  "login.tooManyAttempts": "Too many wrong codes. Ask for a new one.",
  "login.rateLimited": "Too many attempts just now. Wait a few minutes.",
  "login.needHandle": "Enter your Discord handle first.",
  "login.failed": "Something went wrong. Try again in a moment.",
  "login.startOver": "Use a different handle",

  "guild.label": "Server",
  "guild.switch": "Switch",
  "guild.none": "Sonarr does not see you in any server yet. Say something where she can see it.",
  "guild.hint": "Paste the server id you want to look at.",

  "overview.title": "Overview",
  "overview.level": "Level",
  "overview.xp": "XP",
  "overview.rank": "Rank",
  "overview.streak": "Streak",
  "overview.streakDays": "{count} days",
  "overview.streakNone": "None right now",
  "overview.messages": "Messages",
  "overview.voice": "Voice time",
  "overview.voiceMinutes": "{count} min",
  "overview.joined": "First seen",
  "overview.lastActive": "Last active",
  "overview.toNextLevel": "{count} XP to the next level",
  "overview.progressLabel": "Progress through level {level}",
  "overview.reminders": "Active reminders",
  "overview.remindersEmpty": "Nothing scheduled.",

  "myData.title": "My data",
  "myData.lead":
    "Everything Sonarr has stored about you, read live from the database. Nothing here is a summary written in advance.",
  "myData.memberships": "Servers",
  "myData.levels": "Levels",
  "myData.facts": "What she remembers",
  "myData.factsEmpty": "Nothing stored.",
  "myData.counts": "Counts",
  "myData.sessions": "Panel sessions",
  "myData.generatedAt": "Read at {at}",
  "myData.download": "Download everything as JSON",
  "myData.name": "Name",
  "myData.timezone": "Timezone",
  "myData.birthday": "Birthday",
  "myData.predicate": "About",
  "myData.value": "Value",
  "myData.learnedAt": "Learned",
  "myData.chatEpisodes": "Conversations",
  "myData.relationshipEvents": "Relationship events",
  "myData.tracksPlayed": "Tracks played",
  "myData.trackRatings": "Track ratings",
  "myData.savedQuotes": "Saved quotes",
  "myData.remindersCount": "Reminders",
  "myData.capsules": "Capsules",
  "myData.modCases": "Moderation cases",
  "myData.sessionStarted": "Started",
  "myData.sessionExpires": "Expires",
  "myData.device": "Device",

  "sonarr.title": "Sonarr & me",
  "sonarr.relationship": "How she describes you",
  "sonarr.nickname": "The name she uses for you",
  "sonarr.nicknameNone": "She has not picked one.",
  "sonarr.facts": "What she remembers",
  "sonarr.forget": "Ask her to forget this",
  "sonarr.forgetting": "Asking…",
  "sonarr.learnedAt": "Learned {at}",
  "sonarr.confidence": "Confidence {value}",
  "sonarr.confidenceShort": "Confidence",
  "sonarr.tier": "Standing",
  "sonarr.opener": "How she would open",
  "sonarr.factsEmpty": "She has not written anything down about you.",
  "sonarr.forgotten": "Done. She has forgotten that.",
  "sonarr.forgetFailed": "That did not go through. Try again.",

  "errors.title": "My errors",
  "errors.lead":
    "Recent failures from your own commands, newest first. Give the reference to a mod if you want someone to look at it.",
  "errors.empty": "Nothing has broken lately.",
  "errors.note":
    "This list is kept in memory, so it resets when the bot restarts. Only your own commands appear here.",
  "errors.reference": "Reference",
  "errors.command": "Command",
  "errors.message": "What went wrong",

  "music.title": "Music",
  "music.myHistory": "My history",
  "music.myRatings": "My ratings",
  "music.serverTop": "Server top tracks",
  "music.plays": "{count} plays",
  "music.liked": "You liked this",
  "music.disliked": "You disliked this",
  "music.tally": "{likes} up, {dislikes} down",
  "music.empty": "Nothing yet.",
  "music.track": "Track",
  "music.playsHeader": "Plays",
  "music.myVote": "My vote",
  "music.serverVotes": "Server votes",
  "music.score": "Score",
  "music.untitled": "Untitled",

  "privacy.title": "Privacy",
  "privacy.lead":
    "Two buttons, both irreversible. Backups are kept for seven days and then rotate out.",
  "privacy.export": "Export",
  "privacy.exportLead": "Download everything Sonarr stores about you as a JSON file.",
  "privacy.exportButton": "Download my data",
  "privacy.deleteChat": "Delete chat memory",
  "privacy.deleteChatLead":
    "Removes what the chat engine holds: her impression of you, the facts she learned, and your conversation history. She genuinely forgets.",
  "privacy.deleteAll": "Delete everything",
  "privacy.deleteAllLead":
    "The above, plus levels, music history and ratings, saved quotes, reminders, capsules, event RSVPs and your panel sessions. Moderation cases are kept — those are the server's record, not yours.",
  "privacy.confirmLabel": "Type {word} to confirm",
  "privacy.confirmButton": "Delete",
  "privacy.deleting": "Deleting…",
  "privacy.deleted": "Done. {count} rows removed.",
  "privacy.logOutEverywhere": "Log out everywhere",
  "privacy.loggedOutEverywhere": "{count} sessions ended.",
  "privacy.sessions": "Panel sessions",
  "privacy.sessionsLead":
    "Ends every panel session, including this one, on every device. Use it if you think someone else has been in your panel.",

  "admin.status.title": "Status",
  "admin.status.uptime": "Uptime",
  "admin.status.gateway": "Gateway",
  "admin.status.lavalink": "Lavalink",
  "admin.status.database": "Database",
  "admin.status.redis": "Redis",
  "admin.status.unknown": "Unknown",
  "admin.status.stale":
    "The status blob is older than its refresh window, so the bot may be down.",
  "admin.status.ok": "Everything is healthy.",
  "admin.status.degraded": "Something is failing. See the checks below.",
  "admin.status.down": "The bot is not answering. It is probably down.",
  "admin.status.latency": "Gateway latency",
  "admin.status.guilds": "Servers",
  "admin.status.startedAt": "Started",
  "admin.status.checkedAt": "Last self-test",
  "admin.status.checks": "Self-test",
  "admin.status.check": "Check",
  "admin.status.result": "Result",
  "admin.status.detail": "Detail",
  "admin.status.healthy": "Passing",
  "admin.status.failing": "Failing",
  "admin.status.noChecks": "The self-test has not run yet.",
  "admin.status.players": "Music players",
  "admin.status.noPlayers": "Nothing is playing.",
  "admin.status.playerState": "State",
  "admin.status.queued": "Queued",
  "admin.status.nowPlaying": "Now playing",
  "admin.status.uptimeDays": "{days}d {hours}h",
  "admin.status.uptimeHours": "{hours}h {minutes}m",
  "admin.status.uptimeMinutes": "{minutes}m",

  "admin.config.title": "Config",
  "admin.config.key": "Key",
  "admin.config.value": "Value",
  "admin.config.kind": "Type",
  "admin.config.save": "Save",
  "admin.config.saving": "Saving…",
  "admin.config.clear": "Reset to default",
  "admin.config.saved": "Saved.",
  "admin.config.export": "Export as JSON",
  "admin.config.note":
    "An empty field means the key goes back to its default. Values are validated the same way /config set validates them, so the message you get back is hers.",

  "admin.flags.title": "Feature flags",
  "admin.flags.feature": "Module",
  "admin.flags.state": "State",
  "admin.flags.source": "Set by",
  "admin.flags.on": "On",
  "admin.flags.off": "Off",
  "admin.flags.enable": "Enable",
  "admin.flags.disable": "Disable",
  "admin.flags.note":
    "\"Set by\" says where the current state comes from: a default, this server's own row, or the kill switch. Turning a module off here stops its commands answering in this server only.",

  "admin.cases.title": "Mod log",
  "admin.cases.case": "Case",
  "admin.cases.target": "Member",
  "admin.cases.actor": "Moderator",
  "admin.cases.action": "Action",
  "admin.cases.reason": "Reason",
  "admin.cases.expires": "Expires",
  "admin.cases.filterLabel": "Filter by member id",
  "admin.cases.filter": "Filter",
  "admin.cases.empty": "No cases.",
  "admin.cases.total": "{count} cases",
  "admin.cases.pagination": "Mod log pages",

  "admin.stats.title": "Stats",
  "admin.stats.window": "Last {days} days",
  "admin.stats.commands": "Command usage",
  "admin.stats.activity": "Activity",
  "admin.stats.growth": "New members",
  "admin.stats.messages": "Messages",
  "admin.stats.voice": "In voice",
  "admin.stats.online": "Online",
  "admin.stats.uses": "Uses",
  "admin.stats.day": "Day",
  "admin.stats.joined": "Joined",
  "admin.stats.summary": "{total} in the window, busiest sample {peak}.",
  "admin.stats.privacyNote":
    "These are totals only. The panel cannot show one member's activity, by design.",

  "admin.audit.title": "Audit",
  "admin.audit.who": "Who",
  "admin.audit.what": "Action",
  "admin.audit.target": "Target",
  "admin.audit.when": "When",
  "admin.audit.empty": "Nothing yet.",
  "admin.audit.detail": "Detail",
  "admin.audit.pagination": "Audit pages",
  "admin.audit.pageNumber": "Page {page}",
  "admin.audit.note":
    "Every panel write lands here, including logins and self-deletions, which is why this list is not per server. Audit rows are kept indefinitely — the nightly sweep does not touch them.",

  // Her mood, as the accent colour's name. Ids come from persona/sonarr.yaml → modes; the panel
  // never shows the id itself, so a mode rename here is a translation change, not a code change.
  "mood.label": "Sonarr's mood: {mood}",
  "mood.SEETHING": "seething",
  "mood.ANNOYED": "annoyed",
  "mood.BORED": "bored",
  "mood.SMUG": "smug",
  "mood.PLAYFUL": "playful",
  "mood.FOND": "fond",
  "mood.NEUTRAL": "neutral",

  "common.loading": "Loading…",
  "common.retry": "Try again",
  "common.error": "Could not load that. Try again.",
  "common.unauthorized": "Your session has ended. Log in again.",
  "common.forbidden": "You do not have access to this page.",
  "common.when": "When",
  "common.what": "What",
  "common.actions": "Actions",
  "common.none": "None",
  "common.never": "Never",
  "common.page": "Page {page} of {pageCount}",
  "common.previous": "Previous",
  "common.next": "Next",
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
  const text = dictionaries[locale]?.[key] ?? en[key];

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
