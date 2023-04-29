using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using Microsoft.Extensions.Hosting;

namespace Chorn.AiPullRequestAssistor;

internal class Program
{
	private static async Task<int> Main(string[] args)
	{
		Parser parser = new CommandLineBuilder(AiAssistorCommands.RootCommand)
			.UseHost(
				_ => Host.CreateDefaultBuilder(),
				_ => { })
			.UseDefaults()
			.Build();

		return await parser.InvokeAsync(args);
	}
}