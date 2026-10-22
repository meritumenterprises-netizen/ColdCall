using System;
using System.Collections.Generic;

namespace ImportCEIDGByDate.Data.FirmyImport;

public partial class ImportOrigin
{
    public int Id { get; set; }

    public int WojewodztwoId { get; set; }

    public string CompanyXml { get; set; } = null!;

    public string Md5 { get; set; } = null!;

    public string? Nip { get; set; }

    public DateTime ImportDate { get; set; }
}
