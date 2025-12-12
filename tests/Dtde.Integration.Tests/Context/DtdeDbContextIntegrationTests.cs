using Dtde.Core.Metadata;
using Dtde.EntityFramework;
using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Extensions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Integration.Tests.Context;

public class DtdeDbContextIntegrationTests : IDisposable
{
    private readonly TestDbContext _context;

    public DtdeDbContextIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact(DisplayName = "ValidAt returns only entities valid at specified date")]
    public async Task ValidAt_ReturnsOnlyEntitiesValidAtSpecifiedDate()
    {
        // Arrange
        var contracts = new[]
        {
            new Contract { Id = 1, ContractNumber = "C001", ValidFrom = new DateTime(2024, 1, 1), ValidTo = new DateTime(2024, 6, 30) },
            new Contract { Id = 2, ContractNumber = "C002", ValidFrom = new DateTime(2024, 4, 1), ValidTo = new DateTime(2024, 12, 31) },
            new Contract { Id = 3, ContractNumber = "C003", ValidFrom = new DateTime(2024, 7, 1), ValidTo = null }
        };

        await _context.Contracts.AddRangeAsync(contracts);
        await _context.SaveChangesAsync();

        // Act
        var validAtMay = await _context.ValidAt<Contract>(new DateTime(2024, 5, 15)).ToListAsync();
        var validAtAugust = await _context.ValidAt<Contract>(new DateTime(2024, 8, 15)).ToListAsync();

        // Assert
        validAtMay.Should().HaveCount(2);
        validAtMay.Select(c => c.ContractNumber).Should().BeEquivalentTo("C001", "C002");

        validAtAugust.Should().HaveCount(2);
        validAtAugust.Select(c => c.ContractNumber).Should().BeEquivalentTo("C002", "C003");
    }

    [Fact(DisplayName = "ValidBetween returns entities overlapping with date range")]
    public async Task ValidBetween_ReturnsEntitiesOverlappingWithDateRange()
    {
        // Arrange
        var contracts = new[]
        {
            new Contract { Id = 1, ContractNumber = "C001", ValidFrom = new DateTime(2024, 1, 1), ValidTo = new DateTime(2024, 3, 31) },
            new Contract { Id = 2, ContractNumber = "C002", ValidFrom = new DateTime(2024, 4, 1), ValidTo = new DateTime(2024, 6, 30) },
            new Contract { Id = 3, ContractNumber = "C003", ValidFrom = new DateTime(2024, 7, 1), ValidTo = new DateTime(2024, 9, 30) },
            new Contract { Id = 4, ContractNumber = "C004", ValidFrom = new DateTime(2024, 10, 1), ValidTo = null }
        };

        await _context.Contracts.AddRangeAsync(contracts);
        await _context.SaveChangesAsync();

        // Act - Query for Q2 (April to June)
        var q2Contracts = await _context.ValidBetween<Contract>(
            new DateTime(2024, 4, 1),
            new DateTime(2024, 6, 30)).ToListAsync();

        // Assert
        q2Contracts.Should().HaveCount(1);
        q2Contracts[0].ContractNumber.Should().Be("C002");
    }

    [Fact(DisplayName = "AllVersions returns all entity versions without filtering")]
    public async Task AllVersions_ReturnsAllVersions_WithoutFiltering()
    {
        // Arrange - Multiple versions of the same contract
        var contracts = new[]
        {
            new Contract { Id = 1, ContractNumber = "C001", Amount = 1000, ValidFrom = new DateTime(2024, 1, 1), ValidTo = new DateTime(2024, 3, 31) },
            new Contract { Id = 2, ContractNumber = "C001", Amount = 1500, ValidFrom = new DateTime(2024, 4, 1), ValidTo = new DateTime(2024, 6, 30) },
            new Contract { Id = 3, ContractNumber = "C001", Amount = 2000, ValidFrom = new DateTime(2024, 7, 1), ValidTo = null }
        };

        await _context.Contracts.AddRangeAsync(contracts);
        await _context.SaveChangesAsync();

        // Act
        var allVersions = await _context.AllVersions<Contract>()
            .Where(c => c.ContractNumber == "C001")
            .OrderBy(c => c.ValidFrom)
            .ToListAsync();

        // Assert
        allVersions.Should().HaveCount(3);
        allVersions[0].Amount.Should().Be(1000);
        allVersions[1].Amount.Should().Be(1500);
        allVersions[2].Amount.Should().Be(2000);
    }

    [Fact(DisplayName = "AddTemporal initializes entity with temporal validity")]
    public async Task AddTemporal_InitializesEntityWithTemporalValidity()
    {
        // Arrange
        var effectiveDate = new DateTime(2024, 6, 1);
        var contract = new Contract
        {
            ContractNumber = "C-NEW",
            CustomerName = "New Customer",
            Amount = 5000
        };

        // Act
        _context.AddTemporal(contract, effectiveDate);
        await _context.SaveChangesAsync();

        // Assert
        var saved = await _context.Contracts.FirstAsync(c => c.ContractNumber == "C-NEW");
        saved.ValidFrom.Should().Be(effectiveDate);
        saved.ValidTo.Should().BeNull();
    }

    [Fact(DisplayName = "Terminate sets ValidTo on entity")]
    public async Task Terminate_SetsValidToOnEntity()
    {
        // Arrange
        var contract = new Contract
        {
            ContractNumber = "C-TERM",
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };

        await _context.Contracts.AddAsync(contract);
        await _context.SaveChangesAsync();

        var terminationDate = new DateTime(2024, 12, 31);

        // Act
        _context.Terminate(contract, terminationDate);
        await _context.SaveChangesAsync();

        // Assert
        var updated = await _context.Contracts.FirstAsync(c => c.ContractNumber == "C-TERM");
        updated.ValidTo.Should().Be(terminationDate);
    }

    [Fact(DisplayName = "CreateNewVersion terminates current and creates new version")]
    public async Task CreateNewVersion_TerminatesCurrentAndCreatesNewVersion()
    {
        // Arrange
        var original = new Contract
        {
            ContractNumber = "C-VERSION",
            CustomerName = "Original Customer",
            Amount = 1000,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };

        await _context.Contracts.AddAsync(original);
        await _context.SaveChangesAsync();

        var newEffectiveDate = new DateTime(2024, 7, 1);

        // Act
        var newVersion = _context.CreateNewVersion(original, c =>
        {
            c.Amount = 1500;
            c.CustomerName = "Updated Customer";
        }, newEffectiveDate);

        await _context.SaveChangesAsync();

        // Assert
        var versions = await _context.Contracts
            .Where(c => c.ContractNumber == "C-VERSION")
            .OrderBy(c => c.ValidFrom)
            .ToListAsync();

        versions.Should().HaveCount(2);

        // Original version should be terminated
        versions[0].Amount.Should().Be(1000);
        versions[0].ValidTo.Should().Be(newEffectiveDate.AddTicks(-1));

        // New version should have updated values
        versions[1].Amount.Should().Be(1500);
        versions[1].CustomerName.Should().Be("Updated Customer");
        versions[1].ValidFrom.Should().Be(newEffectiveDate);
        versions[1].ValidTo.Should().BeNull();
    }

    [Fact(DisplayName = "ValidAt returns non-temporal entities unchanged")]
    public async Task ValidAt_ReturnsNonTemporalEntities_Unchanged()
    {
        // Arrange - AuditLog is not configured with temporal validity
        var logs = new[]
        {
            new AuditLog { Id = 1, Action = "Create", Timestamp = DateTime.Now },
            new AuditLog { Id = 2, Action = "Update", Timestamp = DateTime.Now }
        };

        await _context.AuditLogs.AddRangeAsync(logs);
        await _context.SaveChangesAsync();

        // Act - ValidAt on non-temporal entity should return all
        var result = await _context.ValidAt<AuditLog>(DateTime.Now).ToListAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Test DbContext for integration tests.
/// </summary>
public class TestDbContext : DtdeDbContext
{
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public TestDbContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        optionsBuilder.UseDtde(dtde =>
        {
            dtde.ConfigureEntity<Contract>(entity =>
            {
                entity.HasTemporalValidity(
                    validFrom: nameof(Contract.ValidFrom),
                    validTo: nameof(Contract.ValidTo));
            });

            dtde.EnableTestMode();
        });
    }
}

public class Contract
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
