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

        private ImportStatistics Statistics { get; set; }
        public CeidgCompanyXmlConverter(
            ImportStatistics statistics, 
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
        }

        public async Task<Company> ConvertXmlToNewCompany (int wojewodztwoId, string xml)
        {
            var company = new Company();
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);
            company.BusinessType = 1; // Jednoosobowa dzialalność gospodarcza
            company.WojewodztwoId = wojewodztwoId;
            company.Name = xmlDoc.GetInnerTextOrEmpty("NazwaPodmiotu");
            company.FirstName = xmlDoc.GetInnerTextOrEmpty("Imie");
            company.LastName = xmlDoc.GetInnerTextOrEmpty("Nazwisko");
            company.Nip = xmlDoc.GetInnerTextOrEmpty("Nip");
            company.Regon = xmlDoc.GetInnerTextOrEmpty("Regon");
            company.Phone = xmlDoc.GetInnerTextOrEmpty("Telefon");
            company.Website = xmlDoc.GetInnerTextOrEmpty("AdresWWW");
            company.DataModifiedOn = Statistics.NewDate;

            #region Adresy
            company.Street = xmlDoc.GetInnerTextOrEmpty("Ulica");
            company.BuildingNumber = xmlDoc.GetInnerTextOrEmpty("NrBudynku");
            company.ApartmentNumber = xmlDoc.GetInnerTextOrEmpty("NrLokalu");
            company.PostalCode = xmlDoc.GetInnerTextOrEmpty("KodPocztowy");
            if (xmlDoc.HasElement("Powiat"))
            {
                var existingPowiat = this.FirmyTempDbContext.Powiaties.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Powiat")).FirstOrDefault();
                if (existingPowiat == null)
                {
                    this.FirmyTempDbContext.Powiaties.Add(new Powiaty() { Name = xmlDoc.GetInnerTextOrEmpty("Powiat") });
                    await this.FirmyTempDbContext.SaveChangesAsync();
                }
                company.PowiatId = this.FirmyTempDbContext.Powiaties.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Powiat")).FirstOrDefault().Id;
            }
            if (xmlDoc.HasElement("Gmina"))
            {
                var existingGmina = this.FirmyTempDbContext.Gminies.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Gmina")).FirstOrDefault();
                if (existingGmina == null)
                {
                    this.FirmyTempDbContext.Gminies.Add(new Gminy() { Name = xmlDoc.GetInnerTextOrEmpty("Gmina") });
                    await this.FirmyTempDbContext.SaveChangesAsync();
                }
                company.GminaId= this.FirmyTempDbContext.Gminies.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Gmina")).FirstOrDefault().Id;
            }

            if (xmlDoc.HasElement("Miejscowosc"))
            {
                var existingMiejscowosc = this.FirmyTempDbContext.Miejscowoscis.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Miejscowosc")).FirstOrDefault();
                if (existingMiejscowosc == null)
                {
                    this.FirmyTempDbContext.Miejscowoscis.Add(new Miejscowosci() { Name = xmlDoc.GetInnerTextOrEmpty("Miejscowosc") });
                    await this.FirmyTempDbContext.SaveChangesAsync();
                }
                company.MiejscowoscId = this.FirmyTempDbContext.Miejscowoscis.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Miejscowosc")).FirstOrDefault().Id;
            }
            #endregion

            #region Statusy działalności
            if (xmlDoc.HasElement("StatusDzialalnosci"))
            {
                company.StatusDzialalnosciId = this.FirmyTempDbContext.CompanyStatuses.Where((s) => s.Nazwa == xmlDoc.GetInnerTextOrEmpty("StatusDzialalnosci")).First().StatusId;
                var statusDzialanosci = xmlDoc.GetInnerTextOrEmpty("StatusDzialalnosci");
                switch (statusDzialanosci)
                {
                    case "Aktywny":
                        {
                            company.StartedAt = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"));
                            try { company.ResumedOn = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataWznowieniaDzialalnosci")); } catch { }
                        }
                        break;
                    case "Zawieszony":
                        {
                            company.SuspendedOn = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataZawieszeniaDzialalnosci"));
                            try { company.StartedAt = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci")); } catch { }
                        }
                        break;
                    case "Działalność prowadzona wyłącznie w formie spółki cywilnej":
                        {
                            company.StartedAt = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"));
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
                foreach (var kodPkdNoDots in kodyPkd.Distinct())
                {
                    if (kodPkdNoDots == "N/A")
                    {
                        continue;
                    }
                    var pkdCodeEntity = this.FirmyTempDbContext.PkdCodes.Where((pkd) => pkd.CodeWithoutDots == kodPkdNoDots).Single();
                    var compPkd = new CompanyPkd();
                    compPkd.PkdCodeId = pkdCodeEntity.Id;
                    compPkd.PkdCode = pkdCodeEntity;
                    compPkd.Prevailing = false;
                    company.CompanyPkds.Add(compPkd);
                }
                if (xmlDoc.HasElement("GlownyKodPkd"))
                {
                    var pkdGlowny = company.CompanyPkds.FirstOrDefault(pkd =>
                        pkd.PkdCode.CodeWithoutDots == xmlDoc.GetInnerTextOrEmpty("GlownyKodPkd"));
                    if (pkdGlowny != null)
                    {
                        pkdGlowny.Prevailing = true;
                    }
                }
            }
            #endregion

            return company;
        }

        public async Task<Company?> ConvertXmlToExistingCompany(int wojewodztwoId, string xml)
        {
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);

            var nip = xmlDoc.GetInnerTextOrEmpty("Nip");
            if (string.IsNullOrEmpty(nip))
            {
                return null;
            }

            var company = await this.FirmyTempDbContext.Companies
                .Include(comp => comp.CompanyPkds).ThenInclude(pkd => pkd.PkdCode)
                .FirstOrDefaultAsync(comp => comp.Nip == nip);

            if (company == null)
            {
                return null;
            }

            company.BusinessType = 1; // Jednoosobowa dzialalność gospodarcza
            company.WojewodztwoId = wojewodztwoId;
            company.Name = xmlDoc.GetInnerTextOrEmpty("NazwaPodmiotu");
            company.FirstName = xmlDoc.GetInnerTextOrEmpty("Imie");
            company.LastName = xmlDoc.GetInnerTextOrEmpty("Nazwisko");
            company.Nip = xmlDoc.GetInnerTextOrEmpty("Nip");
            company.Regon = xmlDoc.GetInnerTextOrEmpty("Regon");
            company.Phone = xmlDoc.GetInnerTextOrEmpty("Telefon");
            company.Website = xmlDoc.GetInnerTextOrEmpty("AdresWWW");
            company.DataModifiedOn = Statistics.NewDate;

            #region Adresy
            company.Street = xmlDoc.GetInnerTextOrEmpty("Ulica");
            company.BuildingNumber = xmlDoc.GetInnerTextOrEmpty("NrBudynku");
            company.ApartmentNumber = xmlDoc.GetInnerTextOrEmpty("NrLokalu");
            company.PostalCode = xmlDoc.GetInnerTextOrEmpty("KodPocztowy");
            if (xmlDoc.HasElement("Powiat"))
            {
                var existingPowiat = this.FirmyTempDbContext.Powiaties.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Powiat")).FirstOrDefault();
                if (existingPowiat == null)
                {
                    this.FirmyTempDbContext.Powiaties.Add(new Powiaty() { Name = xmlDoc.GetInnerTextOrEmpty("Powiat") });
                    await this.FirmyTempDbContext.SaveChangesAsync();
                }
                company.PowiatId = this.FirmyTempDbContext.Powiaties.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Powiat")).FirstOrDefault().Id;
            }
            if (xmlDoc.HasElement("Gmina"))
            {
                var existingGmina = this.FirmyTempDbContext.Gminies.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Gmina")).FirstOrDefault();
                if (existingGmina == null)
                {
                    this.FirmyTempDbContext.Gminies.Add(new Gminy() { Name = xmlDoc.GetInnerTextOrEmpty("Gmina") });
                    await this.FirmyTempDbContext.SaveChangesAsync();
                }
                company.GminaId = this.FirmyTempDbContext.Gminies.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Gmina")).FirstOrDefault().Id;
            }

            if (xmlDoc.HasElement("Miejscowosc"))
            {
                var existingMiejscowosc = this.FirmyTempDbContext.Miejscowoscis.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Miejscowosc")).FirstOrDefault();
                if (existingMiejscowosc == null)
                {
                    this.FirmyTempDbContext.Miejscowoscis.Add(new Miejscowosci() { Name = xmlDoc.GetInnerTextOrEmpty("Miejscowosc") });
                    await this.FirmyTempDbContext.SaveChangesAsync();
                }
                company.MiejscowoscId = this.FirmyTempDbContext.Miejscowoscis.Where((p) => p.Name == xmlDoc.GetInnerTextOrEmpty("Miejscowosc")).FirstOrDefault().Id;
            }
            #endregion

            #region Statusy działalności
            if (xmlDoc.HasElement("StatusDzialalnosci"))
            {
                company.StatusDzialalnosciId = this.FirmyTempDbContext.CompanyStatuses.Where((s) => s.Nazwa == xmlDoc.GetInnerTextOrEmpty("StatusDzialalnosci")).First().StatusId;
                var statusDzialanosci = xmlDoc.GetInnerTextOrEmpty("StatusDzialalnosci");
                switch (statusDzialanosci)
                {
                    case "Aktywny":
                        {
                            company.StartedAt = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"));
                            try { company.ResumedOn = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataWznowieniaDzialalnosci")); } catch { }
                        }
                        break;
                    case "Zawieszony":
                        {
                            company.SuspendedOn = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataZawieszeniaDzialalnosci"));
                            try { company.StartedAt = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci")); } catch { }
                        }
                        break;
                    case "Działalność prowadzona wyłącznie w formie spółki cywilnej":
                        {
                            company.StartedAt = DateTime.Parse(xmlDoc.GetInnerTextOrEmpty("DataRozpoczeciaDzialalnosci"));
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
            var desiredPkds = await FirmyTempDbContext.PkdCodes
                .Where(pkd => codes.Contains(pkd.CodeWithoutDots!)).ToListAsync();
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
                    link = new CompanyPkd { Company = company, PkdCodeId = pkd.Id, PkdCode = pkd };
                    company.CompanyPkds.Add(link);
                }
                link.Prevailing = string.Equals(pkd.CodeWithoutDots, prevailingCode, StringComparison.OrdinalIgnoreCase);
            }
            #endregion

            return company;
        }


        public async Task<string> ConvertCompanyToXml ( Company company )
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml("<Detail></Detail>");
            return xmlDoc.OuterXml;
        }

        public async Task AddNewCompaniesFromImportToTemp()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.ImportNewCompanies, "Importing new companies");
            var newCompanies = this.FirmyImportRepository.GetNewImportCompanies();
            Statistics.CurrentPhase.EntriesCount = newCompanies.Count;
            var companyCounter = 0;
            var companyTotal = newCompanies.Count;
            foreach (var company in newCompanies)
            {
                var newCompany = await ConvertXmlToNewCompany(company.WojewodztwoId, company.CompanyXml);
                Console.WriteLine($"Adding a new company {companyCounter+1}/{companyTotal} with NIP={newCompany.Nip}");
                FirmyTempDbContext.Companies.Add(newCompany);
                await FirmyTempDbContext.SaveChangesAsync();
                company.ImportDate = DateTime.Now;
                company.Imported = true;
                await FirmyImportDbContext.SaveChangesAsync();
                companyCounter++;
            }
        }

        public async Task UpdateExistingCompaniesFromImportToTemp()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.UpdateExistingCompanies, "Updating existing companies");
            var existingCompanies = this.FirmyImportRepository.GetUpdateImportCompanies();
            Statistics.CurrentPhase.EntriesCount = existingCompanies.Count;
            Statistics.CurrentPhase.EntriesCount = existingCompanies.Count;
            var companyCounter = 0;
            var companyTotal = existingCompanies.Count;
            foreach (var company in existingCompanies)
            {
                var existingCompany = await ConvertXmlToExistingCompany(company.WojewodztwoId, company.CompanyXml);
                if (existingCompany == null)
                    throw new InvalidDataException($"Company with NIP={company.Nip} was not found.");
                Console.WriteLine($"Updating existing company {companyCounter + 1}/{companyTotal} with NIP={existingCompany.Nip}");
                await FirmyTempDbContext.SaveChangesAsync();
                company.ImportDate = DateTime.Now;
                company.Imported = true;
                await FirmyImportDbContext.SaveChangesAsync();
                companyCounter++;
            }

        }

    }
}
