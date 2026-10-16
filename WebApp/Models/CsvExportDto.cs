namespace ColdCall.Models;

public record CsvExportDto(int Id, int UserId, int SearchProfileId, string CsvFileName, DateTime CreatedOn, int? NumberOfRecords);
