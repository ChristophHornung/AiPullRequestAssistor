﻿using System.CommandLine.Invocation;
using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using OpenAI.GPT3;
using OpenAI.GPT3.Managers;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;
using OpenAI.GPT3.ObjectModels.ResponseModels;

namespace Chorn.AiPullRequestAssistor;

internal class AiCommenter
{
	public static async Task AddAiComment(InvocationContext arg)
	{
		CancellationToken cancellationToken = arg.GetCancellationToken();
		string projectName;
		string repositoryName;
		string openAiToken = arg.ParseResult.GetValueForOption(AiAssistorCommands.OpenAiTokenOption)!;
		string? initialPrompt = arg.ParseResult.GetValueForOption(AiAssistorCommands.InitialPromptOption);
		int pullRequestId;

		VssConnection connection;
		if (arg.ParseResult.CommandResult.Command == AiAssistorCommands.AddCommentCommand)
		{
			string organizationName = arg.ParseResult.GetValueForOption(AiAssistorCommands.OrgOption)!;
			projectName = arg.ParseResult.GetValueForOption(AiAssistorCommands.ProjOption)!;
			repositoryName = arg.ParseResult.GetValueForOption(AiAssistorCommands.RepoOption)!;
			pullRequestId = arg.ParseResult.GetValueForArgument(AiAssistorCommands.PrIdArgument);

			connection = VssConnectionHelper.GetConnection(organizationName);
		}
		else if (arg.ParseResult.CommandResult.Command == AiAssistorCommands.AddCommentDevOpsCommand)
		{
			(connection, projectName, repositoryName, pullRequestId) = VssConnectionHelper.GetConnectionDevops();
		}
		else
		{
			throw new Exception("Invalid command.");
		}

		Console.WriteLine("Retrieving PR details.");

		// Create the git client.
		GitHttpClient gitClient = connection.GetClient<GitHttpClient>();

		List<ChangePrompt> changeInputs =
			await GetChangeInputs(gitClient, projectName, repositoryName, pullRequestId, cancellationToken);

		var openAiService = new OpenAIService(new OpenAiOptions()
		{
			ApiKey = openAiToken
		});

		Models.Model model = arg.ParseResult.GetValueForOption(AiAssistorCommands.OpenAiModelOption);
		StringBuilder chatMessageBuilder = new();

		BuildInitialInstruction(chatMessageBuilder, initialPrompt);

		int maxSingleRequestTokenCount = model is Models.Model.ChatGpt3_5Turbo or Models.Model.Gpt_4 ? 3_500 : 30_000;
		int maxTotalRequestTokenCount = arg.ParseResult.GetValueForOption(AiAssistorCommands.MaxTotalTokenOption);
		if (maxTotalRequestTokenCount > 0 && changeInputs.Sum(i => i.Content.Length) > maxTotalRequestTokenCount * 4)
		{
			Console.WriteLine("PR is too large to comment on.");
			return;
		}

		int tokenCount = 0;
		double costCent = 0;

		StringBuilder response = new();

		Console.WriteLine("Requesting AI comments.");
		bool allTokensSpend = false;
		foreach (ChangePrompt input in changeInputs.Where(c => c.Content.Length / 4 < maxSingleRequestTokenCount))
		{
			if ((input.Content.Length + chatMessageBuilder.Length) / 4 > maxSingleRequestTokenCount && !allTokensSpend)
			{
				(tokenCount, costCent) =
					await AskAi(chatMessageBuilder, model, openAiService, response, tokenCount, costCent);
				if (maxTotalRequestTokenCount > 0 && tokenCount >= maxTotalRequestTokenCount)
				{
					// We already expended all tokens, we can't ask anymore.
					allTokensSpend = true;
				}

				chatMessageBuilder = new StringBuilder();
				BuildInitialInstruction(chatMessageBuilder, initialPrompt);
			}

			input.AddToMessage(chatMessageBuilder);
		}

		// The last message that may be partially constructed.
		if (!allTokensSpend)
		{
			(tokenCount, costCent) = await AskAi(chatMessageBuilder, model, openAiService, response, tokenCount,
				costCent);
		}

		if (allTokensSpend)
		{
			Console.WriteLine("All tokens spend - some parts of the PR were skipped.");
		}

		await CreateAiCommentInDevOps(model, tokenCount, costCent, response, gitClient, projectName, repositoryName,
			pullRequestId, cancellationToken);
	}

	private static async Task CreateAiCommentInDevOps(Models.Model model, int tokenCount, double costCent,
		StringBuilder response,
		GitHttpClient gitClient, string projectName, string repositoryName, int pullRequestId,
		CancellationToken cancellationToken)
	{
		StringBuilder commentBuilder = new();
		commentBuilder.AppendLine($"## AI Suggestions ({model} - {tokenCount} token {costCent:F1} cent)");
		commentBuilder.AppendLine(response.ToString());

		Comment comment = new()
		{
			CommentType = CommentType.Text,
			Content = commentBuilder.ToString()
		};

		List<GitPullRequestCommentThread>? threads = await gitClient.GetThreadsAsync(projectName,
			repositoryId: repositoryName, pullRequestId: pullRequestId, cancellationToken: cancellationToken);

		GitPullRequestCommentThread? aiCommentThread =
			threads.FirstOrDefault(t => t.Properties?.Any(p => p.Key == "ChatGtpConversation") == true);
		if (aiCommentThread != null)
		{
			comment.ParentCommentId = aiCommentThread.Comments.First().Id;
			await gitClient.CreateCommentAsync(comment, projectName, repositoryId: repositoryName,
				pullRequestId: pullRequestId, threadId: aiCommentThread.Id, cancellationToken: cancellationToken);
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
				pullRequestId: pullRequestId, cancellationToken: cancellationToken);
		}
	}

	private static async Task<List<ChangePrompt>> GetChangeInputs(GitHttpClient gitClient, string projectName,
		string repositoryName,
		int pullRequestId, CancellationToken cancellationToken)
	{
		// Get the pull request details
		GitPullRequest pullRequest =
			await gitClient.GetPullRequestAsync(projectName, repositoryName, pullRequestId: pullRequestId,
				includeCommits: true, cancellationToken: cancellationToken);

		GitCommitRef pullRequestCommit = pullRequest.LastMergeCommit;

		Console.WriteLine("Retrieving commit details.");
		GitCommit commit = await gitClient.GetCommitAsync(projectName, pullRequestCommit.CommitId, repositoryName,
			cancellationToken: cancellationToken);
		GitCommitChanges commitChanges =
			await gitClient.GetChangesAsync(projectName, commit.CommitId, repositoryName,
				cancellationToken: cancellationToken);

		List<ChangePrompt> changeInputs =
			await GetFileChangeInput(commitChanges, gitClient, projectName, repositoryName, pullRequest,
				cancellationToken);
		return changeInputs;
	}

	private static void BuildInitialInstruction(StringBuilder chatMessageBuilder, string? initialPrompt)
	{
		if (string.IsNullOrWhiteSpace(initialPrompt))
		{
			chatMessageBuilder.AppendLine(
				"""
				Act as a code reviewer and provide constructive feedback on the following changes.
				Give a minimum of one and a maximum of five suggestions per file most relevant first.
				Use markdown as the output if possible. Output should be formatted like:
				## [filename]
				- [suggestion 1 text]
				- [suggestion 2 text]
				...
			""");

			chatMessageBuilder.AppendLine(
				"The following files are updates in the pull request and are in unidiff format:");
		}
		else
		{
			chatMessageBuilder.AppendLine(initialPrompt);
		}
	}

	private static async Task<(int tokenCount, double costCent)> AskAi(StringBuilder chatMessageBuilder,
		Models.Model model, OpenAIService openAiService, StringBuilder response, int tokenCount, double costCent)
	{
		// Max size reached, we ask ChatGTP
		string prompt = chatMessageBuilder.Replace("\r\n", "\n").ToString();
		
		if (model is Models.Model.ChatGpt3_5Turbo or Models.Model.Gpt_4 or Models.Model.Gpt_4_32k)
		{
			double tokenPromptCostInCent = model switch
			{
				Models.Model.ChatGpt3_5Turbo => 0.2,
				Models.Model.Gpt_4 => 3,
				Models.Model.Gpt_4_32k => 6,
				_ => throw new ArgumentOutOfRangeException(nameof(model), model, null)
			};

			double tokenCompletionCostInCent = model switch
			{
				Models.Model.ChatGpt3_5Turbo => 0.2,
				Models.Model.Gpt_4 => 6,
				Models.Model.Gpt_4_32k => 12,
				_ => throw new ArgumentOutOfRangeException(nameof(model), model, null)
			};

			int count = 0;
			bool success = false;
			while (!success)
			{
				try
				{
					ChatCompletionCreateResponse completionResult = await openAiService.ChatCompletion.CreateCompletion(
						new ChatCompletionCreateRequest
						{
							Messages = new List<ChatMessage>
							{
								ChatMessage.FromUser(prompt)
							},

							Model = model.EnumToString()
						});

					if (completionResult.Successful)
					{
						Console.WriteLine(completionResult.Choices.First().Message.Content);
						response.AppendLine();
						response.AppendLine(completionResult.Choices.First().Message.Content);
						tokenCount += completionResult.Usage.TotalTokens;
						costCent += tokenPromptCostInCent * completionResult.Usage.PromptTokens / 1000.0;
						costCent += tokenCompletionCostInCent * (completionResult.Usage.CompletionTokens ?? 0) / 1000.0;

						success = true;
					}
				}
				catch (TaskCanceledException)
				{
					count++;
					if (count > 5)
					{
						throw;
					}
				}
			}
		}
		else
		{
			throw new Exception("Invalid model");
		}

		return (tokenCount, costCent);
	}

	private static async Task<List<ChangePrompt>> GetFileChangeInput(GitCommitChanges commitChanges,
		GitHttpClient gitClient,
		string projectName, string repositoryName, GitPullRequest pullRequest, CancellationToken cancellationToken)
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
				}, cancellationToken: cancellationToken);

			using StreamReader sr = new StreamReader(afterMergeFileContents);
			string afterMergeContent = await sr.ReadToEndAsync();

			if (afterMergeContent.Contains('\0'))
			{
				// We assume the file is binary and skip it.
				continue;
			}

			if (commitChange.ChangeType == VersionControlChangeType.Add)
			{
				StringBuilder addBuilder = new();
				addBuilder.AppendLine("```");
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
					}, cancellationToken: cancellationToken);

				using StreamReader sr1 = new StreamReader(beforeMergeFileContents);

				var differ = new InlineDiffBuilder(new Differ());
				var diff = differ.BuildDiffModel(await sr1.ReadToEndAsync(), afterMergeContent, true);

				StringBuilder diffBuilder = new();
				diffBuilder.AppendLine("```diff");
				diffBuilder.AppendLine("--- " + commitChange.Item.Path);
				diffBuilder.AppendLine("+++ " + commitChange.Item.Path);

				foreach (DiffPiece? line in diff.Lines)
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