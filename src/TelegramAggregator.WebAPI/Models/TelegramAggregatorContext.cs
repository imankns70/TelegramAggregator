using Microsoft.EntityFrameworkCore;
using YourNamespace.Entities;

namespace TelegramAggregator.WebAPI.Models
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
        }
    }
}
