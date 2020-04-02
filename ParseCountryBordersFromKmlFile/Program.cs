using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml.Linq;
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

                XDocument doc = XDocument.Load(kmlFilePath);
                List<XElement> placemarks = doc.Descendants().Where(x => x.Name.LocalName == "Placemark").ToList();

                streamWriter = new StreamWriter(path: countriesDestinationFilePath, append: false);
                await streamWriter.WriteLineAsync("CountryId,Name,Alpha2,Alpha3,AffiliationId,CountryBoundaries");

                foreach (XElement placemark in placemarks)
                {
                    var schemaData = placemark.Descendants().Where(p => p.Name.LocalName == "SchemaData").FirstOrDefault();
                    var simpleData = schemaData.Descendants().Where(s => s.Name.LocalName == "SimpleData").ToList();

                    var countryCode = simpleData.Attributes().Where(a => a.Value == "ISO").Select(a => a.Parent.Value).FirstOrDefault();
                    var countryName = simpleData.Attributes().Where(a => a.Value == "COUNTRY").Select(a => a.Parent.Value).FirstOrDefault();

                    var polygons = placemark.Descendants().Where(p => p.Name.LocalName == "Polygon").ToList();

                    DbGeography geoBorder = null;
                    DbGeography geoBorderLeft = null;
                    DbGeography geoBorderRight = null;

                    foreach (var polygon in polygons)
                    {
                        var coordinates = polygon.Descendants().Where(p => p.Name.LocalName == "coordinates").Select(z => z.Value).FirstOrDefault();

                        var points = coordinates.Split(',').ToList();

                        // Remove first longitude
                        points.RemoveAt(0);

                        // Remove last latitude
                        points.RemoveAt(points.Count - 1);

                        if (points.Count() < 5)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Country {countryCode}. Coordinates cannot be used to create polygon: {coordinates}");
                            Console.ResetColor();

                            continue;
                        }

                        // Add end point
                        points.Add(points[0]);

                        var geoCoordinates = GeographicCoordinate.ConvertStringArrayToGeographicCoordinates(points.ToArray());
                        var geoPolygon = GeographicCoordinate.ConvertGeoCoordinatesToPolygon(geoCoordinates);

                        if (countryCode != "RU")
                        {
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
                                    //Console.ForegroundColor = ConsoleColor.Blue;
                                    //Console.WriteLine(ex.Message);
                                    //Console.ResetColor();
                                }
                            }
                        }
                        else // countryCode == "RU"
                        {
                            if (points.Count() < 100)
                            {
                                continue;
                            }

                            if (geoCoordinates[0].Longitude > 0)
                            {
                                if (geoBorderLeft == null)
                                {
                                    geoBorderLeft = geoPolygon;
                                }
                                else
                                {
                                    try
                                    {
                                        geoBorderLeft = geoBorderLeft.Union(geoPolygon);
                                    }
                                    catch (Exception ex)
                                    {
                                        //Console.ForegroundColor = ConsoleColor.Blue;
                                        //Console.WriteLine(ex.Message);
                                        //Console.ResetColor();
                                    }
                                }
                            }
                            else
                            {
                                if (geoBorderRight == null)
                                {
                                    geoBorderRight = geoPolygon;
                                }
                                else
                                {
                                    try
                                    {
                                        geoBorderRight = geoBorderRight.Union(geoPolygon);
                                    }
                                    catch (Exception ex)
                                    {
                                        //Console.ForegroundColor = ConsoleColor.Blue;
                                        //Console.WriteLine(ex.Message);
                                        //Console.ResetColor();
                                    }
                                }
                            }
                        }
                    }

                    try
                    {
                        if (countryCode == "RU")
                            geoBorder = geoBorderLeft;

                        if (geoBorder.Length > 22000)
                        {
                            await SaveCountryInDatabase(countryCode, countryName, geoBorder.AsText());
                            await SaveCountryInFile(countryCode, countries, geoBorder, streamWriter);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Country {countryCode} is not added. Error: {ex.Message}");
                        Console.ResetColor();
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