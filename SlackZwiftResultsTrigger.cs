using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Linq;
using System.Web;

namespace Dropouts.ZwiftResults
{
    public static class SlackZwiftResultsTrigger
    {
        [FunctionName("SlackZwiftResultsTrigger")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [Queue("zwiftresultrequests", Connection = "zwiftresultsstorage_STORAGE")] ICollector<string> resultWorkerQueue,
            ILogger log)
        {
            log.LogInformation($"Received Request on {nameof(SlackZwiftResultsTrigger)}");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            log.LogInformation("Request Body:" + requestBody);

            string jsonQueueMessage = string.Empty;
            try
            {
                var queryString = HttpUtility.ParseQueryString(requestBody);
                jsonQueueMessage = JsonConvert.SerializeObject(
                    queryString.AllKeys.ToDictionary(
                        key => key,
                        key => queryString[key]));
            }
            catch (System.Exception ex)
            {
                log.LogError($"Error parsing slack request.\nRequest Body: {requestBody}\n Exception: {ex.Message}");
                return BuildResponse("Oops, something went wrong with the slack framework.");
            }

            log.LogInformation("Adding request to the queue");
            try
            {
                resultWorkerQueue.Add(jsonQueueMessage);
            }
            catch (System.Exception ex)
            {
                log.LogError($"Error queuing work request.\nRequest text: {jsonQueueMessage}\n Exception: {ex.Message}");
                return BuildResponse("Oops, something went wrong with the slack framework.");
            }

            return BuildResponse("Retreiving Results...");

            IActionResult BuildResponse(string errorMessage)
            {
                return new JsonResult(new
                {
                    response_type = "ephemeral",
                    text = errorMessage
                });
            }
        }
    }
}
