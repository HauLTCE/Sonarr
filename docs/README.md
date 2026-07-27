# Sonarr — Documentation

Design docs for the greenfield C#/.NET 10 rewrite of the Sonarr Discord bot.
The user-facing spec lives in [`../what user need.md`](../what%20user%20need.md); these
docs describe how we build it.

| Doc | Contents |
|---|---|
| [01-overview.md](01-overview.md) | What Sonarr is, the concept, naming rules, scope |
| [02-architecture.md](02-architecture.md) | Process layout, layering convention, project structure |
| [03-stack.md](03-stack.md) | Every technology choice and why |
| [04-database.md](04-database.md) | PostgreSQL schema — all tables and fields |
| [05-caching.md](05-caching.md) | Redis — every key, TTL, and what it's for |
| [06-data-and-privacy.md](06-data-and-privacy.md) | What we collect from users, retention, deletion |
| [07-commands.md](07-commands.md) | All slash commands |
| [08-background-services.md](08-background-services.md) | Everything running besides command handling |
| [09-web-panels.md](09-web-panels.md) | Next.js panels + DM-token login flow |
| [10-elaine-engine.md](10-elaine-engine.md) | The chat engine (internal name: Elaine) |
| [11-deployment.md](11-deployment.md) | Docker, servers, backups, migration/cutover |

Ground rules that apply everywhere:

1. **Users only ever see "Sonarr".** Elaine is the internal name of the chat module
   (`Sonarr.Elaine` project); it never appears in command names, replies, or the web UI.
2. **No generative AI.** Every reply is authored. Embeddings (non-generative) are used
   for understanding and recall only.
3. **Everything runs on the target box** (Pentium J2900, 4C/4T, 8GB RAM, no AVX):
   any component that can't must be redesigned, not excused.
4. **Nothing durable is lost on restart.** State is Postgres (durable), Redis
   (transient by design, TTL'd), or explicitly documented as ephemeral.
