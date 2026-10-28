using ImportCEIDGByDate.Data.FirmyImport;
using ImportCEIDGByDate.Data.FirmyTemp;

namespace ImportCEIDGByDate;

internal sealed class PendingCompanyAddressImport
{
    public required Company Company { get; init; }
    public required ImportTarget Target { get; init; }
}
