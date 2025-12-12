namespace Dtde.Benchmarks.Entities;

/// <summary>
/// Product entity for single table benchmarks.
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? SubCategory { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public List<OrderItem> OrderItems { get; set; } = [];
    public ProductDetails? Details { get; set; }
}

/// <summary>
/// Sharded product entity - hash sharded for even distribution.
/// </summary>
public class ShardedProduct
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? SubCategory { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public List<ShardedOrderItem> OrderItems { get; set; } = [];
    public ShardedProductDetails? Details { get; set; }
}

/// <summary>
/// Nested product details for deep navigation benchmarks.
/// </summary>
public class ProductDetails
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string? Manufacturer { get; set; }
    public string? ModelNumber { get; set; }
    public double Weight { get; set; }
    public string? Dimensions { get; set; }
    public string? WarrantyInfo { get; set; }
    public string? TechnicalSpecs { get; set; }

    public Product? Product { get; set; }
    public List<ProductAttribute> Attributes { get; set; } = [];
}

/// <summary>
/// Sharded product details.
/// </summary>
public class ShardedProductDetails
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string? Manufacturer { get; set; }
    public string? ModelNumber { get; set; }
    public double Weight { get; set; }
    public string? Dimensions { get; set; }
    public string? WarrantyInfo { get; set; }
    public string? TechnicalSpecs { get; set; }

    public ShardedProduct? Product { get; set; }
    public List<ShardedProductAttribute> Attributes { get; set; } = [];
}

/// <summary>
/// Product attribute for deeply nested queries.
/// </summary>
public class ProductAttribute
{
    public int Id { get; set; }
    public int ProductDetailsId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Unit { get; set; }

    public ProductDetails? ProductDetails { get; set; }
}

/// <summary>
/// Sharded product attribute.
/// </summary>
public class ShardedProductAttribute
{
    public int Id { get; set; }
    public int ProductDetailsId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Unit { get; set; }

    public ShardedProductDetails? ProductDetails { get; set; }
}
