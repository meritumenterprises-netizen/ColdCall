namespace PrgAddressImporter;

internal static class PointValidation
{
    public static void ValidateLengths(AddressPoint point)
    {
        Check("SourceId", point.SourceId, 50);
        Check("GmlId", point.GmlId, 500);
        Check("Terc", point.Terc, 7);
        Check("Miejscowosc", point.City, 200);
        Check("Ulica", point.Street, 300);
        Check("NrBudynku", point.Number, 50);
        Check("KodPocztowy", point.PostalCode, 20);
        Check("Simc", point.Simc, 7);
        // Some PRG exports contain the combined SIMC (7) + ULIC (5) identifier.
        Check("Ulic", point.Ulic, 32);
        Check("CityNorm", Normalization.Text(point.City), 200);
        Check("StreetNorm", Normalization.Street(point.Street), 300);
        Check("NumberNorm", Normalization.Number(point.Number), 50);

        void Check(string column, string value, int limit)
        {
            if (value.Length > limit)
                throw new InvalidDataException(
                    $"Przekroczona długość kolumny PRG {column}: {value.Length} znaków, limit {limit}, wartość '{value}'; punkt {point.GmlId}. Dane nie zostaną obcięte.");
        }
    }
}
