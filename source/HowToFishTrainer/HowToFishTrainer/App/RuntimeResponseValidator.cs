using System;
using System.IO;
using System.Text.Json;

namespace HowToFishTrainer.App;

public static class RuntimeResponseValidator
{
	public static void Validate(JsonElement response, Guid expectedSession, Guid expectedCorrelation)
	{
		if (!response.TryGetProperty("protocolVersion", out var value) || value.GetInt32() != 1 || !response.TryGetProperty("session", out var value2) || !Guid.TryParse(value2.GetString(), out var result) || result != expectedSession || !response.TryGetProperty("correlationId", out var value3) || !Guid.TryParse(value3.GetString(), out var result2) || result2 != expectedCorrelation)
		{
			throw new InvalidDataException("Runtime response session or correlation is invalid.");
		}
	}
}
