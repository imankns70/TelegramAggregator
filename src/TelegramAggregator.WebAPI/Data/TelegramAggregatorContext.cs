using Microsoft.EntityFrameworkCore;
using TelegramAggregator.WebAPI.Models.Entities;

namespace TelegramAggregator.WebAPI.Data
{
    public class TelegramAggregatorContext : DbContext
    {
        public TelegramAggregatorContext(DbContextOptions<TelegramAggregatorContext> options) : base(options)
        {

        }
        public DbSet<News> News { get; set; } = null!;
        public DbSet<NewsHistory> NewsHistories { get; set; } = null!;
        public DbSet<NewsStatus> NewsStatuses { get; set; } = null!;
        public DbSet<SportSource> SportSources { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // News
            modelBuilder.Entity<News>(entity =>
            {
                entity.ToTable(nameof(News));
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.GlobalKey).IsUnique(); // کلید یکتا برای جلوگیری از تکرار
                entity.Property(e => e.Score);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.ShortDescription).IsRequired();
                entity.Property(e => e.FullDescription);
                entity.Property(e => e.Link).IsRequired().IsRequired();
                entity.Property(e => e.PubDate).IsRequired();
                entity.Property(e => e.SportSourceId).IsRequired();

                // رابطه یک به چند با History
                entity.HasMany(n => n.Histories)
                      .WithOne(h => h.News)
                      .HasForeignKey(h => h.NewsId);

                entity.HasOne(n => n.NewsStatus)
                      .WithMany(h => h.News)
                      .HasForeignKey(h => h.NewsStatusId);

                entity.HasOne(n => n.SportSource)
                      .WithMany(h => h.News)
                      .HasForeignKey(h => h.SportSourceId);
            });

            modelBuilder.Entity<NewsHistory>(entity =>
            {
                entity.ToTable(nameof(NewsHistory));
                entity.HasKey(e => e.Id);
                entity.Property(e => e.NewsStatusId);
                entity.Property(e => e.NewsId);
                entity.Property(e => e.Reason).IsRequired().HasMaxLength(100);
                entity.Property(e => e.InsertDate);


                entity.HasOne(n => n.NewsStatus)
                      .WithMany(h => h.Histories)
                      .HasForeignKey(h => h.NewsStatusId);

                entity.HasOne(n => n.News)
                      .WithMany(h => h.Histories)
                      .HasForeignKey(h => h.NewsId);
            });

            modelBuilder.Entity<NewsStatus>(entity =>
            {
                entity.ToTable(nameof(NewsStatus));
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(50);

                entity.HasMany(n => n.Histories)
              .WithOne(h => h.NewsStatus)
              .HasForeignKey(h => h.NewsStatusId);

                entity.HasMany(n => n.News)
              .WithOne(h => h.NewsStatus)
              .HasForeignKey(h => h.NewsStatusId);
            });

            modelBuilder.Entity<SportSource>(entity =>
            {
                entity.ToTable(nameof(SportSource));
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(50);
                              

                entity.HasMany(n => n.News)
              .WithOne(h => h.SportSource)
              .HasForeignKey(h => h.SportSourceId);
            });

        }
    }
}
