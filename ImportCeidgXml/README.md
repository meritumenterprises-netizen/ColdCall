# ImportCeidgXml

Import XML CEIDG do Firmy_temp.ColdCall.Companies (.NET 10).
Program.cs zawiera tylko uruchomienie. Osobne klasy: opcje, czytnik XML,
model firmy, repozytorium SQL, serwis importu, logowanie.

## Uruchomienie

```powershell
dotnet run --project C:/projects/ColdCall/ImportCeidgXml -- "C:/folder/CEIDG_Modified"
dotnet run --project C:/projects/ColdCall/ImportCeidgXml -- "C:/folder/CEIDG_Modified" --apply
```

Bez --apply: tylko walidacja XML, bez polaczenia SQL i bez zapisu.
--limit N ogranicza liczbe odczytanych rekordow (przydatne do testow).
Polaczenie: zmienna CEIDG_XML_CONNECTION_STRING; domyslnie localhost, Firmy_temp,
Windows Authentication, TrustServerCertificate=True. Nie przechowuj hasel w repozytorium.

## Zasady

- Czyta strumieniowo kazdy Detail z plikow *.xml w podanym folderze, bez podfolderow.
- Importuje tylko StatusDzialalnosci=Aktywny.
- Pomija brak NIP oraz firmy z NIP juz istniejacym w Companies.
  Nie nadpisuje istniejacych danych, w szczegolnosci BlockedByRODO i adresow.
- Name: NazwaPodmiotu; przy pustej nazwie lub "-" uzywa Imie + Nazwisko.
- Wojewodztwo z XML lub z koncowki nazwy pliku "województwo ...".
  Brak wojewodztwa daje NULL. Puste pola i "-" daja NULL.
- Slowniki adresowe dopasowuje po Name; brakujace nazwy dodaje w tej samej transakcji.
  Nie zmienia nazw gmin ani nie laczy ich wedlug rodzaju.
- Mapuje dane osobowe, NIP, REGON, adres, Telefon, Email, GoogleEmail, AdresWWW,
  DataRozpoczeciaDzialalnosci. Typ JDG i aktywny status pobiera ze slownikow.
- CreatedOn wedlug domyslnej wartosci SQL; ImportedOn=GETDATE(), BatchImported=1.
  AddressIsMissing=1 (nie tworzy Adresy/CompanyAddress). DataModifiedOn pozostaje NULL.
- Ten importer zapisuje Companies i slowniki adresowe, nie przypisania CompanyPkds.
- Partie transakcyjne co 500 odczytanych rekordow. Blad lub Ctrl+C wycofuje
  niezapisana partie; zatwierdzone partie pozostaja i kolejne uruchomienie pomija ich NIP-y.
- Pliki z blednym XML/data lub za dlugimi wartosciami zatrzymuja import bez obcinania danych.
- Jedna instancja importera na baze (SQL application lock).
- Log i podsumowanie sa wyswietlane w konsoli i zapisywane w folderze logs przy aplikacji.
