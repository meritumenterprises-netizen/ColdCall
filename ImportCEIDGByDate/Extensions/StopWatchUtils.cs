using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ImportCEIDGByDate
{
    public static class StopWatchUtils
    {
        public static string ElapsedTime (this Stopwatch sw)
        {
            return sw.Elapsed.ToString(@"mm\:ss\.fff");
        }
    }
}
