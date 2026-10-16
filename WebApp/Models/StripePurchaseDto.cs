namespace ColdCall.Models;

public record StripePurchaseDto(string PlanCode, int MaxNumberOfSearches, int? MaxNumberOfCSVExportsPerSearch,
    int? MaxNumberOfExportedRecords, decimal AmountPaid, DateTime PaidAtUtc);
