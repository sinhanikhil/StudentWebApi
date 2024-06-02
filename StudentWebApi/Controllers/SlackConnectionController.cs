using Microsoft.AspNetCore.Mvc;
using Models.DTO;
using System.Text.Json;

namespace StudentWebApi.Controllers
{
    [ApiController]
    [Route("Hackathon")]
    public class SlackConnectionController : Controller
    {
        private readonly HttpClient _slackConvoClient;
        private readonly HttpClient _slackConvoRepliesClient;

        public SlackConnectionController()
        {
            _slackConvoClient = new HttpClient();
            _slackConvoRepliesClient = new HttpClient();
        }

        /// <summary>
        /// edit course data in db
        /// </summary>
        /// <param name="channelId"></param>
        /// <param name="courseDTO"></param>
        /// <returns></returns>
        [HttpPost("enableSlack/{channelId}/{slackToken}")]
        public async Task<ActionResult> EnableSlackAsync([FromRoute] string channelId, [FromRoute] string slackToken)
        {
            if (!_slackConvoClient.DefaultRequestHeaders.Any())
            {
                _slackConvoClient.DefaultRequestHeaders.Add("Authorization", slackToken);
            }
            if (!_slackConvoRepliesClient.DefaultRequestHeaders.Any())
            {
                _slackConvoRepliesClient.DefaultRequestHeaders.Add("Authorization", slackToken);
            }
            await Hackathon(channelId);
            return Ok("Slack channel successfully registered");
        }

        private async Task Hackathon(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return;
            }

            bool hasMore = true;
            string cursor = null;

            var dataDict = new Dictionary<string, HelpQnA>();
            string filePath = $"C:\\Users\\nikhil.sinha\\workspace\\StudentWebApi\\{channelId}_Conv";
            using StreamWriter writer = new(filePath);
            int queriesCount = 1;
            while (hasMore)
            {
                var conversations = await FetchConversationHistory(channelId, cursor);
                var conversationsHistory = conversations.RootElement;

                var messagesArray = conversationsHistory.TryGetProperty("messages", out var conversationExists) ? conversationExists.EnumerateArray().ToArray() : Array.Empty<JsonElement>();

                foreach (var message in messagesArray)
                {
                    string ts = message.TryGetProperty("ts", out var tsExist) ? tsExist.GetString() : null;
                    string threadTs = message.TryGetProperty("thread_ts", out var threadTsExist) ? threadTsExist.GetString() : null;
                    string query = message.TryGetProperty("text", out var queryExists) ? queryExists.GetString() : null;
                    Console.WriteLine($"ts: {ts}");
                    if (ts == null || (threadTs != null && ts != threadTs) || query == null || query == string.Empty)
                    {
                        continue;
                    }

                    var comments = new List<string>();

                    var replies = await FetchReplies(channelId, ts);
                    var repliesHistory = replies.RootElement;

                    var repliesArray = repliesHistory.TryGetProperty("messages", out var repliesExists) ? repliesExists.EnumerateArray().ToArray() : Array.Empty<JsonElement>();

                    foreach (var replyMessage in repliesArray)
                    {
                        string tsReply = replyMessage.TryGetProperty("ts", out var tsReplyExists) ? tsReplyExists.GetString() : null;
                        string threadTsReply = replyMessage.TryGetProperty("thread_ts", out var threadTsReplyExists) ? threadTsReplyExists.GetString() : null;
                        string comment = replyMessage.TryGetProperty("text", out var commentExists) ? commentExists.GetString() : null;
                        if (tsReply == null || threadTsReply == null || (threadTsReply != null && tsReply == threadTsReply) || comment == null || comment == string.Empty)
                        {
                            continue;
                        }

                        comments.Add(comment);
                    }

                    if (!dataDict.ContainsKey(ts))
                    {
                        dataDict.Add(ts, new HelpQnA { Query = query, Comments = comments });
                    }
                }

                if (!conversationsHistory.GetProperty("has_more").GetBoolean())
                {
                    hasMore = false;
                }
                else
                {
                    cursor = conversationsHistory.GetProperty("response_metadata").GetProperty("next_cursor").GetString();
                }

                WriteToFileInChunks(writer, dataDict, queriesCount);
                dataDict.Clear();
            }
        }

        private static void WriteToFileInChunks(StreamWriter writer, Dictionary<string, HelpQnA> dataDict, int queriesCount)
        {
            foreach (var pair in dataDict)
            {
                var value = pair.Value;
                if (value.Comments.Count > 0)
                {
                    writer.WriteLine("'''");
                    writer.WriteLine($"# Question: {queriesCount}");
                    writer.WriteLine(value.Query);
                    if (value.Comments.Count > 0)
                    {
                        writer.WriteLine($"** Probable Answers: **");
                    }
                    for (int i = 0; i < value.Comments.Count; i++)
                    {
                        writer.WriteLine($"{i + 1}. {value.Comments[i]}");
                    }
                    queriesCount++;
                    writer.WriteLine("'''");
                }
            }
        }

        private async Task<JsonDocument> FetchConversationHistory(string channelId, string cursor = null, CancellationToken cancellationToken = default)
        {
            string limit = "200";
            var response = await _slackConvoClient.GetAsync($"https://slack.com/api/conversations.history?channel={channelId}&limit={limit}&cursor={cursor}", cancellationToken);
            response.EnsureSuccessStatusCode();
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        }

        private async Task<JsonDocument> FetchReplies(string channelId, string ts, CancellationToken cancellationToken = default)
        {
            var response = await _slackConvoRepliesClient.GetAsync($"https://slack.com/api/conversations.replies?channel={channelId}&ts={ts}", cancellationToken);
            response.EnsureSuccessStatusCode();
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        }
    }
}
