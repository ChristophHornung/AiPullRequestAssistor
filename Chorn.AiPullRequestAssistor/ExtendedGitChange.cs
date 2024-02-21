using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace Chorn.AiPullRequestAssistor;

public class ExtendedGitChange : GitChange
{
	/// <summary>Content of the item before the change.</summary>
	public ItemContent OldContent { get; set; }
}