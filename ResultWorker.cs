using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Dropouts.ZwiftResults
{
    public static class ResultWorker
    {
        private const string DropoutsTeamName = "&#9888;&#65039; Dropouts";

        [FunctionName("ResultWorker")]
        public static async Task Run([QueueTrigger("zwiftresultrequests", Connection = "zwiftresultsstorage_STORAGE")]string myQueueItem, ILogger log)
        {
            log.LogInformation($"C# Queue trigger function processed: {myQueueItem}");

            dynamic request = JsonConvert.DeserializeObject(myQueueItem);

            string eventId = request.text;

            string resultsJson = string.Empty;
            using (var client = new HttpClient())
            {
                var resultsUrl = $"https://zwiftpower.com/api3.php?do=event_results&zid={eventId}";
                log.LogInformation($"Querying Zwiftpower for results: {resultsUrl}");
                resultsJson = client.GetStringAsync(resultsUrl).Result;
            }

            log.LogInformation($"Received Result - Parsing Data. Result looks like: {resultsJson.Take(20)}");
            dynamic results = JsonConvert.DeserializeObject(resultsJson);

            log.LogInformation("Building Slack Response String");
            var slackResponseString = CreateSlackResponseString(results);

            using (var client = new HttpClient())
            {
                log.LogInformation($"Posting response string to {(string)request.response_url}");
                await client.PostAsync(
                    (string)request.response_url,
                    new StringContent(
                        JsonConvert.SerializeObject(new
                        {
                            replace_original = true,
                            response_type = "in_channel",
                            text = slackResponseString
                        })));
            }
        }

        private static string CreateSlackResponseString(dynamic results)
        {
            var teamResults = ((IEnumerable<dynamic>)results.data).Where(result => result.tname == DropoutsTeamName);
            var resultMessage = new StringBuilder();
            resultMessage.AppendLine("Results for Dropouts:");

            foreach (var categoryGrouping in teamResults.GroupBy(result => result.category))
            {
                resultMessage.AppendLine($"Category {categoryGrouping.Key}");
                foreach (var result in categoryGrouping)
                {
                    resultMessage.AppendFormat($"{result.position_in_cat} - {result.name}\n");
                }
            }

            return resultMessage.ToString();
        }
    }
}
