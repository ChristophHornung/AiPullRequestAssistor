namespace Chorn.AiPullRequestAssistor;

internal enum FileRequestStrategy
{
	FillContextWithFiles = 0,
	SingleRequestForFile = 1,
	SingleRequestForFileDiffOnly = 2,
}