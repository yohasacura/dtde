using Dtde.Sample.WebApi.Data;
using Dtde.Sample.WebApi.Entities;
using Dtde.Sample.WebApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Sample.WebApi.Controllers;

/// <summary>
/// API controller for managing contracts with temporal versioning.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ContractsController : ControllerBase
{
    private readonly SampleDbContext _context;
    private readonly ILogger<ContractsController> _logger;

    public ContractsController(SampleDbContext context, ILogger<ContractsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Gets all current contracts (valid as of now).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ContractResponse>>> GetContracts()
    {
        var contracts = await _context.ValidAt<Contract>(DateTime.UtcNow)
            .OrderBy(c => c.ContractNumber)
            .ToListAsync();

        return Ok(contracts.Select(c => c.ToResponse(DateTime.UtcNow)));
    }

    /// <summary>
    /// Gets contracts valid at a specific point in time.
    /// </summary>
    /// <param name="asOf">The point in time to query.</param>
    [HttpGet("as-of/{asOf:datetime}")]
    public async Task<ActionResult<IEnumerable<ContractResponse>>> GetContractsAsOf(DateTime asOf)
    {
        _logger.LogInformation("Querying contracts as of {AsOfDate}", asOf);

        var contracts = await _context.ValidAt<Contract>(asOf)
            .OrderBy(c => c.ContractNumber)
            .ToListAsync();

        return Ok(contracts.Select(c => c.ToResponse(asOf)));
    }

    /// <summary>
    /// Gets all versions of a contract.
    /// </summary>
    /// <param name="contractNumber">The contract number.</param>
    [HttpGet("{contractNumber}/history")]
    public async Task<ActionResult<IEnumerable<ContractResponse>>> GetContractHistory(string contractNumber)
    {
        var contracts = await _context.AllVersions<Contract>()
            .Where(c => c.ContractNumber == contractNumber)
            .OrderBy(c => c.ValidFrom)
            .ToListAsync();

        if (!contracts.Any())
        {
            return NotFound();
        }

        return Ok(contracts.Select(c => c.ToResponse()));
    }

    /// <summary>
    /// Gets a specific contract by contract number.
    /// </summary>
    /// <param name="contractNumber">The contract number.</param>
    [HttpGet("{contractNumber}")]
    public async Task<ActionResult<ContractResponse>> GetContract(string contractNumber)
    {
        var contract = await _context.ValidAt<Contract>(DateTime.UtcNow)
            .FirstOrDefaultAsync(c => c.ContractNumber == contractNumber);

        if (contract is null)
        {
            return NotFound();
        }

        return Ok(contract.ToResponse(DateTime.UtcNow));
    }

    /// <summary>
    /// Creates a new contract.
    /// </summary>
    /// <param name="request">The contract creation request.</param>
    [HttpPost]
    public async Task<ActionResult<ContractResponse>> CreateContract(CreateContractRequest request)
    {
        // Check for duplicate contract number
        var existing = await _context.AllVersions<Contract>()
            .AnyAsync(c => c.ContractNumber == request.ContractNumber);

        if (existing)
        {
            return Conflict($"Contract {request.ContractNumber} already exists.");
        }

        var contract = new Contract
        {
            ContractNumber = request.ContractNumber,
            CustomerName = request.CustomerName,
            Amount = request.Amount,
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "System" // Would come from user context in real app
        };

        _context.AddTemporal(contract, request.EffectiveFrom);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Created contract {ContractNumber} effective from {EffectiveFrom}",
            contract.ContractNumber,
            request.EffectiveFrom);

        return CreatedAtAction(
            nameof(GetContract),
            new { contractNumber = contract.ContractNumber },
            contract.ToResponse(DateTime.UtcNow));
    }

    /// <summary>
    /// Updates a contract, creating a new version.
    /// </summary>
    /// <param name="contractNumber">The contract number.</param>
    /// <param name="request">The update request.</param>
    [HttpPut("{contractNumber}")]
    public async Task<ActionResult<ContractResponse>> UpdateContract(
        string contractNumber,
        UpdateContractRequest request)
    {
        var currentContract = await _context.ValidAt<Contract>(DateTime.UtcNow)
            .FirstOrDefaultAsync(c => c.ContractNumber == contractNumber);

        if (currentContract is null)
        {
            return NotFound();
        }

        // Create a new version with the updated values
        var newVersion = _context.CreateNewVersion(
            currentContract,
            contract =>
            {
                contract.CustomerName = request.CustomerName;
                contract.Amount = request.Amount;
                contract.Status = request.Status;
                contract.ModifiedAt = DateTime.UtcNow;
                contract.ModifiedBy = "System";
            },
            request.EffectiveFrom);

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Created new version of contract {ContractNumber} effective from {EffectiveFrom}",
            contractNumber,
            request.EffectiveFrom);

        return Ok(newVersion.ToResponse(DateTime.UtcNow));
    }

    /// <summary>
    /// Terminates a contract as of a specific date.
    /// </summary>
    /// <param name="contractNumber">The contract number.</param>
    /// <param name="terminationDate">The termination date.</param>
    [HttpDelete("{contractNumber}")]
    public async Task<IActionResult> TerminateContract(
        string contractNumber,
        [FromQuery] DateTime? terminationDate = null)
    {
        var effectiveDate = terminationDate ?? DateTime.UtcNow;

        var currentContract = await _context.ValidAt<Contract>(DateTime.UtcNow)
            .FirstOrDefaultAsync(c => c.ContractNumber == contractNumber);

        if (currentContract is null)
        {
            return NotFound();
        }

        _context.Terminate(currentContract, effectiveDate);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Terminated contract {ContractNumber} as of {TerminationDate}",
            contractNumber,
            effectiveDate);

        return NoContent();
    }

    /// <summary>
    /// Compares a contract between two points in time.
    /// </summary>
    /// <param name="contractNumber">The contract number.</param>
    /// <param name="date1">First date.</param>
    /// <param name="date2">Second date.</param>
    [HttpGet("{contractNumber}/compare")]
    public async Task<ActionResult<object>> CompareContract(
        string contractNumber,
        [FromQuery] DateTime date1,
        [FromQuery] DateTime date2)
    {
        var contract1 = await _context.ValidAt<Contract>(date1)
            .FirstOrDefaultAsync(c => c.ContractNumber == contractNumber);

        var contract2 = await _context.ValidAt<Contract>(date2)
            .FirstOrDefaultAsync(c => c.ContractNumber == contractNumber);

        return Ok(new
        {
            ContractNumber = contractNumber,
            Date1 = new { AsOf = date1, Contract = contract1?.ToResponse(date1) },
            Date2 = new { AsOf = date2, Contract = contract2?.ToResponse(date2) },
            HasChanges = contract1?.Id != contract2?.Id
        });
    }
}
