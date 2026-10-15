# PrngToFirmyTemp

Importer aktualnego ZIP PRNG Miejscowości SHP bezpośrednio do SQL Server `Firmy_temp`.

## Co robi
- otwiera ZIP bez ręcznego rozpakowywania;
- odczytuje `PRNG_MIEJSCOWOSCI_SHP.dbf` (nie wymaga biblioteki GIS);
- używa pól: `idPRNG`, `nazwaGlown`, `rodzaj`, `wspGeograf`, `wojewodz`, `powiat`, `gmina`;
- konwertuje `wspGeograf` DMS do Latitude/Longitude WGS84;
- tworzy `ColdCall.Adresy`, jeśli nie istnieje;
- dodaje brakujące nazwy do lookupów Wojewodztwa/Powiaty/Gminy/Miejscowosci;
- ładuje staging przez `SqlBulkCopy`;
- robi set-based INSERT/UPDATE `ColdCall.Adresy` według `PrngId`;
- zapisuje `Location` jako SQL Server `geography` SRID 4326.

## Wymagania
- .NET 8 SDK
- SQL Server z bazą `Firmy_temp`
- istniejące tabele `ColdCall.Wojewodztwa`, `Powiaty`, `Gminy`, `Miejscowosci` z kolumnami `Id int` i `Name nvarchar(...)`.

## Uruchomienie
1. Edytuj `appsettings.json` (Server/connection string).
2. Skopiuj ZIP PRNG do katalogu projektu albo ustaw pełną ścieżkę w `ZipPath`.
3. Uruchom:

    dotnet restore
    dotnet run

Możesz też podać alternatywny config: `dotnet run -- c:\sciezka\appsettings.json`.

## Uwaga o unikalności nazw
Importer stosuje dokładnie ustaloną regułę: nazwa w każdym lookupie jest traktowana jako globalnie unikalna. Nie dodaje rekordu, jeśli identyczne `Name` już istnieje.
