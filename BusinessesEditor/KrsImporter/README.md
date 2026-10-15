# KrsImporter — poprawiona wersja

.NET 10, SQL Server `Firmy`, domyślnie Windows Authentication.

## Najpierw sprawdź połączenie z KRS

Uruchom `Test-API.cmd` lub z katalogu projektu:

```powershell
dotnet run -- --probe 2026-01-02
```

Test pobiera biuletyn i aktualny odpis wybranego podmiotu, wyświetla nazwę, datę rejestracji i PKD. Nie łączy się z SQL Serverem. `TEST OK` oznacza udane pobieranie i parsowanie API. W razie błędu zobaczysz URL, kod HTTP lub wyjątek sieciowy.

## Import

1. Rozpakuj całą paczkę do nowego folderu.
2. W `appsettings.json` ustaw swój serwer SQL w `ConnectionString`. Baza `Firmy` musi istnieć. Dla nazwanej instancji zapisz np. `Server=localhost\\SQLEXPRESS;Database=Firmy;Integrated Security=True;TrustServerCertificate=True;` (dwa ukośniki w pliku JSON).
3. Uruchom `Start-Import.cmd` lub `dotnet run`. Możesz też otworzyć `KrsImporter.csproj` w Visual Studio 2026.

Wymagane: SDK .NET 10, dostęp do internetu i uprawnienia do tworzenia tabel oraz zapisu w bazie. Połączenie używa konta Windows uruchamiającego program. Certyfikat SQL jest zaufany w konfiguracji lokalnej.

`StartDate` i `EndDate` wyznaczają zakres biuletynów. Puste `EndDate` (`null`) oznacza wczoraj. Import zaczyna od najnowszego dnia i najwyższych numerów KRS, aby najpierw przetwarzać nowsze podmioty. Numer KRS nie zastępuje filtra daty: data rejestracji jest zawsze odczytywana z odpisu.

`RegisteredFrom` jest osobnym filtrem daty rejestracji. Domyślnie `2026-01-01`. Aby zapisywać również starsze podmioty pojawiające się w biuletynach od stycznia 2026, ustaw `RegisteredFrom` na `null`. Nie jest to pełna baza wszystkich starszych spółek.

## Co poprawiono

- Logowanie każdego żądania, kodu HTTP, zapisu, pominięcia i błędu, również do pliku.
- Pobieranie od najnowszego biuletynu i najwyższych numerów KRS.
- Ustawienia są kopiowane do katalogu wynikowego i odczytywane względem aplikacji; start w Visual Studio nie zależy od katalogu roboczego.
- Test API bez SQL, obsługa błędów z kodem wyjścia oraz skrypty zachowujące okno konsoli.
- Kolejkowanie całego biuletynu jedną operacją SQL (OPENJSON, wymaga poziomu zgodności bazy >=130).
- Opóźnienie dotyczy również odpisów odrzucanych przez filtr.
- Nieudane rekordy są zapisywane jako oczekujące ponowienia przy następnym uruchomieniu; dni z nieprawidłowym biuletynem nie są oznaczane jako ukończone.

## Dane i postęp

Tworzone tabele: `dbo.Spolki`, `dbo.SpolkiPKD`, `dbo.KrsImportDays`, `dbo.KrsImportQueue`, `dbo.KrsImportSettings`. Istniejące tabele Spolki/SpolkiPKD muszą mieć schemat z tego projektu; aplikacja nie zmienia istniejących kolumn. Nie usuwaj dotychczasowych danych ani tabel postępu.

Status kolejki: 0 oczekuje, 1 zapisano, 2 pominięto (powód w `Error`), 3 błąd do ponowienia. Ponowne uruchomienie podejmuje kolejkę; zmiana filtra rejestracji ponownie sprawdza rekordy wcześniej pominięte ze względu na datę. Program jest importerem wstępnym; pomija już zapisane KRS i nie jest ciągłą synchronizacją wszystkich późniejszych zmian. Uruchamiaj tylko jedną instancję.

Biuletyn zawiera nowe wpisy i zmiany istniejących podmiotów. Duża część pobranych odpisów może zostać odrzucona przez filtr 2026. Przetworzenie całego zakresu może potrwać wiele godzin lub dni. Rejestr przedsiębiorców obejmuje także inne formy podmiotów; importer nie ogranicza ich po formie prawnej.

Zapis obejmuje aktualny odpis, PKD i opisy KRS oraz surowy JSON; nie rekonstruuje stanu historycznego. Nie łączy kodów automatycznie z dbo.PKD, ponieważ wersje klasyfikacji mogą się różnić. Brak odpisu P (404) jest logowany i pomijany, więc zbiór może nie obejmować podmiotów wykreślonych.

Logi znajdują się w katalogu wynikowym, np. `bin/Debug/net10.0/logs`. Gdy pojawi się problem, przekaż ostatnie linie logu. Nie wysyłaj connection stringa z hasłem.

Oficjalna dokumentacja: https://prs.ms.gov.pl/krs/openApi
