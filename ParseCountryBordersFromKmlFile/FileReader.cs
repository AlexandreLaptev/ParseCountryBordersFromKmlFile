using System;
using System.Collections.Generic;
using System.IO;

namespace ParseCountryBordersFromKmlFile
{
    /// <summary>
    /// Reads data from the comma delimited files.
    /// </summary>
    public static class FileReader
    {
        public static IEnumerable<string[]> GetFileData(string directoryPath, string fileName)
        {
            if (!Directory.Exists(directoryPath))
                throw new ArgumentException(string.Format("Directory \"{0}\" does not exist", directoryPath));

            var filePath = Path.Combine(directoryPath, fileName);

            if (!File.Exists(filePath))
                throw new ArgumentException(string.Format("File \"{0}\" does not exist", filePath));

            var data = new List<string[]>();

            using (var reader = new StreamReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                //Ignore the first line
                reader.ReadLine();

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line != null)
                        data.Add(line.Split(','));
                }
            }

            return data;
        }

        public static IEnumerable<Country> GetCountries(IEnumerable<string[]> textLines)
        {
            Console.WriteLine("Reading countries from comma delimited file...");

            var retVal = new List<Country>();

            foreach (var line in textLines)
            {
                retVal.Add(new Country
                {
                    CountryId = int.Parse(line[0]),
                    Name = line[1],
                    CC2 = line[2],
                    CC3 = line[3],
                    AffiliationId = int.Parse(line[4])
                });
            }

            return retVal;
        }
    }
}