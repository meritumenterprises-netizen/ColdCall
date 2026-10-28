using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace ImportCEIDGByDate
{
    public static class XmlUtils
    {
        public static string GetInnerTextOrEmpty(this XmlDocument doc, string elementName)
        {
            XmlElement elem = doc.SelectSingleNode($"Detail/{elementName}") as XmlElement;
            if (elem != null)
            {
                return elem.InnerText.Trim();
            }
            return "";
        }

        public static bool HasElement(this XmlDocument doc, string elementName)
        {
            XmlElement elem = doc.SelectSingleNode($"Detail/{elementName}") as XmlElement;
            return elem != null && !string.IsNullOrEmpty(elem.InnerText);
        }
    }
}
