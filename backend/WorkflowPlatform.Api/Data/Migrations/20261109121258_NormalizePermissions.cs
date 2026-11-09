using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WorkflowPlatform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PermissionPresets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "InvitationPermissions",
                columns: table => new
                {
                    InvitationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationPermissions", x => new { x.InvitationId, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_InvitationPermissions_Invitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "Invitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvitationPermissions_Permissions_PermissionKey",
                        column: x => x.PermissionKey,
                        principalTable: "Permissions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MembershipPermissions",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipPermissions", x => new { x.MembershipId, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_MembershipPermissions_AccountMemberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "AccountMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MembershipPermissions_Permissions_PermissionKey",
                        column: x => x.PermissionKey,
                        principalTable: "Permissions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PermissionPresetItems",
                columns: table => new
                {
                    PresetId = table.Column<int>(type: "int", nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionPresetItems", x => new { x.PresetId, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_PermissionPresetItems_PermissionPresets_PresetId",
                        column: x => x.PresetId,
                        principalTable: "PermissionPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PermissionPresetItems_Permissions_PermissionKey",
                        column: x => x.PermissionKey,
                        principalTable: "Permissions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "PermissionPresets",
                columns: new[] { "Id", "Description", "Name", "SortOrder" },
                values: new object[,]
                {
                    { 1, "Can only look", "Viewer", 1 },
                    { 2, "Can build, edit, lock and run", "Editor", 2 },
                    { 3, "Everything, including the team", "Admin", 3 }
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Key", "Description", "Label", "SortOrder" },
                values: new object[,]
                {
                    { "members.manage", "Invite, change and remove members", "Manage team", 6 },
                    { "workflows.create", "Create new workflows", "Create", 2 },
                    { "workflows.delete", "Delete a workflow (only when unlocked)", "Delete", 5 },
                    { "workflows.edit", "Save changes, turn Active on/off, lock and unlock", "Edit & lock", 3 },
                    { "workflows.run", "Run a workflow manually", "Run", 4 },
                    { "workflows.view", "See workflows and open them read-only", "View", 1 }
                });

            migrationBuilder.InsertData(
                table: "PermissionPresetItems",
                columns: new[] { "PermissionKey", "PresetId" },
                values: new object[,]
                {
                    { "workflows.view", 1 },
                    { "workflows.create", 2 },
                    { "workflows.edit", 2 },
                    { "workflows.run", 2 },
                    { "workflows.view", 2 },
                    { "members.manage", 3 },
                    { "workflows.create", 3 },
                    { "workflows.delete", 3 },
                    { "workflows.edit", 3 },
                    { "workflows.run", 3 },
                    { "workflows.view", 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvitationPermissions_PermissionKey",
                table: "InvitationPermissions",
                column: "PermissionKey");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPermissions_PermissionKey",
                table: "MembershipPermissions",
                column: "PermissionKey");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionPresetItems_PermissionKey",
                table: "PermissionPresetItems",
                column: "PermissionKey");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionPresets_Name",
                table: "PermissionPresets",
                column: "Name",
                unique: true);

            // Copy every existing grant out of the old JSON columns. Rows with invalid JSON are skipped.
            // Only keys that exist in the catalog are copied (the WHERE on Permissions).
            migrationBuilder.Sql(@"
                INSERT INTO MembershipPermissions (MembershipId, PermissionKey)
                SELECT DISTINCT m.Id, j.value
                FROM AccountMemberships m
                CROSS APPLY OPENJSON(CASE WHEN ISJSON(m.PermissionsJson) = 1 THEN m.PermissionsJson ELSE '[]' END) j
                WHERE j.value IN (SELECT [Key] FROM Permissions);

                INSERT INTO InvitationPermissions (InvitationId, PermissionKey)
                SELECT DISTINCT i.Id, j.value
                FROM Invitations i
                CROSS APPLY OPENJSON(CASE WHEN ISJSON(i.PermissionsJson) = 1 THEN i.PermissionsJson ELSE '[]' END) j
                WHERE j.value IN (SELECT [Key] FROM Permissions);");

            migrationBuilder.DropColumn(
                name: "PermissionsJson",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "PermissionsJson",
                table: "AccountMemberships");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PermissionsJson",
                table: "Invitations",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PermissionsJson",
                table: "AccountMemberships",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // Rebuild the JSON columns from the mapping tables before they are dropped.
            migrationBuilder.Sql(@"
                UPDATE m SET m.PermissionsJson = '[' + ISNULL((SELECT STRING_AGG('""' + g.PermissionKey + '""', ',')
                    FROM MembershipPermissions g WHERE g.MembershipId = m.Id), '') + ']'
                FROM AccountMemberships m;

                UPDATE i SET i.PermissionsJson = '[' + ISNULL((SELECT STRING_AGG('""' + g.PermissionKey + '""', ',')
                    FROM InvitationPermissions g WHERE g.InvitationId = i.Id), '') + ']'
                FROM Invitations i;");

            migrationBuilder.DropTable(
                name: "InvitationPermissions");

            migrationBuilder.DropTable(
                name: "MembershipPermissions");

            migrationBuilder.DropTable(
                name: "PermissionPresetItems");

            migrationBuilder.DropTable(
                name: "PermissionPresets");

            migrationBuilder.DropTable(
                name: "Permissions");
        }
    }
}
