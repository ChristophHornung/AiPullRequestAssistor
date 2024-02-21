using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace Chorn.AiPullRequestAssistor;

internal class FileChangePromptBuilder
{
	internal static Task<List<ChangePrompt>> GetFileChangeInput(IEnumerable<ExtendedGitChange> commitChanges)
	{
		List<ChangePrompt> inputs = [];
		foreach (ExtendedGitChange commitChange in commitChanges.Where(c =>
			         !c.Item.IsFolder && c.ChangeType is VersionControlChangeType.Edit or VersionControlChangeType.Add))
		{
			string afterMergeContent = commitChange.NewContent.Content;

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
				string beforeMergeContent = commitChange.OldContent.Content;
				InlineDiffBuilder differ = new InlineDiffBuilder(new Differ());
				DiffPaneModel diff = differ.BuildDiffModel(beforeMergeContent, afterMergeContent, true);

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

		return Task.FromResult(inputs);
	}
}