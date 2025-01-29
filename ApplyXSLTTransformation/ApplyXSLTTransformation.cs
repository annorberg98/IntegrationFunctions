using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using System.Xml.Xsl;
using System.Xml;
using Microsoft.Net.Http.Headers;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace IntegrationFunctions
{
    public static class ApplyXSLTTransformation
    {
        [FunctionName("ApplyXSLTTransformation")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            try
            {
                string stgAccConnectionString = System.Environment.GetEnvironmentVariable("StorageAccountConnString", EnvironmentVariableTarget.Process);
                string containerName = System.Environment.GetEnvironmentVariable("ContainerName", EnvironmentVariableTarget.Process);

                if (string.IsNullOrEmpty(stgAccConnectionString))
                    throw new Exception("Storage Account Connection String configuration is missing.");
                if (string.IsNullOrEmpty(containerName))
                    throw new Exception("Container name configuration is missing.");

                // Retrieve headers from HTTP request and check if the header is present and not null or empty
                if (!req.Headers.TryGetValue("XsltFileName", out var headerValues) || string.IsNullOrEmpty(headerValues.FirstOrDefault()))
                {
                    throw new Exception("Header 'XsltFileName' is missing or empty.");
                }

                string xsltFileName = headerValues.FirstOrDefault();

                string outputContentType = string.Empty;
                if (!req.Headers.TryGetValue("Output-Content-Type", out var outHeaderValues) ||
                   String.IsNullOrEmpty(outHeaderValues.FirstOrDefault()))
                    outputContentType = "text/xml";
                else outputContentType = outHeaderValues.FirstOrDefault();

                //Parse input XML
                string xmlInput = await new StreamReader(req.Body).ReadToEndAsync();
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(xmlInput);

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(stgAccConnectionString);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer container = blobClient.GetContainerReference(containerName);
                CloudBlockBlob blob = container.GetBlockBlobReference(xsltFileName);

                string xsltContent = null;
                using (var memoryStream = new MemoryStream())
                {
                    await blob.DownloadToStreamAsync(memoryStream);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    using (var reader = new StreamReader(memoryStream))
                    {
                        xsltContent = await reader.ReadToEndAsync();
                    }
                }

                // Apply XSLT transformation
                XslCompiledTransform xslt = new XslCompiledTransform();
                using (var reader = XmlReader.Create(new StringReader(xsltContent)))
                {
                    xslt.Load(reader);
                }

                StringWriter stringWriter = new StringWriter();
                using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter, xslt.OutputSettings))
                {
                    xslt.Transform(xmlDocument, xmlWriter);
                }
                string outputXml = stringWriter.ToString();

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(outputXml, Encoding.Default, outputContentType),
                };
            }
            catch (Exception ex) 
            {
                var errorResponse = new JArray
                {
                    new JObject
                    {
                        ["name"] = "ApplyXSLTTransformation function",
                        ["type"] = "Internal Error",
                        ["status"] = "Failed",
                        ["code"] = "500",
                        ["startTime"] = DateTime.UtcNow.ToString("o"),  // ISO 8601 format
                        ["endTime"] = DateTime.UtcNow.ToString("o"),
                        ["errorMessage"] = ex.Message
                    }
                };

                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(errorResponse.ToString(), Encoding.Default, @"application/json")
                };
            }
        }
    }
}
