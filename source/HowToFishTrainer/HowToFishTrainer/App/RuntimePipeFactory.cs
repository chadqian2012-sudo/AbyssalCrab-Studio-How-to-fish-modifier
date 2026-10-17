using System.IO.Pipes;

namespace HowToFishTrainer.App;

public static class RuntimePipeFactory
{
	public static NamedPipeClientStream CreateClient(string pipeName)
	{
		return new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
	}
}
