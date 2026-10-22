using System;
using System.Collections.Generic;

namespace ImportCEIDGByDate.Data.FirmyTemp;

public partial class BusinessType
{
    public int TypDzialalnosciId { get; set; }

    public string TypDzialalnosci { get; set; } = null!;

    public string? TypDzialalnosciSkrot { get; set; }

    public virtual ICollection<Company> Companies { get; set; } = new List<Company>();
}
