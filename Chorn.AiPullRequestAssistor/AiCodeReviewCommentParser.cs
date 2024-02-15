using System.Text.Json;

namespace Chorn.AiPullRequestAssistor;

internal static class AiCodeReviewCommentParser
{
	public static List<AiCodeReviewComment> Parse(string? comment)
	{
		if (string.IsNullOrEmpty(comment))
		{
			return [];
		}

		JsonSerializerOptions options = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			PropertyNameCaseInsensitive = true,
		};

		// Let's try to parse the comment, first let's see if the whole comment is in json format
		try
		{
			AiCodeReviewResult? commentObject = JsonSerializer.Deserialize<AiCodeReviewResult>(comment, options);
			return commentObject?.Reviews ?? [];
		}
		catch (Exception)
		{
			// The comment was not JSON, but it might contain JSON somewhere, lets search for it
			int jsonStart = comment.IndexOf('{');
			int jsonEnd = comment.LastIndexOf('}');
			if (jsonStart != -1 && jsonEnd != -1)
			{
				try
				{
					// JSON found, let's try to parse it until the last '}' character
					string json = comment.Substring(jsonStart, jsonEnd - jsonStart + 1);
					AiCodeReviewResult? commentObject = JsonSerializer.Deserialize<AiCodeReviewResult>(json, options);
					return commentObject?.Reviews ?? [];
				}
				catch (Exception)
				{
					// We ignore any exceptions and just return the comment as a suggestion
				}
			}

			// No JSON found, let's just return the comment as a suggestion
			return [new AiCodeReviewComment(string.Empty, [comment])];
		}
	}
}