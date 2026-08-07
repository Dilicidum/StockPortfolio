using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using StockPortfolio.Modules.MarketData.Infrastructure.Persistence;

#nullable disable

namespace StockPortfolio.Modules.MarketData.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MarketDataDbContext))]
    partial class MarketDataDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("marketdata")
                .HasAnnotation("ProductVersion", "10.0.10")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("StockPortfolio.Modules.MarketData.Domain.KeyRingEntry", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("FriendlyName")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("friendly_name");

                    b.Property<string>("Xml")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("xml");

                    b.HasKey("Id");

                    b.ToTable("data_protection_keys", "marketdata");
                });

            modelBuilder.Entity("StockPortfolio.Modules.MarketData.Domain.UserProviderKey", b =>
                {
                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.Property<string>("Ciphertext")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("ciphertext");

                    b.Property<string>("LastFour")
                        .IsRequired()
                        .HasMaxLength(4)
                        .HasColumnType("character varying(4)")
                        .HasColumnName("last_four");

                    b.Property<DateTimeOffset?>("LastRejectedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_rejected_at");

                    b.Property<DateTimeOffset>("SavedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("saved_at");

                    b.HasKey("UserId");

                    b.ToTable("user_provider_keys", "marketdata");
                });
#pragma warning restore 612, 618
        }
    }
}
