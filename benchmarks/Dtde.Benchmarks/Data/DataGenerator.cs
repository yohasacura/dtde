using Bogus;
using Dtde.Benchmarks.Entities;

namespace Dtde.Benchmarks.Data;

/// <summary>
/// Generates realistic test data for benchmarks using Bogus library.
/// </summary>
public static class DataGenerator
{
    private static readonly string[] Regions = ["US", "EU", "APAC", "LATAM", "MEA"];
    private static readonly string[] Categories = ["Electronics", "Clothing", "Home", "Sports", "Books", "Toys", "Food", "Health"];
    private static readonly string[] Currencies = ["USD", "EUR", "GBP", "JPY", "CNY"];

    /// <summary>
    /// Generates a list of customers for benchmarks.
    /// </summary>
    public static List<Customer> GenerateCustomers(int count, int seed = 12345)
    {
        var faker = new Faker<Customer>()
            .UseSeed(seed)
            .RuleFor(c => c.Id, f => f.IndexFaker + 1)
            .RuleFor(c => c.Name, f => f.Name.FullName())
            .RuleFor(c => c.Email, f => $"customer_{f.IndexFaker + 1}@benchmark.test")
            .RuleFor(c => c.Region, f => f.PickRandom(Regions))
            .RuleFor(c => c.Phone, f => f.Phone.PhoneNumber())
            .RuleFor(c => c.Address, f => f.Address.StreetAddress())
            .RuleFor(c => c.City, f => f.Address.City())
            .RuleFor(c => c.Country, f => f.Address.Country())
            .RuleFor(c => c.Tier, f => f.PickRandom<CustomerTier>())
            .RuleFor(c => c.CreatedAt, f => f.Date.Past(3))
            .RuleFor(c => c.IsActive, f => f.Random.Bool(0.95f));

        return faker.Generate(count);
    }

    /// <summary>
    /// Generates sharded customers.
    /// </summary>
    public static List<ShardedCustomer> GenerateShardedCustomers(int count, int seed = 12345)
    {
        var faker = new Faker<ShardedCustomer>()
            .UseSeed(seed)
            .RuleFor(c => c.Id, f => f.IndexFaker + 1)
            .RuleFor(c => c.Name, f => f.Name.FullName())
            .RuleFor(c => c.Email, f => $"sharded_{f.IndexFaker + 1}@benchmark.test")
            .RuleFor(c => c.Region, f => f.PickRandom(Regions))
            .RuleFor(c => c.Phone, f => f.Phone.PhoneNumber())
            .RuleFor(c => c.Address, f => f.Address.StreetAddress())
            .RuleFor(c => c.City, f => f.Address.City())
            .RuleFor(c => c.Country, f => f.Address.Country())
            .RuleFor(c => c.Tier, f => f.PickRandom<CustomerTier>())
            .RuleFor(c => c.CreatedAt, f => f.Date.Past(3))
            .RuleFor(c => c.IsActive, f => f.Random.Bool(0.95f));

        return faker.Generate(count);
    }

    /// <summary>
    /// Generates orders for benchmarks.
    /// </summary>
    public static List<Order> GenerateOrders(int count, List<Customer> customers, int seed = 12345)
    {
        var orderNumber = 0;
        var faker = new Faker<Order>()
            .UseSeed(seed)
            .RuleFor(o => o.Id, f => f.IndexFaker + 1)
            .RuleFor(o => o.OrderNumber, _ => $"ORD-{++orderNumber:D8}")
            .RuleFor(o => o.CustomerId, f => f.PickRandom(customers).Id)
            .RuleFor(o => o.Region, (f, o) => customers.First(c => c.Id == o.CustomerId).Region)
            .RuleFor(o => o.OrderDate, f => f.Date.Past(2))
            .RuleFor(o => o.TotalAmount, f => f.Finance.Amount(10, 5000))
            .RuleFor(o => o.Currency, f => f.PickRandom(Currencies))
            .RuleFor(o => o.Status, f => f.PickRandom<OrderStatus>())
            .RuleFor(o => o.ShippingAddress, f => f.Address.FullAddress())
            .RuleFor(o => o.CreatedAt, (f, o) => o.OrderDate)
            .RuleFor(o => o.ProcessedAt, (f, o) => o.Status >= OrderStatus.Processing ? o.OrderDate.AddHours(f.Random.Int(1, 48)) : null);

        return faker.Generate(count);
    }

    /// <summary>
    /// Generates sharded orders.
    /// </summary>
    public static List<ShardedOrder> GenerateShardedOrders(int count, List<ShardedCustomer> customers, int seed = 12345)
    {
        var orderNumber = 0;
        var faker = new Faker<ShardedOrder>()
            .UseSeed(seed)
            .RuleFor(o => o.Id, f => f.IndexFaker + 1)
            .RuleFor(o => o.OrderNumber, _ => $"ORD-{++orderNumber:D8}")
            .RuleFor(o => o.CustomerId, f => f.PickRandom(customers).Id)
            .RuleFor(o => o.Region, (f, o) => customers.First(c => c.Id == o.CustomerId).Region)
            .RuleFor(o => o.OrderDate, f => f.Date.Past(2))
            .RuleFor(o => o.TotalAmount, f => f.Finance.Amount(10, 5000))
            .RuleFor(o => o.Currency, f => f.PickRandom(Currencies))
            .RuleFor(o => o.Status, f => f.PickRandom<OrderStatus>())
            .RuleFor(o => o.ShippingAddress, f => f.Address.FullAddress())
            .RuleFor(o => o.CreatedAt, (f, o) => o.OrderDate)
            .RuleFor(o => o.ProcessedAt, (f, o) => o.Status >= OrderStatus.Processing ? o.OrderDate.AddHours(f.Random.Int(1, 48)) : null);

        return faker.Generate(count);
    }

    /// <summary>
    /// Generates order items.
    /// </summary>
    public static List<OrderItem> GenerateOrderItems(List<Order> orders, List<Product> products, int itemsPerOrder = 3, int seed = 12345)
    {
        var random = new Random(seed);
        var items = new List<OrderItem>();
        var itemId = 1L;

        foreach (var order in orders)
        {
            var itemCount = random.Next(1, itemsPerOrder + 1);
            var orderProducts = products.OrderBy(_ => random.Next()).Take(itemCount).ToList();

            foreach (var product in orderProducts)
            {
                items.Add(new OrderItem
                {
                    Id = itemId++,
                    OrderId = order.Id,
                    ProductSku = product.Sku,
                    ProductName = product.Name,
                    Quantity = random.Next(1, 5),
                    UnitPrice = product.Price,
                    Discount = random.Next(0, 20) / 100m * product.Price
                });
            }
        }

        return items;
    }

    /// <summary>
    /// Generates sharded order items.
    /// </summary>
    public static List<ShardedOrderItem> GenerateShardedOrderItems(
        List<ShardedOrder> orders,
        List<ShardedProduct> products,
        int itemsPerOrder = 3,
        int seed = 12345)
    {
        var random = new Random(seed);
        var items = new List<ShardedOrderItem>();
        var itemId = 1L;

        foreach (var order in orders)
        {
            var itemCount = random.Next(1, itemsPerOrder + 1);
            var orderProducts = products.OrderBy(_ => random.Next()).Take(itemCount).ToList();

            foreach (var product in orderProducts)
            {
                items.Add(new ShardedOrderItem
                {
                    Id = itemId++,
                    OrderId = order.Id,
                    Region = order.Region, // Co-located with order
                    ProductSku = product.Sku,
                    ProductName = product.Name,
                    Quantity = random.Next(1, 5),
                    UnitPrice = product.Price,
                    Discount = random.Next(0, 20) / 100m * product.Price
                });
            }
        }

        return items;
    }

    /// <summary>
    /// Generates products.
    /// </summary>
    public static List<Product> GenerateProducts(int count, int seed = 12345)
    {
        var faker = new Faker<Product>()
            .UseSeed(seed)
            .RuleFor(p => p.Id, f => f.IndexFaker + 1)
            .RuleFor(p => p.Sku, f => f.Commerce.Ean13())
            .RuleFor(p => p.Name, f => f.Commerce.ProductName())
            .RuleFor(p => p.Category, f => f.PickRandom(Categories))
            .RuleFor(p => p.SubCategory, f => f.Commerce.Categories(1).First())
            .RuleFor(p => p.Price, f => f.Finance.Amount(5, 500))
            .RuleFor(p => p.StockQuantity, f => f.Random.Int(0, 1000))
            .RuleFor(p => p.Description, f => f.Commerce.ProductDescription())
            .RuleFor(p => p.IsActive, f => f.Random.Bool(0.9f))
            .RuleFor(p => p.CreatedAt, f => f.Date.Past(2));

        return faker.Generate(count);
    }

    /// <summary>
    /// Generates sharded products.
    /// </summary>
    public static List<ShardedProduct> GenerateShardedProducts(int count, int seed = 12345)
    {
        var faker = new Faker<ShardedProduct>()
            .UseSeed(seed)
            .RuleFor(p => p.Id, f => f.IndexFaker + 1)
            .RuleFor(p => p.Sku, f => f.Commerce.Ean13())
            .RuleFor(p => p.Name, f => f.Commerce.ProductName())
            .RuleFor(p => p.Category, f => f.PickRandom(Categories))
            .RuleFor(p => p.SubCategory, f => f.Commerce.Categories(1).First())
            .RuleFor(p => p.Price, f => f.Finance.Amount(5, 500))
            .RuleFor(p => p.StockQuantity, f => f.Random.Int(0, 1000))
            .RuleFor(p => p.Description, f => f.Commerce.ProductDescription())
            .RuleFor(p => p.IsActive, f => f.Random.Bool(0.9f))
            .RuleFor(p => p.CreatedAt, f => f.Date.Past(2));

        return faker.Generate(count);
    }

    /// <summary>
    /// Generates product details with nested attributes.
    /// </summary>
    public static List<ProductDetails> GenerateProductDetails(List<Product> products, int seed = 12345)
    {
        var random = new Random(seed);
        var details = new List<ProductDetails>();
        var detailId = 1;
        var attrId = 1;

        foreach (var product in products)
        {
            var detail = new ProductDetails
            {
                Id = detailId++,
                ProductId = product.Id,
                Manufacturer = new Faker().Company.CompanyName(),
                ModelNumber = $"MDL-{random.Next(10000, 99999)}",
                Weight = Math.Round(random.NextDouble() * 10 + 0.1, 2),
                Dimensions = $"{random.Next(5, 50)}x{random.Next(5, 50)}x{random.Next(5, 50)} cm",
                WarrantyInfo = $"{random.Next(1, 5)} year warranty",
                TechnicalSpecs = new Faker().Lorem.Paragraph()
            };

            // Add 3-5 attributes per product
            var attrCount = random.Next(3, 6);
            for (var i = 0; i < attrCount; i++)
            {
                detail.Attributes.Add(new ProductAttribute
                {
                    Id = attrId++,
                    ProductDetailsId = detail.Id,
                    Name = new Faker().Commerce.ProductAdjective(),
                    Value = new Faker().Commerce.ProductMaterial(),
                    Unit = i % 3 == 0 ? "units" : null
                });
            }

            details.Add(detail);
        }

        return details;
    }

    /// <summary>
    /// Generates transactions for date-based sharding benchmarks.
    /// </summary>
    public static List<Transaction> GenerateTransactions(int count, int seed = 12345)
    {
        var refNumber = 0;
        var faker = new Faker<Transaction>()
            .UseSeed(seed)
            .RuleFor(t => t.Id, f => f.IndexFaker + 1)
            .RuleFor(t => t.TransactionRef, _ => $"TXN-{++refNumber:D10}")
            .RuleFor(t => t.AccountNumber, f => $"ACC-{f.Random.Int(10000, 99999)}")
            .RuleFor(t => t.TransactionDate, f => f.Date.Between(DateTime.Now.AddYears(-2), DateTime.Now))
            .RuleFor(t => t.Amount, f => f.Finance.Amount(1, 10000))
            .RuleFor(t => t.Type, f => f.PickRandom<TransactionType>())
            .RuleFor(t => t.Description, f => f.Finance.TransactionType())
            .RuleFor(t => t.Category, f => f.PickRandom(new[] { "Income", "Expense", "Transfer", "Fee" }))
            .RuleFor(t => t.Merchant, f => f.Company.CompanyName())
            .RuleFor(t => t.BalanceBefore, f => f.Finance.Amount(100, 50000))
            .RuleFor(t => t.BalanceAfter, (f, t) => t.Type == TransactionType.Credit ? t.BalanceBefore + t.Amount : t.BalanceBefore - t.Amount)
            .RuleFor(t => t.Status, _ => "Completed")
            .RuleFor(t => t.CreatedAt, (f, t) => t.TransactionDate);

        return faker.Generate(count);
    }

    /// <summary>
    /// Generates sharded transactions.
    /// </summary>
    public static List<ShardedTransaction> GenerateShardedTransactions(int count, int seed = 12345)
    {
        var refNumber = 0;
        var faker = new Faker<ShardedTransaction>()
            .UseSeed(seed)
            .RuleFor(t => t.Id, f => f.IndexFaker + 1)
            .RuleFor(t => t.TransactionRef, _ => $"TXN-{++refNumber:D10}")
            .RuleFor(t => t.AccountNumber, f => $"ACC-{f.Random.Int(10000, 99999)}")
            .RuleFor(t => t.TransactionDate, f => f.Date.Between(DateTime.Now.AddYears(-2), DateTime.Now))
            .RuleFor(t => t.Amount, f => f.Finance.Amount(1, 10000))
            .RuleFor(t => t.Type, f => f.PickRandom<TransactionType>())
            .RuleFor(t => t.Description, f => f.Finance.TransactionType())
            .RuleFor(t => t.Category, f => f.PickRandom(new[] { "Income", "Expense", "Transfer", "Fee" }))
            .RuleFor(t => t.Merchant, f => f.Company.CompanyName())
            .RuleFor(t => t.BalanceBefore, f => f.Finance.Amount(100, 50000))
            .RuleFor(t => t.BalanceAfter, (f, t) => t.Type == TransactionType.Credit ? t.BalanceBefore + t.Amount : t.BalanceBefore - t.Amount)
            .RuleFor(t => t.Status, _ => "Completed")
            .RuleFor(t => t.CreatedAt, (f, t) => t.TransactionDate);

        return faker.Generate(count);
    }

    /// <summary>
    /// Generates customer profiles with nested preferences.
    /// </summary>
    public static List<CustomerProfile> GenerateCustomerProfiles(List<Customer> customers, int seed = 12345)
    {
        var random = new Random(seed);
        var profiles = new List<CustomerProfile>();
        var profileId = 1;
        var prefId = 1;

        foreach (var customer in customers)
        {
            var profile = new CustomerProfile
            {
                Id = profileId++,
                CustomerId = customer.Id,
                AvatarUrl = $"https://avatars.example.com/{customer.Id}.png",
                Bio = new Faker().Lorem.Paragraph(),
                DateOfBirth = new Faker().Date.Past(50, DateTime.Now.AddYears(-18)),
                PreferredLanguage = new Faker().Random.ArrayElement(new[] { "en", "es", "fr", "de", "zh" }),
                PreferredCurrency = new Faker().PickRandom(Currencies),
                EmailNotifications = random.NextDouble() > 0.2,
                SmsNotifications = random.NextDouble() > 0.5,
                LoyaltyPoints = random.Next(0, 10000),
                Preferences = new CustomerPreferences
                {
                    Id = prefId++,
                    CustomerProfileId = profileId - 1,
                    Theme = new Faker().Random.ArrayElement(new[] { "light", "dark", "system" }),
                    FontSize = new Faker().Random.ArrayElement(new[] { "small", "medium", "large" }),
                    DarkMode = random.NextDouble() > 0.5,
                    DefaultShippingMethod = new Faker().Random.ArrayElement(new[] { "standard", "express", "overnight" }),
                    DefaultPaymentMethod = new Faker().Random.ArrayElement(new[] { "credit_card", "paypal", "bank_transfer" })
                }
            };

            profiles.Add(profile);
        }

        return profiles;
    }

    /// <summary>
    /// Generates sharded customer profiles with nested preferences.
    /// </summary>
    public static List<ShardedCustomerProfile> GenerateShardedCustomerProfiles(List<ShardedCustomer> customers, int seed = 12345)
    {
        var random = new Random(seed);
        var profiles = new List<ShardedCustomerProfile>();
        var profileId = 1;
        var prefId = 1;

        foreach (var customer in customers)
        {
            var profile = new ShardedCustomerProfile
            {
                Id = profileId++,
                CustomerId = customer.Id,
                Region = customer.Region, // Co-located
                AvatarUrl = $"https://avatars.example.com/{customer.Id}.png",
                Bio = new Faker().Lorem.Paragraph(),
                DateOfBirth = new Faker().Date.Past(50, DateTime.Now.AddYears(-18)),
                PreferredLanguage = new Faker().Random.ArrayElement(new[] { "en", "es", "fr", "de", "zh" }),
                PreferredCurrency = new Faker().PickRandom(Currencies),
                EmailNotifications = random.NextDouble() > 0.2,
                SmsNotifications = random.NextDouble() > 0.5,
                LoyaltyPoints = random.Next(0, 10000),
                Preferences = new ShardedCustomerPreferences
                {
                    Id = prefId++,
                    CustomerProfileId = profileId - 1,
                    Theme = new Faker().Random.ArrayElement(new[] { "light", "dark", "system" }),
                    FontSize = new Faker().Random.ArrayElement(new[] { "small", "medium", "large" }),
                    DarkMode = random.NextDouble() > 0.5,
                    DefaultShippingMethod = new Faker().Random.ArrayElement(new[] { "standard", "express", "overnight" }),
                    DefaultPaymentMethod = new Faker().Random.ArrayElement(new[] { "credit_card", "paypal", "bank_transfer" })
                }
            };

            profiles.Add(profile);
        }

        return profiles;
    }

    /// <summary>
    /// Generates sharded product details with nested attributes.
    /// </summary>
    public static List<ShardedProductDetails> GenerateShardedProductDetails(List<ShardedProduct> products, int seed = 12345)
    {
        var random = new Random(seed);
        var details = new List<ShardedProductDetails>();
        var detailId = 1;
        var attrId = 1;

        foreach (var product in products)
        {
            var detail = new ShardedProductDetails
            {
                Id = detailId++,
                ProductId = product.Id,
                Manufacturer = new Faker().Company.CompanyName(),
                ModelNumber = $"MDL-{random.Next(10000, 99999)}",
                Weight = Math.Round(random.NextDouble() * 10 + 0.1, 2),
                Dimensions = $"{random.Next(5, 50)}x{random.Next(5, 50)}x{random.Next(5, 50)} cm",
                WarrantyInfo = $"{random.Next(1, 5)} year warranty",
                TechnicalSpecs = new Faker().Lorem.Paragraph()
            };

            // Add 3-5 attributes per product
            var attrCount = random.Next(3, 6);
            for (var i = 0; i < attrCount; i++)
            {
                detail.Attributes.Add(new ShardedProductAttribute
                {
                    Id = attrId++,
                    ProductDetailsId = detail.Id,
                    Name = new Faker().Commerce.ProductAdjective(),
                    Value = new Faker().Commerce.ProductMaterial(),
                    Unit = i % 3 == 0 ? "units" : null
                });
            }

            details.Add(detail);
        }

        return details;
    }

    /// <summary>
    /// Generates transactions within a specific date range for date-based sharding benchmarks.
    /// </summary>
    public static List<Transaction> GenerateTransactions(int count, DateTime startDate, DateTime endDate, int seed = 12345)
    {
        var refNumber = 0;
        var faker = new Faker<Transaction>()
            .UseSeed(seed)
            .RuleFor(t => t.Id, f => f.IndexFaker + 1)
            .RuleFor(t => t.TransactionRef, _ => $"TXN-{++refNumber:D10}")
            .RuleFor(t => t.AccountNumber, f => $"ACC-{f.Random.Int(10000, 99999)}")
            .RuleFor(t => t.TransactionDate, f => f.Date.Between(startDate, endDate))
            .RuleFor(t => t.Amount, f => f.Finance.Amount(1, 10000))
            .RuleFor(t => t.Type, f => f.PickRandom<TransactionType>())
            .RuleFor(t => t.Description, f => f.Finance.TransactionType())
            .RuleFor(t => t.Category, f => f.PickRandom(new[] { "Income", "Expense", "Transfer", "Fee" }))
            .RuleFor(t => t.Merchant, f => f.Company.CompanyName())
            .RuleFor(t => t.BalanceBefore, f => f.Finance.Amount(100, 50000))
            .RuleFor(t => t.BalanceAfter, (f, t) => t.Type == TransactionType.Credit ? t.BalanceBefore + t.Amount : t.BalanceBefore - t.Amount)
            .RuleFor(t => t.Status, _ => "Completed")
            .RuleFor(t => t.CreatedAt, (f, t) => t.TransactionDate);

        return faker.Generate(count);
    }

    /// <summary>
    /// Generates sharded transactions within a specific date range.
    /// </summary>
    public static List<ShardedTransaction> GenerateShardedTransactions(int count, DateTime startDate, DateTime endDate, int seed = 12345)
    {
        var refNumber = 0;
        var faker = new Faker<ShardedTransaction>()
            .UseSeed(seed)
            .RuleFor(t => t.Id, f => f.IndexFaker + 1)
            .RuleFor(t => t.TransactionRef, _ => $"TXN-{++refNumber:D10}")
            .RuleFor(t => t.AccountNumber, f => $"ACC-{f.Random.Int(10000, 99999)}")
            .RuleFor(t => t.TransactionDate, f => f.Date.Between(startDate, endDate))
            .RuleFor(t => t.Amount, f => f.Finance.Amount(1, 10000))
            .RuleFor(t => t.Type, f => f.PickRandom<TransactionType>())
            .RuleFor(t => t.Description, f => f.Finance.TransactionType())
            .RuleFor(t => t.Category, f => f.PickRandom(new[] { "Income", "Expense", "Transfer", "Fee" }))
            .RuleFor(t => t.Merchant, f => f.Company.CompanyName())
            .RuleFor(t => t.BalanceBefore, f => f.Finance.Amount(100, 50000))
            .RuleFor(t => t.BalanceAfter, (f, t) => t.Type == TransactionType.Credit ? t.BalanceBefore + t.Amount : t.BalanceBefore - t.Amount)
            .RuleFor(t => t.Status, _ => "Completed")
            .RuleFor(t => t.CreatedAt, (f, t) => t.TransactionDate);

        return faker.Generate(count);
    }
}
