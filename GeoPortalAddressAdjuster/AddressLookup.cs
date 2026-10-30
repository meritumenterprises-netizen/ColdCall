using GeoPortalAddressImporter;

namespace GeoPortalAddressAdjuster;

public sealed class AddressLookup
{
    public required GeoportalAddressRequest Company { get; init; }
    public required GeoportalAddressResult Result { get; init; }
}
