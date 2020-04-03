using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;
using System.Data.Spatial;

namespace ParseCountryBordersFromKmlFile
{
    class Program
    {
        static async Task Main(string[] args)
        {
            StreamWriter streamWriter = null;

            try
            {
                // Get the current directory and make it a DirectoryInfo object.
                // Do not use Environment.CurrentDirectory, vistual studio 
                // and visual studio code will return different result:
                // Visual studio will return @"projectDir\bin\Release\netcoreapp2.0\", yet 
                // vs code will return @"projectDir\"
                var currentDirectory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

                // On windows, the current directory is the compiled binary sits,
                // so string like @"bin\Release\netcoreapp2.0\" will follow the project directory. 
                // Hense, the project directory is the great grand-father of the current directory.
                string projectDirectory = currentDirectory.Parent.Parent.Parent.FullName;

                string dataDirectory = Path.Combine(projectDirectory, "Data");

                var countriesSourceFilePath = Path.Combine(dataDirectory, "SeedCountry.csv");
                var countriesDestinationFilePath = Path.Combine(dataDirectory, "SeedCountryWithBoundary.csv");

                if (!File.Exists(countriesSourceFilePath))
                    throw new ArgumentException($"File '{countriesSourceFilePath}' does not exist");

                string kmlFilePath = Path.Combine(dataDirectory, "UIA_World_Countries_Boundaries.kml");

                if (!File.Exists(kmlFilePath))
                    throw new ArgumentException($"File '{kmlFilePath}' does not exist");

                // Read countries from file
                var countries = FileReader.GetCountries(FileReader.GetFileData(dataDirectory, "SeedCountry.csv"));

                streamWriter = new StreamWriter(path: countriesDestinationFilePath, append: false);
                await streamWriter.WriteLineAsync("CountryId,Name,Alpha2,Alpha3,AffiliationId,CountryBoundaries");

                using (var sr = new StreamReader(kmlFilePath))
                {
                    // Read all data from the reader
                    string kml = sr.ReadToEnd();

                    kml = kml.Replace("xmlns=\"http://earth.google.com/kml/2.0\"", ""); // HACK
                    kml = kml.Replace("xmlns='http://earth.google.com/kml/2.0'", "");   // DOUBLE HACK
                    kml = kml.Replace("xmlns=\"http://earth.google.com/kml/2.1\"", ""); // MULTI HACK!
                    kml = kml.Replace("xmlns='http://earth.google.com/kml/2.1'", "");   // M-M-M-M-M-M-M-MONSTER HACK!!!!

                    kml = kml.Replace("xmlns=\"http://www.opengis.net/kml/2.2\"", "");  // HACK
                    kml = kml.Replace("xmlns='http://www.opengis.net/kml/2.2'", "");    // DOUBLE HACK

                    // Open the downloaded xml in an System.Xml.XmlDocument to allow for XPath searching
                    System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                    doc.LoadXml(kml);

                    // Try to find some sort of name for this kml from various places
                    System.Xml.XmlNode node = doc.SelectSingleNode("//Document[name]/name");

                    // Load Placemarks recursively and put them in folders
                    System.Xml.XmlNode documentNode = doc.SelectSingleNode("/kml/Document");
                    if (documentNode == null)
                        documentNode = doc.SelectSingleNode("/kml");

                    if (documentNode != null)
                    {
                        // Find Folders and initialize them recursively
                        System.Xml.XmlNodeList folders = documentNode.SelectNodes("Folder");
                        foreach (System.Xml.XmlNode folderNode in folders)
                        {
                            // Parse all Placemarks that have a name and Polygon
                            System.Xml.XmlNodeList placemarkNodes = folderNode.SelectNodes("Placemark");

                            foreach (System.Xml.XmlNode placemarkNode in placemarkNodes)
                            {
                                System.Xml.XmlNodeList simpleDataNodes = placemarkNode.SelectNodes("ExtendedData/SchemaData/SimpleData");

                                var countryCode = simpleDataNodes[2].InnerText;
                                var countryName = simpleDataNodes[1].InnerText;

                                Console.WriteLine($"Parsing {countryName}...");

                                DbGeography geoBorder = null;

                                System.Xml.XmlNodeList polygonNodes = placemarkNode.SelectNodes("Polygon");
                                foreach (System.Xml.XmlNode polygonNode in polygonNodes)
                                {
                                    // Parse Outer Ring
                                    System.Xml.XmlNode outerRingNode = polygonNode.SelectSingleNode("outerBoundaryIs/LinearRing/coordinates");
                                    if (outerRingNode != null)
                                    {
                                        var points = GeographicCoordinate.ParseCoordinates(outerRingNode);
                                        var geoPolygon = GeographicCoordinate.ConvertGeoCoordinatesToPolygon(points);

                                        if (geoBorder == null)
                                        {
                                            geoBorder = geoPolygon;
                                        }
                                        else
                                        {
                                            try
                                            {
                                                geoBorder = geoBorder.Union(geoPolygon);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.ForegroundColor = ConsoleColor.Blue;
                                                Console.WriteLine(ex.Message);
                                                Console.ResetColor();
                                            }
                                        }
                                    }
                                }

                                polygonNodes = placemarkNode.SelectNodes("MultiGeometry/Polygon");
                                foreach (System.Xml.XmlNode polygonNode in polygonNodes)
                                {
                                    // Parse Outer Ring
                                    System.Xml.XmlNode outerRingNode = polygonNode.SelectSingleNode("outerBoundaryIs/LinearRing/coordinates");
                                    if (outerRingNode != null)
                                    {
                                        var points = GeographicCoordinate.ParseCoordinates(outerRingNode);
                                        var geoPolygon = GeographicCoordinate.ConvertGeoCoordinatesToPolygon(points);

                                        if (geoBorder == null)
                                        {
                                            geoBorder = geoPolygon;
                                        }
                                        else
                                        {
                                            try
                                            {
                                                geoBorder = geoBorder.Union(geoPolygon);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.ForegroundColor = ConsoleColor.Blue;
                                                Console.WriteLine(ex.Message);
                                                Console.ResetColor();
                                            }
                                        }
                                    }
                                }

                                try
                                {
                                    await SaveCountryInDatabase(countryCode, countryName, geoBorder.AsText());
                                    await SaveCountryInFile(countryCode, countries, geoBorder, streamWriter);
                                }
                                catch (Exception ex)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"Country {countryCode} is not added. Error: {ex.Message}");
                                    Console.ResetColor();
                                }
                            }
                        }
                    }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Done");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
            finally
            {
                if (streamWriter != null)
                    streamWriter.Dispose();
            }

            Console.ReadLine();
        }

        private static async Task SaveCountryInDatabase(string countryCode, string countryName, string countryBorder)
        {
            using (var sqlConnection = new SqlConnection("Data Source=localhost;Initial Catalog=CountryBoundaries;Integrated Security=True;MultipleActiveResultSets=True"))
            {
                if (ConnectionState.Open != sqlConnection.State)
                    await sqlConnection.OpenAsync();

                using (var command = sqlConnection.CreateCommand())
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.CommandText = "uspInsertCountry";

                    command.Parameters.Add("@countryCode", SqlDbType.NVarChar).Value = countryCode;
                    command.Parameters.Add("@CountryName", SqlDbType.NVarChar).Value = countryName;
                    command.Parameters.Add("@countryBorder", SqlDbType.NVarChar).Value = countryBorder;

                    command.ExecuteNonQuery();
                }
            }
        }

        private static async Task SaveCountryInFile(string countryCode, IEnumerable<Country> countries, DbGeography geoBorder, StreamWriter streamWriter)
        {
            var country = countries.FirstOrDefault(c => c.CC2 == countryCode || c.CC3 == countryCode);

            if (country != null)
                await streamWriter.WriteLineAsync($"{country.CountryId},{country.Name},{country.CC2},{country.CC3},{country.AffiliationId},{geoBorder.AsGml()}");
        }
    }
}