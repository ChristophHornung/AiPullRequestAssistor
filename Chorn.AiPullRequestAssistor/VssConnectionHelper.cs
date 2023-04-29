using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace Chorn.AiPullRequestAssistor;

internal class VssConnectionHelper
{
	internal static VssConnection GetConnection(string organizationName)
	{
#if NET48
		VssClientCredentials credentials = new();

		Uri devopsUrl = new Uri($"https://dev.azure.com/{organizationName}");
		VssConnection connection = new VssConnection(devopsUrl, credentials);
		return connection;
#else
		throw new InvalidOperationException("This method is only available in .NET 4.8.");
#endif
	}

	internal static (VssConnection connection, string projectName, string repositoryName, int prId) GetConnectionDevops()
	{
		string accessToken = GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");
		string repositoryName = GetEnvironmentVariable("BUILD_REPOSITORY_NAME");
		string projectName = GetEnvironmentVariable("SYSTEM_TEAMPROJECT");
		string collectionUri = GetEnvironmentVariable("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI");
		int prId = Int32.Parse((string)GetEnvironmentVariable("SYSTEM_PULLREQUEST_PULLREQUESTID"));

		Console.WriteLine(repositoryName);
		Console.WriteLine(projectName);
		Console.WriteLine(collectionUri);
		Console.WriteLine(prId);

		VssClientCredentials credentials = new VssBasicCredential(string.Empty, accessToken);

		Uri devopsUrl = new Uri(collectionUri);
		VssConnection connection = new VssConnection(devopsUrl, credentials);
		return (connection, projectName, repositoryName, prId);
	}

	private static string GetEnvironmentVariable(string variable)
	{
		string? value = Environment.GetEnvironmentVariable(variable);

		if (string.IsNullOrEmpty(value))
		{
			throw new InvalidOperationException(
				$"{variable} not found. Make sure to run this program in an Azure DevOps pipeline and that the env: SYSTEM_ACCESSTOKEN is set correctly.");
		}

		return value;
	}
}