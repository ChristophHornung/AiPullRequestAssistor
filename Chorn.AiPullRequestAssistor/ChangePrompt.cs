using System.Text;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using OpenAI.ObjectModels.RequestModels;

namespace Chorn.AiPullRequestAssistor;

public class ChangePrompt
{
	public ChangePrompt(VersionControlChangeType changeType, string path, string content)
	{
		this.ChangeType = changeType;
		this.Path = path;
		this.Content = content;
	}

	public VersionControlChangeType ChangeType { get; }
	public string Path { get; }
	public string Content { get; }

	public void AddToMessage(List<ChatMessage> chatMessages)
	{
		StringBuilder chatMessageBuilder = new ();
		if (this.ChangeType == VersionControlChangeType.Add)
		{
			chatMessageBuilder.AppendLine($"The following code was added:");
		}
		else
		{
			chatMessageBuilder.AppendLine($"The following code was changed, this is in unidiff format (added lines start with +, removed lines with -)");
		}

		chatMessageBuilder.AppendLine(this.Path);

		chatMessageBuilder.AppendLine(this.Content);

		chatMessages.Add(ChatMessage.FromUser(chatMessageBuilder.ToString()));
	}
}