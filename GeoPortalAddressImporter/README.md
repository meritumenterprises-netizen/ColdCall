# GeoPortalAddressImporter

Aplikacja konsolowa .NET 10 uzupelniajaca adresy firm z oficjalnej uslugi UUG
GUGiK: https://services.gugik.gov.pl/uug/opis.html.
Nie potrzebuje tabel PrgAdresy, Terc ani lokalnych plikow PRG.

## Uruchomienie

W PowerShell:

```powershell
cd C:\projects\ColdCall\GeoPortalAddressImporter
dotnet build
dotnet run -- run --limit 10
dotnet run -- run --apply --limit 10
dotnet run -- run --apply --all
```

Pierwsze polecenie uruchomienia to RAPORT, bez zmian w bazie.
Zapis wymaga jawnego --apply. Bez --all importer przetwarza najwyzej 10 firm.
Nie uruchamiaj pelnego importu przed ocena raportu i wykonaniem backupu bazy.

Konfiguracja jest w appsettings.json, kopiowanym do katalogu wykonywalnego.
Mozna nadpisac connection string zmienna GEO_PORTAL_SQL_CONNECTION.
Wymagane: SQL Server, uprawnienia SELECT do Companies/slownikow/CompanyAddress,
INSERT do Adresy/CompanyAddress/Powiaty/Gminy i UPDATE Companies.AddressIsMissing.
Uprawnienia DDL nie sa potrzebne do importu.

## Kwalifikacja i dopasowanie

- AddressIsMissing = 1, WojewodztwoId > 1, MiejscowoscId IS NOT NULL.
- BlockedByRODO = 0 i brak istniejacego CompanyAddress.
- Wymagana miejscowosc. Dla dokladnego budynku wymagany jest tez numer i kod XX-XXX.
- Brak numeru, numer zastepczy (-, --, N/A, B/N, BRAK) lub brak poprawnego kodu
  pocztowego powoduje wyszukanie GPS samej miejscowosci, bez ulicy i numeru.
- Wynik city musi miec dokladna nazwe i wojewodztwo firmy, poprawne SIMC/TERYT,
  geometrie i jeden pasujacy wynik na kompletnej liscie. Niejednoznacznosc jest pomijana.
- Odpowiedzi typu street nadal nie sa zapisywane.
- found objects=null/puste/brak jest akceptowane jako zero tylko przy pustym results
  i jawnym returned objects=0. Niekompletne odpowiedzi i bledy uslugi nie sa brakiem adresu.
- GetAddress z accuracy=1 i exact_number=1 wyszukuje kandydata.
- Gdy odpowiedz GetAddress zawiera wiele punktow, wybierany jest pierwszy
  w kolejnosci odpowiedzi. Nadal musi przejsc wszystkie sprawdzenia adresu i geometrii.
- Dla pelnego adresu puste wyniki oraz odpowiedzi street/city sa pomijane;
  GPS miejscowosci jest pobierany tylko dla niepelnego adresu.
- Miasto, wojewodztwo, ulica, numer i kod pocztowy musza sie zgadzac.
- Drugi request GetAddresses dla SIMC i ULIC sprawdza pelna liste punktow
  ulicy (dla miejscowosci bez ulic: tylko SIMC). Dokladnie jeden punkt musi pasowac.
- Niepelne listy, dwa punkty, rozbieznosci oraz przyblizone centra ulic sa odrzucane.
- Prefiksy ulic sa normalizowane; brak zgadywania podobnych nazw czy numerow.
- Rozpoznawane sa tez prefiksy z dwukropkiem, np. ul:Wielogorska.
- Numer mieszkania nie bierze udzialu w geokodowaniu punktu budynku.

UUG GetAddress udostepnia tylko najlepszy kandydat, a nie wszystkie wyniki w Polsce.
Weryfikacja jednoznacznosci dotyczy zwroconego SIMC/ULIC; zgodnosc miasta,
wojewodztwa i kodu pocztowego ogranicza ryzyko miejscowosci o tych samych nazwach,
ale nie jest formalnym dowodem unikalnosci adresu w calym kraju.
Importer nie wybiera punktow na podstawie samej nazwy firmy.
Lokalizacja miejscowosci jest jawnie oznaczona jako przyblizona, nie jako budynek.

Powiat i gmina pochodza z pola jednostka. Slowniki Powiaty/Gminy
sa wyszukiwane po Name; brakujace nazwy sa dodawane transakcyjnie.
Obecny schemat slownikow jest unikalny globalnie po Name, bez hierarchii TERYT:
importer zachowuje ten model i nie dodaje osobnych wierszy o tej samej nazwie.
Wojewodztwo i miejscowosc uzywaja istniejacych, zweryfikowanych Id firmy.

## Zapis i bezpieczenstwo

Dane API x/y w EPSG:2180 (easting/northing) sa przeliczane przez ProjNet na
latitude/longitude WGS84, walidowane i zaokraglane do 6 miejsc.
Do Location zapisywany jest geography::Point(latitude, longitude, 4326).
Ulica trafia do Ulica, numer budynku do NrUlicy. Rodzaj: punkt adresowy UUG.

Dla niepelnego adresu Rodzaj to `miejscowosc UUG (GPS przyblizony)`.
Ulica i NrUlicy pozostaja NULL; Latitude, Longitude i Location wskazuja miejscowosc.
PrngId ma oddzielny prefiks UUG:C: i hash TERYT/SIMC, wiec nie koliduje z budynkami.
Tworzony jest CompanyAddress, ale AddressIsMissing pozostaje 1: pelny adres nadal
jest nieznany. Istniejace powiazania nie sa nadpisywane ani automatycznie ulepszane.

PrngId ma postac UUG: + 46 znakow SHA-256 identyfikatorow TERYT/SIMC/ULIC
i numeru budynku. Nie jest oryginalnym identyfikatorem PRG: identyfikator id
w odpowiedzi UUG zmienia sie miedzy requestami i nie nadaje sie na klucz.
Istniejacy punkt z tym samym kluczem jest uzywany ponownie, o ile dane sa identyczne.
Punkt wczesniej zaimportowany innym importerem, z innym PrngId, nie jest automatycznie scalany.

Transakcja dla jednej firmy:
1. Ponownie sprawdza kryteria, blokade RODO, brak powiazania i niezmieniony adres.
2. Uzupelnia slowniki i tworzy lub wykorzystuje rekord Adresy.
3. Dodaje CompanyAddress; ustawia AddressIsMissing=0 dla budynku, 1 dla miejscowosci.

Konflikt lub blad SQL powoduje rollback i zatrzymanie procesu.
Po czterech odpowiedziach tekstowych Blad zapytania. importer odklada firme
do trwalej listy do ponowienia i przetwarza nastepna, bez zapisu jej adresu do SQL.
Piec kolejnych takich bledow zatrzymuje import, aby nie kontynuowac podczas awarii uslugi.
Nie nadpisuje istniejacych powiazan ani pol adresowych Companies.
HTTP nigdy nie jest wykonywany wewnatrz transakcji SQL.
Nie tworzy indeksow ani nie zmienia schematu produkcyjnego.

## Wznowienie, cache i logi

Katalog data obok pliku wykonywalnego zawiera:
- logs: log konsolowy i plik na dysku, z CompanyId i przyczyna odrzucenia;
- cache: atomowo zapisywane odpowiedzi, domyslnie wazne 30 dni, puste wyniki 1 dzien;
- cache/invalid-responses: nietypowe surowe odpowiedzi API wraz z zapytaniem;
- progress-...-apply.json / report.json: osobne pozycje wznowienia dla bazy i trybu;
- deferred-...-apply.json / report.json: odlozone firmy, adresy i przyczyny bledow;
- run.lock: blokada rownoleglego uruchomienia z tym samym katalogiem data.

Powtorne wywolanie wznawia od ostatniej zakonczonej firmy, takze w trybie raportu.
Firmy odrzucone zostaja oznaczone jako przetworzone w lokalnym checkpoint,
ale pozostaja AddressIsMissing=1 w SQL. Aby ponowic je lub firmy poprawione w bazie:

```powershell
dotnet run -- run --restart --limit 10
dotnet run -- run --apply --restart --all
dotnet run -- run --company-id 518007
dotnet run -- run --apply --after-id 518000 --limit 100
```

Cache nie jest pomijany przez --restart. Aby ponownie odpytywac API z nowym cache,
uzyj --data-dir wskazujacego nowy katalog (z rozwaga dla obciazenia serwisu).
Po Ctrl+C albo bledzie nieudana firma zostaje do ponowienia.
Po awarii miedzy COMMIT a checkpoint kolejny odczyt wykluczy zapisana firme.

Ponow odlozone firmy (np. po poprawieniu ich adresow w bazie):

```powershell
dotnet run -- run --apply --retry-errors --all
```

--retry-errors czyta aktualne dane firm z SQL i ponownie sprawdza kwalifikacje,
nie zmienia glownego checkpoint, nie laczy sie z --restart, --after-id lub --company-id.
Poprawnie obsluzona albo niekwalifikujaca sie juz firma jest usuwana z listy.
Ponowny blad pozostawia ja na liscie. Tryby RAPORT i ZAPIS maja osobne listy.
Odlozone firmy zachowuja AddressIsMissing=1; nie sa uznawane za brak adresu w Geoportalu.

Nie przechowuj katalogu data w repozytorium. Zawiera publiczne adresy i logi firm;
zabezpiecz jego uprawnienia oraz ustal okres przechowywania i usuwania danych.

## Tempo

Parallel.ForEachAsync wykonuje maksymalnie 8 wyszukiwan firm jednoczesnie.
Domyslnie globalny odstep pomiedzy startami HTTP wynosi 10 ms.
--delay-ms pozwala zwiekszyc odstep, nigdy go zmniejszyc ponizej 10 ms.
Oczekiwanie na odpowiedz nie blokuje pozostalych wyszukiwan. HTTP429 i bledy
tymczasowe nadal powoduja ponowienia z opoznieniem, z uwzglednieniem Retry-After.
Zapis SQL, lista bledow i checkpoint sa wykonywane w kolejnosci firm, po kazdej
paczce do 8 odpowiedzi. Blad lub przerwanie nie przesuwa checkpointu za nieukonczona
firme. Cache, limiter HTTP, liczniki i logi sa bezpieczne dla rownoleglych wywolan.
To ostrozna konfiguracja aplikacji, nie deklarowany oficjalny limit GUGiK.
HTTP429 i 5xx sa ponawiane do czterech prob z respektowaniem Retry-After.
Odpowiedz niebedaca poprawnym JSON (np. tekst Blad zapytania. przy HTTP200)
jest rowniez ponawiana do czterech prob, z odstepami 2/4/8 sekund lub Retry-After.
Nie jest zapisywana jako pusty wynik; po wyczerpaniu prob import zatrzymuje sie
bez przesuniecia checkpoint nieudanej firmy, z wyjatkiem rozpoznanego tekstu
Blad zapytania., ktory trafia do trwalej listy do ponowienia i pozwala kontynuowac.
Trwale bledy sieci lub nieznany format API zatrzymuja import, bez przesuwania checkpoint.

Bez cache dopasowana firma wymaga dwoch requestow. Cache wspoldzieli
listy punktow tej samej ulicy oraz identyczne zapytania adresowe.
Przy 163875 firmach dwa requesty na firme to okolo 9,1 godzin samych odstepow,
plus czas HTTP i SQL; cache moze istotnie ograniczyc liczbe requestow.
Przed masowym wykorzystaniem uzgodnij dopuszczalne obciazenie i warunki z GUGiK;
brak klucza API nie oznacza nielimitowanego korzystania.

## Testy

```powershell
dotnet run -- self-test
dotnet run -- self-test-db
```

self-test nie potrzebuje SQL ani internetu (mock HTTP).
self-test-db tworzy w SQL osobna baze GeoPortalImporterTest_<losowy GUID>,
sprawdza transakcje, rollback, RODO, geography i niedublowanie rekordow,
po czym usuwa TYLKO te testowa baze. Wymaga uprawnien CREATE/DROP DATABASE.
Nigdy nie zapisuje testowych rekordow do Firmy_temp.
