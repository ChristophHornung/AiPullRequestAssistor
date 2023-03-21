using System.Text;
using Microsoft.TeamFoundation.SourceControl.WebApi;

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

	public void AddToMessage(StringBuilder chatMessageBuilder)
	{
		if (this.ChangeType == VersionControlChangeType.Add)
		{
			chatMessageBuilder.AppendLine($"The following code was added:");
		}
		else
		{
			chatMessageBuilder.AppendLine($"The following code was edited:");
		}

		chatMessageBuilder.AppendLine(this.Path);

		chatMessageBuilder.AppendLine(this.Content);
	}
}