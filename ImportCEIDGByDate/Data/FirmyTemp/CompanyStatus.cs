using System;
using System.Collections.Generic;

namespace ImportCEIDGByDate.Data.FirmyTemp;

public partial class CompanyStatus
{
    public byte StatusId { get; set; }

    public string Nazwa { get; set; } = null!;

    public virtual ICollection<Company> Companies { get; set; } = new List<Company>();
}
