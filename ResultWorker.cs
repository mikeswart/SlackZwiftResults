using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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

            var titleTask = GetRideTitleAsync(eventId);
            var resultsTask = GetResultsDataAsync(log, eventId);

            Task.WaitAll(titleTask, resultsTask);

            var resultsJson = await resultsTask;
            var title = await titleTask;

            log.LogInformation($"Received Result - Parsing Data. Result looks like: {resultsJson.Take(20)}");
            dynamic results = JsonConvert.DeserializeObject(resultsJson);

            log.LogInformation("Building Slack Response String");
            var slackResponseString = CreateSlackResponseString(title, results);

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

        private static async Task<string> GetResultsDataAsync(ILogger log, string eventId)
        {
            using (var client = new HttpClient())
            {
                var resultsUrl = $"https://zwiftpower.com/api3.php?do=event_results&zid={eventId}";
                log.LogInformation($"Querying Zwiftpower for results: {resultsUrl}");
                return await client.GetStringAsync(resultsUrl);
            }
        }

        private static string CreateSlackResponseString(string title, dynamic results)
        {
            var teamResults = ((IEnumerable<dynamic>)results.data).Where(result => result.tname == DropoutsTeamName);
            var resultMessage = new StringBuilder();
            resultMessage.AppendLine(title);
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

        private static async Task<string> GetRideTitleAsync(string eventId)
        {
            using(var httpClient = new HttpClient())
            {
                var sourceUrl = $"https://zwiftpower.com/events.php?zid={eventId}";
                var source = await httpClient.GetStringAsync(sourceUrl);

                Match match = Regex.Match(source, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>", RegexOptions.IgnoreCase);
                if(match != null && match.Success)
                {
                    return match.Groups["Title"].Value;
                }

                return "Unknown EventId";
            }
        }
    }
}
