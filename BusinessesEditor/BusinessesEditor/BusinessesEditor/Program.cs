namespace BusinessesEditor
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            var FilesToMigrate = Directory.GetFiles(@"CEIDG", "*.xml").ToList();
            var FilesMigrated = Directory.GetFiles(@"CEIDG_Modified", "*.xml").ToList();

            foreach (var file in FilesToMigrate)
            {
                var fileName = Path.GetFileName(file);
                if (!FilesMigrated.Contains(Path.Combine(@"CEIDG_Modified", fileName)))
                {
                    using (var migratingWindow = new MigratingXmlFilesWindow())
                    {
                        migratingWindow.textBoxMigratingFile.Text = fileName;
                        migratingWindow.Show();
                        Application.DoEvents();
                        migratingWindow.textBoxMigratingFile.Text = fileName;
                        Application.DoEvents();
                        XmlFileMigrator.MigrateXmlFile(file, Path.Combine(@"CEIDG_Modified", fileName));
                        migratingWindow.Close();
                    }
                }
            }
            Application.Run(new Form1());
        }
    }
}