namespace ImportCEIDGByDate.Geoportal;

internal sealed class GeoportalQueryException(string message, Exception inner) : Exception(message, inner);
