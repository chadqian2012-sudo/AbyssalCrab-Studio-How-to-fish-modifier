namespace HowToFishTrainer.App;

public sealed record RuntimeSessionSnapshot(bool Host, bool Client, bool Lobby, string? LobbyId, int? OwnerId, ulong SceneHandle);
