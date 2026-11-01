using System.Text.RegularExpressions;
using ColdCall.Models;

namespace ColdCall.Services;

public static class UserProfileCompletion
{
    public static bool IsRequired(AppUser user) => !user.IsAdmin && user.UsesGmail
        && (new[] { user.FirstName, user.LastName, user.PostalCode, user.City, user.Street, user.StreetNumber }
                .Any(string.IsNullOrWhiteSpace)
            || !Regex.IsMatch(user.PostalCode ?? "", @"\A[0-9]{2}-[0-9]{3}\z"));
}
