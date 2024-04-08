using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;

namespace Chorn.AiPullRequestAssistor;

internal class FileChangePromptBuilder
{
	internal static Task<List<ChangePrompt>> GetFileChangeInput(IEnumerable<ExtendedGitChange> commitChanges,
		FileRequestStrategy fileRequestStrategy)
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

				if (fileRequestStrategy == FileRequestStrategy.SingleRequestForFileDiffOnly)
				{
					int count = 0;
					foreach (IList<DiffPiece> chunk in Chunk(diff.Lines))
					{
						count++;
						diffBuilder.AppendLine($"@@ Change {count} @@");
						foreach (DiffPiece? line in chunk)
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
					}
				}
				else
				{
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

	private static IEnumerable<IList<DiffPiece>> Chunk(List<DiffPiece> diffLines)
	{
		const int preCount = 5;
		const int postCount = 5;
		List<DiffPiece> chunk = new(12);
		Queue<DiffPiece> pre = [];
		Queue<DiffPiece> post = [];
		ChunkIterationState state = ChunkIterationState.Pre;
		foreach (DiffPiece diffLine in diffLines)
		{
			if (state == ChunkIterationState.Pre)
			{
				if (diffLine.Type == ChangeType.Unchanged)
				{
					pre.Enqueue(diffLine);
					if (pre.Count > preCount)
					{
						pre.Dequeue();
					}
				}
				else
				{
					state = ChunkIterationState.Chunk;
					chunk.AddRange(pre);
					chunk.Add(diffLine);
				}
			}
			else if (state == ChunkIterationState.Chunk)
			{
				if (diffLine.Type == ChangeType.Unchanged)
				{
					post.Enqueue(diffLine);
					if (post.Count > postCount * 2)
					{
						// The chunk is finished
						chunk.AddRange(post.Take(postCount));
						yield return chunk;
						chunk.Clear();
						state = ChunkIterationState.Pre;
						pre.Clear();
						post.Skip(postCount).ForEach(pre.Enqueue);
						post.Clear();
					}
				}
				else
				{
					chunk.AddRange(post);
					post.Clear();
					chunk.Add(diffLine);
				}
			}
		}

		if (state == ChunkIterationState.Chunk)
		{
			chunk.AddRange(post.Take(postCount));
			yield return chunk;
		}
	}

	private enum ChunkIterationState
	{
		Pre,
		Chunk
	}
}