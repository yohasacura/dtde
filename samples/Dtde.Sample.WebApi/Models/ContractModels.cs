using Dtde.Sample.WebApi.Entities;

namespace Dtde.Sample.WebApi.Models;

/// <summary>
/// DTO for creating a new contract.
/// </summary>
public record CreateContractRequest(
    string ContractNumber,
    string CustomerName,
    decimal Amount,
    DateTime EffectiveFrom);

/// <summary>
/// DTO for updating a contract with a new version.
/// </summary>
public record UpdateContractRequest(
    int Id,
    string CustomerName,
    decimal Amount,
    string Status,
    DateTime EffectiveFrom);

/// <summary>
/// DTO for contract response.
/// </summary>
public record ContractResponse(
    int Id,
    string ContractNumber,
    string CustomerName,
    decimal Amount,
    string Status,
    DateTime ValidFrom,
    DateTime? ValidTo,
    bool IsCurrent);

/// <summary>
/// Extension methods for mapping entities to DTOs.
/// </summary>
public static class ContractMappings
{
    public static ContractResponse ToResponse(this Contract contract, DateTime? asOfDate = null)
    {
        var isCurrent = contract.ValidTo is null ||
            (asOfDate.HasValue && contract.ValidFrom <= asOfDate && contract.ValidTo > asOfDate);

        return new ContractResponse(
            contract.Id,
            contract.ContractNumber,
            contract.CustomerName,
            contract.Amount,
            contract.Status,
            contract.ValidFrom,
            contract.ValidTo,
            isCurrent);
    }
}
