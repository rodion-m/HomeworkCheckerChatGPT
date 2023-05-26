// See: https://chat.openai.com/share/feff4901-b779-482b-b00a-c70c40e94503

using Octokit;
using Newtonsoft.Json;
using System.Text;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.Write("Enter the repository URL: "); //https://github.com/rodion-m/homework_gpt_example
        string repoUri = Console.ReadLine();

        Console.Write("Enter the homework assignment: "); //Напишите программу, вычисляющую кол-во дней до нового года
        string job = Console.ReadLine();

        IChatGptClient chatGptClient = new ChatGptClient("{YOUR-OPEN-AI-API-KEY}}");
        var gitHubClient = new GitHubClient(new ProductHeaderValue("HomeworkChecker"));
        var checker = new HomeworkChecker(gitHubClient, chatGptClient);

        HomeworkFeedback feedback = await checker.GetFeedbackForRepo(job, repoUri);
        
        Console.WriteLine($"Score: {feedback.Score}");
        Console.WriteLine($"Comments: {feedback.Comments}");
    }
}

public class HomeworkFeedback
{
    public int Score { get; set; }
    public string Comments { get; set; }
}

public interface IChatGptClient
{
    Task<string> GetResponseFromChatGpt(string message);
}

public class ChatGptClient : IChatGptClient
{
    private static readonly HttpClient HttpClient = new HttpClient();
    private readonly string _apiKey;

    public ChatGptClient(string apiKey)
    {
        _apiKey = apiKey;
    }

    public async Task<string> GetResponseFromChatGpt(string message)
    {
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri("https://api.openai.com/v1/chat/completions"),
            Method = HttpMethod.Post,
            Headers =
            {
                { "Authorization", $"Bearer {_apiKey}" }
            },
            Content = new StringContent(
                JsonConvert.SerializeObject(new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = "You are a helpful assistant."
                        },
                        new
                        {
                            role = "user",
                            content = message
                        }
                    }
                }), Encoding.UTF8, "application/json")
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseObject = JsonConvert.DeserializeObject<dynamic>(responseContent);

        return responseObject.choices[0].message.content;
    }
}

public class HomeworkChecker
{
    private readonly GitHubClient _gitHubClient;
    private readonly IChatGptClient _chatGptClient;

    public HomeworkChecker(GitHubClient gitHubClient, IChatGptClient chatGptClient)
    {
        _gitHubClient = gitHubClient;
        _chatGptClient = chatGptClient;
    }

    public async Task<HomeworkFeedback> GetFeedbackForRepo(string job, string repoUri)
    {
        var uriSegments = repoUri.Split('/');
        var owner = uriSegments[uriSegments.Length - 2];
        var repoName = uriSegments[uriSegments.Length - 1];

        var repoContents = await _gitHubClient.Repository.Content.GetAllContents(owner, repoName);
        var csharpFiles = repoContents.Where(file => file.Path.EndsWith(".cs"));

        var httpClient = new HttpClient();
        var allCode = new StringBuilder();

        foreach (var file in csharpFiles)
        {
            if (file.DownloadUrl != null)
            {
                var fileContent = await httpClient.GetStringAsync(file.DownloadUrl);
                allCode.AppendLine(fileContent);
            }
        }

        var prompt = $"Review the following C# code for the homework assignment: '{job}'. Check for code smells, multithreading errors, bad naming, and any other issues. After your analysis, provide specific comments on any issues you find and then write 'Score: ' followed by a grade from 2 to 5. A score of 2 means the job isn't done at all, and a score of 5 means the job is done perfectly.{Environment.NewLine}{allCode.ToString()}";

        var chatGptResponse = await _chatGptClient.GetResponseFromChatGpt(prompt);

        // Extract score from response
        var scoreMarker = "Score: ";
        int score = 0;
        var scoreIndex = chatGptResponse.IndexOf(scoreMarker);
        if (scoreIndex != -1)
        {
            var scoreStart = scoreIndex + scoreMarker.Length;
            var scoreText = chatGptResponse.Substring(scoreStart).TakeWhile(char.IsDigit);
            if (!int.TryParse(new string(scoreText.ToArray()), out score))
            {
                score = 0; // Assign a default score or handle it in a way that suits your needs
            }
        }

        // Extract comments from response
        var comments = scoreIndex != -1 ? chatGptResponse.Substring(0, scoreIndex) : chatGptResponse;

        return new HomeworkFeedback
        {
            Score = score,
            Comments = comments.Trim()
        };
    }



}


//TDD example:
// IChatGptClient chatGptClient = new ChatGptClient(openAiKey);
// var checker = new HomeworkChecker(gitHubClient, chatGptClient);
// var job = "Напишите программу, вычисляющую кол-во дней до нового года";
// var repoUri = "https://github.com/rodion-m/myrepo";
// HomeworkFeedback feedback = checker.GetFeedbackForRepo(job, repoUri);
// feedback.Score.Should().Be(5); //min 2, max 5
// feedback.Comments.Should().NotBeEmpty();
