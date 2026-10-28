namespace OmniCommerce.Api.Domain;

// Minimal catalog entry used only to validate SKUs. It carries no price or stock.
public class Product
{
    private Product() { }

    public Product(Guid id, string sku, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Sku = sku;
        Name = name;
    }

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
}
