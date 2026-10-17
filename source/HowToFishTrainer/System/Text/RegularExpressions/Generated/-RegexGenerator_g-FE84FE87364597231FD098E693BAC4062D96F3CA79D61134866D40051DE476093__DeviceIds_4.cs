using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.14.42015")]
internal sealed class _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__DeviceIds_4 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				while (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan) && runtextpos != inputSpan.Length)
				{
					runtextpos++;
					if (_003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				if (num <= inputSpan.Length - 20)
				{
					int num2 = inputSpan.Slice(num).IndexOf("win-", StringComparison.OrdinalIgnoreCase);
					if (num2 >= 0)
					{
						runtextpos = num + num2;
						return true;
					}
				}
				runtextpos = inputSpan.Length;
				return false;
			}

			private bool TryMatchAtCurrentPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				int start = num;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if ((uint)span.Length < 4u || !span.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
				int num2 = span.Slice(4).IndexOfAnyExcept(_003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__Utilities.s_asciiHexDigits);
				if (num2 < 0)
				{
					num2 = span.Length - 4;
				}
				if (num2 < 16)
				{
					return false;
				}
				span = span.Slice(num2);
				num += num2;
				Capture(0, start, runtextpos = num + 4);
				return true;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__DeviceIds_4 Instance = new _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__DeviceIds_4();

	private _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__DeviceIds_4()
	{
		pattern = "win-[0-9a-f]{16,}";
		roptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
