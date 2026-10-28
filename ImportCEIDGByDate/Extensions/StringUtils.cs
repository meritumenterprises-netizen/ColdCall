using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate.Utils
{
    public static class StringUtils
    {
        public static bool IsSomething(this string str)
        {
            return !string.IsNullOrWhiteSpace(str);
        }
    }
}
