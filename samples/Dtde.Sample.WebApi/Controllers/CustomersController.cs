using Dtde.Sample.WebApi.Data;
using Dtde.Sample.WebApi.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Sample.WebApi.Controllers;

/// <summary>
/// API controller for managing customers.
/// Demonstrates REGULAR EF Core usage (no temporal versioning).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly SampleDbContext _context;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(SampleDbContext context, ILogger<CustomersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Gets all customers using standard EF Core query.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Customer>>> GetCustomers()
    {
        // Standard EF Core - no temporal filtering
        var customers = await _context.Customers
            .OrderBy(c => c.Name)
            .ToListAsync();

        return Ok(customers);
    }

    /// <summary>
    /// Gets a customer by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> GetCustomer(int id)
    {
        var customer = await _context.Customers.FindAsync(id);

        if (customer is null)
        {
            return NotFound();
        }

        return Ok(customer);
    }

    /// <summary>
    /// Creates a new customer.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Customer>> CreateCustomer(CreateCustomerRequest request)
    {
        // Check for duplicate email
        var existing = await _context.Customers
            .AnyAsync(c => c.Email == request.Email);

        if (existing)
        {
            return Conflict($"Customer with email {request.Email} already exists.");
        }

        var customer = new Customer
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            CreatedAt = DateTime.UtcNow
        };

        // Standard EF Core Add - no temporal handling needed
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created customer {CustomerId}: {CustomerName}", customer.Id, customer.Name);

        return CreatedAtAction(nameof(GetCustomer), new { id = customer.Id }, customer);
    }

    /// <summary>
    /// Updates a customer (standard update, NOT versioned).
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Customer>> UpdateCustomer(int id, UpdateCustomerRequest request)
    {
        var customer = await _context.Customers.FindAsync(id);

        if (customer is null)
        {
            return NotFound();
        }

        // Standard EF Core update - overwrites the record
        customer.Name = request.Name;
        customer.Email = request.Email;
        customer.Phone = request.Phone;
        customer.Address = request.Address;
        customer.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated customer {CustomerId}", customer.Id);

        return Ok(customer);
    }

    /// <summary>
    /// Deletes a customer (permanent delete, NOT temporal termination).
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        var customer = await _context.Customers.FindAsync(id);

        if (customer is null)
        {
            return NotFound();
        }

        // Standard EF Core delete - permanently removes the record
        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted customer {CustomerId}", id);

        return NoContent();
    }
}

public record CreateCustomerRequest(
    string Name,
    string Email,
    string? Phone = null,
    string? Address = null);

public record UpdateCustomerRequest(
    string Name,
    string Email,
    string? Phone = null,
    string? Address = null);
