using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace Chorn.AiPullRequestAssistor;

internal static class GitChangeExtensions
{
	internal static async Task<ExtendedGitChange> FillContent(this GitChange change, GitHttpClient gitClient,
		string projectName,
		string repositoryName, GitPullRequest pullRequest, CancellationToken cancellationToken)
	{
		ExtendedGitChange extendedChange = new()
		{
			ChangeId = change.ChangeId,
			ChangeType = change.ChangeType,
			Item = change.Item,
			NewContentTemplate = change.NewContentTemplate,
			OriginalPath = change.OriginalPath,
		};

		if (change.Item.IsFolder)
		{
			return extendedChange;
		}

		if (change.ChangeType is VersionControlChangeType.Edit or VersionControlChangeType.Add)
		{
			extendedChange.NewContent = await GetNewContent(gitClient, projectName, repositoryName, pullRequest, change,
				cancellationToken);
		}

		if (change.ChangeType == VersionControlChangeType.Edit)
		{
			extendedChange.OldContent = await GetOldContent(gitClient, projectName, repositoryName, pullRequest, change,
				cancellationToken);
		}

		return extendedChange;
	}

	private static async Task<ItemContent> GetOldContent(GitHttpClient gitClient, string projectName,
		string repositoryName, GitPullRequest pullRequest, GitChange change, CancellationToken cancellationToken)
	{
		using Stream beforeMergeFileContents = await gitClient.GetItemTextAsync(projectName, repositoryName,
			path: change.Item.Path, versionDescriptor: new GitVersionDescriptor
			{
				VersionType = GitVersionType.Commit,
				Version = pullRequest.LastMergeTargetCommit.CommitId,
			}, cancellationToken: cancellationToken);

		using StreamReader sr = new StreamReader(beforeMergeFileContents);
		string beforeMergeContent = await sr.ReadToEndAsync();

		return new ItemContent()
		{
			Content = beforeMergeContent
		};
	}

	private static async Task<ItemContent> GetNewContent(GitHttpClient gitClient, string projectName,
		string repositoryName, GitPullRequest pullRequest, GitChange change, CancellationToken cancellationToken)
	{
		using Stream afterMergeFileContents = await gitClient.GetItemTextAsync(projectName, repositoryName,
			path: change.Item.Path, versionDescriptor: new GitVersionDescriptor
			{
				VersionType = GitVersionType.Commit,
				Version = pullRequest.LastMergeCommit.CommitId,
			}, cancellationToken: cancellationToken);

		using StreamReader sr = new StreamReader(afterMergeFileContents);
		string afterMergeContent = await sr.ReadToEndAsync();

		return new ItemContent()
		{
			Content = afterMergeContent
		};
	}
}