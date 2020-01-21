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

        [FunctionName("ResultWorker")]
        public static async Task Run([QueueTrigger("zwiftresultrequests", Connection = "zwiftresultsstorage_STORAGE")]string myQueueItem, ILogger log)
        {
            log.LogInformation($"C# Queue trigger function processed: {myQueueItem}");

            dynamic request = JsonConvert.DeserializeObject(myQueueItem);

            string eventId = request.text;

            CommandParser.TryParse(
                (string)request.text,
                (CommandNames.Event, async parameters => await DoEvent(parameters, request, log)),
                (CommandNames.Team, async parameters => await DoTeam(parameters, request,log)));
        }

        private static async Task DoTeam(string[] parameters, dynamic request, ILogger log)
        {
            string jsonResult = string.Empty;
            using(var httpClient = new HttpClient())
            {
                var sourceUrl = $"https://zwiftpower.com/api3.php?do=team_results&id={ZwiftPowerConstants.DropoutsTeamId}";
                jsonResult = await httpClient.GetStringAsync(sourceUrl);
            }


            var lastEventCount = 1;
            if(parameters.Length > 0)
            {
                if(int.TryParse(parameters[0], out var userDefinedEventCount))
                {
                    lastEventCount = userDefinedEventCount;
                }
            }

            dynamic results = JsonConvert.DeserializeObject(jsonResult);

            var resultMessage = new StringBuilder();
            resultMessage.AppendLine(":warning: Dropout Team Results");
            resultMessage.AppendLine($"Results for the last {lastEventCount} event(s)");

            string json = results.events.ToString();
            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json);

            foreach(dynamic zwiftEvent in dictionary.Values.TakeLast(lastEventCount))
            {
                var eventResults = GetEventResults((string)zwiftEvent.zid);
                resultMessage.AppendLine($"{zwiftEvent.title} - http://zwiftpower.com/events/{zwiftEvent.zid}");
                resultMessage.Append(CreateSlackResponseString(eventResults));
            }

            PostMessage(request, resultMessage.ToString(), log, false);

            dynamic GetEventResults(string eventId)
            {
                return ((IEnumerable<dynamic>)results.data).Where(result => string.Equals((string)result.zid, eventId));
            }
        }

        private static async Task DoEvent(string[] parameters, dynamic request, ILogger log)
        {
            if(!int.TryParse(parameters.First(), out var eventId))
            {
                await PostMessage(request.response_url, "Invalid event id!", log, true);
                return;
            }

            var titleTask = GetRideTitleAsync(eventId.ToString());
            var resultsTask = GetResultsDataAsync(log, eventId.ToString());

            await Task.WhenAll(titleTask, resultsTask);

            var resultsJson = await resultsTask;
            var title = await titleTask;

            log.LogInformation($"Received Result - Parsing Data. Result looks like: {resultsJson.Take(20)}");
            dynamic results = JsonConvert.DeserializeObject(resultsJson);

            log.LogInformation("Building Slack Response String");

            var teamResults = ((IEnumerable<dynamic>)results.data).Where(result => result.tname == ZwiftPowerConstants.DropoutsTeamName);
            var resultMessage = new StringBuilder();
            resultMessage.AppendLine(title);
            resultMessage.AppendLine("Results for Dropouts:");
            resultMessage.Append(CreateSlackResponseString(teamResults));

            await PostMessage(request, resultMessage.ToString(), log, false);
        }

        private static async Task PostMessage(dynamic requestMessage, string message, ILogger log, bool ephemeral)
        {
            using (var client = new HttpClient())
            {
                var requestUri = (string)requestMessage.response_url;
                log.LogInformation($"Posting response string to {requestUri}");
                await client.PostAsync(
                    requestUri,
                    new StringContent(
                        JsonConvert.SerializeObject(new
                        {
                            replace_original = true,
                            response_type = ephemeral ? "ephemeral" : "in_channel",
                            text = message
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

        private static string CreateSlackResponseString(IEnumerable<dynamic> teamResults)
        {
            StringBuilder resultMessage = new StringBuilder();
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
