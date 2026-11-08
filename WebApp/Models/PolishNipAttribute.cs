using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class PolishNipAttribute : ValidationAttribute
{
    public PolishNipAttribute() : base("NIP musi zawierac 10 cyfr i miec poprawna cyfre kontrolna albo pozostac pusty.") { }

    public override bool IsValid(object? value)
    {
        if (value == null || value is string { Length: 0 }) return true;
        if (value is not string nip || nip.Length != 10 || nip.Any(c => c < '0' || c > '9')) return false;
        if (nip.All(c => c == nip[0])) return false;
        ReadOnlySpan<int> weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var sum = 0;
        for (var index = 0; index < weights.Length; index++) sum += (nip[index] - '0') * weights[index];
        return sum % 11 == nip[9] - '0';
    }
}
