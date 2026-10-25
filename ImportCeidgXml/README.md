# ImportCeidgXml

Import XML CEIDG do Firmy_temp.ColdCall.Companies i ColdCall.CompanyPkds (.NET 10).
Program.cs zawiera tylko uruchomienie. Osobne klasy: opcje, czytnik XML,
model firmy, repozytorium SQL, serwis importu, logowanie.

## Uruchomienie

```powershell
dotnet run --project C:/projects/ColdCall/ImportCeidgXml -- "C:/folder/CEIDG_Modified"
dotnet run --project C:/projects/ColdCall/ImportCeidgXml -- "C:/folder/CEIDG_Modified" --apply
```

Bez --apply: walidacja XML i odczyt z SQL, bez zapisu firm.
--limit N ogranicza liczbe odczytanych rekordow (przydatne do testow).
W --apply importer automatycznie wznawia od ostatniej zatwierdzonej pozycji.
--restart ignoruje zapisany postep i przetwarza wszystkie XML-e od poczatku (nie usuwa danych z bazy).
Polaczenie: zmienna CEIDG_XML_CONNECTION_STRING; domyslnie localhost, Firmy_temp,
Windows Authentication, TrustServerCertificate=True. Nie przechowuj hasel w repozytorium.

## Zasady

- Czyta strumieniowo kazdy Detail z plikow *.xml w podanym folderze, bez podfolderow.
- StatusDzialalnosci dopasowuje do CompanyStatus.Nazwa bez wzgledu na wielkosc liter.
  Brakujacy status dodaje do CompanyStatus w transakcji zapisu firmy i przypisuje
  jego StatusId do Companies.StatusDzialalnosciId. BusinessType pozostaje bez zmian.
  Pusty status lub nazwa dluzsza niz 100 znakow zatrzymuje import.
  Dry-run waliduje nazwe bez dodawania statusu.
- Pomija firmy bez elementu Nip/NIP, z pustym NIP, samymi spacjami lub wartoscia "-".
  Nie zapisuje dla nich firmy, przypisan PKD ani CompanyXml; nie waliduje ich PKD w dry-run.
  Podsumowanie "bez NIP" zawiera liczbe pominietych rekordow.
  Niepusty NIP jest kluczem: brak firmy powoduje INSERT, istniejaca firma jest aktualizowana
  z zachowaniem Id, CreatedOn, BusinessType, BlockedByRODO i SkipAddressVerification.
  Dopasowanie i zapis sa chronione blokadami UPDLOCK/HOLDLOCK w transakcji partii.
- Name: NazwaPodmiotu; przy pustej nazwie lub "-" uzywa Imie + Nazwisko.
- Dla kazdego pliku rozpoznaje wojewodztwo z nazwy i w trybie --apply wyszukuje
  istniejace Id w ColdCall.Wojewodztwa. Wynik przekazuje do modelu firmy
  i zapisuje w Companies.WojewodztwoId przy dodawaniu firmy.
  "brak województwa" dopasowuje do "Brak województwa"; ignoruje wielkosc liter
  i roznice myslnik/spacja. Dopasowanie dziala rowniez w dry-run.
- Wojewodztwo z nazwy pliku ma pierwszenstwo przed polem w XML.
  Pozostale puste pola i "-" daja NULL.
- Slowniki adresowe dopasowuje po Name; brakujace nazwy dodaje w tej samej transakcji.
  Nie zmienia nazw gmin ani nie laczy ich wedlug rodzaju.
- Mapuje dane osobowe, NIP, REGON, adres, Telefon, Email, GoogleEmail, AdresWWW,
  DataRozpoczeciaDzialalnosci -> StartedAt. Typ JDG i status pobiera ze slownikow.
  DataWznowieniaDzialalnosci (takze wariant DataWznowieniaDzialanosci) -> ResumedOn.
  Tylko dla Zawieszony: DataZawieszeniaDzialalnosci -> SuspendedOn; dla Aktywny NULL.
  Dla istniejacej firmy brak daty rozpoczecia/wznowienia nie kasuje daty zapisanej w SQL.
- Pole Telefon:
  Pole Telefon dluzsze niz 32 znaki oczyszcza do pierwszego rozpoznanego polskiego numeru
  (9 cyfr lub prefiks +48/0048/48). Nie zmienia wartosci mieszczacych sie w limicie.
  Pelna wartosc pozostaje w CompanyXml i jest uwzgledniana w MD5; oczyszczenie loguje.
  Jesli z za dlugiej wartosci nie mozna rozpoznac numeru, loguje ostrzezenie i kontynuuje import:
  nowa firma dostaje Phone=NULL, a istniejaca zachowuje dotychczasowy telefon.
- CreatedOn nowej firmy wedlug domyslnej wartosci SQL; ImportedOn=GETDATE(), BatchImported=1.
  Aktualizacja istniejacej firmy ustawia DataModifiedOn=GETDATE().
  Nowa firma lub zmieniony adres ustawia AddressIsMissing=1 (nie tworzy Adresy/CompanyAddress).
- W --apply zapisuje XML Detail bez Lp. do ColdCall.CompanyXml wraz z MD5 bajtow UTF-8.
  Snapshot jest dodawany/aktualizowany po NIP w tej samej transakcji co firma i PKD.
- Dla kazdego niepustego NIP odczytuje existingCompany z Companies oraz CompanyPkds
  z kodami/opisami z PkdCodes i flaga Prevailing. Brak firmy daje NULL.
  PKDs zawiera liste kodow, PrevailingPKD pierwszy kod z Prevailing=true
  (deterministycznie wedlug kodu i Id); pelne flagi sa zachowane w CompanyPkds.
  Wiele firm z tym samym NIP zatrzymuje import zamiast wybierac przypadkowa firme.
- GlownyKodPkd zapisuje w CompanyPkds z Prevailing=1, PozostaleKodyPkd z Prevailing=0.
  Dopasowuje do PkdCodes.CodeWithoutDots, ignorujac kropki i wielkosc liter.
  Kod glowny obecny tez wsrod pozostalych tworzy jedno przypisanie z Prevailing=1.
  Puste kody oraz N/A sa pomijane. Brakujacy kod dodaje do PkdCodes
  z Name=N'' i NeedsClarification=1; CodeWithoutDots wylicza SQL.
  Standardowe piecioznakowe kody zapisuje z kropkami, np. 0111Z -> 01.11.Z.
  Niejednoznaczny kod zatrzymuje import. Dry-run nie dodaje brakujacych kodow.
  Przypisania PKD sa unikalne w ramach firmy; powtorzony import aktualizuje flagi Prevailing,
  dodaje nowe kody i usuwa przypisania nieobecne w aktualnym XML.
  Firma i jej PKD zapisywane sa w tej samej transakcji partii.
- Partie transakcyjne co 500 odczytanych rekordow. Blad lub Ctrl+C wycofuje
  niezapisana partie; zatwierdzone partie pozostaja. Ponowny import dopasowuje firmy po NIP.
- Checkpoint .ceidg-full-import-<klucz-bazy>.checkpoint.json zapisuje w folderze XML
  sciezke pliku, liczbe zatwierdzonych elementow Detail i stan zakonczenia importu.
  Zapisuje go atomowo po zatwierdzeniu kazdej partii i na koncu pliku.
  Po restarcie pomija zakonczone pliki i zatwierdzone rekordy; dry-run nie korzysta z checkpointu.
  Biezacy XML jest skanowany od poczatku do zapisanej pozycji bez ponownego mapowania/zapisu tych rekordow.
  Zmiana listy plikow, rozmiaru lub czasu modyfikacji wymaga --restart.
  Gdy caly zestaw zakonczono, kolejne uruchomienie go pomija; --restart wymusza ponowny import.
  Awaria pomiedzy COMMIT SQL a zapisem checkpointu moze powtorzyc ostatnia partie,
  ale nie pominie danych; zapis po NIP nie tworzy duplikatow.
- Pliki z blednym XML/data lub za dlugimi wartosciami zatrzymuja import bez obcinania danych.
- Jedna instancja importera na baze (SQL application lock).
- Log i podsumowanie sa wyswietlane w konsoli i zapisywane w folderze logs przy aplikacji.

## Nota

Żeby zrebuildować indexy na ColdCall.Companies, uruchom ten skrypt

```sql
ALTER INDEX ALL ON ColdCall.Companies
REBUILD;
GO
```
