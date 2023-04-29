using System.CommandLine;
using OpenAI.GPT3.ObjectModels;

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
			description: "The initial prompt to start the AI conversation with, the initial prompt is followed by all file changes in unidiff format. Uses a default prompt otherwise.");
		InitialPromptOption.IsRequired = false;

		MaxTotalTokenOption = new Option<int>(
			name: "--maxTotalToken",
			description:
			"The maximum total token count to user for one PR comment. If the PR is larger than this value no AI request will be done.");

		OpenAiModelOption = new Option<Models.Model>(name: "--model",
			description: "The model to use for the AI request.");

		OpenAiModelOption.AddAlias("-m");
		OpenAiModelOption.FromAmong(Models.Model.ChatGpt3_5Turbo.ToString(), Models.Model.Gpt_4.ToString(),
			Models.Model.Gpt_4_32k.ToString());
		OpenAiModelOption.SetDefaultValue(Models.Model.ChatGpt3_5Turbo);

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
		AddCommentCommand.AddOption(MaxTotalTokenOption);
		AddCommentCommand.AddOption(InitialPromptOption);
		AddCommentCommand.AddArgument(PrIdArgument);

		AddCommentDevOpsCommand.AddOption(OpenAiTokenOption);
		AddCommentDevOpsCommand.AddOption(OpenAiModelOption);
		AddCommentDevOpsCommand.AddOption(MaxTotalTokenOption);
		AddCommentDevOpsCommand.AddOption(InitialPromptOption);

		RootCommand.AddCommand(AddCommentCommand);
		RootCommand.AddCommand(AddCommentDevOpsCommand);

		AddCommentCommand.SetHandler(AiCommenter.AddAiComment);
		AddCommentDevOpsCommand.SetHandler(AiCommenter.AddAiComment);
	}

	public static Option<string> OrgOption { get; }
	public static Option<string> ProjOption { get; }
	public static Option<string> RepoOption { get; }
	public static Option<string> OpenAiTokenOption { get; }
	public static Option<string> InitialPromptOption { get; }
	public static Option<Models.Model> OpenAiModelOption { get; }
	public static Option<int> MaxTotalTokenOption { get; }
	public static Argument<int> PrIdArgument { get; }
	public static Command AddCommentCommand { get; }
	public static Command AddCommentDevOpsCommand { get; }
	public static RootCommand RootCommand { get; }
}