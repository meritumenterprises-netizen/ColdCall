using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace BusinessesEditor
{
    public class XmlFileMigrator
    {
        public static void MigrateXmlFile(string filePath, string newFilePath)
        {
            if (File.Exists(newFilePath))
            {
                return;
            }
            Console.WriteLine("Migrating XML file: " + filePath);
            File.Copy(filePath, newFilePath);
            XmlDocument xmlDocSource = new XmlDocument();
            xmlDocSource.Load(filePath);
            XmlDocument xmlDocDestination = new XmlDocument();
            xmlDocDestination.Load(filePath);

            List<XmlElement> xmlInActiveDetails = xmlDocDestination
                .SelectNodes(
                    "/Report/tblBusinessEntityStats/Detail_Collection/Detail[StatusDzialalnosci != 'Aktywny']"
                )!
                .Cast<XmlElement>()
                .ToList();

            xmlDocDestination.SelectSingleNode("/Report/tblBusinessEntityStats/Detail_Collection").InnerXml = "";

            foreach (XmlElement xmlDetail in xmlDocSource.SelectNodes("/Report/tblBusinessEntityStats/Detail_Collection/Detail"))
            {
                if (xmlDetail.SelectSingleNode("StatusDzialalnosci")?.InnerText == "Aktywny")
                {
                    Console.WriteLine("Adding active business entity: " + xmlDetail.SelectSingleNode("Lp.")?.InnerText);
                    XmlElement newDetail = xmlDocDestination.CreateElement("Detail");
                    newDetail.InnerXml = xmlDetail.InnerXml;
                    xmlDocDestination.SelectSingleNode("/Report/tblBusinessEntityStats/Detail_Collection").AppendChild(newDetail);

                    Console.WriteLine("Adding attributes to business entity: " + xmlDetail.SelectSingleNode("Lp.")?.InnerText);
                    if (newDetail.SelectSingleNode("Email") == null)
                    {
                        newDetail.AppendChild(xmlDocDestination.CreateElement("Email"));
                    }
                    newDetail.AppendChild(xmlDocDestination.CreateElement("GoogleEmail"));
                    if (newDetail.SelectSingleNode("AdresWWW") == null)
                    {
                        newDetail.AppendChild(xmlDocDestination.CreateElement("AdresWWW"));
                    }
                    newDetail.AppendChild(xmlDocDestination.CreateElement("GoogleAdresWWW"));
                    if (newDetail.SelectSingleNode("Telefon") == null)
                    {
                        newDetail.AppendChild(xmlDocDestination.CreateElement("Telefon"));
                    }
                    newDetail.AppendChild(xmlDocDestination.CreateElement("GoogleTelefon"));
                }
            }

            // Perform migration logic here
            xmlDocDestination.Save(newFilePath);
        }
    }
}
