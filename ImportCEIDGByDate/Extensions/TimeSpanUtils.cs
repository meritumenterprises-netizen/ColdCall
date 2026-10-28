using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate.Utils
{
    public static class TimeSpanUtils
    {
        public static string AsString(this TimeSpan ts)
        {
            return ts.ToString(@"hh\:mm\:ss\.ffff");
        }
    }
}
