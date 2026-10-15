# FirmyGeoNormalizer v4 — trzy niezależne słowniki nazw

C# / .NET 10. SQL Server, baza Firmy, domyślnie Windows Authentication.

## Docelowy schemat

- dbo.Powiat: Id, Nazwa (unikalna).
- dbo.Gmina: Id, Nazwa (unikalna).
- dbo.Miejscowosc: Id, Nazwa (unikalna).
- dbo.Firmy: nowe PowiatId, GminaId, MiejscowoscId, każdy wskazuje właściwy słownik.

Nie ma żadnych relacji między słownikami ani z nich do województw. Istniejące Firmy.WojewodztwoId i wszystkie oryginalne kolumny tekstowe pozostają bez zmian. WojewodztwoId nie jest wymagane ani wykorzystywane przez tę wersję.

## Uruchomienie

1. Rozpakuj całą paczkę do nowego katalogu. Ustaw ConnectionString w appsettings.json lub skopiuj własny plik z poprzedniej wersji.
2. Analiza.cmd: raport liczby firm i unikalnych nazw bez zmian w bazie.
3. Zatrzymaj inne importery i wykonaj backup bazy. Migracja.cmd: utworzenie/naprawienie schematu i wypełnienie referencji.

W terminalu:

```powershell
dotnet run -- --analyze
dotnet run -- --apply
```

Można otworzyć FirmyGeoNormalizer.csproj w Visual Studio 2026. Bez argumentów program wykonuje tylko analizę. Pliki Sql są częścią aplikacji i automatycznie kopiują się do katalogu wynikowego. Logi zapisują się w podfolderze logs obok plików aplikacji.

## Naprawa poprzedniej migracji

Jeśli poprzednia wersja zakończyła migrację i istnieje jej tabela FirmyGeoNormalizerState, program rozpoznaje schemat. W tej samej transakcji:

1. Usuwa relacje Miejscowosc → Gmina oraz Gmina → Powiat.
2. Usuwa ze słowników kolumny Miejscowosc.GminaId, Gmina.PowiatId i Powiat.WojewodztwoId (lub Powiat.Wojewodztwo ze starszej wersji).
3. Łączy powtarzające się nazwy w słownikach, zachowując najmniejsze istniejące Id. Przed usunięciem duplikatów przepina referencje w Firmy. Id powtarzających się pozycji mogą się zmienić.
4. Uzupełnia słowniki i referencje na podstawie tekstów z Firmy, niezależnie dla każdego z trzech pól.
5. Tworzy/odświeża widok dbo.vFirmyGeografia.

Nie usuwa żadnej kolumny z Firmy i nie zmienia wartości oryginalnych tekstów. Nie usuwa tabeli województw ani istniejących relacji Firmy do województw. Pozostają FK Firmy → trzy słowniki.

Jeżeli słowniki mają dodatkowe zależności z innych aplikacji lub były modyfikowane ręcznie, operacja może się zatrzymać i wycofać zamiast usuwać te zależności. Nie usuwaj tabel ręcznie. Gdy poprzedni rollback nadal trwa, poczekaj; pomocnicze zapytania znajdują się w Sql/Check-Rollback.txt.

## Reguły danych

- Wymagane: dbo.Firmy.Id (unikalne, nie-NULL), Detail_Powiat, Detail_Gmina, Detail_Miejscowosc.
- Słowniki grupują wyłącznie po nazwie. Identyczna nazwa otrzymuje jeden Id, niezależnie od województwa/powiatu/gminy. To słowniki tekstów, nie rejestr jednostek TERYT.
- Usuwane są spacje na początku i końcu wartości użytych do dopasowania; tekst źródłowy pozostaje nienaruszony.
- Puste wartości i NULL dają NULL w odpowiedniej referencji; nie blokują przypisania pozostałych pól.
- Porównywanie nazw podlega kolacji bazy. Przy kolacji niewrażliwej na wielkość liter warianty pisowni otrzymują jeden wpis. Nazwy mogą mieć max. 200 jednostek UTF-16; dłuższe zatrzymują migrację bez obcinania.
- Ponowne uruchomienie dopisuje nowe nazwy i aktualizuje referencje. Nie ma automatycznego triggera — po kolejnych importach tekstowych uruchom program ponownie.
- Nie przejmuje istniejących tabel/kolumn, jeśli nie zostały utworzone przez tę aplikację.

## Transakcja

Cała migracja jest jedną transakcją. Blokuje Firmy na czas pracy i wymaga miejsca w tempdb oraz dzienniku transakcji. Błąd uruchamia rollback z limitem CommandTimeoutSeconds (domyślnie 3600). Konto wymaga odpowiednich praw DDL oraz odczytu/zapisu. Wymagany SDK .NET 10.

## Odczyt

```sql
SELECT TOP (100) * FROM dbo.vFirmyGeografia;
```

Zweryfikowano kompilację i składnię SQL. Nie testowano wykonania migracji na bazie użytkownika.
