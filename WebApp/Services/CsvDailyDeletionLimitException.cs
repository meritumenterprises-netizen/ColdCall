namespace ColdCall.Services;

public class CsvDailyDeletionLimitException(int remaining) : Exception(
    "Plan PX0 pozwala usun\u0105\u0107 maksymalnie 3 pliki CSV dziennie. Pozosta\u0142o dzisiaj: " + remaining + ".")
{
    public int Remaining { get; } = remaining;
}
