using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Data.SqlClient;

namespace ColdCall.Services;

public sealed class SqlDataProtectionXmlRepository(IConfiguration configuration) : IXmlRepository
{
    private readonly string connectionString = configuration.GetConnectionString("Firmy")
        ?? throw new InvalidOperationException("Missing Firmy connection string for persistent authentication keys.");
    private readonly object schemaLock = new();
    private bool schemaReady;

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            connection.Open();
            lock (schemaLock)
            {
                if (!schemaReady)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = """
                        IF OBJECT_ID(N'ColdCall.DataProtectionKeys', N'U') IS NULL
                        BEGIN TRY
                            CREATE TABLE ColdCall.DataProtectionKeys
                            (
                                Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                FriendlyName nvarchar(450) NULL,
                                Xml nvarchar(max) NOT NULL
                            );
                        END TRY
                        BEGIN CATCH
                            IF ERROR_NUMBER() <> 2714 THROW;
                        END CATCH;
                        """;
                    command.ExecuteNonQuery();
                    schemaReady = true;
                }
            }
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Xml FROM ColdCall.DataProtectionKeys ORDER BY Id";
        using var reader = command.ExecuteReader();
        var elements = new List<XElement>();
        while (reader.Read()) elements.Add(XElement.Parse(reader.GetString(0)));
        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT ColdCall.DataProtectionKeys (FriendlyName, Xml) VALUES (@Name, @Xml)";
        command.Parameters.Add("@Name", System.Data.SqlDbType.NVarChar, 450).Value = (object?)friendlyName ?? DBNull.Value;
        command.Parameters.Add("@Xml", System.Data.SqlDbType.NVarChar, -1).Value = element.ToString(SaveOptions.DisableFormatting);
        command.ExecuteNonQuery();
    }
}
