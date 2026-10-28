using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniCommerce.Api.Domain;

namespace OmniCommerce.Api.Infrastructure.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Sku).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(p => p.Sku).IsUnique();

        // Fixed IDs keep the seed (and the migration) deterministic.
        builder.HasData(
            new Product(Guid.Parse("a1000000-0000-0000-0000-000000000001"), "TSHIRT-BLK-M", "Black T-Shirt (M)"),
            new Product(Guid.Parse("a1000000-0000-0000-0000-000000000002"), "TSHIRT-WHT-L", "White T-Shirt (L)"),
            new Product(Guid.Parse("a1000000-0000-0000-0000-000000000003"), "MUG-CER-350", "Ceramic Mug 350 ml"),
            new Product(Guid.Parse("a1000000-0000-0000-0000-000000000004"), "NOTEBOOK-A5", "A5 Notebook"),
            new Product(Guid.Parse("a1000000-0000-0000-0000-000000000005"), "BOTTLE-STL-750", "Steel Bottle 750 ml"));
    }
}
