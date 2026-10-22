using System;
using System.Collections.Generic;

namespace ImportCEIDGByDate.Data.FirmyTemp;

public partial class PkdCode
{
    public int Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? CodeWithoutDots { get; set; }

    public bool NeedsClarification { get; set; }

}
