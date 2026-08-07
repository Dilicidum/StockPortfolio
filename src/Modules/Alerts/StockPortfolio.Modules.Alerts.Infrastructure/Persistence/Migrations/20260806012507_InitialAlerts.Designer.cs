using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

#nullable disable

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AlertsDbContext))]
    [Migration("20260806012507_InitialAlerts")]
    partial class InitialAlerts
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("alerts")
                .HasAnnotation("ProductVersion", "10.0.10")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("StockPortfolio.Modules.Alerts.Domain.AlertSetting", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<bool>("Enabled")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(true)
                        .HasColumnName("enabled");

                    b.Property<decimal>("Threshold")
                        .HasPrecision(5, 2)
                        .HasColumnType("numeric(5,2)")
                        .HasColumnName("threshold_percent");

                    b.Property<string>("Ticker")
                        .IsRequired()
                        .HasMaxLength(5)
                        .HasColumnType("character varying(5)")
                        .HasColumnName("ticker");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.Property<int>("Window")
                        .HasColumnType("integer")
                        .HasColumnName("window_minutes");

                    b.HasKey("Id");

                    b.HasIndex("UserId", "Ticker")
                        .IsUnique()
                        .HasDatabaseName("ix_alert_settings_user_id_ticker");

                    b.ToTable("alert_settings", "alerts");
                });

            modelBuilder.Entity("StockPortfolio.Modules.Alerts.Domain.FiredAlert", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<decimal>("ChangePercent")
                        .HasPrecision(18, 6)
                        .HasColumnType("numeric(18,6)")
                        .HasColumnName("change_percent");

                    b.Property<string>("Direction")
                        .IsRequired()
                        .HasMaxLength(8)
                        .HasColumnType("character varying(8)")
                        .HasColumnName("direction");

                    b.Property<decimal>("EndpointPercent")
                        .HasPrecision(18, 6)
                        .HasColumnType("numeric(18,6)")
                        .HasColumnName("endpoint_percent");

                    b.Property<DateTimeOffset>("FiredAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("fired_at");

                    b.Property<bool>("IsSimulated")
                        .HasColumnType("boolean")
                        .HasColumnName("is_simulated");

                    b.Property<string>("Ticker")
                        .IsRequired()
                        .HasMaxLength(5)
                        .HasColumnType("character varying(5)")
                        .HasColumnName("ticker");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.ComplexProperty(typeof(Dictionary<string, object>), "ReferencePrice", "StockPortfolio.Modules.Alerts.Domain.FiredAlert.ReferencePrice#Money", b1 =>
                        {
                            b1.IsRequired();

                            b1.Property<decimal>("Amount")
                                .HasPrecision(18, 6)
                                .HasColumnType("numeric(18,6)")
                                .HasColumnName("reference_price_amount");

                            b1.Property<string>("Currency")
                                .IsRequired()
                                .HasMaxLength(3)
                                .HasColumnType("character(3)")
                                .HasColumnName("reference_price_currency")
                                .IsFixedLength();
                        });

                    b.ComplexProperty(typeof(Dictionary<string, object>), "TriggerPrice", "StockPortfolio.Modules.Alerts.Domain.FiredAlert.TriggerPrice#Money", b1 =>
                        {
                            b1.IsRequired();

                            b1.Property<decimal>("Amount")
                                .HasPrecision(18, 6)
                                .HasColumnType("numeric(18,6)")
                                .HasColumnName("trigger_price_amount");

                            b1.Property<string>("Currency")
                                .IsRequired()
                                .HasMaxLength(3)
                                .HasColumnType("character(3)")
                                .HasColumnName("trigger_price_currency")
                                .IsFixedLength();
                        });

                    b.HasKey("Id");

                    b.HasIndex("UserId", "FiredAt")
                        .IsDescending(false, true)
                        .HasDatabaseName("ix_fired_alerts_user_id_fired_at");

                    b.ToTable("fired_alerts", "alerts");
                });
#pragma warning restore 612, 618
        }
    }
}
