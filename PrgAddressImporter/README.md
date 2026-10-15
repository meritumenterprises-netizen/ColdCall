# PRG Address Importer

Samodzielny projekt konsolowy C# / .NET 10 dopasowany do schematu bazy
`Firmy_temp` i plików znajdujących się w `C:\Users\plucz\Downloads`.
Nie wymaga rozpakowania ani kopiowania dużych ZIP-ów do katalogu aplikacji.

## Uruchomienie

```powershell
dotnet build .\PrgAddressImporter\PrgAddressImporter.csproj
dotnet run --project .\PrgAddressImporter -- self-test
dotnet run --project .\PrgAddressImporter -- validate
dotnet run --project .\PrgAddressImporter -- validate --full

# Etap 1: tylko lokalny słownik adresów; zapis do dbo.Terc i dbo.PrgAdresy.
dotnet run --project .\PrgAddressImporter -- import

# Etap 2: raport bez zmian tabel ColdCall; opcjonalny limit firm.
dotnet run --project .\PrgAddressImporter -- match --limit 1000

# Jawna zgoda na zapis adresów firm.
dotnet run --project .\PrgAddressImporter -- match --apply --limit 1000

# Pełny proces bez limitu firm.
dotnet run --project .\PrgAddressImporter -- all --apply
```

Domyślne połączenie używa Windows Authentication, serwera `localhost` i bazy
`Firmy_temp`. Można je zastąpić zmienną środowiskową:

```powershell
$env:PRG_SQL_CONNECTION = 'Server=localhost;Database=Firmy_temp;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project .\PrgAddressImporter -- import --prg 'D:\Dane\PRG.zip' --terc 'D:\Dane\TERC.zip' --batch 10000
```

`TrustServerCertificate=True` służy lokalnemu SQL Serverowi. Dla zdalnego
serwera z poprawnym certyfikatem należy ustawić `False`. Nie umieszczaj haseł
w repozytorium. Konto importera potrzebuje DDL dla własnych tabel `dbo`,
odczytu danych firm i, dla `--apply`, zapisu tabel adresowych i słowników.

## Dane i bezpieczeństwo

Kolumna `dbo.PrgAdresy.Ulic` ma typ `varchar(32)`, ponieważ rzeczywiste pliki
PRG mogą zawierać 12-znakowy identyfikator SIMC+ULIC. Import automatycznie
rozszerza kolumnę istniejącego schematu wersji 1 do wersji 2 bez usuwania
rekordów. Walidacja i import sprawdzają te same limity; błąd podaje nazwę
pola, jego wartość, długość i dopuszczalny limit.

- TERC: streaming XML z ZIP, zachowane zera w kodach i rzeczywisty `STAN_NA`.
  Dostarczony ZIP ma stan danych `2026-01-01`, mimo daty w nazwie pliku.
  Oryginalne wiersze pozostają w `dbo.Terc`. Przy wzbogacaniu PRG kod gminy
  miejsko-wiejskiej `3` może korzystać z pary `4`/`5`, wyłącznie gdy obie
  części mają tę samą nazwę. Nadrzędne kody gmin miast podzielonych na
  dzielnice/delegatury korzystają z nazwy miasta na prawach powiatu.
  Nie dopisuje się fikcyjnych wierszy do źródłowego słownika TERC.
- PRG: dwa strumieniowe przebiegi każdego GML; w pamięci znajdują się
  słowniki miejscowości, ulic i odwołań, a nie cały dokument lub zbiór punktów.
  Obsługiwane są `AD_Miejscowosc`, `AD_UlicaPlac`, `AD_PunktAdresowy`,
  odwołania `xlink:href`, w tym powiązania ulic przez `adres2`.
- Oryginalne X/Y i SRID pozostają w bazie. ProjNet przelicza polskie układy
  EPSG:2180 i EPSG:2176–2179; obsługiwane są także EPSG:4326 i CRS84.
  Kolejność osi zależy od SRS. Nieznany SRS lub współrzędne poza Polską
  przerywają import zamiast tworzyć niewiarygodne dane. Przeliczenie ETRF
  do WGS84 nie uwzględnia epoki ani dokładnej transformacji dynamicznej.
- Partie zapisuje `SqlBulkCopy`, domyślnie po 10 000 punktów. Istniejące
  punkty aktualizowane są po stabilnym identyfikatorze IIP, nie wersjonowanym
  `gml:id`. Identyfikator dłuższy niż 50 znaków zastępuje deterministyczny hash.
- Klucz SHA-256 normalizowanej miejscowości, ulicy i numeru jest indeksowany.
  Po wyszukaniu klucza sprawdzane są także pełne teksty, województwo i powiat.
  Gmina CEIDG nie jest kryterium dopasowania. Ulica bez nazwy pozostaje pusta.
  Wpisy `Brak województwa`, `Brak powiatu` i `-` w odpowiednich słownikach
  oznaczają brak informacji, nie rzeczywiste nazwy jednostek. Nie blokują
  dopasowania. Podane rzeczywiste nazwy nadal muszą się zgadzać; wymagane
  jest dokładnie jedno dopasowanie. Log podaje konkretny brakujący element
  adresu i osobno podsumowuje niekompletne adresy oraz brak punktu PRG.
- Import jest powtarzalny. Po przerwaniu uruchom go ponownie: zapisane partie
  zostaną zaktualizowane, nie zdublowane. Nie ma checkpointu wewnątrz GML;
  ponowne uruchomienie czyta archiwum od początku.
  Jeśli identyczny zbiór jest już kompletny, importer pomija ponowny odczyt GML
  i zapis (po sprawdzeniu SHA-256 plików).
- SHA-256 obu ZIP-ów identyfikuje bieżący zbiór. Niepełny import blokuje
  dopasowanie; punkty z wcześniejszego zbioru nie uczestniczą w dopasowaniu.
  Stare punkty pozostają w tabeli, nie są automatycznie usuwane.
- Istniejące `dbo.Terc` / `dbo.PrgAdresy` bez metadanych tego importera
  powodują błąd, nie usunięcie lub przebudowę cudzych tabel.
- Dopasowywane są tylko firmy z `AddressIsMissing=1`, `BlockedByRODO=0`
  i bez istniejącej relacji `CompanyAddress`. NULL flagi nie jest traktowane
  jako zgoda na aktualizację. Niejednoznaczne dopasowania są pomijane.
- `--apply` uzupełnia słowniki nazw, tworzy `Adresy` ze współrzędnymi i
  `geography::Point`, dodaje `CompanyAddress`, a potem ustawia flagę na 0.
  Numer budynku trafia do `Adresy.NrUlicy`; numer mieszkania nie jest
  kryterium punktu PRG. Operacja jest transakcyjna dla każdej partii.
- Ponowna kontrola adresu i flag firmy pod blokadą chroni przed zapisem po
  równoczesnej edycji. Istniejące relacje nie są nadpisywane. Blokada aplikacyjna
  zapobiega jednoczesnemu uruchomieniu dwóch instancji importera w tej bazie.
- Jeżeli istniejący adres z tym samym identyfikatorem PRG ma inne dane lub
  współrzędne niż nowy zbiór, importer nie podłącza do niego kolejnej firmy
  ani nie zmienia adresów wcześniej podłączonych firm. Zapis jest pomijany.
- Nie wykonuje się globalnego resetowania `AddressIsMissing`. Adresy ze zbyt
  długą ulicą dla tabeli docelowej (>200 znaków) nie są zapisywane ani obcinane.
- Wszystkie operacje i błędy trafiają do konsoli oraz pliku UTF-8 w `logs`
  obok aplikacji. Logi mogą zawierać dane adresowe; zabezpiecz ich dostęp
  i ustal okres przechowywania. Ctrl+C bezpiecznie przerywa kolejne partie.

`self-test` nie korzysta z SQL. `integration-test` tworzy osobną bazę
`PrgImporterTest_<GUID>` na skonfigurowanym serwerze, sprawdza zapis, limity,
niejednoznaczność, RODO i powtarzalność, a następnie usuwa tylko tę bazę.
Wymaga uprawnień tworzenia i usuwania bazy. Nie używa produkcyjnych tabel.

Przed pełnym importem wykonaj kopię bazy, zapewnij miejsce na tabelę PRG,
indeksy i dziennik transakcyjny. Odczyt całej Polski i pierwszy zapis mogą
trwać długo; `validate` również czyta słowniki całego pierwszego województwa.
Plik `Sql/OptionalCompanyIndexes.sql` zawiera opcjonalne indeksy przyspieszające
odczyt brakujących adresów i sprawdzanie relacji. Nie jest wykonywany przez
importer; przed ręcznym użyciem sprawdź, czy równoważne indeksy już istnieją.

## Źródła

Sprawdzenie rzeczywistych archiwów: 8 625 366 punktów PRG, w tym 5 546 370
powiązanych z ulicami, oraz 3 631 jednostek źródłowego TERC. Pełny parser
rozpoznaje wszystkie kody TERC tych punktów. Testy zapisu wykonywano w
izolowanych bazach `PrgImporterTest_*`, nie w `Firmy_temp`.

- [Geoportal: struktura publikowanych danych adresowych](https://www.geoportal.gov.pl/aktualnosci/zmiany-struktury-publikowanych-danych-adresowych-2/)
- [GUS: struktury plików TERYT](https://eteryt.stat.gov.pl/eTeryt/rejestr_teryt/udostepnianie_danych/baza_teryt/uzytkownicy_indywidualni/pobieranie/pliki_pelne_struktury.aspx)
- [ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI)
