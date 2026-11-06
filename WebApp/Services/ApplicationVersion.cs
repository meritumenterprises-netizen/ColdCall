using System.Reflection;

namespace ColdCall.Services;

public static class ApplicationVersion
{
    public static string Current { get; } =
        typeof(ApplicationVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? typeof(ApplicationVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
