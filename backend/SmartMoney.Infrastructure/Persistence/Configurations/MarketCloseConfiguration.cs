using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartMoney.Domain.Entities;

namespace SmartMoney.Infrastructure.Persistence.Configurations;

public class MarketCloseConfiguration : IEntityTypeConfiguration<MarketClose>
{
    public void Configure(EntityTypeBuilder<MarketClose> builder)
    {
        builder.ToTable("market_close");

        builder.HasKey(x => new { x.Date, x.Symbol });

        builder.Property(x => x.Date)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.Symbol)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Close)
            .IsRequired();
    }
}