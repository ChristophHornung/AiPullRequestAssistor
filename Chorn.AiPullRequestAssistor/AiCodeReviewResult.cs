namespace Chorn.AiPullRequestAssistor;

internal record AiCodeReviewResult
{
	public AiCodeReviewResult(List<AiCodeReviewComment> reviews)
	{
		this.Reviews = reviews;
	}

	public List<AiCodeReviewComment> Reviews { get; }
}