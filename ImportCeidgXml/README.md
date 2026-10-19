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
Polaczenie: zmienna CEIDG_XML_CONNECTION_STRING; domyslnie localhost, Firmy_temp,
Windows Authentication, TrustServerCertificate=True. Nie przechowuj hasel w repozytorium.

## Zasady

- Czyta strumieniowo kazdy Detail z plikow *.xml w podanym folderze, bez podfolderow.
- StatusDzialalnosci dopasowuje do CompanyStatus.Nazwa bez wzgledu na wielkosc liter.
  Brakujacy status dodaje do CompanyStatus w transakcji zapisu firmy i przypisuje
  jego StatusId do Companies.StatusDzialalnosciId. BusinessType pozostaje bez zmian.
  Pusty status lub nazwa dluzsza niz 100 znakow zatrzymuje import.
  Dry-run waliduje nazwe bez dodawania statusu.
- Importuje rowniez firmy bez NIP, zapisujac Nip=NULL wraz z ich przypisaniami PKD.
  Sprawdza istnienie Nip IS NULL + FirstName + LastName (bez wzgledu na wielkosc liter),
  ale znalezienie takiego rekordu tylko loguje: firma nadal jest dodawana.
  Pusty NIP nie jest kluczem dopasowania: takie firmy sa dodawane jako nowe przy kazdym uruchomieniu.
  Duplikowane niepuste NIP-y rowniez sa dozwolone: InsertIfMissingAsync zawsze
  dodaje nowa firme i jej PKD, bez wyszukiwania/aktualizowania istniejacej firmy po NIP.
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
- CreatedOn wedlug domyslnej wartosci SQL; ImportedOn=GETDATE(), BatchImported=1.
  AddressIsMissing=1 (nie tworzy Adresy/CompanyAddress). DataModifiedOn pozostaje NULL.
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
  Przypisania PKD sa unikalne w ramach nowo dodanej firmy; powtorzony import
  tworzy kolejne firmy (rowniez z tym samym NIP) z ich wlasnymi przypisaniami PKD.
  Firma i jej PKD zapisywane sa w tej samej transakcji partii.
- Partie transakcyjne co 500 odczytanych rekordow. Blad lub Ctrl+C wycofuje
  niezapisana partie; zatwierdzone partie pozostaja. Ponowny import moze je zduplikowac.
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

