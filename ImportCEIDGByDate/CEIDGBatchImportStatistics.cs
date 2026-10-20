using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate
{
    public class CEIDGBatchImportStatistics
    {
        public DateTime ImportDate { get; set; }
        public int CompaniesAdded { get; set; }
        public int CompaniesUpdated { get; set; }
        public int CompaniesSkipped { get; set; }
        public int CompaniesTotal { get; set; }
    }
}
