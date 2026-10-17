using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.14.42015")]
internal sealed class _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__ActivationCodePattern_1 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				if (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan))
				{
					runtextpos = inputSpan.Length;
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				if (num <= inputSpan.Length - 36 && num == 0)
				{
					return true;
				}
				runtextpos = inputSpan.Length;
				return false;
			}

			private bool TryMatchAtCurrentPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				int start = num;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (num != 0)
				{
					return false;
				}
				ulong num2;
				if ((uint)span.Length < 18u || !span.StartsWith("ACST-HTF-PRO-") || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[13] - 50))) & (num2 - 64)) >= 0L || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[14] - 50))) & (num2 - 64)) >= 0L || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[15] - 50))) & (num2 - 64)) >= 0L || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[16] - 50))) & (num2 - 64)) >= 0L || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[17] - 50))) & (num2 - 64)) >= 0L)
				{
					return false;
				}
				num += 18;
				span = inputSpan.Slice(num);
				for (int i = 0; i < 3; i++)
				{
					if ((uint)span.Length < 6u || span[0] != '-' || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[1] - 50))) & (num2 - 64)) >= 0L || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[2] - 50))) & (num2 - 64)) >= 0L || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[3] - 50))) & (num2 - 64)) >= 0L || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[4] - 50))) & (num2 - 64)) >= 0L || (long)((ulong)(-71494644092895232L << (int)(num2 = (uint)(span[5] - 50))) & (num2 - 64)) >= 0L)
					{
						return false;
					}
					num += 6;
					span = inputSpan.Slice(num);
				}
				if (num < inputSpan.Length - 1 || ((uint)num < (uint)inputSpan.Length && inputSpan[num] != '\n'))
				{
					return false;
				}
				runtextpos = num;
				Capture(0, start, num);
				return true;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__ActivationCodePattern_1 Instance = new _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__ActivationCodePattern_1();

	private _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__ActivationCodePattern_1()
	{
		pattern = "^ACST-HTF-PRO-[A-Z2-9]{5}(?:-[A-Z2-9]{5}){3}$";
		roptions = RegexOptions.CultureInvariant;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
