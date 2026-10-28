using GeoPortalAddressImporter;
using ImportCEIDGByDate.Data.FirmyTemp;

namespace ImportCEIDGByDate;

internal sealed class PreparedCompanyAddress
{
    public int Index { get; set; }
    public required Company Company { get; init; }
    public required GeoportalAddressRequest Request { get; init; }
    public required GeoportalAddressResult Result { get; init; }
}
