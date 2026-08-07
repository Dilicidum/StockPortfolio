using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

#nullable disable

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(PortfolioDbContext))]
    partial class PortfolioDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("portfolio")
                .HasAnnotation("ProductVersion", "10.0.10")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("StockPortfolio.Modules.Portfolio.Domain.DashboardSettings", b =>
                {
                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.Property<int>("RefreshInterval")
                        .HasColumnType("integer")
                        .HasColumnName("refresh_interval_seconds");

                    b.HasKey("UserId");

                    b.ToTable("dashboard_settings", "portfolio");
                });

            modelBuilder.Entity("StockPortfolio.Modules.Portfolio.Domain.Holding", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<bool>("IsVisible")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(true)
                        .HasColumnName("is_visible");

                    b.Property<decimal>("Quantity")
                        .HasPrecision(18, 6)
                        .HasColumnType("numeric(18,6)")
                        .HasColumnName("quantity");

                    b.Property<string>("Ticker")
                        .IsRequired()
                        .HasMaxLength(5)
                        .HasColumnType("character varying(5)")
                        .HasColumnName("ticker");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.ComplexProperty(typeof(Dictionary<string, object>), "AveragePrice", "StockPortfolio.Modules.Portfolio.Domain.Holding.AveragePrice#Money", b1 =>
                        {
                            b1.IsRequired();

                            b1.Property<decimal>("Amount")
                                .HasPrecision(18, 6)
                                .HasColumnType("numeric(18,6)")
                                .HasColumnName("avg_price_amount");

                            b1.Property<string>("Currency")
                                .IsRequired()
                                .HasMaxLength(3)
                                .HasColumnType("character(3)")
                                .HasColumnName("avg_price_currency")
                                .IsFixedLength();
                        });

                    b.HasKey("Id");

                    b.HasIndex("UserId", "Ticker")
                        .IsUnique()
                        .HasDatabaseName("ix_holdings_user_id_ticker");

                    b.ToTable("holdings", "portfolio");
                });
#pragma warning restore 612, 618
        }
    }
}
