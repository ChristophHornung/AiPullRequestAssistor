using System.CommandLine.Invocation;
using System.Text;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using OpenAI;
using OpenAI.Managers;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels.ResponseModels;

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

		OpenAiOptions options = new()
		{
			ApiKey = openAiToken,
		};

		string? azureConnection = arg.ParseResult.GetValueForOption(AiAssistorCommands.AzureSettingsOption);
		if (azureConnection != null)
		{
			string[] parts = azureConnection.Split('.');
			options.ProviderType = ProviderType.Azure;
			options.ResourceName = parts[0];
			options.DeploymentId = parts[1];
		}

		OpenAIService openAiService = new(options);


		Models.Model model = arg.ParseResult.GetValueForOption(AiAssistorCommands.OpenAiModelOption);
		List<ChatMessage> chatMessages = [];

		BuildInitialInstruction(chatMessages, initialPrompt);

		int maxSingleRequestTokenCount = model switch
		{
			Models.Model.Gpt_3_5_Turbo => 3_500,
			Models.Model.Gpt_3_5_Turbo_16k => 15_500,
			Models.Model.Gpt_4 => 7_500,
			Models.Model.Gpt_4_32k => 31_500,
			_ => 30_000
		};

		List<ChangePrompt> validChangeInputs =
			changeInputs.Where(c => c.Content.Length / 4 < maxSingleRequestTokenCount).ToList();
		List<ChangePrompt> tooLargeChangeInputs =
			changeInputs.Where(c => c.Content.Length / 4 >= maxSingleRequestTokenCount).ToList();
		foreach (ChangePrompt changeInput in tooLargeChangeInputs)
		{
			Console.WriteLine(
				$"File {changeInput.Path} is too large to comment on (estimated {changeInput.Content.Length / 4}, limit is {maxSingleRequestTokenCount}).");
		}

		int maxTotalRequestTokenCount = arg.ParseResult.GetValueForOption(AiAssistorCommands.MaxTotalTokenOption);
		int estimatedTokens = validChangeInputs.Sum(i => i.Content.Length) / 4;
		if (maxTotalRequestTokenCount > 0 && estimatedTokens > maxTotalRequestTokenCount)
		{
			Console.WriteLine(
				$"PR is too large to comment on (estimated {estimatedTokens}, limit is {maxTotalRequestTokenCount}).");
			return;
		}

		int tokenCount = 0;
		double costCent = 0;

		List<AiCodeReviewComment> codeReviewComments = [];
		Console.WriteLine("Requesting AI comments.");
		bool allTokensSpend = false;
		bool hasContent = false;

		if (validChangeInputs.Count == 0)
		{
			Console.WriteLine("No valid changes to comment on.");
			return;
		}

		string prComment =
			await GetPullRequestComment(gitClient, projectName, repositoryName, pullRequestId, cancellationToken);

		foreach (ChangePrompt input in validChangeInputs)
		{
			bool addToExisting =
				input.Content.Length / 4 + chatMessages.Sum(c => c.Content!.Length) / 4 < maxSingleRequestTokenCount &&
				arg.ParseResult.GetValueForOption(AiAssistorCommands.StrategyOption) !=
				FileRequestStrategy.SingleRequestForFile;

			if (!addToExisting && hasContent && !allTokensSpend)
			{
				(tokenCount, costCent) =
					await AskAi(chatMessages, model, openAiService, codeReviewComments, prComment, tokenCount, costCent);
				if (maxTotalRequestTokenCount > 0 && tokenCount >= maxTotalRequestTokenCount)
				{
					// We already expended all tokens, we can't ask anymore.
					allTokensSpend = true;
				}

				chatMessages.Clear();
				BuildInitialInstruction(chatMessages, initialPrompt);
			}

			input.AddToMessage(chatMessages);
			hasContent = true;
		}

		// The last message that may be partially constructed.
		if (!allTokensSpend)
		{
			(tokenCount, costCent) = await AskAi(chatMessages, model, openAiService, codeReviewComments, prComment,
				tokenCount, costCent);
		}

		if (allTokensSpend)
		{
			Console.WriteLine("All tokens spend - some parts of the PR were skipped.");
		}

		await CreateAiCommentInDevOps(model, tokenCount, costCent, codeReviewComments, gitClient, projectName,
			repositoryName,
			pullRequestId, cancellationToken);
	}

	private static async Task CreateAiCommentInDevOps(Models.Model model, int tokenCount, double costCent,
		List<AiCodeReviewComment> codeReviewComments,
		GitHttpClient gitClient, string projectName, string repositoryName, int pullRequestId,
		CancellationToken cancellationToken)
	{
		List<GitPullRequestCommentThread>? threads = await gitClient.GetThreadsAsync(projectName,
			repositoryId: repositoryName, pullRequestId: pullRequestId, cancellationToken: cancellationToken);

		GitPullRequestCommentThread? aiCommentThread =
			threads.FirstOrDefault(t => t.Properties?.Any(p => p.Key == "ChatGtpConversation") == true);
		if (aiCommentThread == null)
		{
			aiCommentThread = new GitPullRequestCommentThread
			{
				Comments =
				[
					new Comment()
					{
						CommentType = CommentType.Text,
						Content =
							$"## AI Suggestions ({model} - {tokenCount} token {costCent:F1} cent)\r\n" +
							"The following comments are generated by an AI model. Please like those that were useful."
					}
				],

				Status = CommentThreadStatus.Active,
				Properties = new PropertiesCollection { { "ChatGtpConversation", "true" } }
			};

			aiCommentThread = await gitClient.CreateThreadAsync(aiCommentThread, projectName,
				repositoryId: repositoryName,
				pullRequestId: pullRequestId, cancellationToken: cancellationToken);
		}
		else
		{
			Comment comment = new()
			{
				CommentType = CommentType.Text,
				Content =
					$"## AI Suggestions ({model} - {tokenCount} token {costCent:F1} cent)\r\n" +
					"The following comments are generated by an AI model. Please like those that were useful."
			};

			comment.ParentCommentId = aiCommentThread.Comments.First().Id;
			await gitClient.CreateCommentAsync(comment, projectName, repositoryId: repositoryName,
				pullRequestId: pullRequestId, threadId: aiCommentThread.Id, cancellationToken: cancellationToken);
		}

		foreach (AiCodeReviewComment codeReviewComment in codeReviewComments)
		{
			Comment comment = new Comment
			{
				CommentType = CommentType.Text,
				Content = ToContentString(codeReviewComment)
			};

			if (comment.Content != null)
			{
				comment.ParentCommentId = aiCommentThread.Comments.First().Id;
				await gitClient.CreateCommentAsync(comment, projectName, repositoryId: repositoryName,
					pullRequestId: pullRequestId, threadId: aiCommentThread.Id, cancellationToken: cancellationToken);
			}
		}
	}

	private static string? ToContentString(AiCodeReviewComment codeReviewComment)
	{
		if (!codeReviewComment.Suggestions.Any())
		{
			return null;
		}

		if (codeReviewComment.Filename == string.Empty)
		{
			return codeReviewComment.Suggestions[0];
		}
		else
		{
			StringBuilder sb = new();
			sb.AppendLine($"## {codeReviewComment.Filename}");
			foreach (string suggestion in codeReviewComment.Suggestions)
			{
				sb.AppendLine($"- {suggestion}");
			}

			return sb.ToString();
		}
	}

	private static async Task<string> GetPullRequestComment(GitHttpClient gitClient, string projectName,
		string repositoryName,
		int pullRequestId, CancellationToken cancellationToken)
	{
		// Get the pull request details
		GitPullRequest pullRequest =
			await gitClient.GetPullRequestAsync(projectName, repositoryName, pullRequestId: pullRequestId,
				includeCommits: true, cancellationToken: cancellationToken);
		return $"{pullRequest.Title}\n{pullRequest.Description}";
	}

	private static async Task<List<ChangePrompt>> GetChangeInputs(GitHttpClient gitClient, string projectName,
		string repositoryName,
		int pullRequestId, CancellationToken cancellationToken)
	{
		// Get the pull request details
		GitPullRequest pullRequest =
			await gitClient.GetPullRequestAsync(projectName, repositoryName, pullRequestId: pullRequestId,
				includeCommits: true, cancellationToken: cancellationToken);
		List<GitPullRequestIteration> iterations =
			await gitClient.GetPullRequestIterationsAsync(projectName, repositoryName, pullRequestId,
				cancellationToken: cancellationToken, includeCommits: true);

		bool reviewLastIterationOnly = pullRequest.SupportsIterations && iterations.Count > 1;
		IEnumerable<ExtendedGitChange> commitChanges;
		if (reviewLastIterationOnly)
		{
			// We only review the last commit of the PR, not the merge commit.
			// Get the iteration with the highest id.
			GitPullRequestIteration lastIteration = iterations.OrderByDescending(i => i.Id).First();
			int iterationId = lastIteration.Id!.Value;

			Console.WriteLine($"Retrieving iteration details for iteration {iterationId}.");
			GitPullRequestIterationChanges? iterationChanges = await gitClient.GetPullRequestIterationChangesAsync(
				projectName, repositoryName,
				pullRequestId,
				iterationId, compareTo: iterationId - 1, cancellationToken: cancellationToken);
			List<ExtendedGitChange> fullChanges = [];
			foreach (GitPullRequestChange iterationChangesChangeEntry in iterationChanges.ChangeEntries)
			{
				ExtendedGitChange change = new()
				{
					ChangeType = iterationChangesChangeEntry.ChangeType,
					Item = iterationChangesChangeEntry.Item,
				};

				if (change.ChangeType is VersionControlChangeType.Add or VersionControlChangeType.Edit)
				{
					Stream contentNew = await gitClient.GetBlobContentAsync(projectName, repositoryName,
						iterationChangesChangeEntry.Item.ObjectId, cancellationToken: cancellationToken);

					using var reader = new StreamReader(contentNew);
					string content = await reader.ReadToEndAsync();

					change.NewContent = new ItemContent
					{
						Content = content
					};
				}

				if (change.ChangeType == VersionControlChangeType.Edit)
				{
					Stream contentOld = await gitClient.GetBlobContentAsync(projectName, repositoryName,
												iterationChangesChangeEntry.Item.OriginalObjectId, cancellationToken: cancellationToken);

					using var reader = new StreamReader(contentOld);
					string content = await reader.ReadToEndAsync();

					change.OldContent = new ItemContent
					{
						Content = content
					};
				}

				fullChanges.Add(change);
			}

			commitChanges = fullChanges;
		}
		else
		{
			// We review the full merge commit.
			Console.WriteLine("Retrieving commit details.");
			GitCommitRef pullRequestCommit = pullRequest.LastMergeCommit;

			GitCommit commit = await gitClient.GetCommitAsync(projectName, pullRequestCommit.CommitId, repositoryName,
				cancellationToken: cancellationToken);

			GitCommitChanges mergeCommitChanges =
				await gitClient.GetChangesAsync(projectName, commit.CommitId, repositoryName,
					cancellationToken: cancellationToken);

			List<ExtendedGitChange> fullChanges = [];
			foreach (GitChange gitChange in mergeCommitChanges.Changes)
			{
				fullChanges.Add(await gitChange.FillContent(gitClient, projectName, repositoryName,
					pullRequest, cancellationToken));
			}

			commitChanges = fullChanges;
		}

		List<ChangePrompt> changeInputs =
			await FileChangePromptBuilder.GetFileChangeInput(commitChanges);
		return changeInputs;
	}

	private static void BuildInitialInstruction(List<ChatMessage> chatMessageBuilder, string? initialPrompt)
	{
		if (string.IsNullOrWhiteSpace(initialPrompt))
		{
			chatMessageBuilder.Add(ChatMessage.FromUser(
				"""
				Act as a code reviewer for a pull-request and provide constructive feedback .
				The process will have two steps, first you will summarize the changes and then you will give your feedback.
				Always ignore code that was not changed in this PR.
				We will start with summarizing the changes once you are ready.
				"""));

			chatMessageBuilder.Add(ChatMessage.FromAssistant(
				"""
				Absolutely, I'm ready to review the pull request. Please provide the details or code changes that you'd like me to summarize and review.
				"""));


//			Act as a code reviewer for a pull-request and provide constructive feedback on the following changes.
//				Ignore code that was not changed in this PR.
//				Give a minimum of one and a maximum of five suggestions per file most relevant first.
//				Use markdown as the output if possible. Output should be formatted like:
//## [fullPathFilename]
//			- [suggestion 1 text]
//			- [suggestion 2 text]
//			...
//			Focus on the changed parts, don't mention line numbers and don't be too nit-picky.

			//chatMessageBuilder.AppendLine(
			//	"The following files are updates in the pull request and are in unidiff format (added lines start with +, removed lines with -)");
		}
		else
		{
			chatMessageBuilder.Add(ChatMessage.FromUser(initialPrompt!));

			chatMessageBuilder.Add(ChatMessage.FromAssistant(
				"""
				Absolutely, I'm ready to review the pull request. Please provide the details or code changes that you'd like me to summarize and review.
				"""));
		}
	}

	private static async Task<(int tokenCount, double costCent)> AskAi(List<ChatMessage> chatMessagesStart,
		Models.Model model, OpenAIService openAiService, List<AiCodeReviewComment> comments, string prComment, int tokenCount,
		double costCent)
	{
		List<ChatMessage> chatMessages = [..chatMessagesStart];

		double tokenPromptCostInCent = GetTokenCosts(model, out double tokenCompletionCostInCent);

		chatMessages.Add(ChatMessage.FromUser(
			"Now please provide a summary of the changes. Ignore unchanged code, it is only provided for context, do not provide a review yet."));

		int count = 0;
		bool success = false;
		while (!success)
		{
			try
			{
				ChatCompletionCreateResponse completionResult = await openAiService.ChatCompletion.CreateCompletion(
					new ChatCompletionCreateRequest
					{
						Messages = chatMessages,
						Model = model.EnumToString()
					});

				if (completionResult.Successful)
				{
					Console.WriteLine(completionResult.Choices.First().Message.Content);
					chatMessages.Add(completionResult.Choices.First().Message);
					tokenCount += completionResult.Usage.TotalTokens;
					costCent += tokenPromptCostInCent * completionResult.Usage.PromptTokens / 1000.0;
					costCent += tokenCompletionCostInCent * (completionResult.Usage.CompletionTokens ?? 0) / 1000.0;

					chatMessages.Add(ChatMessage.FromUser(
						$$"""
						Thank you for your change analysis. The original author provided the following Pull-Request comment:
						```
						{{prComment}}
						```
						Now provide constructive feedback on the changes.
						Ignore code that was not changed in this PR.
						Give a minimum of one and a maximum of three suggestions per file with the most relevant first.
						Output as a JSON in the format
						{
							"reviews": [
								{
									"filename": "fullPathFilename",
									"suggestions": [
										"suggestion 1 text",
										"suggestion 2 text"
									]
								},
								...
							]
						}

						Do not add additional text beyond the JSON.

						Focus on the changed parts, don't mention line numbers and don't be too nit-picky.
						"""));

					completionResult = await openAiService.ChatCompletion.CreateCompletion(
						new ChatCompletionCreateRequest
						{
							Messages = chatMessages,
							Model = model.EnumToString()
						});

					if (completionResult.Successful)
					{
						Console.WriteLine(completionResult.Choices.First().Message.Content);
						chatMessages.Add(completionResult.Choices.First().Message);
						comments.AddRange(
							AiCodeReviewCommentParser.Parse(completionResult.Choices.First().Message.Content));
						tokenCount += completionResult.Usage.TotalTokens;
						costCent += tokenPromptCostInCent * completionResult.Usage.PromptTokens / 1000.0;
						costCent += tokenCompletionCostInCent * (completionResult.Usage.CompletionTokens ?? 0) / 1000.0;

						success = true;
					}
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

		return (tokenCount, costCent);
	}

	private static double GetTokenCosts(Models.Model model, out double tokenCompletionCostInCent)
	{
		// Max size reached, we ask ChatGTP
		if (model is not (Models.Model.Gpt_3_5_Turbo
		    or Models.Model.Gpt_4
		    or Models.Model.Gpt_4_32k
		    or Models.Model.Gpt_3_5_Turbo_16k))
		{
			throw new Exception("Invalid model");
		}

		double tokenPromptCostInCent = model switch
		{
			Models.Model.Gpt_3_5_Turbo => 0.15,
			Models.Model.Gpt_3_5_Turbo_16k => 0.3,
			Models.Model.Gpt_4 => 3,
			Models.Model.Gpt_4_32k => 6,
			_ => throw new ArgumentOutOfRangeException(nameof(model), model, null)
		};

		tokenCompletionCostInCent = model switch
		{
			Models.Model.Gpt_3_5_Turbo => 0.2,
			Models.Model.Gpt_3_5_Turbo_16k => 0.4,
			Models.Model.Gpt_4 => 6,
			Models.Model.Gpt_4_32k => 12,
			_ => throw new ArgumentOutOfRangeException(nameof(model), model, null)
		};
		return tokenPromptCostInCent;
	}
}