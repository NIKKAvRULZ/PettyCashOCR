using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PettyCashOCR.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIndexColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Index",
                table: "VoucherLineItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Index",
                table: "VoucherLineItems",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
