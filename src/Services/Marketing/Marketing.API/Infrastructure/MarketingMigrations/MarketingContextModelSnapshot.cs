﻿using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure.MarketingMigrations
{
    [DbContext(typeof(MarketingContext))]
    partial class MarketingContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.1.2")
                .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("Microsoft.eShopOnContainers.Services.Marketing.API.Model.Campaign", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnName("Description");

                    b.Property<string>("DetailsUri");

                    b.Property<DateTime>("From")
                        .HasColumnName("From");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnName("Name");

                    b.Property<string>("PictureName");

                    b.Property<string>("PictureUri")
                        .IsRequired()
                        .HasColumnName("PictureUri");

                    b.Property<DateTime>("To")
                        .HasColumnName("To");

                    b.HasKey("Id");

                    b.ToTable("Campaign");
                });

            modelBuilder.Entity("Microsoft.eShopOnContainers.Services.Marketing.API.Model.Rule", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("CampaignId");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnName("Description");

                    b.Property<int>("RuleTypeId");

                    b.HasKey("Id");

                    b.HasIndex("CampaignId");

                    b.ToTable("Rule");

                    b.HasDiscriminator<int>("RuleTypeId");
                });

            modelBuilder.Entity("Microsoft.eShopOnContainers.Services.Marketing.API.Model.PurchaseHistoryRule", b =>
                {
                    b.HasBaseType("Microsoft.eShopOnContainers.Services.Marketing.API.Model.Rule");

                    b.ToTable("PurchaseHistoryRule");

                    b.HasDiscriminator().HasValue(2);
                });

            modelBuilder.Entity("Microsoft.eShopOnContainers.Services.Marketing.API.Model.UserLocationRule", b =>
                {
                    b.HasBaseType("Microsoft.eShopOnContainers.Services.Marketing.API.Model.Rule");

                    b.Property<int>("LocationId")
                        .HasColumnName("LocationId");

                    b.ToTable("UserLocationRule");

                    b.HasDiscriminator().HasValue(3);
                });

            modelBuilder.Entity("Microsoft.eShopOnContainers.Services.Marketing.API.Model.UserProfileRule", b =>
                {
                    b.HasBaseType("Microsoft.eShopOnContainers.Services.Marketing.API.Model.Rule");

                    b.ToTable("UserProfileRule");

                    b.HasDiscriminator().HasValue(1);
                });

            modelBuilder.Entity("Microsoft.eShopOnContainers.Services.Marketing.API.Model.Rule", b =>
                {
                    b.HasOne("Microsoft.eShopOnContainers.Services.Marketing.API.Model.Campaign", "Campaign")
                        .WithMany("Rules")
                        .HasForeignKey("CampaignId")
                        .OnDelete(DeleteBehavior.Cascade);
                });
        }
    }
}
