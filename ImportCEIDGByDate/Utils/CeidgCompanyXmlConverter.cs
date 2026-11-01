using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using ImportCEIDGByDate.Repositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ImportCEIDGByDate
{
    public class CeidgCompanyXmlConverter
    {
        public FirmyTempRepository FirmyTempRepository { get; private set; }
        public FirmyImportRepository FirmyImportRepository { get; private set; }

        public FirmyImportDbContext FirmyImportDbContext { get; private set; }
        public FirmyTempDbContext FirmyTempDbContext { get; private set; }

        private Statistics Statistics { get; set; }
        public CeidgLookupCache Lookups { get; }
        public CeidgCompanyXmlConverter(
            Statistics statistics, 
            FirmyTempRepository firmyTempRepository, 
            FirmyImportRepository firmyImportRepository, 
            FirmyImportDbContext firmyImportDbContext, 
            FirmyTempDbContext firmyTempDbContext)
        {
            this.Statistics = statistics;
            this.FirmyTempRepository = firmyTempRepository;
            this.FirmyImportRepository = firmyImportRepository;

            this.FirmyImportDbContext = firmyImportDbContext;
            this.FirmyTempDbContext = firmyTempDbContext;
            Lookups = new CeidgLookupCache(firmyTempDbContext);
        }

        public async Task<Company> ConvertXmlToNewCompany (int wojewodztwoId, string xml)
        {
            await Lookups.PrepareBatchAsync(new[] { xml });
            var company = new Company();
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);
            company.BusinessType = 1; // Jednoosobowa dzialalność gospodarcza
            company.WojewodztwoId = wojewodztwoId;
            if (wojewodztwoId == 1)
                company.SkipAddressVerification = true;
            
            company.Name = xmlDoc.GetInnerTextOrEmpty("NazwaPodmiotu");
            company.FirstName = xmlDoc.GetInnerTextOrEmpty("Imie");
            company.LastName = xmlDoc.GetInnerTextOrEmpty("Nazwisko");
            company.Nip = xmlDoc.GetInnerTextOrEmpty("Nip");
            company.Regon = xmlDoc.GetInnerTextOrEmpty("Regon");
            company.Phone = xmlDoc.GetInnerTextOrEmpty("Telefon");
            company.Website = xmlDoc.GetInnerTextOrEmpty("AdresWWW");
            company.DataModifiedOn = Statistics.NewDate;
            company.ImportedOn = Statistics.NewDate;

            #region Adresy
            company.Street = xmlDoc.GetInnerTextOrEmpty("Ulica");
            company.BuildingNumber = xmlDoc.GetInnerTextOrEmpty("NrBudynku");
            company.ApartmentNumber = xmlDoc.GetInnerTextOrEmpty("NrLokalu");
            company.PostalCode = xmlDoc.GetInnerTextOrEmpty("KodPocztowy");
            if (xmlDoc.HasElement("Powiat"))
            {
                company.PowiatId = Lookups.CountyId(xmlDoc.GetInnerTextOrEmpty("Powiat"));
            }
            if (xmlDoc.HasElement("Gmina"))
            {
                company.GminaId = Lookups.MunicipalityId(xmlDoc.GetInnerTextOrEmpty("Gmina"));
            }

            if (xmlDoc.HasElement("Miejscowosc"))
            {
                company.MiejscowoscId = Lookups.CityId(xmlDoc.GetInnerTextOrEmpty("Miejscowosc"));
            }
            company.AddressSort = CompanyAddressSort.Build(company, Lookups.CityName(company.MiejscowoscId));
            #endregion

            #region Statusy działalności
            if (xmlDoc.HasElement("StatusDzialalnosci"))
            {
                company.StatusDzialalnosciId = Lookups.StatusId(xmlDoc.GetInnerTextOrEmpty("StatusDzialalnosci"));
                var statusDzialanosci = xmlDoc.GetInnerTextOrEmpty("StatusDzialalnosci");
                switch (statusDzialanosci)
                {
                    case "Aktywny":
                        {
                            DateTime dataRozpoczecia = DateTime.MinValue;
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"), out dataRozpoczecia))
                            {
                                company.StartedAt = dataRozpoczecia;
                            }
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataWznowieniaDzialalnosci"), out var resumedOn))
                                company.ResumedOn = resumedOn;
                        }
                        break;
                    case "Zawieszony":
                        {
                            DateTime dataZawieszenia = DateTime.MinValue;
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataZawieszeniaDzialalnosci"), out dataZawieszenia))
                            {
                                company.SuspendedOn = dataZawieszenia;
                            }
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"), out var startedAt))
                                company.StartedAt = startedAt;
                        }
                        break;
                    case "Działalność prowadzona wyłącznie w formie spółki cywilnej":
                        {
                            DateTime dataRozpoczecia = DateTime.MinValue;
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"), out dataRozpoczecia))
                            {
                                company.StartedAt = dataRozpoczecia;
                            }
                        }
                        break;
                }
            }
            #endregion

            #region kody PKD
            var kodyPkd = new List<string>();
            if (xmlDoc.HasElement("GlownyKodPkd"))
            {
                kodyPkd.Add(xmlDoc.GetInnerTextOrEmpty("GlownyKodPkd"));
            }
            if (xmlDoc.HasElement("PozostaleKodyPkd"))
            {
                var kody = xmlDoc.GetInnerTextOrEmpty("PozostaleKodyPkd");
                kodyPkd.AddRange(Regex.Split(kody, Regex.Escape("$##$")).ToList());
            }
            if (kodyPkd.Count > 0)
            {
                company.CompanyPkds = new List<CompanyPkd>();
                foreach (var kodPkdNoDots in kodyPkd.Select(CeidgLookupCache.NormalizePkd).Distinct())
                {
                    if (string.IsNullOrWhiteSpace(kodPkdNoDots) || kodPkdNoDots == "N/A")
                    {
                        continue;
                    }
                    var compPkd = new CompanyPkd();
                    compPkd.PkdCodeId = Lookups.PkdId(kodPkdNoDots);
                    compPkd.Prevailing = false;
                    company.CompanyPkds.Add(compPkd);
                }
                if (!string.IsNullOrWhiteSpace(xmlDoc.GetInnerTextOrEmpty("GlownyKodPkd")) && xmlDoc.GetInnerTextOrEmpty("GlownyKodPkd") != "N/A")
                {
                    var pkdGlowny = company.CompanyPkds.FirstOrDefault(pkd =>
                        pkd.PkdCodeId == Lookups.PkdId(xmlDoc.GetInnerTextOrEmpty("GlownyKodPkd")));
                    if (pkdGlowny != null)
                    {
                        pkdGlowny.Prevailing = true;
                    }
                }
            }
            #endregion

            return company;
        }

        public async Task<Company?> ConvertXmlToExistingCompany(int wojewodztwoId, string xml, Company? existingCompany = null)
        {
            await Lookups.PrepareBatchAsync(new[] { xml });
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);

            var nip = xmlDoc.GetInnerTextOrEmpty("Nip");
            if (string.IsNullOrEmpty(nip))
            {
                return null;
            }

            var company = existingCompany ?? await FirmyTempRepository.FindByNipAsync(nip);

            if (company == null)
            {
                return null;
            }
            if (!string.Equals(company.Nip, nip, StringComparison.Ordinal))
                throw new InvalidDataException($"Loaded company NIP={company.Nip} does not match XML NIP={nip}.");

            company.BusinessType = 1; // Jednoosobowa dzialalność gospodarcza
            company.WojewodztwoId = wojewodztwoId;
            if (wojewodztwoId == 1)
                company.SkipAddressVerification = true;
            company.Name = xmlDoc.GetInnerTextOrEmpty("NazwaPodmiotu");
            company.FirstName = xmlDoc.GetInnerTextOrEmpty("Imie");
            company.LastName = xmlDoc.GetInnerTextOrEmpty("Nazwisko");
            company.Nip = xmlDoc.GetInnerTextOrEmpty("Nip");
            company.Regon = xmlDoc.GetInnerTextOrEmpty("Regon");
            company.Phone = xmlDoc.GetInnerTextOrEmpty("Telefon");
            company.Website = xmlDoc.GetInnerTextOrEmpty("AdresWWW");
            company.DataModifiedOn = Statistics.NewDate;
            company.ImportedOn = Statistics.NewDate;

            #region Adresy
            company.Street = xmlDoc.GetInnerTextOrEmpty("Ulica");
            company.BuildingNumber = xmlDoc.GetInnerTextOrEmpty("NrBudynku");
            company.ApartmentNumber = xmlDoc.GetInnerTextOrEmpty("NrLokalu");
            company.PostalCode = xmlDoc.GetInnerTextOrEmpty("KodPocztowy");
            if (xmlDoc.HasElement("Powiat"))
            {
                company.PowiatId = Lookups.CountyId(xmlDoc.GetInnerTextOrEmpty("Powiat"));
            }
            if (xmlDoc.HasElement("Gmina"))
            {
                company.GminaId = Lookups.MunicipalityId(xmlDoc.GetInnerTextOrEmpty("Gmina"));
            }

            if (xmlDoc.HasElement("Miejscowosc"))
            {
                company.MiejscowoscId = Lookups.CityId(xmlDoc.GetInnerTextOrEmpty("Miejscowosc"));
            }
            company.AddressSort = CompanyAddressSort.Build(company, Lookups.CityName(company.MiejscowoscId));
            #endregion

            #region Statusy działalności
            if (xmlDoc.HasElement("StatusDzialalnosci"))
            {
                company.StatusDzialalnosciId = Lookups.StatusId(xmlDoc.GetInnerTextOrEmpty("StatusDzialalnosci"));
                var statusDzialanosci = xmlDoc.GetInnerTextOrEmpty("StatusDzialalnosci");
                switch (statusDzialanosci)
                {
                    case "Aktywny":
                        {
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"), out var startedAt))
                                company.StartedAt = startedAt;
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataWznowieniaDzialalnosci"), out var resumedOn))
                                company.ResumedOn = resumedOn;
                        }
                        break;
                    case "Zawieszony":
                        {
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataZawieszeniaDzialalnosci"), out var suspendedOn))
                                company.SuspendedOn = suspendedOn;
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"), out var startedAt))
                                company.StartedAt = startedAt;
                        }
                        break;
                    case "Działalność prowadzona wyłącznie w formie spółki cywilnej":
                        {
                            if (DateTime.TryParse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"), out var startedAt))
                                company.StartedAt = startedAt;
                        }
                        break;
                }
            }
            #endregion

            #region kody PKD
            var prevailingCode = xmlDoc.GetInnerTextOrEmpty("GlownyKodPkd").Trim();
            var codes = xmlDoc.GetInnerTextOrEmpty("PozostaleKodyPkd")
                .Split("$##$", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Append(prevailingCode)
                .Where(code => !string.IsNullOrWhiteSpace(code) && code != "N/A")
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var desiredPkds = codes.Select(code => new { Id = Lookups.PkdId(code), CodeWithoutDots = code }).DistinctBy(pkd => pkd.Id).ToList();
            var missingCodes = codes.Except(desiredPkds.Select(pkd => pkd.CodeWithoutDots!),
                StringComparer.OrdinalIgnoreCase).ToList();
            if (missingCodes.Count > 0)
                throw new InvalidDataException($"Unknown PKD codes: {string.Join(", ", missingCodes)}");

            var desiredIds = desiredPkds.Select(pkd => pkd.Id).ToHashSet();
            foreach (var link in company.CompanyPkds.Where(link => !desiredIds.Contains(link.PkdCodeId)).ToList())
            {
                company.CompanyPkds.Remove(link);
                FirmyTempDbContext.CompanyPkds.Remove(link);
            }
            foreach (var pkd in desiredPkds)
            {
                var link = company.CompanyPkds.FirstOrDefault(link => link.PkdCodeId == pkd.Id);
                if (link == null)
                {
                    link = new CompanyPkd { Company = company, PkdCodeId = pkd.Id };
                    company.CompanyPkds.Add(link);
                }
                link.Prevailing = string.Equals(pkd.CodeWithoutDots, prevailingCode, StringComparison.OrdinalIgnoreCase);
            }
            #endregion

            return company;
        }



    }
}
