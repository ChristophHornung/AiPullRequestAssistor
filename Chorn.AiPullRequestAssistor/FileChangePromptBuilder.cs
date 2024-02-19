using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace Chorn.AiPullRequestAssistor;

internal class FileChangePromptBuilder
{
	internal static async Task<List<ChangePrompt>> GetFileChangeInput(GitCommitChanges commitChanges,
		GitHttpClient gitClient,
		string projectName, string repositoryName, GitPullRequest pullRequest, CancellationToken cancellationToken)
	{
		List<ChangePrompt> inputs = [];
		foreach (GitChange commitChange in commitChanges.Changes.Where(c =>
			         !c.Item.IsFolder && c.ChangeType is VersionControlChangeType.Edit or VersionControlChangeType.Add))
		{
			using Stream afterMergeFileContents = await gitClient.GetItemTextAsync(projectName, repositoryName,
				path: commitChange.Item.Path, versionDescriptor: new GitVersionDescriptor
				{
					VersionType = GitVersionType.Commit,
					Version = pullRequest.LastMergeCommit.CommitId,
				}, cancellationToken: cancellationToken);

			using StreamReader sr = new StreamReader(afterMergeFileContents);
			string afterMergeContent = await sr.ReadToEndAsync();

			if (afterMergeContent.Contains('\0'))
			{
				// We assume the file is binary and skip it.
				continue;
			}

			if (commitChange.ChangeType == VersionControlChangeType.Add)
			{
				StringBuilder addBuilder = new();
				addBuilder.AppendLine("```");
				addBuilder.AppendLine(afterMergeContent);
				addBuilder.AppendLine("```");
				inputs.Add(new ChangePrompt(VersionControlChangeType.Add,
					commitChange.Item.Path,
					afterMergeContent
				));
			}
			else if (commitChange.ChangeType == VersionControlChangeType.Edit)
			{
				using Stream beforeMergeFileContents = await gitClient.GetItemTextAsync(projectName, repositoryName,
					path: commitChange.Item.Path, versionDescriptor: new GitVersionDescriptor
					{
						VersionType = GitVersionType.Commit,
						Version = pullRequest.LastMergeTargetCommit.CommitId,
					}, cancellationToken: cancellationToken);

				using StreamReader sr1 = new StreamReader(beforeMergeFileContents);

				InlineDiffBuilder differ = new InlineDiffBuilder(new Differ());
				DiffPaneModel diff = differ.BuildDiffModel(await sr1.ReadToEndAsync(), afterMergeContent, true);

				StringBuilder diffBuilder = new();
				diffBuilder.AppendLine("```diff");
				diffBuilder.AppendLine("--- " + commitChange.Item.Path);
				diffBuilder.AppendLine("+++ " + commitChange.Item.Path);

				foreach (DiffPiece? line in diff.Lines)
				{
					switch (line.Type)
					{
						case ChangeType.Inserted:
							diffBuilder.AppendLine("+" + line.Text);
							break;
						case ChangeType.Deleted:
							diffBuilder.AppendLine("-" + line.Text);
							break;
						default:
							diffBuilder.AppendLine(" " + line.Text);
							break;
					}
				}

				diffBuilder.AppendLine("```");

				inputs.Add(new ChangePrompt(
					VersionControlChangeType.Edit,
					commitChange.Item.Path,
					diffBuilder.ToString()
				));
			}
		}

		return inputs;
	}
}