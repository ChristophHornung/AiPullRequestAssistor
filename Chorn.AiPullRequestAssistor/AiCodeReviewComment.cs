using System.Text.Json.Serialization;

namespace Chorn.AiPullRequestAssistor;

internal record AiCodeReviewComment
{
	[JsonConstructor]
	internal AiCodeReviewComment(string filename, List<string> suggestions)
	{
		this.Filename = filename;
		this.Suggestions = suggestions;
	}

	public string Filename { get; }

	public List<string> Suggestions { get; }
}