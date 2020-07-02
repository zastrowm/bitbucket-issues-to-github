<Query Kind="Program">
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <NuGetReference>Octokit</NuGetReference>
  <Namespace>Newtonsoft.Json</Namespace>
  <Namespace>Newtonsoft.Json.Linq</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Net.Http.Headers</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>Octokit</Namespace>
</Query>

// file to the exported bitbucket issues
string file = @"T:\nunit.applicationdomain-issues\db-2.0.json";

// the user/org that owns the repo
string ownerName = "someuser";
// the actual repository into which issues should be imported
string repoName = "some-repository";
// the original bitbucket url to issues, used to add links back to the original issues
string bitbucketIssueUrl = "https://bitbucket.org/zastrowm/nunit.applicationdomain_bitbucket/issues/";

// this will be a github token (can be acquired via https://github.com/settings/tokens)
string githubKey = Util.GetPassword("GitHub");

// make sure the issue number matches the original issue number
bool requireIssueNumberToMatch = true;
// true if the issue should be locked if it's closed
bool lockIssuesWhenClosed = false;
int numberToSkip = 0;

// configure what "states" represent a closed issue in github
HashSet<string> closedStatuses = new HashSet<string>(new[] {
  "closed", "duplicate", "invalid", "wontfix", "resolved"
});

// here you can map bitbucket users (via display name) to the corresponding github user
// the key is the original display name shown in bitbucket; the value is markdown to 
// replace the display name with.
Dictionary<string, string> userMapping = new Dictionary<string, string>{
  { "Mackenzie Zastrow",       "@zastrowm" },
  { "Izzy Coding ",            "@izzycoding" },
  { "Ruben Hakopian",          "@rubenhak" },
  { "John Lewin",              "@jlewin" },
  { "Daniel Rose",             "@DanielRose" },
  { "Brandon Ording",          "@bording" },
  { "Tomasz Pluskiewicz",      "@tpluscode" },
  { "Mathieu van Loon",        "[Mathieu van Loon](https://bitbucket.org/Matsie911)" },
  { "Andreas Gullberg Larsen", "@angularsen" },
  { "Frederik Carlier",        "@qmfrederik" },
  { "James John McGuire",      "@jamesjohnmcguire" },
};

async Task Main()
{
  var rawJson = File.ReadAllText(file);
  var bitbucket = JsonConvert.DeserializeObject<BitBucket>(rawJson);
  
  var issueActionLookup = CreateIssueActionLookup(bitbucket);

  var api = new GitHubClient(new Octokit.ProductHeaderValue("issue-migration"));
  api.Credentials = new Credentials(githubKey);

  (await api.Miscellaneous.GetRateLimits()).Dump("Rate Limits");
  
  var repo = await api.Repository.Get(ownerName, repoName);

  
  int expectedId = 1 + numberToSkip;
  
  foreach (var bitbucketIssue in  bitbucket.Issues.OrderBy(i => i.CreatedOn).Skip(numberToSkip))
  {
    while (requireIssueNumberToMatch && expectedId != bitbucketIssue.Id)
    {
      await CreateDummyIssue(api);
      await Delay();
      expectedId++;
    }
    
    var actionList = issueActionLookup.GetValueOrDefault(bitbucketIssue.Id) ?? new List<IIssueAction>();
    await TransferIssue(bitbucketIssue, api, actionList);
    expectedId++;
  }
}

private async Task Delay()
{
    // https://developer.github.com/v3/guides/best-practices-for-integrators/#dealing-with-abuse-rate-limits
    await Task.Delay(TimeSpan.FromSeconds(1));
}

private Dictionary<long, List<IIssueAction>> CreateIssueActionLookup(BitBucket bitbucket)
{
  var actions = new Dictionary<long, List<IIssueAction>>();

  List<IIssueAction> GetActionListForIssue(long issueId)
  {
    if (!actions.TryGetValue(issueId, out var list))
    {
      list = new List<IIssueAction>();
      actions[issueId] = list;
    }

    return list;
  }

  foreach (var comment in bitbucket.Comments)
    GetActionListForIssue(comment.Issue).Add(comment);

  foreach (var log in bitbucket.Logs)
    GetActionListForIssue(log.Issue).Add(log);

  foreach (var kvp in actions)
  {
    kvp.Value.Sort((a, b) =>
    {
      if (a == b)
      {
        // put log entries in before comments
        int aScore = a is Log ? 100 : 10;
        int bScore = b is Log ? 100 : 10;
        
        return aScore - bScore;
      }
      
      return a.CreatedOn.CompareTo(b.CreatedOn);
    });
  }

  return actions;
}

private async Task CreateDummyIssue(GitHubClient api)
{
  var response = await api.Issue.Create(ownerName, repoName, new NewIssue("Placeholder")
  {
    Body = "Placeholder issue so that the issue numbers match after the import from Bitbucket",
  });
  
  var update = response.ToUpdate();
  update.State = ItemState.Closed;
  await api.Issue.Update(ownerName, repoName, response.Number, update);
  
  if (lockIssuesWhenClosed)
  {
    await api.Issue.Lock(ownerName, repoName, response.Number);
  }
}

private async Task TransferIssue(Issue firstIssue, GitHubClient api, IEnumerable<IIssueAction> actionList)
{
  var response = await api.Issue.Create(ownerName, repoName, CreateIssue(firstIssue));
  await Delay();

  bool isClosed = false;

  foreach (var action in actionList)
  {

    if (action is Comment comment)
    {
      if (comment.Content == null)
      {
        action.Dump("Empty Comment");
        continue;
      }

      await api.Issue.Comment.Create(ownerName, repoName, response.Number, CreateComment(comment));
      await Delay();
    }
    else if (action is Log log)
    {
      if (log.Field == "content")
      {
        // we don't persist "edit" messages; they're not really needed for our purposes
        continue;
      }
      
      await api.Issue.Comment.Create(ownerName, repoName, response.Number, CreateFieldChangeComment(log));

      if (log.Field == "status")
      {
        if (closedStatuses.Contains(log.ChangedTo))
        {
          if (!isClosed)
          {
            var update = response.ToUpdate();
            update.State = ItemState.Closed;
            await api.Issue.Update(ownerName, repoName, response.Number, update);
            isClosed = true;
          }
        }
        else
        {
          if (isClosed)
          {
            var update = response.ToUpdate();
            update.State = ItemState.Open;
            await api.Issue.Update(ownerName, repoName, response.Number, update);
            isClosed = false;
          }
        }
      }
    }
  }

  if (isClosed && lockIssuesWhenClosed)
  {
    var update = response.ToUpdate();
    await api.Issue.Lock(ownerName, repoName, response.Number);
  }
}


Regex CodeLanguageFixup = new Regex("^```([\r\n]+)#!(?<lang>[^\r\n ]*)(?<nl>[\r\n]+)", RegexOptions.Multiline | RegexOptions.ExplicitCapture);
Regex CommitReference = new Regex("^→ <<cset (?<ref>[a-zA-Z0-9]{8,})>>(?<nl>[\r\n]+|$)", RegexOptions.Multiline | RegexOptions.ExplicitCapture);

public string TransformMarkup(string markdown)
{
  // Convert bitbuckets hashbangs for syntax highlighting to GitHubs more standard three ticks + language
  markdown = CodeLanguageFixup.Replace(markdown, m => "```" + m.Groups["lang"].Value + m.Groups["nl"].Value);
  // Convert bitbucket automatic sha1 references
  markdown = CommitReference.Replace(markdown, m => "Referenced by " + m.Groups["ref"].Value + m.Groups["nl"].Value);
  
  return markdown;
}

public string GetUsername(User user)
{
  if (user == null) {
    return "&lt;unknown&gt;";
  }
  
  var displayName = user.DisplayName;
  
  if (userMapping.TryGetValue(displayName, out var githubName)
      || (user.AccountId != null && userMapping.TryGetValue(user.AccountId, out githubName)))
  {
    displayName = githubName;
  }
  
  return displayName;
}

string nl = Environment.NewLine;

public NewIssue CreateIssue(Issue issue)
{
  var originalLink = $"[Bitbucket Issue #&#8203;{issue.Id}]({bitbucketIssueUrl}{issue.Id})";

  var body = "" // "&#8203;" is used to make sure that github doesn't turn it into an issue link
        + $"<sup>Issue created by {GetUsername(issue.Reporter)} as {originalLink} on {GetDate(issue.CreatedOn)}.</sup>"
        + $"{nl}"
        + TransformMarkup(issue.Content);

  return new NewIssue(issue.Title)
  {
    Body = body,
  };
}

// The way that dates are formatted
public string GetDate(DateTimeOffset dateTime)
  => dateTime.ToLocalTime().ToString("yyyy.MM.dd HH:mm");

public string CreateComment(Comment comment)
{
  var originalLink = $"[{GetDate(comment.CreatedOn)}]({bitbucketIssueUrl}{comment.Issue}#comment-{comment.Id})";

  var body = ""
        + $"<sup>On {originalLink}, {GetUsername(comment.User)} commented:</sup>"
        + $"{nl}"
        + TransformMarkup(comment.Content);

  return body;
}

public string CreateFieldChangeComment(Log log)
{
  var body = ""
        + $"<sup>On {GetDate(log.CreatedOn)} {GetUsername(log.User)} modified issue:</sup>"
        + $"{nl}"
        + $"**{log.Field}** changed `{log.ChangedFrom}` → `{log.ChangedTo}`";

  return body;
}

// Define other methods, classes and namespaces here

public partial class BitBucket
{
  [JsonProperty("milestones", Required = Required.Always)]
  public object[] Milestones { get; set; }
  [JsonProperty("attachments", Required = Required.Always)]
  public Attachment[] Attachments { get; set; }
  [JsonProperty("versions", Required = Required.Always)]
  public object[] Versions { get; set; }
  [JsonProperty("comments", Required = Required.Always)]
  public Comment[] Comments { get; set; }
  [JsonProperty("meta", Required = Required.Always)]
  public Meta Meta { get; set; }
  [JsonProperty("components", Required = Required.Always)]
  public Component[] Components { get; set; }
  [JsonProperty("issues", Required = Required.Always)]
  public Issue[] Issues { get; set; }
  [JsonProperty("logs", Required = Required.Always)]
  public Log[] Logs { get; set; }
}

public partial class Attachment
{
  [JsonProperty("url", Required = Required.Always)]
  public Uri Url { get; set; }
  [JsonProperty("path", Required = Required.Always)]
  public string Path { get; set; }
  [JsonProperty("issue", Required = Required.Always)]
  public long Issue { get; set; }
  [JsonProperty("user", Required = Required.Always)]
  public User User { get; set; }
  [JsonProperty("filename", Required = Required.Always)]
  public string Filename { get; set; }
}

public partial class User
{
  [JsonProperty("display_name", Required = Required.Always)]
  public string DisplayName { get; set; }
  [JsonProperty("account_id", Required = Required.AllowNull)]
  public string? AccountId { get; set; }
}


interface IIssueAction
{
  public DateTimeOffset CreatedOn { get; set; }
  public long Issue { get; set; }
}

public partial class Comment : IIssueAction
{
  [JsonProperty("content", Required = Required.AllowNull)]
  public string Content { get; set; }
  [JsonProperty("created_on", Required = Required.Always)]
  public DateTimeOffset CreatedOn { get; set; }
  [JsonProperty("user", Required = Required.Always)]
  public User User { get; set; }
  [JsonProperty("updated_on", Required = Required.AllowNull)]
  public DateTimeOffset? UpdatedOn { get; set; }
  [JsonProperty("issue", Required = Required.Always)]
  public long Issue { get; set; }
  [JsonProperty("id", Required = Required.Always)]
  public long Id { get; set; }
}

public partial class Component
{
  [JsonProperty("name", Required = Required.Always)]
  public string Name { get; set; }
}

public partial class Issue
{
  [JsonProperty("status", Required = Required.Always)]
  public string Status { get; set; }
  [JsonProperty("priority", Required = Required.Always)]
  public string Priority { get; set; }
  [JsonProperty("kind", Required = Required.Always)]
  public string Kind { get; set; }
  [JsonProperty("content_updated_on", Required = Required.AllowNull)]
  public DateTimeOffset? ContentUpdatedOn { get; set; }
  [JsonProperty("voters", Required = Required.Always)]
  public object[] Voters { get; set; }
  [JsonProperty("title", Required = Required.Always)]
  public string Title { get; set; }
  [JsonProperty("reporter", Required = Required.AllowNull)]
  public User Reporter { get; set; }
  [JsonProperty("component", Required = Required.AllowNull)]
  public string Component { get; set; }
  [JsonProperty("watchers", Required = Required.Always)]
  public User[] Watchers { get; set; }
  [JsonProperty("content", Required = Required.Always)]
  public string Content { get; set; }
  [JsonProperty("assignee", Required = Required.AllowNull)]
  public User Assignee { get; set; }
  [JsonProperty("created_on", Required = Required.Always)]
  public DateTimeOffset CreatedOn { get; set; }
  [JsonProperty("version", Required = Required.AllowNull)]
  public object Version { get; set; }
  [JsonProperty("edited_on", Required = Required.AllowNull)]
  public object EditedOn { get; set; }
  [JsonProperty("milestone", Required = Required.AllowNull)]
  public object Milestone { get; set; }
  [JsonProperty("updated_on", Required = Required.Always)]
  public DateTimeOffset UpdatedOn { get; set; }
  [JsonProperty("id", Required = Required.Always)]
  public long Id { get; set; }
}

public partial class Log : IIssueAction
{
  [JsonProperty("comment", Required = Required.Always)]
  public long Comment { get; set; }
  [JsonProperty("changed_to", Required = Required.Always)]
  public string ChangedTo { get; set; }
  [JsonProperty("field", Required = Required.Always)]
  public string Field { get; set; }
  [JsonProperty("created_on", Required = Required.Always)]
  public DateTimeOffset CreatedOn { get; set; }
  [JsonProperty("user", Required = Required.Always)]
  public User User { get; set; }
  [JsonProperty("issue", Required = Required.Always)]
  public long Issue { get; set; }
  [JsonProperty("changed_from", Required = Required.Always)]
  public string ChangedFrom { get; set; }
}

public partial class Meta
{
  [JsonProperty("default_milestone", Required = Required.AllowNull)]
  public object DefaultMilestone { get; set; }
  [JsonProperty("default_assignee", Required = Required.AllowNull)]
  public object DefaultAssignee { get; set; }
  [JsonProperty("default_kind", Required = Required.Always)]
  public string DefaultKind { get; set; }
  [JsonProperty("default_component", Required = Required.AllowNull)]
  public object DefaultComponent { get; set; }
  [JsonProperty("default_version", Required = Required.AllowNull)]
  public object DefaultVersion { get; set; }
}