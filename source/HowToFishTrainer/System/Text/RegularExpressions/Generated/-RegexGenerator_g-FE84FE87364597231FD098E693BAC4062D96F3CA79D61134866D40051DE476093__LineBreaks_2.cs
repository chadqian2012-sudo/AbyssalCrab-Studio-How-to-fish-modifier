using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.14.42015")]
internal sealed class _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__LineBreaks_2 : Regex
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
				if ((uint)num < (uint)inputSpan.Length)
				{
					int num2 = inputSpan.Slice(num).IndexOfAny('\t', '\n', '\r');
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
				int num2 = span.IndexOfAnyExcept('\t', '\n', '\r');
				if (num2 < 0)
				{
					num2 = span.Length;
				}
				if (num2 == 0)
				{
					return false;
				}
				span = span.Slice(num2);
				Capture(0, start, runtextpos = num + num2);
				return true;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__LineBreaks_2 Instance = new _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__LineBreaks_2();

	private _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__LineBreaks_2()
	{
		pattern = "[\\r\\n\\t]+";
		roptions = RegexOptions.None;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
