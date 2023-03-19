using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.WebApi;
using OpenAI.GPT3;
using OpenAI.GPT3.Managers;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;
using OpenAI.GPT3.ObjectModels.ResponseModels;

namespace Chorn.AiPullRequestAssistor;

internal class Program
{
	private static readonly Option<string> orgOption;

	private static readonly Option<string> projOption;

	private static readonly Option<string> repoOption;

	private static readonly Option<string> openAiTokenOption;

	private static readonly Argument<int> prIdArgument;

	static Program()
	{
		orgOption = new Option<string>(
			name: "--organization",
			description: "The organization on azure devops.");
		orgOption.IsRequired = true;
		orgOption.AddAlias("-o");

		projOption = new Option<string>(
			name: "--project",
			description: "The project in the azure devops organization.");
		projOption.IsRequired = true;
		projOption.AddAlias("-p");

		repoOption = new Option<string>(
			name: "--repository",
			description: "The repository in the azure devops organization.");
		repoOption.IsRequired = true;
		repoOption.AddAlias("-r");

		openAiTokenOption = new Option<string>(
			name: "--openAiToken",
			description: "The access token for openAI.");
		openAiTokenOption.IsRequired = true;
		openAiTokenOption.AddAlias("-t");

		prIdArgument = new Argument<int>(name: "pull-request-id",
			description: "The id of the pull request to comment on.");
	}

	private static async Task Main(string[] args)
	{
		RootCommand rootCommand = new RootCommand("Automatic AI commenter on a PR.");

		rootCommand.AddOption(orgOption);
		rootCommand.AddOption(projOption);
		rootCommand.AddOption(repoOption);
		rootCommand.AddOption(openAiTokenOption);
		rootCommand.AddArgument(prIdArgument);

		rootCommand.SetHandler(AddAiComment);
		await rootCommand.InvokeAsync(args);
	}

	private static async Task AddAiComment(InvocationContext arg)
	{
		string organizationName = arg.ParseResult.GetValueForOption(orgOption)!;
		string projectName = arg.ParseResult.GetValueForOption(projOption)!;
		string repositoryName = arg.ParseResult.GetValueForOption(repoOption)!;
		string openAiToken = arg.ParseResult.GetValueForOption(openAiTokenOption)!;
		int pullRequestId = arg.ParseResult.GetValueForArgument(prIdArgument);

		VssClientCredentials credentials = new();

		Uri devopsUrl = new Uri($"https://dev.azure.com/{organizationName}");
		VssConnection connection = new VssConnection(devopsUrl, credentials);

		// Get the pull request details
		GitHttpClient gitClient = connection.GetClient<GitHttpClient>();
		GitPullRequest pullRequest =
			await gitClient.GetPullRequestAsync(projectName, repositoryName, pullRequestId: pullRequestId,
				includeCommits: true);

		GitCommitRef pullRequestCommit = pullRequest.LastMergeCommit;
		GitCommit commit = await gitClient.GetCommitAsync(projectName, pullRequestCommit.CommitId, repositoryName);
		GitCommitChanges commitChanges =
			await gitClient.GetChangesAsync(projectName, commit.CommitId, repositoryName);


		List<ChangePrompt> changeInputs =
			await GetFileChangeInput(commitChanges, gitClient, projectName, repositoryName, pullRequest);

		var openAiService = new OpenAIService(new OpenAiOptions()
		{
			ApiKey = openAiToken
		});

		string model = Models.ChatGpt3_5Turbo;
		StringBuilder chatMessageBuilder = new();

		BuildInitialInstruction(chatMessageBuilder);

		int maxToken = 3500;
		int tokenCount = 0;
		double costCent = 0;

		StringBuilder response = new();

		bool initial = true;

		foreach (var input in changeInputs.Where(c => c.Content.Length / 4 < maxToken))
		{
			if ((input.Content.Length + chatMessageBuilder.Length) / 4 > maxToken)
			{
				(tokenCount, costCent) =
					await AskAi(chatMessageBuilder, model, openAiService, response, tokenCount, costCent);
				chatMessageBuilder = new StringBuilder();
				BuildInitialInstruction(chatMessageBuilder);
			}

			input.AddToMessage(chatMessageBuilder);
		}

		(tokenCount, costCent) = await AskAi(chatMessageBuilder, model, openAiService, response, tokenCount, costCent);

		StringBuilder commentBuilder = new();
		commentBuilder.AppendLine($"## AI Suggestions ({model} - {tokenCount} token {costCent:F1} cent)");
		commentBuilder.AppendLine(response.ToString());

		Comment comment = new()
		{
			CommentType = CommentType.Text,
			Content = commentBuilder.ToString()
		};

		List<GitPullRequestCommentThread>? threads = await gitClient.GetThreadsAsync(projectName,
			repositoryId: repositoryName, pullRequestId: pullRequestId);

		GitPullRequestCommentThread? aiCommentThread =
			threads.FirstOrDefault(t => t.Properties?.Any(p => p.Key == "ChatGtpConversation") == true);
		if (aiCommentThread != null)
		{
			comment.ParentCommentId = aiCommentThread.Comments.First().Id;
			await gitClient.CreateCommentAsync(comment, projectName, repositoryId: repositoryName,
				pullRequestId: pullRequestId, threadId: aiCommentThread.Id);
		}
		else
		{
			GitPullRequestCommentThread commentThread = new GitPullRequestCommentThread
			{
				Comments = new List<Comment>
				{
					comment
				},
				Status = CommentThreadStatus.Active,
				Properties = new PropertiesCollection { { "ChatGtpConversation", "true" } }
			};

			await gitClient.CreateThreadAsync(commentThread, projectName, repositoryId: repositoryName,
				pullRequestId: pullRequestId);
		}
	}

	private static void BuildInitialInstruction(StringBuilder chatMessageBuilder)
	{
		chatMessageBuilder.AppendLine(
			"""
				Act as a code reviewer and provide constructive feedback on the following changes.
				Give a minimum of one and a maximum of five suggestions per file most relevant first.
				Use markdown as the output if possible.
			""");

		chatMessageBuilder.AppendLine($"The following files are updates in a pull request:");
	}

	private static async Task<(int tokenCount, double costCent)> AskAi(StringBuilder chatMessageBuilder, string model,
		OpenAIService openAiService,
		StringBuilder response, int tokenCount, double costCent)
	{
		// Max size reached, we ask ChatGTP
		string prompt = chatMessageBuilder.Replace("\r\n", "\n").ToString();
		if (model == Models.ChatGpt3_5Turbo)
		{
			ChatCompletionCreateResponse completionResult = await openAiService.ChatCompletion.CreateCompletion(
				new ChatCompletionCreateRequest
				{
					Messages = new List<ChatMessage>
					{
						ChatMessage.FromUser(prompt)
					},

					Model = model
				});
			if (completionResult.Successful)
			{
				Console.WriteLine(completionResult.Choices.First().Message.Content);
			}

			response.AppendLine(completionResult.Choices.First().Message.Content);
			tokenCount += completionResult.Usage.TotalTokens;
			costCent += 0.2 * completionResult.Usage.TotalTokens / 1000.0;
		}
		else if (model == Models.CodeDavinciV2)
		{
			var completionResult = await openAiService.CreateCompletion(new CompletionCreateRequest()
			{
				Model = model,
				Prompt = prompt
			});

			if (completionResult.Successful)
			{
				Console.WriteLine(completionResult.Choices.First().Text);
			}

			response.AppendLine(completionResult.Choices.First().Text);
			tokenCount += completionResult.Usage.TotalTokens;
			costCent += 0.2 * completionResult.Usage.TotalTokens / 1000.0;
		}
		else
		{
			throw new Exception("Invalid model");
		}

		return (tokenCount, costCent);
	}

	private static async Task<List<ChangePrompt>> GetFileChangeInput(GitCommitChanges commitChanges,
		GitHttpClient gitClient,
		string projectName, string repositoryName, GitPullRequest pullRequest)
	{
		List<ChangePrompt> inputs = new();
		foreach (GitChange commitChange in commitChanges.Changes.Where(c =>
			         !c.Item.IsFolder && c.ChangeType is VersionControlChangeType.Edit or VersionControlChangeType.Add))
		{
			using Stream afterMergeFileContents = await gitClient.GetItemTextAsync(projectName, repositoryName,
				path: commitChange.Item.Path, versionDescriptor: new GitVersionDescriptor
				{
					VersionType = GitVersionType.Commit,
					Version = pullRequest.LastMergeCommit.CommitId,
				});

			using StreamReader sr = new StreamReader(afterMergeFileContents);
			string afterMergeContent = await sr.ReadToEndAsync();

			if (commitChange.ChangeType == VersionControlChangeType.Add)
			{
				StringBuilder addBuilder = new();
				addBuilder.AppendLine("```diff");
				addBuilder.AppendLine(afterMergeContent);
				addBuilder.AppendLine("```");
				inputs.Add(new ChangePrompt(VersionControlChangeType.Add,
					commitChange.Item.Path,
					afterMergeContent
				));
			}
			else if (commitChange.ChangeType == VersionControlChangeType.Edit)
			{
				using Stream beforeMergeFileContents = await gitClient.GetItemTextAsync(projectName, repositoryName,
					path: commitChange.Item.Path, versionDescriptor: new GitVersionDescriptor
					{
						VersionType = GitVersionType.Commit,
						Version = pullRequest.LastMergeTargetCommit.CommitId,
					});
				using StreamReader sr1 = new StreamReader(beforeMergeFileContents);

				var differ = new InlineDiffBuilder(new Differ());
				var diff = differ.BuildDiffModel(await sr1.ReadToEndAsync(), afterMergeContent, true);

				StringBuilder diffBuilder = new();
				diffBuilder.AppendLine("```diff");
				diffBuilder.AppendLine("--- " + commitChange.Item.Path);
				diffBuilder.AppendLine("+++ " + commitChange.Item.Path);

				foreach (var line in diff.Lines)
				{
					switch (line.Type)
					{
						case ChangeType.Inserted:
							diffBuilder.AppendLine("+ " + line.Text);
							break;
						case ChangeType.Deleted:
							diffBuilder.AppendLine("- " + line.Text);
							break;
						default:
							diffBuilder.AppendLine("  " + line.Text);
							break;
					}
				}

				diffBuilder.AppendLine("```");

				inputs.Add(new ChangePrompt(
					VersionControlChangeType.Edit,
					commitChange.Item.Path,
					diffBuilder.ToString()
				));
			}
		}

		return inputs;
	}
}