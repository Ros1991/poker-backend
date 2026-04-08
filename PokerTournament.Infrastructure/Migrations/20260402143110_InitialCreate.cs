using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    old_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    changed_fields = table.Column<string>(type: "jsonb", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "home_games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    logo_url = table.Column<string>(type: "text", nullable: true),
                    pix_key = table.Column<string>(type: "text", nullable: true),
                    pix_beneficiary = table.Column<string>(type: "text", nullable: true),
                    timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "America/Sao_Paulo"),
                    default_buy_in = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    default_rebuy = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    default_addon = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_home_games", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "persons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nickname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    whatsapp = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    document = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "blind_structures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    home_game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blind_structures", x => x.id);
                    table.ForeignKey(
                        name: "FK_blind_structures_home_games_home_game_id",
                        column: x => x.home_game_id,
                        principalTable: "home_games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scoring_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    home_game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    points_config = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scoring_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_scoring_rules_home_games_home_game_id",
                        column: x => x.home_game_id,
                        principalTable: "home_games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "home_game_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    home_game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_home_game_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_home_game_members_home_games_home_game_id",
                        column: x => x.home_game_id,
                        principalTable: "home_games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_home_game_members_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refresh_token = table.Column<string>(type: "text", nullable: true),
                    refresh_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "blind_levels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    blind_structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level_number = table.Column<int>(type: "integer", nullable: false),
                    small_blind = table.Column<int>(type: "integer", nullable: false),
                    big_blind = table.Column<int>(type: "integer", nullable: false),
                    ante = table.Column<int>(type: "integer", nullable: false),
                    big_blind_ante = table.Column<int>(type: "integer", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_break = table.Column<bool>(type: "boolean", nullable: false),
                    break_description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blind_levels", x => x.id);
                    table.ForeignKey(
                        name: "FK_blind_levels_blind_structures_blind_structure_id",
                        column: x => x.blind_structure_id,
                        principalTable: "blind_structures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rankings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    home_game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    season = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    scoring_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rankings", x => x.id);
                    table.ForeignKey(
                        name: "FK_rankings_home_games_home_game_id",
                        column: x => x.home_game_id,
                        principalTable: "home_games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rankings_scoring_rules_scoring_rule_id",
                        column: x => x.scoring_rule_id,
                        principalTable: "scoring_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tournaments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    home_game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ranking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    blind_structure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    start_time = table.Column<TimeSpan>(type: "interval", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    buy_in_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    rebuy_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    addon_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    starting_stack = table.Column<int>(type: "integer", nullable: false),
                    rebuy_stack = table.Column<int>(type: "integer", nullable: true),
                    addon_stack = table.Column<int>(type: "integer", nullable: true),
                    max_rebuys = table.Column<int>(type: "integer", nullable: true),
                    addon_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    late_registration_level = table.Column<int>(type: "integer", nullable: true),
                    rebuy_until_level = table.Column<int>(type: "integer", nullable: true),
                    current_level = table.Column<int>(type: "integer", nullable: true),
                    timer_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    timer_paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    timer_elapsed_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_timer_running = table.Column<bool>(type: "boolean", nullable: false),
                    total_prize_pool = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    total_costs = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    net_prize_pool = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    prize_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    prize_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settlement_closed = table.Column<bool>(type: "boolean", nullable: false),
                    settlement_closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settlement_closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    total_entries = table.Column<int>(type: "integer", nullable: false),
                    total_rebuys = table.Column<int>(type: "integer", nullable: false),
                    total_addons = table.Column<int>(type: "integer", nullable: false),
                    players_remaining = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournaments", x => x.id);
                    table.CheckConstraint("ck_tournaments_status", "status IN ('Draft','OpenForRegistration','InProgress','BreakSettlement','Finished','Cancelled')");
                    table.ForeignKey(
                        name: "FK_tournaments_blind_structures_blind_structure_id",
                        column: x => x.blind_structure_id,
                        principalTable: "blind_structures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tournaments_home_games_home_game_id",
                        column: x => x.home_game_id,
                        principalTable: "home_games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tournaments_rankings_ranking_id",
                        column: x => x.ranking_id,
                        principalTable: "rankings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "cost_extras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    beneficiary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pix_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pix_key_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_cash_box = table.Column<bool>(type: "boolean", nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    payment_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cost_extras", x => x.id);
                    table.ForeignKey(
                        name: "FK_cost_extras_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_logs_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_notification_logs_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tournament_tables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_number = table.Column<int>(type: "integer", nullable: false),
                    table_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    max_seats = table.Column<int>(type: "integer", nullable: false),
                    dealer_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_final_table = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournament_tables", x => x.id);
                    table.ForeignKey(
                        name: "FK_tournament_tables_persons_dealer_person_id",
                        column: x => x.dealer_person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tournament_tables_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tournament_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seat_number = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    buy_in_paid = table.Column<bool>(type: "boolean", nullable: false),
                    buy_in_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    rebuy_count = table.Column<int>(type: "integer", nullable: false),
                    rebuy_total = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    addon_purchased = table.Column<bool>(type: "boolean", nullable: false),
                    addon_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    total_due = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    total_paid = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    payment_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    final_position = table.Column<int>(type: "integer", nullable: true),
                    eliminated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    eliminated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    elimination_table_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_reentry = table.Column<bool>(type: "boolean", nullable: false),
                    original_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    prize_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    prize_paid = table.Column<bool>(type: "boolean", nullable: false),
                    ranking_points = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournament_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_tournament_entries_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tournament_entries_tournament_entries_eliminated_by_id",
                        column: x => x.eliminated_by_id,
                        principalTable: "tournament_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tournament_entries_tournament_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "tournament_tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tournament_entries_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eliminations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    eliminated_by_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    table_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: true),
                    blind_level = table.Column<int>(type: "integer", nullable: true),
                    eliminated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    corrected = table.Column<bool>(type: "boolean", nullable: false),
                    corrected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    corrected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    correction_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eliminations", x => x.id);
                    table.ForeignKey(
                        name: "FK_eliminations_tournament_entries_eliminated_by_entry_id",
                        column: x => x.eliminated_by_entry_id,
                        principalTable: "tournament_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_eliminations_tournament_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "tournament_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_eliminations_tournament_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "tournament_tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_eliminations_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ranking_scores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ranking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: true),
                    points = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    bonus_points = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    total_points = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    player_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranking_scores", x => x.id);
                    table.ForeignKey(
                        name: "FK_ranking_scores_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ranking_scores_rankings_ranking_id",
                        column: x => x.ranking_id,
                        principalTable: "rankings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ranking_scores_tournament_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "tournament_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ranking_scores_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tournament_prizes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    percentage = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid = table.Column<bool>(type: "boolean", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournament_prizes", x => x.id);
                    table.ForeignKey(
                        name: "FK_tournament_prizes_tournament_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "tournament_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tournament_prizes_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    pix_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    cash_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    pix_destination_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_transactions_cost_extras_pix_destination_id",
                        column: x => x.pix_destination_id,
                        principalTable: "cost_extras",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_transactions_tournament_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "tournament_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_transactions_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at",
                table: "audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tournament_id",
                table: "audit_logs",
                column: "tournament_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_blind_levels_structure_level",
                table: "blind_levels",
                columns: new[] { "blind_structure_id", "level_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_blind_structures_home_game_id",
                table: "blind_structures",
                column: "home_game_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_extras_tournament_id",
                table: "cost_extras",
                column: "tournament_id",
                unique: true,
                filter: "is_cash_box = true");

            migrationBuilder.CreateIndex(
                name: "IX_eliminations_eliminated_by_entry_id",
                table: "eliminations",
                column: "eliminated_by_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_eliminations_entry_id",
                table: "eliminations",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_eliminations_table_id",
                table: "eliminations",
                column: "table_id");

            migrationBuilder.CreateIndex(
                name: "ix_eliminations_tournament_id",
                table: "eliminations",
                column: "tournament_id");

            migrationBuilder.CreateIndex(
                name: "ix_home_game_members_home_game_person",
                table: "home_game_members",
                columns: new[] { "home_game_id", "person_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_home_game_members_person_id",
                table: "home_game_members",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_home_games_name",
                table: "home_games",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_home_games_not_deleted",
                table: "home_games",
                column: "deleted_at",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_person_id",
                table: "notification_logs",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_tournament_id",
                table: "notification_logs",
                column: "tournament_id");

            migrationBuilder.CreateIndex(
                name: "ix_persons_email",
                table: "persons",
                column: "email",
                unique: true,
                filter: "email IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_persons_full_name",
                table: "persons",
                column: "full_name");

            migrationBuilder.CreateIndex(
                name: "ix_persons_not_deleted",
                table: "persons",
                column: "deleted_at",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_persons_whatsapp",
                table: "persons",
                column: "whatsapp",
                unique: true,
                filter: "whatsapp IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ranking_scores_entry_id",
                table: "ranking_scores",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_ranking_scores_person_id",
                table: "ranking_scores",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_ranking_scores_ranking_person_tournament",
                table: "ranking_scores",
                columns: new[] { "ranking_id", "person_id", "tournament_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ranking_scores_tournament_id",
                table: "ranking_scores",
                column: "tournament_id");

            migrationBuilder.CreateIndex(
                name: "ix_rankings_home_game_id",
                table: "rankings",
                column: "home_game_id");

            migrationBuilder.CreateIndex(
                name: "ix_rankings_not_deleted",
                table: "rankings",
                column: "deleted_at",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_rankings_scoring_rule_id",
                table: "rankings",
                column: "scoring_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_scoring_rules_home_game_id",
                table: "scoring_rules",
                column: "home_game_id");

            migrationBuilder.CreateIndex(
                name: "IX_tournament_entries_eliminated_by_id",
                table: "tournament_entries",
                column: "eliminated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_entries_person_id",
                table: "tournament_entries",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_entries_position",
                table: "tournament_entries",
                column: "final_position");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_entries_status",
                table: "tournament_entries",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_entries_table_id",
                table: "tournament_entries",
                column: "table_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_entries_tournament_id",
                table: "tournament_entries",
                column: "tournament_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_entries_tournament_person_entry",
                table: "tournament_entries",
                columns: new[] { "tournament_id", "person_id", "entry_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tournament_prizes_entry_id",
                table: "tournament_prizes",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_prizes_tournament_position",
                table: "tournament_prizes",
                columns: new[] { "tournament_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tournament_tables_dealer_person_id",
                table: "tournament_tables",
                column: "dealer_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_tables_tournament_number",
                table: "tournament_tables",
                columns: new[] { "tournament_id", "table_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tournaments_blind_structure_id",
                table: "tournaments",
                column: "blind_structure_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournaments_date",
                table: "tournaments",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_tournaments_home_game_id",
                table: "tournaments",
                column: "home_game_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournaments_not_deleted",
                table: "tournaments",
                column: "deleted_at",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tournaments_ranking_id",
                table: "tournaments",
                column: "ranking_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournaments_status",
                table: "tournaments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_entry_id",
                table: "transactions",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_pix_destination_id",
                table: "transactions",
                column: "pix_destination_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_tournament_id",
                table: "transactions",
                column: "tournament_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_person_id",
                table: "users",
                column: "person_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "blind_levels");

            migrationBuilder.DropTable(
                name: "eliminations");

            migrationBuilder.DropTable(
                name: "home_game_members");

            migrationBuilder.DropTable(
                name: "notification_logs");

            migrationBuilder.DropTable(
                name: "ranking_scores");

            migrationBuilder.DropTable(
                name: "tournament_prizes");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "cost_extras");

            migrationBuilder.DropTable(
                name: "tournament_entries");

            migrationBuilder.DropTable(
                name: "tournament_tables");

            migrationBuilder.DropTable(
                name: "persons");

            migrationBuilder.DropTable(
                name: "tournaments");

            migrationBuilder.DropTable(
                name: "blind_structures");

            migrationBuilder.DropTable(
                name: "rankings");

            migrationBuilder.DropTable(
                name: "scoring_rules");

            migrationBuilder.DropTable(
                name: "home_games");
        }
    }
}
