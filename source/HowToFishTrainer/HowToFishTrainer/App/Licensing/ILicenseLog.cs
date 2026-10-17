namespace HowToFishTrainer.App.Licensing;

public interface ILicenseLog
{
	void Write(LicenseLogEntry entry);
}
