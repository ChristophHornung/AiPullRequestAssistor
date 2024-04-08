using System.CommandLine;
using OpenAI.ObjectModels;

namespace Chorn.AiPullRequestAssistor;

internal static class AiAssistorCommands
{
	static AiAssistorCommands()
	{
		OrgOption = new Option<string>(
			name: "--organization",
			description: "The organization on azure devops.");
		OrgOption.IsRequired = true;
		OrgOption.AddAlias("-o");

		ProjOption = new Option<string>(
			name: "--project",
			description: "The project in the azure devops organization.");
		ProjOption.IsRequired = true;
		ProjOption.AddAlias("-p");

		RepoOption = new Option<string>(
			name: "--repository",
			description: "The repository in the azure devops organization.");
		RepoOption.IsRequired = true;
		RepoOption.AddAlias("-r");

		OpenAiTokenOption = new Option<string>(
			name: "--openAiToken",
			description: "The access token for openAI.");
		OpenAiTokenOption.IsRequired = true;
		OpenAiTokenOption.AddAlias("-t");

		InitialPromptOption = new Option<string>(
			name: "--initialPrompt",
			description:
			"The initial prompt to start the AI conversation with, the initial prompt is followed by all file changes in unidiff format. Uses a default prompt otherwise.");
		InitialPromptOption.IsRequired = false;

		AzureSettingsOption = new Option<string>(
			name: "--azure",
			description:
			"Uses the azure endpoints instead of the OpenAI api. Requires a connection string in the form of <resourceName>.<deploymentId>");
		AzureSettingsOption.IsRequired = false;

		MaxTotalTokenOption = new Option<int>(
			name: "--maxTotalToken",
			description:
			"The maximum total token count to use for one PR comment. " +
			"Once the limit is reached no additional requests are made for one PR - this means the total token count can be slightly larger than this value. " +
			"If the limit is reached by just counting the input no request will be made. " +
			"Set to 0 to allow arbitrarily large requests - BEWARE though this might incur a very high cost for very large PRs.");
		MaxTotalTokenOption.SetDefaultValue(100_000);

		OpenAiModelOption = new Option<Models.Model>(name: "--model",
			description: "The model to use for the AI request.");
		OpenAiModelOption.AddAlias("-m");
		OpenAiModelOption.FromAmong(Models.Model.Gpt_3_5_Turbo.ToString(), Models.Model.Gpt_3_5_Turbo_16k.ToString(),
			Models.Model.Gpt_4.ToString(),
			Models.Model.Gpt_4_32k.ToString());
		OpenAiModelOption.SetDefaultValue(Models.Model.Gpt_3_5_Turbo);

		StrategyOption = new Option<FileRequestStrategy>(name: "--strategy",
			description: "The request strategy.");
		StrategyOption.AddAlias("-s");
		StrategyOption.FromAmong(FileRequestStrategy.FillContextWithFiles.ToString(),
			FileRequestStrategy.SingleRequestForFile.ToString());
		StrategyOption.SetDefaultValue(FileRequestStrategy.SingleRequestForFileDiffOnly);

		PrIdArgument = new Argument<int>(name: "pull-request-id",
			description: "The id of the pull request to comment on.");

		AddCommentCommand = new Command("add-comment", "Adds the AI comment to a pull request.");

		AddCommentDevOpsCommand = new Command("add-comment-devops",
			"Adds the AI comment to a pull request from a devops pipeline.");

		RootCommand = new RootCommand("Automatic AI commenter on a PR.");

		AddCommentCommand.AddOption(OrgOption);
		AddCommentCommand.AddOption(ProjOption);
		AddCommentCommand.AddOption(RepoOption);
		AddCommentCommand.AddOption(OpenAiTokenOption);
		AddCommentCommand.AddOption(OpenAiModelOption);
		AddCommentCommand.AddOption(StrategyOption);
		AddCommentCommand.AddOption(MaxTotalTokenOption);
		AddCommentCommand.AddOption(AzureSettingsOption);
		AddCommentCommand.AddOption(InitialPromptOption);
		AddCommentCommand.AddArgument(PrIdArgument);

		AddCommentDevOpsCommand.AddOption(OpenAiTokenOption);
		AddCommentDevOpsCommand.AddOption(OpenAiModelOption);
		AddCommentDevOpsCommand.AddOption(StrategyOption);
		AddCommentDevOpsCommand.AddOption(MaxTotalTokenOption);
		AddCommentDevOpsCommand.AddOption(InitialPromptOption);
		AddCommentDevOpsCommand.AddOption(AzureSettingsOption);

		RootCommand.AddCommand(AddCommentCommand);
		RootCommand.AddCommand(AddCommentDevOpsCommand);

		AddCommentCommand.SetHandler(AiCommenter.AddAiComment);
		AddCommentDevOpsCommand.SetHandler(AiCommenter.AddAiComment);
	}

	public static Option<FileRequestStrategy> StrategyOption { get; }
	public static Option<string> OrgOption { get; }
	public static Option<string> ProjOption { get; }
	public static Option<string> RepoOption { get; }
	public static Option<string> OpenAiTokenOption { get; }
	public static Option<string> AzureSettingsOption { get; }
	public static Option<string> InitialPromptOption { get; }
	public static Option<Models.Model> OpenAiModelOption { get; }
	public static Option<int> MaxTotalTokenOption { get; }
	public static Argument<int> PrIdArgument { get; }
	public static Command AddCommentCommand { get; }
	public static Command AddCommentDevOpsCommand { get; }
	public static RootCommand RootCommand { get; }
}