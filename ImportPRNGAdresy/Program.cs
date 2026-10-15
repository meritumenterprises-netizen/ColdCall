using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
const string SchemaSql = @"
IF SCHEMA_ID(N'ColdCall') IS NULL EXEC(N'CREATE SCHEMA ColdCall');
IF OBJECT_ID(N'ColdCall.Adresy',N'U') IS NULL BEGIN CREATE TABLE ColdCall.Adresy(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Adresy PRIMARY KEY,WojewodztwoId int NOT NULL,PowiatId int NOT NULL,GminaId int NOT NULL,MiejscowoscId int NOT NULL,Rodzaj nvarchar(200) NULL,Latitude decimal(9,6) NOT NULL,Longitude decimal(9,6) NOT NULL,Location geography NOT NULL,PrngId varchar(50) NULL,CONSTRAINT FK_Adresy_Wojewodztwa FOREIGN KEY(WojewodztwoId) REFERENCES ColdCall.Wojewodztwa(Id),CONSTRAINT FK_Adresy_Powiaty FOREIGN KEY(PowiatId) REFERENCES ColdCall.Powiaty(Id),CONSTRAINT FK_Adresy_Gminy FOREIGN KEY(GminaId) REFERENCES ColdCall.Gminy(Id),CONSTRAINT FK_Adresy_Miejscowosci FOREIGN KEY(MiejscowoscId) REFERENCES ColdCall.Miejscowosci(Id)); END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'ColdCall.Adresy') AND name=N'UX_Adresy_PrngId') CREATE UNIQUE INDEX UX_Adresy_PrngId ON ColdCall.Adresy(PrngId) WHERE PrngId IS NOT NULL;
IF OBJECT_ID(N'ColdCall.AdresyImportStage',N'U') IS NULL CREATE TABLE ColdCall.AdresyImportStage(PrngId varchar(50) NULL,Nazwa nvarchar(250) NOT NULL,Rodzaj nvarchar(200) NULL,Wojewodztwo nvarchar(250) NOT NULL,Powiat nvarchar(250) NOT NULL,Gmina nvarchar(250) NOT NULL,Latitude decimal(9,6) NOT NULL,Longitude decimal(9,6) NOT NULL);";
const string ImportSql = @"
SET XACT_ABORT ON; BEGIN TRAN;
INSERT ColdCall.Wojewodztwa(Name) SELECT DISTINCT s.Wojewodztwo FROM ColdCall.AdresyImportStage s WHERE NOT EXISTS(SELECT 1 FROM ColdCall.Wojewodztwa x WHERE x.Name=s.Wojewodztwo);
INSERT ColdCall.Powiaty(Name) SELECT DISTINCT s.Powiat FROM ColdCall.AdresyImportStage s WHERE NOT EXISTS(SELECT 1 FROM ColdCall.Powiaty x WHERE x.Name=s.Powiat);
INSERT ColdCall.Gminy(Name) SELECT DISTINCT s.Gmina FROM ColdCall.AdresyImportStage s WHERE NOT EXISTS(SELECT 1 FROM ColdCall.Gminy x WHERE x.Name=s.Gmina);
INSERT ColdCall.Miejscowosci(Name) SELECT DISTINCT s.Nazwa FROM ColdCall.AdresyImportStage s WHERE NOT EXISTS(SELECT 1 FROM ColdCall.Miejscowosci x WHERE x.Name=s.Nazwa);
UPDATE a SET WojewodztwoId=w.Id,PowiatId=p.Id,GminaId=g.Id,MiejscowoscId=m.Id,Rodzaj=s.Rodzaj,Latitude=s.Latitude,Longitude=s.Longitude,Location=geography::Point(CONVERT(float,s.Latitude),CONVERT(float,s.Longitude),4326) FROM ColdCall.Adresy a JOIN ColdCall.AdresyImportStage s ON s.PrngId=a.PrngId JOIN ColdCall.Wojewodztwa w ON w.Name=s.Wojewodztwo JOIN ColdCall.Powiaty p ON p.Name=s.Powiat JOIN ColdCall.Gminy g ON g.Name=s.Gmina JOIN ColdCall.Miejscowosci m ON m.Name=s.Nazwa WHERE s.PrngId IS NOT NULL;
INSERT ColdCall.Adresy(WojewodztwoId,PowiatId,GminaId,MiejscowoscId,Rodzaj,Latitude,Longitude,Location,PrngId) SELECT w.Id,p.Id,g.Id,m.Id,s.Rodzaj,s.Latitude,s.Longitude,geography::Point(CONVERT(float,s.Latitude),CONVERT(float,s.Longitude),4326),s.PrngId FROM ColdCall.AdresyImportStage s JOIN ColdCall.Wojewodztwa w ON w.Name=s.Wojewodztwo JOIN ColdCall.Powiaty p ON p.Name=s.Powiat JOIN ColdCall.Gminy g ON g.Name=s.Gmina JOIN ColdCall.Miejscowosci m ON m.Name=s.Nazwa WHERE s.PrngId IS NULL OR NOT EXISTS(SELECT 1 FROM ColdCall.Adresy a WHERE a.PrngId=s.PrngId);
COMMIT;";

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(args.Length > 0 ? args[0] : "appsettings.json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var zipPath = Path.GetFullPath(cfg.ZipPath); if (!File.Exists(zipPath)) throw new FileNotFoundException("PRNG ZIP not found", zipPath);
await using var cn = new SqlConnection(cfg.ConnectionString); await cn.OpenAsync(); await Exec(cn, SchemaSql); await Exec(cn, "TRUNCATE TABLE ColdCall.AdresyImportStage;");
var tmp = Path.Combine(Path.GetTempPath(), "PRNG_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp); var dbfPath = Path.Combine(tmp, "PRNG.dbf");
try
{
    using (var z = ZipFile.OpenRead(zipPath)) { var e = z.Entries.FirstOrDefault(x => x.FullName.EndsWith(".dbf", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("No DBF in ZIP"); e.ExtractToFile(dbfPath, true); var c = z.Entries.FirstOrDefault(x => x.FullName.EndsWith(".cpg", StringComparison.OrdinalIgnoreCase)); if (c != null) c.ExtractToFile(Path.ChangeExtension(dbfPath, ".cpg"), true); }
    using var rd = new DbfReader(dbfPath); var t = StageTable(); int n = 0, skip = 0;
    using var bulk = new SqlBulkCopy(cn, SqlBulkCopyOptions.TableLock, null) { DestinationTableName = "ColdCall.AdresyImportStage", BatchSize = cfg.BatchSize, BulkCopyTimeout = 0 }; foreach (DataColumn c in t.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
    foreach (var r in rd.ReadRecords()) { var name = G(r, "nazwaGlown"); var woj = G(r, "wojewodz"); var pow = G(r, "powiat"); var gm = G(r, "gmina"); if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(woj) || string.IsNullOrWhiteSpace(pow) || string.IsNullOrWhiteSpace(gm) || !Dms(G(r, "wspGeograf"), out var lat, out var lon)) { skip++; continue; } t.Rows.Add(G(r, "idPRNG"), name.Trim(), N(G(r, "rodzaj")), woj.Trim(), pow.Trim(), gm.Trim(), lat, lon); n++; if (t.Rows.Count >= cfg.BatchSize) { await bulk.WriteToServerAsync(t); t.Clear(); Console.Write($"\rStaged {n:N0}"); } }
    if (t.Rows.Count > 0) await bulk.WriteToServerAsync(t); Console.WriteLine($"\rStaged {n:N0}; skipped {skip:N0}"); await Exec(cn, ImportSql, 0); Console.WriteLine("Import completed.");
}
finally { try { Directory.Delete(tmp, true); } catch { } }
static string G(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? v : ""; static object N(string s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s.Trim();
static bool Dms(string s, out decimal lat, out decimal lon) { lat = lon = 0; var m = Regex.Matches(s ?? "", @"(-?\d{1,3})°\s*(\d{1,2})'\s*([\d.,]+)"""); if (m.Count != 2) return false; decimal F(Match x) { var d = decimal.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture); var mi = decimal.Parse(x.Groups[2].Value, CultureInfo.InvariantCulture); var se = decimal.Parse(x.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture); return (d < 0 ? -1 : 1) * (Math.Abs(d) + mi / 60m + se / 3600m); } lat = F(m[0]); lon = F(m[1]); return lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180; }
static DataTable StageTable() { var t = new DataTable(); foreach (var x in new[] { ("PrngId", typeof(string)), ("Nazwa", typeof(string)), ("Rodzaj", typeof(string)), ("Wojewodztwo", typeof(string)), ("Powiat", typeof(string)), ("Gmina", typeof(string)), ("Latitude", typeof(decimal)), ("Longitude", typeof(decimal)) }) t.Columns.Add(x.Item1, x.Item2); return t; }
static async Task Exec(SqlConnection c, string sql, int timeout = 120) { await using var q = new SqlCommand(sql, c) { CommandTimeout = timeout }; await q.ExecuteNonQueryAsync(); }
sealed record Config(string ConnectionString, string ZipPath, int BatchSize = 5000);
sealed record DbfField(string Name, char Type, int Length, int Decimals);
sealed class DbfReader : IDisposable { readonly FileStream fs; readonly Encoding enc; readonly int hlen, rlen, count; public List<DbfField> Fields { get; } = []; public DbfReader(string p) { fs = File.OpenRead(p); using var b = new BinaryReader(fs, Encoding.ASCII, true); b.ReadByte(); b.ReadBytes(3); count = b.ReadInt32(); hlen = b.ReadInt16(); rlen = b.ReadInt16(); b.ReadBytes(20); while (fs.Position < hlen) { var x = b.ReadByte(); if (x == 13) break; var nb = new byte[11]; nb[0] = x; b.Read(nb, 1, 10); var name = Encoding.ASCII.GetString(nb).TrimEnd('\0', ' '); var type = (char)b.ReadByte(); b.ReadBytes(4); var len = b.ReadByte(); var dec = b.ReadByte(); b.ReadBytes(14); Fields.Add(new(name, type, len, dec)); } var cpg = Path.ChangeExtension(p, ".cpg"); enc = File.Exists(cpg) ? Encoding.GetEncoding(File.ReadAllText(cpg).Trim()) : Encoding.UTF8; fs.Position = hlen; } public IEnumerable<Dictionary<string, string>> ReadRecords() { var buf = new byte[rlen]; for (int i = 0; i < count; i++) { if (fs.Read(buf, 0, buf.Length) != buf.Length) yield break; if (buf[0] == 42) continue; int p = 1; var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); foreach (var f in Fields) { d[f.Name] = enc.GetString(buf, p, f.Length).Trim().TrimEnd('\0'); p += f.Length; } yield return d; } } public void Dispose() => fs.Dispose(); }
