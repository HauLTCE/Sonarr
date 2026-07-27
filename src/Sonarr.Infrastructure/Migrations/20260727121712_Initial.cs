using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Sonarr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "stats");

            migrationBuilder.EnsureSchema(
                name: "web");

            migrationBuilder.EnsureSchema(
                name: "social");

            migrationBuilder.EnsureSchema(
                name: "mod");

            migrationBuilder.EnsureSchema(
                name: "chat");

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "music");

            migrationBuilder.EnsureSchema(
                name: "levels");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "audit",
                schema: "web",
                columns: table => new
                {
                    audit_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    detail = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit", x => x.audit_id);
                });

            migrationBuilder.CreateTable(
                name: "episode",
                schema: "chat",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    quote = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    sentiment_tag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    turn = table.Column<long>(type: "bigint", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(384)", nullable: true),
                    happened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_episode", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fact",
                schema: "chat",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    predicate = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    confidence = table.Column<float>(type: "real", nullable: false),
                    learned_at_turn = table.Column<long>(type: "bigint", nullable: false),
                    learned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fact", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feature_flag",
                schema: "core",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    feature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<bool>(type: "boolean", nullable: false),
                    changed_by = table.Column<long>(type: "bigint", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flag", x => new { x.guild_id, x.feature });
                });

            migrationBuilder.CreateTable(
                name: "guild",
                schema: "core",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild", x => x.guild_id);
                });

            migrationBuilder.CreateTable(
                name: "guild_state",
                schema: "chat",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    room_mood = table.Column<string>(type: "jsonb", nullable: false),
                    global_clock = table.Column<long>(type: "bigint", nullable: false),
                    event_log = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_state", x => x.guild_id);
                });

            migrationBuilder.CreateTable(
                name: "intent_embedding",
                schema: "chat",
                columns: table => new
                {
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    intent_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    example = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    embedding = table.Column<Vector>(type: "vector(384)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_intent_embedding", x => x.content_hash);
                });

            migrationBuilder.CreateTable(
                name: "job",
                schema: "core",
                columns: table => new
                {
                    job_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recurrence = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job", x => x.job_id);
                });

            migrationBuilder.CreateTable(
                name: "login_token",
                schema: "web",
                columns: table => new
                {
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used = table.Column<bool>(type: "boolean", nullable: false),
                    requested_ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_token", x => x.token_hash);
                });

            migrationBuilder.CreateTable(
                name: "person",
                schema: "chat",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    dialogue_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    registers = table.Column<string>(type: "jsonb", nullable: false),
                    slots = table.Column<string>(type: "jsonb", nullable: false),
                    logical_clock = table.Column<long>(type: "bigint", nullable: false),
                    relationship_tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    assigned_nickname = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    fired_log = table.Column<string>(type: "jsonb", nullable: false),
                    activity_stack = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person", x => new { x.guild_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "relationship_event",
                schema: "chat",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    delta = table.Column<string>(type: "jsonb", nullable: false),
                    cause = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    turn = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relationship_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "session",
                schema: "web",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked = table.Column<bool>(type: "boolean", nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session", x => x.session_id);
                });

            migrationBuilder.CreateTable(
                name: "stance",
                schema: "chat",
                columns: table => new
                {
                    topic = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stance = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    pool_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stance", x => x.topic);
                });

            migrationBuilder.CreateTable(
                name: "activity_sample",
                schema: "stats",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    hour_bucket = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    messages = table.Column<int>(type: "integer", nullable: false),
                    voice_users = table.Column<int>(type: "integer", nullable: false),
                    online_estimate = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_sample", x => new { x.guild_id, x.hour_bucket });
                    table.ForeignKey(
                        name: "fk_activity_sample_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "capsule",
                schema: "social",
                columns: table => new
                {
                    capsule_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    deliver_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capsule", x => x.capsule_id);
                    table.ForeignKey(
                        name: "fk_capsule_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "case",
                schema: "mod",
                columns: table => new
                {
                    case_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    target_id = table.Column<long>(type: "bigint", nullable: false),
                    actor_id = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    context = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case", x => x.case_id);
                    table.ForeignKey(
                        name: "fk_case_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "command_usage",
                schema: "stats",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    command = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_command_usage", x => new { x.guild_id, x.command, x.day });
                    table.ForeignKey(
                        name: "fk_command_usage_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event",
                schema: "social",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    creator_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    discord_event_id = table.Column<long>(type: "bigint", nullable: true),
                    ping_role_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event", x => x.event_id);
                    table.ForeignKey(
                        name: "fk_event_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_config",
                schema: "core",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_config", x => new { x.guild_id, x.key });
                    table.ForeignKey(
                        name: "fk_guild_config_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "member",
                schema: "core",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    message_count = table.Column<long>(type: "bigint", nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    birthday = table.Column<DateOnly>(type: "date", nullable: true),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member", x => new { x.guild_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_member_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "play_history",
                schema: "music",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    requester_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    uri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_play_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_play_history_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist",
                schema: "music",
                columns: table => new
                {
                    playlist_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist", x => x.playlist_id);
                    table.ForeignKey(
                        name: "fk_playlist_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "progress",
                schema: "levels",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    xp = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    last_message_xp_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voice_seconds = table.Column<long>(type: "bigint", nullable: false),
                    streak_days = table.Column<int>(type: "integer", nullable: false),
                    streak_last_day = table.Column<DateOnly>(type: "date", nullable: true),
                    first_msg_bonus_day = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_progress", x => new { x.guild_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_progress_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quote_board",
                schema: "social",
                columns: table => new
                {
                    quote_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    saved_by = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    message_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_board", x => x.quote_id);
                    table.ForeignKey(
                        name: "fk_quote_board_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reward",
                schema: "levels",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reward", x => new { x.guild_id, x.level });
                    table.ForeignKey(
                        name: "fk_reward_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "season",
                schema: "levels",
                columns: table => new
                {
                    season_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_season", x => x.season_id);
                    table.ForeignKey(
                        name: "fk_season_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket",
                schema: "social",
                columns: table => new
                {
                    ticket_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    opener_id = table.Column<long>(type: "bigint", nullable: false),
                    thread_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    transcript_ref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    closed_by = table.Column<long>(type: "bigint", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket", x => x.ticket_id);
                    table.ForeignKey(
                        name: "fk_ticket_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_rating",
                schema: "music",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    uri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    vote = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_rating", x => new { x.guild_id, x.uri, x.user_id });
                    table.CheckConstraint("ck_track_rating_vote", "vote IN (-1, 1)");
                    table.ForeignKey(
                        name: "fk_track_rating_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_prefs",
                schema: "music",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    volume = table.Column<int>(type: "integer", nullable: true),
                    favorites = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_prefs", x => new { x.guild_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_user_prefs_guild_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "core",
                        principalTable: "guild",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stance_agreement",
                schema: "chat",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    topic = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    agreed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stance_agreement", x => new { x.guild_id, x.user_id, x.topic });
                    table.ForeignKey(
                        name: "fk_stance_agreement_stance_topic",
                        column: x => x.topic,
                        principalSchema: "chat",
                        principalTable: "stance",
                        principalColumn: "topic",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_rsvp",
                schema: "social",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    response = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_rsvp", x => new { x.event_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_event_rsvp_event_event_id",
                        column: x => x.event_id,
                        principalSchema: "social",
                        principalTable: "event",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist_track",
                schema: "music",
                columns: table => new
                {
                    playlist_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    uri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    added_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist_track", x => new { x.playlist_id, x.position });
                    table.ForeignKey(
                        name: "fk_playlist_track_playlist_playlist_id",
                        column: x => x.playlist_id,
                        principalSchema: "music",
                        principalTable: "playlist",
                        principalColumn: "playlist_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "season_result",
                schema: "levels",
                columns: table => new
                {
                    season_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    xp_earned = table.Column<long>(type: "bigint", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_season_result", x => new { x.season_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_season_result_season_season_id",
                        column: x => x.season_id,
                        principalSchema: "levels",
                        principalTable: "season",
                        principalColumn: "season_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_at",
                schema: "web",
                table: "audit",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_capsule_deliver_at",
                schema: "social",
                table: "capsule",
                column: "deliver_at");

            migrationBuilder.CreateIndex(
                name: "ix_capsule_guild_id",
                schema: "social",
                table: "capsule",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_guild_id_target_id",
                schema: "mod",
                table: "case",
                columns: new[] { "guild_id", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_episode_embedding",
                schema: "chat",
                table: "episode",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "ivfflat")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:lists", 100);

            migrationBuilder.CreateIndex(
                name: "ix_episode_guild_id_user_id_happened_at",
                schema: "chat",
                table: "episode",
                columns: new[] { "guild_id", "user_id", "happened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_event_guild_id_starts_at",
                schema: "social",
                table: "event",
                columns: new[] { "guild_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fact_guild_id_user_id_predicate",
                schema: "chat",
                table: "fact",
                columns: new[] { "guild_id", "user_id", "predicate" });

            migrationBuilder.CreateIndex(
                name: "ix_intent_embedding_intent_id",
                schema: "chat",
                table: "intent_embedding",
                column: "intent_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_status_run_at",
                schema: "core",
                table: "job",
                columns: new[] { "status", "run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_login_token_expires_at",
                schema: "web",
                table: "login_token",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_member_guild_id_birthday",
                schema: "core",
                table: "member",
                columns: new[] { "guild_id", "birthday" });

            migrationBuilder.CreateIndex(
                name: "ix_member_guild_id_message_count",
                schema: "core",
                table: "member",
                columns: new[] { "guild_id", "message_count" });

            migrationBuilder.CreateIndex(
                name: "ix_play_history_guild_id_played_at",
                schema: "music",
                table: "play_history",
                columns: new[] { "guild_id", "played_at" });

            migrationBuilder.CreateIndex(
                name: "ix_play_history_guild_id_requester_id",
                schema: "music",
                table: "play_history",
                columns: new[] { "guild_id", "requester_id" });

            migrationBuilder.CreateIndex(
                name: "ix_playlist_guild_id_name",
                schema: "music",
                table: "playlist",
                columns: new[] { "guild_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_progress_guild_id_xp",
                schema: "levels",
                table: "progress",
                columns: new[] { "guild_id", "xp" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_board_guild_id_author_id",
                schema: "social",
                table: "quote_board",
                columns: new[] { "guild_id", "author_id" });

            migrationBuilder.CreateIndex(
                name: "ix_relationship_event_guild_id_user_id_at",
                schema: "chat",
                table: "relationship_event",
                columns: new[] { "guild_id", "user_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_season_guild_id_status",
                schema: "levels",
                table: "season",
                columns: new[] { "guild_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_session_expires_at",
                schema: "web",
                table: "session",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_session_user_id",
                schema: "web",
                table: "session",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stance_agreement_topic",
                schema: "chat",
                table: "stance_agreement",
                column: "topic");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_guild_id_status",
                schema: "social",
                table: "ticket",
                columns: new[] { "guild_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_thread_id",
                schema: "social",
                table: "ticket",
                column: "thread_id",
                unique: true);
            // Hand-written: EF does not scaffold views. Kept in sync with
            // InfractionSummaryConfiguration.ViewSql (mod.infraction_summary).
            migrationBuilder.Sql(Sonarr.Infrastructure.Persistence.Configurations.Mod.InfractionSummaryConfiguration.ViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS mod.infraction_summary;");

            migrationBuilder.DropTable(
                name: "activity_sample",
                schema: "stats");

            migrationBuilder.DropTable(
                name: "audit",
                schema: "web");

            migrationBuilder.DropTable(
                name: "capsule",
                schema: "social");

            migrationBuilder.DropTable(
                name: "case",
                schema: "mod");

            migrationBuilder.DropTable(
                name: "command_usage",
                schema: "stats");

            migrationBuilder.DropTable(
                name: "episode",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "event_rsvp",
                schema: "social");

            migrationBuilder.DropTable(
                name: "fact",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "feature_flag",
                schema: "core");

            migrationBuilder.DropTable(
                name: "guild_config",
                schema: "core");

            migrationBuilder.DropTable(
                name: "guild_state",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "intent_embedding",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "job",
                schema: "core");

            migrationBuilder.DropTable(
                name: "login_token",
                schema: "web");

            migrationBuilder.DropTable(
                name: "member",
                schema: "core");

            migrationBuilder.DropTable(
                name: "person",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "play_history",
                schema: "music");

            migrationBuilder.DropTable(
                name: "playlist_track",
                schema: "music");

            migrationBuilder.DropTable(
                name: "progress",
                schema: "levels");

            migrationBuilder.DropTable(
                name: "quote_board",
                schema: "social");

            migrationBuilder.DropTable(
                name: "relationship_event",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "reward",
                schema: "levels");

            migrationBuilder.DropTable(
                name: "season_result",
                schema: "levels");

            migrationBuilder.DropTable(
                name: "session",
                schema: "web");

            migrationBuilder.DropTable(
                name: "stance_agreement",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "ticket",
                schema: "social");

            migrationBuilder.DropTable(
                name: "track_rating",
                schema: "music");

            migrationBuilder.DropTable(
                name: "user_prefs",
                schema: "music");

            migrationBuilder.DropTable(
                name: "event",
                schema: "social");

            migrationBuilder.DropTable(
                name: "playlist",
                schema: "music");

            migrationBuilder.DropTable(
                name: "season",
                schema: "levels");

            migrationBuilder.DropTable(
                name: "stance",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "guild",
                schema: "core");
        }
    }
}
