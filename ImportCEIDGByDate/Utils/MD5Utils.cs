using System.Security.Cryptography;
using System.Text;

namespace ImportCEIDGByDate;

public static class MD5Utils
{
    public static string CalculateMD5(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static string CalculateFileMD5(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var file = File.OpenRead(filePath);
        return Convert.ToHexString(MD5.HashData(file));
    }

    public static bool FilesHaveIdenticalMD5(string firstFilePath, string secondFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondFilePath);

        using var firstFile = File.OpenRead(firstFilePath);
        using var secondFile = File.OpenRead(secondFilePath);
        var firstHash = MD5.HashData(firstFile);
        var secondHash = MD5.HashData(secondFile);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }
}
