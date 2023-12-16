using Google.Cloud.Functions.Framework;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using GCP;
using System.IO;

namespace GCP
{
    public class Function : IHttpFunction
    {
        /// <summary>
        /// Logic for your function goes here.
        /// </summary>
        /// <param name="context">The HTTP context, containing the request and the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleAsync(HttpContext context)
        {
            // Create an instance of Class1
            string path = "..\\..\\..\\dicom_output";
            DICOMImporter importer = new DICOMImporter(path, true);
            VolumeDataset dataset = importer.Import();
            string myName;
            if (dataset != null)
            {
                myName = "YOU DONE IT !!";
            }
            else
            {
                myName = "Your are missing somethong  !!";
            }
            // Respond with the value of 'myname'
            await context.Response.WriteAsync($"Hello, from {myName}.");
        }
    }
}
