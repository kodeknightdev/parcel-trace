using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParcelTrace.Shipments.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sender_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    sender_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sender_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    recipient_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    recipient_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    pickup_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pickup_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pickup_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pickup_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    pickup_lat = table.Column<double>(type: "double precision", nullable: false),
                    pickup_lng = table.Column<double>(type: "double precision", nullable: false),
                    dropoff_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dropoff_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dropoff_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dropoff_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    dropoff_lat = table.Column<double>(type: "double precision", nullable: false),
                    dropoff_lng = table.Column<double>(type: "double precision", nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    length_cm = table.Column<int>(type: "integer", nullable: false),
                    width_cm = table.Column<int>(type: "integer", nullable: false),
                    height_cm = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ServiceLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DistanceKm = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CourierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourierName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SlaDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EtaAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EtaCalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PickedUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    pod_recipient_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    pod_signature_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    pod_photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pod_delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_CourierId",
                table: "shipments",
                column: "CourierId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_CustomerId",
                table: "shipments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_Status_CreatedAt",
                table: "shipments",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_TrackingNumber",
                table: "shipments",
                column: "TrackingNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipments");
        }
    }
}
