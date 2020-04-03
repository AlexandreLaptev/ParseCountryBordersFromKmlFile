using System;
using System.Collections.Generic;
using System.Data.Spatial;
using System.Device.Location;
using System.Linq;
using System.Text;

namespace ParseCountryBordersFromKmlFile
{
    public class GeographicCoordinate
    {
        private const double Tolerance = 10.0 * .1;

        public GeographicCoordinate(double longitude, double latitude)
        {
            this.Longitude = longitude;
            this.Latitude = latitude;
        }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public static bool operator ==(GeographicCoordinate a, GeographicCoordinate b)
        {
            // If both are null, or both are same instance, return true.
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            // If one is null, but not both, return false.
            if (((object)a == null) || ((object)b == null))
            {
                return false;
            }

            var latResult = Math.Abs(a.Latitude - b.Latitude);
            var lonResult = Math.Abs(a.Longitude - b.Longitude);
            return (latResult < Tolerance) && (lonResult < Tolerance);
        }

        public static bool operator !=(GeographicCoordinate a, GeographicCoordinate b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            // Check for null values and compare run-time types.
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            var p = (GeographicCoordinate)obj;
            var latResult = Math.Abs(this.Latitude - p.Latitude);
            var lonResult = Math.Abs(this.Longitude - p.Longitude);
            return (latResult < Tolerance) && (lonResult < Tolerance);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (this.Latitude.GetHashCode() * 397) ^ this.Longitude.GetHashCode();
            }
        }

        /// <summary>
        /// Parse a list of coordinates
        /// </summary>
        /// <param name="coordinatesNode">The node containing coordinates to parse</param>
        public static List<GeoCoordinate> ParseCoordinates(System.Xml.XmlNode coordinatesNode)
        {
            string coordlist = coordinatesNode.InnerText.Trim();
            char[] splitters = { '\n', ' ', '\t', ',' };
            string[] lines = coordlist.Split(splitters);

            var coordinates = new List<GeoCoordinate>();

            for (int i = 0; i < lines.Length; i += 2)
            {
                string tokenLongitude = lines[i].Trim();
                if (tokenLongitude.Length == 0 || tokenLongitude == String.Empty)
                    continue;

                string tokenLatitude = lines[i + 1].Trim();
                if (tokenLatitude.Length == 0 || tokenLatitude == String.Empty)
                    continue;

                coordinates.Add(new GeoCoordinate(double.Parse(tokenLatitude), double.Parse(tokenLongitude)));
            }

            return coordinates;
        }

        public static DbGeography ConvertGeoCoordinatesToPolygon(IEnumerable<GeoCoordinate> coordinates)
        {
            var coordinateList = coordinates.ToList();
            if (coordinateList.First() != coordinateList.Last())
                throw new Exception("First and last point do not match. This is not a valid polygon");

            var count = 0;
            var sb = new StringBuilder();
            sb.Append(@"POLYGON((");

            foreach (var coordinate in coordinateList)
            {
                if (count == 0)
                    sb.Append(coordinate.Longitude + " " + coordinate.Latitude);
                else
                    sb.Append("," + coordinate.Longitude + " " + coordinate.Latitude);

                count++;
            }

            sb.Append(@"))");

            return DbGeography.PolygonFromText(sb.ToString(), DbGeography.DefaultCoordinateSystemId);
        }
    }
}