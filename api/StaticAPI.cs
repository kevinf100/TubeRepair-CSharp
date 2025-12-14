using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TubeRepair_CSharp.api
{
    public class StaticAPI
    {
        private static readonly string _deviceKey = Guid.NewGuid().ToString("N"); // hex format, no hyphens

        /// <summary>
        /// Serves the categories.cat static file for sidebar menu
        /// </summary>
        public static IResult CategoriesCat()
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "static", "categories.cat");

            if (!System.IO.File.Exists(filePath))
            {
                return Results.NotFound();
            }

            return Results.File(filePath, contentType: "text/plain");
        }

        /// <summary>
        /// Legacy login bypass for YouTube Classic (first layer)
        /// </summary>
        public static IResult LegacyLoginBypass1()
        {
            string response = $"r2={_deviceKey}\nhmackr2={_deviceKey}";
            return Results.Content(response, "text/plain");
        }

        /// <summary>
        /// Legacy login bypass for YouTube Classic (second layer)
        /// </summary>
        public static IResult LegacyLoginBypass2()
        {
            string response = $"Auth={_deviceKey}";
            return Results.Content(response, "text/plain");
        }

        /// <summary>
        /// Login bypass for Google YouTube - registerDevice endpoint
        /// </summary>
        public static IResult LoginBypass()
        {
            string response = $"DeviceId={_deviceKey}\nDeviceKey={_deviceKey}";
            return Results.Content(response, "text/plain");
        }
    }
}
