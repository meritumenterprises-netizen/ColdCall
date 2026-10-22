using System;
using System.Collections.Generic;

namespace ImportCEIDGByDate.Data.FirmyTemp;

public partial class CompanyAddress
{
    public int CompanyAdresId { get; set; }

    public int CompanyId { get; set; }

    public int AddressId { get; set; }

    public virtual Adresy Address { get; set; } = null!;

    public virtual Company Company { get; set; } = null!;
}
