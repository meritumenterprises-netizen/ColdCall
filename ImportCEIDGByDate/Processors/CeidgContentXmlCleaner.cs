using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using ImportCEIDGByDate.Repositories;
using ImportCEIDGByDate.Utils;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate.Processors
{
    public class CeidgContentXmlCleaner
    {
        public FirmyTempRepository FirmyTempRepository { get; private set; }
        public FirmyImportRepository FirmyImportRepository { get; private set; }

        public FirmyImportDbContext FirmyImportDbContext { get; private set; }
        public FirmyTempDbContext FirmyTempDbContext { get; private set; }

        private Statistics Statistics { get; set; }

        private CeidgCompanyXmlConverter CeidgConverter { get; set; }
        private readonly CeidgDeferredAddressImport deferredAddresses;
        public bool DeferAddressAssignment { get; init; }

        public GlobalSettings GlobalSettings { get; set; }

        public List<Miejscowosci> Miejscowosci
        {
            get
            {
                return FirmyTempDbContext.Miejscowoscis.ToList();
            }
        }

        public List<Powiaty> Powiaty
        {
            get
            {
                return FirmyTempDbContext.Powiaties.ToList();
            }
        }

        public List<Gminy> Gminy
        {
            get
            {
                return FirmyTempDbContext.Gminies.ToList();
            }
        }

        public List<Wojewodztwa> Wojewodztwa
        {
            get
            {
                return FirmyTempDbContext.Wojewodztwas.ToList();
            }
        }

        public CeidgContentXmlCleaner(
            Statistics statistics,
            FirmyTempRepository firmyTempRepository,
            FirmyImportRepository firmyImportRepository,
            FirmyImportDbContext firmyImportDbContext,
            FirmyTempDbContext firmyTempDbContext,
            GlobalSettings settings)
        {
            this.Statistics = statistics;
            this.FirmyTempRepository = firmyTempRepository;
            this.FirmyImportRepository = firmyImportRepository;

            this.FirmyImportDbContext = firmyImportDbContext;
            this.FirmyTempDbContext = firmyTempDbContext;
            deferredAddresses = new CeidgDeferredAddressImport(firmyTempDbContext, firmyImportRepository);

            this.CeidgConverter = new CeidgCompanyXmlConverter(statistics, firmyTempRepository, firmyImportRepository, firmyImportDbContext, firmyTempDbContext);
            this.GlobalSettings = settings;
        }


        public async Task PerformContentXmlCleanup()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.ContentXmlCleanup,"");
            Console.WriteLine($"Performing ContentXml cleanup, records with NIP not in Companies, should be added as companies");
            var xmlConverter = new CeidgCompanyXmlConverter(Statistics, FirmyTempRepository, FirmyImportRepository, FirmyImportDbContext, FirmyTempDbContext);

            var globalNips = await FirmyTempDbContext.Companies.Select((c) => c.Nip).ToHashSetAsync();
            var contentXmlNips = await FirmyTempDbContext.CompanyXml.Select((c) => c.Nip).ToHashSetAsync();
            var missingNips = contentXmlNips.Except(globalNips);

            Console.WriteLine($"Found {missingNips.Count()} missing NIPs, present in CompanyXml, but not in Companies");

            var counter = 0;

            foreach (var nip in missingNips)
            {
                Console.WriteLine($"{counter+1}/{missingNips.Count()} Processing NIP={nip}");
                var contentXmlRecord = FirmyTempDbContext.CompanyXml.Where((c) => c.Nip == nip).SingleOrDefault();
                var xml = contentXmlRecord.XmlContent;
                var company = await xmlConverter.ConvertXmlToNewCompany(0, xml);
                company.Miejscowosc = Miejscowosci.Where((m) => m.Id == company.MiejscowoscId).SingleOrDefault();
                company.Gmina = Gminy.Where((g) => g.Id == company.GminaId).SingleOrDefault();
                company.Powiat = Powiaty.Where((p) => p.Id == company.PowiatId).SingleOrDefault();

                var miejscowoscId = company.MiejscowoscId;
                var powiatId = company.PowiatId;
                var gminaId = company.GminaId;
                if (miejscowoscId > 0 && powiatId > 0 && gminaId > 0)
                {
                    var companyWithMiejscowoscGminaPowiat = FirmyTempDbContext.Companies.Where((comp) =>
                        comp.MiejscowoscId == company.Miejscowosc.Id &&
                        comp.GminaId == company.Gmina.Id &&
                        comp.PowiatId == company.PowiatId).FirstOrDefault();
                    if (companyWithMiejscowoscGminaPowiat != null)
                    {
                        company.Wojewodztwo = Wojewodztwa.Where((w) => w.Id == companyWithMiejscowoscGminaPowiat.WojewodztwoId).FirstOrDefault();
                    }
                    else
                    {
                        company.WojewodztwoId = 1; // brak województwa
                    }
                }
                else
                {
                    company.WojewodztwoId = 1; // brak województwa
                }

                FirmyTempDbContext.Companies.Add(company);
                await FirmyTempDbContext.SaveChangesAsync();
                counter++;
            }

            Console.WriteLine($"Processed {counter} records from CompanyXml");

            Statistics.FinishCurrentPhase("");
        }
    }
}
