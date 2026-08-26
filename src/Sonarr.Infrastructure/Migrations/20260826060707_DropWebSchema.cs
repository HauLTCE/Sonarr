using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sonarr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropWebSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit",
                schema: "web");

            migrationBuilder.DropTable(
                name: "login_token",
                schema: "web");

            migrationBuilder.DropTable(
                name: "session",
                schema: "web");

            // Scaffolding drops the three tables but leaves the schema standing, and an empty
            // web schema is exactly the thing a later migration would grow a table back into.
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS web;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "web");

            migrationBuilder.CreateTable(
                name: "audit",
                schema: "web",
                columns: table => new
                {
                    audit_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    detail = table.Column<string>(type: "jsonb", nullable: false),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit", x => x.audit_id);
                });

            migrationBuilder.CreateTable(
                name: "login_token",
                schema: "web",
                columns: table => new
                {
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    used = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_token", x => x.token_hash);
                });

            migrationBuilder.CreateTable(
                name: "session",
                schema: "web",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session", x => x.session_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_at",
                schema: "web",
                table: "audit",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_login_token_expires_at",
                schema: "web",
                table: "login_token",
                column: "expires_at");

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
        }
    }
}
