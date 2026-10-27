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
INSERT do Adresy/CompanyAddress i UPDATE Companies.AddressIsMissing.
Uprawnienia DDL nie sa potrzebne do importu.

## Kwalifikacja i dopasowanie

- AddressIsMissing = 1, WojewodztwoId > 1, MiejscowoscId IS NOT NULL.
- PowiatId i GminaId musza byc dodatnimi identyfikatorami w Companies.
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

Wszystkie cztery identyfikatory adresu (WojewodztwoId, PowiatId, GminaId,
MiejscowoscId) sa kopiowane bezposrednio z Companies. UUG dostarcza GPS;
nazwy powiatu/gminy z odpowiedzi nie zmieniaja identyfikatorow firmy.
Importer nie tworzy ani nie modyfikuje slownikow administracyjnych.
Firmy bez wymaganych identyfikatorow nie kwalifikuja sie do zapisu.

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

Nowe PrngId ma postac UUG:CO: + 43 znaki SHA-256 klucza punktu UUG
i czterech identyfikatorow z Companies. Rozne zestawy identyfikatorow nie sa scalane.
Nie jest oryginalnym identyfikatorem PRG: identyfikator id
w odpowiedzi UUG zmienia sie miedzy requestami i nie nadaje sie na klucz.
Istniejacy punkt z tym samym kluczem jest uzywany ponownie, o ile dane sa identyczne.
Punkt wczesniej zaimportowany innym importerem, z innym PrngId, nie jest automatycznie scalany.

Transakcja dla jednej firmy:
1. Ponownie sprawdza kryteria, blokade RODO, brak powiazania i niezmieniony adres.
2. Tworzy lub wykorzystuje rekord Adresy z identyfikatorami firmy i GPS z UUG.
3. Dodaje CompanyAddress; ustawia AddressIsMissing=0 dla budynku, 1 dla miejscowosci.

Konflikt lub blad SQL powoduje rollback i zatrzymanie procesu.
Po pierwszej odpowiedzi niebedacej poprawnym JSON importer odklada firme
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

Parallel.ForEachAsync wykonuje maksymalnie 128 wyszukiwan firm jednoczesnie.
Domyslnie brak sztucznego odstepu pomiedzy startami HTTP (DelayMilliseconds=0).
--delay-ms pozwala ustawic odstep; wartosc 0 wylacza limiter czasu.
Oczekiwanie na odpowiedz nie blokuje pozostalych wyszukiwan. HTTP429 i bledy
tymczasowe nadal powoduja ponowienia z opoznieniem, z uwzglednieniem Retry-After.
Wyszukiwania dzialaja ciagle, bez bariery paczek: wolny request nie blokuje zapisu
pozostalych wynikow ani startu kolejnego wyszukiwania w wolnym slocie.
Gotowe wyniki trafiaja do ograniczonego kanalu i jednego konsumenta zapisu SQL.
W trybie --apply konsument zbiera do 100 wynikow, czekajac maksymalnie sekunde.
Adresy sa wysylane jako parametryzowany JSON do SQL (OPENJSON), bez instalowania
typu tabelarycznego ani procedury w bazie. Jedna partia to jedno wywolanie SQL
i jedna transakcja; zachowane sa kontrole aktualnosci firmy, RODO i konfliktow adresu.
Blad SQL wycofuje cala partie; nastepnie dziala dotychczasowy zapis pojedynczy.
Nieudana firma nadal zatrzymuje import bez przeskakiwania jej checkpointem.
Checkpoint jest utrwalany co 100 przesuniec lub po sekundzie oraz na zakonczenie.
Po twardym przerwaniu niewielki fragment moze zostac ponowiony; zapisane adresy
nie sa dublowane. Tryb raportu nadal przetwarza gotowe wyniki pojedynczo.
Logi SAVE/SKIP/DEFER moga miec ID w innej kolejnosci niz rosnaca.
Checkpoint przesuwa sie tylko za ciag zapisanych lub obsluzonych rekordow,
bez przeskakiwania wolniejszej firmy. PROGRESS podaje bezpieczny checkpoint Id.
Kanal wynikow ma pojemnosc 256, a okno niezcheckpointowanych firm 512.
Wyczerpanie okna lub wypelnienie kanalu wstrzymuje pobieranie kolejnych firm,
aby opoznione HTTP lub wolny SQL nie powodowaly nieograniczonego zuzycia pamieci.
Blad lub przerwanie anuluje i oczekuje na workerow przed zamknieciem HTTP/logu.
Retry-errors i company-id nadal nie zmieniaja glownego checkpointu.
Cache, limiter HTTP, liczniki i logi sa bezpieczne dla rownoleglych wywolan.
To ostrozna konfiguracja aplikacji, nie deklarowany oficjalny limit GUGiK.
HTTP429 i 5xx sa ponawiane do czterech prob z respektowaniem Retry-After.
Odpowiedz niebedaca poprawnym JSON (np. tekst Blad zapytania. przy HTTP200)
nie jest ponawiana. Surowa odpowiedz jest zachowana diagnostycznie, a firma trafia
do trwalej listy do recznego ponowienia przez --retry-errors. Import przechodzi dalej;
dotychczasowy bezpiecznik pieciu kolejnych bledow nadal zatrzymuje przebieg.
Niepoprawny JSON nie jest zapisywany w cache jako pusty wynik.
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
