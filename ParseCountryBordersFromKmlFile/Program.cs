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

                string kmlFilePath = Path.Combine(projectDirectory, "Data\\UIA_World_Countries_Boundaries.kml");

                XDocument doc = XDocument.Load(kmlFilePath);
                List<XElement> placemarks = doc.Descendants().Where(x => x.Name.LocalName == "Placemark").ToList();

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
                            await PerisitCountry(countryCode, countryName, geoBorder.AsText());
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

            Console.ReadLine();
        }

        private static async Task PerisitCountry(string countryCode, string countryName, string countryBorder)
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
    }
}

/*
    USE [CountryBoundaries]
    GO

    SET ANSI_NULLS ON
    GO

    SET QUOTED_IDENTIFIER ON
    GO

    CREATE TABLE[dbo].[Country]
    (
       [CountryCode][nvarchar](3) NOT NULL,
       [CountryName] [nvarchar] (40) NOT NULL,
       [CountryBorder] [geography]
    NOT NULL,
    CONSTRAINT[PK_CountryCode] PRIMARY KEY CLUSTERED
    (
      [CountryCode] ASC
    )WITH(PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON[PRIMARY]
    ) ON[PRIMARY] TEXTIMAGE_ON[PRIMARY]
    GO


    CREATE PROCEDURE [dbo].[uspInsertCountry]
    (
	    @countryCode nvarchar(3),
	    @countryName nvarchar(40),
	    @CountryBorder nvarchar(max)
    )
    AS
    BEGIN
        SET NOCOUNT ON

	    DECLARE @g geography = geography::STGeomFromText(@CountryBorder, 4326);  

        INSERT INTO Country
		    (
			    CountryCode, 
			    CountryName, 
			    CountryBorder
		    )
        values 
		    (
			    @countryCode, 
			    @countryName, 
			    @g
		    )
    END;
    GO
 */
