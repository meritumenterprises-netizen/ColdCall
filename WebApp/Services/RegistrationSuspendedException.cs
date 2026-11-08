namespace ColdCall.Services;

public sealed class RegistrationSuspendedException : InvalidOperationException
{
    public RegistrationSuspendedException() : base("Rejestracja nowych uzytkownikow jest wstrzymana. Sprobuj ponownie pozniej.") { }
}
